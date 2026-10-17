using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace ScreenRecorderApp
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += (s, a) => WriteCrashLog(a.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (s, a) => { WriteCrashLog(a.Exception); a.SetObserved(); };
            base.OnStartup(e);

            int idx = Array.FindIndex(e.Args, a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                int seconds = idx + 1 < e.Args.Length && int.TryParse(e.Args[idx + 1], out var parsed) ? parsed : 8;
                bool fake = Array.Exists(e.Args, a => a.Equals("--fake-video", StringComparison.OrdinalIgnoreCase));
                int code = await ScreenRecorderApp.Services.SelfTest.RunAsync(seconds, fake);
                Shutdown(code);
                return;
            }

            if (Array.Exists(e.Args, a => a.Equals("--playtest", StringComparison.OrdinalIgnoreCase)))
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                Shutdown(await ScreenRecorderApp.Services.SelfTest.RunPlayTestAsync());
                return;
            }

            var window = new MainWindow();
            MainWindow = window;
            window.Show();

            // --snapshot <file.png> [tabIndex]: chup giao dien ra anh PNG (chan doan giao dien)
            int snap = Array.FindIndex(e.Args, a => a.Equals("--snapshot", StringComparison.OrdinalIgnoreCase));
            if (snap >= 0 && snap + 1 < e.Args.Length)
            {
                string file = Path.GetFullPath(e.Args[snap + 1]);
                if (snap + 2 < e.Args.Length && int.TryParse(e.Args[snap + 2], out var tab)) window.Tabs.SelectedIndex = tab;
                await Task.Delay(4000);
                try { SaveSnapshot(window, file); } catch (Exception ex) { WriteCrashLog(ex); }
                window.Close();
            }
        }

        private static void SaveSnapshot(Window window, string file)
        {
            var content = (System.Windows.Media.Visual)window.Content;
            var fe = (FrameworkElement)window.Content;
            int w = (int)Math.Ceiling(fe.ActualWidth), h = (int)Math.Ceiling(fe.ActualHeight);
            var dv = new System.Windows.Media.DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(window.Background, null, new Rect(0, 0, w, h));
                dc.DrawRectangle(new System.Windows.Media.VisualBrush(content)
                {
                    Stretch = System.Windows.Media.Stretch.None,
                    AlignmentX = System.Windows.Media.AlignmentX.Left,
                    AlignmentY = System.Windows.Media.AlignmentY.Top,
                    ViewboxUnits = System.Windows.Media.BrushMappingMode.Absolute,
                    Viewbox = new Rect(0, 0, w, h),
                }, null, new Rect(0, 0, w, h));
            }
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(dv);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            using (var fs = File.Create(file)) enc.Save(fs);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            WriteCrashLog(e.Exception);
            MessageBox.Show("Đã xảy ra lỗi không mong muốn:\n\n" + e.Exception.Message,
                "Screen Recorder", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private static void WriteCrashLog(Exception? ex)
        {
            if (ex == null) return;
            try
            {
                File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ScreenRecorder-error.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\r\n\r\n");
            }
            catch { }
        }
    }
}
