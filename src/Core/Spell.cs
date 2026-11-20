using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AndersCraftStudio.Core
{
    /// <summary>One value of a spell enum (effect, aura, target, attribute bit…).</summary>
    public sealed class SpellEnum
    {
        public long Value;
        public string Name, Label, Desc, Summary;
        public bool Studio;   // label/description written by Studio (not only the generated name)
        public override string ToString() => $"{Value} · {Label}";
    }

    /// <summary>Spell enums from SpellCatalog.txt (generated from AzerothCore) with Studio's wording from SpellTexts.txt on top.</summary>
    public static class SpellCatalog
    {
        static Dictionary<string, List<SpellEnum>> kinds;
        static readonly object gate = new object();

        static string Resource(string name)
        {
            var asm = typeof(SpellCatalog).Assembly;
            var res = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(name, StringComparison.OrdinalIgnoreCase));
            if (res == null) return "";
            using var s = asm.GetManifestResourceStream(res);
            using var r = new StreamReader(s, Encoding.UTF8);
            return r.ReadToEnd();
        }

        static long ParseNum(string s) => s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToInt64(s.Substring(2), 16) : long.Parse(s, CultureInfo.InvariantCulture);

        static Dictionary<string, List<SpellEnum>> All()
        {
            lock (gate)
            {
                if (kinds != null) return kinds;
                var d = new Dictionary<string, List<SpellEnum>>(StringComparer.OrdinalIgnoreCase);
                List<SpellEnum> cur = null;
                foreach (var raw in Resource("SpellCatalog.txt").Split('\n'))
                {
                    var line = raw.TrimEnd('\r');
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    if (line.StartsWith("@")) { d[line.Substring(1)] = cur = new List<SpellEnum>(); continue; }
                    var p = line.Split('|');
                    if (cur == null || p.Length < 3) continue;
                    cur.Add(new SpellEnum { Value = ParseNum(p[0]), Name = p[1], Label = p[2], Desc = p.Length > 3 ? p[3] : "" });
                }
                foreach (var raw in Resource("SpellTexts.txt").Split('\n'))
                {
                    var line = raw.TrimEnd('\r');
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    var p = line.Split('|');
                    if (p.Length < 3) continue;
                    if (!d.TryGetValue(p[0], out var list)) d[p[0]] = list = new List<SpellEnum>();
                    long v = ParseNum(p[1]);
                    var e = list.FirstOrDefault(x => x.Value == v);
                    if (e == null) { e = new SpellEnum { Value = v, Name = "" }; list.Add(e); }
                    e.Label = p[2]; e.Studio = true;
                    if (p.Length > 3 && p[3].Length > 0) e.Desc = p[3];
                    if (p.Length > 4) e.Summary = p[4];
                }
                foreach (var l in d.Values) l.Sort((a, b) => a.Value.CompareTo(b.Value));
                return kinds = d;
            }
        }

        public static IReadOnlyList<SpellEnum> Of(string kind) => All().TryGetValue(kind, out var l) ? l : new List<SpellEnum>();
        public static SpellEnum Get(string kind, long v) => Of(kind).FirstOrDefault(e => e.Value == v);
        public static string Label(string kind, long v) => Get(kind, v)?.Label ?? (v == 0 ? "None" : $"Unknown ({v})");

        /// <summary>Names of the schools in a school mask ("Fire", "Frost and Fire"…).</summary>
        public static string Schools(long mask)
        {
            if (mask == 0) return "";
            if ((mask & 0x7F) == 0x7F) return "all schools";
            var names = Of("school").Where(s => (mask & s.Value) != 0).Select(s => s.Label).ToList();
            return names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " and " + names.Last();
        }
    }

    /// <summary>Named access to one Spell.dbc row through the M4 layout engine.</summary>
    public sealed class SpellData
    {
        public DbcTable Table { get; }
        public DbcRow Row { get; }
        public SpellData(DbcTable t, DbcRow r) { Table = t; Row = r; }

        public int Field(string name)
        {
            var f = Table.Layout.FieldByName(name);
            if (f < 0) { var c = Table.Layout.Columns.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)); f = c?.Field ?? -1; }
            return f;
        }
        public uint U(string name) { var f = Field(name); return f < 0 ? 0 : Row.V[f]; }
        public int I(string name) => unchecked((int)U(name));
        public float F(string name) => BitConverter.Int32BitsToSingle(unchecked((int)U(name)));
        public string S(string name) { var f = Field(name); return f < 0 ? "" : Row.S[f] ?? ""; }
        public string Text(string name) { var f = Field(name); return f < 0 ? "" : Table.FieldText(Row, f); }
        public bool Set(string name, string text, out string error) { error = "No such field: " + name; var f = Field(name); return f >= 0 && Table.TrySetField(Row, f, text, out error); }

        public uint Id => Row.V[0];
        public string Name => S("Name_enUS");
        public string Rank => S("NameSubtext_enUS");

        // effects are 1-based in the column names (Effect_1 … Effect_3)
        public int Effect(int i) => I("Effect_" + i);
        public int Aura(int i) => I("EffectAura_" + i);
        public int BasePoints(int i) => I("EffectBasePoints_" + i);
        public int DieSides(int i) => I("EffectDieSides_" + i);
        public int Misc(int i) => I("EffectMiscValue_" + i);
        public int Period(int i) => I("EffectAuraPeriod_" + i);
        public uint Trigger(int i) => U("EffectTriggerSpell_" + i);
        public uint ItemType(int i) => U("EffectItemType_" + i);
        public uint RadiusIndex(int i) => U("EffectRadiusIndex_" + i);
        public int Chain(int i) => I("EffectChainTargets_" + i);
        public float Multiple(int i) => F("EffectMultipleValue_" + i);
        public float PerLevel(int i) => F("EffectRealPointsPerLevel_" + i);

        /// <summary>The (min, max) an effect rolls: BasePoints + 1 … BasePoints + DieSides (3.3.5a rule).</summary>
        public (int min, int max) Points(int i)
        {
            int bp = BasePoints(i), die = DieSides(i);
            return die <= 1 ? (bp + die, bp + die) : (bp + 1, bp + die);
        }
    }

    /// <summary>Lookups the tooltip needs from related DBCs and names. Each returns null/0 when unknown.</summary>
    public interface ISpellRefs
    {
        /// <summary>(min, max) yards, friendly/hostile max taken as the larger.</summary>
        (float min, float max, string name)? Range(uint id);
        int? CastTime(uint id);       // ms
        int? Duration(uint id);       // ms, -1 = until cancelled
        float? Radius(uint id);       // yards
        string SpellName(uint id);
        string ItemName(uint id);
        SpellData Spell(uint id);     // other spells for $12345s1 references
        /// <summary>SpellDescriptionVariables text ("$name=expr" lines) or null.</summary>
        string Variables(uint id);
        string CreatureName(uint id);
    }

    /// <summary>ISpellRefs over DBC tables (loaded on first use from a loader, e.g. the selected DBC source).</summary>
    public sealed class DbcSpellRefs : ISpellRefs
    {
        readonly Func<string, DbcTable> load;
        readonly Dictionary<string, DbcTable> cache = new Dictionary<string, DbcTable>(StringComparer.OrdinalIgnoreCase);
        public Func<uint, string> Items { get; set; }
        public DbcTable SpellTable { get; set; }
        public DbcSpellRefs(Func<string, DbcTable> load) { this.load = load; }

        public DbcTable Get(string file)
        {
            if (string.Equals(file, "Spell.dbc", StringComparison.OrdinalIgnoreCase) && SpellTable != null) return SpellTable;
            if (!cache.TryGetValue(file, out var t)) { try { t = load(file); } catch { t = null; } cache[file] = t; }
            return t;
        }
        /// <summary>Use a working copy (with unstaged edits) instead of the file, e.g. for SpellRange while editing.</summary>
        public void Override(DbcTable t) => cache[t.File] = t;

        DbcRow Row(string file, uint id) => id == 0 ? null : Get(file)?.Find(id);
        float Fl(DbcTable t, DbcRow r, string col) { var f = t.Layout.FieldByName(col); return f < 0 ? 0 : BitConverter.Int32BitsToSingle(unchecked((int)r.V[f])); }
        int In(DbcTable t, DbcRow r, string col) { var f = t.Layout.FieldByName(col); return f < 0 ? 0 : unchecked((int)r.V[f]); }

        public (float min, float max, string name)? Range(uint id)
        {
            var t = Get("SpellRange.dbc"); var r = Row("SpellRange.dbc", id); if (r == null) return null;
            var nf = t.Layout.FieldByName("DisplayName_enUS");
            return (Math.Max(Fl(t, r, "RangeMin_1"), Fl(t, r, "RangeMin_2")), Math.Max(Fl(t, r, "RangeMax_1"), Fl(t, r, "RangeMax_2")), nf < 0 ? "" : r.S[nf]);
        }
        public int? CastTime(uint id) { var t = Get("SpellCastTimes.dbc"); var r = Row("SpellCastTimes.dbc", id); return r == null ? (int?)null : In(t, r, "Base"); }
        public int? Duration(uint id) { var t = Get("SpellDuration.dbc"); var r = Row("SpellDuration.dbc", id); return r == null ? (int?)null : In(t, r, "Duration"); }
        public float? Radius(uint id) { var t = Get("SpellRadius.dbc"); var r = Row("SpellRadius.dbc", id); return r == null ? (float?)null : Fl(t, r, "Radius"); }
        public string SpellName(uint id) { var sp = Spell(id); return sp?.Name; }
        public string ItemName(uint id) => Items?.Invoke(id);
        public SpellData Spell(uint id) { var t = Get("Spell.dbc"); var r = t?.Find(id); return r == null ? null : new SpellData(t, r); }
        public Func<uint, string> Creatures { get; set; }
        public string CreatureName(uint id) => Creatures?.Invoke(id);
        public string Variables(uint id)
        {
            var t = Get("SpellDescriptionVariables.dbc"); var r = id == 0 ? null : t?.Find(id);
            if (r == null) return null;
            for (int f = 1; f < t.Layout.FieldCount; f++) if (t.Layout.FieldIsString[f] && !string.IsNullOrEmpty(r.S[f])) return r.S[f];
            return null;
        }
    }

    /// <summary>The in-game style tooltip and plain-English effect summaries.</summary>
    public static class SpellTooltip
    {
        public sealed class Tip
        {
            public string Name, Rank, Cost, Range, Cast, Cooldown, Description, AuraText;
            public List<string> Effects = new List<string>();
            public List<string> Lines()
            {
                var l = new List<string> { Name + (string.IsNullOrEmpty(Rank) ? "" : "  (" + Rank + ")") };
                if (!string.IsNullOrEmpty(Cost) || !string.IsNullOrEmpty(Range)) l.Add(string.Join("    ", new[] { Cost, Range }.Where(x => !string.IsNullOrEmpty(x))));
                if (!string.IsNullOrEmpty(Cast) || !string.IsNullOrEmpty(Cooldown)) l.Add(string.Join("    ", new[] { Cast, Cooldown }.Where(x => !string.IsNullOrEmpty(x))));
                if (!string.IsNullOrEmpty(Description)) l.Add(Description);
                return l;
            }
        }

        public static string Seconds(int ms)
        {
            if (ms < 0) return "until cancelled";
            double s = ms / 1000.0;
            if (s >= 3600 && s % 3600 == 0) return $"{s / 3600:0.#} hr";
            if (s >= 60 && s % 60 == 0) return $"{s / 60:0.#} min";
            return s.ToString("0.##", CultureInfo.InvariantCulture) + " sec";
        }

        static string Yards(float y) => y.ToString("0.##", CultureInfo.InvariantCulture) + " yd";

        public static string CostText(SpellData s)
        {
            int power = s.I("PowerType"), cost = s.I("ManaCost"), pct = s.I("ManaCostPct"), runes = s.I("RuneCostID");
            if (pct > 0) return $"{pct}% of base {PowerName(power).ToLowerInvariant()}";
            if (cost <= 0) return power == 5 && runes > 0 ? "Runes" : "";
            int shown = power == 1 || power == 6 ? cost / 10 : cost;
            return $"{shown} {PowerName(power)}" + (s.I("ManaPerSecond") > 0 ? $", plus {s.I("ManaPerSecond")} per sec" : "");
        }

        public static string PowerName(int p) => p == -2 ? "Health" : SpellCatalog.Get("power", p)?.Label ?? "Power " + p;

        public static Tip Build(SpellData s, ISpellRefs refs)
        {
            var t = new Tip { Name = s.Name, Rank = s.Rank, Cost = CostText(s) };
            var r = refs.Range(s.U("RangeIndex"));
            if (r != null)
            {
                var (min, max, name) = r.Value;
                t.Range = max <= 0 ? "" : max >= 50000 ? "Unlimited range" : max <= 5.5f ? "Melee range" : min > 0 ? $"{min:0.#}-{max:0.#} yd range" : $"{max:0.#} yd range";
            }
            var ct = refs.CastTime(s.U("CastingTimeIndex")) ?? 0;
            bool channeled = (s.U("AttributesEx") & 0x44) != 0;   // channeled 1 / channeled 2
            t.Cast = channeled ? "Channeled" : ct <= 0 ? "Instant" : Seconds(ct) + " cast";
            if ((s.U("Attributes") & 0x40) != 0) t.Cast = "Passive";
            int cd = Math.Max(s.I("RecoveryTime"), s.I("CategoryRecoveryTime"));
            if (cd > 0) t.Cooldown = Seconds(cd) + " cooldown";
            t.Description = Substitute(s.S("Description_enUS"), s, refs);
            t.AuraText = Substitute(s.S("AuraDescription_enUS"), s, refs);
            for (int i = 1; i <= 3; i++) { var e = EffectSummary(s, i, refs); if (e != null) t.Effects.Add(e); }
            if (string.IsNullOrWhiteSpace(t.Description) && t.Effects.Count > 0) t.Description = string.Join(". ", t.Effects) + ".";
            return t;
        }

        static string PointsText(SpellData s, int i)
        {
            var (a, b) = s.Points(i);
            return a == b ? Math.Abs(a).ToString(CultureInfo.InvariantCulture) : $"{Math.Abs(a)} to {Math.Abs(b)}";
        }

        /// <summary>Plain English for one effect (1-3), or null when the slot is empty.</summary>
        public static string EffectSummary(SpellData s, int i, ISpellRefs refs)
        {
            int eff = s.Effect(i);
            if (eff == 0) return null;
            var e = SpellCatalog.Get("effect", eff);
            string template = e?.Summary;
            string text;
            if (eff == 6 || eff == 27 || eff == 35 || eff == 65 || eff == 119 || eff == 128 || eff == 129 || eff == 143)
            {
                var a = SpellCatalog.Get("aura", s.Aura(i));
                var auraText = a?.Summary != null ? Fill(a.Summary, s, i, refs) : (a != null ? a.Label + (DurationText(s, refs) is string d && d.Length > 0 ? " for " + d : "") : "Aura " + s.Aura(i));
                text = template != null && template.Contains("{aura}") ? Fill(template.Replace("{aura}", auraText), s, i, refs) : auraText;
            }
            else text = template != null ? Fill(template, s, i, refs) : (e?.Label ?? "Effect " + eff);
            var tgt = TargetText(s.I("ImplicitTargetA_" + i), s.I("ImplicitTargetB_" + i));
            int chain = s.Chain(i);
            if (chain > 1) text += $", jumping to up to {chain} targets";
            return Cap(text) + (tgt.Length > 0 ? " (" + tgt + ")" : "");
        }

        static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        public static string TargetText(int a, int b)
        {
            if (a == 0 && b == 0) return "";
            var ta = a == 0 ? null : SpellCatalog.Get("target", a);
            var tb = b == 0 ? null : SpellCatalog.Get("target", b);
            if (tb != null && (a == 22 || a == 18 || a == 0)) return tb.Label.ToLowerInvariant();
            return (ta?.Label ?? "target " + a).ToLowerInvariant() + (tb != null ? ", then " + tb.Label.ToLowerInvariant() : "");
        }

        static string DurationText(SpellData s, ISpellRefs refs)
        {
            var d = refs.Duration(s.U("DurationIndex"));
            return d == null || d == 0 ? "" : Seconds(d.Value);
        }

        static string Fill(string tpl, SpellData s, int i, ISpellRefs refs)
        {
            string school = SpellCatalog.Schools(s.U("SchoolMask"));
            int period = s.Period(i);
            var dur = refs.Duration(s.U("DurationIndex"));
            var radius = refs.Radius(s.RadiusIndex(i));
            string misc = s.Misc(i).ToString(CultureInfo.InvariantCulture);
            int eff = s.Effect(i);
            if (eff == 8 || eff == 30 || eff == 62 || eff == 137 || s.Aura(i) == 24 || s.Aura(i) == 35 || s.Aura(i) == 85) misc = PowerName(s.Misc(i)).ToLowerInvariant();
            if (eff == 38 || eff == 126) misc = (DispelName(s.Misc(i)) ?? misc).ToLowerInvariant();
            int aura = s.Aura(i);
            bool auraEffect = eff == 6 || eff == 27 || eff == 35 || eff == 65 || eff == 119 || eff == 128 || eff == 129 || eff == 143;
            if (auraEffect && (aura == 29 || aura == 80 || aura == 137)) misc = StatName(s.Misc(i));
            if (auraEffect && aura == 77) misc = SpellCatalog.Get("mechanic", s.Misc(i))?.Label.ToLowerInvariant() ?? misc;
            if (auraEffect && aura == 39) misc = SpellCatalog.Schools(s.Misc(i));
            if (eff == 28 || eff == 56 || (auraEffect && aura == 56)) misc = refs.CreatureName((uint)s.Misc(i)) is string cn && cn.Length > 0 ? $"{cn} ({s.Misc(i)})" : "creature " + misc;
            var trig = s.Trigger(i);
            var res = tpl
                .Replace("{pts}", PointsText(s, i))
                .Replace("{per}", PointsText(s, i))
                .Replace("{school}", school.Length > 0 ? school : "physical")
                .Replace("{period}", period > 0 ? Seconds(period) : "tick")
                .Replace("{dur}", dur == null || dur == 0 ? "the duration" : Seconds(dur.Value))
                .Replace("{trigger}", trig == 0 ? "nothing" : (refs.SpellName(trig) is string tn && tn.Length > 0 ? $"{tn} ({trig})" : "spell " + trig))
                .Replace("{item}", s.ItemType(i) == 0 ? "nothing" : (refs.ItemName(s.ItemType(i)) ?? "item " + s.ItemType(i)))
                .Replace("{misc}", misc)
                .Replace("{radius}", radius == null || radius == 0 ? "the area" : Yards(radius.Value))
                .Replace("{chain}", s.Chain(i).ToString(CultureInfo.InvariantCulture))
                .Replace("{mult}", s.Multiple(i).ToString("0.##", CultureInfo.InvariantCulture))
                .Replace("{procchance}", s.U("ProcChance").ToString(CultureInfo.InvariantCulture));
            res = res.Replace(" for the duration", "");
            return res;
        }

        public static string StatName(int v) => v switch { -1 => "all stats", 0 => "Strength", 1 => "Agility", 2 => "Stamina", 3 => "Intellect", 4 => "Spirit", _ => "stat " + v };

        static string DispelName(int v) => v switch { 1 => "Magic", 2 => "Curse", 3 => "Disease", 4 => "Poison", 5 => "Stealth", 6 => "Invisibility", 9 => "Enrage", _ => null };

        // ---------- description tokens ----------
        static readonly Regex Token = new Regex(@"\$(\d+)?([a-zA-Z])(\d)?", RegexOptions.Compiled);

        /// <summary>
        /// Fills the client's description tokens: $s1 amount, $m1/$M1 min/max, $o1 total over time, $t1 period, $d duration,
        /// $a1 radius, $h proc chance, $n charges, $x1 chain targets, $u stacks, $e1 multiple value, $b1 per combo point,
        /// $i max targets, $r range, $12345s1 another spell; ${…} sums are worked out; $lsingular:plural; and $gmale:female;
        /// pick the first form; $?condition[a][b] picks b. Unknown tokens are left as written.
        /// </summary>
        public static string Substitute(string text, SpellData s, ISpellRefs refs) => Substitute(text, s, refs, 0);

        static readonly Regex VarRef = new Regex(@"\$<(\w+)>", RegexOptions.Compiled);

        /// <summary>The spell's description variables ($name=expr), each already filled in.</summary>
        static Dictionary<string, string> Variables(SpellData s, ISpellRefs refs, int depth)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var text = refs.Variables(s.U("SpellDescriptionVariableID"));
            if (string.IsNullOrEmpty(text) || depth > 3) return d;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                int eq = line.IndexOf('=');
                if (!line.StartsWith("$") || eq < 2) continue;
                var name = line.Substring(1, eq - 1);
                var expr = VarRef.Replace(line.Substring(eq + 1), m => d.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
                d[name] = Substitute(expr, s, refs, depth + 1);
            }
            return d;
        }

        static string Substitute(string text, SpellData s, ISpellRefs refs, int depth)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            if (text.Contains("$<"))
            {
                var vars = Variables(s, refs, depth);
                text = VarRef.Replace(text, m => vars.TryGetValue(m.Groups[1].Value, out var v) ? v : "…");
            }
            var sb = new StringBuilder();
            int pos = 0;
            while (pos < text.Length)
            {
                int d = text.IndexOf('$', pos);
                if (d < 0) { sb.Append(text, pos, text.Length - pos); break; }
                sb.Append(text, pos, d - pos);
                pos = d;
                if (pos + 1 < text.Length && text[pos + 1] == '{')
                {
                    int end = text.IndexOf('}', pos);
                    if (end < 0) { sb.Append(text.Substring(pos)); break; }
                    var inner = text.Substring(pos + 2, end - pos - 2);
                    int after = end + 1;
                    int decimals = 0;
                    if (after + 1 < text.Length && text[after] == '.' && char.IsDigit(text[after + 1])) { decimals = text[after + 1] - '0'; after += 2; }
                    var expr = Token.Replace(inner, m => Value(m, s, refs, out var v) ? v.ToString(CultureInfo.InvariantCulture) : m.Value);
                    sb.Append(Eval(expr, out var num) ? Math.Abs(num).ToString("F" + decimals, CultureInfo.InvariantCulture) : "…");
                    pos = after;
                    continue;
                }
                if (pos + 1 < text.Length && (text[pos + 1] == 'l' || text[pos + 1] == 'L' || text[pos + 1] == 'g' || text[pos + 1] == 'G'))
                {
                    int semi = text.IndexOf(';', pos);
                    int colon = semi < 0 ? -1 : text.IndexOf(':', pos, semi - pos);
                    if (semi > 0 && colon > 0)
                    {
                        // $l: plural form (spells mostly use plural), $g: first (male) form
                        bool plural = text[pos + 1] == 'l' || text[pos + 1] == 'L';
                        sb.Append(plural ? text.Substring(colon + 1, semi - colon - 1) : text.Substring(pos + 2, colon - pos - 2));
                        pos = semi + 1;
                        continue;
                    }
                }
                if (pos + 1 < text.Length && text[pos + 1] == '?')
                {
                    // $?condition[then][else] → else (Studio can't know the player's talents/auras)
                    int b1 = text.IndexOf('[', pos);
                    int e1 = b1 < 0 ? -1 : MatchBracket(text, b1);
                    if (e1 > 0 && e1 + 1 < text.Length && text[e1 + 1] == '[')
                    {
                        int e2 = MatchBracket(text, e1 + 1);
                        if (e2 > 0) { sb.Append(Substitute(text.Substring(e1 + 2, e2 - e1 - 2), s, refs, depth + 1)); pos = e2 + 1; continue; }
                    }
                }
                var m2 = Token.Match(text, pos);
                if (m2.Success && m2.Index == pos)
                {
                    if (Value(m2, s, refs, out var v, true)) sb.Append(FormatValue(m2, v, s, refs));
                    else sb.Append(m2.Value);
                    pos += m2.Length;
                    continue;
                }
                sb.Append('$'); pos++;
            }
            return sb.ToString();
        }

        static int MatchBracket(string t, int open)
        {
            int depth = 0;
            for (int i = open; i < t.Length; i++) { if (t[i] == '[') depth++; else if (t[i] == ']' && --depth == 0) return i; }
            return -1;
        }

        static string FormatValue(Match m, double v, SpellData s, ISpellRefs refs)
        {
            char c = char.ToLowerInvariant(m.Groups[2].Value[0]);
            if (c == 'd') return v < 0 ? "until cancelled" : Seconds((int)v);
            if (c == 's')
            {
                int i = m.Groups[3].Success ? m.Groups[3].Value[0] - '0' : 1;
                var target = m.Groups[1].Success && uint.TryParse(m.Groups[1].Value, out var other) ? refs.Spell(other) : s;
                if (target != null && i >= 1 && i <= 3) return PointsText(target, i);
            }
            return Math.Abs(v).ToString("0.##", CultureInfo.InvariantCulture);
        }

        /// <summary>Numeric value of a $ token (for ${…} sums and plain tokens). Durations are in ms.</summary>
        static bool Value(Match m, SpellData self, ISpellRefs refs, out double v, bool display = false)
        {
            v = 0;
            var s = self;
            if (m.Groups[1].Success)
            {
                if (!uint.TryParse(m.Groups[1].Value, out var other)) return false;
                s = refs.Spell(other);
                if (s == null) return false;
            }
            char c = m.Groups[2].Value[0];
            int i = m.Groups[3].Success ? m.Groups[3].Value[0] - '0' : 1;
            if (i < 1 || i > 3) i = 1;
            switch (c)
            {
                case 's': case 'S': case 'm': v = s.Points(i).min; return true;
                case 'M': v = s.Points(i).max; return true;
                case 't': case 'T': v = s.Period(i) / 1000.0; return true;
                case 'o': case 'O':
                {
                    var d = refs.Duration(s.U("DurationIndex")) ?? 0; int p = s.Period(i);
                    v = p > 0 && d > 0 ? s.Points(i).min * (d / p) : s.Points(i).min; return true;
                }
                case 'd': case 'D': { var d = refs.Duration(s.U("DurationIndex")); if (d == null) return false; v = display ? d.Value : d.Value / 1000.0; return true; }
                case 'a': case 'A': { var r = refs.Radius(s.RadiusIndex(i)); if (r == null) return false; v = r.Value; return true; }
                case 'h': case 'H': v = s.U("ProcChance"); return true;
                case 'n': case 'N': v = s.U("ProcCharges"); return true;
                case 'x': case 'X': v = s.Chain(i); return true;
                case 'u': case 'U': v = s.U("CumulativeAura"); return true;
                case 'e': case 'E': v = s.Multiple(i); return true;
                case 'b': case 'B': v = s.F("EffectPointsPerCombo_" + i); return true;
                case 'i': case 'I': v = s.U("MaxTargets"); return true;
                case 'q': case 'Q': v = s.Misc(i); return true;
                case 'r': case 'R': { var r = refs.Range(s.U("RangeIndex")); if (r == null) return false; v = r.Value.max; return true; }
                default: return false;
            }
        }

        /// <summary>Evaluates + - * / and parentheses (what ${…} uses). False if the text isn't a sum.</summary>
        public static bool Eval(string expr, out double v)
        {
            v = 0;
            int p = 0;
            try { v = Sum(expr.Replace(" ", ""), ref p); return p == expr.Replace(" ", "").Length && !double.IsNaN(v) && !double.IsInfinity(v); }
            catch { return false; }
        }
        static double Sum(string e, ref int p) { var v = Prod(e, ref p); while (p < e.Length && (e[p] == '+' || e[p] == '-')) { var op = e[p++]; var r = Prod(e, ref p); v = op == '+' ? v + r : v - r; } return v; }
        static double Prod(string e, ref int p) { var v = Atom(e, ref p); while (p < e.Length && (e[p] == '*' || e[p] == '/')) { var op = e[p++]; var r = Atom(e, ref p); v = op == '*' ? v * r : v / r; } return v; }
        static double Atom(string e, ref int p)
        {
            if (p < e.Length && e[p] == '-') { p++; return -Atom(e, ref p); }
            if (p < e.Length && e[p] == '(') { p++; var v = Sum(e, ref p); if (p >= e.Length || e[p] != ')') throw new FormatException(); p++; return v; }
            int st = p;
            while (p < e.Length && (char.IsDigit(e[p]) || e[p] == '.')) p++;
            if (st == p) throw new FormatException();
            return double.Parse(e.Substring(st, p - st), CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Server-side support: the world database's spell_dbc table (AzerothCore loads its rows into the spell store).</summary>
    public static class SpellServerRow
    {
        public const string Table = "spell_dbc";

        /// <summary>spell_dbc column name for a Spell.dbc field (Name_enUS → Name_Lang_enUS, Name_Mask → Name_Lang_Mask).</summary>
        public static string Column(DbcLayout l, int field)
        {
            var c = l.ColumnOf(field);
            if (c.Kind != DbcKind.Loc) return c.Name;
            int k = field - c.Field;
            return c.Name + "_Lang_" + (k < 16 ? DbcLayout.Locales[k] : "Mask");
        }

        /// <summary>
        /// Stages INSERT INTO spell_dbc for this spell (undo: DELETE). Only columns present in <paramref name="tableColumns"/>
        /// are written when it is given (the live table's columns); otherwise every Spell.dbc field is written.
        /// </summary>
        public static Change Stage(Changeset cs, SpellData s, IList<string> tableColumns = null)
        {
            var l = s.Table.Layout;
            var have = tableColumns == null ? null : new HashSet<string>(tableColumns, StringComparer.OrdinalIgnoreCase);
            var cols = new List<string>(); var lits = new List<string>();
            for (int f = 0; f < l.FieldCount; f++)
            {
                var name = Column(l, f);
                if (have != null && !have.Contains(name)) continue;
                cols.Add(name);
                if (l.FieldIsString[f]) lits.Add(SqlGen.Literal(s.Row.S[f] ?? ""));
                else
                {
                    var kind = l.ColumnOf(f).Kind;
                    uint v = s.Row.V[f];
                    lits.Add(kind == DbcKind.Float ? SqlGen.Literal(BitConverter.Int32BitsToSingle(unchecked((int)v)))
                        : kind == DbcKind.Int ? unchecked((int)v).ToString(CultureInfo.InvariantCulture)
                        : v.ToString(CultureInfo.InvariantCulture));
                }
            }
            var key = new List<KeyPart> { new KeyPart(cols.Count > 0 ? cols[0] : "ID", s.Id.ToString(CultureInfo.InvariantCulture)) };
            cs.Changes.RemoveAll(c => c.Kind == ChangeKind.Insert && c.SameRow(Table, key));
            var ch = ChangeFactory.StageRowInsert(cs, Table, key, cols, lits);
            ch.Category = "Spells";
            ch.Summary = $"Server-side spell {s.Id} ({s.Name}) in spell_dbc";
            ch.UndoNote = "Undo deletes the spell_dbc row again.";
            // the diff only needs the meaningful values, not 234 zeros
            ch.Fields.RemoveAll(f => f.After == "0" || f.After == "''");
            return ch;
        }
    }

    public static class SpellIds
    {
        /// <summary>First ID at or above start that isn't in Spell.dbc, not staged as a new Spell.dbc row and not in <paramref name="alsoUsed"/> (e.g. spell_dbc IDs).</summary>
        public static uint FreeId(DbcTable spells, Changeset pending, uint start, ISet<uint> alsoUsed = null)
        {
            var used = new HashSet<uint>(spells.Rows.Select(r => r.V[0]));
            foreach (var c in pending.Changes.Where(c => DbcStager.IsDbc(c) && string.Equals(c.Table, "Spell.dbc", StringComparison.OrdinalIgnoreCase))) used.Add(c.Dbc.Key);
            if (alsoUsed != null) used.UnionWith(alsoUsed);
            var id = Math.Max(1u, start);
            while (used.Contains(id)) id++;
            return id;
        }
    }
}
