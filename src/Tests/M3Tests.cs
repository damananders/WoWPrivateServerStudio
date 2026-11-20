using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AndersCraftStudio.Core;

namespace AndersCraftStudio.Tests
{
    static class M3Tests
    {
        static readonly string[] VendorCols = { "entry", "slot", "item", "maxcount", "incrtime", "ExtendedCost" };
        static readonly string[] VendorKey = { "entry", "item", "ExtendedCost" };
        static RowValues V(int item, int slot = 0, int max = 0) => new RowValues().Set("entry", "66").Set("slot", slot.ToString()).Set("item", item.ToString()).Set("maxcount", max.ToString()).Set("incrtime", "0").Set("ExtendedCost", "0");

        [Test] static void RowDiffStagesInsertUpdateDelete()
        {
            var cs = new Changeset();
            var before = new List<RowValues> { V(159), V(4540, 1), V(117, 2) };
            var after = new List<RowValues> { V(159), V(4540, 1, 5), V(2287, 3) };   // 4540 limited to 5, 117 removed, 2287 added
            int n = RowDiff.Stage(cs, "npc_vendor", VendorKey, VendorCols, before, after);
            A.Eq(3, n);
            var del = cs.Changes.Single(c => c.Kind == ChangeKind.Delete);
            A.Eq("DELETE FROM `npc_vendor` WHERE `entry` = 66 AND `item` = 117 AND `ExtendedCost` = 0 LIMIT 1;", del.ForwardSql);
            A.Eq("INSERT INTO `npc_vendor` (`entry`, `slot`, `item`, `maxcount`, `incrtime`, `ExtendedCost`) VALUES (66, 2, 117, 0, 0, 0);", del.UndoSql);
            var ins = cs.Changes.Single(c => c.Kind == ChangeKind.Insert);
            A.Eq("INSERT INTO `npc_vendor` (`entry`, `slot`, `item`, `maxcount`, `incrtime`, `ExtendedCost`) VALUES (66, 3, 2287, 0, 0, 0);", ins.ForwardSql);
            A.Eq("DELETE FROM `npc_vendor` WHERE `entry` = 66 AND `item` = 2287 AND `ExtendedCost` = 0 LIMIT 1;", ins.UndoSql);
            var up = cs.Changes.Single(c => c.Kind == ChangeKind.Update);
            A.Eq("UPDATE `npc_vendor` SET `maxcount` = 5 WHERE `entry` = 66 AND `item` = 4540 AND `ExtendedCost` = 0 LIMIT 1;", up.ForwardSql);
        }

        [Test] static void RowDiffKeyChangeIsDeletePlusInsert()
        {
            var cs = new Changeset();
            RowDiff.Stage(cs, "npc_vendor", VendorKey, VendorCols, new List<RowValues> { V(159) }, new List<RowValues> { V(1179) });
            A.Eq(1, cs.Changes.Count(c => c.Kind == ChangeKind.Delete)); A.Eq(1, cs.Changes.Count(c => c.Kind == ChangeKind.Insert));
        }

        [Test] static void RowDiffWorksOnTopOfStagedChanges()
        {
            var cs = new Changeset();
            var db = new List<RowValues> { V(159), V(4540, 1) };
            RowDiff.Stage(cs, "npc_vendor", VendorKey, VendorCols, db, new List<RowValues> { V(159, 0, 3), V(4540, 1), V(2287, 2) });
            // reopen: the overlay shows the staged state
            var shown = RowDiff.Overlay(cs, "npc_vendor", VendorKey, db, r => r.Lit("entry") == "66");
            A.Eq(3, shown.Count, "staged insert visible");
            A.Eq("3", shown.First(r => r.Lit("item") == "159").Lit("maxcount"), "staged edit visible");
            // edit again: change the staged insert, put 159 back, remove 4540
            var again = shown.Select(r => r.Clone()).ToList();
            again.First(r => r.Lit("item") == "2287").Set("maxcount", "9");
            again.First(r => r.Lit("item") == "159").Set("maxcount", "0");
            again.RemoveAll(r => r.Lit("item") == "4540");
            RowDiff.Stage(cs, "npc_vendor", VendorKey, VendorCols, shown, again);
            A.Eq(0, cs.Changes.Count(c => c.Kind == ChangeKind.Update), "159 back to original: edit dropped");
            A.Contains(cs.Changes.Single(c => c.Kind == ChangeKind.Insert).ForwardSql, "(66, 2, 2287, 9, 0, 0)");
            A.Eq(1, cs.Changes.Count(c => c.Kind == ChangeKind.Delete));
            // removing the staged insert just forgets it
            var third = RowDiff.Overlay(cs, "npc_vendor", VendorKey, db, r => true);
            RowDiff.Stage(cs, "npc_vendor", VendorKey, VendorCols, third, third.Where(r => r.Lit("item") != "2287").ToList());
            A.Eq(0, cs.Changes.Count(c => c.Kind == ChangeKind.Insert), "insert forgotten");
        }

