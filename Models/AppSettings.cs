using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace ScreenRecorderApp.Models
{
    /// <summary>Cai dat duoc luu vao recorder-gui-settings.json (canh file exe, hoac %AppData% neu khong ghi duoc).</summary>
    [DataContract]
    public sealed class AppSettings
    {
        private const string FileName = "recorder-gui-settings.json";

        [DataMember] public string MonitorKey { get; set; } = "";
        [DataMember] public string CaptureMethod { get; set; } = "gdigrab";
        [DataMember] public bool DrawMouse { get; set; }
        [DataMember] public string Encoder { get; set; } = "auto";
        [DataMember] public int Fps { get; set; }
        [DataMember] public string Quality { get; set; } = "medium";
        [DataMember] public int ScaleHeight { get; set; }
        [DataMember] public string Container { get; set; } = "mp4";
        [DataMember] public string OutputDir { get; set; } = "";

        [DataMember] public bool SystemAudioEnabled { get; set; }
        [DataMember] public string SystemAudioDeviceId { get; set; } = "";
        [DataMember] public int SystemVolume { get; set; }
        [DataMember] public bool MicEnabled { get; set; }
        [DataMember] public string MicDeviceId { get; set; } = "";
        [DataMember] public int MicVolume { get; set; }
        [DataMember] public bool NoiseFilter { get; set; }
        [DataMember] public int MicBoostDb { get; set; }
        [DataMember] public bool VoiceLeveler { get; set; }
        [DataMember] public bool Ducking { get; set; }
        [DataMember] public bool SeparateTracks { get; set; }
        [DataMember] public int AudioOffsetMs { get; set; }
        [DataMember] public int AudioBitrateK { get; set; }

        [DataMember] public bool Countdown { get; set; }
        [DataMember] public bool MinimizeToTray { get; set; }
        [DataMember] public string FfmpegPath { get; set; } = "";
        /// <summary>Ten may da luu cai dat (de nhan biet khi thu muc duoc chep sang may khac).</summary>
        [DataMember] public string MachineName { get; set; } = "";

        public AppSettings() { SetDefaults(); }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext _) { SetDefaults(); }

        private void SetDefaults()
        {
            MonitorKey = ""; CaptureMethod = "gdigrab"; DrawMouse = true; Encoder = "auto";
            Fps = 30; Quality = "medium"; ScaleHeight = 0; Container = "mp4"; OutputDir = "";
            SystemAudioEnabled = true; SystemAudioDeviceId = ""; SystemVolume = 100;
            MicEnabled = true; MicDeviceId = ""; MicVolume = 100; NoiseFilter = true;
            MicBoostDb = 8; VoiceLeveler = true; Ducking = false;
            SeparateTracks = false; AudioOffsetMs = 0; AudioBitrateK = 160;
            Countdown = true; MinimizeToTray = false; FfmpegPath = "";
        }

        public static string AppDir => AppDomain.CurrentDomain.BaseDirectory;

        private static string PortablePath => Path.Combine(AppDir, FileName);

        private static string RoamingPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenRecorder", FileName);

        public static AppSettings Load()
        {
            foreach (var path in new[] { PortablePath, RoamingPath })
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    using (var fs = File.OpenRead(path))
                    {
                        var ser = new DataContractJsonSerializer(typeof(AppSettings));
                        if (ser.ReadObject(fs) is AppSettings s)
                        {
                            s.ResetIfOtherMachine();
                            return s;
                        }
                    }
                }
                catch { /* file hong -> dung mac dinh */ }
            }
            return new AppSettings();
        }

        /// <summary>
        /// Thu muc duoc chep sang may khac -> bo cac cai dat gan voi phan cung/duong dan cua may cu
        /// (man hinh, thiet bi am thanh, duong dan ffmpeg, thu muc luu khong ton tai).
        /// Cac lua chon chung (fps, chat luong, loc on, tang giong...) duoc giu nguyen.
        /// </summary>
        private void ResetIfOtherMachine()
        {
            string me = Environment.MachineName;
            if (string.Equals(MachineName, me, StringComparison.OrdinalIgnoreCase)) return;
            MonitorKey = "";
            SystemAudioDeviceId = "";
            MicDeviceId = "";
            MicVolume = 100;
            SystemVolume = 100;
            FfmpegPath = "";
            try { if (!string.IsNullOrEmpty(OutputDir) && !Directory.Exists(OutputDir)) OutputDir = ""; }
            catch { OutputDir = ""; }
            MachineName = me;
        }

        public void Save()
        {
            MachineName = Environment.MachineName;
            if (TrySave(PortablePath)) return;
            try { Directory.CreateDirectory(Path.GetDirectoryName(RoamingPath)!); } catch { }
            TrySave(RoamingPath);
        }

        private bool TrySave(string path)
        {
            try
            {
                using (var ms = new MemoryStream())
                {
                    using (var writer = JsonReaderWriterFactory.CreateJsonWriter(ms, Encoding.UTF8, false, true, "  "))
                    {
                        new DataContractJsonSerializer(typeof(AppSettings)).WriteObject(writer, this);
                    }
                    File.WriteAllBytes(path, ms.ToArray());
                }
                return true;
            }
            catch { return false; }
        }
    }
}
