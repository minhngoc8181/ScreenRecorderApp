using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using ScreenRecorderApp.Controls;
using ScreenRecorderApp.Models;
using ScreenRecorderApp.Services;
using WinForms = System.Windows.Forms;
using DrawingIcon = System.Drawing.Icon;

namespace ScreenRecorderApp
{
    /// <summary>Muc chon trong ComboBox (text hien thi + gia tri).</summary>
    public sealed class Choice<T>
    {
        public Choice(string text, T value) { Text = text; Value = value; }
        public string Text { get; }
        public T Value { get; }
        public override string ToString() => Text;
    }

    public partial class MainWindow : Window
    {
        private const int HotkeyRecord = 1, HotkeyMute = 2;

        private readonly AppSettings _settings = AppSettings.Load();
        private readonly DispatcherTimer _meterTimer;
        private readonly DispatcherTimer _previewTimer;
        private readonly DispatcherTimer _clockTimer;
        private readonly DispatcherTimer _saveTimer;
        private readonly Stopwatch _recClock = new Stopwatch();

        private string? _ffmpeg;
        private List<MonitorInfo> _monitors = new List<MonitorInfo>();
        private List<EncoderInfo> _encoders = new List<EncoderInfo>();
        private bool _ddagrabAvailable;
        private bool _probing;
        private AudioSource? _sysSource, _micSource;
        private bool _micMuted;
        private RecordingSession? _session;
        private bool _isTestRecording;
        private bool _busy;
        private bool _closeAfterStop;
        private bool _previewBusy;
        private int _previewTick;
        private bool _suppress = true;
        private string? _lastFile;
        private string _lastCommandLine = "";
        private System.Threading.CancellationTokenSource? _playCts;
        private WinForms.NotifyIcon? _tray;
        private WinForms.ToolStripMenuItem? _trayRecordItem;
        private DrawingIcon? _iconIdle, _iconRec;
        private IntPtr _hwnd;

        public MainWindow()
        {
            InitializeComponent();
            // Khong de cua so lon hon vung lam viec (laptop 1080p + scale 125%...)
            var work = SystemParameters.WorkArea;
            if (Height > work.Height) Height = Math.Max(MinHeight, work.Height);
            if (Width > work.Width) Width = Math.Max(MinWidth, work.Width);
            _meterTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
            _meterTimer.Tick += (s, e) => UpdateMeters();
            _previewTimer = new DispatcherTimer { Interval = PreviewIdleInterval };
            _previewTimer.Tick += (s, e) => RefreshPreview();
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _clockTimer.Tick += (s, e) => UpdateClock();
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _saveTimer.Tick += (s, e) => { _saveTimer.Stop(); _settings.Save(); };

            SourceInitialized += OnSourceInitialized;
            Loaded += OnLoaded;
            Closing += OnClosing;
            Closed += OnClosed;
            StateChanged += OnStateChanged;
        }

        // =====================================================================
        //  Khoi dong
        // =====================================================================

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Native.ApplyDarkTitleBar(_hwnd);
            HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            _suppress = true;
            try
            {
                PopulateChoices();
                ApplySettingsToControls();
                LocateFfmpeg();
                LoadMonitors();
                LoadAudioDevices();
            }
            finally { _suppress = false; }

            WireEvents();
            RegisterHotkeys();
            CreateTray();
            UpdateAdminText();
            RestartSystemSource();
            RestartMicSource();
            _meterTimer.Start();
            _previewTimer.Start();
            RefreshPreview(true);
            Log("Khởi động xong. Thư mục lưu: " + GetOutputDir(false));
            await ProbeEncodersAsync();
        }

        private void PopulateChoices()
        {
            CaptureCombo.ItemsSource = new[]
            {
                new Choice<string>("GDI (gdigrab) — ổn định, mọi máy", "gdigrab"),
                new Choice<string>("Desktop Duplication (ddagrab) — GPU, nhẹ CPU", "ddagrab"),
            };
            FpsCombo.ItemsSource = new[] { 10, 15, 20, 24, 30, 60 }.Select(f => new Choice<int>($"{f} fps", f)).ToList();
            QualityCombo.ItemsSource = new[]
            {
                new Choice<string>("Thấp — file nhỏ, nhẹ máy", "low"),
                new Choice<string>("Trung bình — khuyên dùng", "medium"),
                new Choice<string>("Cao — nét, file lớn", "high"),
            };
            ResolutionCombo.ItemsSource = new[]
            {
                new Choice<int>("Gốc (theo màn hình)", 0),
                new Choice<int>("1080p", 1080),
                new Choice<int>("720p", 720),
                new Choice<int>("480p", 480),
            };
            FormatCombo.ItemsSource = new[]
            {
                new Choice<string>("MP4 — phổ biến nhất", "mp4"),
                new Choice<string>("MKV — an toàn tuyệt đối", "mkv"),
            };
            BitrateCombo.ItemsSource = new[] { 96, 128, 160, 192, 256 }.Select(b => new Choice<int>($"{b} kbps", b)).ToList();
        }

        private void ApplySettingsToControls()
        {
            SelectChoice(CaptureCombo, _settings.CaptureMethod);
            SelectChoice(FpsCombo, _settings.Fps);
            SelectChoice(QualityCombo, _settings.Quality);
            SelectChoice(ResolutionCombo, _settings.ScaleHeight);
            SelectChoice(FormatCombo, _settings.Container);
            SelectChoice(BitrateCombo, _settings.AudioBitrateK);
            DrawMouseCheck.IsChecked = _settings.DrawMouse;
            SysEnabled.IsChecked = _settings.SystemAudioEnabled;
            MicEnabled.IsChecked = _settings.MicEnabled;
            NoiseFilterCheck.IsChecked = _settings.NoiseFilter;
            MicBoostSlider.Value = _settings.MicBoostDb;
            MicBoostText.Text = FormatDb(_settings.MicBoostDb);
            VoiceLevelerCheck.IsChecked = _settings.VoiceLeveler;
            DuckingCheck.IsChecked = _settings.Ducking;
            SeparateTracksCheck.IsChecked = _settings.SeparateTracks;
            CountdownCheck.IsChecked = _settings.Countdown;
            TrayCheck.IsChecked = _settings.MinimizeToTray;
            SysVolume.Value = _settings.SystemVolume;
            SysVolumeText.Text = _settings.SystemVolume + "%";
            MicVolume.Value = _settings.MicVolume;
            MicVolumeText.Text = _settings.MicVolume + "%";
            OffsetSlider.Value = _settings.AudioOffsetMs;
            OffsetText.Text = _settings.AudioOffsetMs + " ms";
            OutputDirBox.Text = GetOutputDir(false);
        }

