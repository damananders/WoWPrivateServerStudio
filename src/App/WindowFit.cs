using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AndersCraftStudio
{
    /// <summary>
    /// Borderless windows (WindowStyle None + WindowChrome) maximize over the whole monitor, under the taskbar, so whatever
    /// sits at the bottom edge can't be reached. This answers WM_GETMINMAXINFO with the work area of the monitor the window
    /// is on (taskbar excluded, any monitor, any taskbar side) and keeps the window's own minimum size.
    /// </summary>
    public static class WindowFit
    {
        const int WM_GETMINMAXINFO = 0x0024;
        const uint MONITOR_DEFAULTTONEAREST = 2;

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct MINMAXINFO { public POINT Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct MONITORINFO { public int Size; public RECT Monitor, Work; public uint Flags; }

        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

        /// <summary>Hooks the window (now, or once it has a handle).</summary>
        public static void Apply(Window w)
        {
            void Hook()
            {
                var src = HwndSource.FromHwnd(new WindowInteropHelper(w).Handle);
                src?.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                {
                    if (msg == WM_GETMINMAXINFO) { Fill(w, hwnd, lParam); handled = true; }
                    return IntPtr.Zero;
                });
            }
            if (new WindowInteropHelper(w).Handle != IntPtr.Zero) Hook(); else w.SourceInitialized += (s, e) => Hook();
        }

        static void Fill(Window w, IntPtr hwnd, IntPtr lParam)
        {
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            var mon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
            if (mon != IntPtr.Zero && GetMonitorInfo(mon, ref info))
            {
                mmi.MaxPosition.X = info.Work.Left - info.Monitor.Left;
                mmi.MaxPosition.Y = info.Work.Top - info.Monitor.Top;
                mmi.MaxSize.X = info.Work.Right - info.Work.Left;
                mmi.MaxSize.Y = info.Work.Bottom - info.Work.Top;
                mmi.MaxTrackSize = mmi.MaxSize;
                // the window's minimum size, in device pixels
                var m = PresentationSource.FromVisual(w)?.CompositionTarget?.TransformToDevice ?? System.Windows.Media.Matrix.Identity;
                if (w.MinWidth > 0) mmi.MinTrackSize.X = (int)Math.Ceiling(w.MinWidth * m.M11);
                if (w.MinHeight > 0) mmi.MinTrackSize.Y = (int)Math.Ceiling(w.MinHeight * m.M22);
                // never ask for more than the work area (small screens): the window then fits and scrolls inside
                mmi.MinTrackSize.X = Math.Min(mmi.MinTrackSize.X, mmi.MaxSize.X);
                mmi.MinTrackSize.Y = Math.Min(mmi.MinTrackSize.Y, mmi.MaxSize.Y);
            }
            Marshal.StructureToPtr(mmi, lParam, true);
        }
    }
}
