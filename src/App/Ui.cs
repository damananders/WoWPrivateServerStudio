using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AndersCraftStudio
{
    /// <summary>Small UI building blocks so every screen looks and explains itself the same way.</summary>
    public static class U
    {
        public static Brush R(string key) => (Brush)Application.Current.Resources[key];
        public static Style S(string key) => (Style)Application.Current.Resources[key];
        public static FontFamily Mono => (FontFamily)Application.Current.Resources["MonoFont"];
        public static FontFamily Font => (FontFamily)Application.Current.Resources["UiFont"];

        public static TextBlock T(string text, double size = 13.5, string brush = "Text", FontWeight? weight = null, bool wrap = true, Thickness? margin = null)
            => new TextBlock { Text = text, FontSize = size, Foreground = R(brush), FontWeight = weight ?? FontWeights.Normal, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis, Margin = margin ?? new Thickness(0), FontFamily = Font };

        public static TextBlock H1(string text) => T(text, 26, "Text", FontWeights.SemiBold, true, new Thickness(0, 0, 0, 4));
        public static TextBlock H2(string text) => T(text, 17, "Text", FontWeights.SemiBold, true, new Thickness(0, 0, 0, 6));
        public static TextBlock Sub(string text) => T(text, 13.5, "Muted", null, true, new Thickness(0, 0, 0, 14));
        public static TextBlock Eyebrow(string text) => T(text.ToUpperInvariant(), 11, "Amber", FontWeights.Bold, false, new Thickness(0, 0, 0, 6));

        public static Button Btn(string text, Action click, string style = null, string tip = null, double minW = 0)
        {
            var b = new Button { Content = text, MinWidth = minW, Margin = new Thickness(0, 0, 10, 0) };
            if (style != null) b.Style = S(style);
            if (tip != null) b.ToolTip = tip;
            if (click != null) b.Click += (s, e) => click();
            return b;
        }

        /// <summary>The softer card (no outline): a tinted, rounded surface with a faint shadow; grouping by space, not ruled boxes.</summary>
        public static Border Soft(UIElement child, Thickness? pad = null, Thickness? margin = null, string bg = "Panel")
            => new Border { Background = R(bg), CornerRadius = new CornerRadius(18), Padding = pad ?? new Thickness(22), Margin = margin ?? new Thickness(0, 0, 0, 18), Child = child,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 22, ShadowDepth = 3, Opacity = 0.28, Color = Colors.Black } };

        /// <summary>Show raw engine names (tables, columns, IDs, commands) as well as the plain ones. Settings › Show technical names.</summary>
        public static bool Technical { get => technical; set { technical = value; Core.SmartAI.ShowIds = value; } }
        static bool technical;

        public static Border Card(UIElement child, Thickness? pad = null, Thickness? margin = null, string bg = "Panel")
            => new Border { Background = R(bg), BorderBrush = R("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = pad ?? new Thickness(20), Margin = margin ?? new Thickness(0, 0, 0, 16), Child = child };

        public static StackPanel Stack(params UIElement[] kids) { var s = new StackPanel(); foreach (var k in kids) if (k != null) s.Children.Add(k); return s; }
        public static StackPanel Row(params UIElement[] kids) { var s = new StackPanel { Orientation = Orientation.Horizontal }; foreach (var k in kids) if (k != null) s.Children.Add(k); return s; }
        public static WrapPanel Wrap(params UIElement[] kids) { var s = new WrapPanel(); foreach (var k in kids) if (k != null) s.Children.Add(k); return s; }

        /// <summary>A labelled field: label on top, the control, then a muted hint line. The tooltip repeats the full explanation.</summary>
        public static StackPanel Field(string label, string hint, FrameworkElement control, string tooltip = null, double width = double.NaN)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 16), Width = width, HorizontalAlignment = double.IsNaN(width) ? HorizontalAlignment.Stretch : HorizontalAlignment.Left };
            var l = T(label, 13, "Text", FontWeights.SemiBold, false, new Thickness(0, 0, 0, 6));
            sp.Children.Add(l);
            sp.Children.Add(control);
            if (!string.IsNullOrEmpty(hint)) sp.Children.Add(T(hint, 12.5, "Muted", null, true, new Thickness(2, 6, 0, 0)));
            var tip = tooltip ?? hint;
            if (!string.IsNullOrEmpty(tip)) { control.ToolTip = tip; l.ToolTip = tip; }
            AutomationName(control, label);
            return sp;
        }

        public static void AutomationName(DependencyObject d, string name) => System.Windows.Automation.AutomationProperties.SetName(d, name);

        public static TextBox Box(string text = "", bool mono = false, string tip = null)
        {
            var t = new TextBox { Text = text ?? "" };
            if (mono) { t.FontFamily = Mono; t.FontSize = 13; }
            if (tip != null) t.ToolTip = tip;
            return t;
        }

        public static TextBox Code(string text, double height, bool readOnly = true)
        {
            return new TextBox
            {
                Text = text, IsReadOnly = readOnly, FontFamily = Mono, FontSize = 12.5, Height = height, AcceptsReturn = true, AcceptsTab = !readOnly,
                TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(12, 10, 12, 10),
            };
        }

        /// <summary>Small rounded tag, e.g. a category or "read-only".</summary>
        public static Border Pill(string text, string bg = "Panel2", string fg = "Muted", string tip = null)
        {
            var b = new Border { Background = R(bg), CornerRadius = new CornerRadius(20), Padding = new Thickness(9, 3, 9, 3), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = text, Foreground = R(fg), FontSize = 11.5, FontWeight = FontWeights.SemiBold, FontFamily = Font } };
            if (tip != null) b.ToolTip = tip;
            return b;
        }

        public enum Check { Ok, Warn, Bad, Pending, Info }

        /// <summary>One line of a checklist: icon, label, value.</summary>
        public static Border CheckRow(Check state, string label, string value, string tip = null, double labelWidth = 210)
        {
            string icon, fg, bg;
            switch (state)
            {
                case Check.Ok: icon = "✓"; fg = "Ok"; bg = "OkBg"; break;
                case Check.Bad: icon = "✕"; fg = "Bad"; bg = "BadBg"; break;
                case Check.Warn: icon = "!"; fg = "Amber"; bg = "AmberDim"; break;
                case Check.Info: icon = "i"; fg = "Muted"; bg = "Panel2"; break;
                default: icon = "…"; fg = "Muted"; bg = "Panel2"; break;
            }
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var dot = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = R(bg), HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock { Text = icon, Foreground = R(fg), FontWeight = FontWeights.Bold, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            g.Children.Add(dot);
            var l = T(label, 13.5, "Muted", null, false); Grid.SetColumn(l, 1); g.Children.Add(l); l.VerticalAlignment = VerticalAlignment.Center;
            var v = T(value, 13.5, "Text", FontWeights.SemiBold, true); Grid.SetColumn(v, 2); g.Children.Add(v); v.VerticalAlignment = VerticalAlignment.Center;
            var b = new Border { Child = g, Padding = new Thickness(4, 7, 4, 7), BorderBrush = R("Line"), BorderThickness = new Thickness(0, 0, 0, 1) };
            if (tip != null) b.ToolTip = tip;
            return b;
        }

        /// <summary>The 1-2-3-4 strip shown on editor screens. <paramref name="active"/> is 1..4.</summary>
        public static Border StepStrip(int active)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            string[] names = { "Edit", "Stage", "Review", "Apply" };
            string[] tips =
            {
                "Change values in a table, or type SQL in the query editor.",
                "Each change is written into your changeset (a file on disk), not into the database.",
                "Open Changes to see every staged change side by side: before, after, and the SQL to undo it.",
                "Apply runs the changes you tick, in one transaction, after backing up the affected tables.",
            };
            for (int i = 0; i < 4; i++)
            {
                bool on = i + 1 == active, done = i + 1 < active;
                var num = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = on ? R("Amber") : done ? R("MaroonDim") : R("Panel2"), BorderBrush = R("Line"), BorderThickness = new Thickness(on ? 0 : 1),
                    Child = new TextBlock { Text = (i + 1).ToString(), FontSize = 12, FontWeight = FontWeights.Bold, Foreground = on ? R("AmberInk") : R("Muted"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                var step = Row(num, T(names[i], 13, on ? "Text" : "Muted", on ? FontWeights.SemiBold : FontWeights.Normal, false, new Thickness(7, 0, 0, 0)));
                step.ToolTip = tips[i];
                step.Background = Brushes.Transparent;
                row.Children.Add(step);
                if (i < 3) row.Children.Add(new Rectangle { Width = 22, Height = 1, Fill = R("LineHi"), Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
            }
            var note = Row(new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = R("Ok"), Margin = new Thickness(18, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center },
                T("Nothing reaches your database until step 4", 12.5, "Ok", null, false));
            row.Children.Add(note);
            return new Border { Background = R("Panel"), BorderBrush = R("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 9, 14, 9), Child = row, HorizontalAlignment = HorizontalAlignment.Left };
        }

        /// <summary>The crossed-swords crest. Drawn from simple shapes so it scales crisply.</summary>
        public static Viewbox Crest(double size)
        {
            var c = new Canvas { Width = 64, Height = 64 };
            var shield = new Path
            {
                Data = Geometry.Parse("M32,3 L57,12 L57,30 C57,46 46,56 32,61 C18,56 7,46 7,30 L7,12 Z"),
                Fill = new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#A53547"), (Color)ColorConverter.ConvertFromString("#5E1A26"), 90),
                Stroke = (Brush)new BrushConverter().ConvertFromString("#F0B46A"), StrokeThickness = 2.2,
            };
            c.Children.Add(shield);
            c.Children.Add(Sword(-42));
            c.Children.Add(Sword(42));
            c.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = (Brush)new BrushConverter().ConvertFromString("#F0B46A"), Margin = new Thickness(28.5, 28.5, 0, 0) });
            return new Viewbox { Width = size, Height = size, Child = c };
        }

        static Path Sword(double angle)
        {
            // blade pointing up, crossguard and grip, rotated around the centre
            var g = Geometry.Parse("M32,12 L34.2,16 L34.2,38 L39,38 L39,40.6 L34,40.6 L34,47 L35.4,47 L35.4,50 L28.6,50 L28.6,47 L30,47 L30,40.6 L25,40.6 L25,38 L29.8,38 L29.8,16 Z");
            return new Path
            {
                Data = g, Fill = (Brush)new BrushConverter().ConvertFromString("#F3E6D8"), Stroke = (Brush)new BrushConverter().ConvertFromString("#24170D"), StrokeThickness = 0.8,
                RenderTransform = new RotateTransform(angle, 32, 32),
            };
        }

        public static Border Divider(double top = 8, double bottom = 8) => new Border { Height = 1, Background = R("Line"), Margin = new Thickness(0, top, 0, bottom) };

        /// <summary>An info / warning callout box.</summary>
        public static Border Callout(string text, string kind = "info", string title = null)
        {
            string bg = kind == "warn" ? "AmberDim" : kind == "bad" ? "BadBg" : kind == "ok" ? "OkBg" : "Panel2";
            string fg = kind == "warn" ? "Amber" : kind == "bad" ? "Bad" : kind == "ok" ? "Ok" : "Muted";
            var sp = new StackPanel();
            if (title != null) sp.Children.Add(T(title, 13.5, fg, FontWeights.SemiBold, true, new Thickness(0, 0, 0, 3)));
            sp.Children.Add(T(text, 13, kind == "info" ? "Muted" : "Text", null, true));
            return new Border { Background = R(bg), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 14), Child = sp,
                BorderBrush = R(fg), BorderThickness = new Thickness(3, 0, 0, 0) };
        }

        public static void OnKey(UIElement el, Key key, ModifierKeys mods, Action a)
        {
            el.PreviewKeyDown += (s, e) =>
            {
                var k = e.Key == Key.System ? e.SystemKey : e.Key;
                if (k == key && Keyboard.Modifiers == mods) { a(); e.Handled = true; }
            };
        }

        public static ScrollViewer Scroll(UIElement content) => new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };

        /// <summary>A text box with a muted placeholder that disappears while typing.</summary>
        public static Grid WithPlaceholder(TextBox box, string placeholder)
        {
            var g = new Grid();
            var ph = new TextBlock { Text = placeholder, Foreground = R("Faint"), FontSize = box.FontSize, FontFamily = box.FontFamily, IsHitTestVisible = false, Margin = new Thickness(box.Padding.Left + 3, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            g.Children.Add(box); g.Children.Add(ph);
            void Upd() => ph.Visibility = string.IsNullOrEmpty(box.Text) ? Visibility.Visible : Visibility.Collapsed;
            box.TextChanged += (s, e) => Upd();
            Upd();
            return g;
        }
    }
}
