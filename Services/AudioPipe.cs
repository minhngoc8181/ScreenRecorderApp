using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using NAudio.Wave;

namespace ScreenRecorderApp.Services
{
    /// <summary>
    /// Bo dem chong giat: du lieu tu WASAPI den theo tung goi khong deu, ben doc lay theo dong ho.
    /// Thieu du lieu (vd loa im lang) -> tra ve im lang; du qua nhieu -> bo bot de giu do tre on dinh.
    /// </summary>
    public sealed class AudioJitterBuffer
    {
        private readonly float[] _buf;
        private readonly int _target;
        private readonly int _max;
        private readonly object _lock = new object();
        private int _read;
        private int _count;
        private bool _primed;

        public AudioJitterBuffer(int targetMs = 50, int maxMs = 300, int capacityMs = 3000)
        {
            int perMs = AudioSource.SampleRate * AudioSource.Channels / 1000;
            _buf = new float[perMs * capacityMs];
            _target = perMs * targetMs;
            _max = perMs * maxMs;
        }

        public long Underruns { get; private set; }

        public void Write(float[] data, int count)
        {
            lock (_lock)
            {
                int offset = 0;
                if (count > _buf.Length) { offset = count - _buf.Length; count = _buf.Length; }
                int free = _buf.Length - _count;
                if (count > free) Skip(count - free);
                int write = (_read + _count) % _buf.Length;
                int first = Math.Min(count, _buf.Length - write);
                Array.Copy(data, offset, _buf, write, first);
                if (count > first) Array.Copy(data, offset + first, _buf, 0, count - first);
                _count += count;
            }
        }

        public void Read(float[] dest, int count)
        {
            lock (_lock)
            {
                if (!_primed)
                {
                    if (_count >= _target) _primed = true;
                    else { Array.Clear(dest, 0, count); return; }
                }
                if (_count > _max) Skip(_count - _target);

                int n = Math.Min(count, _count);
                n -= n % 2;
                int first = Math.Min(n, _buf.Length - _read);
                Array.Copy(_buf, _read, dest, 0, first);
                if (n > first) Array.Copy(_buf, 0, dest, first, n - first);
                _read = (_read + n) % _buf.Length;
                _count -= n;
                if (n < count)
                {
                    Array.Clear(dest, n, count - n);
                    _primed = false;
                    Underruns++;
                }
            }
        }

        public void Reset()
        {
            lock (_lock) { _read = 0; _count = 0; _primed = false; }
        }

        private void Skip(int n)
        {
            n = Math.Min(n, _count);
            n -= n % 2;
            _read = (_read + n) % _buf.Length;
            _count -= n;
        }
    }

    /// <summary>
    /// Named pipe server: ffmpeg doc am thanh raw f32le 48k stereo tu \\.\pipe\ten.
    /// Ghi theo dong ho thuc (Stopwatch) nen am thanh luon khop thoi gian voi video, ke ca khi loa im lang.
    /// </summary>
    public sealed class PipeAudioWriter : IDisposable
    {
        private readonly string _name;
        private readonly AudioJitterBuffer _buffer;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private NamedPipeServerStream? _pipe;
        private Thread? _thread;

        public PipeAudioWriter(string name, AudioJitterBuffer buffer)
        {
            _name = name;
            _buffer = buffer;
        }

        public string PipePath => @"\\.\pipe\" + _name;
        public bool Connected { get; private set; }

        public void Start()
        {
            _pipe = new NamedPipeServerStream(_name, PipeDirection.Out, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous, 0, 1 << 20);
            _thread = new Thread(Run) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "pipe-" + _name };
            _thread.Start();
        }

        private void Run()
        {
            var pipe = _pipe!;
            try
            {
                pipe.WaitForConnectionAsync(_cts.Token).Wait(_cts.Token);
            }
            catch { return; }

            Connected = true;
            _buffer.Reset();
            const int rate = AudioSource.SampleRate;
            var floats = new float[rate];          // toi da 0.5 s / lan ghi
            var bytes = new byte[floats.Length * 4];
            var clock = Stopwatch.StartNew();
            long written = 0;
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    long due = (long)(clock.Elapsed.TotalSeconds * rate) - written;
                    if (due < 240) { Thread.Sleep(3); continue; }   // ~5 ms
                    int frames = (int)Math.Min(due, rate / 2);
                    int samples = frames * AudioSource.Channels;
                    _buffer.Read(floats, samples);
                    Buffer.BlockCopy(floats, 0, bytes, 0, samples * 4);
                    pipe.Write(bytes, 0, samples * 4);
                    written += frames;
                }
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        public void Dispose()
        {
            try { _cts.Cancel(); } catch { }
            try { _pipe?.Dispose(); } catch { }
            try { _thread?.Join(1500); } catch { }
        }
    }

    /// <summary>Phat lai mot mang float 48k stereo (dung cho thu thu mic / am bao thu loa).</summary>
    public sealed class BufferSampleProvider : ISampleProvider
    {
        private readonly float[] _data;
        private int _pos;

        public BufferSampleProvider(float[] data) { _data = data; }

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(AudioSource.SampleRate, AudioSource.Channels);

        /// <summary>Do dai am thanh (dung de dat timeout an toan khi phat).</summary>
        public TimeSpan Duration => TimeSpan.FromSeconds(_data.Length / (double)(AudioSource.SampleRate * AudioSource.Channels));

        /// <summary>So mau da duoc bo phat doc (de phat hien driver khong keo du lieu).</summary>
        public int Position => Volatile.Read(ref _pos);
        public int Length => _data.Length;

        /// <summary>Tua ve dau de phat lai bang backend khac.</summary>
        public void Rewind() => Volatile.Write(ref _pos, 0);

        public int Read(float[] buffer, int offset, int count)
        {
            int n = Math.Min(count, _data.Length - _pos);
            if (n <= 0) return 0;
            // KHONG dung Array.Copy: NAudio (WaveBuffer) truyen vao mang byte[] "doi lot" float[],
            // Array.Copy kiem tra kieu that -> ArrayTypeMismatchException -> trinh phat dung ngay, khong ra tieng.
            for (int i = 0; i < n; i++) buffer[offset + i] = _data[_pos + i];
            _pos += n;
            return n;
        }

        /// <summary>Tieng "ting" 4 not (Do-Mi-Sol-Do) de kiem tra loa.</summary>
        public static float[] CreateChime()
        {
            const int rate = AudioSource.SampleRate;
            double[] notes = { 523.25, 659.25, 783.99, 1046.5 };
            double step = 0.16, decay = 0.9;
            int total = (int)((step * notes.Length + decay) * rate);
            var data = new float[total * 2];
            for (int i = 0; i < total; i++)
            {
                double t = i / (double)rate, v = 0;
                for (int n = 0; n < notes.Length; n++)
                {
                    double tn = t - n * step;
                    if (tn < 0) continue;
                    double env = Math.Min(1.0, tn / 0.004) * Math.Exp(-tn * 5.5);
                    v += 0.2 * env * (Math.Sin(2 * Math.PI * notes[n] * tn) + 0.25 * Math.Sin(4 * Math.PI * notes[n] * tn));
                }
                data[2 * i] = data[2 * i + 1] = (float)v;
            }
            return data;
        }
    }
}
