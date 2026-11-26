using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Studio.DesignSystem
{
    /// <summary>
    /// The app's drawn icon set, in the crest's style: a maroon badge with an amber rim and a cream picture.
    /// Built from shapes, so every icon is sharp at any size.
    /// </summary>
    public static class Icons
    {
        static Brush B(string hex) => (Brush)new BrushConverter().ConvertFromString(hex);
        static readonly Brush Cream = B("#F3E6D8"), Amber = B("#F0A83A"), Ink = B("#24170D");

        public static readonly string[] Names = { "words", "picture", "page", "colors", "undo", "online", "helper", "home", "code", "publish", "easy", "pro", "safety" };

        /// <summary>A badge icon by name (see <see cref="Names"/>).</summary>
        public static Viewbox Get(string name, double size, bool badge = true)
        {
            var c = new Canvas { Width = 64, Height = 64 };
            if (badge)
                c.Children.Add(new Rectangle
                {
                    Width = 60, Height = 60, RadiusX = 16, RadiusY = 16, Margin = new Thickness(2),
                    Fill = new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#A3283A"), (Color)ColorConverter.ConvertFromString("#5A1220"), 90),
                    Stroke = Amber, StrokeThickness = 2.2,
                });
            foreach (var p in Glyph(name)) c.Children.Add(p);
            return new Viewbox { Width = size, Height = size, Child = c };
        }

        static Path Line(string data, Brush stroke = null, double w = 4.2) => new()
        {
            Data = Geometry.Parse(data), Stroke = stroke ?? Cream, StrokeThickness = w,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        };
        static Path Fill(string data, Brush fill) => new() { Data = Geometry.Parse(data), Fill = fill };

        static UIElement[] Glyph(string name) => name switch
        {
            // big A with text lines
            "words" => new UIElement[] { Line("M15,44 L24,18 L33,44 M18.5,35 L29.5,35"), Line("M39,24 L50,24 M39,33 L50,33 M39,42 L48,42", Amber, 3.6) },
            // framed picture: hills and sun
            "picture" => new UIElement[] { Line("M14,17 L50,17 L50,47 L14,47 Z", Cream, 3.6), Line("M17,43 L28,30 L35,38 L40,33 L48,43", Cream, 3.4), Fill("M38,22 m-4.5,0 a4.5,4.5 0 1,0 9,0 a4.5,4.5 0 1,0 -9,0", Amber) },
            // page with a folded corner and a plus
            "page" => new UIElement[] { Line("M19,12 L37,12 L46,21 L46,52 L19,52 Z M37,12 L37,21 L46,21", Cream, 3.6), Line("M32.5,30 L32.5,44 M25.5,37 L39.5,37", Amber, 4.2) },
            // painter's palette with three dots
            "colors" => new UIElement[] { Line("M32,13 C19,13 12,22 12,32 C12,43 21,51 31,51 C35,51 36,48 35,45 C34,42 36,40 39,40 L44,40 C49,40 52,36 52,31 C52,20 43,13 32,13 Z", Cream, 3.6),
                                          Fill("M24,26 m-3.6,0 a3.6,3.6 0 1,0 7.2,0 a3.6,3.6 0 1,0 -7.2,0", Amber), Fill("M35,22 m-3.6,0 a3.6,3.6 0 1,0 7.2,0 a3.6,3.6 0 1,0 -7.2,0", B("#E58A9A")), Fill("M22,38 m-3.6,0 a3.6,3.6 0 1,0 7.2,0 a3.6,3.6 0 1,0 -7.2,0", B("#9FD19B")) },
            // curved arrow going back
            "undo" => new UIElement[] { Line("M22,22 L14,30 L22,38", Amber, 4.4), Line("M15,30 L38,30 C45,30 50,35 50,41 C50,47 45,51 38,51 L30,51") },
            // globe with an up arrow
            "online" => new UIElement[] { Line("M32,14 m-17,18 a17,17 0 1,0 34,0 a17,17 0 1,0 -34,0", Cream, 3.4), Line("M15,32 L49,32 M32,15 C24,22 24,42 32,49 M32,15 C40,22 40,42 32,49", Cream, 2.6), Line("M44,20 L52,12 M45,12 L52,12 L52,19", Amber, 3.8) },
            // speech bubble with sparkle dots
            "helper" => new UIElement[] { Line("M14,18 L50,18 L50,42 L30,42 L20,51 L21,42 L14,42 Z", Cream, 3.6), Fill("M24,30 m-3,0 a3,3 0 1,0 6,0 a3,3 0 1,0 -6,0", Amber), Fill("M32,30 m-3,0 a3,3 0 1,0 6,0 a3,3 0 1,0 -6,0", Amber), Fill("M40,30 m-3,0 a3,3 0 1,0 6,0 a3,3 0 1,0 -6,0", Amber) },
            "home" => new UIElement[] { Line("M13,31 L32,14 L51,31"), Line("M18,28 L18,50 L46,50 L46,28", Cream, 3.6), Line("M28,50 L28,38 L36,38 L36,50", Amber, 3.4) },
            "code" => new UIElement[] { Line("M24,21 L15,32 L24,43"), Line("M40,21 L49,32 L40,43"), Line("M35,17 L29,47", Amber, 3.8) },
            // cloud with an up arrow
            "publish" => new UIElement[] { Line("M20,46 C12,46 10,36 17,33 C16,24 27,20 31,26 C34,18 48,20 47,30 C55,31 54,46 45,46", Cream, 3.4), Line("M32,52 L32,33 M25,40 L32,33 L39,40", Amber, 4) },
            // smiley star = easy
            "easy" => new UIElement[] { Line("M32,14 m-18,18 a18,18 0 1,0 36,0 a18,18 0 1,0 -36,0", Cream, 3.4), Fill("M26,28 m-2.8,0 a2.8,2.8 0 1,0 5.6,0 a2.8,2.8 0 1,0 -5.6,0", Cream), Fill("M38,28 m-2.8,0 a2.8,2.8 0 1,0 5.6,0 a2.8,2.8 0 1,0 -5.6,0", Cream), Line("M23,37 C27,43 37,43 41,37", Amber, 3.6) },
            // wrench = pro
            "pro" => new UIElement[] { Line("M44,14 C37,12 31,18 33,25 L15,43 C13,45 13,48 15,50 C17,52 20,52 22,50 L40,32 C47,34 53,28 51,21 L45,27 L39,25 L37,19 Z", Cream, 3.4), Fill("M18.5,46.5 m-2,0 a2,2 0 1,0 4,0 a2,2 0 1,0 -4,0", Amber) },
            // shield with a tick
            "safety" => new UIElement[] { Line("M32,12 L48,18 L48,31 C48,42 41,49 32,53 C23,49 16,42 16,31 L16,18 Z", Cream, 3.6), Line("M24.5,32 L30,37.5 L40.5,26.5", Amber, 4.2) },
            _ => new UIElement[0],
        };

        /// <summary>Writes every icon as a 256 px PNG (for docs and the screens folder).</summary>
        public static void ExportAll(string dir)
        {
            System.IO.Directory.CreateDirectory(dir);
            foreach (var n in Names)
            {
                var v = Get(n, 256);
                v.Measure(new Size(256, 256)); v.Arrange(new Rect(0, 0, 256, 256)); v.UpdateLayout();
                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(256, 256, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(v);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder(); enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                using var fs = System.IO.File.Create(System.IO.Path.Combine(dir, "icon_" + n + ".png")); enc.Save(fs);
            }
        }
    }
}
