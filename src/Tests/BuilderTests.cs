using System;
using System.Collections.Generic;
using System.Linq;
using AndersCraftStudio.Core;
using AndersCraftStudio.Core.Build;

namespace AndersCraftStudio.Tests
{
    /// <summary>The step-by-step builders: quests, NPCs and behaviours, end to end on in-memory rows (no database).</summary>
    static class BuilderTests
    {
        static BRow Row(string table, string[] keys, params (string c, object v)[] cols) { var r = new BRow(table, keys); foreach (var (c, v) in cols) r.Put(c, v); return r; }

        static BRow QuestTemplateRow(long id)
        {
            var r = new BRow("quest_template", "ID");
            foreach (var c in new[] { "ID", "QuestType", "QuestLevel", "MinLevel", "QuestSortID", "QuestInfoID", "SuggestedGroupNum", "RewardNextQuest", "RewardXPDifficulty", "RewardMoney", "RewardSpell", "Flags", "AllowableRaces" }) r.Put(c, 0L);
            for (int i = 1; i <= 4; i++) { r.Put("RewardItem" + i, 0L); r.Put("RewardAmount" + i, 0L); r.Put("RequiredNpcOrGo" + i, 0L); r.Put("RequiredNpcOrGoCount" + i, 0L); r.Put("ObjectiveText" + i, ""); }
            for (int i = 1; i <= 6; i++) { r.Put("RewardChoiceItemID" + i, 0L); r.Put("RewardChoiceItemQuantity" + i, 0L); r.Put("RequiredItemId" + i, 0L); r.Put("RequiredItemCount" + i, 0L); }
            for (int i = 1; i <= 5; i++) { r.Put("RewardFactionID" + i, 0L); r.Put("RewardFactionValue" + i, 0L); r.Put("RewardFactionOverride" + i, 0L); }
            foreach (var c in new[] { "LogTitle", "LogDescription", "QuestDescription", "QuestCompletionLog" }) r.Put(c, "");
            r["ID"] = id;
            return r;
        }

        static QuestBuilder.QuestRows BlankQuest() => new QuestBuilder.QuestRows
        {
            Template = QuestTemplateRow(0),
            Addon = Row("quest_template_addon", new[] { "ID" }, ("ID", 0L), ("MaxLevel", 0L), ("AllowableClasses", 0L), ("PrevQuestID", 0L), ("SpecialFlags", 0L)),
            Request = Row("quest_request_items", new[] { "ID" }, ("ID", 0L), ("EmoteOnComplete", 0L), ("EmoteOnIncomplete", 0L), ("CompletionText", "")),
            Offer = Row("quest_offer_reward", new[] { "ID" }, ("ID", 0L), ("RewardText", "")),
        };

        static string After(Change c, string col) => c.Fields.FirstOrDefault(f => f.Name == col)?.After;

