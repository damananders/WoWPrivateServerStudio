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
                case "gameobjects":
                {
                    var t = LongVal("type");
                    var size = Val("size") is float f && Math.Abs(f - 1f) > 0.001 ? $" · size {f.ToString("0.##", CultureInfo.InvariantCulture)}" : "";
                    return $"{GameObjectData.TypeName(t)} (type {t}) · model {LongVal("displayId")}{size}";
                }
                case "npctext":
                {
                    int n = Enumerable.Range(0, 8).Count(v => !string.IsNullOrEmpty(Convert.ToString(Val($"text{v}_0"))) || !string.IsNullOrEmpty(Convert.ToString(Val($"text{v}_1"))));
                    return $"{n} variant(s) with text · what an NPC says when its gossip window opens";
                }
                case "pagetext":
                {
                    var next = LongVal("NextPageID");
                    return next != 0 ? $"Continues on page {next}" : "Last page of its book";
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
            ("creatures", "Look & movement") => "Mount, pose, looping animation, auras, movement abilities, resistances (template addon + movement).",
            ("creatures", "Extras") => "Click-to-use spells and the lines it says.",
            ("quests", "Texts") => "What the NPC says when you return and when you hand in.",
            ("items", "Contains & enchants") => "Random enchants, and what opening / disenchanting / prospecting / milling gives.",
            ("gameobjects", "Type data") => "Data0-Data23, labelled for this object's type.",
            ("gameobjects", "Spawns") => "Where this object is placed in the world (editable, staged).",
            ("gameobjects", "Loot") => "What a chest or fishing pool contains; opens the Loot editor.",
            ("gameobjects", "Extras") => "Faction, flags and money (gameobject_template_addon).",
            ("npctext", "Used by") => "Gossip menus, creatures and objects that show this text.",
            ("pagetext", "Used by") => "Items, objects and pages that lead to this page.",
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
                case "gameobjects":
                {
                    yield return Link("Type data ›", "Its settings, labelled for its type.", () => ShowTab("Type data"));
                    yield return Link("Spawns ›", "Where it is placed in the world.", () => ShowTab("Spawns"));
                    if (GameObjectData.LootSlot(LongVal("type")) >= 0) yield return Link("Loot ›", "What's inside.", () => ShowTab("Loot"));
                    var goId = id.Value;
                    yield return Link("SmartAI ›", "Its SmartAI rows (source type 1).", () => w.Navigate("smartai", pg => ((SmartAIPage)pg).Open(1, goId)));
                    var ps = GameObjectData.PageSlot(LongVal("type"));
                    if (ps >= 0 && LongVal("Data" + ps) > 0) { var pid = LongVal("Data" + ps); yield return Link("Read the page ›", "Open its page text.", () => w.Navigate("pagetext", pg => ((EntityEditorPage)pg).Open(pid))); }
                    break;
                }
                case "npctext":
                    yield return Link("Used by ›", "Gossip menus and NPCs that show it.", () => ShowTab("Used by"));
                    break;
                case "pagetext":
                    if (LongVal("NextPageID") > 0) { var np = LongVal("NextPageID"); yield return Link("Next page ›", "Open the next page of the book.", () => w.Navigate("pagetext", pg => ((EntityEditorPage)pg).Open(np))); }
                    yield return Link("Used by ›", "Items and objects that show it.", () => ShowTab("Used by"));
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
                    yield return ListEditor("models", () =>
                    {
                        var ed = new RowListEditor(w, "Models", "Which 3D models this creature can appear as (creature_template_model). Idx numbers the models from 0; each spawn picks one by Probability (a weight). DisplayScale 1 = normal size.",
                            "creature_template_model", $"`CreatureID` = {Id}", new Dictionary<string, string> { ["CreatureID"] = Id }, "Idx",
                            new[] { new RowListEditor.NameColumn { IdColumn = "CreatureDisplayID", Kind = "creaturedisplay", Header = "model file" } }, refs.ByKind);
                        ed.NewRow = r => r.Set("Idx", NextIndex(ed, "Idx")).Set("DisplayScale", "1").Set("Probability", "1");
                        return ed;
                    });
                    yield return CreaturePreview();
                    yield return ListCard("Quests it gives", "Quests this NPC offers (creature_queststarter). Double-click to open.",
                        $"SELECT q.ID, q.LogTitle, q.QuestLevel FROM creature_queststarter s JOIN quest_template q ON q.ID = s.quest WHERE s.id = {Id} ORDER BY q.ID", open: ("quest", "ID"));
                    yield return ListCard("Quests it accepts", "Quests handed in to this NPC (creature_questender).",
                        $"SELECT q.ID, q.LogTitle, q.QuestLevel FROM creature_questender s JOIN quest_template q ON q.ID = s.quest WHERE s.id = {Id} ORDER BY q.ID", open: ("quest", "ID"));
                    break;
                case ("creatures", "Look & movement"):
                    yield return ListEditor("resist", () => new RowListEditor(w, "Resistances", "creature_template_resistance: extra resistance per magic school. School 1 holy, 2 fire, 3 nature, 4 frost, 5 shadow, 6 arcane; Resistance is the amount (about 5 per level resists a lot).",
                        "creature_template_resistance", $"`CreatureID` = {Id}", new Dictionary<string, string> { ["CreatureID"] = Id }, "School") { NewRow = r => r.Set("School", "2") });
                    yield return ListEditor("tspells", () =>
                    {
                        var ed = new RowListEditor(w, "Spells it can be made to cast", "creature_template_spell: the action bar a player gets when controlling it (vehicles, mind control, possessed pets). Index 0-7 is the button.",
                            "creature_template_spell", $"`CreatureID` = {Id}", new Dictionary<string, string> { ["CreatureID"] = Id }, "`Index`",
                            new[] { new RowListEditor.NameColumn { IdColumn = "Spell", Kind = "spell", Header = "spell name" } }, refs.ByKind);
                        ed.NewRow = r => r.Set("Index", NextIndex(ed, "Index"));
                        return ed;
                    });
                    break;
                case ("creatures", "Extras"):
                    yield return ListEditor("spellclick", () => new RowListEditor(w, "Click to use (spell click)", "npc_spellclick_spells: clicking the creature casts a spell (mounting vehicles, riding taxis, quest helpers). The creature also needs the 'Click to use' service ticked on Basics. cast_flags: 1 the player casts, 2 targets the creature (0/1/2/3 combine); user_type 0 anyone, 1 friendly, 2 raid, 3 party.",
                        "npc_spellclick_spells", $"`npc_entry` = {Id}", new Dictionary<string, string> { ["npc_entry"] = Id }, "spell_id",
                        new[] { new RowListEditor.NameColumn { IdColumn = "spell_id", Kind = "spell", Header = "spell name" } }, refs.ByKind) { NewRow = r => r.Set("cast_flags", "1") });
                    yield return ListEditor("ctext", () =>
                    {
                        var ed = new RowListEditor(w, "What it says (creature_text)", "Lines it can say or yell. SmartAI's Say a line picks a GroupID; several IDs in one group are picked at random by Probability. Type 12 say, 14 yell, 16 emote, 41 boss emote, 15 whisper, 42 boss whisper. Language 0 = everyone understands.",
                            "creature_text", $"`CreatureID` = {Id}", new Dictionary<string, string> { ["CreatureID"] = Id }, "GroupID, ID");
                        ed.NewRow = r => r.Set("ID", NextIndex(ed, "ID")).Set("Type", "12").Set("Probability", "100");
                        return ed;
                    });
                    yield return OpenButton("Open its SmartAI script", () => w.Navigate("smartai", pg => ((SmartAIPage)pg).Open(0, id.Value)));
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
                    yield return ListEditor("spawnaddon", () => new RowListEditor(w, "Per-spawn look and pose (creature_addon)", "Overrides the template's look for single spawns: path_id (its own waypoint path), mount, bytes1 (stand state), bytes2 (weapon pose), emote (looping animation), auras (spell IDs). Add a row with the spawn's guid.",
                        "creature_addon", $"`guid` IN (SELECT guid FROM creature WHERE {SqlGen.Ident(SpawnIdCol())} = {Id})", null, "guid"));
                    yield return ListEditor("formations", () => new RowListEditor(w, "Formations (creature_formations)", "Spawns that move together. Every member (also the leader) gets a row: leaderGUID, memberGUID, dist (yards from the leader), angle (degrees), groupAI (1 members help the leader, 2 the leader helps members, 3 both).",
                        "creature_formations", $"`leaderGUID` IN (SELECT guid FROM creature WHERE {SqlGen.Ident(SpawnIdCol())} = {Id}) OR `memberGUID` IN (SELECT guid FROM creature WHERE {SqlGen.Ident(SpawnIdCol())} = {Id})", null, "leaderGUID, memberGUID"));
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
                    yield return ListEditor("cqitems", () =>
                    {
                        var ed = new RowListEditor(w, "Quest items it drops (creature_questitem)", "Tells the client which quest items this creature can drop, so they sparkle and show in its tooltip. The drop itself comes from the loot table above.",
                            "creature_questitem", $"`CreatureEntry` = {Id}", new Dictionary<string, string> { ["CreatureEntry"] = Id }, "Idx",
                            new[] { new RowListEditor.NameColumn { IdColumn = "ItemId", Kind = "item", Header = "item name" } }, refs.ByKind);
                        ed.NewRow = r => r.Set("Idx", NextIndex(ed, "Idx"));
                        return ed;
                    });
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
                    foreach (var (table, title, help, kind) in new[]
                    {
                        ("creature_queststarter", "Started by NPCs", "NPCs that offer this quest (put the creature entry in id). They also need the Quest giver service ticked.", "creature"),
                        ("gameobject_queststarter", "Started by objects", "Objects that offer it: wanted posters, books… (game object entry in id).", "gameobject"),
                        ("creature_questender", "Handed in to NPCs", "NPCs that accept it when it's done.", "creature"),
                        ("gameobject_questender", "Handed in to objects", "Objects that accept it.", "gameobject"),
                    })
                    {
                        var tbl = table; var k = kind; var ti = title; var he = help;
                        yield return ListEditor(tbl, () => new RowListEditor(w, ti, he, tbl, $"`quest` = {Id}", new Dictionary<string, string> { ["quest"] = Id }, "id",
                            new[] { new RowListEditor.NameColumn { IdColumn = "id", Kind = k, Header = k + " name" } }, refs.ByKind));
                    }
                    break;
                case ("gameobjects", "Basics"):
                    yield return Preview3D("3D preview", v => v.Load(S.Models.GameObject(CurrentLong(D.Fields.First(f => f.Column == "displayId")))));
                    yield return ListCard("Quests it starts", "gameobject_queststarter: quests offered by clicking this object. Double-click to open.",
                        $"SELECT q.ID, q.LogTitle, q.QuestLevel FROM gameobject_queststarter s JOIN quest_template q ON q.ID = s.quest WHERE s.id = {Id} ORDER BY q.ID", open: ("quest", "ID"));
                    yield return ListCard("Quests it accepts", "gameobject_questender: quests handed in at this object.",
                        $"SELECT q.ID, q.LogTitle, q.QuestLevel FROM gameobject_questender s JOIN quest_template q ON q.ID = s.quest WHERE s.id = {Id} ORDER BY q.ID", open: ("quest", "ID"));
                    yield return ListCard("Quests that ask you to use it", "Quests whose objective is this object (a negative RequiredNpcOrGo).",
                        $"SELECT ID, LogTitle, QuestLevel FROM quest_template WHERE -{Id} IN (RequiredNpcOrGo1, RequiredNpcOrGo2, RequiredNpcOrGo3, RequiredNpcOrGo4) ORDER BY ID LIMIT 200", open: ("quest", "ID"));
                    break;
                case ("gameobjects", "Spawns"):
                    yield return ListEditor("gospawns", () =>
                    {
                        var ed = new RowListEditor(w, "Spawns", "Every placed copy of this object. Edit positions, rotation, respawn time (spawntimesecs; negative = stays despawned until a script spawns it) and state here. Duplicate row makes a new spawn with the next free guid; Remove row deletes a spawn.",
                            "gameobject", $"`id` = {Id}", new Dictionary<string, string> { ["id"] = Id }, "guid",
                            new[] { new RowListEditor.NameColumn { IdColumn = "map", Kind = "map", Header = "map name" }, new RowListEditor.NameColumn { IdColumn = "zoneId", Kind = "area", Header = "zone name" }, new RowListEditor.NameColumn { IdColumn = "areaId", Kind = "area", Header = "area name" } },
                            refs.ByKind);
                        ed.NewRow = r => r.Set("guid", NextFree("gameobject", "guid").ToString(CultureInfo.InvariantCulture));
                        return ed;
                    });
                    yield return ListEditor("goaddon", () => new RowListEditor(w, "Per-spawn extras (gameobject_addon)", "For single spawns: parent_rotation0-3 (rotation when it's part of a transport) and invisibility (invisibilityType 0 none, 1 general, 3 trap…; invisibilityValue = detection needed). Add a row with the spawn's guid.",
                        "gameobject_addon", $"`guid` IN (SELECT guid FROM gameobject WHERE id = {Id})", null, "guid"));
                    break;
                case ("gameobjects", "Loot"):
                {
                    var type = CurrentLong(D.Fields.First(f => f.Column == "type"));
                    var slot = GameObjectData.LootSlot(type);
                    if (slot < 0)
                    {
                        yield return U.Callout($"A {GameObjectData.TypeName(type)} has no loot table. Only chests (type 3) and fishing pools (type 25) keep a loot ID, in Data1.", "info", "Loot");
                        yield return GoQuestItems();
                        break;
                    }
                    var lootId = CurrentLong(D.Fields.First(f => f.Column == "Data" + slot));
                    if (lootId == 0) { yield return U.Callout("Its loot table ID (Data1 on the Type data tab) is 0, so it contains nothing. Set it to a gameobject_loot_template entry; the object's own entry number is the usual choice.", "warn", "Loot"); break; }
                    yield return OpenButton("Edit these drops in the Loot editor (with drop simulator)", () => w.Navigate("loot", pg => ((LootPage)pg).Open("gameobject_loot_template", lootId)));
                    yield return ListCard($"Contents (gameobject_loot_template {lootId})", "Chance is in percent. A Reference row points to a shared list. Double-click to open the item.",
                        $"SELECT l.Item, i.name, l.Reference, l.Chance, l.QuestRequired, l.GroupId, l.MinCount, l.MaxCount FROM gameobject_loot_template l LEFT JOIN item_template i ON i.entry = l.Item WHERE l.Entry = {lootId} ORDER BY l.GroupId, l.Chance DESC", open: ("item", "Item"));
                    yield return SqlLink("gameobject_loot_template", $"`Entry` = {lootId}", "Open these loot rows in the SQL browser");
                    yield return GoQuestItems();
                    break;
                }
                case ("items", "Display"):
                    yield return Preview3D("3D preview", v => v.Load(S.Models.Item(CurrentLong(D.Fields.First(f => f.Column == "displayid")), CurrentLong(D.Fields.First(f => f.Column == "InventoryType")))),
                        "Weapons, shields, helmets, shoulders and other items with a model of their own. Body armour has no model: it's drawn onto the character.");
                    break;
                case ("items", "Contains & enchants"):
                {
                    long rp = LongVal("RandomProperty"), rs = LongVal("RandomSuffix");
                    if (rp == 0 && rs == 0) yield return U.Callout("This item has no random enchant group (Display tab: Random enchant group / Random suffix group are 0), so it always has the same stats.", "info", "Random enchants");
                    else
                    {
                        var grp = rp != 0 ? rp : rs;
                        yield return ListEditor("ench" + grp, () => new RowListEditor(w, $"Random enchants (item_enchantment_template {grp})", "When the item drops it rolls one of these: ench = an ItemRandomProperties ID (for Random enchant group) or ItemRandomSuffix ID (for Random suffix group); chance in percent. Other items with the same group change too.",
                            "item_enchantment_template", $"`entry` = {grp}", new Dictionary<string, string> { ["entry"] = L(grp) }, "ench"));
                    }
                    foreach (var (table, title, entry, why) in new[]
                    {
                        ("item_loot_template", "What's inside when opened", LongVal("entry") , "Bags, boxes and clams (needs 'Can be opened' in Item flags)."),
                        ("disenchant_loot_template", "What disenchanting gives", LongVal("DisenchantID"), "The entry is the item's DisenchantID."),
                        ("prospecting_loot_template", "What prospecting gives", LongVal("entry"), "Ores: needs the prospectable item flag."),
                        ("milling_loot_template", "What milling gives", LongVal("entry"), "Herbs: needs the millable item flag."),
                    })
                    {
                        if (entry == 0) continue;
                        var tbl = table; var en = entry;
                        yield return ListCard($"{title} ({tbl} {en})", why + " Double-click an item to open it.",
                            $"SELECT l.Item, i.name, l.Reference, l.Chance, l.GroupId, l.MinCount, l.MaxCount FROM {tbl} l LEFT JOIN item_template i ON i.entry = l.Item WHERE l.Entry = {L(en)} ORDER BY l.GroupId, l.Chance DESC", open: ("item", "Item"));
                        yield return OpenButton($"Edit in the Loot editor ({tbl})", () => w.Navigate("loot", pg => ((LootPage)pg).Open(tbl, en)));
                    }
                    break;
                }
                case ("npctext", "Variants 1-4"):
                    yield return NpcTextPreview();
                    break;
                case ("npctext", "Used by"):
                    yield return ListCard("Gossip menus", "gossip_menu rows that show this text (conditions can pick between several texts of one menu).",
                        $"SELECT MenuID, TextID FROM gossip_menu WHERE TextID = {Id} ORDER BY MenuID");
                    yield return ListCard("Creatures", "Creatures whose gossip menu shows this text. Double-click to open.",
                        $"SELECT c.entry, c.name, c.subname, c.gossip_menu_id FROM creature_template c JOIN gossip_menu g ON g.MenuID = c.gossip_menu_id WHERE g.TextID = {Id} ORDER BY c.entry LIMIT 200", open: ("creature", "entry"));
                    yield return ListCard("Game objects", "Quest givers (Data3) and goobers (Data19) whose gossip menu shows this text.",
                        $"SELECT o.entry, o.name, o.type FROM gameobject_template o JOIN gossip_menu g ON g.MenuID = IF(o.type = 2, o.Data3, o.Data19) WHERE o.type IN (2, 10) AND g.TextID = {Id} ORDER BY o.entry LIMIT 200", open: ("gameobject", "entry"));
                    yield return OpenButton("Open gossip menus in World tables", () => w.Navigate("world"));
                    break;
                case ("pagetext", "Page"):
                    yield return BookPreview();
                    break;
                case ("pagetext", "Used by"):
                    yield return ListCard("Items", "Items that open this page when read (item_template.PageText). Double-click to open.",
                        $"SELECT entry, name FROM item_template WHERE PageText = {Id} ORDER BY entry LIMIT 200", open: ("item", "entry"));
                    yield return ListCard("Game objects", "Readable objects (type 9, Data0) and goobers (type 10, Data7) that show this page.",
                        $"SELECT entry, name, type FROM gameobject_template WHERE (type = 9 AND Data0 = {Id}) OR (type = 10 AND Data7 = {Id}) ORDER BY entry LIMIT 200", open: ("gameobject", "entry"));
                    yield return ListCard("Pages before it", "Pages whose Next page is this one. Double-click to open.",
                        $"SELECT ID, NextPageID, LEFT(Text, 80) AS Text FROM page_text WHERE NextPageID = {Id} ORDER BY ID", open: ("pagetext", "ID"));
                    break;
                case ("quests", "Chain"):
                    yield return ListCard("Leads here", "Quests that point to this one as their follow-up.",
                        $"SELECT q.ID, q.LogTitle, q.QuestLevel FROM quest_template q LEFT JOIN quest_template_addon a ON a.ID = q.ID WHERE q.RewardNextQuest = {Id} OR a.NextQuestID = {Id} ORDER BY q.ID", open: ("quest", "ID"));
                    yield return ListCard("Unlocked by finishing this", "Quests that require this one first (PrevQuestID).",
                        $"SELECT q.ID, q.LogTitle, q.QuestLevel FROM quest_template_addon a JOIN quest_template q ON q.ID = a.ID WHERE a.PrevQuestID = {Id} ORDER BY q.ID", open: ("quest", "ID"));
                    break;
            }
        }

        static string L(long v) => v.ToString(CultureInfo.InvariantCulture);

        /// <summary>A card with the 3D viewer, filled by <paramref name="load"/> (read-only).</summary>
        UIElement Preview3D(string title, Action<ModelViewer> load, string help = null, UIElement extra = null)
        {
            var v = new ModelViewer(S.Models);
            var sp = new StackPanel();
            var head = U.Row(U.T(title, 15, "Text", FontWeights.SemiBold, false), new Border { Width = 10 }, U.Pill("read-only", "OkBg", "Ok"));
            if (extra != null) { head.Children.Add(new Border { Width = 12 }); head.Children.Add(extra); }
            sp.Children.Add(head);
            if (help != null) sp.Children.Add(U.T(help, 12.5, "Muted", null, true, new Thickness(0, 4, 0, 8)));
            else sp.Children.Add(new Border { Height = 8 });
            sp.Children.Add(v.View);
            lastViewer = v;
            try { load(v); } catch (Exception ex) { v.ShowMessage(ex.Message); }
            return U.Card(sp, new Thickness(18, 14, 18, 14), new Thickness(0, 0, 0, 12));
        }

        public ModelViewer lastViewer;

        UIElement CreaturePreview()
        {
            var displays = new List<long>();
            try
            {
                using var db = S.OpenRead(S.Profile.WorldDb);
                foreach (var r in db.Query($"SELECT CreatureDisplayID FROM creature_template_model WHERE CreatureID = {Id} ORDER BY Idx", 10).Rows) displays.Add(Convert.ToInt64(r[0], CultureInfo.InvariantCulture));
            }
            catch { }
            foreach (var c in new[] { "modelid1", "modelid2", "modelid3", "modelid4" }) { var x = LongVal(c); if (x > 0 && !displays.Contains(x)) displays.Add(x); }   // older databases
            var pick = new ComboBox { Width = 180 };
            foreach (var d in displays) pick.Items.Add(new ComboBoxItem { Content = $"Display {d}", Tag = d });
            ModelViewer viewer = null;
            pick.SelectionChanged += (s, e) => { if (viewer != null && pick.SelectedItem is ComboBoxItem ci) viewer.Load(S.Models.Creature((long)ci.Tag)); };
            var card = Preview3D("3D preview", v =>
            {
                viewer = v;
                if (displays.Count == 0) { v.ShowMessage("This creature has no model yet (add one to the Models list above)."); return; }
                v.Load(S.Models.Creature(displays[0]));
            }, displays.Count > 1 ? "This creature has several models; each spawn picks one. Choose which to look at." : null, displays.Count > 1 ? pick : null);
            if (displays.Count > 1) pick.SelectedIndex = 0;
            return card;
        }

        /// <summary>Next free small index in a list (Idx, Index, ID…), from the rows on screen.</summary>
        static string NextIndex(RowListEditor ed, string col)
        {
            long max = -1;
            if (ed.Data != null)
                foreach (System.Data.DataRow r in ed.Data.Rows)
                    if (r.RowState != System.Data.DataRowState.Deleted && long.TryParse(Convert.ToString(r[col]), out var v) && v > max) max = v;
            return (max + 1).ToString(CultureInfo.InvariantCulture);
        }

        UIElement GoQuestItems() => ListEditor("goqitems", () =>
        {
            var ed = new RowListEditor(w, "Quest items inside (gameobject_questitem)", "Tells the client which quest items this object can contain, so it sparkles for players on the quest. The items themselves come from the loot table.",
                "gameobject_questitem", $"`GameObjectEntry` = {Id}", new Dictionary<string, string> { ["GameObjectEntry"] = Id }, "Idx",
                new[] { new RowListEditor.NameColumn { IdColumn = "ItemId", Kind = "item", Header = "item name" } }, refs.ByKind);
            ed.NewRow = r => r.Set("Idx", NextIndex(ed, "Idx"));
            return ed;
        });

        /// <summary>The filled-in NPC text variants as a player would read them, with their chances.</summary>
        UIElement NpcTextPreview()
        {
            var sp = new StackPanel();
            sp.Children.Add(U.Row(U.T("How it reads", 15, "Text", FontWeights.SemiBold, false), new Border { Width = 10 }, U.Pill("preview", "OkBg", "Ok")));
            sp.Children.Add(U.T("Each time the window opens, the server picks one filled-in variant by its chance. $N, $C and $R are shown as placeholders.", 12.5, "Muted", null, true, new Thickness(0, 4, 0, 10)));
            string Txt(string c) { var f = D.Fields.First(x => x.Column == c); if (!Exists(f)) return ""; var t = CurrentText(f); return t == "NULL" ? "" : t; }
            double Num(string c) => double.TryParse(Txt(c), NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : 0;
            string Fix(string t) => t.Replace("$B", "\n").Replace("$b", "\n").Replace("$N", "<name>").Replace("$n", "<name>").Replace("$C", "<class>").Replace("$c", "<class>").Replace("$R", "<race>").Replace("$r", "<race>");
            double total = 0; int shown = 0;
            for (int v = 0; v < 8; v++) if (Txt($"text{v}_0").Length > 0 || Txt($"text{v}_1").Length > 0) total += Num($"Probability{v}");
            for (int v = 0; v < 8; v++)
            {
                var male = Txt($"text{v}_0"); var female = Txt($"text{v}_1");
                if (male.Length == 0 && female.Length == 0) continue;
                shown++;
                var pr = Num($"Probability{v}");
                var chance = total > 0 ? (pr / total).ToString("P0", CultureInfo.InvariantCulture) + " of the time" : "chance 0";
                sp.Children.Add(U.T($"Variant {v + 1} · {chance}", 12, "Amber", FontWeights.SemiBold, false, new Thickness(0, 6, 0, 4)));
                var body = Fix(male.Length > 0 ? male : female) + (female.Length > 0 && male.Length > 0 && female != male ? "\n\n(Female characters read: " + Fix(female) + ")" : "");
                sp.Children.Add(new Border { Background = U.R("Panel2"), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10, 12, 10), Child = U.T(body, 13, "Text", null, true) });
            }
            if (shown == 0) sp.Children.Add(U.T("No variant has text yet.", 13, "Faint"));
            else if (total <= 0) sp.Children.Add(U.Callout("Every filled-in variant has chance 0, so none of them can be picked. Give at least one a chance above 0.", "warn"));
            return U.Card(sp, new Thickness(18, 14, 18, 14), new Thickness(0, 0, 0, 12));
        }

        /// <summary>This page and the pages after it, read as a book (follows NextPageID, read-only, stops at loops).</summary>
        UIElement BookPreview()
        {
            var sp = new StackPanel();
            sp.Children.Add(U.Row(U.T("The book from here", 15, "Text", FontWeights.SemiBold, false), new Border { Width = 10 }, U.Pill("read-only", "OkBg", "Ok")));
            sp.Children.Add(U.T("This page and every page after it, following Next page. Unstaged edits to this page show here too.", 12.5, "Muted", null, true, new Thickness(0, 4, 0, 10)));
            var seen = new HashSet<long>();
            long pageId = id.Value; int n = 0;
            var text = CurrentText(D.Fields.First(f => f.Column == "Text"));
            long next = CurrentLong(D.Fields.First(f => f.Column == "NextPageID"));
            try
            {
                using var db = S.OpenRead(S.Profile.WorldDb);
                while (true)
                {
                    seen.Add(pageId); n++;
                    sp.Children.Add(U.T($"Page {n} · page_text {pageId}", 12, "Amber", FontWeights.SemiBold, false, new Thickness(0, 6, 0, 4)));
                    sp.Children.Add(new Border { Background = U.R("Panel2"), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10, 12, 10), Child = U.T((text == "NULL" ? "" : text).Replace("$B", "\n").Replace("$b", "\n"), 13, "Text", null, true) });
                    if (next == 0) break;
                    if (seen.Contains(next)) { sp.Children.Add(U.Callout($"Page {next} was already shown: the chain loops back. Set the last page's Next page to 0.", "warn")); break; }
                    if (n >= 30) { sp.Children.Add(U.T("… stopped after 30 pages.", 12.5, "Faint")); break; }
                    var t = db.Query($"SELECT Text, NextPageID FROM page_text WHERE ID = {next.ToString(CultureInfo.InvariantCulture)}", 1);
                    if (t.Rows.Count == 0) { sp.Children.Add(U.Callout($"Next page {next} doesn't exist, so the book ends early.", "warn")); break; }
                    pageId = next; text = Convert.ToString(t.Rows[0][0]); next = Convert.ToInt64(t.Rows[0][1], CultureInfo.InvariantCulture);
                }
            }
            catch (Exception ex) { sp.Children.Add(U.T("Couldn't read the next pages: " + ex.Message, 12.5, "Faint")); }
            return U.Card(sp, new Thickness(18, 14, 18, 14), new Thickness(0, 0, 0, 12));
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
