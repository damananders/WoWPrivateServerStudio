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

        /// <summary>The colour scheme in use.</summary>
        public static StudioScheme Scheme { get; private set; } = StudioScheme.Default;
        static ResourceDictionary current;

        /// <summary>Merges the design system (in a colour scheme) into the app and switches motion off when Windows asks for reduced motion.</summary>
        public static void Apply(Application app, string schemeId = null)
        {
            Scheme = StudioScheme.ById(schemeId);
            current = Build(Scheme);
            app.Resources.MergedDictionaries.Add(current);
            app.Resources["Shimmer.Visibility"] = MotionEnabled ? Visibility.Visible : Visibility.Collapsed;   // primary buttons' light sweep
            if (!MotionEnabled)
            {
                app.Resources["Motion.Fast"] = new Duration(TimeSpan.Zero);
                app.Resources["Motion.Medium"] = new Duration(TimeSpan.Zero);
            }
        }

        /// <summary>
        /// Swaps the whole look to another scheme. Everything created from now on uses it; windows that are already open
        /// keep the brushes they were built with, so the app re-opens its main window after this (Studio does it for you).
        /// </summary>
        public static void SetScheme(Application app, string schemeId)
        {
            var s = StudioScheme.ById(schemeId);
            var d = Build(s);
            int i = current == null ? -1 : app.Resources.MergedDictionaries.IndexOf(current);
            if (i >= 0) app.Resources.MergedDictionaries[i] = d; else app.Resources.MergedDictionaries.Add(d);
            current = d; Scheme = s;
        }

        /// <summary>The design system's dictionary in <paramref name="s"/>: the compiled one for the default, else its XAML re-coloured and loaded.</summary>
        public static ResourceDictionary Build(StudioScheme s)
        {
            if (s.IsDefault) return new ResourceDictionary { Source = Dictionary };
            string Read(string n) { using var st = typeof(StudioLook).Assembly.GetManifestResourceStream("Studio.DesignSystem." + n); using var r = new System.IO.StreamReader(st); return r.ReadToEnd(); }
            return (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(Compose(s.Recolor(Read("StudioDesignSystem.xaml")), s.Recolor(Read("Tokens.xaml"))));
        }

        /// <summary>One dictionary from the two files: the token entries go in place of the merged Tokens.xaml (the design system's own keys win).</summary>
        public static string Compose(string ds, string tokens)
        {
            var rx = new System.Text.RegularExpressions.Regex(@"x:Key=""([^""]+)""");
            var dsKeys = new System.Collections.Generic.HashSet<string>();
            int dsBody = ds.IndexOf("</ResourceDictionary.MergedDictionaries>", StringComparison.Ordinal);
            foreach (System.Text.RegularExpressions.Match m in rx.Matches(ds.Substring(dsBody))) dsKeys.Add(m.Groups[1].Value);
            int tStart = tokens.IndexOf('>', tokens.IndexOf("<ResourceDictionary", StringComparison.Ordinal)) + 1, tEnd = tokens.LastIndexOf("</ResourceDictionary>", StringComparison.Ordinal);
            var body = tokens.Substring(tStart, tEnd - tStart);
            // drop token entries the design system defines itself (top-level elements only)
            var keep = new System.Text.StringBuilder();
            int pos = 0;
            var top = new System.Text.RegularExpressions.Regex(@"\n    <([\w.:]+)\s[^>]*?x:Key=""([^""]+)""[^>]*?(/?)>");
            foreach (System.Text.RegularExpressions.Match m in top.Matches(body))
            {
                if (m.Index < pos) continue;
                int end = m.Index + m.Length;
                if (m.Groups[3].Value != "/") { var close = "</" + m.Groups[1].Value + ">"; int c = body.IndexOf(close, end, StringComparison.Ordinal); if (c > 0) end = c + close.Length; }
                if (dsKeys.Contains(m.Groups[2].Value)) { keep.Append(body, pos, m.Index - pos); pos = end; }
            }
            keep.Append(body, pos, body.Length - pos);
            int mStart = ds.IndexOf("<ResourceDictionary.MergedDictionaries>", StringComparison.Ordinal);
            int mEnd = dsBody + "</ResourceDictionary.MergedDictionaries>".Length;
            var outp = ds.Substring(0, mStart) + keep.ToString() + ds.Substring(mEnd);
            if (!outp.Contains("xmlns:sys=")) outp = outp.Replace("xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"", "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:sys=\"clr-namespace:System;assembly=System.Runtime\"");
            return outp;
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