        [Test] static void New_quest_has_every_row_it_needs()
        {
            var d = new QuestDraft
            {
                Id = 900100, Title = "The Kobold Menace", Level = 7, MinLevel = 5, Giver = 197, Ender = 0, Repeat = "Daily", Money = 35000, XpSize = 5,
                Objectives =
                {
                    new Objective { Kind = "kill", Target = 6, Count = 10 },
                    new Objective { Kind = "collect", Target = 772, Count = 6, DropFrom = 6, DropChance = 45 },
                    new Objective { Kind = "use", Target = 1561, Count = 1 },
                    new Objective { Kind = "explore", Target = 88 },
                },
                Choices = { (2385, 1), (2386, 1) }, Items = { (159, 5) }, Reputation = { (72, 250) }, Previous = 783, Classes = 128 | 16,
            };
            A.Eq(0, QuestBuilder.Check(d).Count, string.Join(" | ", QuestBuilder.Check(d)));
            var notes = new List<string>();
            var rows = QuestBuilder.Apply(d, BlankQuest(), null, new Changeset(), 900000, notes);
            var cs = new Changeset();
            var ch = QuestBuilder.Stage(StartMode.New, null, rows, cs);
            var tpl = ch.Single(c => c.Table == "quest_template");
            A.Eq(ChangeKind.Insert, tpl.Kind);
            A.Eq("'The Kobold Menace'", After(tpl, "LogTitle"));
            A.Eq("6", After(tpl, "RequiredNpcOrGo1")); A.Eq("10", After(tpl, "RequiredNpcOrGoCount1"));
            A.Eq("-1561", After(tpl, "RequiredNpcOrGo2"), "objects are negative");
            A.Eq("772", After(tpl, "RequiredItemId1")); A.Eq("6", After(tpl, "RequiredItemCount1"));
            A.Eq("2385", After(tpl, "RewardChoiceItemID1")); A.Eq("159", After(tpl, "RewardItem1")); A.Eq("5", After(tpl, "RewardAmount1"));
            A.Eq("72", After(tpl, "RewardFactionID1")); A.Eq("25000", After(tpl, "RewardFactionOverride1"), "reputation ×100");
            A.Eq("4096", After(tpl, "Flags"), "daily"); A.Eq("35000", After(tpl, "RewardMoney"));
            var addon = ch.Single(c => c.Table == "quest_template_addon");
            A.Eq("783", After(addon, "PrevQuestID")); A.Eq("144", After(addon, "AllowableClasses")); A.Eq("3", After(addon, "SpecialFlags"), "repeatable + explore");
            A.True(ch.Any(c => c.Table == "creature_queststarter" && After(c, "id") == "197" && After(c, "quest") == "900100"), "giver");
            A.True(ch.Any(c => c.Table == "creature_questender" && After(c, "id") == "197"), "same one takes it back");
            A.True(ch.Any(c => c.Table == "areatrigger_involvedrelation" && After(c, "id") == "88"), "explore");
            var drop = ch.Single(c => c.Table == "creature_loot_template");
            A.Eq("772", After(drop, "Item")); A.Eq("1", After(drop, "QuestRequired"), "quest-only drop");
            A.Contains(After(tpl, "LogDescription"), "Kill 10");
            // and it reads back as the same quest
            var back = QuestBuilder.ToDraft(rows);
            A.Eq("The Kobold Menace", back.Title); A.Eq("Daily", back.Repeat); A.Eq(197L, back.Giver); A.Eq(2, back.Choices.Count); A.Eq(4, back.Objectives.Count);
            A.Eq(250, back.Reputation[0].amount);
        }

        [Test] static void Editing_a_quest_stages_only_what_changed()
        {
            var original = BlankQuest();
            original.Template["ID"] = 7L; original.Template["LogTitle"] = "Kobold Camp Cleanup"; original.Template["QuestLevel"] = 2L; original.Template["MinLevel"] = 1L;
            original.Template["RequiredNpcOrGo1"] = 6L; original.Template["RequiredNpcOrGoCount1"] = 10L; original.Template["QuestDescription"] = "Kill kobolds."; original.Template["LogDescription"] = "Kill 10 Kobold Vermin.";
            foreach (var r in new[] { original.Addon, original.Request, original.Offer }) r["ID"] = 7L;
            original.Request["CompletionText"] = "Done?"; original.Offer["RewardText"] = "Thanks.";
            original.Starters.Add(Row("creature_queststarter", new[] { "id", "quest" }, ("id", 197L), ("quest", 7L)));
            original.Enders.Add(Row("creature_questender", new[] { "id", "quest" }, ("id", 197L), ("quest", 7L)));
            var d = QuestBuilder.ToDraft(original);
            A.Eq("Kill 10 Kobold Vermin.", d.Summary);
            d.Title = "Kobold Camp Cleanup (revised)"; d.Ender = 240;
            var rows = QuestBuilder.Apply(d, original, null, new Changeset(), 900000, null);
            var ch = QuestBuilder.Stage(StartMode.Edit, original, rows, new Changeset());
            var tpl = ch.Single(c => c.Table == "quest_template");
            A.Eq(ChangeKind.Update, tpl.Kind);
            A.Eq("LogTitle", string.Join(",", tpl.Fields.Select(f => f.Name)), "only the title changed");
            A.True(ch.Any(c => c.Table == "creature_questender" && c.Kind == ChangeKind.Delete), "old turn-in removed");
            A.True(ch.Any(c => c.Table == "creature_questender" && c.Kind == ChangeKind.Insert && After(c, "id") == "240"), "new turn-in added");
            A.False(ch.Any(c => c.Table == "creature_queststarter"), "giver unchanged");
            A.Eq(3, ch.Count);
            // copy as new: everything is new, under the new ID, the original untouched
            d.Id = 900200;
            var copy = original.Clone(); copy.Starters.Clear(); copy.Enders.Clear();
            var ch2 = QuestBuilder.Stage(StartMode.CopyAsNew, original, QuestBuilder.Apply(d, copy, null, new Changeset(), 900000, null), new Changeset());
            A.True(ch2.All(c => c.Kind == ChangeKind.Insert), "all new");
            A.Eq("900200", After(ch2.First(c => c.Table == "quest_template"), "ID"));
        }

