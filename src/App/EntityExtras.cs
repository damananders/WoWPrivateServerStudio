using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AndersCraftStudio.Core;

namespace AndersCraftStudio
{
    /// <summary>Read-only related lists (spawns, loot, vendors, quest givers...) and cross-links for the entity editors.</summary>
    public sealed partial class EntityEditorPage
    {
        string Id => id.Value.ToString(CultureInfo.InvariantCulture);

        static string spawnIdCol;
        /// <summary>The creature table's template column: `id1` on current AzerothCore, `id` on older databases.</summary>
        string SpawnIdCol()
        {
            if (spawnIdCol != null) return spawnIdCol;
            try
            {
                using var db = S.OpenRead(S.Profile.WorldDb);
                spawnIdCol = db.Scalar("SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'creature' AND COLUMN_NAME = 'id1'") == "1" ? "id1" : "id";
            }
            catch { return "id1"; }
            return spawnIdCol;
        }
        object Val(string col) => rows[D.Table].Table.Columns.Contains(col) ? rows[D.Table][col] : null;
        long LongVal(string col) { var v = Val(col); return v == null || v is DBNull ? 0 : Convert.ToInt64(v, CultureInfo.InvariantCulture); }

        string HeaderSubtitle()
        {
            switch (D.Key)
            {
                case "creatures":
                {
                    var lv = LongVal("minlevel") == LongVal("maxlevel") ? $"Level {LongVal("minlevel")}" : $"Level {LongVal("minlevel")}-{LongVal("maxlevel")}";
                    var rank = EntityDefs.CreatureRank.TryGetValue(LongVal("rank"), out var r) ? r : "";
                    var fac = refs.Name("factiontemplate", LongVal("faction"));
                    var sub = Convert.ToString(Val("subname"));
                    return string.Join(" · ", new[] { lv, rank, string.IsNullOrEmpty(sub) ? null : "<" + sub + ">", fac == null ? $"faction {LongVal("faction")}" : fac }.Where(x => !string.IsNullOrEmpty(x)));
                }
                case "items":
                {
                    var q = EntityDefs.ItemQuality.TryGetValue(LongVal("Quality"), out var qq) ? qq : "";
                    var slot = EntityDefs.InventoryType.TryGetValue(LongVal("InventoryType"), out var s) ? s : "";
                    return $"{q} · {slot} · item level {LongVal("ItemLevel")}" + (LongVal("RequiredLevel") > 0 ? $" · requires level {LongVal("RequiredLevel")}" : "");
                }
                default:
                    return $"Level {LongVal("QuestLevel")} · minimum {LongVal("MinLevel")}" + (EntityDefs.QuestInfo.TryGetValue(LongVal("QuestInfoID"), out var qi) && LongVal("QuestInfoID") != 0 ? " · " + qi : "");
            }
        }

        string TabTip(string t) => (D.Key, t) switch
        {
            ("creatures", "Spawns") => "Where this creature is placed in the world (editable, staged).",
            ("creatures", "Equipment") => "Weapons it visibly holds (editable, staged).",
            ("creatures", "Vendor/trainer") => "What it sells or teaches (editable, staged).",
            ("creatures", "Loot") => "Loot table IDs and what it drops; opens the Loot editor.",
            ("creatures", "SmartAI") => "Its SmartAI script rows; opens the SmartAI builder.",
            ("quests", "Starters/enders") => "Which NPCs and objects give and accept this quest.",
            ("quests", "Chain") => "Quests before and after this one.",
            _ => t + " fields",
        };

        IEnumerable<UIElement> CrossLinks()
        {
            Button Link(string text, string tip, Action a) { var b = U.Btn(text, a, "BtnGhost", tip); return b; }
            switch (D.Key)
            {
                case "creatures":
                    yield return Link("Loot ›", "What it drops.", () => ShowTab("Loot"));
                    yield return Link("SmartAI ›", "Its SmartAI script rows.", () => ShowTab("SmartAI"));
                    yield return Link("Spawns ›", "Where it stands in the world.", () => ShowTab("Spawns"));
                    break;
                case "items":
                    yield return Link("Dropped by ›", "Creatures whose loot includes it.", () => ShowTab("Basics"));
                    yield return Link("Sold by ›", "Vendors that sell it.", () => ShowTab("Prices"));
                    break;
                case "quests":
                    yield return Link("Givers ›", "Who starts and ends it.", () => ShowTab("Starters/enders"));
                    yield return Link("Chain ›", "Quests before and after.", () => ShowTab("Chain"));
                    break;
            }
        }