        private void WireEvents()
        {
            MonitorList.SelectionChanged += (s, e) =>
            {
                if (_suppress || !(MonitorList.SelectedItem is MonitorInfo m)) return;
                _settings.MonitorKey = m.Key;
                Persist();
                UpdateCaptureInfo();
                // Hien ngay anh thu nho (neu co) roi lam moi anh lon
                PreviewImage.Source = m.Thumbnail;
                RefreshPreview();
            };
            CaptureCombo.SelectionChanged += (s, e) =>
            {
                if (_suppress) return;
                _settings.CaptureMethod = Sel(CaptureCombo, "gdigrab");
                Persist();
                UpdateCaptureInfo();
            };
            DrawMouseCheck.Click += (s, e) => { _settings.DrawMouse = DrawMouseCheck.IsChecked == true; Persist(); };

            SysEnabled.Click += (s, e) => { _settings.SystemAudioEnabled = SysEnabled.IsChecked == true; Persist(); RestartSystemSource(); };
            MicEnabled.Click += (s, e) => { _settings.MicEnabled = MicEnabled.IsChecked == true; Persist(); RestartMicSource(); };
            SysDeviceCombo.SelectionChanged += (s, e) =>
            {
                if (_suppress || !(SysDeviceCombo.SelectedItem is AudioDeviceItem d)) return;
                _settings.SystemAudioDeviceId = d.Id;
                Persist();
                RestartSystemSource();
            };
            MicDeviceCombo.SelectionChanged += (s, e) =>
            {
                if (_suppress || !(MicDeviceCombo.SelectedItem is AudioDeviceItem d)) return;
                _settings.MicDeviceId = d.Id;
                Persist();
                RestartMicSource();
            };
            SysVolume.ValueChanged += (s, e) =>
            {
                int v = (int)Math.Round(e.NewValue);
                SysVolumeText.Text = v + "%";
                _settings.SystemVolume = v;
                if (_sysSource != null) _sysSource.Gain = v / 100f;
                Persist();
            };
            MicVolume.ValueChanged += (s, e) =>
            {
                int v = (int)Math.Round(e.NewValue);
                MicVolumeText.Text = v + "%";
                _settings.MicVolume = v;
                if (_micSource != null) _micSource.Gain = v / 100f;
                Persist();
            };
            NoiseFilterCheck.Click += (s, e) => { _settings.NoiseFilter = NoiseFilterCheck.IsChecked == true; Persist(); };
            MicBoostSlider.ValueChanged += (s, e) =>
            {
                int v = (int)Math.Round(e.NewValue);
                MicBoostText.Text = FormatDb(v);
                _settings.MicBoostDb = v;
                Persist();
            };
            VoiceLevelerCheck.Click += (s, e) => { _settings.VoiceLeveler = VoiceLevelerCheck.IsChecked == true; Persist(); };
            DuckingCheck.Click += (s, e) => { _settings.Ducking = DuckingCheck.IsChecked == true; Persist(); };

            EncoderCombo.SelectionChanged += (s, e) =>
            {
                if (_suppress || !(EncoderCombo.SelectedItem is EncoderInfo enc)) return;
                _settings.Encoder = enc.Key;
                Persist();
                UpdateEncoderHint();
                UpdateCaptureInfo();
            };
            FpsCombo.SelectionChanged += (s, e) => { if (!_suppress) { _settings.Fps = Sel(FpsCombo, 30); Persist(); } };
            QualityCombo.SelectionChanged += (s, e) => { if (!_suppress) { _settings.Quality = Sel(QualityCombo, "medium"); Persist(); } };
            ResolutionCombo.SelectionChanged += (s, e) => { if (!_suppress) { _settings.ScaleHeight = Sel(ResolutionCombo, 0); Persist(); } };
            FormatCombo.SelectionChanged += (s, e) => { if (!_suppress) { _settings.Container = Sel(FormatCombo, "mp4"); Persist(); } };
            BitrateCombo.SelectionChanged += (s, e) => { if (!_suppress) { _settings.AudioBitrateK = Sel(BitrateCombo, 160); Persist(); } };
            OutputDirBox.LostFocus += (s, e) =>
            {
                var t = OutputDirBox.Text.Trim();
                _settings.OutputDir = string.Equals(t, DefaultOutputDir, StringComparison.OrdinalIgnoreCase) ? "" : t;
                Persist();
            };

            OffsetSlider.ValueChanged += (s, e) =>
            {
                int v = (int)Math.Round(e.NewValue);
                OffsetText.Text = (v > 0 ? "+" : "") + v + " ms";
                _settings.AudioOffsetMs = v;
                Persist();
            };
            SeparateTracksCheck.Click += (s, e) => { _settings.SeparateTracks = SeparateTracksCheck.IsChecked == true; Persist(); };
            CountdownCheck.Click += (s, e) => { _settings.Countdown = CountdownCheck.IsChecked == true; Persist(); };
            TrayCheck.Click += (s, e) => { _settings.MinimizeToTray = TrayCheck.IsChecked == true; Persist(); };
            FfmpegPathBox.LostFocus += async (s, e) =>
            {
                var t = FfmpegPathBox.Text.Trim();
                if (string.Equals(t, _ffmpeg ?? "", StringComparison.OrdinalIgnoreCase)) return;
                _settings.FfmpegPath = t;
                Persist();
                LocateFfmpeg();
                await ProbeEncodersAsync();
            };
        }

        // =====================================================================
        //  FFmpeg + encoder
        // =====================================================================

        private void LocateFfmpeg()
        {
            _ffmpeg = FfmpegLocator.Find(_settings.FfmpegPath);
            FfmpegPathBox.Text = _ffmpeg ?? _settings.FfmpegPath;
            if (_ffmpeg == null)
            {
                FfmpegVersionText.Text = "⚠ Không tìm thấy ffmpeg.exe — hãy đặt ffmpeg.exe cạnh ScreenRecorder.exe hoặc bấm “Chọn…”.";
                FfmpegVersionText.Foreground = (Brush)FindResource("WarnBrush");
                EncoderHint.Text = "Cần ffmpeg.exe để ghi hình.";
                Log("Không tìm thấy ffmpeg.exe.");
            }
            else
            {
                FfmpegVersionText.Foreground = (Brush)FindResource("MutedBrush");
                Log("FFmpeg: " + _ffmpeg);
            }
        }

