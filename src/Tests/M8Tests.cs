using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AndersCraftStudio.Core;

namespace AndersCraftStudio.Tests
{
    /// <summary>0.8: side-table rows created on first edit, Keira3 parity tables, the Admin console's SQL builders.</summary>
    static class M8Tests
    {
        static List<KeyPart> K(long id, string col = "entry") => new List<KeyPart> { new KeyPart(col, id.ToString(), "#" + id) };

        // ---------------------------------------------------------------- side rows
        [Test] static void Missing_side_row_is_staged_as_one_insert_then_merged()
        {
            var cs = new Changeset();
            var s = new EditSession();
            var cols = new[] { "entry", "path_id", "mount", "auras" };
            var defs = new[] { "0", "0", "0", "NULL" };
            s.Set("creature_template_addon", K(100), "mount", "0", "0", "2402", "2402");
            s.Set("creature_template_addon", K(100), "auras", "NULL", "NULL", "'1234'", "1234");
            s.Set("creature_template", K(100), "name", "'A'", "A", "'B'", "B");
            var c = s.StageAsNewRow(cs, "creature_template_addon", K(100), cols, defs);
            A.Eq(ChangeKind.Insert, c.Kind);
            A.Eq("INSERT INTO `creature_template_addon` (`entry`, `path_id`, `mount`, `auras`) VALUES (100, 0, 2402, '1234');", c.ForwardSql);
            A.Eq("DELETE FROM `creature_template_addon` WHERE `entry` = 100 LIMIT 1;", c.UndoSql);
            A.Eq(1, s.Count, "other tables' edits stay in the session");
            A.Eq("2402", c.Fields.First(f => f.Name == "mount").AfterDisplay);
            // a later edit merges into the staged insert instead of adding an UPDATE
            s.Set("creature_template_addon", K(100), "path_id", "0", "0", "55", "55");
            var c2 = s.StageAsNewRow(cs, "creature_template_addon", K(100), cols, defs);
            A.True(ReferenceEquals(c, c2), "same change");
            A.Eq("INSERT INTO `creature_template_addon` (`entry`, `path_id`, `mount`, `auras`) VALUES (100, 55, 2402, '1234');", c.ForwardSql);
            A.Eq(1, cs.Changes.Count(x => x.Table == "creature_template_addon"));
            A.True(s.StageAsNewRow(cs, "creature_template_addon", K(100), cols, defs) == null, "nothing to do without edits");
        }

        [Test] static void Column_defaults_for_new_rows()
        {
            A.Eq("0", ColumnDefaults.Literal(DBNull.Value, typeof(uint), false), "NOT NULL number");
            A.Eq("''", ColumnDefaults.Literal(null, typeof(string), false), "NOT NULL text");
            A.Eq("NULL", ColumnDefaults.Literal(null, typeof(string), true), "nullable");
            A.Eq("NULL", ColumnDefaults.Literal("NULL", typeof(int), true), "MariaDB spells NULL");
            A.Eq("1", ColumnDefaults.Literal("1", typeof(byte), false));
            A.Eq("-1", ColumnDefaults.Literal("-1", typeof(int), false));
            A.Eq("'abc'", ColumnDefaults.Literal("'abc'", typeof(string), false), "MariaDB quotes text defaults");
            A.Eq("'abc'", ColumnDefaults.Literal("abc", typeof(string), false), "MySQL doesn't");
            A.Eq("1.5", ColumnDefaults.Literal("1.5", typeof(float), false));
            A.Eq(1.5f, ColumnDefaults.Value("1.5", typeof(float)));
            A.Eq("it's", ColumnDefaults.Value(SqlGen.Literal("it's"), typeof(string)));
            A.True(ColumnDefaults.Value("NULL", typeof(int)) is DBNull, "NULL value");
            A.Contains(ColumnDefaults.Query("creature_template_addon"), "TABLE_NAME = 'creature_template_addon'");
        }

