using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScreenRecorderApp.Services
{
    public sealed class ProcessResult
    {
        public int Code { get; set; }
        public string Out { get; set; } = "";
        public string Err { get; set; } = "";
    }

    internal static class ProcessUtil
    {
        /// <summary>Quote 1 doi so theo quy tac CommandLineToArgvW (ffmpeg dung cung quy tac).</summary>
        public static string Quote(string arg)
        {
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return arg;
            var sb = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char c in arg)
            {
                if (c == '\\') { backslashes++; continue; }
                if (c == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1).Append('"');
                    backslashes = 0;
                    continue;
                }
                sb.Append('\\', backslashes).Append(c);
                backslashes = 0;
            }
            sb.Append('\\', backslashes * 2).Append('"');
            return sb.ToString();
        }

        public static string Join(IEnumerable<string> args) => string.Join(" ", args.Select(Quote));

        public static async Task<ProcessResult> RunAsync(string exe, IEnumerable<string> args, int timeoutMs)
        {
            var psi = new ProcessStartInfo(exe, Join(args))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            using (var p = new Process { StartInfo = psi, EnableRaisingEvents = true })
            {
                var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                p.Exited += (s, e) => exited.TrySetResult(true);
                p.Start();
                try { p.StandardInput.Close(); } catch { }
                var outTask = p.StandardOutput.ReadToEndAsync();
                var errTask = p.StandardError.ReadToEndAsync();
                var done = await Task.WhenAny(exited.Task, Task.Delay(timeoutMs)).ConfigureAwait(false);
                if (done != exited.Task)
                {
                    try { p.Kill(); } catch { }
                    return new ProcessResult { Code = -999, Err = "timeout" };
                }
                p.WaitForExit();
                return new ProcessResult { Code = p.ExitCode, Out = await outTask.ConfigureAwait(false), Err = await errTask.ConfigureAwait(false) };
            }
        }
    }

    internal static class FfmpegLocator
    {
        /// <summary>Tim ffmpeg.exe: duong dan da cau hinh -> canh exe -> cac thu muc cha -> PATH.</summary>
        public static string? Find(string? configured)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
                    return Path.GetFullPath(configured);
            }
            catch { }

            string? dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 6 && !string.IsNullOrEmpty(dir); i++)
            {
                foreach (var candidate in new[] { Path.Combine(dir, "ffmpeg.exe"), Path.Combine(dir, "ffmpeg", "bin", "ffmpeg.exe") })
                    if (File.Exists(candidate)) return candidate;
                dir = Path.GetDirectoryName(dir!.TrimEnd('\\', '/'));
            }

            foreach (var part in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                try
                {
                    var p = part.Trim().Trim('"');
                    if (p.Length == 0) continue;
                    var candidate = Path.Combine(p, "ffmpeg.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }
            return null;
        }
    }
}