        private async Task ProbeEncodersAsync()
        {
            if (_ffmpeg == null || _probing) return;
            _probing = true;
            ProbeButton.IsEnabled = false;
            try
            {
                string ff = _ffmpeg;
                FfmpegVersionText.Text = await EncoderProbe.VersionAsync(ff);
                _encoders = EncoderProbe.CreateList();
                BindEncoders();
                EncoderHint.Text = "Đang kiểm tra bộ mã hóa phần cứng…";
                foreach (var enc in _encoders.Where(x => x.Key != "auto"))
                {
                    enc.Available = await EncoderProbe.TestAsync(ff, enc);
                    enc.Probed = true;
                    BindEncoders();
                    Log($"Encoder {enc.Codec}: {(enc.Available ? "dùng được" : "không khả dụng")}");
                }
                _ddagrabAvailable = await EncoderProbe.HasFilterAsync(ff, "ddagrab");
                Log("ddagrab (Desktop Duplication): " + (_ddagrabAvailable ? "có" : "không có trong bản ffmpeg này"));
                UpdateEncoderHint();
                UpdateCaptureInfo();
            }
            catch (Exception ex)
            {
                Log("Lỗi kiểm tra encoder: " + ex.Message);
            }
            finally
            {
                _probing = false;
                ProbeButton.IsEnabled = _session == null;
            }
        }

        private void BindEncoders()
        {
            bool old = _suppress;
            _suppress = true;
            try
            {
                EncoderCombo.ItemsSource = null;
                EncoderCombo.ItemsSource = _encoders;
                EncoderCombo.SelectedItem = _encoders.FirstOrDefault(x => x.Key == _settings.Encoder) ?? _encoders.FirstOrDefault();
            }
            finally { _suppress = old; }
        }

        private EncoderInfo? ResolveEncoder()
        {
            var selected = _encoders.FirstOrDefault(x => x.Key == _settings.Encoder);
            if (selected != null && selected.Key != "auto" && (selected.Available || !selected.Probed)) return selected;
            var best = _encoders.FirstOrDefault(x => x.Key != "auto" && x.Available);
            if (best != null) return best;
            // Chua kiem tra xong -> tam dung x264
            return _encoders.FirstOrDefault(x => x.Key == "x264" && !x.Probed);
        }

        private void UpdateEncoderHint()
        {
            if (_probing) return;
            var enc = ResolveEncoder();
            var sel = EncoderCombo.SelectedItem as EncoderInfo;
            if (enc == null) EncoderHint.Text = "⚠ Không có bộ mã hóa nào hoạt động.";
            else if (sel != null && sel.Key != "auto" && sel.Key != enc.Key) EncoderHint.Text = $"“{sel.Name}” không khả dụng → sẽ dùng {enc.Name}.";
            else EncoderHint.Text = $"Sẽ dùng: {enc.Name}" + (enc.Key == "x264" ? "  (dùng CPU, máy yếu nên chọn 15–24 fps)" : "  (phần cứng, nhẹ CPU)");
        }

        // =====================================================================
        //  Man hinh
        // =====================================================================

        private void LoadMonitors()
        {
            bool old = _suppress;
            _suppress = true;
            try
            {
                _monitors = MonitorService.GetMonitors();
                MonitorList.ItemsSource = _monitors;
                MonitorList.SelectedItem = _monitors.FirstOrDefault(m => m.Key.Equals(_settings.MonitorKey, StringComparison.OrdinalIgnoreCase))
                                            ?? _monitors.FirstOrDefault();
            }
            finally { _suppress = old; }
            UpdateCaptureInfo();
        }

        private bool CanUseDda(MonitorInfo m, out string reason)
        {
            reason = "";
            if (_settings.CaptureMethod != "ddagrab") return false;
            if (!_ddagrabAvailable) { reason = "Bản ffmpeg này không có ddagrab → dùng GDI."; return false; }
            if (m.IsAllScreens) { reason = "ddagrab chỉ ghi được 1 màn hình → dùng GDI."; return false; }
            if (!m.DxgiOutputIndex.HasValue) { reason = "Màn hình này không thuộc GPU chính → dùng GDI."; return false; }
            return true;
        }

        private void UpdateCaptureInfo()
        {
            if (!(MonitorList.SelectedItem is MonitorInfo m)) { PreviewInfo.Text = ""; return; }
            var b = m.Bounds;
            PreviewInfo.Text = $"{b.Width}×{b.Height}  •  vị trí ({b.X}, {b.Y})" + (m.DxgiOutputIndex.HasValue ? $"  •  DXGI #{m.DxgiOutputIndex}" : "");
            if (_settings.CaptureMethod == "ddagrab")
                CaptureHint.Text = CanUseDda(m, out var reason) ? "Desktop Duplication: ít tốn CPU nhất." : reason;
            else
                CaptureHint.Text = "GDI: tương thích mọi máy.";
        }

        private static readonly TimeSpan PreviewIdleInterval = TimeSpan.FromMilliseconds(400);
        private static readonly TimeSpan PreviewRecordingInterval = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Lam moi anh xem truoc man hinh dang chon (~2.5 hinh/giay, 1 hinh/giay khi dang ghi)
        /// va anh thu nho cua tat ca man hinh (~2 giay/lan, tam dung khi dang ghi).
        /// </summary>
        private async void RefreshPreview(bool forceThumbnails = false)
        {
            if (_previewBusy || WindowState == WindowState.Minimized || !IsVisible) return;
            if (!(MonitorList.SelectedItem is MonitorInfo m)) return;
            _previewBusy = true;
            try
            {
                bool thumbs = forceThumbnails || (_session == null && _previewTick % 5 == 0);
                _previewTick++;
                var bounds = m.Bounds;
                var targets = thumbs ? _monitors.ToList() : new List<MonitorInfo>();
                var result = await Task.Run(() =>
                {
                    var big = ScreenCapture.CaptureThumbnail(bounds, 960, 720);
                    var small = targets.Select(x => (Monitor: x, Image: ScreenCapture.CaptureThumbnail(x.Bounds, 264, 140))).ToList();
                    return (Big: big, Small: small);
                });
                if (result.Big != null && MonitorList.SelectedItem == m)
                {
                    PreviewImage.Source = result.Big;
                    PreviewPlaceholder.Visibility = Visibility.Collapsed;
                }
                foreach (var t in result.Small)
                    if (t.Image != null) t.Monitor.Thumbnail = t.Image;
            }
            catch (Exception ex) { Debug.WriteLine("Preview: " + ex.Message); }
            finally { _previewBusy = false; }
        }

