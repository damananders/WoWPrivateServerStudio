using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AndersCraftStudio.Core;

namespace AndersCraftStudio.Tests
{
    static class ConfTests
    {
        const string Sample = @"
###############################################
# AzerothCore World Server configuration file #
###############################################
[worldserver]
# LoginDatabaseInfo = ""ignored;1;a;b;c""
LoginDatabaseInfo     = ""127.0.0.1;3309;acore;s3cret;acore_auth""
WorldDatabaseInfo     = ""127.0.0.1;3309;acore;s3cret;acore_world""
CharacterDatabaseInfo = ""127.0.0.1;3309;acore;s3cret;acore_characters""
HCWorldDatabaseInfo   = ""127.0.0.1;3309;acore;s3cret;achc_world""
DataDir = ""D:\Server\Data""
RealmID = 1
Rate.XP.Kill = 2  # comment after value
";

        [Test] static void ParsesDatabaseLines()
        {
            var c = ConfParser.Parse(Sample);
            A.Eq("AzerothCore", c.Core);
            A.Eq("127.0.0.1", c.World.Host); A.Eq(3309, c.World.Port); A.Eq("acore", c.World.User);
            A.Eq("s3cret", c.World.Password); A.Eq("acore_world", c.World.Database);
            A.Eq("acore_characters", c.Characters.Database); A.Eq("acore_auth", c.Login.Database);
            A.Eq(0, c.Warnings.Count, "warnings");
        }

        [Test] static void IgnoresCommentsAndReadsOtherKeys()
        {
            var c = ConfParser.Parse(Sample);
            A.Eq(@"D:\Server\Data", c.DataDir); A.Eq(1, c.RealmId.Value);
            A.Eq("2", c.Get("Rate.XP.Kill"), "trailing comment stripped");
            A.Eq("127.0.0.1;3309;acore;s3cret;achc_world", c.Get("HCWorldDatabaseInfo"), "custom keys kept");
        }

        [Test] static void PasswordContainingSemicolon()
        {
            var d = ConfParser.ParseDbInfo("localhost;3306;root;pa;ss;word;acore_world");
            A.Eq("pa;ss;word", d.Password); A.Eq("acore_world", d.Database);
        }

        [Test] static void SocketPortAndBadLines()
        {
            var d = ConfParser.ParseDbInfo(".;.;acore;x;acore_world");
            A.True(d.UsesSocket, "socket"); A.Eq(3306, d.Port);
            A.Eq(null, ConfParser.ParseDbInfo("127.0.0.1;notaport;a;b;c"));
            A.Eq(null, ConfParser.ParseDbInfo("only;three;parts"));
            A.Eq(null, ConfParser.ParseDbInfo(""));
        }

        [Test] static void LaterDuplicateWinsAndMissingWarns()
        {
            var c = ConfParser.Parse("WorldDatabaseInfo = \"a;1;u;p;first\"\nWorldDatabaseInfo = \"b;2;u;p;second\"\n");
            A.Eq("second", c.World.Database);
            A.True(c.Warnings.Count == 2, "warns for missing login/characters");
            A.Eq("AzerothCore-compatible (not confirmed)", c.Core);
        }

        [Test] static void LocatorFindsBuildLayout()
        {
            var root = Path.Combine(Path.GetTempPath(), "acs_loc_" + Guid.NewGuid().ToString("N"));
            var cfg = Path.Combine(root, "azerothcore-wotlk", "Build", "bin", "RelWithDebInfo", "configs");
            Directory.CreateDirectory(cfg);
            File.WriteAllText(Path.Combine(cfg, "worldserver.conf"), Sample);
            try
            {
                var found = ConfLocator.Find(new[] { root });
                A.Eq(1, found.Count, "found");
                A.Contains(found[0], "RelWithDebInfo");
            }
            finally { Directory.Delete(root, true); }
        }
    }

    static class ClassifierTests
    {
        static StatementKind K(string sql) => SqlClassifier.ClassifyOne(sql).Kind;

        [Test] static void Reads()
        {
            foreach (var s in new[] { "SELECT * FROM creature_template", "  select 1", "SHOW TABLES", "DESCRIBE item_template", "DESC quest_template",
                "EXPLAIN SELECT 1", "(SELECT 1) UNION (SELECT 2)", "WITH x AS (SELECT 1) SELECT * FROM x", "SELECT 'DELETE FROM t'", "SELECT `update` FROM t",
                "-- DELETE FROM t\nSELECT 1", "/* DROP TABLE t */ SELECT 1", "# UPDATE\nSELECT 1", "TABLE creature" })
                A.Eq(StatementKind.Read, K(s), s);
        }

