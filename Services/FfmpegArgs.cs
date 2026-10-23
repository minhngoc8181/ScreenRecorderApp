using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ScreenRecorderApp.Services
{
    public sealed class EncoderInfo
    {
        public EncoderInfo(string key, string codec, string name)
        {
            Key = key; Codec = codec; Name = name;
        }

        public string Key { get; }
        public string Codec { get; }
        public string Name { get; }
        public bool Available { get; set; }
        public bool Probed { get; set; }

        public override string ToString()
        {
            if (Key == "auto") return Name;
            if (!Probed) return Name + "   (đang kiểm tra…)";
            return Available ? Name + "   ✓" : Name + "   (không khả dụng)";
        }
    }

    internal static class EncoderProbe
    {
        public static List<EncoderInfo> CreateList() => new List<EncoderInfo>
        {
            new EncoderInfo("auto", "", "Tự động (chọn bộ mã hóa tốt nhất)") { Available = true, Probed = true },
            new EncoderInfo("qsv", "h264_qsv", "Intel Quick Sync — H.264"),
            new EncoderInfo("nvenc", "h264_nvenc", "NVIDIA NVENC — H.264"),
            new EncoderInfo("amf", "h264_amf", "AMD AMF — H.264"),
            new EncoderInfo("x264", "libx264", "CPU (x264) — H.264"),
        };

        public static async Task<bool> TestAsync(string ffmpeg, EncoderInfo e)
        {
            var r = await ProcessUtil.RunAsync(ffmpeg, new[]
            {
                "-hide_banner", "-nostdin", "-loglevel", "error",
                "-f", "lavfi", "-i", "color=c=black:s=320x240:r=10",
                "-frames:v", "5", "-an", "-c:v", e.Codec, "-pix_fmt", FfmpegArgs.PixFmtFor(e.Key),
                "-f", "null", "-",
            }, 20000).ConfigureAwait(false);
            return r.Code == 0;
        }

        public static async Task<bool> HasFilterAsync(string ffmpeg, string filter)
        {
            var r = await ProcessUtil.RunAsync(ffmpeg, new[] { "-hide_banner", "-nostdin", "-filters" }, 15000).ConfigureAwait(false);
            return Regex.IsMatch(r.Out, @"\s" + Regex.Escape(filter) + @"\s");
        }

        public static async Task<string> VersionAsync(string ffmpeg)
        {
            var r = await ProcessUtil.RunAsync(ffmpeg, new[] { "-hide_banner", "-nostdin", "-version" }, 15000).ConfigureAwait(false);
            var first = (r.Out ?? "").Split('\n').FirstOrDefault()?.Trim() ?? "";
            return first.Length > 0 ? first : "Không đọc được phiên bản ffmpeg";
        }

        /// <summary>
        /// Thu nhanh duong chup Desktop Duplication (ddagrab) voi dung bo ma hoa se dung, 1 giay, ghi ra NUL.
        /// Kiem tra ca kich thuoc khung hinh: neu khac kich thuoc man hinh (man hinh xoay, sai chi so DXGI) -> khong dung.
        /// </summary>
        public static async Task<(bool Ok, string Reason)> TestDdaAsync(RecordingOptions template)
        {
            var o = new RecordingOptions
            {
                FfmpegPath = template.FfmpegPath,
                Area = template.Area,
                UseDda = true,
                DdaIndex = template.DdaIndex,
                EncoderKey = template.EncoderKey,
                Codec = template.Codec,
                Fps = 5,
                Quality = template.Quality,
                ScaleHeight = 0,
                DrawMouse = template.DrawMouse,
                RecordFile = "NUL",
                DurationSeconds = 1,
                LogLevel = "info",
            };
            var r = await ProcessUtil.RunAsync(o.FfmpegPath, FfmpegArgs.Build(o), 15000).ConfigureAwait(false);
            if (r.Code != 0)
            {
                string last = (r.Err ?? "").Split('\n').Select(x => x.Trim()).LastOrDefault(x => x.Length > 0) ?? "";
                return (false, $"Desktop Duplication không chạy được ({(r.Code == -999 ? "quá thời gian" : last)})");
            }
            var m = Regex.Match(r.Err ?? "", @"Stream #0:0.*?Video:.*?\s(\d{2,5})x(\d{2,5})");
            if (m.Success)
            {
                int w = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                int h = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                if (Math.Abs(w - template.Area.Width) > 2 || Math.Abs(h - template.Area.Height) > 2)
                    return (false, $"Desktop Duplication trả về {w}×{h}, khác màn hình {template.Area.Width}×{template.Area.Height} (màn hình xoay?)");
            }
            return (true, "");
        }
    }

    public sealed class RecordingOptions
    {
        public string FfmpegPath { get; set; } = "";
        public Rectangle Area { get; set; }
        public bool UseDda { get; set; }
        public int DdaIndex { get; set; }
        public string EncoderKey { get; set; } = "x264";
        public string Codec { get; set; } = "libx264";
        public int Fps { get; set; } = 30;
        public string Quality { get; set; } = "medium";
        public int ScaleHeight { get; set; }
        public bool DrawMouse { get; set; } = true;
        public string RecordFile { get; set; } = "";
        public string? MicPipePath { get; set; }
        public string? SystemPipePath { get; set; }
        public bool NoiseFilter { get; set; }
        /// <summary>Khuech dai micro (dB) SAU bo loc on.</summary>
        public int MicBoostDb { get; set; }
        /// <summary>Bo nen giong noi (compressor) cho micro.</summary>
        public bool VoiceLeveler { get; set; }
        /// <summary>Giam am thanh he thong khi micro co tieng noi (sidechain).</summary>
        public bool Ducking { get; set; }
        public bool SeparateTracks { get; set; }
        public int AudioOffsetMs { get; set; }
        public int AudioBitrateK { get; set; } = 128;
        public int DurationSeconds { get; set; }
        /// <summary>Chi dung cho --selftest --fake-video: anh test thay cho man hinh.</summary>
        public bool TestPattern { get; set; }
        public string LogLevel { get; set; } = "warning";
    }

    internal static class FfmpegArgs
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // Bo loc tieng on cho micro (giong script record-intel-lowcpu.ps1)
        public const string MicDenoise =
            "highpass=f=80,afftdn=nr=18:nf=-45:tn=1,agate=threshold=0.015:ratio=2:range=0.08:attack=10:release=200,lowpass=f=10000";

        // Bo nen giong noi: tren -24 dBFS nen 3:1, bu +6 dB -> giong nho duoc nang len, giong to khong vo
        public const string VoiceCompressor =
            "acompressor=threshold=0.063:ratio=3:attack=8:release=250:knee=3:makeup=2";

        // Gioi han dinh (tat auto-level de dinh khong bi day nguoc len 0 dBFS)
        public const string Limiter = "alimiter=limit=0.95:level=disabled";

        // Giam am he thong ~8 dB khi co giong noi
        public const string DuckFilter =
            "sidechaincompress=threshold=0.05:ratio=6:attack=15:release=600:knee=4";

        /// <summary>Chuoi xu ly micro: loc on -> nen giong -> tang am sau loc -> gioi han dinh.</summary>
        public static string MicChain(bool noiseFilter, bool leveler, int boostDb)
        {
            var parts = new List<string>();
            if (noiseFilter) parts.Add(MicDenoise);
            if (leveler) parts.Add(VoiceCompressor);
            if (boostDb > 0) parts.Add($"volume={boostDb.ToString(Inv)}dB");
            if (leveler || boostDb > 0) parts.Add(Limiter);
            return parts.Count > 0 ? string.Join(",", parts) : "anull";
        }

        public static string PixFmtFor(string key) => key == "nvenc" || key == "x264" ? "yuv420p" : "nv12";

        private static string[] QualityArgs(string key, string quality)
        {
            int i = quality == "low" ? 0 : quality == "high" ? 2 : 1;
            switch (key)
            {
                case "qsv":
                    return new[] { "-preset", "veryfast", "-global_quality", new[] { "28", "24", "20" }[i] };
                case "nvenc":
                    return new[] { "-preset", "p4", "-rc", "vbr", "-cq", new[] { "32", "26", "21" }[i], "-b:v", "0" };
                case "amf":
                    return new[] { "-usage", "transcoding", "-quality", "speed", "-rc", "cqp",
                                   "-qp_i", new[] { "30", "24", "20" }[i], "-qp_p", new[] { "32", "26", "22" }[i] };
                default:
                    return new[] { "-preset", new[] { "ultrafast", "superfast", "veryfast" }[i], "-crf", new[] { "30", "25", "21" }[i] };
            }
        }

        public static List<string> Build(RecordingOptions o)
        {
            var a = new List<string> { "-hide_banner", "-loglevel", o.LogLevel, "-nostats", "-progress", "pipe:1", "-y" };

            int capW = o.Area.Width & ~1, capH = o.Area.Height & ~1;
            int outW = capW, outH = capH;
            bool scale = false;
            if (o.ScaleHeight > 0 && o.ScaleHeight < capH)
            {
                outH = o.ScaleHeight & ~1;
                outW = (int)Math.Round(capW * (double)outH / capH / 2.0) * 2;
                scale = true;
            }

            string fps = o.Fps.ToString(Inv);
            string mouse = o.DrawMouse ? "1" : "0";
            bool qsvDda = o.UseDda && o.EncoderKey == "qsv";
            double videoDelay = o.AudioOffsetMs < 0 ? -o.AudioOffsetMs / 1000.0 : 0;
            double audioDelay = o.AudioOffsetMs > 0 ? o.AudioOffsetMs / 1000.0 : 0;

            // ---------------- Input 0: video
            if (videoDelay > 0) a.AddRange(new[] { "-itsoffset", videoDelay.ToString("0.###", Inv) });
            if (o.TestPattern)
            {
                a.AddRange(new[] { "-f", "lavfi", "-i", $"testsrc2=size={capW}x{capH}:rate={fps},realtime" });
            }
            else if (o.UseDda)
            {
                string src = $"ddagrab=output_idx={o.DdaIndex}:framerate={fps}:draw_mouse={mouse}";
                if (qsvDda)
                {
                    a.AddRange(new[] { "-init_hw_device", "d3d11va=d3d", "-init_hw_device", "qsv=q@d3d", "-filter_hw_device", "d3d" });
                    src += ",hwmap=derive_device=qsv,format=qsv,scale_qsv=format=nv12" + (scale ? $":w={outW}:h={outH}" : "");
                }
                else
                {
                    src += ",hwdownload,format=bgra";
                }
                a.AddRange(new[] { "-f", "lavfi", "-i", src });
            }
            else
            {
                a.AddRange(new[]
                {
                    "-thread_queue_size", "64", "-f", "gdigrab", "-framerate", fps, "-draw_mouse", mouse,
                    "-offset_x", o.Area.X.ToString(Inv), "-offset_y", o.Area.Y.ToString(Inv),
                    "-video_size", $"{capW}x{capH}", "-i", "desktop",
                });
            }

            // ---------------- Input 1..2: am thanh qua named pipe (raw float 48k stereo)
            int nextIndex = 1;
            int? micIndex = null, sysIndex = null;
            void AddPipe(string path)
            {
                if (audioDelay > 0) a.AddRange(new[] { "-itsoffset", audioDelay.ToString("0.###", Inv) });
                a.AddRange(new[]
                {
                    "-thread_queue_size", "1024", "-f", "f32le", "-ar", "48000", "-ac", "2",
                    "-probesize", "32", "-analyzeduration", "0", "-i", path,
                });
            }
            if (o.MicPipePath != null) { AddPipe(o.MicPipePath); micIndex = nextIndex++; }
            if (o.SystemPipePath != null) { AddPipe(o.SystemPipePath); sysIndex = nextIndex++; }

            // ---------------- Bo loc am thanh
            var fc = new List<string>();
            var audioMaps = new List<string>();
            var titles = new List<string>();
            if (micIndex.HasValue) fc.Add($"[{micIndex}:a]{MicChain(o.NoiseFilter, o.VoiceLeveler, o.MicBoostDb)}[mic]");
            if (sysIndex.HasValue) fc.Add($"[{sysIndex}:a]anull[sys]");
            string mix = "amix=inputs=2:duration=longest:dropout_transition=0:normalize=0," + Limiter;
            if (micIndex.HasValue && sysIndex.HasValue)
            {
                bool duck = o.Ducking;
                // Tach mic/sys thanh cac nhanh: mix (+ sidechain neu duck) (+ track rieng)
                var micOuts = new List<string> { "[micm]" };
                if (duck) micOuts.Add("[micsc]");
                if (o.SeparateTracks) micOuts.Add("[mic2]");
                fc.Add(micOuts.Count > 1 ? $"[mic]asplit={micOuts.Count}{string.Concat(micOuts)}" : "[mic]anull[micm]");

                if (o.SeparateTracks) fc.Add("[sys]asplit=2[sysm][sys2]");
                else fc.Add("[sys]anull[sysm]");

                string sysForMix = "[sysm]";
                if (duck)
                {
                    fc.Add($"[sysm][micsc]{DuckFilter}[sysd]");
                    sysForMix = "[sysd]";
                }
                fc.Add($"[micm]{sysForMix}{mix}[mix]");
                audioMaps.Add("[mix]");
                titles.Add("Mic + He thong");
                if (o.SeparateTracks)
                {
                    audioMaps.AddRange(new[] { "[mic2]", "[sys2]" });
                    titles.AddRange(new[] { "Microphone", "Am thanh he thong" });
                }
            }
            else if (micIndex.HasValue) { audioMaps.Add("[mic]"); titles.Add("Microphone"); }
            else if (sysIndex.HasValue) { audioMaps.Add("[sys]"); titles.Add("Am thanh he thong"); }

            if (fc.Count > 0) a.AddRange(new[] { "-filter_complex", string.Join(";", fc) });
            a.AddRange(new[] { "-map", "0:v:0" });
            foreach (var m in audioMaps) a.AddRange(new[] { "-map", m });

            // ---------------- Bo loc video
            var vf = new List<string>();
            if (!qsvDda)
            {
                if (scale) vf.Add($"scale={outW}:{outH}:flags=fast_bilinear");
                vf.Add("format=" + PixFmtFor(o.EncoderKey));
            }
            if (vf.Count > 0) a.AddRange(new[] { "-vf", string.Join(",", vf) });

            // ---------------- Ma hoa
            a.AddRange(new[] { "-c:v", o.Codec });
            a.AddRange(QualityArgs(o.EncoderKey, o.Quality));
            // Khung hinh khoa moi 10 giay (truoc: 2 giay). Bai giang phan lon dung yen -> khung khoa chiem phan lon
            // dung luong; gian ra 10 giay giam ~70% dung luong video ma chu van ro nhu cu (do SSIM ~0.9994).
            a.AddRange(new[] { "-g", (o.Fps * 10).ToString(Inv), "-bf", "0" });
            if (audioMaps.Count > 0)
            {
                a.AddRange(new[] { "-c:a", "aac", "-b:a", o.AudioBitrateK.ToString(Inv) + "k", "-ar", "48000", "-ac", "2" });
                for (int i = 0; i < titles.Count; i++)
                    a.AddRange(new[] { $"-metadata:s:a:{i}", "title=" + titles[i] });
            }
            if (o.DurationSeconds > 0) a.AddRange(new[] { "-t", o.DurationSeconds.ToString(Inv) });
            a.AddRange(new[] { "-max_muxing_queue_size", "2048", "-f", "matroska", o.RecordFile });
            return a;
        }
    }
}