        IEnumerable<UIElement> Extras(string t)
        {
            switch ((D.Key, t))
            {
                case ("creatures", "Basics"):
                    yield return ListCard("Models", "Which 3D models this creature can appear as (creature_template_model). Edit them in the SQL browser for now.",
                        $"SELECT Idx, CreatureDisplayID, DisplayScale, Probability FROM creature_template_model WHERE CreatureID = {Id} ORDER BY Idx", "CreatureDisplayID", "creaturedisplay", "Model file");
                    yield return ListCard("Quests it gives", "Quests this NPC offers (creature_queststarter). Double-click to open.",
                        $"SELECT q.ID, q.LogTitle, q.QuestLevel FROM creature_queststarter s JOIN quest_template q ON q.ID = s.quest WHERE s.id = {Id} ORDER BY q.ID", open: ("quest", "ID"));
                    yield return ListCard("Quests it accepts", "Quests handed in to this NPC (creature_questender).",
                        $"SELECT q.ID, q.LogTitle, q.QuestLevel FROM creature_questender s JOIN quest_template q ON q.ID = s.quest WHERE s.id = {Id} ORDER BY q.ID", open: ("quest", "ID"));
                    break;
                case ("creatures", "Spawns"):
                    yield return ListEditor("spawns", () =>
                    {
                        var col = SpawnIdCol();
                        var ed = new RowListEditor(w, "Spawns", "Every placed copy of this creature. Edit positions, respawn time (spawntimesecs), wander distance and movement here. Duplicate row makes a new spawn with the next free guid; Remove row deletes a spawn.",
                            "creature", $"{SqlGen.Ident(col)} = {Id}", new Dictionary<string, string> { [col] = Id }, "guid",
                            new[] { new RowListEditor.NameColumn { IdColumn = "map", Kind = "map", Header = "map name" }, new RowListEditor.NameColumn { IdColumn = "zoneId", Kind = "area", Header = "zone name" }, new RowListEditor.NameColumn { IdColumn = "areaId", Kind = "area", Header = "area name" } },
                            refs.ByKind);
                        ed.NewRow = r => r.Set("guid", NextFree("creature", "guid").ToString(CultureInfo.InvariantCulture));
                        return ed;
                    });
                    break;
                case ("creatures", "Equipment"):
                    yield return ListEditor("equip", () => new RowListEditor(w, "Equipment sets", "Weapons it holds (creature_equip_template). ID is the set number; a spawn picks a set with its equipment_id. ItemID1 = main hand, ItemID2 = off hand, ItemID3 = ranged.",
                        "creature_equip_template", $"`CreatureID` = {Id}", new Dictionary<string, string> { ["CreatureID"] = Id }, "ID",
                        new[] { new RowListEditor.NameColumn { IdColumn = "ItemID1", Kind = "item", Header = "main hand" }, new RowListEditor.NameColumn { IdColumn = "ItemID2", Kind = "item", Header = "off hand" }, new RowListEditor.NameColumn { IdColumn = "ItemID3", Kind = "item", Header = "ranged" } },
                        refs.ByKind) { NewRow = r => r.Set("ID", "1") });
                    break;
                case ("creatures", "Vendor/trainer"):
                    yield return U.Callout("Services ticked: " + FlagMask.Describe(unchecked((ulong)LongVal("npcflag")), EntityDefs.NpcFlags) + ". Change them on the Basics tab (What talking to it does). A vendor needs the Vendor flag; a trainer needs Trainer.", "info", "Services");
                    yield return ListEditor("vendor", () => new RowListEditor(w, "Sells", "Items it sells (npc_vendor). slot = order in the window; maxcount 0 = unlimited stock, otherwise it restocks after incrtime seconds; ExtendedCost = an ItemExtendedCost ID for token/honor prices (0 = gold only).",
                        "npc_vendor", $"`entry` = {Id}", new Dictionary<string, string> { ["entry"] = Id }, "slot, item",
                        new[] { new RowListEditor.NameColumn { IdColumn = "item", Kind = "item", Header = "item name" } }, refs.ByKind));
                    var trainerId = TrainerId();
                    if (trainerId == null)
                        yield return U.Callout("This creature has no trainer list linked (creature_default_trainer). Link one with the SQL browser first; then its spells can be edited here.", "info", "Trainer");
                    else
                        yield return ListEditor("trainer", () => new RowListEditor(w, $"Teaches (trainer {trainerId})", "Spells it trains (trainer_spell). MoneyCost in copper; ReqLevel = minimum level; ReqSkillLine / ReqSkillRank for professions; ReqAbility = spells needed first. Other creatures linked to the same trainer list change too.",
                            "trainer_spell", $"`TrainerId` = {trainerId}", new Dictionary<string, string> { ["TrainerId"] = trainerId }, "ReqLevel, SpellId",
                            new[] { new RowListEditor.NameColumn { IdColumn = "SpellId", Kind = "spell", Header = "spell name" } }, refs.ByKind));
                    break;
                case ("creatures", "Loot"):
                    var lid = LongVal("lootid");
                    yield return OpenButton($"Edit these drops in the Loot editor (with drop simulator)", () => w.Navigate("loot", pg => ((LootPage)pg).Open("creature_loot_template", lid)));
                    yield return ListCard("Drops (creature_loot_template " + lid + ")", "Chance is in percent. A Reference row points to a shared list. Double-click to open the item.",
                        $"SELECT l.Item, i.name, l.Reference, l.Chance, l.QuestRequired, l.GroupId, l.MinCount, l.MaxCount FROM creature_loot_template l LEFT JOIN item_template i ON i.entry = l.Item WHERE l.Entry = {lid} ORDER BY l.GroupId, l.Chance DESC", open: ("item", "Item"));
                    yield return SqlLink("creature_loot_template", $"`Entry` = {lid}", "Open these loot rows in the SQL browser");
                    break;
                case ("creatures", "SmartAI"):
                    string ai = Convert.ToString(Val("AIName"));
                    yield return U.Callout(ai == "SmartAI" ? "AI type is SmartAI, so the rows below drive this creature." : $"AI type is \"{(ai == "" ? "none" : ai)}\", so these rows are ignored until AI type is set to SmartAI on the Basics tab.", ai == "SmartAI" ? "ok" : "warn", "AI type");
                    yield return OpenButton("Open in the SmartAI builder", () => w.Navigate("smartai", pg => ((SmartAIPage)pg).Open(0, id.Value)));
                    yield return ListCard("SmartAI rows", "Rows for this creature template (smart_scripts, source_type 0) plus rows for individual spawns (negative entryorguid).",
                        $"SELECT entryorguid, id, link, event_type, event_chance, action_type, target_type, comment FROM smart_scripts WHERE source_type = 0 AND (entryorguid = {Id} OR entryorguid IN (SELECT -guid FROM creature WHERE {SpawnIdCol()} = {Id})) ORDER BY entryorguid DESC, id LIMIT 300");
                    yield return SqlLink("smart_scripts", $"`entryorguid` = {Id} AND `source_type` = 0", "Open SmartAI rows in the SQL browser");
                    break;
                case ("items", "Basics"):
                    yield return ListCard("Dropped by", "Creatures whose loot table includes this item, with the drop chance. Double-click to open the creature.",
                        $"SELECT c.entry, c.name, l.Chance, l.QuestRequired FROM creature_loot_template l JOIN creature_template c ON c.lootid = l.Entry WHERE l.Item = {Id} ORDER BY l.Chance DESC LIMIT 200", open: ("creature", "entry"));
                    yield return ListCard("Used by quests", "Quests that ask for or give this item.",
                        $"SELECT ID, LogTitle, QuestLevel FROM quest_template WHERE {Id} IN (RequiredItemId1, RequiredItemId2, RequiredItemId3, RequiredItemId4, RequiredItemId5, RequiredItemId6, RewardItem1, RewardItem2, RewardItem3, RewardItem4, RewardChoiceItemID1, RewardChoiceItemID2, RewardChoiceItemID3, RewardChoiceItemID4, RewardChoiceItemID5, RewardChoiceItemID6, StartItem) ORDER BY ID LIMIT 200", open: ("quest", "ID"));
                    break;
                case ("items", "Prices"):
                    yield return ListCard("Sold by", "Vendors that stock this item (npc_vendor). Double-click to open the vendor.",
                        $"SELECT c.entry, c.name, c.subname, v.maxcount, v.ExtendedCost FROM npc_vendor v JOIN creature_template c ON c.entry = v.entry WHERE v.item = {Id} ORDER BY c.entry LIMIT 200", open: ("creature", "entry"));
                    break;
                case ("quests", "Starters/enders"):
                    yield return ListCard("Started by NPCs", "creature_queststarter. Double-click to open the NPC.",
                        $"SELECT c.entry, c.name, c.subname FROM creature_queststarter s JOIN creature_template c ON c.entry = s.id WHERE s.quest = {Id}", open: ("creature", "entry"));
                    yield return ListCard("Started by objects", "gameobject_queststarter (wanted posters, books...).",
                        $"SELECT g.entry, g.name FROM gameobject_queststarter s JOIN gameobject_template g ON g.entry = s.id WHERE s.quest = {Id}");
                    yield return ListCard("Handed in to NPCs", "creature_questender.",
                        $"SELECT c.entry, c.name, c.subname FROM creature_questender s JOIN creature_template c ON c.entry = s.id WHERE s.quest = {Id}", open: ("creature", "entry"));
                    yield return ListCard("Handed in to objects", "gameobject_questender.",
                        $"SELECT g.entry, g.name FROM gameobject_questender s JOIN gameobject_template g ON g.entry = s.id WHERE s.quest = {Id}");
                    yield return SqlLink("creature_queststarter", $"`quest` = {Id}", "Change quest givers in the SQL browser");
                    break;
                case ("quests", "Chain"):
                    yield return ListCard("Leads here", "Quests that point to this one as their follow-up.",
                        $"SELECT q.ID, q.LogTitle, q.QuestLevel FROM quest_template q LEFT JOIN quest_template_addon a ON a.ID = q.ID WHERE q.RewardNextQuest = {Id} OR a.NextQuestID = {Id} ORDER BY q.ID", open: ("quest", "ID"));
                    yield return ListCard("Unlocked by finishing this", "Quests that require this one first (PrevQuestID).",
                        $"SELECT q.ID, q.LogTitle, q.QuestLevel FROM quest_template_addon a JOIN quest_template q ON q.ID = a.ID WHERE a.PrevQuestID = {Id} ORDER BY q.ID", open: ("quest", "ID"));
                    break;
            }
        }