        /// <summary>Answers the few reads the quest builder makes, from fixed rows.</summary>
        sealed class Reader : ISqlReader
        {
            public IList<string> PrimaryKey(string table) => new List<string>();
            public ResultTable Query(string sql, int max)
            {
                var t = new ResultTable();
                if (sql.Contains("INFORMATION_SCHEMA"))
                {
                    t.Columns.AddRange(new[] { "COLUMN_NAME", "COLUMN_DEFAULT", "IS_NULLABLE", "DATA_TYPE" });
                    foreach (var c in new[] { "Entry", "Item", "Reference", "Chance", "QuestRequired", "LootMode", "GroupId", "MinCount", "MaxCount" }) t.Rows.Add(new object[] { c, "0", "NO", c == "Chance" ? "float" : "int" });
                    t.Rows.Add(new object[] { "Comment", null, "YES", "varchar" });
                    return t;
                }
                if (sql.Contains("FROM `creature_template`") && sql.Contains("SELECT *"))
                {
                    t.Columns.AddRange(new[] { "entry", "name", "lootid", "npcflag", "AIName", "ScriptName" });
                    t.Rows.Add(new object[] { 6L, "Kobold Vermin", 6L, 0L, "", "" });
                    return t;
                }
                if (sql.Contains("SELECT `name` FROM `creature_template`")) { t.Columns.Add("name"); t.Rows.Add(new object[] { "Kobold Vermin" }); return t; }
                t.Columns.Add("x");
                return t;   // nothing else exists
            }
        }

        [Test] static void A_quest_never_removes_or_changes_creatures_it_only_reads()
        {
            var d = new QuestDraft { Id = 900400, Title = "Candles", Giver = 0, Objectives = { new Objective { Kind = "collect", Target = 772, Count = 4, DropFrom = 6, DropChance = 40 } } };
            var rows = QuestBuilder.Apply(d, BlankQuest(), new Reader(), new Changeset(), 900000, new List<string>());
            var ch = QuestBuilder.Stage(StartMode.New, null, rows, new Changeset());
            A.False(ch.Any(c => c.Table == "creature_template"), "the creature already had a drops list: left alone, never removed");
            A.Eq("6", After(ch.Single(c => c.Table == "creature_loot_template"), "Entry"), "drop added to its list");
            // the draft isn't changed by preparing it (a new quest item gets its number each time)
            var d2 = new QuestDraft { Id = 900401, Title = "x", Objectives = { new Objective { Kind = "collect", NewItemName = "Candle", Count = 1 } } };
            try { QuestBuilder.Apply(d2, BlankQuest(), new Reader(), new Changeset(), 900000, null); } catch (InvalidOperationException) { }
            A.Eq(0L, d2.Objectives[0].Target, "draft untouched");
        }

