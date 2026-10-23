using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ScreenRecorderApp.Services
{
    public sealed class CompressResult
    {
        public bool Ok { get; set; }
        public bool Cancelled { get; set; }
        public string File { get; set; } = "";
        public long BytesBefore { get; set; }
        public long BytesAfter { get; set; }
        public string Message { get; set; } = "";
    }

    /// <summary>
    /// Nen lai video sau khi ghi (libx264, uu tien thap) de giam dung luong.
    /// Ghi ra file tam canh file goc; thanh cong va nho hon thi thay the file goc (cung ten, cung thu muc).
    /// </summary>
    internal static class VideoCompressor
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Describe(string mode) =>
            mode == "strong" ? "nén mạnh" : mode == "medium" ? "nén vừa" : "không nén";

        public static string[] BuildArgs(string input, string output, string mode)
        {
            bool strong = mode == "strong";
            bool mp4 = output.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase);
            // Dung toi da nua so nhan CPU de may van muot khi dang nen
            int threads = Math.Max(2, Environment.ProcessorCount / 2);
            var a = new System.Collections.Generic.List<string>
            {
                "-hide_banner", "-nostdin", "-loglevel", "error", "-nostats", "-progress", "pipe:1", "-y",
                "-i", input, "-map", "0", "-map_metadata", "0",
                // Giu nguyen do phan giai goc (khong scale) de chu ro khi hoc lai
                "-c:v", "libx264", "-preset", strong ? "slow" : "medium", "-crf", strong ? "28" : "26",
                "-pix_fmt", "yuv420p", "-threads", threads.ToString(Inv),
                // Khung hinh khoa: tu dong o moi lan doi slide (scene cut) + toi thieu moi 20/30 giay.
                // Khung khoa chiem phan lon dung luong bai giang -> gian ra giam ~50% ma chu khong mo di.
                "-g", "100000", "-keyint_min", "10",
                "-force_key_frames", strong ? "expr:gte(t,n_forced*30)" : "expr:gte(t,n_forced*20)",
            };
            // Tối đa 15 fps: 15 fps giữ nguyên, 30 fps bỏ đều 1/2 khung. (Ép về 10 fps từ file 15 fps sẽ bỏ khung không đều → mặt người nói bị giật.)
            if (strong) a.AddRange(new[] { "-fpsmax", "15" });
            a.AddRange(strong
                ? new[] { "-c:a", "aac", "-b:a", "64k", "-ac", "1" }
                : new[] { "-c:a", "aac", "-b:a", "96k" });
            if (mp4) a.AddRange(new[] { "-movflags", "+faststart" });
            a.Add(output);
            return a.ToArray();
        }

        public static async Task<TimeSpan> ProbeDurationAsync(string ffmpeg, string file)
        {
            var r = await ProcessUtil.RunAsync(ffmpeg, new[] { "-hide_banner", "-nostdin", "-i", file }, 20000).ConfigureAwait(false);
            var m = Regex.Match(r.Err ?? "", @"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)");
            if (!m.Success) return TimeSpan.Zero;
            return TimeSpan.FromHours(int.Parse(m.Groups[1].Value, Inv))
                 + TimeSpan.FromMinutes(int.Parse(m.Groups[2].Value, Inv))
                 + TimeSpan.FromSeconds(double.Parse(m.Groups[3].Value, Inv));
        }

        public static async Task<CompressResult> CompressAsync(string ffmpeg, string input, string mode,
            Action<double> progress, Action<string> log, CancellationToken ct)
        {
            var result = new CompressResult { File = input };
            if (!File.Exists(input)) { result.Message = "Không tìm thấy file."; return result; }
            result.BytesBefore = new FileInfo(input).Length;

            var duration = await ProbeDurationAsync(ffmpeg, input).ConfigureAwait(false);
            string dir = Path.GetDirectoryName(input) ?? "";
            string ext = Path.GetExtension(input);
            string temp = Path.Combine(dir, Path.GetFileNameWithoutExtension(input) + ".dang-nen" + ext);

            var psi = new ProcessStartInfo(ffmpeg, ProcessUtil.Join(BuildArgs(input, temp, mode)))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            log($"Bắt đầu {Describe(mode)}: {Path.GetFileName(input)}");

            var errors = new StringBuilder();
            int code;
            bool cancelled = false;
            using (var p = new Process { StartInfo = psi })
            {
                p.Start();
                Native.AssignToKillOnCloseJob(p);
                // Uu tien thap nhat: chi dung CPU khi may ranh, khong lam cham viec khac
                try { p.PriorityClass = ProcessPriorityClass.Idle; } catch { }

                var errTask = Task.Run(() =>
                {
                    string? line;
                    while ((line = p.StandardError.ReadLine()) != null)
                        if (errors.Length < 4000) errors.AppendLine(line);
                });
                var outTask = Task.Run(() =>
                {
                    string? line;
                    while ((line = p.StandardOutput.ReadLine()) != null)
                    {
                        if (!line.StartsWith("out_time_us=", StringComparison.Ordinal) || duration <= TimeSpan.Zero) continue;
                        if (long.TryParse(line.Substring(12), out var us) && us > 0)
                            progress(Math.Min(1.0, us * 10.0 / duration.Ticks)); // 1 micro giay = 10 tick
                    }
                });

                using (ct.Register(() => { cancelled = true; try { if (!p.HasExited) p.Kill(); } catch { } }))
                {
                    await Task.Run(() => p.WaitForExit()).ConfigureAwait(false);
                    try { await Task.WhenAll(outTask, errTask).ConfigureAwait(false); } catch { }
                }
                code = cancelled ? -1 : p.ExitCode;
            }

            if (cancelled || code != 0)
            {
                TryDelete(temp);
                result.Cancelled = cancelled;
                result.Message = cancelled ? "Đã hủy nén, giữ nguyên file gốc." : "Nén thất bại, giữ nguyên file gốc. " + errors.ToString().Trim();
                return result;
            }

            long after = File.Exists(temp) ? new FileInfo(temp).Length : 0;
            if (after <= 0 || after >= result.BytesBefore * 0.95)
            {
                TryDelete(temp);
                result.Ok = true;
                result.BytesAfter = result.BytesBefore;
                result.Message = "File gốc đã đủ nhỏ, giữ nguyên.";
                return result;
            }

            try
            {
                File.Delete(input);
                File.Move(temp, input);
            }
            catch (Exception ex)
            {
                // Khong thay duoc (file dang mo?) -> giu ca 2, bao cho nguoi dung
                result.Ok = true;
                result.File = temp;
                result.BytesAfter = after;
                result.Message = "Không thay được file gốc (đang mở?): " + ex.Message;
                return result;
            }

            result.Ok = true;
            result.BytesAfter = after;
            return result;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
