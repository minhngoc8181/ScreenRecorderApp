using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ScreenRecorderApp.Services
{
    /// <summary>
    /// Che do tu kiem tra khong giao dien: ScreenRecorder.exe --selftest [giay]
    /// Ghi thu man hinh chinh + loa + micro, phat am "ting" trong luc ghi, roi phan tich file ket qua.
    /// Bao cao ghi vao ScreenRecorder-selftest.log canh file exe.
    /// </summary>
    internal static class SelfTest
    {
        /// <summary>
        /// ScreenRecorder.exe --playtest : thu phat chuong bang tung cach (DirectSound / WASAPI),
        /// do loopback cung luc de biet cach nao thuc su ra loa. Ghi ScreenRecorder-playtest.log.
        /// </summary>
        public static async Task<int> RunPlayTestAsync()
        {
            string logPath = Path.Combine(AppSettings_AppDir(), "ScreenRecorder-playtest.log");
            try { File.WriteAllText(logPath, "", Encoding.UTF8); } catch { }
            void L(string s)
            {
                try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {s}{Environment.NewLine}", Encoding.UTF8); } catch { }
            }
            int okCount = 0;
            AudioSource? loop = null;
            try
            {
                L("Windows " + Environment.OSVersion + ", CLR " + Environment.Version);
                var outs = AudioDeviceService.GetDevices(DataFlow.Render);
                foreach (var d in outs) L("Render: " + d);
                var dev = outs.FirstOrDefault(d => d.IsDefault) ?? outs.FirstOrDefault();
                if (dev == null) { L("Không có thiết bị phát."); return 2; }
                L("Thiết bị thử: " + dev.Name);

                float peak = 0;
                var gate = new object();
                try
                {
                    loop = new AudioSource(dev.Id, dev.Name, true);
                    loop.SamplesAvailable += (buf, n) =>
                    {
                        float m = 0;
                        for (int i = 0; i < n; i++) { float a = Math.Abs(buf[i]); if (a > m) m = a; }
                        lock (gate) if (m > peak) peak = m;
                    };
                    loop.Start();
                    L("Loopback: " + loop.FormatDescription);
                }
                catch (Exception ex) { L("Loopback FAIL: " + ex.Message); loop = null; }

                await Task.Delay(500);
                lock (gate) peak = 0;
                await Task.Delay(500);
                float silence; lock (gate) silence = peak;
                L($"Mức nền loopback (không phát gì): {ToDb(silence)}");

                foreach (var b in TestPlayer.Order)
                {
                    var chime = new BufferSampleProvider(BufferSampleProvider.CreateChime());
                    lock (gate) peak = 0;
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    bool ok;
                    string err = "";
                    try { ok = await TestPlayer.PlayOnceAsync(dev.Id, b, chime, System.Threading.CancellationToken.None); }
                    catch (Exception ex) { ok = false; err = ex.GetType().Name + ": " + ex.Message; }
                    float p; lock (gate) p = peak;
                    bool heard = loop != null && p > Math.Max(0.01f, silence * 2);
                    if (ok) okCount++;
                    L($"{TestPlayer.Describe(b),-18} phát xong={ok}  đã đọc={chime.Position}/{chime.Length}  " +
                      $"thời gian={sw.Elapsed.TotalSeconds:0.00}s  loopback={ToDb(p)}  nghe thấy={heard} {err}");
                    await Task.Delay(400);
                }
            }
            catch (Exception ex) { L("LỖI: " + ex); }
            finally
            {
                loop?.Dispose();
                L("Done.");
            }
            return okCount > 0 ? 0 : 1;
        }

        private static string ToDb(float v) => v <= 0.00001f ? "-inf dB" : (20 * Math.Log10(v)).ToString("0.0") + " dB";
        public static async Task<int> RunAsync(int seconds, bool fakeVideo = false)
        {
            var log = new StringBuilder();
            string logPath = Path.Combine(AppSettings_AppDir(), "ScreenRecorder-selftest.log");
            try { File.WriteAllText(logPath, "", Encoding.UTF8); } catch { }
            void L(string s)
            {
                string line = $"[{DateTime.Now:HH:mm:ss.fff}] {s}";
                lock (log)
                {
                    log.AppendLine(line);
                    try { File.AppendAllText(logPath, line + Environment.NewLine, Encoding.UTF8); } catch { }
                }
            }
            int exit = 1;
            AudioSource? sys = null, mic = null;
            try
            {
                L("Windows " + Environment.OSVersion + (Environment.Is64BitProcess ? " x64" : " x86") + ", CLR " + Environment.Version);
                var ff = FfmpegLocator.Find(null) ?? throw new Exception("Không tìm thấy ffmpeg.exe");
                L("ffmpeg: " + ff + " | " + await EncoderProbe.VersionAsync(ff));

                var monitors = MonitorService.GetMonitors();
                foreach (var m in monitors) L($"Monitor: {m} | {m.DeviceName} | {m.Bounds} | DXGI={m.DxgiOutputIndex?.ToString() ?? "-"}");
                var mon = monitors.First();

                var encs = EncoderProbe.CreateList();
                foreach (var e in encs.Where(x => x.Key != "auto"))
                {
                    e.Available = await EncoderProbe.TestAsync(ff, e);
                    e.Probed = true;
                    L($"Encoder {e.Codec}: {e.Available}");
                }
                L("ddagrab: " + await EncoderProbe.HasFilterAsync(ff, "ddagrab"));
                var enc = encs.First(x => x.Key != "auto" && x.Available);

                var outs = AudioDeviceService.GetDevices(DataFlow.Render);
                var ins = AudioDeviceService.GetDevices(DataFlow.Capture);
                foreach (var d in outs) L("Render: " + d);
                foreach (var d in ins) L("Capture: " + d);

                var outDev = outs.FirstOrDefault(d => d.IsDefault) ?? outs.FirstOrDefault();
                var inDev = ins.FirstOrDefault(d => d.IsDefault) ?? ins.FirstOrDefault();
                if (outDev != null)
                {
                    try { sys = new AudioSource(outDev.Id, outDev.Name, true); sys.Start(); L("Loopback OK: " + sys.FormatDescription); }
                    catch (Exception ex) { L("Loopback FAIL: " + ex.Message); sys = null; }
                }
                if (inDev != null)
                {
                    try { mic = new AudioSource(inDev.Id, inDev.Name, false); mic.Start(); L("Mic OK: " + mic.FormatDescription); }
                    catch (Exception ex) { L("Mic FAIL: " + ex.Message); mic = null; }
                }

                string dir = Path.Combine(AppSettings_AppDir(), "videos");
                Directory.CreateDirectory(dir);
                string baseName = "SelfTest_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                var opts = new RecordingOptions
                {
                    FfmpegPath = ff, Area = mon.Bounds, EncoderKey = enc.Key, Codec = enc.Codec, Fps = 30,
                    RecordFile = Path.Combine(dir, baseName + ".mkv"), NoiseFilter = true, DurationSeconds = seconds, TestPattern = fakeVideo,
                    MicBoostDb = 8, VoiceLeveler = true, Ducking = true, SeparateTracks = true,
                };
                var session = new RecordingSession(opts, Path.Combine(dir, baseName + ".mp4"));
                if (mic != null) session.AddAudio(mic, true);
                if (sys != null) session.AddAudio(sys, false);
                var done = new TaskCompletionSource<int>();
                FfmpegProgress? last = null;
                session.Log += s => L(s);
                session.Progress += p => last = p;
                session.Exited += (code, err) => done.TrySetResult(code);
                session.Start();
                L("CMD: " + session.CommandLine);

                // Phat am "ting" 3 lan trong luc ghi de kiem tra loopback
                float sysPeak = 0, micPeak = 0;
                var peakTask = Task.Run(async () =>
                {
                    while (!done.Task.IsCompleted)
                    {
                        await Task.Delay(50);
                        sysPeak = Math.Max(sysPeak, sys?.ReadPeak() ?? 0);
                        micPeak = Math.Max(micPeak, mic?.ReadPeak() ?? 0);
                    }
                });
                if (outDev != null)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        await Task.Delay(1200);
                        L($"Chime {i + 1} start");
                        try
                        {
                            using (var player = new WasapiOut(AudioDeviceService.GetDevice(outDev.Id, DataFlow.Render), AudioClientShareMode.Shared, false, 100))
                            {
                                player.Init(new BufferSampleProvider(BufferSampleProvider.CreateChime()));
                                player.Play();
                                var until = DateTime.UtcNow.AddSeconds(5);
                                while (player.PlaybackState == PlaybackState.Playing && DateTime.UtcNow < until) await Task.Delay(50);
                                L($"Chime {i + 1} state={player.PlaybackState}");
                            }
                        }
                        catch (Exception ex) { L("Play chime FAIL: " + ex.Message); }
                        L($"Chime {i + 1} end");
                    }
                }

                L("Chimes done, waiting for ffmpeg…");
                var finished = await Task.WhenAny(done.Task, Task.Delay((seconds + 30) * 1000));
                if (finished != done.Task) { L("Timeout -> stop"); await session.StopAsync(); }
                int code = await done.Task;
                L("ffmpeg exited, waiting peak task");
                await peakTask;
                L($"ffmpeg exit={code} frames={last?.Frame} fps={last?.Fps} size={last?.TotalSize} speed={last?.Speed} drop={last?.Dropped} dup={last?.Duplicated}");
                L($"Peak loopback={sysPeak:0.000} mic={micPeak:0.000}");

                bool remux = await RecordingSession.RemuxToMp4Async(ff, opts.RecordFile, session.FinalFile, s => L(s));
                L("Remux MP4: " + remux);
                string result = remux ? session.FinalFile : opts.RecordFile;
                if (remux) File.Delete(opts.RecordFile);

                var probe = await ProcessUtil.RunAsync(ff, new[] { "-hide_banner", "-nostdin", "-i", result, "-map", "0:a:0?", "-af", "volumedetect", "-f", "null", "-" }, 60000);
                foreach (var line in probe.Err.Split('\n').Where(x => x.Contains("Stream #") || x.Contains("Duration") || x.Contains("_volume")))
                    L("  " + line.Trim());
                L("Kết quả: " + result + " (" + new FileInfo(result).Length + " bytes)");
                exit = code == 0 ? 0 : 2;
            }
            catch (Exception ex)
            {
                L("LỖI: " + ex);
            }
            finally
            {
                sys?.Dispose();
                mic?.Dispose();
                L("Done.");
            }
            return exit;
        }

        private static string AppSettings_AppDir() => AppDomain.CurrentDomain.BaseDirectory;
    }
}
