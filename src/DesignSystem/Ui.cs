using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Studio.DesignSystem
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

        public static Border Card(UIElement child, Thickness? pad = null, Thickness? margin = null, string bg = "Panel")
            => new Border { Background = R(bg == "Panel" ? "Fill.Bleed" : bg), CornerRadius = new CornerRadius(16), Padding = pad ?? new Thickness(24), Margin = margin ?? new Thickness(0, 0, 0, 18), Child = child };   // §2: cards bleed (fill fades out) instead of sitting in a box

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
            var v = T(value, 13.5, labelWidth == 0 ? "Text" : "Text", labelWidth == 0 ? FontWeights.Normal : FontWeights.SemiBold, true); Grid.SetColumn(v, 2); g.Children.Add(v); v.VerticalAlignment = VerticalAlignment.Center;
            var b = new Border { Child = g, Padding = new Thickness(4, 6, 4, 6) };
            if (tip != null) b.ToolTip = tip;
            return b;
        }

        /// <summary>The DMA crest: the maroon shield with an amber rim, carrying a code bracket. Drawn from shapes so it scales crisply.</summary>
        public static Viewbox Crest(double size)
        {
            var c = new Canvas { Width = 64, Height = 64 };
            c.Children.Add(new Path
            {
                Data = Geometry.Parse("M32,3 L57,12 L57,30 C57,46 46,56 32,61 C18,56 7,46 7,30 L7,12 Z"),
                Fill = new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#A53547"), (Color)ColorConverter.ConvertFromString("#5A1220"), 90),
                Stroke = (Brush)new BrushConverter().ConvertFromString("#F0B46A"), StrokeThickness = 2.4,
            });
            var ink = (Brush)new BrushConverter().ConvertFromString("#F3E6D8");
            // < / >
            c.Children.Add(new Path { Data = Geometry.Parse("M24,21 L15,31 L24,41"), Stroke = ink, StrokeThickness = 4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round });
            c.Children.Add(new Path { Data = Geometry.Parse("M40,21 L49,31 L40,41"), Stroke = ink, StrokeThickness = 4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round });
            c.Children.Add(new Path { Data = Geometry.Parse("M35,18 L29,44"), Stroke = (Brush)new BrushConverter().ConvertFromString("#F0B46A"), StrokeThickness = 3.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
            return new Viewbox { Width = size, Height = size, Child = c };
        }

        /// <summary>
        /// The AndersCraft crest: a maroon shield with an amber rim, two crossed swords and an amber pommel stone.
        /// Drawn from shapes so it scales crisply; colours come from the Crest.* keys.
        /// </summary>
        public static Viewbox SwordsCrest(double size)
        {
            var c = new Canvas { Width = 64, Height = 64 };
            c.Children.Add(new Path
            {
                Data = Geometry.Parse("M32,3 L57,12 L57,30 C57,46 46,56 32,61 C18,56 7,46 7,30 L7,12 Z"),
                Fill = new LinearGradientBrush((Color)Application.Current.Resources["Crest.HiColor"], (Color)Application.Current.Resources["Crest.LoColor"], 90),
                Stroke = R("Crest.Rim"), StrokeThickness = 2.2,
            });
            c.Children.Add(Sword(-42));
            c.Children.Add(Sword(42));
            c.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = R("Crest.Rim"), Margin = new Thickness(28.5, 28.5, 0, 0) });
            return new Viewbox { Width = size, Height = size, Child = c };
        }

        static Path Sword(double angle) => new Path
        {
            // blade pointing up, crossguard and grip, rotated around the centre
            Data = Geometry.Parse("M32,12 L34.2,16 L34.2,38 L39,38 L39,40.6 L34,40.6 L34,47 L35.4,47 L35.4,50 L28.6,50 L28.6,47 L30,47 L30,40.6 L25,40.6 L25,38 L29.8,38 L29.8,16 Z"),
            Fill = R("Crest.Blade"), Stroke = R("Crest.Edge"), StrokeThickness = 0.8,
            RenderTransform = new RotateTransform(angle, 32, 32),
        };

        /// <summary>
        /// The one hero per screen: the page's title block on a bleed panel (Radius.Hero). The panel takes over the block's
        /// place in its parent (grid cell, dock side) and its outer margin.
        /// </summary>
        public static Border Hero(FrameworkElement head, Thickness? pad = null)
        {
            var m = head.Margin; head.Margin = new Thickness(0);
            // the glowing underline beneath the title (Text.Magic + Glow.Title)
            var line = new Border { Width = 56, Height = 3, CornerRadius = new CornerRadius(2), Background = R("Text.Magic"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0),
                Effect = (System.Windows.Media.Effects.Effect)Application.Current.Resources["Glow.Title"], IsHitTestVisible = false };
            var stack = new StackPanel(); stack.Children.Add(head); stack.Children.Add(line);
            var b = new Border { Background = R("Fill.Bleed"), CornerRadius = (CornerRadius)Application.Current.Resources["Radius.Hero"], Padding = pad ?? new Thickness(26, 20, 26, 18),
                Margin = new Thickness(m.Left, m.Top, m.Right, Math.Max(m.Bottom, 16)), Child = stack };
            foreach (var dp in new[] { Grid.RowProperty, Grid.ColumnProperty, Grid.RowSpanProperty, Grid.ColumnSpanProperty, DockPanel.DockProperty })
            {
                var v = head.ReadLocalValue(dp);
                if (v != DependencyProperty.UnsetValue) b.SetValue(dp, v);
            }
            return b;
        }

        /// <summary>A row of segment buttons (tabs): no track or box around them; the chosen one glows.</summary>
        public static Border Segments(UIElement child) => new Border { Background = Brushes.Transparent, Padding = new Thickness(3), HorizontalAlignment = HorizontalAlignment.Left, Child = child };

        /// <summary>The faint luminous edge of a borderless window (a dialog drawn without the Windows frame).</summary>
        public static Border Frame(Border b)
        {
            b.BorderBrush = R("Hairline"); b.BorderThickness = new Thickness(1);
            return b;
        }

        /// <summary>A replica of the game's own tooltip frame (navy fill, grey rim), for previews of what the player will see.</summary>
        public static Border GameTooltip(Border b)
        {
            b.Background = R("Game.Tooltip"); b.BorderBrush = R("Game.TooltipEdge"); b.BorderThickness = new Thickness(1.5);
            return b;
        }

        /// <summary>A numbered amber marker for pointing at things in a picture (how-to guides). Big markers get a light ring and a shadow.</summary>
        public static Border Marker(int n, double size, double font) => new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = R("Amber"),
            BorderBrush = R("Marker.Ring"), BorderThickness = new Thickness(size > 30 ? 3 : 0),
            Effect = size > 30 ? new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.7 } : null,
            Child = new TextBlock { Text = n.ToString(), FontSize = font, FontWeight = FontWeights.Bold, Foreground = R("AmberInk"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };

        public static Border Divider(double top = 8, double bottom = 8) => new Border { Height = 12, Margin = new Thickness(0, top, 0, bottom) };

        /// <summary>A soft drop shadow so panels float on the background instead of being boxed in by lines.</summary>
        public static System.Windows.Media.Effects.DropShadowEffect Soft(double blur = 26, double opacity = 0.32)
            => new() { BlurRadius = blur, ShadowDepth = 4, Direction = 270, Opacity = opacity, Color = Colors.Black };

        /// <summary>
        /// A clickable tile (§2): a tone and a bleed fill that fade out, with the edges feathered by a radial mask.
        /// Only the fill layers are masked, so the words stay sharp. Hover or keyboard focus drops the mask, brightens
        /// the fill and wakes a soft accent glow behind (its own layer); all of it is instant with reduced motion.
        /// </summary>
        public static Grid Tile(UIElement content, string tone, double radius, Thickness pad, Action click, string name, double glow = 0.22)
        {
            var cr = new CornerRadius(radius);
            var halo = new Border { Background = R("Layer10"), CornerRadius = cr, Effect = (System.Windows.Media.Effects.Effect)Application.Current.Resources["Glow.Accent"], Opacity = 0, IsHitTestVisible = false };
            var tint = new Border { Background = R(tone), CornerRadius = cr, OpacityMask = R("Mask.Feather"), IsHitTestVisible = false, Opacity = 0.6 };
            var fill = new Border { Background = R("Fill.Bleed"), CornerRadius = cr, OpacityMask = R("Mask.Feather"), IsHitTestVisible = false };
            var face = new Border { Background = Brushes.Transparent, CornerRadius = cr, Padding = pad, Child = content, Cursor = Cursors.Hand, Focusable = true, FocusVisualStyle = null, ToolTip = name };
            AutomationName(face, name);
            var g = new Grid(); g.Children.Add(halo); g.Children.Add(tint); g.Children.Add(fill); g.Children.Add(face);
            void On() { fill.OpacityMask = null; tint.OpacityMask = null; fill.Background = R("Fill.BleedHover"); StudioLook.Fade(halo, UIElement.OpacityProperty, glow); }
            void Off() { if (face.IsKeyboardFocused) return; fill.OpacityMask = R("Mask.Feather"); tint.OpacityMask = R("Mask.Feather"); fill.Background = R("Fill.Bleed"); StudioLook.Fade(halo, UIElement.OpacityProperty, 0); }
            face.MouseEnter += (s, e) => On();
            face.MouseLeave += (s, e) => Off();
            face.GotKeyboardFocus += (s, e) => On();
            face.LostKeyboardFocus += (s, e) => Off();
            if (click != null)
            {
                face.MouseLeftButtonUp += (s, e) => click();
                face.KeyDown += (s, e) => { if (e.Key == Key.Enter || e.Key == Key.Space) { click(); e.Handled = true; } };
            }
            return g;
        }

        /// <summary>A list row as a floating strip: a fill that fades toward the right, 6 px apart, brighter on hover. No lines.</summary>
        public static Border Strip(UIElement child, Thickness pad, bool hover = true)
        {
            var b = new Border { Child = child, Background = R("Fill.Row"), CornerRadius = new CornerRadius(16), Padding = pad, Margin = new Thickness(0, 0, 0, 6) };
            if (hover) { b.MouseEnter += (s, e) => b.Background = R("Fill.RowHover"); b.MouseLeave += (s, e) => b.Background = R("Fill.Row"); }
            return b;
        }

        /// <summary>A floating panel: tinted, very round, softly shadowed, with breathing room around it.</summary>
        /// <remarks>The shadow sits on its own empty layer behind the panel, so text and web views inside stay crisp.</remarks>
        public static Grid Float(UIElement child, Thickness margin, string bg = "Panel", double radius = 22, Thickness? pad = null, bool shadow = true)
        {
            var g = new Grid { Margin = margin };
            if (shadow) g.Children.Add(new Border { Background = R("Deep"), CornerRadius = new CornerRadius(radius), Effect = Soft(34, 0.6) });
            g.Children.Add(new Border { Child = child, Background = R(bg), CornerRadius = new CornerRadius(radius), Padding = pad ?? new Thickness(0), BorderBrush = shadow ? R("Hairline") : null, BorderThickness = new Thickness(shadow ? 1 : 0) });   // luminous edge: faint inset hairline + big soft shadow
            return g;
        }

        /// <summary>An info / warning callout box.</summary>
        public static Border Callout(string text, string kind = "info", string title = null)
        {
            string bg = kind == "warn" ? "AmberDim" : kind == "bad" ? "BadBg" : kind == "ok" ? "OkBg" : "Panel2";
            string fg = kind == "warn" ? "Amber" : kind == "bad" ? "Bad" : kind == "ok" ? "Ok" : "Muted";
            var sp = new StackPanel();
            if (title != null) sp.Children.Add(T(title, 13.5, fg, FontWeights.SemiBold, true, new Thickness(0, 0, 0, 3)));
            sp.Children.Add(T(text, 13, kind == "info" ? "Muted" : "Text", null, true));
            return new Border { Background = R(bg), CornerRadius = new CornerRadius(16), Padding = new Thickness(18, 14, 18, 14), Margin = new Thickness(0, 0, 0, 14), Child = sp };
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
