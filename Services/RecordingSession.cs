using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace ScreenRecorderApp.Services
{
    public sealed class FfmpegProgress
    {
        public long Frame { get; set; }
        public double Fps { get; set; }
        public long TotalSize { get; set; }
        public TimeSpan OutTime { get; set; }
        public string Speed { get; set; } = "";
        public string Bitrate { get; set; } = "";
        public long Dropped { get; set; }
        public long Duplicated { get; set; }

        public FfmpegProgress Clone() => (FfmpegProgress)MemberwiseClone();
    }

    /// <summary>
    /// Mot phien ghi: tao pipe am thanh, chay ffmpeg an (khong cua so), doc tien do, dung an toan bang phim 'q'.
    /// </summary>
    public sealed class RecordingSession
    {
        private readonly List<PipeAudioWriter> _writers = new List<PipeAudioWriter>();
        private readonly List<Action> _unsubscribe = new List<Action>();
        private readonly LinkedList<string> _lastErrors = new LinkedList<string>();
        private readonly object _cleanupLock = new object();
        private Process? _process;
        private bool _cleaned;

        public RecordingSession(RecordingOptions options, string finalFile)
        {
            Options = options;
            FinalFile = finalFile;
        }

        public RecordingOptions Options { get; }
        public string FinalFile { get; }
        public string CommandLine { get; private set; } = "";
        public bool StopRequested { get; private set; }
        public bool HasAudio => _writers.Count > 0;

        public event Action<string>? Log;
        public event Action<FfmpegProgress>? Progress;
        /// <summary>ffmpeg da thoat (ma thoat, cac dong loi cuoi).</summary>
        public event Action<int, string>? Exited;

        public void AddAudio(AudioSource source, bool isMic)
        {
            var buffer = new AudioJitterBuffer();
            Action<float[], int> handler = buffer.Write;
            source.SamplesAvailable += handler;
            _unsubscribe.Add(() => source.SamplesAvailable -= handler);

            string name = $"screenrec_{Process.GetCurrentProcess().Id}_{(isMic ? "mic" : "sys")}_{Guid.NewGuid():N}";
            var writer = new PipeAudioWriter(name, buffer);
            _writers.Add(writer);
            if (isMic) Options.MicPipePath = writer.PipePath;
            else Options.SystemPipePath = writer.PipePath;
        }

        public void Start()
        {
            foreach (var w in _writers) w.Start();

            CommandLine = ProcessUtil.Join(FfmpegArgs.Build(Options));
            var psi = new ProcessStartInfo(Options.FfmpegPath, CommandLine)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = Path.GetDirectoryName(Options.RecordFile) ?? "",
            };

            var p = new Process { StartInfo = psi };
            try
            {
                p.Start();
            }
            catch
            {
                Cleanup();
                throw;
            }
            _process = p;
            Native.AssignToKillOnCloseJob(p);
            try { p.PriorityClass = ProcessPriorityClass.AboveNormal; } catch { }

            var outTask = Task.Run(() => ReadProgress(p.StandardOutput));
            var errTask = Task.Run(() => ReadErrors(p.StandardError));
            Task.Run(async () =>
            {
                try { await Task.WhenAll(outTask, errTask).ConfigureAwait(false); } catch { }
                int code;
                try { p.WaitForExit(); code = p.ExitCode; } catch { code = -1; }
                Cleanup();
                string errors;
                lock (_lastErrors) errors = string.Join(Environment.NewLine, _lastErrors);
                Exited?.Invoke(code, errors);
            });
        }

        /// <summary>Gui 'q' cho ffmpeg de ket thuc file dung chuan; qua thoi gian thi buoc dung.</summary>
        public async Task StopAsync(int timeoutMs = 20000)
        {
            var p = _process;
            if (p == null) return;
            StopRequested = true;
            try
            {
                if (!p.HasExited)
                {
                    p.StandardInput.Write('q');
                    p.StandardInput.Flush();
                }
            }
            catch { }

            bool exited = await Task.Run(() =>
            {
                try { return p.WaitForExit(timeoutMs); } catch { return true; }
            }).ConfigureAwait(false);

            if (!exited)
            {
                Log?.Invoke("FFmpeg không phản hồi — buộc dừng.");
                try { p.Kill(); } catch { }
            }
        }

        private void ReadProgress(StreamReader reader)
        {
            var p = new FfmpegProgress();
            try
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();
                    switch (key)
                    {
                        case "frame": if (long.TryParse(val, out var f)) p.Frame = f; break;
                        case "fps": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var fps)) p.Fps = fps; break;
                        case "total_size": if (long.TryParse(val, out var size)) p.TotalSize = size; break;
                        case "out_time_us":
                        case "out_time_ms": // ffmpeg ghi micro giay cho ca 2 khoa
                            if (long.TryParse(val, out var us) && us >= 0) p.OutTime = TimeSpan.FromTicks(us * 10); break;
                        case "speed": p.Speed = val; break;
                        case "bitrate": p.Bitrate = val; break;
                        case "drop_frames": if (long.TryParse(val, out var d)) p.Dropped = d; break;
                        case "dup_frames": if (long.TryParse(val, out var du)) p.Duplicated = du; break;
                        case "progress": Progress?.Invoke(p.Clone()); break;
                    }
                }
            }
            catch { }
        }

        private void ReadErrors(StreamReader reader)
        {
            try
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    lock (_lastErrors)
                    {
                        _lastErrors.AddLast(line);
                        while (_lastErrors.Count > 25) _lastErrors.RemoveFirst();
                    }
                    Log?.Invoke("[ffmpeg] " + line);
                }
            }
            catch { }
        }

        private void Cleanup()
        {
            lock (_cleanupLock)
            {
                if (_cleaned) return;
                _cleaned = true;
            }
            foreach (var u in _unsubscribe) { try { u(); } catch { } }
            foreach (var w in _writers) { try { w.Dispose(); } catch { } }
        }

        /// <summary>Chuyen MKV -> MP4 (copy stream, khong ma hoa lai, rat nhanh).</summary>
        public static async Task<bool> RemuxToMp4Async(string ffmpeg, string source, string target, Action<string> log)
        {
            var r = await ProcessUtil.RunAsync(ffmpeg, new[]
            {
                "-hide_banner", "-nostdin", "-loglevel", "error", "-y",
                "-i", source, "-map", "0", "-c", "copy", "-movflags", "+faststart", target,
            }, 60 * 60 * 1000).ConfigureAwait(false);

            if (r.Code == 0 && File.Exists(target) && new FileInfo(target).Length > 0) return true;
            log("Chuyển sang MP4 thất bại, giữ file MKV. " + r.Err.Trim());
            try { if (File.Exists(target)) File.Delete(target); } catch { }
            return false;
        }
    }
}
