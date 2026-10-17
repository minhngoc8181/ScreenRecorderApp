using System;
using System.Collections.Generic;
using System.Linq;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ScreenRecorderApp.Services
{
    public sealed class AudioDeviceItem
    {
        public AudioDeviceItem(string id, string name, bool isDefault)
        {
            Id = id; Name = name; IsDefault = isDefault;
        }

        public string Id { get; }
        public string Name { get; }
        public bool IsDefault { get; }

        public override string ToString() => IsDefault ? $"{Name}   (mặc định)" : Name;
    }

    internal static class AudioDeviceService
    {
        public static List<AudioDeviceItem> GetDevices(DataFlow flow)
        {
            var list = new List<AudioDeviceItem>();
            using (var en = new MMDeviceEnumerator())
            {
                string? defaultId = null;
                try
                {
                    if (en.HasDefaultAudioEndpoint(flow, Role.Multimedia))
                        defaultId = en.GetDefaultAudioEndpoint(flow, Role.Multimedia).ID;
                }
                catch { }

                foreach (var d in en.EnumerateAudioEndPoints(flow, DeviceState.Active))
                {
                    try { list.Add(new AudioDeviceItem(d.ID, d.FriendlyName, d.ID == defaultId)); }
                    catch { }
                    finally { d.Dispose(); }
                }
            }
            return list.OrderBy(d => d.IsDefault ? 0 : 1).ThenBy(d => d.Name).ToList();
        }

        public static MMDevice? GetDevice(string? id, DataFlow flow)
        {
            var en = new MMDeviceEnumerator();
            try
            {
                if (!string.IsNullOrEmpty(id)) return en.GetDevice(id);
                return en.GetDefaultAudioEndpoint(flow, Role.Multimedia);
            }
            catch { return null; }
        }
    }

    /// <summary>
    /// WASAPI loopback voi bo dem nho (do tre thap hon WasapiLoopbackCapture mac dinh 100 ms).
    /// </summary>
    internal sealed class LowLatencyLoopbackCapture : WasapiCapture
    {
        public LowLatencyLoopbackCapture(MMDevice device) : base(device, false, 40) { }

        protected override AudioClientStreamFlags GetAudioClientStreamFlags() => AudioClientStreamFlags.Loopback;
    }

    /// <summary>
    /// Mot nguon am thanh (microphone hoac loopback cua loa) da duoc chuyen ve 48 kHz / stereo / float.
    /// Chay lien tuc de hien thi muc am; khi ghi hinh thi du lieu duoc day vao pipe cho ffmpeg.
    /// </summary>
    public sealed class AudioSource : IDisposable
    {
        public const int SampleRate = 48000;
        public const int Channels = 2;
        private static readonly Guid FloatSubType = new Guid("00000003-0000-0010-8000-00aa00389b71");

        private WasapiCapture? _capture;
        private WaveFormat? _inFormat;
        private bool _isFloat;
        private int _bytesPerSample;
        private BufferedWaveProvider? _resampleQueue;
        private ISampleProvider? _resampler;
        private float[] _stereo = new float[0];
        private byte[] _queueBytes = new byte[0];
        private float[] _resampled = new float[0];
        private float _peak;
        private readonly object _peakLock = new object();
        private volatile float _gain = 1f;
        private volatile bool _muted;
        private volatile bool _stopped;
        private volatile bool _disposed;
        private bool _errorReported;

        public AudioSource(string deviceId, string name, bool loopback)
        {
            DeviceId = deviceId; Name = name; IsLoopback = loopback;
        }

        public string DeviceId { get; }
        public string Name { get; }
        public bool IsLoopback { get; }
        public string FormatDescription { get; private set; } = "";
        public bool IsRunning => _capture != null && !_stopped && !_disposed;

        public float Gain { get => _gain; set => _gain = value; }
        public bool Muted { get => _muted; set => _muted = value; }

        /// <summary>Goi tren thread capture: mau float 48k stereo xen ke (da nhan gain).</summary>
        public event Action<float[], int>? SamplesAvailable;
        public event Action<string>? Error;

        public void Start()
        {
            var device = AudioDeviceService.GetDevice(DeviceId, IsLoopback ? DataFlow.Render : DataFlow.Capture)
                         ?? throw new InvalidOperationException("Không tìm thấy thiết bị âm thanh.");
            if (IsLoopback)
            {
                StartCapture(new LowLatencyLoopbackCapture(device));
                return;
            }
            try
            {
                StartCapture(new WasapiCapture(device, true, 50));
            }
            catch
            {
                // Mot so driver khong ho tro event-sync -> dung che do polling
                var again = AudioDeviceService.GetDevice(DeviceId, DataFlow.Capture)
                            ?? throw new InvalidOperationException("Không tìm thấy thiết bị âm thanh.");
                StartCapture(new WasapiCapture(again, false, 100));
            }
        }

        private void StartCapture(WasapiCapture capture)
        {
            try
            {
                SetupFormat(capture.WaveFormat);
                capture.DataAvailable += OnDataAvailable;
                capture.RecordingStopped += OnRecordingStopped;
                capture.StartRecording();
                _capture = capture;
            }
            catch
            {
                capture.DataAvailable -= OnDataAvailable;
                capture.RecordingStopped -= OnRecordingStopped;
                try { capture.Dispose(); } catch { }
                throw;
            }
        }

        private void SetupFormat(WaveFormat f)
        {
            _inFormat = f;
            _bytesPerSample = f.BitsPerSample / 8;
            _isFloat = f.Encoding == WaveFormatEncoding.IeeeFloat
                       || (f.Encoding == WaveFormatEncoding.Extensible &&
                           (f is WaveFormatExtensible ext ? ext.SubFormat == FloatSubType : f.BitsPerSample == 32));
            _resampleQueue = null;
            _resampler = null;
            if (f.SampleRate != SampleRate)
            {
                _resampleQueue = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(f.SampleRate, Channels))
                {
                    ReadFully = false,
                    BufferDuration = TimeSpan.FromSeconds(2),
                    DiscardOnBufferOverflow = true,
                };
                _resampler = new WdlResamplingSampleProvider(_resampleQueue.ToSampleProvider(), SampleRate);
            }
            FormatDescription = $"{f.SampleRate / 1000.0:0.#} kHz • {f.Channels} kênh • {f.BitsPerSample}-bit {(_isFloat ? "float" : "PCM")}";
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (_disposed || e.BytesRecorded <= 0 || _inFormat == null) return;
            try
            {
                int frames = e.BytesRecorded / _inFormat.BlockAlign;
                if (frames <= 0) return;
                Ensure(ref _stereo, frames * 2);
                ConvertToStereo(e.Buffer, frames, _stereo);

                float[] output;
                int count;
                if (_resampler != null && _resampleQueue != null)
                {
                    int bytes = frames * 8;
                    if (_queueBytes.Length < bytes) _queueBytes = new byte[bytes];
                    Buffer.BlockCopy(_stereo, 0, _queueBytes, 0, bytes);
                    _resampleQueue.AddSamples(_queueBytes, 0, bytes);

                    int expected = (int)((long)frames * SampleRate / _inFormat.SampleRate) * 2 + 1024;
                    Ensure(ref _resampled, expected);
                    count = 0;
                    while (true)
                    {
                        int space = _resampled.Length - count;
                        if (space < 512)
                        {
                            Array.Resize(ref _resampled, _resampled.Length * 2);
                            space = _resampled.Length - count;
                        }
                        int n = _resampler.Read(_resampled, count, space - (space % 2));
                        if (n <= 0) break;
                        count += n;
                    }
                    output = _resampled;
                }
                else
                {
                    output = _stereo;
                    count = frames * 2;
                }
                if (count <= 0) return;

                float gain = _muted ? 0f : _gain;
                float peak = 0f;
                for (int i = 0; i < count; i++)
                {
                    float v = output[i] * gain;
                    output[i] = v;
                    float a = v < 0 ? -v : v;
                    if (a > peak) peak = a;
                }
                lock (_peakLock) { if (peak > _peak) _peak = peak; }

                SamplesAvailable?.Invoke(output, count);
            }
            catch (Exception ex)
            {
                if (!_errorReported)
                {
                    _errorReported = true;
                    Error?.Invoke($"Lỗi xử lý âm thanh '{Name}': {ex.Message}");
                }
            }
        }

        private void ConvertToStereo(byte[] b, int frames, float[] dst)
        {
            int ch = _inFormat!.Channels, block = _inFormat.BlockAlign, bps = _bytesPerSample;
            for (int f = 0; f < frames; f++)
            {
                int o = f * block;
                float l, r;
                if (ch == 1)
                {
                    l = r = Sample(b, o);
                }
                else
                {
                    l = Sample(b, o);
                    r = Sample(b, o + bps);
                    if (ch > 2)
                    {
                        // Downmix 5.1 / 7.1 -> stereo: center vao 2 ben, bo LFE, surround chia trai/phai
                        float c = Sample(b, o + 2 * bps) * 0.7f;
                        l += c; r += c;
                        for (int k = 4; k < ch; k++)
                        {
                            float x = Sample(b, o + k * bps) * 0.5f;
                            if ((k & 1) == 0) l += x; else r += x;
                        }
                    }
                }
                dst[2 * f] = l;
                dst[2 * f + 1] = r;
            }
        }

        private float Sample(byte[] b, int i)
        {
            if (_isFloat)
                return _bytesPerSample == 8 ? (float)BitConverter.ToDouble(b, i) : BitConverter.ToSingle(b, i);
            switch (_bytesPerSample)
            {
                case 1: return (b[i] - 128) / 128f;
                case 2: return (short)(b[i] | (b[i + 1] << 8)) / 32768f;
                case 3: return ((b[i] << 8) | (b[i + 1] << 16) | (b[i + 2] << 24)) / 2147483648f;
                case 4: return BitConverter.ToInt32(b, i) / 2147483648f;
                default: return 0f;
            }
        }

        private static void Ensure(ref float[] arr, int size)
        {
            if (arr.Length < size) arr = new float[size];
        }

        /// <summary>Lay muc dinh (0..1+) ke tu lan goi truoc.</summary>
        public float ReadPeak()
        {
            lock (_peakLock)
            {
                float p = _peak;
                _peak = 0f;
                return p;
            }
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            _stopped = true;
            if (!_disposed && e.Exception != null)
                Error?.Invoke($"Thiết bị '{Name}' đã dừng: {e.Exception.Message}");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            var cap = _capture;
            _capture = null;
            if (cap == null) return;
            cap.DataAvailable -= OnDataAvailable;
            try { cap.StopRecording(); } catch { }
            try { cap.Dispose(); } catch { }
        }
    }
}