        [Test] static void RowWrites()
        {
            foreach (var s in new[] { "UPDATE creature_template SET name='x' WHERE entry=1", "insert into t values (1)", "DELETE FROM t", "REPLACE INTO t VALUES (1)",
                "WITH x AS (SELECT 1) UPDATE t SET a = 1", "  /* hi */ Update t set a=1" })
                A.Eq(StatementKind.RowWrite, K(s), s);
        }

        [Test] static void StructureWrites()
        {
            foreach (var s in new[] { "DROP TABLE t", "CREATE TABLE t (a int)", "ALTER TABLE t ADD c INT", "TRUNCATE t", "RENAME TABLE a TO b" })
                A.Eq(StatementKind.StructureWrite, K(s), s);
        }

        [Test] static void BlockedOrUnknown()
        {
            foreach (var s in new[] { "GRANT ALL ON *.* TO x", "SET GLOBAL x = 1", "CALL p()", "LOAD DATA INFILE 'x' INTO TABLE t", "COMMIT", "USE acore_world",
                "SELECT * FROM t INTO OUTFILE '/tmp/x'", "SELECT 1 INTO @a", "/*!50000 DELETE FROM t */", "SELECT 1 /*!, (DELETE) */", "FLUSH PRIVILEGES",
                "EXPLAIN ANALYZE DELETE FROM t", "frobnicate", "", "123" })
                A.Eq(StatementKind.Blocked, K(s), s);
        }

        [Test] static void MultipleStatementsAreSplitAndChecked()
        {
            var all = SqlClassifier.Classify("SELECT 1; SELECT ';'; UPDATE t SET a = 'x;y'");
            A.Eq(3, all.Count, "count");
            A.Eq(StatementKind.RowWrite, all[2].Kind);
            A.False(SqlClassifier.IsReadOnly("SELECT 1; DELETE FROM t"), "mixed is not read-only");
            A.True(SqlClassifier.IsReadOnly("SELECT 1; SHOW TABLES;"), "two reads");
            A.Eq(StatementKind.Blocked, SqlClassifier.ClassifyOne("SELECT 1; DROP TABLE t").Kind, "ClassifyOne refuses two statements");
        }