        readonly Dictionary<string, RowListEditor> listEditors = new Dictionary<string, RowListEditor>();

        /// <summary>An editable list, kept while you switch tabs so unstaged edits aren't lost.</summary>
        UIElement ListEditor(string key, Func<RowListEditor> make)
        {
            var k = Id + ":" + key;
            if (!listEditors.TryGetValue(k, out var ed)) { ed = make(); listEditors[k] = ed; ed.Load(); }
            if (ed.View is FrameworkElement fe && fe.Parent is Border old) old.Child = null;
            return U.Card(ed.View, new Thickness(18, 14, 18, 14), new Thickness(0, 0, 0, 12));
        }

        public RowListEditor ListEditorFor(string key) => listEditors.TryGetValue(Id + ":" + key, out var e) ? e : null;

        string TrainerId()
        {
            try { using var db = S.OpenRead(S.Profile.WorldDb); return db.Scalar($"SELECT TrainerId FROM creature_default_trainer WHERE CreatureId = {Id} LIMIT 1"); }
            catch { return null; }
        }

        /// <summary>Next free numeric key (above the database's highest and any staged inserts).</summary>
        long NextFree(string table, string col)
        {
            long max = 0;
            try { using var db = S.OpenRead(S.Profile.WorldDb); long.TryParse(db.Scalar($"SELECT COALESCE(MAX({SqlGen.Ident(col)}), 0) FROM {SqlGen.Ident(table)}"), out max); } catch { }
            foreach (var c in S.Changes.Pending.Changes.Where(c => c.Kind == ChangeKind.Insert && c.Table == table))
                foreach (var kp in c.Key.Where(k => k.Column == col)) if (long.TryParse(kp.Value, out var v) && v > max) max = v;
            foreach (var ed in listEditors.Values.Where(e => e.Table == table && e.Data != null))
                foreach (System.Data.DataRow r in ed.Data.Rows) if (r.RowState != System.Data.DataRowState.Deleted && long.TryParse(Convert.ToString(r[col]), out var v) && v > max) max = v;
            return max + 1;
        }