        private void RefreshMonitors_Click(object sender, RoutedEventArgs e)
        {
            LoadMonitors();
            RefreshPreview(true);
            Log($"Đã tải lại {_monitors.Count(m => !m.IsAllScreens)} màn hình.");
        }

        private void Identify_Click(object sender, RoutedEventArgs e)
        {
            foreach (var m in _monitors.Where(x => !x.IsAllScreens))
            {
                var number = new TextBlock
                {
                    Text = m.Number.ToString(),
                    FontSize = 120,
                    FontWeight = FontWeights.Bold,
                    Foreground = (Brush)FindResource("AccentGradient"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                };
                var info = new TextBlock
                {
                    Text = $"{m.Bounds.Width}×{m.Bounds.Height}{(m.Primary ? " • chính" : "")}",
                    FontSize = 22,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                };
                var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                panel.Children.Add(number);
                panel.Children.Add(info);
                var win = new Window
                {
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize,
                    ShowInTaskbar = false,
                    Topmost = true,
                    ShowActivated = false,
                    Background = new SolidColorBrush(Color.FromArgb(240, 37, 42, 57)),
                    BorderBrush = (Brush)FindResource("AccentBrush"),
                    BorderThickness = new Thickness(3),
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -10000, Top = -10000, Width = 300, Height = 220,
                    Content = new Viewbox { Margin = new Thickness(16), Child = panel },
                };
                win.Show();
                int w = Math.Max(240, m.Bounds.Width / 5), h = w * 3 / 4;
                var hwnd = new WindowInteropHelper(win).Handle;
                Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, m.Bounds.X + (m.Bounds.Width - w) / 2, m.Bounds.Y + (m.Bounds.Height - h) / 2,
                    w, h, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
                var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
                t.Tick += (s2, e2) => { t.Stop(); win.Close(); };
                t.Start();
            }
        }

        // =====================================================================
        //  Am thanh
        // =====================================================================

        private void LoadAudioDevices()
        {
            bool old = _suppress;
            _suppress = true;
            try
            {
                var outs = AudioDeviceService.GetDevices(DataFlow.Render);
                var ins = AudioDeviceService.GetDevices(DataFlow.Capture);
                SysDeviceCombo.ItemsSource = outs;
                MicDeviceCombo.ItemsSource = ins;
                SysDeviceCombo.SelectedItem = outs.FirstOrDefault(d => d.Id == _settings.SystemAudioDeviceId)
                                              ?? outs.FirstOrDefault(d => d.IsDefault) ?? outs.FirstOrDefault();
                MicDeviceCombo.SelectedItem = ins.FirstOrDefault(d => d.Id == _settings.MicDeviceId)
                                              ?? ins.FirstOrDefault(d => d.IsDefault) ?? ins.FirstOrDefault();
                if (outs.Count == 0) Log("Không tìm thấy thiết bị phát âm thanh (loa/tai nghe).");
                if (ins.Count == 0) Log("Không tìm thấy microphone.");
            }
            catch (Exception ex)
            {
                Log("Lỗi liệt kê thiết bị âm thanh: " + ex.Message);
            }
            finally { _suppress = old; }
        }

        private void RestartSystemSource()
        {
            _sysSource?.Dispose();
            _sysSource = null;
            SysFormatText.Text = "Mọi âm thanh phát ra thiết bị này";
            if (SysEnabled.IsChecked != true || !(SysDeviceCombo.SelectedItem is AudioDeviceItem d)) return;
            try
            {
                var src = new AudioSource(d.Id, d.Name, loopback: true) { Gain = (float)(SysVolume.Value / 100.0) };
                src.Error += msg => Log(msg);
                src.Start();
                _sysSource = src;
                SysFormatText.Text = "Thu từ: " + src.FormatDescription;
            }
            catch (Exception ex)
            {
                SysFormatText.Text = "⚠ Không mở được thiết bị này";
                Log($"Không mở được loopback '{d.Name}': {ex.Message}");
            }
        }

        private void RestartMicSource()
        {
            _micSource?.Dispose();
            _micSource = null;
            MicFormatText.Text = "Giọng nói của bạn";
            if (MicEnabled.IsChecked != true || !(MicDeviceCombo.SelectedItem is AudioDeviceItem d)) return;
            try
            {
                var src = new AudioSource(d.Id, d.Name, loopback: false) { Gain = (float)(MicVolume.Value / 100.0), Muted = _micMuted };
                src.Error += msg => Log(msg);
                src.Start();
                _micSource = src;
                MicFormatText.Text = "Thu từ: " + src.FormatDescription;
            }
            catch (Exception ex)
            {
                MicFormatText.Text = "⚠ Không mở được micro (kiểm tra Quyền riêng tư → Microphone)";
                Log($"Không mở được micro '{d.Name}': {ex.Message}");
            }
        }

        private void UpdateMeters()
        {
            SysMeter.SetLevel(_sysSource?.ReadPeak() ?? 0f);
            MicMeter.SetLevel(_micSource?.ReadPeak() ?? 0f);
        }

        private void RefreshAudio_Click(object sender, RoutedEventArgs e)
        {
            LoadAudioDevices();
            RestartSystemSource();
            RestartMicSource();
            Log("Đã tải lại thiết bị âm thanh.");
        }

        private void ToggleMicMute()
        {
            _micMuted = !_micMuted;
            if (_micSource != null) _micSource.Muted = _micMuted;
            MicMutedBadge.Visibility = _micMuted ? Visibility.Visible : Visibility.Collapsed;
            Log(_micMuted ? "Micro: ĐÃ TẮT TIẾNG (Ctrl+Alt+M để bật lại)" : "Micro: đã bật tiếng");
            if (_tray != null && !IsVisible)
                _tray.ShowBalloonTip(1500, "Screen Recorder", _micMuted ? "Đã tắt tiếng micro" : "Đã bật tiếng micro", WinForms.ToolTipIcon.Info);
        }

        private async void SysTest_Click(object sender, RoutedEventArgs e)
        {
            var dev = SysDeviceCombo.SelectedItem as AudioDeviceItem;
            SysTestButton.IsEnabled = false;
            try
            {
                await PlayAsync(dev?.Id, new BufferSampleProvider(BufferSampleProvider.CreateChime()));
                if (SysEnabled.IsChecked == true)
                    Log("Đã phát âm thử. Nếu thanh mức âm hệ thống nhảy lên thì phần thu loa hoạt động.");
            }
            catch (Exception ex) { Log("Không phát được âm thử: " + ex.Message); }
            finally { SysTestButton.IsEnabled = _session == null; }
        }

        private async void MicTest_Click(object sender, RoutedEventArgs e)
        {
            var mic = _micSource;
            if (mic == null || !mic.IsRunning)
            {
                MessageBox.Show(this, "Chưa bật hoặc chưa mở được microphone.", "Thu thử", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            const int seconds = 5;
            int max = AudioSource.SampleRate * AudioSource.Channels * seconds;
            var samples = new List<float>(max);
            var gate = new object();
            Action<float[], int> handler = (buf, n) =>
            {
                lock (gate)
                {
                    int take = Math.Min(n, max - samples.Count);
                    if (take > 0) samples.AddRange(new ArraySegment<float>(buf, 0, take));
                }
            };

            MicTestButton.IsEnabled = false;
            try
            {
                mic.SamplesAvailable += handler;
                for (int i = seconds; i > 0; i--)
                {
                    MicTestLabel.Text = $"Hãy nói… {i}s";
                    await Task.Delay(1000);
                }
                mic.SamplesAvailable -= handler;
                float[] data;
                lock (gate) data = samples.ToArray();
                MicTestLabel.Text = "Đang xử lý…";
                data = await ProcessMicSamplesAsync(data);
                MicTestLabel.Text = "Đang phát lại…";
                await PlayAsync((SysDeviceCombo.SelectedItem as AudioDeviceItem)?.Id, new BufferSampleProvider(data));
            }
            catch (Exception ex) { Log("Thu thử micro lỗi: " + ex.Message); }
            finally
            {
                mic.SamplesAvailable -= handler;
                MicTestLabel.Text = "Thu thử 5 giây";
                MicTestButton.IsEnabled = _session == null;
            }
        }

        /// <summary>
        /// Cho mau thu micro di qua dung chuoi loc cua FFmpeg (loc on + nen giong + tang am)
        /// de nguoi dung nghe dung nhu trong video. Loi -> tra ve am thanh goc.
        /// </summary>
        private async Task<float[]> ProcessMicSamplesAsync(float[] data)
        {
            string chain = FfmpegArgs.MicChain(_settings.NoiseFilter, _settings.VoiceLeveler, _settings.MicBoostDb);
            if (_ffmpeg == null || chain == "anull" || data.Length == 0) return data;
            string tmpIn = Path.Combine(Path.GetTempPath(), $"screenrec_mic_{Guid.NewGuid():N}.f32");
            string tmpOut = tmpIn + ".out.f32";
            try
            {
                var bytes = new byte[data.Length * 4];
                Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
                File.WriteAllBytes(tmpIn, bytes);
                var r = await ProcessUtil.RunAsync(_ffmpeg, new[]
                {
                    "-hide_banner", "-nostdin", "-loglevel", "error",
                    "-f", "f32le", "-ar", "48000", "-ac", "2", "-i", tmpIn,
                    "-af", chain, "-f", "f32le", "-ar", "48000", "-ac", "2", "-y", tmpOut,
                }, 30000);
                if (r.Code != 0 || !File.Exists(tmpOut))
                {
                    Log("Không xử lý được mẫu micro bằng FFmpeg, phát bản gốc. " + r.Err.Trim());
                    return data;
                }
                var ob = File.ReadAllBytes(tmpOut);
                var processed = new float[ob.Length / 4];
                Buffer.BlockCopy(ob, 0, processed, 0, processed.Length * 4);
                return processed;
            }
            catch (Exception ex)
            {
                Log("Xử lý mẫu micro lỗi: " + ex.Message);
                return data;
            }
            finally
            {
                try { File.Delete(tmpIn); } catch { }
                try { File.Delete(tmpOut); } catch { }
            }
        }

        private static string FormatDb(int db) => db > 0 ? $"+{db} dB" : "0 dB";

        /// <summary>
        /// Phat am thu ra loa da chon. Thu lan luot cac cach phat; cach nao loi / khong ra du lieu
        /// thi tu chuyen sang cach tiep theo.
        /// </summary>
        private async Task PlayAsync(string? deviceId, BufferSampleProvider provider)
        {
            StopTestPlayer();
            var cts = new System.Threading.CancellationTokenSource();
            _playCts = cts;
            Exception? lastError = null;
            try
            {
                foreach (var backend in TestPlayer.Order)
                {
                    provider.Rewind();
                    try
                    {
                        if (await TestPlayer.PlayOnceAsync(deviceId, backend, provider, cts.Token)) return;
                        if (cts.IsCancellationRequested) return;
                        lastError = new InvalidOperationException("thiết bị không nhận dữ liệu âm thanh");
                    }
                    catch (Exception ex) { lastError = ex; }
                    if (cts.IsCancellationRequested) return;
                    Log($"Phát bằng {TestPlayer.Describe(backend)} không được ({lastError?.Message}) → thử cách khác…");
                }
                if (lastError != null) throw lastError;
            }
            finally
            {
                if (_playCts == cts) _playCts = null;
                cts.Dispose();
            }
        }

        private void StopTestPlayer()
        {
            var c = _playCts;
            _playCts = null;
            try { c?.Cancel(); } catch { }
        }

        // =====================================================================
        //  Ghi hinh
        // =====================================================================

        private async void RecordButton_Click(object sender, RoutedEventArgs e) => await ToggleRecordingAsync();

        private async void TestRecord_Click(object sender, RoutedEventArgs e)
        {
            if (_session == null) await StartRecordingAsync(test: true);
        }

        private async Task ToggleRecordingAsync()
        {
            if (_session != null) await StopRecordingAsync();
            else await StartRecordingAsync(test: false);
        }

        private async Task StartRecordingAsync(bool test)
        {
            if (_session != null || _busy) return;
            if (_ffmpeg == null)
            {
                MessageBox.Show(this, "Không tìm thấy ffmpeg.exe.\nHãy đặt ffmpeg.exe cạnh ScreenRecorder.exe hoặc chọn trong tab Nâng cao.",
                    "Screen Recorder", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!(MonitorList.SelectedItem is MonitorInfo monitor)) return;

            _busy = true;
            RecordButton.IsEnabled = false;
            TestRecordButton.IsEnabled = false;
            try
            {
                if (_probing)
                {
                    SetStatus("Đang kiểm tra encoder…", "WarnBrush", false);
                    while (_probing) await Task.Delay(200);
                }
                var enc = ResolveEncoder();
                if (enc == null)
                {
                    MessageBox.Show(this, "Không có bộ mã hóa video nào hoạt động với bản ffmpeg này.", "Screen Recorder",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (_settings.Countdown && !test)
                {
                    for (int i = 3; i > 0; i--)
                    {
                        SetStatus($"Bắt đầu sau {i}…", "WarnBrush", false);
                        RecordLabel.Text = $"{i}…";
                        await Task.Delay(1000);
                    }
                }

                string dir = GetOutputDir(true);
                string baseName = (test ? "Test_" : "Rec_") + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string container = test ? "mp4" : _settings.Container;
                bool useDda = CanUseDda(monitor, out var ddaReason);
                if (!string.IsNullOrEmpty(ddaReason)) Log(ddaReason);

                var opts = new RecordingOptions
                {
                    FfmpegPath = _ffmpeg,
                    Area = monitor.Bounds,
                    UseDda = useDda,
                    DdaIndex = monitor.DxgiOutputIndex ?? 0,
                    EncoderKey = enc.Key,
                    Codec = enc.Codec,
                    Fps = _settings.Fps,
                    Quality = _settings.Quality,
                    ScaleHeight = _settings.ScaleHeight,
                    DrawMouse = _settings.DrawMouse,
                    RecordFile = Path.Combine(dir, baseName + ".mkv"),
                    NoiseFilter = _settings.NoiseFilter,
                    MicBoostDb = _settings.MicBoostDb,
                    VoiceLeveler = _settings.VoiceLeveler,
                    Ducking = _settings.Ducking,
                    SeparateTracks = _settings.SeparateTracks,
                    AudioOffsetMs = _settings.AudioOffsetMs,
                    AudioBitrateK = _settings.AudioBitrateK,
                    DurationSeconds = test ? 10 : 0,
                };
                var session = new RecordingSession(opts, Path.Combine(dir, baseName + "." + container));
                if (_micSource != null && _micSource.IsRunning) session.AddAudio(_micSource, isMic: true);
                if (_sysSource != null && _sysSource.IsRunning) session.AddAudio(_sysSource, isMic: false);
                if (!session.HasAudio) Log("⚠ Không có nguồn âm thanh nào đang hoạt động — chỉ ghi hình.");

                session.Log += msg => Log(msg);
                session.Progress += p => Dispatcher.InvokeAsync(() => UpdateStats(p));
                session.Exited += (code, errors) => Dispatcher.InvokeAsync(() => OnSessionExited(session, code, errors));

                session.Start();
                _session = session;
                _isTestRecording = test;
                _lastCommandLine = $"\"{_ffmpeg}\" {session.CommandLine}";
                Log($"Bắt đầu {(test ? "ghi thử 10 giây" : "ghi")}: {monitor} • {enc.Name} • {(useDda ? "ddagrab" : "gdigrab")} • {_settings.Fps} fps");
                Log("Lệnh: " + _lastCommandLine);

                Native.timeBeginPeriod(1);
                _recClock.Restart();
                _clockTimer.Start();
                ResetStats();
                SetRecordingUi(true);
                if (_settings.MinimizeToTray && !test) Hide();
            }
            catch (Exception ex)
            {
                Log("Không bắt đầu ghi được: " + ex.Message);
                MessageBox.Show(this, "Không bắt đầu ghi được:\n" + ex.Message, "Screen Recorder", MessageBoxButton.OK, MessageBoxImage.Error);
                _session = null;
                SetRecordingUi(false);
            }
            finally
            {
                _busy = false;
                RecordButton.IsEnabled = true;
                if (_session == null) TestRecordButton.IsEnabled = true;
            }
        }

        private async Task StopRecordingAsync()
        {
            var s = _session;
            if (s == null || _busy) return;
            _busy = true;
            RecordButton.IsEnabled = false;
            SetStatus("Đang hoàn tất…", "WarnBrush", false);
            Log("Đang dừng ghi…");
            await s.StopAsync();
            // Phan con lai xu ly trong OnSessionExited
        }

        private async void OnSessionExited(RecordingSession session, int code, string errors)
        {
            if (session != _session) return;
            _busy = true;
            RecordButton.IsEnabled = false;
            _clockTimer.Stop();
            _recClock.Stop();
            Native.timeEndPeriod(1);

            string? result = null;
            try
            {
                string rec = session.Options.RecordFile;
                bool hasFile = File.Exists(rec) && new FileInfo(rec).Length > 4096;
                if (hasFile)
                {
                    if (!session.FinalFile.Equals(rec, StringComparison.OrdinalIgnoreCase))
                    {
                        SetStatus("Đang chuyển sang MP4…", "WarnBrush", false);
                        bool ok = await RecordingSession.RemuxToMp4Async(_ffmpeg!, rec, session.FinalFile, Log);
                        if (ok)
                        {
                            try { File.Delete(rec); } catch { }
                            result = session.FinalFile;
                        }
                        else result = rec;
                    }
                    else result = rec;
                }
                else
                {
                    try { if (File.Exists(rec)) File.Delete(rec); } catch { }
                }
            }
            catch (Exception ex) { Log("Lỗi hoàn tất file: " + ex.Message); }

            bool test = _isTestRecording;
            _session = null;
            _busy = false;
            SetRecordingUi(false);
            RecordButton.IsEnabled = true;

            bool normal = code == 0 || session.StopRequested;
            if (!normal || result == null)
            {
                Log($"FFmpeg dừng bất thường (mã {code}).");
                if (!string.IsNullOrWhiteSpace(errors)) Log(errors);
                if (!IsVisible) RestoreWindow();
                MessageBox.Show(this,
                    $"FFmpeg đã dừng bất thường (mã {code}).\n\n{TrimLines(errors, 8)}\n\n" +
                    "Gợi ý: thử đổi Phương thức chụp sang GDI, hoặc Bộ mã hóa sang CPU (x264), rồi bấm “Ghi thử 10 giây”. Xem chi tiết ở tab Nhật ký.",
                    "Screen Recorder", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            if (result != null)
            {
                _lastFile = result;
                var size = new FileInfo(result).Length;
                LastFileText.Text = $"Đã lưu: {result}  ({FormatBytes(size)})";
                OpenFileButton.IsEnabled = true;
                Log($"Đã lưu: {result} ({FormatBytes(size)})");
                if (test) OpenWithShell(result);
                else if (_tray != null && !IsVisible)
                    _tray.ShowBalloonTip(3000, "Đã lưu video", Path.GetFileName(result), WinForms.ToolTipIcon.Info);
            }

            if (_closeAfterStop) Close();
        }

        private void UpdateStats(FfmpegProgress p)
        {
            if (_session == null) return;
            StatFps.Text = p.Fps > 0 ? p.Fps.ToString("0.#") : "—";
            StatSize.Text = p.TotalSize > 0 ? FormatBytes(p.TotalSize) : "—";
            StatSpeed.Text = string.IsNullOrEmpty(p.Speed) || p.Speed == "N/A" ? "—" : p.Speed.Trim();
            StatDrop.Text = $"{p.Dropped} / {p.Duplicated}";
        }

        private void ResetStats()
        {
            StatFps.Text = StatSize.Text = StatSpeed.Text = StatDrop.Text = "—";
            TimerText.Text = "00:00:00";
        }

        private void UpdateClock()
        {
            var t = _recClock.Elapsed;
            TimerText.Text = $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
            if (_tray != null) _tray.Text = Truncate($"Đang ghi {TimerText.Text}", 63);
        }

        private void SetStatus(string text, string brushKey, bool pulse)
        {
            StatusText.Text = text;
            StatusDot.Fill = (Brush)FindResource(brushKey);
            if (pulse)
            {
                var anim = new DoubleAnimation(1, 0.2, TimeSpan.FromMilliseconds(650)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                StatusDot.BeginAnimation(OpacityProperty, anim);
                RecBadge.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.55, TimeSpan.FromMilliseconds(900)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            }
            else
            {
                StatusDot.BeginAnimation(OpacityProperty, null);
                RecBadge.BeginAnimation(OpacityProperty, null);
            }
        }

        private void SetRecordingUi(bool recording)
        {
            var lockable = new UIElement[]
            {
                MonitorList, CaptureCombo, RefreshMonitorsButton, DrawMouseCheck,
                SysEnabled, SysDeviceCombo, MicEnabled, MicDeviceCombo, RefreshAudioButton, NoiseFilterCheck,
                MicBoostSlider, VoiceLevelerCheck, DuckingCheck,
                SysTestButton, MicTestButton, EncoderCombo, ProbeButton, FpsCombo, QualityCombo, ResolutionCombo,
                FormatCombo, OutputDirBox, BrowseOutputButton, OffsetSlider, SeparateTracksCheck, BitrateCombo,
                FfmpegPathBox, BrowseFfmpegButton, AdminButton, TestRecordButton,
            };
            foreach (var c in lockable) c.IsEnabled = !recording;
            _previewTimer.Interval = recording ? PreviewRecordingInterval : PreviewIdleInterval;

            RecordIcon.Text = recording ? "\uE71A" : "\uE7C8";
            RecordLabel.Text = recording ? (_isTestRecording ? "Dừng ghi thử" : "Dừng ghi") : "Bắt đầu ghi";
            TestRecordLabel.Text = recording && _isTestRecording ? "Đang ghi thử…" : "Ghi thử 10 giây";
            RecBadge.Visibility = recording ? Visibility.Visible : Visibility.Collapsed;
            if (recording) SetStatus(_isTestRecording ? "Đang ghi thử" : "Đang ghi", "RecordBrush", true);
            else SetStatus("Sẵn sàng", "SuccessBrush", false);
            Title = recording ? $"● REC — Screen Recorder" : "Screen Recorder";

            if (_tray != null)
            {
                _tray.Icon = recording ? _iconRec : _iconIdle;
                _tray.Text = recording ? "Screen Recorder — đang ghi" : "Screen Recorder";
            }
            if (_trayRecordItem != null) _trayRecordItem.Text = recording ? "Dừng ghi" : "Bắt đầu ghi";
        }

        // =====================================================================
        //  Thu muc / file
        // =====================================================================

        private static string DefaultOutputDir => Path.Combine(AppSettings.AppDir, "videos");

        private string GetOutputDir(bool create)
        {
            string dir = string.IsNullOrWhiteSpace(_settings.OutputDir) ? DefaultOutputDir : _settings.OutputDir;
            if (!create) return dir;
            try
            {
                Directory.CreateDirectory(dir);
                var probe = Path.Combine(dir, ".write_test");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return dir;
            }
            catch
            {
                string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Screen Recorder");
                Directory.CreateDirectory(fallback);
                Log($"Không ghi được vào '{dir}' → lưu tạm vào '{fallback}'.");
                return fallback;
            }
        }

        private void BrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new WinForms.FolderBrowserDialog { Description = "Chọn thư mục lưu video", SelectedPath = GetOutputDir(false), ShowNewFolderButton = true })
            {
                if (dlg.ShowDialog() != WinForms.DialogResult.OK) return;
                OutputDirBox.Text = dlg.SelectedPath;
                _settings.OutputDir = dlg.SelectedPath;
                Persist();
            }
        }

        private async void BrowseFfmpeg_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "ffmpeg.exe|ffmpeg.exe|Chương trình (*.exe)|*.exe", Title = "Chọn ffmpeg.exe" };
            if (dlg.ShowDialog(this) != true) return;
            _settings.FfmpegPath = dlg.FileName;
            Persist();
            LocateFfmpeg();
            await ProbeEncodersAsync();
        }

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            if (_lastFile != null && File.Exists(_lastFile)) OpenWithShell(_lastFile);
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_lastFile != null && File.Exists(_lastFile))
                    Process.Start("explorer.exe", "/select," + ProcessUtil.Quote(_lastFile));
                else
                    Process.Start("explorer.exe", ProcessUtil.Quote(GetOutputDir(true)));
            }
            catch (Exception ex) { Log("Không mở được thư mục: " + ex.Message); }
        }

        private void OpenWithShell(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { Log("Không mở được file: " + ex.Message); }
        }

        // =====================================================================
        //  Nang cao
        // =====================================================================

        private static bool IsAdmin()
        {
            try
            {
                using (var id = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        private void UpdateAdminText()
        {
            bool admin = IsAdmin();
            AdminText.Text = admin
                ? "Đang chạy với quyền Administrator."
                : "Đang chạy quyền thường — đủ để ghi màn hình, loa và micro.";
            AdminButton.Visibility = admin ? Visibility.Collapsed : Visibility.Visible;
        }

        private void Admin_Click(object sender, RoutedEventArgs e)
        {
            if (_session != null) return;
            try
            {
                var exe = Process.GetCurrentProcess().MainModule?.FileName ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
                _settings.Save();
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
                Application.Current.Shutdown();
            }
            catch (Win32Exception) { Log("Đã hủy yêu cầu quyền Admin."); }
            catch (Exception ex) { Log("Không chạy lại được: " + ex.Message); }
        }

        private void SoundSettings_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start("control.exe", "mmsys.cpl sounds"); }
            catch (Exception ex) { Log(ex.Message); }
        }

        private void Probe_Click(object sender, RoutedEventArgs e) => _ = ProbeEncodersAsync();

        private void CopyCmd_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_lastCommandLine)) { Log("Chưa có lệnh FFmpeg nào (hãy ghi thử trước)."); return; }
            try { Clipboard.SetText(_lastCommandLine); Log("Đã sao chép lệnh FFmpeg vào clipboard."); }
            catch (Exception ex) { Log("Không sao chép được: " + ex.Message); }
        }