        [Test] static void Keira_tables_are_on_the_forms()
        {
            var cr = EntityDefs.Creature;
            foreach (var t in new[] { "creature_template_addon", "creature_template_movement", "creature_onkill_reputation" }) A.True(cr.SideTables.ContainsKey(t), t);
            A.Eq("CreatureId", cr.SideTables["creature_template_movement"]);
            A.True(cr.Tabs.Contains("Look & movement") && cr.Tabs.Contains("Extras"), "creature tabs");
            A.Eq("creaturedisplay", cr.Fields.First(f => f.Column == "mount").RefKind);
            var q = EntityDefs.Quest;
            A.True(q.SideTables.ContainsKey("quest_offer_reward") && q.SideTables.ContainsKey("quest_request_items"), "quest text tables");
            A.Eq(FieldKind.LongText, q.Fields.First(f => f.Column == "RewardText").Kind);
            A.True(EntityDefs.Item.Tabs.Contains("Contains & enchants"), "item tab");
            A.Eq("broadcast_text", EntityDefs.BroadcastText.Table); A.Eq("game_tele", EntityDefs.GameTele.Table);
            A.Eq("map", EntityDefs.GameTele.Fields.First(f => f.Column == "map").RefKind);
        }

        // ---------------------------------------------------------------- admin: scripts
        [Test] static void Sql_batches_split_like_the_mysql_client()
        {
            var s = SqlBatch.Split("-- header\nSET NAMES utf8mb4;\nINSERT INTO t VALUES ('a;b', \"c;d\"); /* x; y */\n# note\nUPDATE t SET a = 1;\n");
            A.Eq(3, s.Count); A.Contains(s[1], "'a;b'"); A.True(s[2].EndsWith("a = 1"), s[2]);
            var p = SqlBatch.Split("CREATE TABLE a (x INT);\nDELIMITER //\nCREATE PROCEDURE p() BEGIN SELECT 1; SELECT 2; END//\nDELIMITER ;\nSELECT 3;");
            A.Eq(3, p.Count, string.Join(" | ", p)); A.Contains(p[1], "SELECT 1; SELECT 2; END");
            A.Eq(0, SqlBatch.Split("-- only a comment\n/* and another */").Count);
            var order = SqlBatch.FolderOrder(new[] { @"c:\x\2023_02_01_00.sql", @"c:\x\readme.txt", @"c:\x\2022_12_31_05.sql", @"c:\x\2023_01_15_00.SQL" });
            A.Eq(3, order.Count); A.True(order[0].EndsWith("2022_12_31_05.sql") && order[2].EndsWith("2023_02_01_00.sql"), "name order");
        }

        // ---------------------------------------------------------------- admin: builders
        [Test] static void Admin_statements_are_built_safely()
        {
            A.Eq("CREATE DATABASE IF NOT EXISTS `acore_world` DEFAULT CHARACTER SET utf8mb4;", AdminSql.CreateDatabase("acore_world"));
            A.Eq("DROP DATABASE `x``y`;", AdminSql.DropDatabase("x`y"), "names are quoted");
            var cu = AdminSql.CreateUser("acore", "localhost", "s3cr'et");
            A.Eq("CREATE USER IF NOT EXISTS 'acore'@'localhost' IDENTIFIED BY 's3cr\\'et';", cu);
            A.Eq("CREATE USER IF NOT EXISTS 'acore'@'localhost' IDENTIFIED BY '********';", AdminSql.Mask(cu), "password masked");
            A.Eq("ALTER USER 'u'@'%' IDENTIFIED BY '********';", AdminSql.Mask(AdminSql.SetPassword("u", "", "pw")));
            A.True(!AdminSql.Mask("SET PASSWORD FOR 'u'@'h' = PASSWORD('abc');").Contains("abc"), "old syntax masked too");
            A.Eq("GRANT SELECT, INSERT ON `acore_world`.* TO 'studio_rw'@'localhost';", AdminSql.Grant(new[] { "select", " insert " }, "acore_world", "*", "studio_rw", "localhost"));
            A.Eq("GRANT ALL PRIVILEGES ON *.* TO 'a'@'%' WITH GRANT OPTION;", AdminSql.Grant(new[] { "ALL PRIVILEGES" }, "*", null, "a", "%", true));
            A.Eq("REVOKE DELETE ON `w`.`t` FROM 'a'@'h';", AdminSql.Revoke(new[] { "DELETE" }, "w", "t", "a", "h"));
            A.Throws(() => AdminSql.Grant(new[] { "SELECT; DROP DATABASE x" }, "w", "*", "a", "h"), "unknown privilege refused");
            A.Throws(() => AdminSql.Grant(new string[0], "w", "*", "a", "h"), "empty");
            A.Eq("KILL 42;", AdminSql.Kill(42));
        }

