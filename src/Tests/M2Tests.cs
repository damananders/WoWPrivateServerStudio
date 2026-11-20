using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AndersCraftStudio.Core;

namespace AndersCraftStudio.Tests
{
    static class M2Tests
    {
        static List<KeyPart> K(long id, string col = "entry") => new List<KeyPart> { new KeyPart(col, id.ToString()) };

        [Test] static void FlagMaskKeepsUnknownBits()
        {
            ulong v = 0x80 | 0x40000000; // vendor + an unnamed bit
            v = FlagMask.Set(v, 0x2, true);
            A.Eq(0x40000082UL, v);
            v = FlagMask.Set(v, 0x80, false);
            A.Eq(0x40000002UL, v);
            A.True(FlagMask.Has(v, 0x2), "has quest giver");
            A.Eq(0x40000000UL, FlagMask.Unknown(v, EntityDefs.NpcFlags.Keys));
            A.Contains(FlagMask.Describe(v, EntityDefs.NpcFlags), "other bits 0x40000000");
            A.Eq("none", FlagMask.Describe(0, EntityDefs.NpcFlags));
        }

        [Test] static void EditSessionPreviewGroupsByRow()
        {
            var s = new EditSession();
            s.Set("creature_template", K(69), "minlevel", "2", "2", "5", "5");
            s.Set("creature_template", K(69), "name", "'Wolf'", "Wolf", "'Big Wolf'", "Big Wolf");
            s.Set("quest_template_addon", K(7, "ID"), "MaxLevel", "0", "0", "20", "20");
            var p = s.Preview();
            A.Eq(2, p.Count, "two rows");
            A.Eq("UPDATE `creature_template` SET `minlevel` = 5, `name` = 'Big Wolf' WHERE `entry` = 69 LIMIT 1;", p[0]);
            A.Eq("UPDATE `quest_template_addon` SET `MaxLevel` = 20 WHERE `ID` = 7 LIMIT 1;", p[1]);
            A.Eq("UPDATE `creature_template` SET `minlevel` = 2, `name` = 'Wolf' WHERE `entry` = 69 LIMIT 1;", s.PreviewUndo()[0]);
        }

        [Test] static void EditSessionBackToOriginalDropsEdit()
        {
            var s = new EditSession();
            s.Set("item_template", K(25), "Quality", "1", "1", "4", "4");
            s.Set("item_template", K(25), "Quality", "1", "1", "1", "1");
            A.Eq(0, s.Count, "cleared");
        }

        [Test] static void EditSessionStagesWithUndoAndMerges()
        {
            var cs = new Changeset();
            ChangeFactory.StageCellEdit(cs, "creature_template", K(69), "minlevel", "2", "2", "4", "4"); // staged earlier
            var s = new EditSession();
            s.Set("creature_template", K(69), "minlevel", "2", "2", "6", "6");
            s.Set("creature_template", K(69), "faction", "38", "38", "14", "14");
            var touched = s.Stage(cs);
            A.Eq(1, cs.Changes.Count, "merged into one row change");
            A.Eq(1, touched.Count, "one change touched");
            A.Eq(0, s.Count, "session emptied");
            var c = cs.Changes[0];
            A.Eq("UPDATE `creature_template` SET `minlevel` = 6, `faction` = 14 WHERE `entry` = 69 LIMIT 1;", c.ForwardSql);
            A.Eq("UPDATE `creature_template` SET `minlevel` = 2, `faction` = 38 WHERE `entry` = 69 LIMIT 1;", c.UndoSql);
        }

        [Test] static void FreeIdQueryText()
        {
            A.Eq("SELECT COALESCE(MAX(`entry`) + 1, 900000) FROM `creature_template` WHERE `entry` >= 900000", Duplicator.FreeIdQuery("creature_template", "entry", 900000));
            A.Eq(StatementKind.Read, SqlClassifier.ClassifyOne(Duplicator.FreeIdQuery("quest_template", "ID", 5)).Kind, "runs on the read-only account");
        }

        [Test] static void DuplicateStagesInsertWithDeleteUndo()
        {
            var cs = new Changeset();
            var c = Duplicator.Stage(cs, "creature_template", "entry", 900000, new[] { "entry", "name", "minlevel", "subname" }, new object[] { 69u, "Diseased Timber Wolf", (byte)2, "" }, "name");
            A.Eq(ChangeKind.Insert, c.Kind);
            A.Eq("INSERT INTO `creature_template` (`entry`, `name`, `minlevel`, `subname`) VALUES (900000, 'Diseased Timber Wolf (copy)', 2, '');", c.ForwardSql);
            A.Eq("DELETE FROM `creature_template` WHERE `entry` = 900000 LIMIT 1;", c.UndoSql);
            A.Contains(c.Summary, "copy of #69");
            A.Eq("Diseased Timber Wolf (copy)", c.Fields[1].AfterDisplay);
        }

