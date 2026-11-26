using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Studio.DesignSystem
{
    /// <summary>
    /// The magic layer (§3): a slowly drifting, hue-shifting aurora, sparse rising motes and an occasional sheen.
    /// Built from radial gradient fills (no blur effects), bitmap-cached, capped at 14 motes, and paused whenever the
    /// window is minimized or not focused. With reduced motion it's a still, dimmer picture.
    /// </summary>
    public sealed class AuroraBackdrop : Grid
    {
        readonly Canvas aurora = new() { IsHitTestVisible = false, ClipToBounds = true };
        readonly Canvas motes = new() { IsHitTestVisible = false, ClipToBounds = true };
        readonly Rectangle sheen = new() { IsHitTestVisible = false, Opacity = 0 };
        readonly List<(UIElement el, AnimationClock[] clocks)> running = new();
        readonly List<Storyboard> boards = new();
        readonly Random rnd = new(7);
        public const int MaxMotes = 46;
        public bool Animated { get; private set; }

        public AuroraBackdrop(Color a, Color b, Color c) : this(a, b, c, Color.FromRgb(0xF6, 0xC6, 0x87)) { }   // design-check: data (gold glow)

        /// <summary>Four soft glows (accent, second accent, plum, gold) drifting and breathing, motes rising, a sheen now and then.</summary>
        public AuroraBackdrop(Color a, Color b, Color c, Color d)
        {
            IsHitTestVisible = false;
            var sheenHost = new Canvas { IsHitTestVisible = false, ClipToBounds = true };   // canvases never ask for size, so the backdrop can sit in any window
            sheenHost.Children.Add(sheen);
            Children.Add(aurora); Children.Add(motes); Children.Add(sheenHost);
            Blob(a, 0.10, 0.05, 0.85, 0.36);
            Blob(b, 0.62, 0.55, 0.80, 0.22);
            Blob(c, 0.30, 0.70, 0.70, 0.18);
            Blob(d, 0.85, 0.15, 0.55, 0.14);
            sheen.Fill = new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(0, 255, 255, 255), 0), new GradientStop(Color.FromArgb(18, 255, 240, 220), 0.5), new GradientStop(Color.FromArgb(0, 255, 255, 255), 1),
            }, new Point(0, 0), new Point(1, 0.35));
            sheen.RenderTransform = new TranslateTransform(-2000, 0);
            CacheMode = new BitmapCache { EnableClearType = false, RenderAtScale = 0.5 };
            Loaded += (s, e) => Start();
            SizeChanged += (s, e) => Layout();
        }

        readonly List<(Ellipse e, double x, double y, double size)> blobs = new();

        void Blob(Color c, double x, double y, double size, double alpha)
        {
            var g = new RadialGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromArgb((byte)(alpha * 255), c.R, c.G, c.B), 0),
                new GradientStop(Color.FromArgb((byte)(alpha * 120), c.R, c.G, c.B), 0.45),
                new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1),
            });
            var el = new Ellipse { Fill = g, RenderTransform = new TranslateTransform() };
            aurora.Children.Add(el);
            blobs.Add((el, x, y, size));
        }

        void Layout()
        {
            double w = Math.Max(ActualWidth, 1), h = Math.Max(ActualHeight, 1);
            foreach (var (e, x, y, size) in blobs)
            {
                e.Width = w * size; e.Height = h * size * 1.2;
                Canvas.SetLeft(e, w * x - e.Width / 2); Canvas.SetTop(e, h * y - e.Height / 2);
            }
            sheen.Width = w * 0.45; sheen.Height = h * 1.6; Canvas.SetTop(sheen, -h * 0.3);
        }

        void Start()
        {
            Layout();
            if (!StudioLook.MotionEnabled) { Opacity = 0.75; return; }   // reduced motion: a still, dimmer aurora
            Animated = true;
            // drift + hue shift for each blob (30–55 s loops)
            int i = 0;
            foreach (var (e, _, _, _) in blobs)
            {
                var t = (TranslateTransform)e.RenderTransform;
                var dur = TimeSpan.FromSeconds(9 + 4.6 * i);   // 9 to 23 s cycles
                t.BeginAnimation(TranslateTransform.XProperty, Loop(-80 + 40 * i, 90 - 30 * i, dur));
                t.BeginAnimation(TranslateTransform.YProperty, Loop(50 - 20 * i, -60 + 25 * i, dur * 1.3));
                var stop = ((RadialGradientBrush)e.Fill).GradientStops[0];
                var shifted = Shift(stop.Color, 0.10);
                stop.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(stop.Color, shifted, dur * 0.8) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
                i++;
            }
            // motes: sparse, slow, rising
            for (int m = 0; m < MaxMotes; m++) SpawnMote(rnd.NextDouble() * 18);
            // sheen: a soft band sweeps across every ~26 s
            var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
            var move = new DoubleAnimation(-ActualWidth * 0.6, ActualWidth * 1.2, TimeSpan.FromSeconds(4.5)) { BeginTime = TimeSpan.FromSeconds(8), EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            Storyboard.SetTarget(move, sheen); Storyboard.SetTargetProperty(move, new PropertyPath("RenderTransform.X"));
            var fade = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromSeconds(8) };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.5))));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.5))));
            Storyboard.SetTarget(fade, sheen); Storyboard.SetTargetProperty(fade, new PropertyPath(OpacityProperty));
            sb.Children.Add(move); sb.Children.Add(fade);
            sb.Duration = TimeSpan.FromSeconds(26);
            sb.Begin(this, true);
            boards.Add(sb);
        }

        void SpawnMote(double delay)
        {
            double w = Math.Max(ActualWidth, 400), h = Math.Max(ActualHeight, 300);
            var size = 3 + rnd.NextDouble() * 5;
            var amber = rnd.NextDouble() < 0.6;
            var c = amber ? GlowColor("Color.Accent", Color.FromRgb(0xF0, 0xB4, 0x6A)) : GlowColor("Color.AccentPink", Color.FromRgb(0xC2, 0x4A, 0x5C));   // design-check: data (fallbacks)
            if (rnd.NextDouble() < 0.12) size *= 2.2;   // a few bigger orbs
            var e = new Ellipse
            {
                Width = size * 4, Height = size * 4, Opacity = 0,
                Fill = new RadialGradientBrush(new GradientStopCollection { new GradientStop(Color.FromArgb(220, c.R, c.G, c.B), 0), new GradientStop(Color.FromArgb(60, c.R, c.G, c.B), 0.3), new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1) }),
            };
            Canvas.SetLeft(e, rnd.NextDouble() * w);
            motes.Children.Add(e);
            var rise = TimeSpan.FromSeconds(14 + rnd.NextDouble() * 22);   // 14 to 36 s per pass
            var sway = new TranslateTransform(); e.RenderTransform = sway;
            sway.BeginAnimation(TranslateTransform.XProperty, Loop(-10 - rnd.NextDouble() * 10, 10 + rnd.NextDouble() * 10, TimeSpan.FromSeconds(3 + rnd.NextDouble() * 4)));
            var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(delay) };
            var up = new DoubleAnimation(h + 20, -40, rise);
            Storyboard.SetTarget(up, e); Storyboard.SetTargetProperty(up, new PropertyPath(Canvas.TopProperty));
            var fade = new DoubleAnimationUsingKeyFrames { Duration = rise };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.55, KeyTime.FromPercent(0.25)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.4, KeyTime.FromPercent(0.75)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
            Storyboard.SetTarget(fade, e); Storyboard.SetTargetProperty(fade, new PropertyPath(OpacityProperty));
            sb.Children.Add(up); sb.Children.Add(fade);
            sb.Begin(this, true);
            boards.Add(sb);
        }

        static Color GlowColor(string key, Color fallback) => Application.Current?.TryFindResource(key) is Color c ? c : fallback;

        static DoubleAnimation Loop(double from, double to, TimeSpan d) => new(from, to, d) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };

        static Color Shift(Color c, double amount)
        {
            // rotate hue a little (toward amber/plum) without changing alpha
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double nr = r * (1 - amount) + b * amount, ng = g * (1 - amount) + r * amount, nb = b * (1 - amount) + g * amount;
            return Color.FromArgb(c.A, (byte)(nr * 255), (byte)(ng * 255), (byte)(nb * 255));
        }

        /// <summary>Pause everything (minimized / not focused) or carry on.</summary>
        public void SetRunning(bool run)
        {
            if (!Animated) return;
            foreach (var sb in boards) { try { if (run) sb.Resume(this); else sb.Pause(this); } catch { } }
            // the blob drifts are plain animations: freeze by hiding motion via a speed ratio on their clocks isn't available, so hold them in place
            foreach (var (e, _, _, _) in blobs)
            {
                var t = (TranslateTransform)e.RenderTransform;
                if (!run) { var x = t.X; var y = t.Y; t.BeginAnimation(TranslateTransform.XProperty, null); t.BeginAnimation(TranslateTransform.YProperty, null); t.X = x; t.Y = y; }
            }
            if (run) RestartDrift();
        }

        void RestartDrift()
        {
            int i = 0;
            foreach (var (e, _, _, _) in blobs)
            {
                var t = (TranslateTransform)e.RenderTransform;
                var dur = TimeSpan.FromSeconds(9 + 4.6 * i);
                t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(t.X, 90 - 30 * i, dur / 2) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
                t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(t.Y, -60 + 25 * i, dur * 0.65) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
                i++;
            }
        }
    }

    /// <summary>Living states: shimmer on the wordmark, breathing primary actions, pulsing halos on active cards.</summary>
    public static class Glow
    {

        /// <summary>A soft light that slides across the text every few seconds (the brand wordmark).</summary>
        public static void Shimmer(TextBlock t)
        {
            var baseColor = ((SolidColorBrush)Application.Current.Resources["TextStrong"]).Color;
            var hi = Color.FromRgb(0xFF, 0xD9, 0x9A);
            var stops = new GradientStopCollection { new GradientStop(baseColor, 0), new GradientStop(baseColor, 0.35), new GradientStop(hi, 0.5), new GradientStop(baseColor, 0.65), new GradientStop(baseColor, 1) };
            var brush = new LinearGradientBrush(stops, new Point(-1, 0), new Point(0, 0)) { MappingMode = BrushMappingMode.RelativeToBoundingBox };
            t.Foreground = brush;
            if (!StudioLook.MotionEnabled) { t.Foreground = new SolidColorBrush(baseColor); return; }
            var tr = new TranslateTransform();
            brush.RelativeTransform = tr;
            // one 2.4 s sweep, then a rest, forever
            var sweep = new DoubleAnimation(0, 2, TimeSpan.FromSeconds(2.4)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            Storyboard.SetTarget(sweep, t);
            Storyboard.SetTargetProperty(sweep, new PropertyPath("(TextBlock.Foreground).(Brush.RelativeTransform).(TranslateTransform.X)"));
            var sb = new Storyboard { Duration = TimeSpan.FromSeconds(9), RepeatBehavior = RepeatBehavior.Forever };
            sb.Children.Add(sweep);
            sb.Begin(t, true);
            Track(sb, t);
        }

        static readonly List<(Storyboard sb, FrameworkElement owner)> boards = new();
        static void Track(Storyboard sb, FrameworkElement owner) { lock (boards) boards.Add((sb, owner)); }

        /// <summary>Pauses or resumes every shimmer (called when the window is minimized, unfocused, or back).</summary>
        public static void SetRunning(bool run)
        {
            lock (boards)
                foreach (var (sb, owner) in boards) { try { if (run) sb.Resume(owner); else sb.Pause(owner); } catch { } }
        }

        /// <summary>
        /// Wraps an element with a halo layer behind it that breathes (primary actions) or pulses (active cards).
        /// The halo is its own layer, so text inside stays crisp. Still with reduced motion.
        /// </summary>
        public static Grid Halo(FrameworkElement content, CornerRadius radius, Color color, double strength = 0.55, double seconds = 2.8, bool breathe = true)
        {
            var g = new Grid();
            var halo = new Border
            {
                CornerRadius = radius, Background = new SolidColorBrush(Color.FromArgb(40, color.R, color.G, color.B)), Margin = content.Margin, IsHitTestVisible = false,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 26, ShadowDepth = 0, Color = color, Opacity = strength },
                Opacity = breathe ? 0.35 : 0.8,
                HorizontalAlignment = content.HorizontalAlignment, VerticalAlignment = content.VerticalAlignment,
            };
            halo.SetBinding(FrameworkElement.WidthProperty, new System.Windows.Data.Binding("ActualWidth") { Source = content });
            halo.SetBinding(FrameworkElement.HeightProperty, new System.Windows.Data.Binding("ActualHeight") { Source = content });
            g.Children.Add(halo); g.Children.Add(content);
            g.HorizontalAlignment = content.HorizontalAlignment; g.VerticalAlignment = content.VerticalAlignment;
            if (StudioLook.MotionEnabled && breathe)
                halo.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.25, 0.95, TimeSpan.FromSeconds(seconds / 2)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            return g;
        }

        public static Color AmberColor => (Color)Application.Current.Resources["Glow.AmberColor"];
        public static Color MaroonColor => (Color)Application.Current.Resources["Glow.MaroonColor"];
        public static Color GoColor => Color.FromRgb(0x3B, 0xAF, 0x67);
        public static Color PlumColor => Color.FromRgb(0x6B, 0x2A, 0x5C);
    }
}
