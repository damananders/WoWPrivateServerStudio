using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Studio.DesignSystem
{
    /// <summary>
    /// Runtime side of the Studio design system: motion that honours the Windows "animation effects" setting,
    /// a dark title bar that matches the app, and the Windows 11 Mica backdrop where it's available.
    /// </summary>
    public static class StudioLook
    {
        public static readonly Uri Dictionary = new("pack://application:,,,/Studio.DesignSystem;component/StudioDesignSystem.xaml");

        /// <summary>False when the person turned off animation effects in Windows (Settings › Accessibility › Visual effects).</summary>
        /// <summary>Raised with false when the main window is minimized or loses focus (animations pause), true when it's back.</summary>
        public static event Action<bool> Running;

        public static bool MotionEnabled => SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast && Environment.GetEnvironmentVariable("STUDIO_REDUCED_MOTION") != "1";

        /// <summary>Merges the design system into the app and switches motion off when Windows asks for reduced motion.</summary>
        public static void Apply(Application app)
        {
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = Dictionary });
            app.Resources["Shimmer.Visibility"] = MotionEnabled ? Visibility.Visible : Visibility.Collapsed;   // primary buttons' light sweep
            if (!MotionEnabled)
            {
                app.Resources["Motion.Fast"] = new Duration(TimeSpan.Zero);
                app.Resources["Motion.Medium"] = new Duration(TimeSpan.Zero);
            }
        }

        /// <summary>Fades a property to a value over the design system's motion time (instant with reduced motion).</summary>
        public static void Fade(UIElement el, DependencyProperty prop, double to, int ms = 160)
        {
            if (!MotionEnabled) { el.BeginAnimation(prop, null); el.SetValue(prop, to); return; }
            el.BeginAnimation(prop, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        /// <summary>Fades a glow (drop shadow) in or out; instant with reduced motion.</summary>
        public static void FadeEffect(System.Windows.Media.Effects.DropShadowEffect fx, double to, int ms = 120)
        {
            if (!MotionEnabled) { fx.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, null); fx.Opacity = to; return; }
            fx.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        /// <summary>Animates a solid background colour (hover tone shifts).</summary>
        public static void Tint(System.Windows.Controls.Border b, Color to, int ms = 140)
        {
            if (b.Background is not SolidColorBrush cur || cur.IsFrozen) { cur = new SolidColorBrush((b.Background as SolidColorBrush)?.Color ?? to); b.Background = cur; }
            if (!MotionEnabled) { cur.BeginAnimation(SolidColorBrush.ColorProperty, null); cur.Color = to; return; }
            cur.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS m);
        [StructLayout(LayoutKind.Sequential)] struct MARGINS { public int L, R, T, B; }

        /// <summary>
        /// Dark title bar in the app's colour, and Mica behind the window on Windows 11 22H2+ (build 22621).
        /// The window's own background is made slightly translucent so the Mica tint shows through the gaps between panels.
        /// Returns true when Mica is on.
        /// </summary>
        public static bool Window(Window w, Color baseColor)
        {
            bool mica = false;
            void Go()
            {
                var h = new WindowInteropHelper(w).Handle;
                if (h == IntPtr.Zero) return;
                int on = 1;
                DwmSetWindowAttribute(h, 20, ref on, 4);                    // DWMWA_USE_IMMERSIVE_DARK_MODE
                int caption = baseColor.R | baseColor.G << 8 | baseColor.B << 16;
                DwmSetWindowAttribute(h, 35, ref caption, 4);               // DWMWA_CAPTION_COLOR
                int border = caption;
                DwmSetWindowAttribute(h, 34, ref border, 4);                // DWMWA_BORDER_COLOR
                if (Environment.OSVersion.Version.Build >= 22621 && !SystemParameters.HighContrast)
                {
                    var src = HwndSource.FromHwnd(h);
                    if (src?.CompositionTarget != null) src.CompositionTarget.BackgroundColor = Colors.Transparent;
                    var m = new MARGINS { L = -1, R = -1, T = -1, B = -1 };
                    DwmExtendFrameIntoClientArea(h, ref m);
                    int backdrop = 2;                                       // DWMSBT_MAINWINDOW (Mica)
                    mica = DwmSetWindowAttribute(h, 38, ref backdrop, 4) == 0;
                    if (mica) w.Background = new SolidColorBrush(Color.FromArgb(0xE8, baseColor.R, baseColor.G, baseColor.B));
                }
            }
            if (new WindowInteropHelper(w).Handle != IntPtr.Zero) Go(); else w.SourceInitialized += (s, e) => Go();
            void Run() { bool on = w.IsActive && w.WindowState != WindowState.Minimized; Running?.Invoke(on); Glow.SetRunning(on); }
            w.Activated += (s, e) => Run();
            w.Deactivated += (s, e) => Run();
            w.StateChanged += (s, e) => Run();
            return mica;
        }
    }
}