        [Test] static void RowDiffDeleteUndoRestoresDatabaseValues()
        {
            var cs = new Changeset();
            var db = new List<RowValues> { V(159, 0, 0) };
            RowDiff.Stage(cs, "npc_vendor", VendorKey, VendorCols, db, new List<RowValues> { V(159, 0, 7) });   // staged edit first
            var shown = RowDiff.Overlay(cs, "npc_vendor", VendorKey, db, r => true);
            RowDiff.Stage(cs, "npc_vendor", VendorKey, VendorCols, shown, new List<RowValues>());                 // then remove the row
            var del = cs.Changes.Single();
            A.Eq(ChangeKind.Delete, del.Kind);
            A.Contains(del.UndoSql, "(66, 0, 159, 0, 0, 0)");                                                      // maxcount 0, not the staged 7
            A.Throws(() => RowDiff.Stage(cs, "npc_vendor", VendorKey, VendorCols, db, new List<RowValues> { V(1), V(1) }), "duplicate keys rejected");
        }

        static Func<string, int, List<LootRow>> Tables(Dictionary<(string, int), List<LootRow>> t) => (tb, e) => t.TryGetValue((tb, e), out var r) ? r : new List<LootRow>();

        [Test] static void SimulatorIndependentRows()
        {
            var t = new Dictionary<(string, int), List<LootRow>> { [("creature_loot_template", 1)] = new List<LootRow> { new LootRow { Item = 10, Chance = 50 }, new LootRow { Item = 11, Chance = 100, MinCount = 2, MaxCount = 4 } } };
            var res = new LootSim(Tables(t)).Run("creature_loot_template", 1, 40000);
            var a = res.Single(s => s.Item == 10); var b = res.Single(s => s.Item == 11);
            A.True(Math.Abs(a.DropPct - 50) < 1.5, "50% row: " + a.DropPct);
            A.True(Math.Abs(b.DropPct - 100) < 0.001, "100% row always");
            A.True(Math.Abs(b.AvgWhenDropped - 3) < 0.05, "2-4 copies average 3: " + b.AvgWhenDropped);
        }

        [Test] static void SimulatorGroupsDropAtMostOne()
        {
            var t = new Dictionary<(string, int), List<LootRow>>
            {
                [("creature_loot_template", 1)] = new List<LootRow>
                {
                    new LootRow { Item = 1, GroupId = 1, Chance = 30 }, new LootRow { Item = 2, GroupId = 1, Chance = 20 },
                    new LootRow { Item = 3, GroupId = 2 }, new LootRow { Item = 4, GroupId = 2 }, new LootRow { Item = 5, GroupId = 2 },
                },
            };
            var sim = new LootSim(Tables(t));
            var res = sim.Run("creature_loot_template", 1, 60000);
            double P(int i) => res.FirstOrDefault(s => s.Item == i)?.DropPct ?? 0;
            A.True(Math.Abs(P(1) - 30) < 1.2 && Math.Abs(P(2) - 20) < 1.2, $"explicit chances {P(1)} / {P(2)}");
            A.True(Math.Abs(P(3) + P(4) + P(5) - 100) < 0.001, "equal-chance group always drops exactly one");
            A.True(Math.Abs(P(3) - 33.3) < 1.5, "shared evenly: " + P(3));
            A.Contains(LootSim.DescribeGroup(t[("creature_loot_template", 1)].Where(r => r.GroupId == 1).ToList()), "50% of kills get nothing");
            A.Contains(LootSim.DescribeGroup(t[("creature_loot_template", 1)].Where(r => r.GroupId == 2).ToList()), "something always drops");
        }

        [Test] static void SimulatorFollowsReferencesAndQuestRule()
        {
            var t = new Dictionary<(string, int), List<LootRow>>
            {
                [("creature_loot_template", 1)] = new List<LootRow> { new LootRow { Item = 0, Reference = 500, Chance = 100, MaxCount = 2 }, new LootRow { Item = 77, Chance = 100, QuestRequired = true } },
                [("reference_loot_template", 500)] = new List<LootRow> { new LootRow { Item = 9, Chance = 100 } },
            };
            var sim = new LootSim(Tables(t));
            var res = sim.Run("creature_loot_template", 1, 100);
            A.Eq(2.0, res.Single(s => s.Item == 9).PerKill, "reference processed MaxCount times");
            A.True(res.Any(s => s.Item == 77), "quest item when on the quest");
            var noQuest = new LootSim(Tables(t)) { HasQuests = false }.Run("creature_loot_template", 1, 100);
            A.False(noQuest.Any(s => s.Item == 77), "quest item hidden without the quest");
            t[("reference_loot_template", 500)].Add(new LootRow { Item = 0, Reference = 500, Chance = 100, MaxCount = 1 });
            var loop = new LootSim(Tables(t));
            loop.Run("creature_loot_template", 1, 3);
            A.True(loop.Warnings.Count > 0, "reference loop detected");
        }

        static SmartRow Row(int id, int ev, int act, int tgt, int link = 0) => new SmartRow { EntryOrGuid = 299, Id = id, EventType = ev, ActionType = act, TargetType = tgt, Link = link };

