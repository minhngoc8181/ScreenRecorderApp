using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ScreenRecorderApp.Services
{
    /// <summary>
    /// Phat am thu (chuong "ting" / mau micro) ra mot thiet bi loa cu the.
    /// WasapiOut o che do event tren mot so driver khong bao gio day du lieu (im lang, khong ket thuc),
    /// nen co nhieu cach phat va tu chuyen sang cach khac neu cach truoc that bai.
    /// </summary>
    internal static class TestPlayer
    {
        public enum Backend { DirectSound, WasapiPoll, WasapiEvent }

        public static readonly Backend[] Order = { Backend.WasapiPoll, Backend.DirectSound, Backend.WasapiEvent };

        public static string Describe(Backend b) => b switch
        {
            Backend.DirectSound => "DirectSound",
            Backend.WasapiPoll => "WASAPI (polling)",
            _ => "WASAPI (event)",
        };

        /// <summary>Tao bo phat cho thiet bi (deviceId rong = mac dinh).</summary>
        public static IWavePlayer Create(string? deviceId, Backend backend)
        {
            switch (backend)
            {
                case Backend.DirectSound:
                {
                    Guid guid = DirectSoundOut.DSDEVID_DefaultPlayback;
                    if (!string.IsNullOrEmpty(deviceId))
                    {
                        using (var dev = AudioDeviceService.GetDevice(deviceId, DataFlow.Render))
                        {
                            // GUID DirectSound cua endpoint = PKEY_AudioEndpoint_GUID
                            var v = dev?.Properties[PropertyKeys.PKEY_AudioEndpoint_GUID]?.Value as string;
                            if (!string.IsNullOrEmpty(v) && Guid.TryParse(v, out var g)) guid = g;
                        }
                    }
                    return new DirectSoundOut(guid, 120);
                }
                case Backend.WasapiPoll:
                {
                    var dev = AudioDeviceService.GetDevice(deviceId, DataFlow.Render)
                              ?? throw new InvalidOperationException("Không có thiết bị phát âm thanh.");
                    return new WasapiOut(dev, AudioClientShareMode.Shared, false, 150);
                }
                default:
                {
                    var dev = AudioDeviceService.GetDevice(deviceId, DataFlow.Render)
                              ?? throw new InvalidOperationException("Không có thiết bị phát âm thanh.");
                    return new WasapiOut(dev, AudioClientShareMode.Shared, true, 100);
                }
            }
        }

        /// <summary>
        /// Phat het provider bang 1 backend. Tra ve true neu phat xong binh thuong
        /// (vi tri phat tien len va ket thuc dung han). Luon dung + giai phong player.
        /// </summary>
        public static async Task<bool> PlayOnceAsync(string? deviceId, Backend backend, BufferSampleProvider provider,
                                                     CancellationToken ct, Action<IWavePlayer>? started = null)
        {
            IWavePlayer? player = null;
            var stopped = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                player = Create(deviceId, backend);
                player.PlaybackStopped += (s, e) => stopped.TrySetResult(e.Exception);
                player.Init(provider);
                player.Play();
                started?.Invoke(player);

                var sw = Stopwatch.StartNew();
                var limit = provider.Duration + TimeSpan.FromSeconds(1.5);
                using (ct.Register(() => stopped.TrySetResult(null)))
                    await Task.WhenAny(stopped.Task, Task.Delay(limit)).ConfigureAwait(true);

                bool endedByItself = stopped.Task.IsCompleted && !ct.IsCancellationRequested && stopped.Task.Result == null;
                if (stopped.Task.IsCompleted && stopped.Task.Result != null)
                    throw new InvalidOperationException(stopped.Task.Result.Message, stopped.Task.Result);
                // Driver "im lang": provider khong duoc doc het
                bool consumed = provider.Position >= provider.Length;
                return consumed && (endedByItself || sw.Elapsed >= provider.Duration);
            }
            finally
            {
                if (player != null)
                {
                    try { if (player.PlaybackState != PlaybackState.Stopped) player.Stop(); } catch { }
                    await Task.WhenAny(stopped.Task, Task.Delay(400)).ConfigureAwait(true);
                    try { player.Dispose(); } catch { }
                }
            }
        }
    }
}