        [Test] static void NextIdSkipsStagedCopies()
        {
            var cs = new Changeset();
            Duplicator.Stage(cs, "item_template", "entry", 900000, new[] { "entry" }, new object[] { 25 });
            Duplicator.Stage(cs, "item_template", "entry", 900001, new[] { "entry" }, new object[] { 25 });
            A.Eq(900002L, Duplicator.NextId(900000, cs, "item_template", 900000));
            A.Eq(900000L, Duplicator.NextId(900000, cs, "creature_template", 900000), "other tables don't count");
            A.Eq(900000L, Duplicator.NextId(5, new Changeset(), "item_template", 900000), "never below the custom start");
        }

        [Test] static void DuplicateInsertAppliesAndRollsBack()
        {
            var cs = new Changeset();
            var c = Duplicator.Stage(cs, "item_template", "entry", 900010, new[] { "entry", "name" }, new object[] { 25, "Worn Shortsword" }, "name");
            var db = new FakeDb { OnQuery = _ => FakeDb.Table(new[] { "entry" }) }; // no row with that id yet
            A.True(ApplyEngine.Apply(new[] { c }, db).Success, "apply");
            A.Eq("X " + c.ForwardSql, db.Log.First(l => l.StartsWith("X")));
            var db2 = new FakeDb();
            A.True(ApplyEngine.Rollback(new[] { c }, db2).Success, "rollback");
            A.Eq("X DELETE FROM `item_template` WHERE `entry` = 900010 LIMIT 1;", db2.Log.First(l => l.StartsWith("X")));
        }

        [Test] static void DbcReaderReadsNumbersAndStrings()
        {
            var bytes = DbcFile.Build(3, new List<object[]> { new object[] { 1u, "Wolf", 7u }, new object[] { 2u, "Cat", 9u }, new object[] { 3u, "", 1u } });
            var f = new DbcFile(bytes);
            A.Eq(3, f.Records); A.Eq(3, f.Fields);
            A.Eq("Cat", f.Str(1, 1)); A.Eq(9u, f.UInt(1, 2));
            var names = f.Names(1);
            A.Eq(2, names.Count, "empty names skipped");
            A.Eq("Wolf", names[1]);
            A.Throws(() => new DbcFile(new byte[] { 1, 2, 3 }), "bad header");
        }

        [Test] static void DbcNamesFromFolder()
        {
            var dir = Path.Combine(Path.GetTempPath(), "acs_dbc_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var fac = new object[23 + 1]; fac[0] = 15u; for (int i = 1; i < 23; i++) fac[i] = 0u; fac[23] = "Defias Brotherhood";
                File.WriteAllBytes(Path.Combine(dir, "Faction.dbc"), DbcFile.Build(24, new List<object[]> { fac }));
                File.WriteAllBytes(Path.Combine(dir, "FactionTemplate.dbc"), DbcFile.Build(2, new List<object[]> { new object[] { 17u, 15u } }));
                var n = new DbcNames(dir);
                A.Eq("Defias Brotherhood", n.Name("faction", 15));
                A.Eq("Defias Brotherhood", n.Name("factiontemplate", 17), "template → faction name");
                A.Eq(null, n.Name("spell", 1), "missing file = no name");
                A.Eq(17L, n.Search("factiontemplate", "defias").First().id);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Test] static void FieldDefsAreCompleteAndUnique()
        {
            foreach (var e in EntityDefs.All)
            {
                A.True(e.Tabs.Length > 0, e.Key + " tabs");
                foreach (var f in e.Fields)
                {
                    A.True(!string.IsNullOrWhiteSpace(f.Label) && !string.IsNullOrWhiteSpace(f.Guide) && !string.IsNullOrWhiteSpace(f.Tip), $"{e.Key}.{f.Column} has label, guide and tip");
                    A.True(e.Tabs.Contains(f.Tab), $"{f.Column} tab {f.Tab} exists");
                    if (f.Kind == FieldKind.Enum) A.True(f.Options?.Count > 0, f.Column + " options");
                    if (f.Kind == FieldKind.Flags) A.True(f.Bits?.Count > 0, f.Column + " bits");
                    if (f.Kind == FieldKind.Ref) A.True(!string.IsNullOrEmpty(f.RefKind), f.Column + " ref kind");
                    if (f.Table != null) A.True(e.SideTables.ContainsKey(f.Table), f.Column + " side table registered");
                }
                var dup = e.Fields.GroupBy(f => (f.Table ?? e.Table) + "." + f.Column).FirstOrDefault(g => g.Count() > 1);
                A.True(dup == null, "no duplicate column " + dup?.Key);
            }
        }

        [Test] static void FieldDefsMatchAzerothCoreSchema()
        {
            var baseDir = Path.Combine(DevPaths.AzerothCore, "data", "sql", "base", "db_world");
            if (!Directory.Exists(baseDir)) return; // schema reference not on this machine
            foreach (var e in EntityDefs.All)
                foreach (var g in e.Fields.GroupBy(f => f.Table ?? e.Table))
                {
                    var sql = File.ReadAllText(Path.Combine(baseDir, g.Key + ".sql"));
                    var cols = new HashSet<string>(Regex.Matches(sql, @"(?m)^\s*`(\w+)`\s+\w").Select(m => m.Groups[1].Value), StringComparer.OrdinalIgnoreCase);
                    foreach (var f in g) A.True(cols.Contains(f.Column), $"{g.Key}.{f.Column} exists in AzerothCore");
                }
        }
    }
}
