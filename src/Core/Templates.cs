using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AndersCraftStudio.Core
{
    /// <summary>One input of a template form.</summary>
    public sealed class TemplateInput
    {
        public string Key, Label, Hint, Default;
        /// <summary>int, decimal, text, longtext, creature, item, quest, spell, itemlist, choice</summary>
        public string Kind = "text";
        public string[] Choices;
        public bool Optional;
    }

    /// <summary>What a template produced: the staged changes (in the given changeset) and plain-English notes.</summary>
    public sealed class TemplateResult
    {
        public List<Change> Changes = new List<Change>();
        public List<string> Notes = new List<string>();
        public string Headline;
    }

    /// <summary>A ready-made action that stages a reviewable set of changes. Reads with the given reader; never writes.</summary>
    public abstract class StudioTemplate
    {
        public string Key { get; protected set; }
        public string Title { get; protected set; }
        /// <summary>One line for the list.</summary>
        public string What { get; protected set; }
        /// <summary>A paragraph for the Guide card: what it does and what to check afterwards.</summary>
        public string Explain { get; protected set; }
        public string Glyph { get; protected set; } = "";
        public List<TemplateInput> Inputs { get; } = new List<TemplateInput>();

        /// <summary>Stages the template's changes into <paramref name="cs"/>. Throws TemplateException with a friendly message on bad input.</summary>
        public abstract TemplateResult Build(IDictionary<string, string> values, ISqlReader db, Changeset cs, long customStart);

        // ---------- shared helpers ----------
        protected static string V(IDictionary<string, string> v, string key) => v.TryGetValue(key, out var s) ? (s ?? "").Trim() : "";

        protected static long Long(IDictionary<string, string> v, string key, string label, long min = long.MinValue, long max = long.MaxValue)
        {
            var s = V(v, key).TrimStart('#');
            if (!long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) throw new TemplateException($"{label}: enter a whole number.");
            if (n < min || n > max) throw new TemplateException($"{label}: must be between {min} and {max}.");
            return n;
        }

        protected static double Dec(IDictionary<string, string> v, string key, string label, double min = 0, double max = 1000)
        {
            if (!double.TryParse(V(v, key).Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) throw new TemplateException($"{label}: enter a number like 1.5.");
            if (d < min || d > max) throw new TemplateException($"{label}: must be between {min} and {max}.");
            return d;
        }

        public sealed class Row
        {
            public List<string> Columns; public object[] Values;
            public int Index(string col) => Columns.FindIndex(c => string.Equals(c, col, StringComparison.OrdinalIgnoreCase));
            public bool Has(string col) => Index(col) >= 0;
            public object this[string col] { get { int i = Index(col); return i < 0 ? null : Values[i]; } set { int i = Index(col); if (i >= 0) Values[i] = value; } }
            public double Num(string col) { var o = this[col]; return o == null || o is DBNull ? 0 : Convert.ToDouble(o, CultureInfo.InvariantCulture); }
            public string Str(string col) => Convert.ToString(this[col], CultureInfo.InvariantCulture) ?? "";
        }

        protected static List<Row> Rows(ISqlReader db, string sql, int max = 500)
        {
            var t = db.Query(sql, max);
            return t.Rows.Select(r => new Row { Columns = t.Columns.ToList(), Values = (object[])r.Clone() }).ToList();
        }

        protected static Row One(ISqlReader db, string table, string keyCol, long id)
            => Rows(db, $"SELECT * FROM {SqlGen.Ident(table)} WHERE {SqlGen.Ident(keyCol)} = {id.ToString(CultureInfo.InvariantCulture)} LIMIT 1", 1).FirstOrDefault();

        /// <summary>Rows of a side table, or an empty list if the table doesn't exist on this server.</summary>
        protected static List<Row> Side(ISqlReader db, string table, string keyCol, long id)
        {
            try { return Rows(db, $"SELECT * FROM {SqlGen.Ident(table)} WHERE {SqlGen.Ident(keyCol)} = {id.ToString(CultureInfo.InvariantCulture)}", 200); }
            catch { return new List<Row>(); }
        }

        protected static long FreeId(ISqlReader db, Changeset cs, string table, string keyCol, long start)
        {
            var t = db.Query(Duplicator.FreeIdQuery(table, keyCol, start), 1);
            long dbFree = t.Rows.Count > 0 && t.Rows[0][0] != null && !(t.Rows[0][0] is DBNull) ? Convert.ToInt64(t.Rows[0][0], CultureInfo.InvariantCulture) : start;
            return Duplicator.NextId(dbFree, cs, table, start);
        }

        /// <summary>Stages INSERT of a row (key columns given for the undo DELETE).</summary>
        protected static Change Insert(Changeset cs, string table, Row r, params string[] keyCols)
        {
            var lits = r.Values.Select(SqlGen.Literal).ToList();
            var key = keyCols.Select(k => { int i = r.Index(k); return new KeyPart(r.Columns[i], lits[i], SqlGen.Display(r.Values[i])); }).ToList();
            var c = ChangeFactory.StageRowInsert(cs, table, key, r.Columns, lits);
            for (int i = 0; i < c.Fields.Count; i++) c.Fields[i].AfterDisplay = SqlGen.Display(r.Values[i]);
            return c;
        }

        /// <summary>Stages a cell UPDATE (with the current value as "before"); null when nothing changes.</summary>
        protected static Change Update(Changeset cs, string table, string keyCol, long id, Row current, string col, object after)
        {
            if (!current.Has(col)) return null;
            var before = current[col];
            var bl = SqlGen.Literal(before); var al = SqlGen.Literal(after);
            if (bl == al) return null;
            var key = new List<KeyPart> { new KeyPart(keyCol, id.ToString(CultureInfo.InvariantCulture)) };
            return ChangeFactory.StageCellEdit(cs, table, key, col, bl, SqlGen.Display(before), al, SqlGen.Display(after));
        }

        protected static string Name(ISqlReader db, string table, string keyCol, string nameCol, long id)
        {
            var r = One(db, table, keyCol, id);
            return r?.Str(nameCol);
        }

        protected static void Tag(TemplateResult res, string category, string summaryPrefix)
        {
            foreach (var c in res.Changes) { c.Category = category; if (summaryPrefix != null) c.Summary = summaryPrefix + c.Summary; }
        }
    }

    public sealed class TemplateException : Exception { public TemplateException(string m) : base(m) { } }

    public static class Templates
    {
        public static readonly IReadOnlyList<StudioTemplate> All = new StudioTemplate[]
        {
            new CloneBossTemplate(), new ScaleCreatureTemplate(), new VendorTemplate(), new QuestTemplate(), new SmartCasterTemplate(),
        };
        public static StudioTemplate Get(string key) => All.FirstOrDefault(t => t.Key == key);
    }

    /// <summary>Clone a creature (a boss, an elite…) as a stronger, higher-level copy.</summary>
    public sealed class CloneBossTemplate : StudioTemplate
    {
        static readonly (string table, string key)[] SideTables =
        {
            ("creature_template_model", "CreatureID"), ("creature_template_addon", "entry"), ("creature_equip_template", "CreatureID"),
            ("creature_template_resistance", "CreatureID"), ("creature_template_spell", "CreatureID"),
        };

        public CloneBossTemplate()
        {
            Key = "clone-boss"; Glyph = "";
            Title = "Clone a creature as a stronger version";
            What = "A copy of a boss or elite with a new ID, a higher level and more health and damage.";
            Explain = "Copies the creature's template and its model, addon, equipment, resistance and spell rows (and its SmartAI script, if it has one) to the first free ID at or above your custom ID range. The copy gets the level you choose, and its HealthModifier and DamageModifier are multiplied. It keeps the original's loot (same lootid) and faction. It isn't placed in the world: spawn it with .npc add <ID> or add a row on its Spawns tab.";
            Inputs.Add(new TemplateInput { Key = "creature", Label = "Creature to copy", Kind = "creature", Hint = "The creature_template entry, e.g. a dungeon boss." });
            Inputs.Add(new TemplateInput { Key = "name", Label = "New name", Kind = "text", Optional = true, Hint = "Leave empty for \"<name> (Empowered)\"." });
            Inputs.Add(new TemplateInput { Key = "level", Label = "Level", Kind = "int", Default = "83", Hint = "Both minimum and maximum level of the copy." });
            Inputs.Add(new TemplateInput { Key = "health", Label = "Health ×", Kind = "decimal", Default = "2", Hint = "Multiplies the original HealthModifier." });
            Inputs.Add(new TemplateInput { Key = "damage", Label = "Damage ×", Kind = "decimal", Default = "1.5", Hint = "Multiplies the original DamageModifier." });
        }

        public override TemplateResult Build(IDictionary<string, string> v, ISqlReader db, Changeset cs, long customStart)
        {
            long src = Long(v, "creature", "Creature to copy", 1);
            int level = (int)Long(v, "level", "Level", 1, 255);
            double hp = Dec(v, "health", "Health ×", 0.01, 1000), dmg = Dec(v, "damage", "Damage ×", 0.01, 1000);
            var row = One(db, "creature_template", "entry", src) ?? throw new TemplateException($"Creature {src} doesn't exist.");
            long id = FreeId(db, cs, "creature_template", "entry", customStart);
            var name = V(v, "name"); if (name.Length == 0) name = row.Str("name") + " (Empowered)";
            var res = new TemplateResult();
            row["entry"] = id; row["name"] = name; row["minlevel"] = level; row["maxlevel"] = level;
            if (row.Has("HealthModifier")) row["HealthModifier"] = Math.Round(row.Num("HealthModifier") * hp, 4);
            if (row.Has("DamageModifier")) row["DamageModifier"] = Math.Round(row.Num("DamageModifier") * dmg, 4);
            if (row.Has("difficulty_entry_1")) { row["difficulty_entry_1"] = 0; row["difficulty_entry_2"] = 0; row["difficulty_entry_3"] = 0; }
            if (row.Has("KillCredit1")) { row["KillCredit1"] = 0; row["KillCredit2"] = 0; }
            var main = Insert(cs, "creature_template", row, "entry");
            main.Summary = $"New creature #{id} \"{name}\" (level {level}, copy of #{src})";
            res.Changes.Add(main);
            foreach (var (t, k) in SideTables)
                foreach (var r in Side(db, t, k, src)) { r[k] = id; res.Changes.Add(Insert(cs, t, r, KeysOf(t, r))); }
            if (row.Str("AIName") == "SmartAI")
                foreach (var r in Side(db, "smart_scripts", "entryorguid", src).Where(r => r.Str("source_type") == "0"))
                { r["entryorguid"] = id; res.Changes.Add(Insert(cs, "smart_scripts", r, SmartRow.KeyColumns)); }
            Tag(res, "Creatures", null);
            res.Headline = $"Creature #{id} \"{name}\": level {level}, health ×{hp}, damage ×{dmg}, {res.Changes.Count - 1} side row(s) copied.";
            res.Notes.Add($"Shares loot with #{src} (lootid {row.Str("lootid")}). Change it on the creature's Loot tab if the copy should drop something else.");
            if (row.Str("ScriptName").Length > 0) res.Notes.Add($"The original uses the C++ script \"{row.Str("ScriptName")}\"; the copy does too, and scripts often check the original entry. Clear ScriptName if the copy misbehaves.");
            res.Notes.Add("Not placed in the world yet: use .npc add " + id + " in game, or the Spawns tab.");
            return res;
        }

        static string[] KeysOf(string table, Row r) => table switch
        {
            "creature_template_model" => new[] { "CreatureID", "Idx" }.Where(r.Has).ToArray(),
            "creature_equip_template" => new[] { "CreatureID", "ID" }.Where(r.Has).ToArray(),
            "creature_template_resistance" => new[] { "CreatureID", "School" }.Where(r.Has).ToArray(),
            "creature_template_spell" => new[] { "CreatureID", "Index" }.Where(r.Has).ToArray(),
            _ => new[] { r.Columns[0] },
        };
    }

    /// <summary>Change a creature's level (and optionally its health/damage) in place.</summary>
    public sealed class ScaleCreatureTemplate : StudioTemplate
    {
        public ScaleCreatureTemplate()
        {
            Key = "scale"; Glyph = "";
            Title = "Scale a creature to a level";
            What = "Set a creature's level range and multiply its health and damage, in place.";
            Explain = "Stages UPDATEs on creature_template: minlevel and maxlevel, and HealthModifier / DamageModifier multiplied by your factors (1 = unchanged). Health, damage and armor then follow AzerothCore's class-level stats for the new level. Every spawn of this creature changes; use \"Clone a creature\" if you want a separate copy.";
            Inputs.Add(new TemplateInput { Key = "creature", Label = "Creature", Kind = "creature", Hint = "The creature_template entry." });
            Inputs.Add(new TemplateInput { Key = "min", Label = "Min level", Kind = "int", Default = "60" });
            Inputs.Add(new TemplateInput { Key = "max", Label = "Max level", Kind = "int", Default = "60", Hint = "Each spawn picks a level in this range." });
            Inputs.Add(new TemplateInput { Key = "health", Label = "Health ×", Kind = "decimal", Default = "1" });
            Inputs.Add(new TemplateInput { Key = "damage", Label = "Damage ×", Kind = "decimal", Default = "1" });
        }

        public override TemplateResult Build(IDictionary<string, string> v, ISqlReader db, Changeset cs, long customStart)
        {
            long id = Long(v, "creature", "Creature", 1);
            int min = (int)Long(v, "min", "Min level", 1, 255), max = (int)Long(v, "max", "Max level", 1, 255);
            if (max < min) throw new TemplateException("Max level must be at least the min level.");
            double hp = Dec(v, "health", "Health ×", 0.01, 1000), dmg = Dec(v, "damage", "Damage ×", 0.01, 1000);
            var row = One(db, "creature_template", "entry", id) ?? throw new TemplateException($"Creature {id} doesn't exist.");
            var res = new TemplateResult();
            void Add(Change c) { if (c != null && !res.Changes.Contains(c)) res.Changes.Add(c); }
            Add(Update(cs, "creature_template", "entry", id, row, "minlevel", min));
            Add(Update(cs, "creature_template", "entry", id, row, "maxlevel", max));
            if (hp != 1) Add(Update(cs, "creature_template", "entry", id, row, "HealthModifier", Math.Round(row.Num("HealthModifier") * hp, 4)));
            if (dmg != 1) Add(Update(cs, "creature_template", "entry", id, row, "DamageModifier", Math.Round(row.Num("DamageModifier") * dmg, 4)));
            res.Headline = res.Changes.Count == 0 ? $"#{id} \"{row.Str("name")}\" already has these values." : $"#{id} \"{row.Str("name")}\": level {min}-{max}" + (hp != 1 ? $", health ×{hp}" : "") + (dmg != 1 ? $", damage ×{dmg}" : "") + ".";
            res.Notes.Add("All spawns of this creature change. Existing spawns pick their new level when they respawn or the server restarts.");
            return res;
        }
    }

    /// <summary>A new vendor NPC that sells a list of items.</summary>
    public sealed class VendorTemplate : StudioTemplate
    {
        public VendorTemplate()
        {
            Key = "vendor"; Glyph = "";
            Title = "Make a vendor that sells items";
            What = "A new NPC (looking like an existing one) that sells the items you list.";
            Explain = "Copies the look of an existing creature (template and model rows) to a new ID in your custom range, makes it a friendly, non-attackable vendor (npcflag gossip + vendor, faction 35 by default, no loot or AI), and adds one npc_vendor row per item with unlimited stock. Items are checked against item_template. Place it with .npc add <ID> or the Spawns tab.";
            Inputs.Add(new TemplateInput { Key = "look", Label = "Looks like creature", Kind = "creature", Hint = "Any creature whose model the vendor should use." });
            Inputs.Add(new TemplateInput { Key = "name", Label = "Name", Kind = "text", Default = "Studio Supplier" });
            Inputs.Add(new TemplateInput { Key = "subname", Label = "Title under the name", Kind = "text", Default = "Trade Goods", Optional = true });
            Inputs.Add(new TemplateInput { Key = "level", Label = "Level", Kind = "int", Default = "30" });
            Inputs.Add(new TemplateInput { Key = "faction", Label = "Faction template", Kind = "int", Default = "35", Hint = "35 = friendly to everyone." });
            Inputs.Add(new TemplateInput { Key = "items", Label = "Items to sell", Kind = "itemlist", Default = "159, 4540, 2512", Hint = "Item entries separated by commas or spaces." });
        }

        public override TemplateResult Build(IDictionary<string, string> v, ISqlReader db, Changeset cs, long customStart)
        {
            long look = Long(v, "look", "Looks like creature", 1);
            var name = V(v, "name"); if (name.Length == 0) throw new TemplateException("Name: the vendor needs a name.");
            int level = (int)Long(v, "level", "Level", 1, 255);
            long faction = Long(v, "faction", "Faction template", 1, 100000);
            var items = V(v, "items").Split(new[] { ',', ' ', ';', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => long.TryParse(s.TrimStart('#'), out var n) ? n : throw new TemplateException($"Items to sell: \"{s}\" isn't an item ID.")).Distinct().ToList();
            if (items.Count == 0) throw new TemplateException("Items to sell: list at least one item ID.");
            if (items.Count > 150) throw new TemplateException("Items to sell: at most 150 items (the vendor window's limit).");
            var found = Rows(db, $"SELECT entry FROM item_template WHERE entry IN ({string.Join(",", items)})", 200).Select(r => Convert.ToInt64(r.Values[0], CultureInfo.InvariantCulture)).ToHashSet();
            var missing = items.Where(i => !found.Contains(i)).ToList();
            if (missing.Count > 0) throw new TemplateException("These items don't exist in item_template: " + string.Join(", ", missing));
            var row = One(db, "creature_template", "entry", look) ?? throw new TemplateException($"Creature {look} doesn't exist.");
            long id = FreeId(db, cs, "creature_template", "entry", customStart);
            row["entry"] = id; row["name"] = name; row["subname"] = V(v, "subname"); row["minlevel"] = level; row["maxlevel"] = level;
            row["faction"] = faction; row["npcflag"] = 1 | 128;
            foreach (var c in new[] { "lootid", "pickpocketloot", "skinloot", "mingold", "maxgold", "difficulty_entry_1", "difficulty_entry_2", "difficulty_entry_3", "KillCredit1", "KillCredit2", "gossip_menu_id", "VehicleId" }) if (row.Has(c)) row[c] = 0;
            foreach (var c in new[] { "AIName", "ScriptName" }) if (row.Has(c)) row[c] = "";
            if (row.Has("unit_flags")) row["unit_flags"] = Convert.ToInt64(row.Num("unit_flags")) | 0x2 | 0x100;   // non-attackable, immune to players
            var res = new TemplateResult();
            var main = Insert(cs, "creature_template", row, "entry");
            main.Summary = $"New vendor #{id} \"{name}\"";
            res.Changes.Add(main);
            foreach (var m in Side(db, "creature_template_model", "CreatureID", look)) { m["CreatureID"] = id; res.Changes.Add(Insert(cs, "creature_template_model", m, new[] { "CreatureID", "Idx" }.Where(m.Has).ToArray())); }
            var cols = db.Query("SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'npc_vendor' ORDER BY ORDINAL_POSITION", 50).Rows.Select(r => Convert.ToString(r[0])).ToList();
            if (cols.Count == 0) cols = new List<string> { "entry", "slot", "item", "maxcount", "incrtime", "ExtendedCost", "VerifiedBuild" };
            int slot = 0;
            foreach (var item in items)
            {
                var vr = new Row { Columns = cols, Values = cols.Select(c => (object)0).ToArray() };
                vr["entry"] = id; vr["slot"] = slot++; vr["item"] = item;
                res.Changes.Add(Insert(cs, "npc_vendor", vr, new[] { "entry", "item", "ExtendedCost" }.Where(vr.Has).ToArray()));
            }
            Tag(res, "Creatures", null);
            res.Headline = $"Vendor #{id} \"{name}\" selling {items.Count} item(s), looking like #{look}.";
            res.Notes.Add("Unlimited stock, normal gold prices (item_template BuyPrice). Set ExtendedCost on the creature's Vendor tab for token prices.");
            res.Notes.Add("Not placed in the world yet: use .npc add " + id + " in game, or the Spawns tab.");
            return res;
        }
    }

    /// <summary>A new quest copied from an existing one, with a giver and an ender.</summary>
    public sealed class QuestTemplate : StudioTemplate
    {
        public QuestTemplate()
        {
            Key = "quest"; Glyph = "";
            Title = "New quest from a template";
            What = "Copy a quest you like (its objectives and type) into a new quest with your text, level, reward and NPCs.";
            Explain = "Copies quest_template (and quest_template_addon) of the quest you pick to a new ID in your custom range: objectives, required kills/items and flags stay the same; title, texts, level and money reward are yours; chain links (previous/next quest) are cleared. Adds creature_queststarter and creature_questender rows, and turns on the quest-giver flag on those NPCs if needed. Check the Objectives tab of the new quest afterwards.";
            Inputs.Add(new TemplateInput { Key = "base", Label = "Copy quest", Kind = "quest", Hint = "A quest with the kind of objective you want (kill X, collect Y…)." });
            Inputs.Add(new TemplateInput { Key = "title", Label = "Title", Kind = "text", Default = "A Studio Errand" });
            Inputs.Add(new TemplateInput { Key = "level", Label = "Quest level", Kind = "int", Default = "10" });
            Inputs.Add(new TemplateInput { Key = "minlevel", Label = "Min level to take it", Kind = "int", Default = "8" });
            Inputs.Add(new TemplateInput { Key = "objectives", Label = "Objective text", Kind = "longtext", Default = "Bring me what I asked for.", Hint = "The short summary in the quest log." });
            Inputs.Add(new TemplateInput { Key = "details", Label = "Quest text", Kind = "longtext", Default = "Hello, $N. I have a task for you.", Hint = "What the giver says. $N = player name, $C = class, $R = race." });
            Inputs.Add(new TemplateInput { Key = "money", Label = "Money reward, in copper", Kind = "int", Default = "500", Hint = "100 copper = 1 silver, 10000 = 1 gold." });
            Inputs.Add(new TemplateInput { Key = "giver", Label = "Quest giver", Kind = "creature", Hint = "Creature entry that offers the quest." });
            Inputs.Add(new TemplateInput { Key = "ender", Label = "Turned in to", Kind = "creature", Optional = true, Hint = "Leave empty to use the giver." });
        }

        public override TemplateResult Build(IDictionary<string, string> v, ISqlReader db, Changeset cs, long customStart)
        {
            long baseId = Long(v, "base", "Copy quest", 1);
            var title = V(v, "title"); if (title.Length == 0) throw new TemplateException("Title: the quest needs a title.");
            int level = (int)Long(v, "level", "Quest level", -1, 255), min = (int)Long(v, "minlevel", "Min level", 0, 255);
            long money = Long(v, "money", "Money reward", 0, 100000000);
            long giver = Long(v, "giver", "Quest giver", 1);
            long ender = V(v, "ender").Length == 0 ? giver : Long(v, "ender", "Turned in to", 1);
            var q = One(db, "quest_template", "ID", baseId) ?? throw new TemplateException($"Quest {baseId} doesn't exist.");
            var g = One(db, "creature_template", "entry", giver) ?? throw new TemplateException($"Quest giver: creature {giver} doesn't exist.");
            var e = ender == giver ? g : One(db, "creature_template", "entry", ender) ?? throw new TemplateException($"Turned in to: creature {ender} doesn't exist.");
            long id = FreeId(db, cs, "quest_template", "ID", customStart);
            q["ID"] = id; q["LogTitle"] = title; q["QuestLevel"] = level; q["MinLevel"] = min;
            q["LogDescription"] = V(v, "objectives"); q["QuestDescription"] = V(v, "details");
            if (q.Has("RewardMoney")) q["RewardMoney"] = money;
            foreach (var c in new[] { "RewardNextQuest", "RewardMailTemplateID", "StartItem" }) if (q.Has(c)) q[c] = 0;
            var res = new TemplateResult();
            var main = Insert(cs, "quest_template", q, "ID");
            main.Summary = $"New quest #{id} \"{title}\" (copy of #{baseId})";
            res.Changes.Add(main);
            foreach (var a in Side(db, "quest_template_addon", "ID", baseId))
            {
                a["ID"] = id;
                foreach (var c in new[] { "PrevQuestID", "NextQuestID", "ExclusiveGroup", "RewardMailTemplateID", "BreadcrumbForQuestId" }) if (a.Has(c)) a[c] = 0;
                res.Changes.Add(Insert(cs, "quest_template_addon", a, "ID"));
            }
            res.Changes.Add(Insert(cs, "creature_queststarter", new Row { Columns = new List<string> { "id", "quest" }, Values = new object[] { giver, id } }, "id", "quest"));
            res.Changes.Add(Insert(cs, "creature_questender", new Row { Columns = new List<string> { "id", "quest" }, Values = new object[] { ender, id } }, "id", "quest"));
            foreach (var (cid, row) in new[] { (giver, g), (ender, e) }.Distinct())
            {
                long flags = Convert.ToInt64(row.Num("npcflag"));
                if ((flags & 2) == 0) { var c = Update(cs, "creature_template", "entry", cid, row, "npcflag", flags | 2); if (c != null) res.Changes.Add(c); res.Notes.Add($"#{cid} \"{row.Str("name")}\" wasn't a quest giver; its npcflag gets the quest-giver bit."); }
            }
            Tag(res, "Quests", null);
            res.Headline = $"Quest #{id} \"{title}\" (level {level}), from #{giver} \"{g.Str("name")}\" to #{ender} \"{e.Str("name")}\".";
            res.Notes.Add($"Objectives (kills/items) are the same as quest #{baseId} \"{One(db, "quest_template", "ID", baseId)?.Str("LogTitle")}\". Change them on the new quest's Objectives tab.");
            return res;
        }
    }

    /// <summary>Make a creature cast a spell in combat with SmartAI.</summary>
    public sealed class SmartCasterTemplate : StudioTemplate
    {
        public SmartCasterTemplate()
        {
            Key = "smart-caster"; Glyph = "";
            Title = "Make a creature cast a spell in combat";
            What = "Adds a SmartAI row: in combat, cast a spell on its target (or itself) every few seconds.";
            Explain = "Adds one smart_scripts row (event: in combat, first after 2–4 s, then repeating; action: cast the spell; target: its victim or itself) with the next free row id for that creature, and sets AIName to SmartAI if it isn't yet. If the creature has a C++ ScriptName, SmartAI won't run: the template tells you. Open the result in SmartAI to fine-tune it.";
            Inputs.Add(new TemplateInput { Key = "creature", Label = "Creature", Kind = "creature" });
            Inputs.Add(new TemplateInput { Key = "spell", Label = "Spell", Kind = "spell", Default = "133", Hint = "Spell ID (e.g. 133 Fireball)." });
            Inputs.Add(new TemplateInput { Key = "min", Label = "Every, at the soonest (thousandths of a second)", Kind = "int", Default = "8000", Hint = "1000 = 1 second." });
            Inputs.Add(new TemplateInput { Key = "max", Label = "Every, at the latest (thousandths of a second)", Kind = "int", Default = "12000", Hint = "1000 = 1 second." });
            Inputs.Add(new TemplateInput { Key = "target", Label = "Cast on", Kind = "choice", Choices = new[] { "its target", "itself" }, Default = "its target" });
        }

        public override TemplateResult Build(IDictionary<string, string> v, ISqlReader db, Changeset cs, long customStart)
        {
            long id = Long(v, "creature", "Creature", 1);
            long spell = Long(v, "spell", "Spell", 1, uint.MaxValue);
            long min = Long(v, "min", "Every (min ms)", 0, 3600000), max = Long(v, "max", "Every (max ms)", 0, 3600000);
            if (max < min) throw new TemplateException("Every (max ms) must be at least the minimum.");
            var row = One(db, "creature_template", "entry", id) ?? throw new TemplateException($"Creature {id} doesn't exist.");
            var res = new TemplateResult();
            var existing = Rows(db, $"SELECT id FROM smart_scripts WHERE entryorguid = {id} AND source_type = 0", 1000).Select(r => Convert.ToInt32(r.Values[0], CultureInfo.InvariantCulture)).ToList();
            foreach (var c in cs.Changes.Where(c => c.Kind == ChangeKind.Insert && c.Table == "smart_scripts" && c.Key.Any(k => k.Column == "entryorguid" && k.Value == id.ToString(CultureInfo.InvariantCulture)) && c.Key.Any(k => k.Column == "source_type" && k.Value == "0")))
                if (int.TryParse(c.Key.First(k => k.Column == "id").Value, out var sid)) existing.Add(sid);
            int next = existing.Count == 0 ? 0 : existing.Max() + 1;
            if (row.Str("AIName") != "SmartAI") { var c = Update(cs, "creature_template", "entry", id, row, "AIName", "SmartAI"); if (c != null) res.Changes.Add(c); }
            var sr = new SmartRow { EntryOrGuid = id, SourceType = 0, Id = next, EventType = 0, ActionType = 11, TargetType = V(v, "target") == "itself" ? 1 : 2, Comment = $"{row.Str("name")} - In Combat - Cast spell {spell} (Studio template)" };
            sr.EventParams[0] = 2000; sr.EventParams[1] = 4000; sr.EventParams[2] = min; sr.EventParams[3] = max; sr.ActionParams[0] = spell;
            var vals = sr.ToValues();
            var lits = SmartRow.Columns.Select(c => vals.Lit(c)).ToList();
            var key = SmartRow.KeyColumns.Select(k => new KeyPart(k, vals.Lit(k), vals.Disp(k))).ToList();
            var ins = ChangeFactory.StageRowInsert(cs, "smart_scripts", key, SmartRow.Columns, lits);
            ins.Summary = $"SmartAI row {next} for #{id}: in combat, cast {spell} every {min / 1000.0:0.#}-{max / 1000.0:0.#} s";
            res.Changes.Add(ins);
            Tag(res, "SmartAI", null);
            res.Headline = $"#{id} \"{row.Str("name")}\" casts spell {spell} on {(sr.TargetType == 1 ? "itself" : "its target")} every {min / 1000.0:0.#}-{max / 1000.0:0.#} s in combat.";
            if (row.Str("ScriptName").Length > 0) res.Notes.Add($"Warning: it has the C++ script \"{row.Str("ScriptName")}\", which takes priority over SmartAI. Clear ScriptName for SmartAI to run.");
            return res;
        }
    }
}
