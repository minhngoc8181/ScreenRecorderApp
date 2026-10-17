using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenRecorderApp.Services;
using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;
using WpfPen = System.Windows.Media.Pen;
using LinearGradientBrush = System.Windows.Media.LinearGradientBrush;
using GdiGradientBrush = System.Drawing.Drawing2D.LinearGradientBrush;

namespace ScreenRecorderApp.Controls
{
    /// <summary>Thanh do muc am kieu LED (thang dB -60..0), co vach giu dinh.</summary>
    public sealed class LevelMeter : FrameworkElement
    {
        private static readonly WpfBrush Track = Frozen(new SolidColorBrush(WpfColor.FromRgb(0x3A, 0x41, 0x57)));
        private static readonly WpfBrush Gap = Frozen(new SolidColorBrush(WpfColor.FromRgb(0x25, 0x2A, 0x39)));
        private static readonly WpfBrush PeakBrush = Frozen(new SolidColorBrush(WpfColor.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)));
        private LinearGradientBrush? _fill;
        private double _fillWidth;
        private double _level;
        private double _peak;
        private DateTime _peakTime;

        private static WpfBrush Frozen(WpfBrush b) { b.Freeze(); return b; }

        public void SetLevel(float linear)
        {
            double db = linear > 1e-6 ? 20 * Math.Log10(linear) : -120;
            double norm = Math.Max(0, Math.Min(1, (db + 60) / 60));
            _level = norm > _level ? norm : Math.Max(norm, _level - 0.035);
            var now = DateTime.UtcNow;
            if (_level >= _peak || (now - _peakTime).TotalMilliseconds > 1200)
            {
                _peak = _level;
                _peakTime = now;
            }
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            double r = Math.Min(3, h / 2);
            dc.DrawRoundedRectangle(Track, null, new Rect(0, 0, w, h), r, r);

            if (_fill == null || Math.Abs(_fillWidth - w) > 0.5)
            {
                _fill = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(w, 0) };
                _fill.GradientStops.Add(new GradientStop(WpfColor.FromRgb(0x2B, 0xD9, 0xA0), 0));
                _fill.GradientStops.Add(new GradientStop(WpfColor.FromRgb(0x4F, 0xC3, 0xFF), 0.55));
                _fill.GradientStops.Add(new GradientStop(WpfColor.FromRgb(0xFF, 0xC9, 0x4D), 0.82));
                _fill.GradientStops.Add(new GradientStop(WpfColor.FromRgb(0xFF, 0x4D, 0x6A), 1));
                _fill.Freeze();
                _fillWidth = w;
            }

            if (_level > 0.002)
            {
                dc.PushClip(new RectangleGeometry(new Rect(0, 0, w * _level, h)));
                dc.DrawRoundedRectangle(_fill, null, new Rect(0, 0, w, h), r, r);
                dc.Pop();
            }
            for (double x = 4; x < w; x += 5) dc.DrawRectangle(Gap, null, new Rect(x, 0, 1, h));
            if (_peak > 0.01) dc.DrawRectangle(PeakBrush, null, new Rect(Math.Max(0, w * _peak - 2), 0, 2, h));
        }
    }

    internal static class IconFactory
    {
        /// <summary>Ve icon tron: dang ghi = cham do day; binh thuong = vong tim + cham do.</summary>
        public static Icon Create(bool recording)
        {
            using (var bmp = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(System.Drawing.Color.Transparent);
                    if (recording)
                    {
                        using (var b = new SolidBrush(System.Drawing.Color.FromArgb(255, 255, 61, 99))) g.FillEllipse(b, 2, 2, 28, 28);
                        using (var b = new SolidBrush(System.Drawing.Color.White)) g.FillRectangle(b, 11, 11, 10, 10);
                    }
                    else
                    {
                        using (var lg = new GdiGradientBrush(new Rectangle(0, 0, 32, 32),
                                   System.Drawing.Color.FromArgb(255, 139, 108, 255), System.Drawing.Color.FromArgb(255, 79, 195, 255), 45f))
                            g.FillEllipse(lg, 1, 1, 30, 30);
                        using (var b = new SolidBrush(System.Drawing.Color.FromArgb(255, 21, 25, 36))) g.FillEllipse(b, 6, 6, 20, 20);
                        using (var b = new SolidBrush(System.Drawing.Color.FromArgb(255, 255, 61, 99))) g.FillEllipse(b, 10, 10, 12, 12);
                    }
                }
                IntPtr h = bmp.GetHicon();
                try
                {
                    using (var tmp = Icon.FromHandle(h)) return (Icon)tmp.Clone();
                }
                finally { Native.DestroyIcon(h); }
            }
        }

        public static ImageSource ToImageSource(Icon icon)
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
    }
}
