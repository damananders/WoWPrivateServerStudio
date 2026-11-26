using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Studio.DesignSystem;

namespace AndersCraftStudio.Tests
{
    /// <summary>Colour schemes: every built-in one re-colours every token, keeps data colours, and stays readable.</summary>
    static class SchemeTests
    {
        static string Root()
        {
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "src", "AndersCraftStudio.sln"))) return d.FullName;
            throw new InvalidOperationException("Repository root not found.");
        }
        static string Ds => File.ReadAllText(Path.Combine(Root(), "src", "DesignSystem", "StudioDesignSystem.xaml"));
        static string Tokens => File.ReadAllText(Path.Combine(Root(), "src", "DesignSystem", "Tokens.xaml"));

        /// <summary>key → colour text of every simple colour entry (Color or SolidColorBrush) in a dictionary.</summary>
        static Dictionary<string, string> Colours(string xaml)
        {
            var d = new Dictionary<string, string>();
            foreach (Match m in Regex.Matches(xaml, @"<Color x:Key=""([^""]+)"">(#[0-9A-Fa-f]{6,8})</Color>")) d[m.Groups[1].Value] = m.Groups[2].Value;
            foreach (Match m in Regex.Matches(xaml, @"<SolidColorBrush x:Key=""([^""]+)"" Color=""(#[0-9A-Fa-f]{6,8})""")) d[m.Groups[1].Value] = m.Groups[2].Value;
            return d;
        }

        static (byte, byte, byte) Rgb(string hex, (byte, byte, byte) over)
        {
            hex = hex.TrimStart('#');
            double a = 1; int o = 0;
            if (hex.Length == 8) { a = int.Parse(hex.Substring(0, 2), NumberStyles.HexNumber) / 255.0; o = 2; }
            byte C(int i, byte under) => (byte)Math.Round(int.Parse(hex.Substring(o + i, 2), NumberStyles.HexNumber) * a + under * (1 - a));
            return (C(0, over.Item1), C(2, over.Item2), C(4, over.Item3));
        }

        [Test] static void Every_scheme_recolours_every_token_and_keeps_data_colours()
        {
            var ds0 = Ds; var t0 = Tokens;
            int keys0 = Regex.Matches(ds0, "x:Key=").Count + Regex.Matches(t0, "x:Key=").Count;
            A.True(StudioScheme.All.Count >= 5, "several schemes");
            A.Eq("andersian", StudioScheme.Default.Id); A.True(StudioScheme.ById("nope").IsDefault, "unknown id → default");
            A.Eq(t0, StudioScheme.Default.Recolor(t0), "the default changes nothing");
            foreach (var s in StudioScheme.All.Where(x => !x.IsDefault))
            {
                var ds = s.Recolor(ds0); var t = s.Recolor(t0);
                A.Eq(keys0, Regex.Matches(ds, "x:Key=").Count + Regex.Matches(t, "x:Key=").Count, s.Name + ": every token still there");
                foreach (Match m in Regex.Matches(ds + t, @"#[0-9A-Fa-f]+\b")) A.True(m.Length == 7 || m.Length == 9, s.Name + ": well-formed colour " + m.Value);
                var c0 = Colours(ds0).Concat(Colours(t0)).GroupBy(kv => kv.Key).ToDictionary(g => g.Key, g => g.First().Value);
                var c = Colours(ds).Concat(Colours(t)).GroupBy(kv => kv.Key).ToDictionary(g => g.Key, g => g.First().Value);
                foreach (var k in StudioScheme.Kept.Where(c0.ContainsKey)) A.Eq(c0[k], c[k], s.Name + ": data colour " + k + " kept");
                foreach (var k in new[] { "Bg", "Maroon", "Amber", "Color.Accent", "Color.AccentPink", "Color.Canvas", "Crest.Rim", "Glow.MaroonColor", "Glow.AmberColor" })
                    A.True(c0[k] != c[k], s.Name + ": " + k + " recoloured");
                // readable: text on the page, accent on the page, light text on the deep colour
                var bg = Rgb(c["Bg"], (0, 0, 0));
                A.True(StudioScheme.Contrast(Rgb(c["Text"], bg), bg) >= 7, s.Name + ": body text contrast");
                A.True(StudioScheme.Contrast(Rgb(c["Muted"], bg), bg) >= 4.5, s.Name + ": muted text contrast");
                A.True(StudioScheme.Contrast(Rgb(c["Amber"], bg), bg) >= 4.5, s.Name + ": accent contrast");
                var deep = Rgb(c["Maroon"], bg);
                A.True(StudioScheme.Contrast(((byte)0xFF, (byte)0xF6, (byte)0xF2), deep) >= 4.5, s.Name + ": button text on the deep colour");   // design-check: data
            }
        }
    }
}
