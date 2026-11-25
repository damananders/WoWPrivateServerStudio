using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace AndersCraftStudio.Core.Build
{
    // ============================================================================ rows and staging (shared by every builder)
    /// <summary>One database row a builder reads, changes or adds.</summary>
    public sealed class BRow
    {
        public string Table;
        public string[] KeyCols;
        public List<string> Cols = new List<string>();
        public List<object> Vals = new List<object>();

        public BRow(string table, params string[] keyCols) { Table = table; KeyCols = keyCols; }
        int Ix(string c) => Cols.FindIndex(x => string.Equals(x, c, StringComparison.OrdinalIgnoreCase));
        public bool Has(string c) => Ix(c) >= 0;
        public object this[string c]
        {
            get { int i = Ix(c); return i < 0 ? null : Vals[i]; }
            set { int i = Ix(c); if (i >= 0) Vals[i] = value; }
        }
        /// <summary>Sets the column, adding it when the row doesn't have it (only for rows built here, not read).</summary>
        public BRow Put(string c, object v) { int i = Ix(c); if (i < 0) { Cols.Add(c); Vals.Add(v); } else Vals[i] = v; return this; }
        public long L(string c) { var o = this[c]; return o == null || o is DBNull ? 0 : Convert.ToInt64(Convert.ToDouble(o, CultureInfo.InvariantCulture)); }
        public double D(string c) { var o = this[c]; return o == null || o is DBNull ? 0 : Convert.ToDouble(o, CultureInfo.InvariantCulture); }
        public string S(string c) => this[c] is null or DBNull ? "" : Convert.ToString(this[c], CultureInfo.InvariantCulture);
        public BRow Clone() => new BRow(Table, KeyCols) { Cols = Cols.ToList(), Vals = Vals.ToList() };
        public string KeyText => Table + "|" + string.Join("|", KeyCols.Select(k => SqlGen.Literal(this[k])));
        public List<KeyPart> Key => KeyCols.Select(k => new KeyPart(Cols[Ix(k)], SqlGen.Literal(this[k]), SqlGen.Display(this[k]))).ToList();

        public static BRow FromResult(string table, string[] keyCols, ResultTable t, int row)
        {
            var r = new BRow(table, keyCols);
            for (int i = 0; i < t.Columns.Count; i++) { r.Cols.Add(t.Columns[i]); r.Vals.Add(t.Rows[row][i]); }
            return r;
        }
    }

    public static class Rows
    {
        public static List<BRow> Read(ISqlReader db, string table, string[] keyCols, string where, int max = 500)
        {
            try
            {
                var t = db.Query($"SELECT * FROM {SqlGen.Ident(table)} WHERE {where}", max);
                return Enumerable.Range(0, t.Rows.Count).Select(i => BRow.FromResult(table, keyCols, t, i)).ToList();
            }
            catch { return new List<BRow>(); }   // a table this server doesn't have
        }

        public static BRow One(ISqlReader db, string table, string[] keyCols, string where) => Read(db, table, keyCols, where, 2).FirstOrDefault();

        /// <summary>A new row with every column at its database default (numbers as numbers).</summary>
        public static BRow Blank(ISqlReader db, string table, params string[] keyCols)
        {
            var r = new BRow(table, keyCols);
            var t = db.Query("SELECT COLUMN_NAME, COLUMN_DEFAULT, IS_NULLABLE, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = " + SqlGen.Literal(table) + " ORDER BY ORDINAL_POSITION", 500);
            foreach (var x in t.Rows)
            {
                var name = Convert.ToString(x[0]); var def = x[1] is null or DBNull ? null : Convert.ToString(x[1], CultureInfo.InvariantCulture);
                bool nullable = string.Equals(Convert.ToString(x[2]), "YES", StringComparison.OrdinalIgnoreCase);
                var type = (Convert.ToString(x[3]) ?? "").ToLowerInvariant();
                if (def != null && def.Equals("NULL", StringComparison.OrdinalIgnoreCase)) def = null;
                if (def != null && def.Length >= 2 && def[0] == '\'' && def[^1] == '\'') def = def.Substring(1, def.Length - 2);
                bool numeric = type.Contains("int") || type is "float" or "double" or "decimal";
                object v = def == null ? (nullable ? DBNull.Value : numeric ? 0L : (object)"") :
                           numeric ? (double.TryParse(def, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (type.Contains("int") ? (object)(long)d : d) : 0L) : def;
                r.Cols.Add(name); r.Vals.Add(v);
            }
            return r;
        }

        /// <summary>The first unused ID at or above <paramref name="start"/> (a gap counts), also skipping IDs already staged.</summary>
        public static long FreeGap(ISqlReader db, Changeset cs, string table, string keyCol, long start)
        {
            var k = SqlGen.Ident(keyCol); var t = SqlGen.Ident(table); var st = start.ToString(CultureInfo.InvariantCulture);
            var used = new HashSet<long>(cs?.Changes.Where(c => c.Table == table && c.Kind == ChangeKind.Insert).SelectMany(c => c.Key.Where(x => string.Equals(x.Column, keyCol, StringComparison.OrdinalIgnoreCase))).Select(x => long.TryParse(x.Value, out var v) ? v : -1) ?? Enumerable.Empty<long>());
            for (long probe = start; ; )
            {
                var q = db.Query($"SELECT COALESCE((SELECT MIN(a.{k} + 1) FROM {t} a LEFT JOIN {t} b ON b.{k} = a.{k} + 1 WHERE a.{k} >= {probe.ToString(CultureInfo.InvariantCulture)} - 1 AND b.{k} IS NULL), {probe.ToString(CultureInfo.InvariantCulture)})", 1);
                long id = Convert.ToInt64(q.Rows[0][0], CultureInfo.InvariantCulture);
                if ((long)Convert.ToInt64(db.Query($"SELECT COUNT(*) FROM {t} WHERE {k} = {id.ToString(CultureInfo.InvariantCulture)}", 1).Rows[0][0], CultureInfo.InvariantCulture) > 0) { probe = id + 1; continue; }
                if (used.Contains(id)) { probe = id + 1; continue; }
                return Math.Max(id, start);
            }
        }

        public static long FreeId(ISqlReader db, Changeset cs, string table, string keyCol, long start)
        {
            var t = db.Query(Duplicator.FreeIdQuery(table, keyCol, start), 1);
            long free = t.Rows.Count > 0 && !(t.Rows[0][0] is null or DBNull) ? Convert.ToInt64(t.Rows[0][0], CultureInfo.InvariantCulture) : start;
            return Duplicator.NextId(free, cs, table, start);
        }

        /// <summary>
        /// Stages the difference between two sets of rows: rows only in <paramref name="after"/> are added, rows only in
        /// <paramref name="before"/> are removed, and changed columns of rows in both are edited (old value kept for undo).
        /// </summary>
        public static List<Change> Stage(IEnumerable<BRow> before, IEnumerable<BRow> after, Changeset cs, string category)
        {
            var res = new List<Change>();
            var b = before.GroupBy(r => r.KeyText).ToDictionary(g => g.Key, g => g.First());
            var a = after.GroupBy(r => r.KeyText).ToDictionary(g => g.Key, g => g.First());
            foreach (var r in after.GroupBy(r => r.KeyText).Select(g => g.First()))
            {
                if (!b.TryGetValue(r.KeyText, out var old))
                {
                    var lits = r.Vals.Select(SqlGen.Literal).ToList();
                    var c = ChangeFactory.StageRowInsert(cs, r.Table, r.Key, r.Cols, lits);
                    for (int i = 0; i < c.Fields.Count; i++) c.Fields[i].AfterDisplay = SqlGen.Display(r.Vals[i]);
                    res.Add(c);
                    continue;
                }
                foreach (var col in r.Cols)
                {
                    if (!old.Has(col)) continue;
                    var bl = SqlGen.Literal(old[col]); var al = SqlGen.Literal(r[col]);
                    if (bl == al || Same(old[col], r[col])) continue;
                    var c = ChangeFactory.StageCellEdit(cs, r.Table, old.Key, col, bl, SqlGen.Display(old[col]), al, SqlGen.Display(r[col]));
                    if (c != null && !res.Contains(c)) res.Add(c);
                }
            }
            foreach (var r in before.Where(r => !a.ContainsKey(r.KeyText)))
                res.Add(ChangeFactory.StageRowDelete(cs, r.Table, r.Key, r.Cols, r.Vals));
            foreach (var c in res) c.Category = category;
            return res;
        }

        /// <summary>Numbers read back as another numeric type (12 vs 12.0) are the same value.</summary>
        static bool Same(object x, object y)
        {
            if (x is null or DBNull || y is null or DBNull) return false;
            if (x is string || y is string) return false;
            try { return Convert.ToDouble(x, CultureInfo.InvariantCulture) == Convert.ToDouble(y, CultureInfo.InvariantCulture); } catch { return false; }
        }
    }

    /// <summary>How a builder starts: from nothing, from your own record, or from a game original (Blizzard's).</summary>
    public enum StartMode { New, Edit, CopyAsNew }

    // ============================================================================ quests
    public sealed class Objective
    {
        /// <summary>"kill", "collect", "talk", "use" (an object) or "explore".</summary>
        public string Kind = "kill";
        /// <summary>Creature (kill, talk), item (collect), game object (use) or area trigger (explore).</summary>
        public long Target;
        public int Count = 1;
        /// <summary>Collect: the creature that drops it, and how often (%). 0 = it already drops somewhere.</summary>
        public long DropFrom; public double DropChance = 40;
        /// <summary>Collect: make a new quest item with this name instead of using an existing one.</summary>
        public string NewItemName;
        /// <summary>What the quest log shows for it (empty = Studio writes it).</summary>
        public string Text;
    }

    public sealed class QuestDraft
    {
        public long Id;
        public string Title = "A new task";
        public int Level = 10, MinLevel = 8;
        /// <summary>Normal, Group, Dungeon, Raid, PvP.</summary>
        public string Type = "Normal";
        public int GroupSize = 0;
        /// <summary>Once, Daily, Weekly, Repeatable.</summary>
        public string Repeat = "Once";
        /// <summary>Positive = creature, negative = game object.</summary>
        public long Giver, Ender;
        public List<Objective> Objectives = new List<Objective>();
        public int XpSize = 5;
        public long Money = 1000;
        public List<(long item, int count)> Items = new List<(long, int)>();
        public List<(long item, int count)> Choices = new List<(long, int)>();
        public List<(long faction, int amount)> Reputation = new List<(long, int)>();
        public long RewardSpell;
        public string Description = "Hello, $N. I need your help.", Summary = "", Progress = "Have you done what I asked?", Completion = "Well done, $N!", Done = "";
        public long Previous, Next;
        /// <summary>0 = everyone; Alliance or Horde race masks.</summary>
        public long Races;
        public long Classes;
        /// <summary>Zone the quest log lists it under (0 = none).</summary>
        public long Zone;
    }

    public static class QuestBuilder
    {
        public const long AllianceRaces = 1 | 4 | 8 | 64 | 1024, HordeRaces = 2 | 16 | 32 | 128 | 512;
        public static readonly string[] XpNames = { "No experience", "Very little", "Little", "Some", "A fair amount", "The usual amount", "A good amount", "A lot", "A huge amount" };
        public static readonly string[] Types = { "Normal", "Group", "Dungeon", "Raid", "PvP" };
        public static readonly string[] Repeats = { "Once", "Daily", "Weekly", "Repeatable" };
        static int InfoOf(string t) => t switch { "Group" => 1, "Dungeon" => 81, "Raid" => 62, "PvP" => 41, _ => 0 };
        static string TypeOf(long info) => info switch { 1 => "Group", 81 => "Dungeon", 62 => "Raid", 41 => "PvP", _ => "Normal" };
        const long FlagDaily = 0x1000, FlagWeekly = 0x8000, SpecialRepeatable = 1, SpecialExplore = 2;

        /// <summary>Everything a quest is made of, as rows.</summary>
        public sealed class QuestRows
        {
            public BRow Template, Addon, Request, Offer;
            public List<BRow> Starters = new List<BRow>(), Enders = new List<BRow>(), Triggers = new List<BRow>();
            /// <summary>Other rows the quest needs (drops, new quest items, a talk-to script, giver flags).</summary>
            public List<BRow> Extra = new List<BRow>();
            /// <summary>Existing rows of other tables as they were before <see cref="Extra"/> changed them (for the diff).</summary>
            public List<BRow> ExtraBefore = new List<BRow>();
            public IEnumerable<BRow> All => new[] { Template, Addon, Request, Offer }.Where(r => r != null).Concat(Starters).Concat(Enders).Concat(Triggers).Concat(Extra);
            public QuestRows Clone() => new QuestRows { Template = Template?.Clone(), Addon = Addon?.Clone(), Request = Request?.Clone(), Offer = Offer?.Clone(), Starters = Starters.Select(r => r.Clone()).ToList(), Enders = Enders.Select(r => r.Clone()).ToList(), Triggers = Triggers.Select(r => r.Clone()).ToList(), Extra = Extra.Select(r => r.Clone()).ToList(), ExtraBefore = ExtraBefore.ToList() };
        }

        static string Id(long id) => id.ToString(CultureInfo.InvariantCulture);

        public static QuestRows Load(ISqlReader db, long id)
        {
            var q = new QuestRows
            {
                Template = Rows.One(db, "quest_template", new[] { "ID" }, "ID = " + Id(id)),
                Addon = Rows.One(db, "quest_template_addon", new[] { "ID" }, "ID = " + Id(id)),
                Request = Rows.One(db, "quest_request_items", new[] { "ID" }, "ID = " + Id(id)),
                Offer = Rows.One(db, "quest_offer_reward", new[] { "ID" }, "ID = " + Id(id)),
            };
            q.Starters.AddRange(Rows.Read(db, "creature_queststarter", new[] { "id", "quest" }, "quest = " + Id(id)));
            q.Starters.AddRange(Rows.Read(db, "gameobject_queststarter", new[] { "id", "quest" }, "quest = " + Id(id)));
            q.Enders.AddRange(Rows.Read(db, "creature_questender", new[] { "id", "quest" }, "quest = " + Id(id)));
            q.Enders.AddRange(Rows.Read(db, "gameobject_questender", new[] { "id", "quest" }, "quest = " + Id(id)));
            q.Triggers.AddRange(Rows.Read(db, "areatrigger_involvedrelation", new[] { "id" }, "quest = " + Id(id)));
            return q.Template == null ? null : q;
        }

        /// <summary>Blank rows for a brand-new quest (every column at its default).</summary>
        public static QuestRows Blank(ISqlReader db)
        {
            BRow B(string t) { try { var r = Rows.Blank(db, t, "ID"); return r.Cols.Count == 0 ? null : r; } catch { return null; } }
            return new QuestRows { Template = B("quest_template") ?? throw new InvalidOperationException("quest_template couldn't be read."), Addon = B("quest_template_addon"), Request = B("quest_request_items"), Offer = B("quest_offer_reward") };
        }

        public static QuestDraft ToDraft(QuestRows r)
        {
            var t = r.Template; var a = r.Addon;
            var d = new QuestDraft
            {
                Id = t.L("ID"), Title = t.S("LogTitle"), Level = (int)t.L("QuestLevel"), MinLevel = (int)t.L("MinLevel"), Type = TypeOf(t.L("QuestInfoID")), GroupSize = (int)t.L("SuggestedGroupNum"),
                XpSize = (int)t.L("RewardXPDifficulty"), Money = t.L("RewardMoney"), RewardSpell = t.L("RewardSpell"), Races = t.L("AllowableRaces"), Zone = t.L("QuestSortID") > 0 ? t.L("QuestSortID") : 0,
                Description = t.S("QuestDescription"), Summary = t.S("LogDescription"), Done = t.S("QuestCompletionLog"),
                Progress = r.Request?.S("CompletionText") ?? "", Completion = r.Offer?.S("RewardText") ?? "", Next = t.L("RewardNextQuest"),
                Previous = a?.L("PrevQuestID") ?? 0, Classes = a?.L("AllowableClasses") ?? 0,
            };
            long flags = t.L("Flags"), special = a?.L("SpecialFlags") ?? 0;
            d.Repeat = (flags & FlagDaily) != 0 ? "Daily" : (flags & FlagWeekly) != 0 ? "Weekly" : (special & SpecialRepeatable) != 0 ? "Repeatable" : "Once";
            var g = r.Starters.FirstOrDefault(); d.Giver = g == null ? 0 : g.Table.StartsWith("gameobject") ? -g.L("id") : g.L("id");
            var e = r.Enders.FirstOrDefault(); d.Ender = e == null ? 0 : e.Table.StartsWith("gameobject") ? -e.L("id") : e.L("id");
            for (int i = 1; i <= 4; i++)
            {
                long npc = t.L("RequiredNpcOrGo" + i); int n = (int)t.L("RequiredNpcOrGoCount" + i);
                if (npc != 0) d.Objectives.Add(new Objective { Kind = npc < 0 ? "use" : "kill", Target = Math.Abs(npc), Count = Math.Max(1, n), Text = t.S("ObjectiveText" + i) });
            }
            for (int i = 1; i <= 6; i++) { long it = t.L("RequiredItemId" + i); if (it != 0) d.Objectives.Add(new Objective { Kind = "collect", Target = it, Count = Math.Max(1, (int)t.L("RequiredItemCount" + i)) }); }
            foreach (var tr in r.Triggers) d.Objectives.Add(new Objective { Kind = "explore", Target = tr.L("id"), Count = 1 });
            for (int i = 1; i <= 4; i++) { long it = t.L("RewardItem" + i); if (it != 0) d.Items.Add((it, (int)Math.Max(1, t.L("RewardAmount" + i)))); }
            for (int i = 1; i <= 6; i++) { long it = t.L("RewardChoiceItemID" + i); if (it != 0) d.Choices.Add((it, (int)Math.Max(1, t.L("RewardChoiceItemQuantity" + i)))); }
            for (int i = 1; i <= 5; i++)
            {
                long f = t.L("RewardFactionID" + i); if (f == 0) continue;
                long over = t.L("RewardFactionOverride" + i), idx = t.L("RewardFactionValue" + i);
                d.Reputation.Add((f, over != 0 ? (int)(over / 100) : RepOfIndex(idx)));
            }
            return d;
        }

        /// <summary>The game's preset reputation amounts (QuestFactionReward.dbc row 1) that quests point at by number.</summary>
        static int RepOfIndex(long idx) { int[] t = { 0, 10, 25, 75, 150, 250, 350, 500, 1000, 5 }; long a = Math.Abs(idx); int v = a < t.Length ? t[a] : 0; return idx < 0 ? -v : v; }

        /// <summary>Plain-English problems that would make the quest not work (an empty list = good to go).</summary>
        public static List<string> Check(QuestDraft d)
        {
            var p = new List<string>();
            if (string.IsNullOrWhiteSpace(d.Title)) p.Add("It needs a name.");
            if (d.Giver == 0) p.Add("Pick who gives the quest.");
            if (d.Level < -1 || d.Level > 80 || d.MinLevel < 0 || d.MinLevel > 80) p.Add("Levels go from 1 to 80.");
            if (d.MinLevel > d.Level && d.Level > 0) p.Add("Players can take it before its level? The minimum level is above the quest's level.");
            if (d.Objectives.Count(o => o.Kind is "kill" or "use" or "talk") > 4) p.Add("At most 4 kill / use / talk objectives.");
            if (d.Objectives.Count(o => o.Kind == "collect") > 6) p.Add("At most 6 items to collect.");
            if (d.Objectives.Any(o => o.Count < 1 || o.Count > 255)) p.Add("Objective counts go from 1 to 255.");
            if (d.Objectives.Any(o => o.Kind != "collect" && o.Target <= 0 || o.Kind == "collect" && o.Target <= 0 && string.IsNullOrWhiteSpace(o.NewItemName))) p.Add("Every objective needs a target (pick a creature, item, object or area).");
            if (d.Objectives.Any(o => o.Kind == "collect" && o.DropFrom > 0 && (o.DropChance <= 0 || o.DropChance > 100))) p.Add("Drop chance goes from 1 to 100%.");
            if (d.Items.Count > 4) p.Add("At most 4 items are given as rewards."); if (d.Choices.Count > 6) p.Add("At most 6 items to choose from.");
            if (d.Reputation.Count > 5) p.Add("At most 5 reputation rewards.");
            if (d.Money < 0) p.Add("Money can't be negative.");
            if (d.Previous != 0 && d.Previous == d.Id) p.Add("A quest can't come after itself.");
            return p;
        }

        /// <summary>
        /// Writes the draft into a copy of <paramref name="rows"/> (the quest's rows as read, or blank ones) and adds the rows
        /// it needs: giver and turn-in links, drops for items to collect, new quest items, talk-to scripts, quest-giver flags.
        /// </summary>
        public static QuestRows Apply(QuestDraft d, QuestRows rows, ISqlReader db, Changeset cs, long customStart, List<string> notes)
        {
            var r = rows.Clone();
            var t = r.Template;
            void Set(BRow row, string c, object v) { if (row != null && row.Has(c)) row[c] = v; }
            bool fresh = t.L("ID") == 0;   // a blank row (a brand-new quest), not one read from the database
            Set(t, "ID", d.Id); Set(t, "LogTitle", d.Title); Set(t, "QuestLevel", d.Level); Set(t, "MinLevel", d.MinLevel);
            Set(t, "QuestInfoID", InfoOf(d.Type)); Set(t, "SuggestedGroupNum", d.Type == "Group" ? Math.Max(2, d.GroupSize) : 0);
            if (fresh) Set(t, "QuestType", 2);
            Set(t, "RewardXPDifficulty", Math.Max(0, Math.Min(8, d.XpSize))); Set(t, "RewardMoney", d.Money); Set(t, "RewardSpell", d.RewardSpell);
            Set(t, "AllowableRaces", d.Races); Set(t, "QuestSortID", d.Zone); Set(t, "RewardNextQuest", d.Next);
            Set(t, "QuestDescription", d.Description ?? ""); Set(t, "QuestCompletionLog", d.Done ?? "");
            long flags = t.L("Flags") & ~(FlagDaily | FlagWeekly);
            if (d.Repeat == "Daily") flags |= FlagDaily; else if (d.Repeat == "Weekly") flags |= FlagWeekly;
            Set(t, "Flags", flags);
            // objectives
            var npcs = d.Objectives.Where(o => o.Kind is "kill" or "use" or "talk").ToList();
            var items = d.Objectives.Where(o => o.Kind == "collect").ToList();
            for (int i = 1; i <= 4; i++)
            {
                var o = i <= npcs.Count ? npcs[i - 1] : null;
                Set(t, "RequiredNpcOrGo" + i, o == null ? 0 : o.Kind == "use" ? -o.Target : o.Target);
                Set(t, "RequiredNpcOrGoCount" + i, o?.Count ?? 0);
                Set(t, "ObjectiveText" + i, o == null ? "" : string.IsNullOrWhiteSpace(o.Text) ? (o.Kind == "talk" ? "Speak with " + Name(db, "creature", o.Target) : "") : o.Text);
            }
            // (the draft itself isn't changed: a new quest item gets its number here, every time the quest is prepared)
            var collected = new List<Objective>();
            for (int i = 1; i <= 6; i++)
            {
                var o = i <= items.Count ? items[i - 1] : null;
                long target = o?.Target ?? 0;
                if (o != null && target <= 0 && !string.IsNullOrWhiteSpace(o.NewItemName)) target = NewQuestItem(db, cs, r, o.NewItemName, customStart, notes);
                if (o != null) collected.Add(new Objective { Kind = "collect", Target = target, Count = o.Count, DropFrom = o.DropFrom, DropChance = o.DropChance });
                Set(t, "RequiredItemId" + i, target); Set(t, "RequiredItemCount" + i, o?.Count ?? 0);
            }
            // rewards
            for (int i = 1; i <= 4; i++) { var x = i <= d.Items.Count ? d.Items[i - 1] : (0, 0); Set(t, "RewardItem" + i, x.Item1); Set(t, "RewardAmount" + i, x.Item1 == 0 ? 0 : Math.Max(1, x.Item2)); }
            for (int i = 1; i <= 6; i++) { var x = i <= d.Choices.Count ? d.Choices[i - 1] : (0, 0); Set(t, "RewardChoiceItemID" + i, x.Item1); Set(t, "RewardChoiceItemQuantity" + i, x.Item1 == 0 ? 0 : Math.Max(1, x.Item2)); }
            for (int i = 1; i <= 5; i++) { var x = i <= d.Reputation.Count ? d.Reputation[i - 1] : (0, 0); Set(t, "RewardFactionID" + i, x.Item1); Set(t, "RewardFactionValue" + i, 0); Set(t, "RewardFactionOverride" + i, x.Item2 * 100); }
            // the quest log's short summary
            var summary = string.IsNullOrWhiteSpace(d.Summary) ? AutoSummary(d, db) : d.Summary;
            Set(t, "LogDescription", summary);
            // addon: who can take it, chain, repeat
            if (r.Addon == null && (d.Previous != 0 || d.Classes != 0 || d.Repeat == "Repeatable" || d.Objectives.Any(o => o.Kind == "explore")))
                r.Addon = new BRow("quest_template_addon", "ID").Put("ID", d.Id).Put("PrevQuestID", 0L).Put("AllowableClasses", 0L).Put("SpecialFlags", 0L);
            if (r.Addon != null)
            {
                Set(r.Addon, "ID", d.Id); Set(r.Addon, "PrevQuestID", d.Previous); Set(r.Addon, "AllowableClasses", d.Classes);
                long sp = r.Addon.L("SpecialFlags") & ~(SpecialRepeatable | SpecialExplore);
                if (d.Repeat == "Repeatable" || d.Repeat == "Daily" || d.Repeat == "Weekly") sp |= SpecialRepeatable;
                if (d.Objectives.Any(o => o.Kind == "explore")) sp |= SpecialExplore;
                Set(r.Addon, "SpecialFlags", sp);
            }
            // texts on the hand-in windows
            if (r.Request == null && !string.IsNullOrWhiteSpace(d.Progress)) r.Request = new BRow("quest_request_items", "ID").Put("ID", d.Id).Put("EmoteOnComplete", 0L).Put("EmoteOnIncomplete", 0L).Put("CompletionText", "");
            if (r.Request != null) { Set(r.Request, "ID", d.Id); Set(r.Request, "CompletionText", d.Progress ?? ""); }
            if (r.Offer == null && !string.IsNullOrWhiteSpace(d.Completion)) r.Offer = new BRow("quest_offer_reward", "ID").Put("ID", d.Id).Put("RewardText", "");
            if (r.Offer != null) { Set(r.Offer, "ID", d.Id); Set(r.Offer, "RewardText", d.Completion ?? ""); }
            // who gives it and who takes it back
            r.Starters = new List<BRow> { Link(d.Giver < 0 ? "gameobject_queststarter" : "creature_queststarter", Math.Abs(d.Giver), d.Id) };
            var ender = d.Ender == 0 ? d.Giver : d.Ender;
            r.Enders = new List<BRow> { Link(ender < 0 ? "gameobject_questender" : "creature_questender", Math.Abs(ender), d.Id) };
            foreach (var c in new[] { d.Giver, ender }.Where(c => c > 0).Distinct()) QuestGiverFlag(db, r, c, notes);
            // explore: area triggers
            r.Triggers = d.Objectives.Where(o => o.Kind == "explore" && o.Target > 0).Select(o => new BRow("areatrigger_involvedrelation", "id").Put("id", o.Target).Put("quest", d.Id)).ToList();
            // collect: make sure something drops it (quest-only)
            foreach (var o in collected.Where(o => o.DropFrom > 0 && o.Target > 0)) AddDrop(db, r, o, notes);
            // talk to: a small script gives the credit when the player opens the NPC's chat window
            foreach (var o in npcs.Where(o => o.Kind == "talk")) TalkCredit(db, cs, r, o, notes);
            return r;
        }

        /// <summary>A creature_template row read for editing: the original is remembered for the diff, a copy is returned.</summary>
        static BRow Edited(ISqlReader db, QuestRows r, long entry)
        {
            var orig = Rows.One(db, "creature_template", new[] { "entry" }, "entry = " + Id(entry));
            if (orig == null) return null;
            if (!r.ExtraBefore.Any(x => x.Table == "creature_template" && x.L("entry") == entry)) r.ExtraBefore.Add(orig);
            return orig.Clone();
        }

        static BRow Link(string table, long who, long quest) => new BRow(table, "id", "quest").Put("id", who).Put("quest", quest);

        public static string AutoSummary(QuestDraft d, ISqlReader db)
        {
            var parts = d.Objectives.Select(o => o.Kind switch
            {
                "kill" => $"Kill {o.Count} {Name(db, "creature", o.Target)}",
                "collect" => $"Bring {o.Count} {(o.Target > 0 ? Name(db, "item", o.Target) : o.NewItemName)}",
                "talk" => $"Speak with {Name(db, "creature", o.Target)}",
                "use" => $"Use {Name(db, "gameobject", o.Target)}" + (o.Count > 1 ? $" {o.Count} times" : ""),
                "explore" => "Find the place you were sent to",
                _ => "",
            }).Where(s => s.Length > 0).ToList();
            var ender = d.Ender == 0 ? d.Giver : d.Ender;
            var to = ender == 0 ? "" : $" then return to {Name(db, ender < 0 ? "gameobject" : "creature", Math.Abs(ender))}";
            return parts.Count == 0 ? (ender == 0 ? "" : "Speak with " + Name(db, ender < 0 ? "gameobject" : "creature", Math.Abs(ender)) + ".") : string.Join(", ", parts) + to + ".";
        }

        /// <summary>A display name for a creature, item, game object or faction (or "#id").</summary>
        public static string Name(ISqlReader db, string kind, long id)
        {
            if (db == null || id <= 0) return "#" + id;
            var (table, key, col) = kind switch { "item" => ("item_template", "entry", "name"), "gameobject" => ("gameobject_template", "entry", "name"), _ => ("creature_template", "entry", "name") };
            try { var t = db.Query($"SELECT {SqlGen.Ident(col)} FROM {SqlGen.Ident(table)} WHERE {SqlGen.Ident(key)} = {Id(id)} LIMIT 1", 1); return t.Rows.Count > 0 ? Convert.ToString(t.Rows[0][0]) : "#" + id; }
            catch { return "#" + id; }
        }

        static void QuestGiverFlag(ISqlReader db, QuestRows r, long creature, List<string> notes)
        {
            if (db == null || r.Extra.Any(x => x.Table == "creature_template" && x.L("entry") == creature)) return;
            var c = Rows.One(db, "creature_template", new[] { "entry" }, "entry = " + Id(creature));
            if (c == null || (c.L("npcflag") & 2) != 0) return;
            r.ExtraBefore.Add(c);
            c = c.Clone(); c["npcflag"] = c.L("npcflag") | 2;
            r.Extra.Add(c);
            notes?.Add($"{c.S("name")} wasn't a quest giver yet; it gets the quest giver mark (\"!\").");
        }

        static void AddDrop(ISqlReader db, QuestRows r, Objective o, List<string> notes)
        {
            if (db == null) { r.Extra.Add(DropRow(o.DropFrom, o.Target, o.DropChance)); return; }
            var c = r.Extra.FirstOrDefault(x => x.Table == "creature_template" && x.L("entry") == o.DropFrom) ?? Edited(db, r, o.DropFrom);
            if (c == null) { notes?.Add($"Creature {o.DropFrom} doesn't exist, so nothing drops the item."); return; }
            long loot = c.L("lootid");
            if (loot == 0) { loot = o.DropFrom; c["lootid"] = loot; if (!r.Extra.Contains(c)) r.Extra.Add(c); notes?.Add($"{c.S("name")} had no loot list; it gets one."); }
            var existing = Rows.One(db, "creature_loot_template", new[] { "Entry", "Item" }, $"Entry = {Id(loot)} AND Item = {Id(o.Target)}");
            if (existing != null) { notes?.Add($"{c.S("name")} already drops it ({existing.D("Chance"):0.#}%); left as it is."); return; }
            var row = Rows.Blank(db, "creature_loot_template", "Entry", "Item");
            foreach (var (k, v) in new (string, object)[] { ("Entry", loot), ("Item", o.Target), ("Reference", 0L), ("Chance", o.DropChance), ("QuestRequired", 1L), ("LootMode", 1L), ("GroupId", 0L), ("MinCount", 1L), ("MaxCount", 1L), ("Comment", "Quest item (Studio)") }) row.Put(k, v);
            r.Extra.Add(row);
        }

        public static BRow DropRow(long loot, long item, double chance) => new BRow("creature_loot_template", "Entry", "Item").Put("Entry", loot).Put("Item", item).Put("Reference", 0L).Put("Chance", chance).Put("QuestRequired", 1L).Put("LootMode", 1L).Put("GroupId", 0L).Put("MinCount", 1L).Put("MaxCount", 1L);

        static long NewQuestItem(ISqlReader db, Changeset cs, QuestRows r, string name, long customStart, List<string> notes)
        {
            var baseRow = Rows.Read(db, "item_template", new[] { "entry" }, "class = 12 AND Quality = 1 AND stackable > 1 ORDER BY entry LIMIT 1", 1).FirstOrDefault();
            if (baseRow == null) throw new InvalidOperationException("No quest item to copy was found (item_template class 12).");
            var id = Rows.FreeId(db, cs, "item_template", "entry", customStart);
            while (r.Extra.Any(x => x.Table == "item_template" && x.L("entry") == id)) id++;
            var it = baseRow.Clone(); it["entry"] = id; it["name"] = name.Trim(); if (it.Has("description")) it["description"] = "";
            r.Extra.Add(it);
            notes?.Add($"New quest item #{id} \"{name.Trim()}\" (a copy of \"{baseRow.S("name")}\": same look and stacking).");
            return id;
        }

        static void TalkCredit(ISqlReader db, Changeset cs, QuestRows r, Objective o, List<string> notes)
        {
            if (db == null) return;
            var c = r.Extra.FirstOrDefault(x => x.Table == "creature_template" && x.L("entry") == o.Target) ?? Edited(db, r, o.Target);
            if (c == null) return;
            if (c.S("ScriptName").Length > 0) { notes?.Add($"{c.S("name")} has its own program script, so a \"talk to\" script can't be added; it needs code to give the credit."); return; }
            var existing = Rows.Read(db, "smart_scripts", SmartRow.KeyColumns, $"entryorguid = {Id(o.Target)} AND source_type = 0", 200);
            if (existing.Any(x => x.L("event_type") == 64 && x.L("action_type") == 33 && x.L("action_param1") == o.Target)) return;
            int next = existing.Select(x => (int)x.L("id")).DefaultIfEmpty(-1).Max() + 1 + r.Extra.Count(x => x.Table == "smart_scripts" && x.L("entryorguid") == o.Target);
            if (c.S("AIName") != "SmartAI") { c["AIName"] = "SmartAI"; if (!r.Extra.Contains(c)) r.Extra.Add(c); }
            if ((c.L("npcflag") & 1) == 0) { c["npcflag"] = c.L("npcflag") | 1; if (!r.Extra.Contains(c)) r.Extra.Add(c); }
            var sr = new SmartRow { EntryOrGuid = o.Target, SourceType = 0, Id = next, EventType = 64, ActionType = 33, TargetType = 7, Comment = $"{c.S("name")} - On Gossip Hello - Quest credit (Studio)" };
            sr.ActionParams[0] = o.Target;
            r.Extra.Add(Smart.ToRow(sr));
            notes?.Add($"Talking to {c.S("name")} now counts for the quest (a small SmartAI script).");
        }

        /// <summary>
        /// Stages the quest. New: everything is added. Edit: only what changed. Copy as new: the original is left alone and a
        /// copy with a new ID (and its own giver / turn-in links) is added.
        /// </summary>
        public static List<Change> Stage(StartMode mode, QuestRows original, QuestRows result, Changeset cs)
        {
            // existing rows of other tables count only when the builder changed them (never "removed" because they weren't touched)
            var touched = result.ExtraBefore.Where(b => result.Extra.Any(a => a.KeyText == b.KeyText));
            var before = (mode == StartMode.Edit ? original.All : Enumerable.Empty<BRow>()).Concat(touched).ToList();
            // only creature rows we actually changed count as "after" for them (others stay out of the diff)
            var after = result.All.Where(a => a.Table != "creature_template" || result.ExtraBefore.Any(b => b.KeyText == a.KeyText)).ToList();
            return Rows.Stage(before, after, cs, "Quests");
        }
    }

    /// <summary>Turns SmartAI rows into database rows (and back).</summary>
    public static class Smart
    {
        public static BRow ToRow(SmartRow s)
        {
            var v = s.ToValues();
            var r = new BRow("smart_scripts", SmartRow.KeyColumns);
            foreach (var c in SmartRow.Columns) r.Put(c, Typed(c, v.Disp(c)));
            return r;
        }
        static object Typed(string col, string disp)
        {
            if (col == "comment") return disp ?? "";
            if (col.StartsWith("target_") && col.Length == 8) return double.TryParse(disp, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0.0;
            return long.TryParse(disp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : 0L;
        }
    }
}