        [Test] static void Quest_check_explains_problems()
        {
            var d = new QuestDraft { Title = "", Giver = 0, MinLevel = 20, Level = 10 };
            d.Objectives.Add(new Objective { Kind = "kill", Target = 0 });
            var p = QuestBuilder.Check(d);
            A.True(p.Any(x => x.Contains("name")) && p.Any(x => x.Contains("gives")) && p.Any(x => x.Contains("minimum level")) && p.Any(x => x.Contains("target")), string.Join(" | ", p));
            A.Contains(QuestBuilder.AutoSummary(new QuestDraft { Giver = 5, Objectives = { new Objective { Kind = "collect", NewItemName = "Kobold Candle", Count = 6 } } }, null), "Bring 6 Kobold Candle");
        }

        static BRow CreatureTemplate()
        {
            var r = new BRow("creature_template", "entry");
            foreach (var c in new[] { "entry", "minlevel", "maxlevel", "faction", "npcflag", "rank", "unit_class", "type", "BaseAttackTime", "RangeAttackTime", "exp", "RegenHealth", "gossip_menu_id" }) r.Put(c, 0L);
            foreach (var c in new[] { "HealthModifier", "DamageModifier", "ManaModifier", "ArmorModifier", "ExperienceModifier", "speed_walk", "speed_run", "detection_range" }) r.Put(c, 0.0);
            r.Put("name", ""); r.Put("subname", ""); r.Put("AIName", ""); r.Put("ScriptName", "");
            return r;
        }

        [Test] static void New_npc_with_look_services_place_and_greeting()
        {
            var gps = "Map: 0 (Eastern Kingdoms) Zone: 1519 (Stormwind City) Area: 5150\nX: -8823.53 Y: 640.21 Z: 94.31 Orientation: 3.14";
            var s = Spawn.Parse(gps);
            A.True(s != null && s.Map == 0 && Math.Abs(s.X + 8823.53) < 0.01 && Math.Abs(s.O - 3.14) < 0.01, "parsed .gps");
            A.Eq(530, Spawn.Parse("530 -1000.5 200 12.25").Map, "plain map x y z");
            A.Eq(null, Spawn.Parse("nothing here"));
            var d = new NpcDraft { Entry = 900300, Name = "Brannock Ironsong", Title = "Weapon Merchant", Kind = "Vendor", Side = "Alliance", Strength = "Elite", MinLevel = 30, MaxLevel = 32,
                Display = 3258, Scale = 1.1, Services = new HashSet<string> { "Talk", "Vendor", "Repairs" }, Sells = { 2488, 2489 }, Spawns = { s }, Wander = 0, RespawnSeconds = 120, Greeting = "Steel for the brave, $N.", Weapon1 = 1899 };
            A.Eq(0, NpcBuilder.Check(d).Count, string.Join(" | ", NpcBuilder.Check(d)));
            BRow Blank(string t) => t != "creature" ? null : Row("creature", new[] { "guid" }, ("guid", 0L), ("id1", 0L), ("map", 0L), ("spawnMask", 0L), ("phaseMask", 0L), ("equipment_id", 0L), ("position_x", 0.0), ("position_y", 0.0), ("position_z", 0.0), ("orientation", 0.0), ("spawntimesecs", 0L), ("wander_distance", 0.0), ("MovementType", 0L), ("curhealth", 0L));
            var rows = NpcBuilder.Apply(d, new NpcBuilder.NpcRows { Template = CreatureTemplate() }, null, new Changeset(), new List<string>(), Blank);
            var ch = NpcBuilder.Stage(StartMode.New, null, rows, new Changeset());
            var t = ch.Single(c => c.Table == "creature_template");
            A.Eq("'Brannock Ironsong'", After(t, "name")); A.Eq("11", After(t, "faction"), "Alliance");
            A.Eq((1 + 128 + 4096).ToString(), After(t, "npcflag"), "services");
            A.Eq("1", After(t, "rank"), "elite"); A.Eq("3", After(t, "HealthModifier"));
            A.Eq("90000", After(t, "gossip_menu_id"), "its chat window");
            A.Eq("3258", After(ch.Single(c => c.Table == "creature_template_model"), "CreatureDisplayID"));
            A.Eq("1899", After(ch.Single(c => c.Table == "creature_equip_template"), "ItemID1"));
            var spawn = ch.Single(c => c.Table == "creature");
            A.Eq("900300", After(spawn, "id1")); A.Eq("120", After(spawn, "spawntimesecs")); A.Eq("1", After(spawn, "equipment_id"));
            A.Eq(2, ch.Count(c => c.Table == "npc_vendor")); A.Eq("1", After(ch.Where(c => c.Table == "npc_vendor").Last(), "slot"));
            A.Eq("'Steel for the brave, $N.'", After(ch.Single(c => c.Table == "npc_text"), "text0_0"));
            A.True(ch.Any(c => c.Table == "gossip_menu"), "gossip menu");
            // read back
            var back = NpcBuilder.ToDraft(rows);
            A.Eq("Vendor", back.Kind); A.Eq("Alliance", back.Side); A.Eq("Elite", back.Strength); A.Eq(2, back.Sells.Count); A.Eq(1, back.Spawns.Count);
            // editing only the name stages one change
            var d2 = NpcBuilder.ToDraft(rows); d2.Name = "Brannock the Smith";
            var ch2 = NpcBuilder.Stage(StartMode.Edit, rows, NpcBuilder.Apply(d2, rows, null, new Changeset(), null, _ => null), new Changeset());
            A.Eq(1, ch2.Count, string.Join(" | ", ch2.Select(c => c.Table + ":" + string.Join(",", c.Fields.Select(f => f.Name)))));
            A.Eq("name", ch2[0].Fields.Single().Name);
            A.True(NpcBuilder.Check(new NpcDraft { Name = "", Display = 0, MinLevel = 5, MaxLevel = 2 }).Count >= 3, "problems");
        }