        [Test] static void SmartSentences()
        {
            var r = Row(0, 0, 11, 2);
            r.EventParams[0] = 0; r.EventParams[1] = 2000; r.EventParams[2] = 8000; r.EventParams[3] = 12000; r.ActionParams[0] = 133;
            string Resolve(SP k, long id) => k == SP.Spell && id == 133 ? "Fireball" : null;
            A.Eq("When in combat, cast Fireball (133) on its victim, every 8-12s (first after 0-2s).", SmartAI.Sentence(r, Resolve));
            var talk = Row(1, 4, 1, 1); talk.EventChance = 50;
            A.Eq("When it enters combat, say line group 0 (50% chance).", SmartAI.Sentence(talk, Resolve));
            var linked = Row(2, 61, 37, 1); linked.EventPhaseMask = 0b101;
            A.Eq("Then, die (phase 1, 3).", SmartAI.Sentence(linked, Resolve));
            A.Eq("8-12s", SmartAI.MsRange(8000, 12000)); A.Eq("1.5s", SmartAI.MsRange(1500, 1500)); A.Eq("at once", SmartAI.MsRange(0, 0));
        }

        [Test] static void SmartCatalogCoversSource()
        {
            var h = Path.Combine(DevPaths.AzerothCore, "src", "server", "game", "AI", "SmartScripts", "SmartScriptMgr.h");
            if (!File.Exists(h)) return;
            var src = File.ReadAllText(h);
            int Check(string prefix, Func<int, SType> find, string skip)
            {
                int missing = 0;
                foreach (Match m in Regex.Matches(src, @"^\s*" + prefix + @"(\w+)\s*=\s*(\d+)", RegexOptions.Multiline))
                {
                    var name = m.Groups[1].Value;
                    if (Regex.IsMatch(name, skip)) continue;
                    if (find(int.Parse(m.Groups[2].Value)) == null) { missing++; Console.WriteLine("        no entry for " + prefix + name); }
                }
                return missing;
            }
            A.Eq(0, Check("SMART_EVENT_", SmartAI.Event, "^(TC_END|AC_START|AC_END|PHASE|FLAG)"), "every event documented");
            A.Eq(0, Check("SMART_ACTION_", SmartAI.Action, "^(TC_END|AC_START|AC_END)$"), "every action documented");
            A.Eq(0, Check("SMART_TARGET_", SmartAI.Target, "^(TC_END|AC_START|AC_END)$"), "every target documented");
            foreach (var t in SmartAI.Events.Concat(SmartAI.Actions).Concat(SmartAI.Targets))
                A.True(!string.IsNullOrWhiteSpace(t.Name) && !string.IsNullOrWhiteSpace(t.Desc) && t.P.All(p => !string.IsNullOrWhiteSpace(p.Label)), $"{t.Id} {t.Name} complete");
        }

        [Test] static void SmartValidation()
        {
            var rows = new List<SmartRow> { Row(0, 4, 11, 2, link: 1), Row(1, 61, 1, 1), Row(2, 61, 1, 1), Row(3, 0, 11, 2, link: 9), Row(4, 0, 999, 2) };
            rows[0].ActionParams[0] = 133; rows[3].ActionParams[0] = 424242; rows[4].EventChance = 0;
            rows[3].EventParams[2] = 9000; rows[3].EventParams[3] = 5000;
            var issues = SmartAI.Validate(rows, (k, id) => k == SP.Spell ? id == 133 : (bool?)null);
            string All() => string.Join("\n", issues.Select(i => i.Text));
            A.Contains(All(), "Row 2 is a \"Linked\" row, but no row links to it");
            A.Contains(All(), "links to row 9, which doesn't exist");
            A.Contains(All(), "424242 doesn't exist");
            A.Contains(All(), "action type 999 doesn't exist");
            A.Contains(All(), "chance is 0%");
            A.Contains(All(), "repeat minimum is larger");
            A.False(issues.Any(i => i.RowIndex == 0 || i.RowIndex == 1), "rows 0 and 1 are fine");
        }

        [Test] static void SmartRowRoundTripsToSql()
        {
            var r = Row(5, 0, 11, 2); r.ActionParams[0] = 133; r.Comment = "Kobold's fireball"; r.X = 1.5f;
            var v = r.ToValues();
            A.Eq(SmartRow.Columns.Length, v.Cells.Count, "all columns");
            A.Eq("'Kobold\\'s fireball'", v.Lit("comment"));
            var cs = new Changeset();
            RowDiff.Stage(cs, "smart_scripts", SmartRow.KeyColumns, SmartRow.Columns, new List<RowValues>(), new List<RowValues> { v });
            A.Contains(cs.Changes[0].ForwardSql, "INSERT INTO `smart_scripts`");
            A.Contains(cs.Changes[0].UndoSql, "WHERE `entryorguid` = 299 AND `source_type` = 0 AND `id` = 5 AND `link` = 0");
            var back = SmartRow.FromValues(c => v.Disp(c));
            A.Eq(133L, back.ActionParams[0]); A.Eq("Kobold's fireball", back.Comment); A.Eq(1.5f, back.X);
        }
    }
}
