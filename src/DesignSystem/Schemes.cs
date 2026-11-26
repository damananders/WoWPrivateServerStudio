using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Studio.DesignSystem
{
    /// <summary>
    /// Colour schemes. A scheme changes colours only: the design system's own XAML (StudioDesignSystem.xaml + Tokens.xaml)
    /// is re-coloured by hue family, so glass, bleeds, glowing titles, the crest, the ambient light and every token move
    /// together and nothing about structure changes. Data colours (ok / problem, diffs, the game's own tooltip and parchment,
    /// the 3D stage) are never touched. Plain C#, no WPF, so the tests can check every scheme.
    /// </summary>
    public sealed class StudioScheme
    {
        public string Id, Name, Description;
        /// <summary>Hue (0-360) for the deep colour family (Studio's maroon: primary buttons, rail glow, crest), the light
        /// accent family (amber: highlights, titles, motes) and the page's dark base. NaN keeps the original.</summary>
        public double PrimaryHue = double.NaN, AccentHue = double.NaN, BaseHue = double.NaN;
        /// <summary>Saturation multipliers for the same families (1 = as designed).</summary>
        public double PrimarySat = 1, AccentSat = 1, BaseSat = 1;
        public bool IsDefault => Id == Default.Id;

        public static readonly StudioScheme Default = new() { Id = "andersian", Name = "Andersian", Description = "Dark maroon and amber, the AndersCraft crest colours. The default." };

        public static readonly IReadOnlyList<StudioScheme> All = new[]
        {
            Default,
            new StudioScheme { Id = "frostforge", Name = "Frostforge", Description = "Deep blue-black with frost and ice blue.", PrimaryHue = 214, AccentHue = 196, BaseHue = 220, PrimarySat = 0.95, AccentSat = 0.85, BaseSat = 1.4 },
            new StudioScheme { Id = "verdant", Name = "Verdant", Description = "Deep forest green with old gold.", PrimaryHue = 152, AccentHue = 44, BaseHue = 150, PrimarySat = 0.8, AccentSat = 1.0, BaseSat = 1.2 },
            new StudioScheme { Id = "arcane", Name = "Arcane", Description = "Plum-black with violet and a magenta glow.", PrimaryHue = 276, AccentHue = 312, BaseHue = 285, PrimarySat = 1.0, AccentSat = 0.9, BaseSat = 1.4 },
            new StudioScheme { Id = "emberforge", Name = "Emberforge", Description = "Char-black with crimson and forge orange.", PrimaryHue = 4, AccentHue = 20, BaseHue = 20, PrimarySat = 1.35, AccentSat = 1.25, BaseSat = 0.3 },
            new StudioScheme { Id = "obsidian", Name = "Obsidian", Description = "Neutral graphite with cyan and silver.", PrimaryHue = 198, AccentHue = 188, BaseHue = 210, PrimarySat = 0.55, AccentSat = 0.55, BaseSat = 0.35 },
        };

        /// <summary>The default palette's page, panel, deep and accent colours (the swatches a scheme picker shows).</summary>
        public static readonly (byte r, byte g, byte b)[] Palette = { (0x14, 0x11, 0x13), (0x1B, 0x16, 0x18), (0x8E, 0x2B, 0x3A), (0xF0, 0xB4, 0x6A) };
        public (byte r, byte g, byte b)[] Swatches => Palette.Select(c => MapRgb(c.r, c.g, c.b)).Select(t => (t.Item1, t.Item2, t.Item3)).ToArray();

        public static StudioScheme ById(string id) => All.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Default;

        /// <summary>Keys whose colours are data, not decoration: they stay the same in every scheme.</summary>
        public static readonly string[] Kept = { "Ok", "OkBg", "Bad", "BadBg", "Go", "GoHi", "Go.Gradient", "Color.Danger", "Color.Success", "Status.Danger", "Status.Success", "Marker.Ring", "Game.Tooltip", "Game.TooltipEdge", "Game.Gold", "Game.White", "Game.Grey", "Parchment", "Parchment.Ink", "Parchment.Head", "Viewer.TopColor", "Viewer.BottomColor", "Diff.Add", "Diff.Del", "Diff.ChgOld", "Diff.ChgNew" };

        static readonly Regex Hex = new(@"#([0-9A-Fa-f]{8}|[0-9A-Fa-f]{6})\b", RegexOptions.Compiled);

        /// <summary>The design system's XAML with this scheme's colours (the default scheme returns it unchanged).</summary>
        public string Recolor(string xaml)
        {
            if (IsDefault) return xaml;
            // spans that hold data colours are left alone
            var kept = new List<(int a, int b)>();
            foreach (Match m in Regex.Matches(xaml, @"<([\w.:]+)\s[^>]*?x:Key=""([^""]+)""[^>]*?(/?)>"))
            {
                if (!Kept.Contains(m.Groups[2].Value)) continue;
                int end = m.Index + m.Length;
                if (m.Groups[3].Value != "/") { var close = "</" + m.Groups[1].Value + ">"; int c = xaml.IndexOf(close, end, StringComparison.Ordinal); if (c > 0) end = c + close.Length; }
                kept.Add((m.Index, end));
            }
            return Hex.Replace(xaml, m => kept.Any(k => m.Index >= k.a && m.Index < k.b) ? m.Value : "#" + Map(m.Groups[1].Value));
        }

        /// <summary>One colour (RRGGBB or AARRGGBB, no #) in this scheme.</summary>
        public string Map(string hex)
        {
            byte a = 255; int o = 0;
            if (hex.Length == 8) { a = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber); o = 2; }
            byte r = byte.Parse(hex.Substring(o, 2), NumberStyles.HexNumber), g = byte.Parse(hex.Substring(o + 2, 2), NumberStyles.HexNumber), b = byte.Parse(hex.Substring(o + 4, 2), NumberStyles.HexNumber);
            var (nr, ng, nb) = MapRgb(r, g, b);
            var rgb = $"{nr:X2}{ng:X2}{nb:X2}";
            return hex.Length == 8 ? $"{a:X2}{rgb}" : rgb;
        }

        public (byte, byte, byte) MapRgb(byte r, byte g, byte b)
        {
            if (IsDefault) return (r, g, b);
            ToHsl(r, g, b, out var h, out var s, out var l);
            if (s < 0.02 || (r == g && g == b)) return (r, g, b);                     // greys and white stay greys
            if (l < 0.16 || s < 0.22) { h = Pick(BaseHue, h); s = Math.Min(1, s * BaseSat); }   // the dark page and its tints
            else if (h >= 300 || h < 12) { h = Pick(PrimaryHue, h); s = Math.Min(1, s * PrimarySat); }   // maroon / rose / plum
            else if (h < 70) { h = Pick(AccentHue, h); s = Math.Min(1, s * AccentSat); }   // amber / gold / warm text
            // keep the colour exactly as bright as the original (relative luminance), so every contrast the default
            // was designed with holds in every scheme: a green or cyan gets darker, a blue gets lighter
            double target = Lum(r, g, b), lo = 0, hi = 1;
            for (int i = 0; i < 24; i++)
            {
                double mid = (lo + hi) / 2;
                FromHsl(h, s, mid, out var tr, out var tg, out var tb);
                if (Lum(tr, tg, tb) < target) lo = mid; else hi = mid;
            }
            FromHsl(h, s, (lo + hi) / 2, out r, out g, out b);
            return (r, g, b);
        }

        static double Lum(byte r, byte g, byte b)
        {
            double f(byte v) { double x = v / 255.0; return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
            return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b);
        }

        static double Pick(double want, double have) => double.IsNaN(want) ? have : want;

        public static void ToHsl(byte R, byte G, byte B, out double h, out double s, out double l)
        {
            double r = R / 255.0, g = G / 255.0, b = B / 255.0, max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            l = (max + min) / 2; h = 0; s = 0;
            if (max == min) return;
            double d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0); else if (max == g) h = (b - r) / d + 2; else h = (r - g) / d + 4;
            h *= 60;
        }

        public static void FromHsl(double h, double s, double l, out byte r, out byte g, out byte b)
        {
            double C(double p, double q, double t) { if (t < 0) t += 1; if (t > 1) t -= 1; return t < 1 / 6.0 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2 / 3.0 ? p + (q - p) * (2 / 3.0 - t) * 6 : p; }
            if (s == 0) { r = g = b = (byte)Math.Round(l * 255); return; }
            double qq = l < 0.5 ? l * (1 + s) : l + s - l * s, pp = 2 * l - qq, hh = h / 360;
            r = (byte)Math.Round(C(pp, qq, hh + 1 / 3.0) * 255); g = (byte)Math.Round(C(pp, qq, hh) * 255); b = (byte)Math.Round(C(pp, qq, hh - 1 / 3.0) * 255);
        }

        /// <summary>WCAG contrast ratio of two opaque colours.</summary>
        public static double Contrast((byte r, byte g, byte b) x, (byte r, byte g, byte b) y)
        {
            double L((byte r, byte g, byte b) c) { double f(byte v) { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); } return 0.2126 * f(c.r) + 0.7152 * f(c.g) + 0.0722 * f(c.b); }
            double a = L(x), b = L(y);
            return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
        }
    }
}