        UIElement OpenButton(string text, Action go)
        {
            var b = U.Btn(text, go, "BtnPrimary");
            b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 0, 0, 12);
            return b;
        }

        UIElement M3Note(string title, string text)
        {
            var b = U.Callout(text, "info", title);
            return b;
        }

        UIElement SqlLink(string table, string where, string text)
        {
            var b = U.Btn(text, () => w.Navigate("sql", pg => { var sp = (SqlBrowserPage)pg; sp.OpenTable("world", table); sp.ScreensFilter(where); }), null, "Read and stage raw row changes in the SQL browser.");
            b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 0, 0, 16);
            return b;
        }

        /// <summary>A card with a read-only grid. Optional DBC name columns are added next to ID columns; double-click can open a linked entity.</summary>
        UIElement ListCard(string title, string help, string sql, string idCol1 = null, string kind1 = null, string name1 = null, string idCol2 = null, string kind2 = null, string name2 = null, (string kind, string col)? open = null)
        {
            DataTable dt;
            string err = null;
            try
            {
                using var db = S.OpenRead(S.Profile.WorldDb);
                dt = db.QueryTable(sql, 500, out _);
            }
            catch (Exception ex) { dt = null; err = ex.Message; }
            var sp = new StackPanel();
            sp.Children.Add(U.Row(U.T(title, 15, "Text", FontWeights.SemiBold, false), new Border { Width = 10 }, U.Pill(dt == null ? "n/a" : dt.Rows.Count >= 500 ? "500+" : dt.Rows.Count.ToString(), "Panel2", "Muted"), U.Pill("read-only", "OkBg", "Ok")));
            sp.Children.Add(U.T(help, 12.5, "Muted", null, true, new Thickness(0, 4, 0, 10)));
            if (dt == null) { sp.Children.Add(U.T("Not available on this server: " + err, 12.5, "Faint")); return U.Card(sp, new Thickness(18, 14, 18, 14), new Thickness(0, 0, 0, 12)); }
            if (dt.Rows.Count == 0) { sp.Children.Add(U.T("None.", 13, "Faint")); return U.Card(sp, new Thickness(18, 14, 18, 14), new Thickness(0, 0, 0, 12)); }
            void AddName(string col, string kind, string label)
            {
                if (col == null || !dt.Columns.Contains(col)) return;
                var nc = dt.Columns.Add(label, typeof(string));
                nc.SetOrdinal(dt.Columns[col].Ordinal + 1);
                foreach (DataRow r in dt.Rows) { var v = r[col]; if (!(v is DBNull)) r[nc] = S.Dbc.Name(kind, Convert.ToInt64(v, CultureInfo.InvariantCulture)) ?? ""; }
            }
            AddName(idCol1, kind1, name1); AddName(idCol2, kind2, name2);
            var g = new DataGrid { IsReadOnly = true, AutoGenerateColumns = true, MaxHeight = 360, SelectionUnit = DataGridSelectionUnit.FullRow, ItemsSource = dt.DefaultView };
            g.AutoGeneratingColumn += (s, e) => { e.Column.Header = new TextBlock { Text = e.PropertyName }; e.Column.MaxWidth = 320; };
            if (open != null)
            {
                var (kind, col) = open.Value;
                var ws = Refs.Workspace(kind);
                g.ToolTip = "Double-click a row to open it.";
                g.MouseDoubleClick += (s, e) =>
                {
                    if (g.SelectedItem is DataRowView rv && dt.Columns.Contains(col) && !(rv.Row[col] is DBNull))
                    {
                        var v = Convert.ToInt64(rv.Row[col], CultureInfo.InvariantCulture);
                        if (v > 0 && ws != null) w.Navigate(ws, pg => ((EntityEditorPage)pg).Open(v));
                    }
                };
            }
            sp.Children.Add(g);
            return U.Card(sp, new Thickness(18, 14, 18, 14), new Thickness(0, 0, 0, 12));
        }
    }
}