        [Test] static void Admin_structure_statements()
        {
            var c = new AdminSql.ColumnSpec { Name = "Chance", Type = "float", Default = "100", Comment = "percent" };
            A.Eq("`Chance` FLOAT NOT NULL DEFAULT 100 COMMENT 'percent'", AdminSql.ColumnDef(c));
            A.Eq("`name` VARCHAR(100) NULL DEFAULT 'x''s'".Replace("''", "\\'"), AdminSql.ColumnDef(new AdminSql.ColumnSpec { Name = "name", Type = "varchar(100)", NotNull = false, Default = "x's" }));
            A.Throws(() => AdminSql.ColumnDef(new AdminSql.ColumnSpec { Name = "a", Type = "INT; DROP TABLE x" }), "type checked");
            var ct = AdminSql.CreateTable("my_table", new[] { new AdminSql.ColumnSpec { Name = "id", Type = "INT", Unsigned = true, AutoIncrement = true, PrimaryKey = true }, new AdminSql.ColumnSpec { Name = "v", Type = "TEXT", NotNull = false } });
            A.Eq("CREATE TABLE `my_table` (\n  `id` INT UNSIGNED NOT NULL AUTO_INCREMENT,\n  `v` TEXT NULL,\n  PRIMARY KEY (`id`)\n) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;", ct);
            A.Throws(() => AdminSql.CreateTable("t", new[] { new AdminSql.ColumnSpec { Name = "a" }, new AdminSql.ColumnSpec { Name = "A" } }), "duplicate column");
            A.Eq("ALTER TABLE `t` ADD COLUMN `x` INT NOT NULL AFTER `id`;", AdminSql.AddColumn("t", new AdminSql.ColumnSpec { Name = "x" }, "id"));
            A.Eq("ALTER TABLE `t` ADD COLUMN `x` INT NOT NULL FIRST;", AdminSql.AddColumn("t", new AdminSql.ColumnSpec { Name = "x" }, "FIRST"));
            A.Eq("ALTER TABLE `t` CHANGE COLUMN `a` `b` SMALLINT UNSIGNED NOT NULL;", AdminSql.ChangeColumn("t", "a", new AdminSql.ColumnSpec { Name = "b", Type = "smallint", Unsigned = true }));
            A.Eq("ALTER TABLE `t` ADD UNIQUE INDEX `u1` (`a`, `b`);", AdminSql.AddIndex("t", "u1", new[] { "a", "b" }, true));
            A.Eq("ALTER TABLE `t` DROP PRIMARY KEY;", AdminSql.DropIndex("t", "PRIMARY"));
            A.Eq("ALTER TABLE `t` DROP INDEX `u1`;", AdminSql.DropIndex("t", "u1"));
            A.Eq("CREATE OR REPLACE VIEW `bosses` AS SELECT entry FROM creature_template WHERE `rank` = 3;", AdminSql.CreateView("bosses", "SELECT entry FROM creature_template WHERE `rank` = 3;"));
            A.Throws(() => AdminSql.CreateView("v", "DELETE FROM t"), "views must be SELECT");
            A.Eq("RENAME TABLE `a` TO `b`;", AdminSql.RenameTable("a", "b"));
        }

