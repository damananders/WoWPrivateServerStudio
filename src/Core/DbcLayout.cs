using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace AndersCraftStudio.Core
{
    public enum DbcKind { UInt, Int, Float, String, Loc, Byte, Flags }

    /// <summary>One visible column of a DBC table. A localized string (Loc) spans 17 fields: 16 locales + a mask.</summary>
    public sealed class DbcColumn
    {
        public string Name { get; set; }
        public DbcKind Kind { get; set; }
        public string Hint { get; set; }
        /// <summary>First field index in the record.</summary>
        public int Field { get; set; }
        /// <summary>Studio guessed the type from the data (no published layout, or the published one didn't fit).</summary>
        public bool Guessed { get; set; }
        /// <summary>Foreign key: "Spell.dbc" or "db:item" / "db:creature" / "db:quest". Null if none.</summary>
        public string Link { get; set; }
        public int FieldCount => Kind == DbcKind.Loc ? 17 : 1;
        public string TypeName => Kind switch
        {
            DbcKind.UInt => "uint", DbcKind.Int => "int", DbcKind.Float => "float", DbcKind.String => "string",
            DbcKind.Loc => "localized string", DbcKind.Byte => "byte", DbcKind.Flags => "flags", _ => "?",
        };
        public override string ToString() => Name;
    }

    /// <summary>
    /// Column layout of a 3.3.5a (build 12340) DBC. Layouts come from DbcLayouts.txt (generated from AzerothCore's DBC
    /// definitions) and DbcLayoutsExtra.txt (Studio's own), each checked against the real file before use; anything
    /// else gets a layout guessed from the data ("raw mode").
    /// </summary>
    public sealed class DbcLayout
    {
        public static readonly string[] Locales = { "enUS", "enGB", "koKR", "frFR", "deDE", "enCN", "zhCN", "enTW", "zhTW", "esES", "esMX", "ruRU", "ptPT", "ptBR", "itIT", "Unk" };

        public string File { get; private set; }
        public List<DbcColumn> Columns { get; private set; } = new List<DbcColumn>();
        public int FieldCount { get; private set; }
        public int RecordSize { get; private set; }
        public int[] FieldOffset { get; private set; }
        public int[] FieldSize { get; private set; }
        /// <summary>Per field: true when it holds a string-block offset.</summary>
        public bool[] FieldIsString { get; private set; }
        /// <summary>"catalog" (published layout, verified against the file), "guessed" (raw mode) or "catalog+guess".</summary>
        public string Origin { get; private set; }
        /// <summary>Why a catalog layout was not used, if it wasn't.</summary>
        public string Note { get; private set; }
        /// <summary>True when every string field is known for sure, so the string block can be rebuilt from scratch.</summary>
        public bool StringsCertain { get; private set; }
        /// <summary>Rows are identified by field 0 (an ID). False for tables without one (gt*.dbc): rows are identified by position.</summary>
        public bool HasId { get; private set; }

        public string KeyName => HasId ? Columns[0].Name : "Row";

        // ---------- catalog ----------
        sealed class CatalogEntry { public List<(string name, char type, string hint)> Cols = new List<(string, char, string)>(); }

        /// <summary>Columns typed 'B' in the catalog: one byte when the record size needs it, otherwise 4-byte ints.</summary>
        readonly List<DbcColumn> maybeBytes = new List<DbcColumn>();
        static Dictionary<string, CatalogEntry> catalog;
        static Dictionary<string, string> descriptions;
        static readonly object gate = new object();

        static string Resource(string name)
        {
            var asm = typeof(DbcLayout).Assembly;
            var res = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(name, StringComparison.OrdinalIgnoreCase));
            if (res == null) return "";
            using var s = asm.GetManifestResourceStream(res);
            using var r = new StreamReader(s, Encoding.UTF8);
            return r.ReadToEnd();
        }

        static Dictionary<string, CatalogEntry> Catalog()
        {
            lock (gate)
            {
                if (catalog != null) return catalog;
                var d = new Dictionary<string, CatalogEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (var text in new[] { Resource("DbcLayouts.txt"), Resource("DbcLayoutsExtra.txt") })
                {
                    CatalogEntry cur = null;
                    foreach (var raw in text.Split('\n'))
                    {
                        var line = raw.TrimEnd('\r');
                        if (line.Length == 0 || line.StartsWith("#")) continue;
                        if (line.StartsWith("@")) { cur = new CatalogEntry(); d[line.Substring(1).Trim()] = cur; continue; }
                        if (cur == null) continue;
                        var p = line.Split('|');
                        if (p.Length < 2 || p[1].Length != 1 || "uifsLbBx".IndexOf(p[1][0]) < 0) continue;
                        cur.Cols.Add((p[0], p[1][0], p.Length > 2 ? string.Join("|", p.Skip(2)) : ""));
                    }
                }
                return catalog = d;
            }
        }

        public static bool InCatalog(string file) => Catalog().ContainsKey(file);
        public static IEnumerable<string> CatalogFiles => Catalog().Keys;

        /// <summary>One-line plain-English description of a DBC (Studio's own wording).</summary>
        public static string Describe(string file)
        {
            lock (gate)
            {
                if (descriptions == null)
                {
                    descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var raw in Resource("DbcDescriptions.txt").Split('\n'))
                    {
                        var line = raw.TrimEnd('\r');
                        int i = line.IndexOf('|');
                        if (i > 0 && !line.StartsWith("#")) descriptions[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                    }
                }
                return descriptions.TryGetValue(file, out var s) ? s : "No description yet (raw mode: column types are guessed from the data).";
            }
        }

        /// <summary>The catalog hint for a field name (Name_enUS → Name's hint). Empty when unknown.</summary>
        public static string HintOf(string file, string fieldName)
        {
            if (!Catalog().TryGetValue(file ?? "", out var ce) || fieldName == null) return "";
            foreach (var (name, t, hint) in ce.Cols)
                if (string.Equals(name, fieldName, StringComparison.OrdinalIgnoreCase) || (t == 'L' && fieldName.StartsWith(name + "_", StringComparison.OrdinalIgnoreCase)))
                    return (t == 'L' ? (fieldName.EndsWith("_Mask") ? "Language mask of " + name + ". " : "Text in " + fieldName.Substring(name.Length + 1) + ". ") : "") + hint;
            return "";
        }

        /// <summary>Every DBC Studio has a description for (all 246 of 3.3.5a).</summary>
        public static IEnumerable<string> DescribedFiles { get { Describe(""); lock (gate) return descriptions.Keys.ToList(); } }

        // ---------- building ----------
        /// <summary>The layout to use for this file: the catalog layout if it fits the real data, otherwise a guess.</summary>
        public static DbcLayout For(string file, DbcFile f)
        {
            file = Path.GetFileName(file);
            if (Catalog().TryGetValue(file, out var ce))
            {
                var l = FromCatalog(file, ce, f, out var why);
                if (l != null) return l;
                var g = Guess(file, f);
                g.Note = why;
                return g;
            }
            return Guess(file, f);
        }

        static DbcLayout FromCatalog(string file, CatalogEntry ce, DbcFile f, out string why)
        {
            why = null;
            var l = new DbcLayout { File = file, Origin = "catalog" };
            int field = 0;
            foreach (var (name, t, hint) in ce.Cols)
            {
                var kind = t switch { 'B' => DbcKind.Int, 'u' => DbcKind.UInt, 'i' => DbcKind.Int, 'f' => DbcKind.Float, 's' => DbcKind.String, 'L' => DbcKind.Loc, 'b' => DbcKind.Byte, 'x' => DbcKind.Flags, _ => DbcKind.UInt };
                var col = new DbcColumn { Name = name, Kind = kind, Hint = hint, Field = field };
                if (t == 'B') l.maybeBytes.Add(col);
                l.Columns.Add(col);
                field += kind == DbcKind.Loc ? 17 : 1;
            }
            if (field == f.Fields + 1 && l.Columns.Count > 1 && l.Columns[0].Name == "ID" && l.Columns[1].Kind != DbcKind.Loc)
            {
                // The server's copy of some tables (gt*.dbc) has an ID column that the file itself doesn't store.
                l.Columns.RemoveAt(0);
                foreach (var c in l.Columns) c.Field--;
                field--;
            }
            if (field != f.Fields) { why = $"The published layout has {field} columns but this file has {f.Fields}; column types were guessed instead."; return null; }
            l.Finish(f.Fields);
            if (l.RecordSize != f.RecordSize && l.maybeBytes.Count > 0) { foreach (var c in l.maybeBytes) c.Kind = DbcKind.Byte; l.Finish(f.Fields); }
            if (l.RecordSize != f.RecordSize) { why = $"The published layout gives {l.RecordSize}-byte records but this file has {f.RecordSize}; column types were guessed instead."; return null; }
            for (int i = 0; i < l.FieldCount; i++)
                if (l.FieldIsString[i])
                    for (int r = 0; r < f.Records; r++)
                        if (!f.IsStringStart(f.UIntAt(r, l.FieldOffset[i])))
                        { why = $"Field {i} should be text but row {r} doesn't point at a string; column types were guessed instead."; return null; }
            l.StringsCertain = true;
            l.HasId = DetectId(l, f);
            Links.Apply(l);
            return l;
        }

        void Finish(int fields)
        {
            FieldCount = fields;
            FieldOffset = new int[fields]; FieldSize = new int[fields]; FieldIsString = new bool[fields];
            int off = 0;
            foreach (var c in Columns)
                for (int k = 0; k < c.FieldCount; k++)
                {
                    int fi = c.Field + k;
                    FieldOffset[fi] = off;
                    FieldSize[fi] = c.Kind == DbcKind.Byte ? 1 : 4;
                    FieldIsString[fi] = c.Kind == DbcKind.String || (c.Kind == DbcKind.Loc && k < 16);
                    off += FieldSize[fi];
                }
            RecordSize = off;
        }

        static bool DetectId(DbcLayout l, DbcFile f)
        {
            var c0 = l.Columns[0];
            if (c0.Kind == DbcKind.Float || c0.Kind == DbcKind.String || c0.Kind == DbcKind.Loc || c0.Kind == DbcKind.Byte) return false;
            var seen = new HashSet<uint>();
            for (int r = 0; r < f.Records; r++) if (!seen.Add(f.UIntAt(r, 0))) return false;
            return true;
        }

        /// <summary>Raw mode: types guessed from the values (strings, localized strings, floats, signed ints).</summary>
        public static DbcLayout Guess(string file, DbcFile f)
        {
            var l = new DbcLayout { File = Path.GetFileName(file), Origin = "guessed" };
            int fields = f.Fields;
            bool dwords = f.RecordSize == fields * 4;
            if (!dwords)
            {
                // Record size doesn't match 4 bytes per field: show it as dwords plus trailing bytes.
                int dw = f.RecordSize / 4, rest = f.RecordSize % 4;
                for (int i = 0; i < dw; i++) l.Columns.Add(new DbcColumn { Name = i == 0 ? "ID" : "Field" + i, Kind = DbcKind.UInt, Field = i, Guessed = true });
                for (int i = 0; i < rest; i++) l.Columns.Add(new DbcColumn { Name = "Byte" + i, Kind = DbcKind.Byte, Field = dw + i, Guessed = true });
                l.Finish(dw + rest);
                l.Note = $"The header says {fields} fields but records are {f.RecordSize} bytes, so some fields are smaller than 4 bytes; shown as raw 4-byte values.";
                l.HasId = DetectId(l, f);
                l.StringsCertain = false;
                return l;
            }
            var kinds = new DbcKind[fields];
            var strOk = new bool[fields];
            var anyNonZero = new bool[fields];
            for (int i = 0; i < fields; i++)
            {
                bool s = f.StringBlockSize > 1, fl = true, neg = false, nz = false;
                for (int r = 0; r < f.Records; r++)
                {
                    uint v = f.UIntAt(r, i * 4);
                    if (v == 0) continue;
                    nz = true;
                    if (s && !f.IsStringStart(v)) s = false;
                    if (fl)
                    {
                        float x = BitConverter.Int32BitsToSingle(unchecked((int)v));
                        if (float.IsNaN(x) || float.IsInfinity(x) || Math.Abs(x) < 1e-6f || Math.Abs(x) > 1e8f) fl = false;
                    }
                    int iv = unchecked((int)v);
                    if (iv < 0 && iv > -1000000) neg = true;
                }
                anyNonZero[i] = nz;
                strOk[i] = s && i > 0;
                kinds[i] = !nz ? DbcKind.UInt : strOk[i] ? DbcKind.String : fl ? DbcKind.Float : neg ? DbcKind.Int : DbcKind.UInt;
            }
            int t = 1;
            for (int i = 0; i < fields;)
            {
                // localized block: a text field, 15 more fields that are text or empty, then a mask that isn't text
                if (i > 0 && kinds[i] == DbcKind.String && i + 16 < fields && Enumerable.Range(i + 1, 15).All(k => strOk[k] || !anyNonZero[k]) && kinds[i + 16] != DbcKind.String)
                {
                    l.Columns.Add(new DbcColumn { Name = "Text" + t++, Kind = DbcKind.Loc, Field = i, Guessed = true });
                    i += 17; continue;
                }
                var k = kinds[i];
                string name = i == 0 ? "ID" : k == DbcKind.String ? "Text" + t++ : "Field" + i;
                l.Columns.Add(new DbcColumn { Name = name, Kind = k, Field = i, Guessed = true });
                i++;
            }
            l.Finish(fields);
            l.HasId = DetectId(l, f);
            l.StringsCertain = false;
            Links.Apply(l);
            return l;
        }

        /// <summary>Field-level names used for CSV and staged changes: Loc columns expand to Name_enUS … Name_Unk, Name_Mask.</summary>
        public string FieldName(int field)
        {
            foreach (var c in Columns)
                if (field >= c.Field && field < c.Field + c.FieldCount)
                    return c.Kind == DbcKind.Loc ? c.Name + "_" + (field - c.Field < 16 ? Locales[field - c.Field] : "Mask") : c.Name;
            return "Field" + field;
        }

        public DbcColumn ColumnOf(int field) => Columns.FirstOrDefault(c => field >= c.Field && field < c.Field + c.FieldCount);

        public int FieldByName(string name)
        {
            for (int i = 0; i < FieldCount; i++) if (string.Equals(FieldName(i), name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// <summary>The column whose text best names a row (first localized or plain string), or null.</summary>
        public DbcColumn NameColumn =>
            Columns.FirstOrDefault(c => c.Kind == DbcKind.Loc && !c.Guessed) ?? Columns.FirstOrDefault(c => c.Kind == DbcKind.Loc)
            ?? Columns.FirstOrDefault(c => c.Kind == DbcKind.String && !c.Name.Contains("Texture") && !c.Name.Contains("Model"))
            ?? Columns.FirstOrDefault(c => c.Kind == DbcKind.String);
    }

    /// <summary>Foreign keys between DBC columns (and to the world database), by column name. Studio's own table.</summary>
    public static class Links
    {
        // (file filter or "*", column name without _N suffix, target)
        static readonly (string file, string col, string target)[] Rules =
        {
            ("*", "MapID", "Map.dbc"), ("*", "ContinentID", "Map.dbc"), ("*", "CorpseMapID", "Map.dbc"), ("Achievement.dbc", "Instance_Id", "Map.dbc"),
            ("*", "AreaID", "AreaTable.dbc"), ("*", "AreaTableID", "AreaTable.dbc"), ("*", "ParentAreaID", "AreaTable.dbc"), ("*", "ZoneID", "AreaTable.dbc"),
            ("*", "ParentFactionID", "Faction.dbc"), ("FactionTemplate.dbc", "Faction", "Faction.dbc"), ("Spell.dbc", "MinFactionID", "Faction.dbc"),
            ("ChrRaces.dbc", "FactionID", "FactionTemplate.dbc"),
            ("*", "SpellIconID", "SpellIcon.dbc"), ("*", "ActiveIconID", "SpellIcon.dbc"),
            ("*", "SpellVisualID", "SpellVisual.dbc"),
            ("*", "SoundID", "SoundEntries.dbc"), ("*", "SplashSoundID", "SoundEntries.dbc"), ("*", "ExplorationSoundID", "SoundEntries.dbc"), ("*", "IntroSound", "SoundEntries.dbc"),
            ("CreatureDisplayInfo.dbc", "SoundID", "CreatureSoundData.dbc"), ("CreatureModelData.dbc", "SoundID", "CreatureSoundData.dbc"),
            ("*", "ItemID", "db:item"), ("*", "Src_ItemID", "db:item"), ("*", "EffectItemType", "db:item"), ("Spell.dbc", "Reagent", "db:item"), ("Spell.dbc", "Totem", "db:item"),
            ("Item.dbc", "ID", "db:item"), ("Item.dbc", "DisplayInfoID", "ItemDisplayInfo.dbc"),
            ("*", "MountCreatureID", "db:creature"),
            ("*", "CreatureDisplayID", "CreatureDisplayInfo.dbc"), ("ChrRaces.dbc", "MaleDisplayId", "CreatureDisplayInfo.dbc"), ("ChrRaces.dbc", "FemaleDisplayId", "CreatureDisplayInfo.dbc"),
            ("CreatureDisplayInfo.dbc", "ModelID", "CreatureModelData.dbc"), ("CreatureDisplayInfo.dbc", "ExtendedDisplayInfoID", "CreatureDisplayInfoExtra.dbc"),
            ("*", "LiquidTypeID", "LiquidType.dbc"), ("*", "PowerDisplayID", "PowerDisplay.dbc"),
            ("*", "SkillLine", "SkillLine.dbc"), ("*", "SkillID", "SkillLine.dbc"), ("*", "RequiredSkillID", "SkillLine.dbc"), ("ItemSet.dbc", "RequiredSkill", "SkillLine.dbc"),
            ("*", "Spell", "Spell.dbc"), ("*", "SpellID", "Spell.dbc"), ("*", "SetSpellID", "Spell.dbc"), ("*", "PresetSpellID", "Spell.dbc"), ("*", "EffectTriggerSpell", "Spell.dbc"),
            ("*", "RequiredSpellID", "Spell.dbc"), ("*", "ResSicknessSpellID", "Spell.dbc"), ("*", "SupercededBySpell", "Spell.dbc"), ("*", "DifficultySpellID", "Spell.dbc"),
            ("*", "CasterAuraSpell", "Spell.dbc"), ("*", "TargetAuraSpell", "Spell.dbc"), ("*", "ExcludeCasterAuraSpell", "Spell.dbc"), ("*", "ExcludeTargetAuraSpell", "Spell.dbc"),
            ("*", "SpellRank", "Spell.dbc"), ("*", "Spells", "Spell.dbc"), ("*", "ModalNextSpell", "Spell.dbc"),
            ("Spell.dbc", "Category", "SpellCategory.dbc"), ("*", "RangeIndex", "SpellRange.dbc"), ("*", "CastingTimeIndex", "SpellCastTimes.dbc"), ("*", "DurationIndex", "SpellDuration.dbc"),
            ("*", "EffectRadiusIndex", "SpellRadius.dbc"), ("*", "RuneCostID", "SpellRuneCost.dbc"), ("*", "SpellDifficultyID", "SpellDifficulty.dbc"), ("*", "SpellMissileID", "SpellMissile.dbc"),
            ("*", "RequiresSpellFocus", "SpellFocusObject.dbc"), ("*", "SpellDescriptionVariableID", "SpellDescriptionVariables.dbc"), ("*", "RequiredTotemCategoryID", "TotemCategory.dbc"),
            ("*", "TabID", "TalentTab.dbc"), ("*", "PrereqTalent", "Talent.dbc"),
            ("*", "RaceID", "ChrRaces.dbc"), ("*", "ClassID", "ChrClasses.dbc"), ("Item.dbc", "ClassID", null),
            ("*", "CinematicSequenceID", "CinematicSequences.dbc"), ("*", "LoadingScreenID", "LoadingScreens.dbc"),
            ("Achievement_Criteria.dbc", "Achievement_Id", "Achievement.dbc"), ("Achievement.dbc", "Supercedes", "Achievement.dbc"), ("Achievement.dbc", "Shares_Criteria", "Achievement.dbc"),
            ("Achievement.dbc", "Category", "Achievement_Category.dbc"), ("Achievement.dbc", "IconID", "SpellIcon.dbc"), ("Achievement_Category.dbc", "Parent", "Achievement_Category.dbc"),
            ("TaxiPath.dbc", "FromTaxiNode", "TaxiNodes.dbc"), ("TaxiPath.dbc", "ToTaxiNode", "TaxiNodes.dbc"), ("TaxiPathNode.dbc", "PathID", "TaxiPath.dbc"),
            ("Vehicle.dbc", "SeatID", "VehicleSeat.dbc"), ("*", "Holiday", "Holidays.dbc"), ("*", "HolidayID", "Holidays.dbc"),
            ("*", "LightParamsID", "LightParams.dbc"), ("*", "ParticleColorID", "ParticleColor.dbc"), ("*", "ItemVisual", "ItemVisuals.dbc"),
            ("*", "WorldStateID", "WorldStateUI.dbc"), ("*", "AmbienceID", "SoundAmbience.dbc"), ("*", "ZoneMusic", "ZoneMusic.dbc"),
            ("GameObjectDisplayInfo.dbc", "Sound", "SoundEntries.dbc"),
        };

        public static string Base(string col)
        {
            int u = col.LastIndexOf('_');
            return u > 0 && col.Substring(u + 1).All(char.IsDigit) ? col.Substring(0, u) : col;
        }

        public static void Apply(DbcLayout l)
        {
            foreach (var c in l.Columns)
            {
                if (c.Guessed || c.Kind == DbcKind.String || c.Kind == DbcKind.Loc || c.Kind == DbcKind.Float || c.Kind == DbcKind.Flags) continue;
                var b = Base(c.Name);
                var hit = Rules.FirstOrDefault(r => string.Equals(r.file, l.File, StringComparison.OrdinalIgnoreCase) && string.Equals(r.col, b, StringComparison.OrdinalIgnoreCase));
                if (hit.col == null) hit = Rules.FirstOrDefault(r => r.file == "*" && string.Equals(r.col, b, StringComparison.OrdinalIgnoreCase));
                if (hit.col != null && hit.target != null && !(c.Field == 0 && !hit.target.StartsWith("db:"))) c.Link = hit.target;
            }
        }
    }
}
