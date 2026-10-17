using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using WinForms = System.Windows.Forms;
using WpfPixelFormats = System.Windows.Media.PixelFormats;

namespace ScreenRecorderApp.Services
{
    public sealed class MonitorInfo : System.ComponentModel.INotifyPropertyChanged
    {
        public int Number { get; set; }
        public string DeviceName { get; set; } = "";
        public bool Primary { get; set; }
        public Rectangle Bounds { get; set; }
        public int? DxgiOutputIndex { get; set; }
        public bool IsAllScreens { get; set; }

        public string Key => IsAllScreens ? "ALL" : DeviceName;

        /// <summary>Ten ngan hien tren the chon man hinh.</summary>
        public string Title => IsAllScreens ? "Tất cả màn hình" : $"Màn hình {Number}" + (Primary ? " (chính)" : "");
        public string Subtitle => $"{Bounds.Width}×{Bounds.Height}";
        public string Badge => IsAllScreens ? "★" : Number.ToString();

        private BitmapSource? _thumbnail;
        /// <summary>Anh thu nho cua man hinh (cap nhat dinh ky).</summary>
        public BitmapSource? Thumbnail
        {
            get => _thumbnail;
            set
            {
                _thumbnail = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Thumbnail)));
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        public override string ToString() => IsAllScreens
            ? $"Tất cả màn hình  —  {Bounds.Width}×{Bounds.Height}"
            : $"Màn hình {Number}  —  {Bounds.Width}×{Bounds.Height}{(Primary ? "  (chính)" : "")}";
    }

    internal static class MonitorService
    {
        /// <summary>Danh sach man hinh (toa do pixel vat ly). Man hinh chinh luon la so 1.</summary>
        public static List<MonitorInfo> GetMonitors()
        {
            var dxgi = GetDxgiOutputMap();
            var screens = WinForms.Screen.AllScreens
                .OrderBy(s => s.Primary ? 0 : 1).ThenBy(s => s.Bounds.X).ThenBy(s => s.Bounds.Y)
                .ToList();

            var list = new List<MonitorInfo>();
            for (int i = 0; i < screens.Count; i++)
            {
                var s = screens[i];
                list.Add(new MonitorInfo
                {
                    Number = i + 1,
                    DeviceName = s.DeviceName,
                    Primary = s.Primary,
                    Bounds = s.Bounds,
                    DxgiOutputIndex = dxgi.TryGetValue(s.DeviceName, out var idx) ? idx : (int?)null,
                });
            }

            if (list.Count > 1)
            {
                var union = list.Select(m => m.Bounds).Aggregate(Rectangle.Union);
                list.Add(new MonitorInfo { Number = 0, DeviceName = "ALL", IsAllScreens = true, Bounds = union });
            }
            return list;
        }

        // ------------------------------------------------------------------ DXGI (cho ddagrab)
        // ddagrab dung chi so output cua adapter 0 -> anh xa "\\.\DISPLAYx" -> output_idx.

        [DllImport("dxgi.dll")]
        private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int EnumDelegate(IntPtr self, uint index, out IntPtr result);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetDescDelegate(IntPtr self, out DXGI_OUTPUT_DESC desc);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint ReleaseDelegate(IntPtr self);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DXGI_OUTPUT_DESC
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            public int Left, Top, Right, Bottom;
            public int AttachedToDesktop;
            public int Rotation;
            public IntPtr Monitor;
        }

        private static T VTable<T>(IntPtr comObject, int slot) where T : Delegate
        {
            IntPtr vtable = Marshal.ReadIntPtr(comObject);
            IntPtr fn = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
            return Marshal.GetDelegateForFunctionPointer<T>(fn);
        }

        private static void Release(IntPtr comObject)
        {
            if (comObject != IntPtr.Zero) VTable<ReleaseDelegate>(comObject, 2)(comObject);
        }

        private static Dictionary<string, int> GetDxgiOutputMap()
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            IntPtr factory = IntPtr.Zero, adapter = IntPtr.Zero;
            try
            {
                var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387"); // IDXGIFactory1
                if (CreateDXGIFactory1(ref iid, out factory) < 0) return map;
                // IDXGIFactory::EnumAdapters = slot 7
                if (VTable<EnumDelegate>(factory, 7)(factory, 0, out adapter) < 0) return map;
                var enumOutputs = VTable<EnumDelegate>(adapter, 7); // IDXGIAdapter::EnumOutputs
                for (uint i = 0; i < 16; i++)
                {
                    if (enumOutputs(adapter, i, out IntPtr output) < 0) break;
                    try
                    {
                        // IDXGIOutput::GetDesc = slot 7
                        if (VTable<GetDescDelegate>(output, 7)(output, out var desc) >= 0 && !string.IsNullOrEmpty(desc.DeviceName))
                            map[desc.DeviceName] = (int)i;
                    }
                    finally { Release(output); }
                }
            }
            catch { /* DXGI khong co -> chi dung gdigrab */ }
            finally
            {
                Release(adapter);
                Release(factory);
            }
            return map;
        }
    }

    internal static class ScreenCapture
    {
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr hdc, int mode);
        [DllImport("gdi32.dll")] private static extern bool SetBrushOrgEx(IntPtr hdc, int x, int y, IntPtr lppt);
        [DllImport("gdi32.dll")]
        private static extern bool StretchBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
                                              IntPtr hdcSrc, int xSrc, int ySrc, int wSrc, int hSrc, int rop);
        private const int HALFTONE = 4, SRCCOPY = 0x00CC0020;

        /// <summary>
        /// Chup anh xem truoc cua mot vung man hinh (dung cho preview, khong dung de ghi).
        /// Thu nho truc tiep bang StretchBlt (HALFTONE) -> nhanh, it ton bo nho.
        /// </summary>
        public static BitmapSource? CaptureThumbnail(Rectangle area, int maxWidth, int maxHeight = int.MaxValue)
        {
            if (area.Width <= 0 || area.Height <= 0) return null;
            try
            {
                double scale = Math.Min(1.0, Math.Min(maxWidth / (double)area.Width, maxHeight / (double)area.Height));
                int w = Math.Max(1, (int)(area.Width * scale));
                int h = Math.Max(1, (int)(area.Height * scale));
                using (var small = new Bitmap(w, h, PixelFormat.Format32bppRgb))
                {
                    using (var g = Graphics.FromImage(small))
                    {
                        IntPtr dst = g.GetHdc();
                        IntPtr src = GetDC(IntPtr.Zero);
                        try
                        {
                            SetStretchBltMode(dst, HALFTONE);
                            SetBrushOrgEx(dst, 0, 0, IntPtr.Zero);
                            if (!StretchBlt(dst, 0, 0, w, h, src, area.X, area.Y, area.Width, area.Height, SRCCOPY))
                                return null;
                        }
                        finally
                        {
                            ReleaseDC(IntPtr.Zero, src);
                            g.ReleaseHdc(dst);
                        }
                    }
                    var data = small.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                    try
                    {
                        var bs = BitmapSource.Create(w, h, 96, 96, WpfPixelFormats.Bgr32, null, data.Scan0, data.Stride * h, data.Stride);
                        bs.Freeze();
                        return bs;
                    }
                    finally { small.UnlockBits(data); }
                }
            }
            catch
            {
                return null; // vd: man hinh khoa / UAC
            }
        }
    }
}