        [Test] static void Admin_risk_and_confirmation()
        {
            A.Eq(AdminRisk.Read, AdminSql.Risk("SELECT 1"));
            A.Eq(AdminRisk.Read, AdminSql.Risk("-- c\nSHOW TABLES"));
            A.Eq(AdminRisk.Change, AdminSql.Risk("UPDATE t SET a = 1 WHERE id = 2"));
            A.Eq(AdminRisk.Destructive, AdminSql.Risk("UPDATE t SET a = 1"), "no WHERE");
            A.Eq(AdminRisk.Destructive, AdminSql.Risk("DELETE FROM t"));
            A.Eq(AdminRisk.Destructive, AdminSql.Risk("DROP TABLE t"));
            A.Eq(AdminRisk.Destructive, AdminSql.Risk("truncate t"));
            A.Eq(AdminRisk.Change, AdminSql.Risk("CREATE TABLE t (a INT)"));
            A.Eq(AdminRisk.Change, AdminSql.Risk("ALTER TABLE t ADD COLUMN b INT"));
            A.Eq(AdminRisk.Irreversible, AdminSql.Risk("CREATE USER 'a'@'%' IDENTIFIED BY 'x'"));
            A.Eq(AdminRisk.Irreversible, AdminSql.Risk("GRANT SELECT ON *.* TO 'a'@'%'"));
            A.Eq(AdminRisk.Irreversible, AdminSql.Risk("DROP USER 'a'@'%'"));
            A.Eq(AdminRisk.Irreversible, AdminSql.Risk("KILL 5"));
            A.Eq("creature", AdminSql.ConfirmWord(new[] { "SELECT 1", "DROP TABLE IF EXISTS `creature`" }), "the object's name");
            A.Eq("acore_world", AdminSql.ConfirmWord(new[] { "DROP DATABASE acore_world" }));
            A.Eq("RUN", AdminSql.ConfirmWord(new[] { "DROP TABLE a", "DROP TABLE b" }), "several: RUN");
            A.Eq("RUN", AdminSql.ConfirmWord(new[] { "KILL 4" }));
        }

        [Test] static void AzerothCore_setup_plan()
        {
            var p = new ServerSetup.Plan { Password = "pw", SqlRoot = @"C:\ac\data\sql" };
            var s = ServerSetup.Statements(p);
            A.Eq(3 + 1 + 3, s.Count, "3 databases, the account, 3 grants");
            A.Eq("CREATE DATABASE IF NOT EXISTS `acore_world` DEFAULT CHARACTER SET utf8mb4;", s[0]);
            A.Eq("GRANT ALL PRIVILEGES ON `acore_auth`.* TO 'acore'@'localhost';", s[6]);
            A.True(!s.Any(x => AdminSql.Mask(x).Contains("'pw'")), "masked");
            var f = ServerSetup.BaseFolders(p).ToList();
            A.Eq(@"C:\ac\data\sql\base\db_auth", f[0].folder); A.Eq("acore_auth", f[0].db); A.Eq("acore_world", f[2].db);
            A.Throws(() => ServerSetup.Statements(new ServerSetup.Plan { Password = "" }), "needs a password when creating the account");
            A.Eq(3, ServerSetup.Statements(new ServerSetup.Plan { CreateUser = false }).Count, "databases only");
            A.Throws(() => ServerSetup.Statements(new ServerSetup.Plan { World = "a;DROP", CreateUser = false }), "bad name");
            A.Contains(ServerSetup.ConfLines(p), "WorldDatabaseInfo     = \"127.0.0.1;3306;acore;<password>;acore_world\"");
            A.Eq("acore_world_2026-11-20_120000.sql", DatabaseDump.FileName("acore_world", 0, new DateTime(2026, 11, 20, 12, 0, 0)));
            var sw = new StringWriter(); DatabaseDump.WriteHeader(sw, "w", true);
            A.Contains(sw.ToString(), "CREATE DATABASE IF NOT EXISTS `w`"); A.Contains(sw.ToString(), "USE `w`;");
        }
    }
}