        [Test] static void Behaviours_make_valid_scripts_in_plain_words()
        {
            foreach (var b in Behaviours.All)
            {
                var v = b.Inputs.ToDictionary(i => i.key, i => i.kind is "spell" or "creature" ? "133" : i.def ?? "");
                A.Eq(0, Behaviours.Check(b.Key, v).Count, b.Key + ": " + string.Join(" | ", Behaviours.Check(b.Key, v)));
                var r = Behaviours.Make(b.Key, v, 68, "Stormwind City Guard", 5, 2);
                A.True(r.Scripts.Count > 0, b.Key + " has rows");
                foreach (var s in r.Scripts)
                {
                    A.True(SmartAI.Event(s.EventType) != null && SmartAI.Action(s.ActionType) != null && SmartAI.Target(s.TargetType) != null, b.Key + " uses known kinds");
                    A.Eq(68L, s.EntryOrGuid); A.True(s.Id >= 5, "after existing rows");
                    var sentence = SmartAI.Sentence(s, (k, id) => null);
                    A.True(sentence.Length > 10, b.Key + " reads as a sentence");
                    var row = Smart.ToRow(s);
                    A.Eq((long)s.ActionType, row.L("action_type"));
                }
            }
            var say = Behaviours.Make("say_aggro", new Dictionary<string, string> { ["text"] = "You'll regret this!", ["how"] = "Yell" }, 68, "Guard", 0, 3);
            A.Eq(3L, say.Scripts[0].ActionParams[0], "says its own line group");
            A.Eq(3L, say.Texts[0].L("GroupID")); A.Eq(14L, say.Texts[0].L("Type"), "yell");
            A.True(Behaviours.Check("cast_health", new Dictionary<string, string> { ["pct"] = "150", ["spell"] = "0" }).Count == 2, "bad inputs explained");
        }

        [Test] static void Plain_names_for_tables_and_columns()
        {
            A.Eq("Item to bring 3", PlainNames.Column("quest_template", "RequiredItemId3"));
            A.Eq("Only for players on the quest", PlainNames.Column("creature_loot_template", "QuestRequired"));
            A.Eq("Respawn time (seconds)", PlainNames.Column("creature", "spawntimesecs"));
            A.Eq("When: setting 2", PlainNames.Column("smart_scripts", "event_param2"));
            A.Eq("Required skill points", PlainNames.Column("x", "RequiredSkillPoints"), "unknown: made readable");
            A.Eq("What a vendor sells", PlainNames.Table("npc_vendor"));
            A.Eq("some_table", PlainNames.Table("some_table"));
        }
    }
}