        private void ClearLog_Click(object sender, RoutedEventArgs e) => LogBox.Clear();

        // =====================================================================
        //  Hotkey, khay he thong, dong cua so
        // =====================================================================

        private void RegisterHotkeys()
        {
            if (_hwnd == IntPtr.Zero) return;
            uint mods = Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT;
            if (!Native.RegisterHotKey(_hwnd, HotkeyRecord, mods, (uint)KeyInterop.VirtualKeyFromKey(Key.R)))
                Log("Không đăng ký được phím tắt Ctrl+Alt+R (đang bị chương trình khác dùng).");
            if (!Native.RegisterHotKey(_hwnd, HotkeyMute, mods, (uint)KeyInterop.VirtualKeyFromKey(Key.M)))
                Log("Không đăng ký được phím tắt Ctrl+Alt+M (đang bị chương trình khác dùng).");
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id == HotkeyRecord) { _ = ToggleRecordingAsync(); handled = true; }
                else if (id == HotkeyMute) { ToggleMicMute(); handled = true; }
            }
            return IntPtr.Zero;
        }

        private void CreateTray()
        {
            try
            {
                _iconIdle = IconFactory.Create(false);
                _iconRec = IconFactory.Create(true);
                Icon = IconFactory.ToImageSource(_iconIdle);

                var menu = new WinForms.ContextMenuStrip();
                menu.Items.Add("Hiện cửa sổ", null, (s, e) => RestoreWindow());
                _trayRecordItem = new WinForms.ToolStripMenuItem("Bắt đầu ghi", null, async (s, e) => await ToggleRecordingAsync());
                menu.Items.Add(_trayRecordItem);
                menu.Items.Add("Tắt / bật tiếng micro", null, (s, e) => ToggleMicMute());
                menu.Items.Add(new WinForms.ToolStripSeparator());
                menu.Items.Add("Thoát", null, (s, e) => { RestoreWindow(); Close(); });

                _tray = new WinForms.NotifyIcon { Icon = _iconIdle, Text = "Screen Recorder", Visible = true, ContextMenuStrip = menu };
                _tray.DoubleClick += (s, e) => RestoreWindow();
            }
            catch (Exception ex) { Log("Không tạo được biểu tượng khay: " + ex.Message); }
        }

        private void RestoreWindow()
        {
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        }

        private void OnStateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized && _session != null && _settings.MinimizeToTray)
            {
                Hide();
                _tray?.ShowBalloonTip(2000, "Screen Recorder", "Đang ghi ở chế độ nền. Ctrl+Alt+R để dừng.", WinForms.ToolTipIcon.Info);
            }
            if (WindowState != WindowState.Minimized) RefreshPreview();
        }

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            if (_session != null || _busy)
            {
                e.Cancel = true;
                if (_closeAfterStop) return;
                var r = MessageBox.Show(this, "Đang ghi hình. Dừng ghi, lưu video rồi thoát?", "Screen Recorder",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r == MessageBoxResult.Yes)
                {
                    _closeAfterStop = true;
                    _ = StopRecordingAsync();
                }
                return;
            }
            _settings.Save();
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            _meterTimer.Stop();
            _previewTimer.Stop();
            _clockTimer.Stop();
            StopTestPlayer();
            _sysSource?.Dispose();
            _micSource?.Dispose();
            if (_hwnd != IntPtr.Zero)
            {
                Native.UnregisterHotKey(_hwnd, HotkeyRecord);
                Native.UnregisterHotKey(_hwnd, HotkeyMute);
            }
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
            }
        }

        // =====================================================================
        //  Tien ich
        // =====================================================================

        private void Persist()
        {
            if (_suppress) return;
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void Log(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.InvokeAsync(() => Log(message));
                return;
            }
            LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            if (LogBox.Text.Length > 300_000) LogBox.Text = LogBox.Text.Substring(LogBox.Text.Length - 150_000);
            LogBox.ScrollToEnd();
        }

        private static T Sel<T>(ComboBox combo, T fallback) => combo.SelectedItem is Choice<T> c ? c.Value : fallback;

        private static void SelectChoice<T>(ComboBox combo, T value)
        {
            foreach (var item in combo.Items)
            {
                if (item is Choice<T> c && EqualityComparer<T>.Default.Equals(c.Value, value))
                {
                    combo.SelectedItem = item;
                    return;
                }
            }
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1L << 30) return $"{bytes / (double)(1L << 30):0.00} GB";
            if (bytes >= 1L << 20) return $"{bytes / (double)(1L << 20):0.0} MB";
            if (bytes >= 1L << 10) return $"{bytes / 1024.0:0} KB";
            return bytes + " B";
        }

        private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max);

        private static string TrimLines(string text, int maxLines)
        {
            var lines = (text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(Environment.NewLine, lines.Skip(Math.Max(0, lines.Length - maxLines)));
        }
    }
}
