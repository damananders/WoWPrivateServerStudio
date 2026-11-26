using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;

using DS = global::Studio.DesignSystem;

namespace AndersCraftStudio
{
    /// <summary>
    /// "How to use this": a picture of the screen with numbered markers and the matching steps beside it. Not a tour: it
    /// opens in its own window, so the screen you're on stays as it is. Opened from the top bar (Shift+F1) and from Help.
    /// </summary>
    public sealed class GuideWindow : Window
    {
        readonly MainWindow w;
        Guide guide;
        int part;
        readonly ContentControl body = new ContentControl { Focusable = false };
        readonly ComboBox picker = new ComboBox { Width = 260 };
        readonly List<Border> markers = new List<Border>();

        public static GuideWindow Open;

        /// <summary>Shows the guide for <paramref name="key"/> (a page key), reusing the window if it's already open.</summary>
        public static void ShowFor(MainWindow owner, string key)
        {
            var g = Guides.For(key) ?? Guides.All[0];
            if (Open != null && Open.IsLoaded) { Open.Show(g); Open.Activate(); return; }
            Open = new GuideWindow(owner, g);
            Open.Closed += (s, e) => Open = null;
            Open.Show();
        }

        GuideWindow(MainWindow owner, Guide g)
        {
            w = owner; Owner = owner; Title = "How to use this"; MinWidth = 900; MinHeight = 560;
            Width = Math.Max(1000, Math.Min(1560, owner.ActualWidth * 0.94)); Height = Math.Max(640, Math.Min(960, owner.ActualHeight * 0.94));
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResizeWithGrip; ShowInTaskbar = false; Background = U.R("Bg"); FontFamily = U.Font; Foreground = U.R("Text");
            WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 52, GlassFrameThickness = new Thickness(0), ResizeBorderThickness = new Thickness(6), CornerRadius = new CornerRadius(0) });
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };

            var dp = new DockPanel();
            var head = new DockPanel { Background = U.R("Layer5"), Height = 52 };
            var close = U.Btn("", Close, "BtnGhost", "Close (Esc)");
            close.FontFamily = new FontFamily("Segoe MDL2 Assets"); close.FontSize = 11; close.Width = 46; close.Height = 36; close.Margin = new Thickness(0, 0, 8, 0); close.VerticalAlignment = VerticalAlignment.Center;
            WindowChrome.SetIsHitTestVisibleInChrome(close, true);
            DockPanel.SetDock(close, Dock.Right); head.Children.Add(close);
            foreach (var x in Guides.All) picker.Items.Add(new ComboBoxItem { Content = x.Title, Tag = x });
            picker.SelectionChanged += (s, e) => { if (picker.SelectedItem is ComboBoxItem ci && ci.Tag is Guide gg && gg != guide) Show(gg); };
            picker.VerticalAlignment = VerticalAlignment.Center; picker.Margin = new Thickness(0, 0, 12, 0);
            U.AutomationName(picker, "Pick a guide");
            WindowChrome.SetIsHitTestVisibleInChrome(picker, true);
            var pick = U.Row(U.T("Other guides", 12.5, "Muted", null, false, new Thickness(0, 0, 8, 0)), picker);
            pick.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(pick, Dock.Right); head.Children.Add(pick);
            var t = U.Row(new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 16, Foreground = U.R("Amber"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 10, 0) },
                U.T("How to use this", 15, "Text", FontWeights.SemiBold, false));
            t.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(t);
            var line = new Border { Child = head };
            DockPanel.SetDock(line, Dock.Top); dp.Children.Add(line);
            dp.Children.Add(body);
            Content = dp;
            Show(g);
        }

        public void Show(Guide g)
        {
            guide = g; part = 0;
            foreach (ComboBoxItem ci in picker.Items) if (ci.Tag == g) picker.SelectedItem = ci;
            Build();
        }

        public void ScreensPart(int i) { part = i; Build(); }

        void Build()
        {
            var p = guide.Parts[part];
            var grid = new Grid { Margin = new Thickness(22, 18, 22, 18) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });

            // the picture, with numbered markers on top (positions are fractions of the picture, so they scale with it)
            var pic = new Grid { Width = 1600, Height = 1000 };
            try { pic.Children.Add(new Image { Source = new BitmapImage(new Uri($"pack://application:,,,/AndersCraftStudio;component/Guides/{p.Image}")), Stretch = Stretch.Fill }); }
            catch { pic.Children.Add(new Border { Background = U.R("Panel2"), Child = U.T("(picture missing)", 20, "Muted") }); }
            var canvas = new Canvas();
            markers.Clear();
            for (int i = 0; i < p.Steps.Count; i++)
            {
                var s = p.Steps[i];
                if (s.X <= 0 && s.Y <= 0) { markers.Add(null); continue; }
                var m = Marker(i + 1, 54, 24);
                Canvas.SetLeft(m, s.X * 1600 - 27); Canvas.SetTop(m, s.Y * 1000 - 27);
                m.RenderTransformOrigin = new Point(0.5, 0.5);
                canvas.Children.Add(m);
                markers.Add(m);
            }
            pic.Children.Add(canvas);
            var frame = new Border { CornerRadius = new CornerRadius(16), ClipToBounds = true, Child = new Viewbox { Child = pic, Stretch = Stretch.Uniform } };
            var left = new DockPanel();
            if (guide.Parts.Count > 1)
            {
                var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
                for (int i = 0; i < guide.Parts.Count; i++)
                {
                    int idx = i;
                    var r = new RadioButton { Style = U.S("SegBtn"), Content = guide.Parts[i].Name, GroupName = "gpart", IsChecked = i == part };
                    r.Checked += (s, e) => { if (part != idx) { part = idx; Build(); } };
                    tabs.Children.Add(r);
                }
                var tb = new Border { Background = Brushes.Transparent, Padding = new Thickness(3), HorizontalAlignment = HorizontalAlignment.Left, Child = tabs, Margin = new Thickness(0, 0, 0, 12) };
                DockPanel.SetDock(tb, Dock.Top); left.Children.Add(tb);
            }
            frame.VerticalAlignment = VerticalAlignment.Top;
            left.Children.Add(frame);
            grid.Children.Add(left);

            // the steps
            var sp = new StackPanel();
            sp.Children.Add(U.Eyebrow(guide.Area));
            sp.Children.Add(U.H2(guide.Title));
            sp.Children.Add(U.T(p.Intro ?? guide.Intro, 13, "Muted", null, true, new Thickness(0, 0, 0, 14)));
            for (int i = 0; i < p.Steps.Count; i++)
            {
                var s = p.Steps[i];
                var num = Marker(i + 1, 26, 13);
                num.VerticalAlignment = VerticalAlignment.Top; num.Margin = new Thickness(0, 1, 12, 0);
                var text = U.Stack(U.T(s.Head, 13.5, "Text", FontWeights.SemiBold, true), U.T(s.Text, 12.5, "Muted", null, true, new Thickness(0, 2, 0, 0)));
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 12), Background = Brushes.Transparent };
                DockPanel.SetDock(num, Dock.Left); row.Children.Add(num); row.Children.Add(text);
                var mk = markers[i];
                if (mk != null)
                {
                    row.MouseEnter += (o, e) => mk.RenderTransform = new ScaleTransform(1.35, 1.35);
                    row.MouseLeave += (o, e) => mk.RenderTransform = null;
                    row.ToolTip = "Marker " + (i + 1) + " on the picture";
                }
                sp.Children.Add(row);
            }
            if (guide.Tips.Count > 0)
            {
                var tips = U.Callout(string.Join("\n", guide.Tips.Select(x => "• " + x)), "info", "Good to know");
                tips.Margin = new Thickness(0, 6, 0, 0);
                sp.Children.Add(tips);
            }
            if (guide.Go != null && w.CurrentPage != guide.Go)
            {
                var go = U.Btn("Go to " + guide.Title, () => { w.Navigate(guide.Go); w.Activate(); }, null, "Open this screen in the main window. This guide stays open.");
                go.HorizontalAlignment = HorizontalAlignment.Left; go.Margin = new Thickness(0, 14, 0, 0);
                sp.Children.Add(go);
            }
            var right = U.Scroll(sp);
            Grid.SetColumn(right, 2); grid.Children.Add(right);
            body.Content = grid;
        }

        static Border Marker(int n, double size, double font) => DS.U.Marker(n, size, font);
    }
}
