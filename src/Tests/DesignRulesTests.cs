using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace AndersCraftStudio.Tests
{
    /// <summary>
    /// The Studio design MO, enforced (ported from DMA Web Studio's DesignRulesTests): outside src/DesignSystem, screen code
    /// may not draw borders or separators, hard-code colours, or use the plain Windows message box. Every look comes from the
    /// shared design system. Lines that carry data colours (a colour picker's swatches) are marked "design-check: data".
    /// </summary>
    static class DesignRulesTests
    {
        /// <summary>Screens allowed to break the rules while a restyle is in progress. Empty: every screen follows the MO. A file on it that is already clean fails.</summary>
        internal static readonly HashSet<string> NotYetSwept = new(StringComparer.OrdinalIgnoreCase)
        {
        };

        static string RepoRoot()
        {
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "src", "AndersCraftStudio.sln"))) return d.FullName;
            throw new InvalidOperationException("Repository root not found.");
        }

        static IEnumerable<string> ScreenFiles()
        {
            var app = Path.Combine(RepoRoot(), "src", "App");
            foreach (var f in Directory.EnumerateFiles(app, "*.*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(app, f).Replace('\\', '/');
                if (rel.StartsWith("bin/") || rel.StartsWith("obj/")) continue;
                if (f.EndsWith(".cs") || f.EndsWith(".xaml")) yield return f;
            }
        }

        static readonly (string name, Regex rx)[] Rules =
        {
            ("a border thicker than 0 (use the design system: Fill.Bleed, layers, Surfaces.Float)", new Regex(@"\bBorderThickness\s*=\s*new Thickness\((?!\s*0\s*\))", RegexOptions.Compiled)),
            ("a border thicker than 0 in XAML", new Regex(@"BorderThickness=""(?!0"")", RegexOptions.Compiled)),
            ("a hard-coded colour (add a key to StudioDesignSystem.xaml)", new Regex(@"#[0-9A-Fa-f]{6}\b|Color\.From(A?Rgb|ScRgb)|BrushConverter|ColorConverter", RegexOptions.Compiled)),
            ("a solid separator line (use spacing or the Hairline brush inside the design system)", new Regex(@"U\.R\(""(Line|LineHi|Divider)""\)|new Separator\b|<Separator\b|Height\s*=\s*1\b.*Background", RegexOptions.Compiled)),
            ("the plain Windows message box (use StudioDialog)", new Regex(@"\bMessageBox\.Show\(", RegexOptions.Compiled)),
            ("Brushes.White/Black (use Text / TextStrong / layers)", new Regex(@"Brushes\.(White|Black)\b", RegexOptions.Compiled)),
        };

        static List<string> Problems(string f)
        {
            var problems = new List<string>();
            var lines = File.ReadAllLines(f);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Contains("design-check: data")) continue;
                var code = Regex.Replace(line, @"//.*$", "");   // comments may describe colours
                foreach (var (name, rx) in Rules)
                    if (rx.IsMatch(code)) problems.Add($"{Path.GetFileName(f)}:{i + 1}: {name}\n    {line.Trim()}");
            }
            return problems;
        }

        [Test]
        static void Screens_use_only_the_shared_design_system()
        {
            var problems = new List<string>();
            foreach (var f in ScreenFiles())
            {
                var p = Problems(f);
                if (NotYetSwept.Contains(Path.GetFileName(f)))
                    A.True(p.Count > 0, Path.GetFileName(f) + " is clean now: take it off DesignRulesTests.NotYetSwept");
                else problems.AddRange(p);
            }
            A.True(problems.Count == 0, "Design MO violations:\n" + string.Join("\n", problems));
        }

        [Test]
        static void The_design_system_defines_the_MO_tokens()
        {
            var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "DesignSystem", "StudioDesignSystem.xaml"));
            foreach (var key in new[] { "Layer5", "Layer10", "Layer15", "Layer20", "Layer40", "Hairline", "Fill.Bleed", "Fill.Row", "Mask.Feather", "Mask.FadeDown",
                                        "Accent.Gradient", "Radius.Card", "Radius.Control", "Radius.Tag", "Radius.Pill", "Motion.Fast", "Motion.Medium", "NavBtn", "Pill", "Chip" })
                A.Contains(xaml, $"x:Key=\"{key}\"");
            A.Contains(xaml, "<CornerRadius x:Key=\"Radius.Card\">16</CornerRadius>");
            A.Contains(xaml, "<CornerRadius x:Key=\"Radius.Control\">8</CornerRadius>");
            A.Contains(xaml, "<CornerRadius x:Key=\"Radius.Tag\">4</CornerRadius>");
            // AndersCraft keeps its own palette: dark maroon page, amber accent
            A.Contains(xaml, "<SolidColorBrush x:Key=\"Bg\" Color=\"#141113\"/>");
            A.Contains(xaml, "<SolidColorBrush x:Key=\"Panel\" Color=\"#1B1618\"/>");
            A.Contains(xaml, "<SolidColorBrush x:Key=\"Amber\" Color=\"#F0B46A\"/>");
        }

        [Test]
        static void The_suite_tokens_are_vendored_with_Studios_own_colours()
        {
            var tokens = File.ReadAllText(Path.Combine(RepoRoot(), "src", "DesignSystem", "Tokens.xaml"));
            foreach (var key in new[] { "Color.Canvas", "Color.Accent", "Color.AccentPink", "Layer.3", "Layer.5", "Layer.8", "Layer.10", "Layer.15", "Layer.20", "Layer.40", "Divider",
                                        "Text.Strong", "Text.Primary", "Text.Secondary", "Text.Disabled", "Accent.Gradient", "Bleed.Accent", "Bleed.AccentSoft", "Bleed.Hover", "Bleed.Rail", "Rail.Marker",
                                        "Text.Magic", "Glow.Title", "Glow.Text", "Glow.Card", "Glow.AccentSoft", "Glow.AccentStrong", "Shimmer", "Card.Glass", "Card.Sheen", "Focus.DoubleRing" })
                A.Contains(tokens, $"x:Key=\"{key}\"");
            A.Contains(tokens, "<Color x:Key=\"Color.Canvas\">#141113</Color>");
            A.Contains(tokens, "<Color x:Key=\"Color.Raised\">#1B1618</Color>");
            A.Contains(tokens, "<Color x:Key=\"Color.Accent\">#F0B46A</Color>");
            A.False(tokens.Contains("#F2A93B") || tokens.Contains("#F0588C") || tokens.Contains("#1A0B0E"), "no AndersMod colours left");
            var ds = File.ReadAllText(Path.Combine(RepoRoot(), "src", "DesignSystem", "StudioDesignSystem.xaml"));
            A.Contains(ds, "component/Tokens.xaml");
        }

        [Test]
        static void Reduced_motion_switches_the_magic_off()
        {
            var look = File.ReadAllText(Path.Combine(RepoRoot(), "src", "DesignSystem", "StudioLook.cs"));
            var magic = File.ReadAllText(Path.Combine(RepoRoot(), "src", "DesignSystem", "Magic.cs"));
            A.Contains(look, "SystemParameters.ClientAreaAnimation");
            A.Contains(magic, "if (!StudioLook.MotionEnabled)");           // aurora / shimmer don't animate
            A.Contains(magic, "StudioLook.MotionEnabled && breathe");      // halos don't breathe
            A.Contains(magic, "MaxMotes = 46");                            // ~46 motes (STUDIO_DESIGN_LANGUAGE §2.6)
            A.Contains(look, "w.Deactivated");                             // paused when unfocused / minimized
        }

        [Test]
        static void The_design_system_is_vendored_not_referenced_by_path()
        {
            var proj = File.ReadAllText(Path.Combine(RepoRoot(), "src", "App", "AndersCraftStudio.csproj"));
            A.Contains(proj, @"<ProjectReference Include=""..\DesignSystem\Studio.DesignSystem.csproj"" />");
            A.False(Regex.IsMatch(proj, @"Include=""[A-Za-z]:\\"), "absolute project paths would break the public build");
        }
    }
}