        [Test] static void StringsWithQuotesAndEscapes()
        {
            var all = SqlClassifier.Classify(@"SELECT 'it\'s; fine', ""a"""";b"", 'x''; DROP TABLE t; --'");
            A.Eq(1, all.Count, "one statement");
            A.Eq(StatementKind.Read, all[0].Kind);
        }

        [Test] static void DashDashNeedsSpace()
        {
            // "1--1" is arithmetic in MySQL, not a comment
            var toks = SqlLexer.Lex("SELECT 1--1");
            A.Eq(5, toks.Count, "tokens");
        }
    }

    static class SqlGenTests
    {
        [Test] static void Literals()
        {
            A.Eq("NULL", SqlGen.Literal(null)); A.Eq("NULL", SqlGen.Literal(DBNull.Value));
            A.Eq("'O\\'Brien \\\\ \\n'", SqlGen.Literal("O'Brien \\ \n"));
            A.Eq("42", SqlGen.Literal(42)); A.Eq("18446744073709551615", SqlGen.Literal(ulong.MaxValue));
            A.Eq("1.5", SqlGen.Literal(1.5f)); A.Eq("0.1", SqlGen.Literal(0.1));
            A.Eq("1.25", SqlGen.Literal(1.25m));
            A.Eq("X'00FF'", SqlGen.Literal(new byte[] { 0, 255 }));
            A.Eq("'2026-11-19 10:20:30'", SqlGen.Literal(new DateTime(2026, 11, 19, 10, 20, 30)));
            A.Eq("1", SqlGen.Literal(true));
            A.Throws(() => SqlGen.Literal(double.NaN), "NaN");
        }

        [Test] static void IdentEscaping()
        {
            A.Eq("`creature`", SqlGen.Ident("creature"));
            A.Eq("`we``ird`", SqlGen.Ident("we`ird"));
        }

        [Test] static void LiteralRoundTripsThroughLexer()
        {
            foreach (var s in new[] { "plain", "it's", "back\\slash", "new\nline", "nul\0", "semi;colon", "\"dq\"", "ctrl-z\x1a" })
            {
                var toks = SqlLexer.Lex(SqlGen.Literal(s));
                A.Eq(1, toks.Count, "one token for " + s);
                A.Eq(s, toks[0].Value, "round trip");
            }
        }

        [Test] static void TypedLiteralFromGridInput()
        {
            A.True(SqlGen.TryTypedLiteral("12", typeof(uint), false, out var l, out _), "uint"); A.Eq("12", l);
            A.False(SqlGen.TryTypedLiteral("-1", typeof(uint), false, out _, out _), "negative uint");
            A.False(SqlGen.TryTypedLiteral("300", typeof(byte), false, out _, out _), "byte overflow");
            A.True(SqlGen.TryTypedLiteral("1.5", typeof(float), false, out l, out _), "float"); A.Eq("1.5", l);
            A.False(SqlGen.TryTypedLiteral("1,5", typeof(float), false, out _, out _), "comma decimal");
            A.True(SqlGen.TryTypedLiteral("NULL", typeof(string), true, out l, out _), "null"); A.Eq("NULL", l);
            A.False(SqlGen.TryTypedLiteral("NULL", typeof(string), false, out _, out _), "not nullable");
            A.True(SqlGen.TryTypedLiteral("null", typeof(string), false, out l, out _), "lower-case null is text"); A.Eq("'null'", l);
            A.True(SqlGen.TryTypedLiteral("0x0a1b", typeof(byte[]), false, out l, out _), "hex"); A.Eq("X'0A1B'", l);
            A.True(SqlGen.TryTypedLiteral("x' OR 1=1 --", typeof(string), false, out l, out _), "injection is quoted");
            A.Eq("'x\\' OR 1=1 --'", l);
        }

        [Test] static void UpdateInsertDelete()
        {
            var key = new List<KeyPart> { new KeyPart("entry", "1234") };
            A.Eq("UPDATE `creature_template` SET `name` = 'Bob', `minlevel` = 5 WHERE `entry` = 1234 LIMIT 1;",
                SqlGen.UpdateRow("creature_template", key, new[] { ("name", "'Bob'"), ("minlevel", "5") }));
            A.Eq("DELETE FROM `t` WHERE `a` = 1 AND `b` = 'x' LIMIT 1;", SqlGen.DeleteRow("t", new List<KeyPart> { new KeyPart("a", "1"), new KeyPart("b", "'x'") }));
            A.Eq("INSERT INTO `t` (`a`, `b`) VALUES (1, NULL);", SqlGen.InsertRow("t", new[] { "a", "b" }, new[] { "1", "NULL" }));
            A.Throws(() => SqlGen.WhereKey(new List<KeyPart>()), "empty key");
        }
    }

    static class UndoTests
    {
        static List<KeyPart> Key(int e) => new List<KeyPart> { new KeyPart("entry", e.ToString()) };

        [Test] static void CellEditUndoRestoresBefore()
        {
            var cs = new Changeset();
            var c = ChangeFactory.StageCellEdit(cs, "creature_template", Key(1), "name", "'Old'", "Old", "'New'", "New");
            A.Eq("UPDATE `creature_template` SET `name` = 'New' WHERE `entry` = 1 LIMIT 1;", c.ForwardSql);
            A.Eq("UPDATE `creature_template` SET `name` = 'Old' WHERE `entry` = 1 LIMIT 1;", c.UndoSql);
            A.Eq("Creatures", c.Category);
        }

        [Test] static void EditsToSameRowMergeAndKeepOriginalBefore()
        {
            var cs = new Changeset();
            ChangeFactory.StageCellEdit(cs, "creature_template", Key(1), "name", "'Old'", "Old", "'Mid'", "Mid");
            ChangeFactory.StageCellEdit(cs, "creature_template", Key(1), "name", "'Mid'", "Mid", "'New'", "New");
            ChangeFactory.StageCellEdit(cs, "creature_template", Key(1), "minlevel", "1", "1", "2", "2");
            A.Eq(1, cs.Changes.Count, "merged");
            var c = cs.Changes[0];
            A.Eq("'Old'", c.Fields[0].Before); A.Eq("'New'", c.Fields[0].After);
            A.Eq("UPDATE `creature_template` SET `name` = 'Old', `minlevel` = 1 WHERE `entry` = 1 LIMIT 1;", c.UndoSql);
        }

        [Test] static void EditingBackToOriginalRemovesChange()
        {
            var cs = new Changeset();
            ChangeFactory.StageCellEdit(cs, "item_template", Key(5), "Quality", "2", "2", "4", "4");
            ChangeFactory.StageCellEdit(cs, "item_template", Key(5), "Quality", "4", "4", "2", "2");
            A.Eq(0, cs.Changes.Count, "no-op removed");
        }

        [Test] static void RowDeleteUndoReinsertsEveryColumn()
        {
            var cs = new Changeset();
            var c = ChangeFactory.StageRowDelete(cs, "creature_loot_template", new List<KeyPart> { new KeyPart("Entry", "10"), new KeyPart("Item", "25") },
                new[] { "Entry", "Item", "Chance", "Comment" }, new object[] { 10, 25, 33.3f, null });
            A.Eq("DELETE FROM `creature_loot_template` WHERE `Entry` = 10 AND `Item` = 25 LIMIT 1;", c.ForwardSql);
            A.Eq("INSERT INTO `creature_loot_template` (`Entry`, `Item`, `Chance`, `Comment`) VALUES (10, 25, 33.3, NULL);", c.UndoSql);
            A.Eq("Loot", c.Category);
        }

        [Test] static void RawUpdateCapturesRowsForUndo()
        {
            var db = new FakeDb();
            db.Pks["creature_template"] = new[] { "entry" };
            db.OnQuery = sql => FakeDb.Table(new[] { "entry", "name", "minlevel" }, new object[] { 1, "A", 3 }, new object[] { 2, "B's", 4 });
            var st = SqlClassifier.ClassifyOne("UPDATE creature_template SET minlevel = minlevel + 1, name = UPPER(name) WHERE entry IN (1,2)");
            var cs = new Changeset();
            var c = ChangeFactory.StageRawStatement(cs, st, db);
            A.True(c.UndoAvailable, c.UndoNote);
            A.Eq("Q SELECT * FROM `creature_template` WHERE entry IN (1,2) LIMIT 5001", db.Log[0]);
            A.Eq("UPDATE `creature_template` SET `minlevel` = 3, `name` = 'A' WHERE `entry` = 1 LIMIT 1;\r\n".Replace("\r\n", Environment.NewLine) +
                 "UPDATE `creature_template` SET `minlevel` = 4, `name` = 'B\\'s' WHERE `entry` = 2 LIMIT 1;", c.UndoSql);
            A.False(db.Log.Any(l => l.StartsWith("X")), "nothing executed while staging");
        }

        [Test] static void RawDeleteUndoInserts()
        {
            var db = new FakeDb();
            db.Pks["npc_vendor"] = new[] { "entry", "item", "ExtendedCost" };
            db.OnQuery = sql => FakeDb.Table(new[] { "entry", "slot", "item", "maxcount", "ExtendedCost" }, new object[] { 100, 0, 2512, 0, 0 });
            var st = SqlClassifier.ClassifyOne("DELETE FROM `npc_vendor` WHERE entry = 100 AND item = 2512");
            var c = ChangeFactory.StageRawStatement(new Changeset(), st, db);
            A.Eq("INSERT INTO `npc_vendor` (`entry`, `slot`, `item`, `maxcount`, `ExtendedCost`) VALUES (100, 0, 2512, 0, 0);", c.UndoSql);
            A.Eq(3, c.Key.Count, "key captured");
        }

        [Test] static void RawInsertUndoDeletesByKey()
        {
            var db = new FakeDb(); db.Pks["game_tele"] = new[] { "id" };
            var st = SqlClassifier.ClassifyOne("INSERT INTO game_tele (id, position_x, name) VALUES (9001, 1.5, 'Home'), (9002, -2, 'Away')");
            var c = ChangeFactory.StageRawStatement(new Changeset(), st, db);
            A.True(c.UndoAvailable, c.UndoNote);
            A.Eq(("DELETE FROM `game_tele` WHERE `id` = 9001 LIMIT 1;" + Environment.NewLine + "DELETE FROM `game_tele` WHERE `id` = 9002 LIMIT 1;"), c.UndoSql);
        }

        [Test] static void RawUndoRefusals()
        {
            var db = new FakeDb(); db.Pks["t"] = new[] { "id" };
            string Note(string sql) => ChangeFactory.StageRawStatement(new Changeset(), SqlClassifier.ClassifyOne(sql), db).UndoNote;
            A.Contains(Note("INSERT INTO t (name) VALUES ('x')"), "primary key");
            A.Contains(Note("INSERT INTO t VALUES (1)"), "column names");
            A.Contains(Note("INSERT IGNORE INTO t (id) VALUES (1)"), "IGNORE");
            A.Contains(Note("INSERT INTO t (id) VALUES (1) ON DUPLICATE KEY UPDATE id = 2"), "DUPLICATE");
            A.Contains(Note("REPLACE INTO t (id) VALUES (1)"), "REPLACE");
            A.Contains(Note("UPDATE t SET id = id + 1"), "primary key column");
            A.Contains(Note("UPDATE t a JOIN u b ON a.id=b.id SET a.x = 1"), "join");
            A.Contains(Note("INSERT INTO nopk (id) VALUES (1)"), "no primary key");
            var big = new FakeDb(); big.Pks["t"] = new[] { "id" };
            big.OnQuery = _ => { var r = FakeDb.Table(new[] { "id" }); for (int i = 0; i < 5001; i++) r.Rows.Add(new object[] { i }); return r; };
            A.Contains(ChangeFactory.StageRawStatement(new Changeset(), SqlClassifier.ClassifyOne("DELETE FROM t"), big).UndoNote, "more than 5000");
            var ddl = ChangeFactory.StageRawStatement(new Changeset(), SqlClassifier.ClassifyOne("DROP TABLE t"), db);
            A.True(ddl.Structure && !ddl.UndoAvailable, "ddl flagged");
        }

        [Test] static void RawParserPieces()
        {
            var p = RawStatementParser.Parse(SqlClassifier.ClassifyOne("UPDATE acore_world.item_template SET BuyPrice = 10 WHERE entry = 25 ORDER BY entry LIMIT 1"));
            A.Eq("acore_world", p.Database); A.Eq("item_template", p.Table);
            A.Eq("entry = 25", p.Where); A.Eq("entry", p.OrderBy); A.Eq("1", p.Limit);
            A.True(p.Qualifiers.Contains("acore_world"), "qualifier");
            A.Eq("SELECT * FROM `item_template` WHERE entry = 25 ORDER BY entry LIMIT 1 FOR UPDATE", RawUndoBuilder.SnapshotQuery(p, true));
            var ins = RawStatementParser.Parse(SqlClassifier.ClassifyOne("INSERT INTO t SET id = 5, name = 'a,b'"));
            A.Eq(2, ins.InsertColumns.Count, "insert set cols"); A.Eq("'a,b'", ins.InsertRows[0][1]);
            A.True(RawStatementParser.IsSimpleLiteral("-5"), "neg"); A.False(RawStatementParser.IsSimpleLiteral("5+1"), "expr");
        }
    }

    static class ApplyTests
    {
        static Change Edit(out Changeset cs)
        {
            cs = new Changeset();
            return ChangeFactory.StageCellEdit(cs, "creature_template", new List<KeyPart> { new KeyPart("entry", "7") }, "minlevel", "1", "1", "60", "60");
        }

        [Test] static void AppliesInOneTransaction()
        {
            var c = Edit(out _);
            var db = new FakeDb { OnQuery = _ => FakeDb.Table(new[] { "minlevel" }, new object[] { (byte)1 }) };
            var o = ApplyEngine.Apply(new[] { c }, db);
            A.True(o.Success, o.Message);
            A.Eq("BEGIN", db.Log[0]);
            A.Eq("Q SELECT `minlevel` FROM `creature_template` WHERE `entry` = 7 LIMIT 2 FOR UPDATE;", db.Log[1]);
            A.Eq("X " + c.ForwardSql, db.Log[2]);
            A.Eq("COMMIT", db.Log[3]);
        }

        [Test] static void ConflictRollsBackAndExecutesNothing()
        {
            var c = Edit(out _);
            var db = new FakeDb { OnQuery = _ => FakeDb.Table(new[] { "minlevel" }, new object[] { 5 }) };
            var o = ApplyEngine.Apply(new[] { c }, db);
            A.False(o.Success, "should fail");
            A.Contains(o.Message, "Something changed that row");
            A.False(db.Log.Any(l => l.StartsWith("X")), "no write ran");
            A.Eq("ROLLBACK", db.Log.Last());
        }

        [Test] static void UnexpectedRowCountRollsBack()
        {
            var c = Edit(out _);
            var db = new FakeDb { OnQuery = _ => FakeDb.Table(new[] { "minlevel" }, new object[] { 1 }), OnExecute = _ => 0 };
            var o = ApplyEngine.Apply(new[] { c }, db);
            A.False(o.Success, "0 rows is a failure");
            A.Eq("ROLLBACK", db.Log.Last());
        }

        [Test] static void RollbackRunsUndoNewestFirst()
        {
            var cs = new Changeset();
            var a = ChangeFactory.StageCellEdit(cs, "t", new List<KeyPart> { new KeyPart("id", "1") }, "v", "1", "1", "2", "2");
            var b = ChangeFactory.StageCellEdit(cs, "t", new List<KeyPart> { new KeyPart("id", "2") }, "v", "1", "1", "3", "3");
            var db = new FakeDb { OnQuery = sql => FakeDb.Table(new[] { "v" }, new object[] { sql.Contains("= 1 ") ? 2 : 3 }) };
            var o = ApplyEngine.Rollback(new[] { a, b }, db);
            A.True(o.Success, o.Message);
            var writes = db.Log.Where(l => l.StartsWith("X")).ToList();
            A.Eq("X " + b.UndoSql, writes[0]); A.Eq("X " + a.UndoSql, writes[1]);
        }

        [Test] static void RollbackRefusesWhenChangedAfterApply()
        {
            var c = Edit(out _);
            var db = new FakeDb { OnQuery = _ => FakeDb.Table(new[] { "minlevel" }, new object[] { 70 }) };
            var o = ApplyEngine.Rollback(new[] { c }, db);
            A.False(o.Success, "conflict");
            A.False(db.Log.Any(l => l.StartsWith("X")), "nothing ran");
        }

        [Test] static void ValidateBlocksStructureAndOtherDatabases()
        {
            var cs = new Changeset();
            var ddl = ChangeFactory.StageRawStatement(cs, SqlClassifier.ClassifyOne("ALTER TABLE t ADD x INT"), null);
            var cross = ChangeFactory.StageRawStatement(cs, SqlClassifier.ClassifyOne("UPDATE acore_world.t SET a = 1 WHERE id = 1"), null);
            var sneaky = ChangeFactory.StageRawStatement(cs, SqlClassifier.ClassifyOne("DELETE FROM t WHERE id IN (SELECT guid FROM acore_characters.characters)"), null);
            var p = ApplyEngine.Validate(cs.Changes, "acore_world_studio_sandbox", new[] { "acore_world", "acore_characters", "acore_auth" });
            A.True(p.Any(x => x.Contains("structure")), "ddl blocked");
            A.True(p.Any(x => x.Contains("names database acore_world")), "explicit db blocked");
            A.True(p.Any(x => x.Contains("acore_characters")), "qualifier blocked");
            A.Eq(0, ApplyEngine.Validate(new[] { ChangeFactory.StageCellEdit(new Changeset(), "t", new List<KeyPart> { new KeyPart("id", "1") }, "v", "1", "1", "2", "2") },
                "acore_world", new[] { "acore_characters" }).Count, "plain edit ok");
        }

        [Test] static void RollbackRefusedWithoutUndo()
        {
            var c = ChangeFactory.StageRawStatement(new Changeset(), SqlClassifier.ClassifyOne("REPLACE INTO t (id) VALUES (1)"), new FakeDb());
            var db = new FakeDb();
            var o = ApplyEngine.Rollback(new[] { c }, db);
            A.False(o.Success, "refused"); A.Eq(0, db.Log.Count, "db untouched");
        }
    }

    static class ScriptTests
    {
        [Test] static void AccountScriptGrantsAreLimited()
        {
            var p = new Profile();
            var s = AccountScript.Build(p, "roPw'1", "rwPw2");
            A.Contains(s, "CREATE USER IF NOT EXISTS 'studio_ro'@'localhost' IDENTIFIED BY 'roPw\\'1';");
            A.Contains(s, "GRANT SELECT, SHOW VIEW ON `acore\\_world`.* TO 'studio_ro'@'127.0.0.1';");
            A.Contains(s, "GRANT SELECT, INSERT, UPDATE, DELETE, SHOW VIEW ON `acore\\_world`.* TO 'studio_rw'@'localhost';");
            A.False(s.Contains("ALL PRIVILEGES"), "never ALL");
            A.False(s.Contains("acore\\_characters`.* TO 'studio_rw'"), "rw has no characters access");
            A.False(s.Contains("acore\\_auth`.* TO 'studio_rw'"), "rw has no auth access");
            // every executable line is a CREATE/ALTER USER, GRANT or SHOW GRANTS
            foreach (var st in SqlClassifier.Classify(s))
                A.True(st.Verb == "CREATE" || st.Verb == "ALTER" || st.Verb == "GRANT" || st.Verb == "SHOW", st.Text);
        }

        [Test] static void GrantCheckSpotsWritePrivileges()
        {
            A.Eq(0, GrantCheck.WritePrivileges(new[] { "GRANT USAGE ON *.* TO `studio_ro`@`localhost`", "GRANT SELECT, SHOW VIEW ON `acore\\_world`.* TO `studio_ro`@`localhost`" }).Count, "ro clean");
            var w = GrantCheck.WritePrivileges(new[] { "GRANT SELECT, INSERT, UPDATE ON `acore_world`.* TO `x`@`%`" });
            A.True(w.Contains("INSERT") && w.Contains("UPDATE"), "writes found");
            A.True(GrantCheck.WritePrivileges(new[] { "GRANT ALL PRIVILEGES ON *.* TO `root`@`localhost` WITH GRANT OPTION" }).Contains("ALL"), "all");
            A.True(GrantCheck.WritePrivileges(new[] { "GRANT `admin`@`%` TO `x`@`%`" }).Contains("ROLE"), "role");
        }

        [Test] static void SandboxPlanCopiesEveryTable()
        {
            var s = SandboxPlan.Build(new Profile(), new[] { "creature", "item_template" });
            A.Contains(s, "CREATE DATABASE IF NOT EXISTS `acore_world_studio_sandbox`");
            A.Contains(s, "CREATE TABLE IF NOT EXISTS `acore_world_studio_sandbox`.`item_template` LIKE `acore_world`.`item_template`;");
            A.Contains(s, "INSERT INTO `acore_world_studio_sandbox`.`creature` SELECT * FROM `acore_world`.`creature`;");
        }

        [Test] static void BackupArgsKeepPasswordOffCommandLine()
        {
            var a = Backup.BuildArgs(@"C:\t\x.cnf", "127.0.0.1", 3309, "acore_world", new[] { "creature_template" }, @"D:\b\out.sql");
            A.True(a.Contains("--single-transaction"), "consistent dump");
            A.False(a.Any(x => x.StartsWith("--password") || x.StartsWith("-p")), "no password arg");
            A.Eq("creature_template", a.Last());
            A.Contains(Backup.OptionFileText("u", "p\"q"), "password=\"p\\\"q\"");
        }

        [Test] static void ChangesetStoreRoundTripAndHistory()
        {
            var dir = Path.Combine(Path.GetTempPath(), "acs_cs_" + Guid.NewGuid().ToString("N"));
            try
            {
                var st = new ChangesetStore(dir);
                var c = ChangeFactory.StageCellEdit(st.Pending, "t", new List<KeyPart> { new KeyPart("id", "1") }, "v", "1", "1", "2", "2");
                ChangeFactory.StageCellEdit(st.Pending, "t", new List<KeyPart> { new KeyPart("id", "2") }, "v", "1", "1", "2", "2");
                st.SavePending();
                var again = new ChangesetStore(dir);
                A.Eq(2, again.Pending.Changes.Count, "reloaded");
                again.RecordApplied(new[] { again.Pending.Changes[0] }, new ApplyRecord { Target = "Sandbox", Success = true });
                A.Eq(1, again.Pending.Changes.Count, "applied one moved out");
                A.Eq(1, again.History().Count, "history");
                A.Contains(again.ExportSql(again.History()[0].Changes, true), "START TRANSACTION;");
            }
            finally { Directory.Delete(dir, true); }
        }

        [Test] static void CatalogBasics()
        {
            A.True(TableCatalog.Describe("creature_template") != null, "described");
            A.Eq(null, TableCatalog.Describe("no_such_table"));
            A.Eq("Loot", TableCatalog.CategoryOf("gameobject_loot_template"));
            A.Eq("Quests", TableCatalog.CategoryOf("creature_queststarter"));
            A.Contains(TableCatalog.FieldMeaning("skinning_loot_template", "Chance"), "percent");
        }
    }
}
