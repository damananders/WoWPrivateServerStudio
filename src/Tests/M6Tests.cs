using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AndersCraftStudio.Core;

namespace AndersCraftStudio.Tests
{
    static class M6Tests
    {
        static bool Have => Directory.Exists(DbcRoundTrip.DefaultDir);

        // ---------------------------------------------------------------- templates (FakeDb)
        static FakeDb World()
        {
            var db = new FakeDb();
            db.OnQuery = sql =>
            {
                if (sql.Contains("COALESCE(MAX(")) return FakeDb.Table(new[] { "x" }, new object[] { 900000L });
                if (sql.Contains("FROM `creature_template` WHERE `entry` = 100"))
                    return FakeDb.Table(new[] { "entry", "name", "subname", "minlevel", "maxlevel", "HealthModifier", "DamageModifier", "lootid", "AIName", "ScriptName", "npcflag", "faction", "unit_flags", "gossip_menu_id" },
                        new object[] { 100L, "Hogger", "", 11L, 11L, 3.0f, 1.0f, 448L, "", "", 0L, 14L, 0L, 0L });
                if (sql.Contains("FROM `creature_template` WHERE `entry` = 200"))
                    return FakeDb.Table(new[] { "entry", "name", "subname", "minlevel", "maxlevel", "HealthModifier", "DamageModifier", "lootid", "AIName", "ScriptName", "npcflag", "faction", "unit_flags", "gossip_menu_id" },
                        new object[] { 200L, "Marshal", "", 20L, 20L, 1.0f, 1.0f, 0L, "SmartAI", "", 3L, 12L, 0L, 0L });
                if (sql.Contains("FROM `creature_template_model` WHERE `CreatureID` = 100"))
                    return FakeDb.Table(new[] { "CreatureID", "Idx", "CreatureDisplayID", "DisplayScale", "Probability" }, new object[] { 100L, 0L, 384L, 1.0f, 1.0f });
                if (sql.Contains("FROM item_template WHERE entry IN")) return FakeDb.Table(new[] { "entry" }, new object[] { 159L }, new object[] { 4540L });
                if (sql.Contains("TABLE_NAME = 'npc_vendor'")) return FakeDb.Table(new[] { "COLUMN_NAME" }, new object[] { "entry" }, new object[] { "slot" }, new object[] { "item" }, new object[] { "maxcount" }, new object[] { "incrtime" }, new object[] { "ExtendedCost" }, new object[] { "VerifiedBuild" });
                if (sql.Contains("FROM `quest_template` WHERE `ID` = 783"))
                    return FakeDb.Table(new[] { "ID", "LogTitle", "QuestLevel", "MinLevel", "LogDescription", "QuestDescription", "RewardMoney", "RewardNextQuest", "RequiredNpcOrGo1" },
                        new object[] { 783L, "A Threat Within", 1L, 1L, "x", "y", 0L, 7L, 6L });
                if (sql.Contains("FROM `quest_template_addon` WHERE `ID` = 783")) return FakeDb.Table(new[] { "ID", "PrevQuestID", "NextQuestID", "ExclusiveGroup" }, new object[] { 783L, 5L, 7L, 0L });
                if (sql.Contains("SELECT id FROM smart_scripts WHERE entryorguid = 200")) return FakeDb.Table(new[] { "id" }, new object[] { 0 }, new object[] { 1 });
                return FakeDb.Table(new[] { "x" });
            };
            return db;
        }

        static Dictionary<string, string> V(params (string k, string v)[] kv) => kv.ToDictionary(x => x.k, x => x.v);

        [Test] static void Template_clone_boss_inserts_copy_with_new_id_level_and_modifiers()
        {
            var cs = new Changeset();
            var res = Templates.Get("clone-boss").Build(V(("creature", "100"), ("name", ""), ("level", "60"), ("health", "2"), ("damage", "1.5")), World(), cs, 900000);
            A.Eq(2, res.Changes.Count, "template + model row");
            var c = res.Changes[0];
            A.Eq("creature_template", c.Table); A.Eq(ChangeKind.Insert, c.Kind);
            A.Contains(c.ForwardSql, "900000"); A.Contains(c.ForwardSql, "'Hogger (Empowered)'");
            A.Contains(c.ForwardSql, ", 60, 60, 6, 1.5,");
            A.Contains(c.UndoSql, "DELETE FROM `creature_template` WHERE `entry` = 900000");
            A.Contains(res.Changes[1].ForwardSql, "INSERT INTO `creature_template_model`");
            A.Contains(res.Changes[1].ForwardSql, "(900000, 0, 384");
            A.True(res.Notes.Any(n => n.Contains("Shares loot")), "loot note");
            A.Throws(() => Templates.Get("clone-boss").Build(V(("creature", "999"), ("level", "60"), ("health", "1"), ("damage", "1")), World(), new Changeset(), 900000), "missing creature");
            A.Throws(() => Templates.Get("clone-boss").Build(V(("creature", "100"), ("level", "abc"), ("health", "1"), ("damage", "1")), World(), new Changeset(), 900000), "bad level");
        }

        [Test] static void Template_scale_stages_updates_with_undo_and_skips_unchanged()
        {
            var cs = new Changeset();
            var res = Templates.Get("scale").Build(V(("creature", "100"), ("min", "11"), ("max", "13"), ("health", "1"), ("damage", "2")), World(), cs, 900000);
            A.Eq(1, res.Changes.Count, "one row edit merges all columns");
            var c = res.Changes[0];
            A.Eq(ChangeKind.Update, c.Kind);
            A.True(c.Fields.Any(f => f.Name == "maxlevel" && f.Before == "11" && f.After == "13"), "maxlevel");
            A.True(c.Fields.Any(f => f.Name == "DamageModifier" && f.After == "2"), "damage");
            A.False(c.Fields.Any(f => f.Name == "minlevel"), "min unchanged");
            A.Throws(() => Templates.Get("scale").Build(V(("creature", "100"), ("min", "20"), ("max", "10"), ("health", "1"), ("damage", "1")), World(), new Changeset(), 900000), "max < min");
        }

        [Test] static void Template_vendor_creates_npc_and_vendor_rows()
        {
            var cs = new Changeset();
            var res = Templates.Get("vendor").Build(V(("look", "100"), ("name", "Bob"), ("subname", "Goods"), ("level", "30"), ("faction", "35"), ("items", "159, 4540")), World(), cs, 900000);
            A.Eq(4, res.Changes.Count, "template + model + 2 vendor rows");
            A.Contains(res.Changes[0].ForwardSql, "'Bob'");
            A.Contains(res.Changes[0].ForwardSql, ", 129, 35,");   // npcflag gossip+vendor, faction
            var v = res.Changes.Where(c => c.Table == "npc_vendor").ToList();
            A.Eq(2, v.Count);
            A.Contains(v[1].ForwardSql, "(900000, 1, 4540, 0, 0, 0, 0)");
            A.Throws(() => Templates.Get("vendor").Build(V(("look", "100"), ("name", "Bob"), ("level", "30"), ("faction", "35"), ("items", "159, 777")), World(), new Changeset(), 900000), "unknown item");
        }

        [Test] static void Template_quest_copies_quest_and_adds_starter_ender_and_flag()
        {
            var cs = new Changeset();
            var res = Templates.Get("quest").Build(V(("base", "783"), ("title", "Rats!"), ("level", "5"), ("minlevel", "3"), ("objectives", "Kill rats"), ("details", "Hi $N"), ("money", "250"), ("giver", "100"), ("ender", "")), World(), cs, 900000);
            var q = res.Changes.First(c => c.Table == "quest_template");
            A.Contains(q.ForwardSql, "(900000, 'Rats!', 5, 3, 'Kill rats', 'Hi $N', 250, 0, 6)");
            A.Contains(res.Changes.First(c => c.Table == "quest_template_addon").ForwardSql, "(900000, 0, 0, 0)");
            A.Contains(res.Changes.First(c => c.Table == "creature_queststarter").ForwardSql, "(100, 900000)");
            A.Contains(res.Changes.First(c => c.Table == "creature_questender").ForwardSql, "(100, 900000)");
            var flag = res.Changes.First(c => c.Table == "creature_template" && c.Kind == ChangeKind.Update);
            A.True(flag.Fields.Any(f => f.Name == "npcflag" && f.After == "2"), "quest giver flag added");
        }

        [Test] static void Template_smart_caster_adds_row_after_existing_ids()
        {
            var cs = new Changeset();
            var res = Templates.Get("smart-caster").Build(V(("creature", "200"), ("spell", "133"), ("min", "8000"), ("max", "12000"), ("target", "its target")), World(), cs, 900000);
            A.Eq(1, res.Changes.Count, "already SmartAI: only the row");
            A.Contains(res.Changes[0].ForwardSql, "INSERT INTO `smart_scripts`");
            A.Eq("2", res.Changes[0].Key.First(k => k.Column == "id").Value);
            var res2 = Templates.Get("smart-caster").Build(V(("creature", "100"), ("spell", "133"), ("min", "8000"), ("max", "12000"), ("target", "itself")), World(), cs, 900000);
            A.True(res2.Changes.Any(c => c.Kind == ChangeKind.Update && c.Fields.Any(f => f.Name == "AIName" && f.After == "'SmartAI'")), "AIName set");
            A.True(ApplyEngine.Validate(cs.Changes, "w", new string[0]).Count == 0, "all are normal world changes");
        }

        [Test] static void Every_template_explains_itself()
        {
            A.True(Templates.All.Count >= 5, "at least 5");
            foreach (var t in Templates.All) { A.True(t.Explain.Length > 80 && t.What.Length > 20 && t.Inputs.Count > 0, t.Key); }
        }

        // ---------------------------------------------------------------- SQL tools
        [Test] static void Sql_script_stages_writes_skips_session_lines_and_refuses_grants()
        {
            var dump = "/*!40101 SET NAMES utf8 */;\nSET FOREIGN_KEY_CHECKS=0;\nLOCK TABLES `npc_vendor` WRITE;\nINSERT INTO npc_vendor VALUES (1,0,2,0,0,0,0);\nDELETE FROM creature WHERE guid = 5;\nUNLOCK TABLES;\nSELECT 1;\nSTART TRANSACTION;\nCOMMIT;";
            var p = SqlScript.Prepare(dump);
            A.Eq(2, p.Writes.Count); A.Eq(1, p.Reads); A.Eq(6, p.Skipped.Count); A.True(p.Ok, "ok");
            var bad = SqlScript.Prepare("INSERT INTO a VALUES (1); GRANT ALL ON *.* TO x;");
            A.False(bad.Ok, "grant refuses the file"); A.Eq(1, bad.Blocked.Count);
            A.Eq(1, SqlScript.Prepare("CREATE TABLE t (a INT);").Structure);
        }

        [Test] static void Sql_dump_and_find_text()
        {
            var sw = new StringWriter();
            SqlDump.WriteTable(sw, "t", "CREATE TABLE `t` (`a` int)", new[] { "a", "b" }, new[] { new object[] { 1, "x'y" }, new object[] { 2, null } }, 1);
            var s = sw.ToString().Replace("\r\n", "\n");
            A.Contains(s, "DROP TABLE IF EXISTS `t`;");
            A.Contains(s, "INSERT INTO `t` (`a`, `b`) VALUES\n(1, 'x\\'y');");
            A.Contains(s, "(2, NULL);");
            // MySQL reads the literal '\\%' as \% and LIKE then treats it as a literal percent sign
            A.Eq(@"CAST(`a` AS CHAR) LIKE '%50\\%%' OR CAST(`b` AS CHAR) LIKE '%50\\%%'",SqlDump.FindInAllColumns(new[] { "a", "b" }, "50%"));
            var h = new QueryHistory(Path.Combine(Path.GetTempPath(), "studio_hist_" + Guid.NewGuid().ToString("N") + ".json"));
            h.Add("w", "SELECT 1"); h.Add("w", "SELECT 2"); h.Add("w", "SELECT 1");
            A.Eq(2, h.Items.Count); A.Eq("SELECT 1", h.Items[0].sql);
        }

        // ---------------------------------------------------------------- BLP encoder / TGA
        static BgraImage Gradient(int w, int h, bool alpha)
        {
            var img = new BgraImage { Width = w, Height = h, Pixels = new byte[w * h * 4] };
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                int q = (y * w + x) * 4;
                img.Pixels[q] = (byte)(x * 255 / (w - 1)); img.Pixels[q + 1] = (byte)(y * 255 / (h - 1)); img.Pixels[q + 2] = 128;
                img.Pixels[q + 3] = alpha ? (byte)(x < w / 2 ? 255 : 0) : (byte)255;
            }
            return img;
        }

        static double MeanError(BgraImage a, BgraImage b, int channels = 3)
        {
            double sum = 0; int n = 0;
            for (int i = 0; i < a.Pixels.Length; i += 4) for (int c = 0; c < channels; c++) { sum += Math.Abs(a.Pixels[i + c] - b.Pixels[i + c]); n++; }
            return sum / n;
        }

        [Test] static void Blp_encoder_round_trips_every_format()
        {
            var src = Gradient(64, 64, true);
            foreach (var fmt in new[] { BlpFormat.Raw, BlpFormat.Dxt5, BlpFormat.Dxt1, BlpFormat.Palette })
            {
                var bytes = BlpEncoder.Encode(src, fmt);
                var back = Blp.Decode(bytes);
                A.Eq(64, back.Width, fmt + " width");
                double err = MeanError(src, back);
                A.True(err < (fmt == BlpFormat.Raw ? 0.01 : 10), $"{fmt} colour error {err:0.00}");
                if (fmt != BlpFormat.Dxt1) A.True(MeanError(src, back, 4) < 12, fmt + " alpha kept");
                A.Eq(1, (int)bytes[11], fmt + " has mips");
                A.Eq(7u, BitConverter.ToUInt32(bytes, 20 + 6 * 4) > 0 ? 7u : 0u, fmt + " 7 mip levels for 64x64");
            }
            A.Throws(() => BlpEncoder.Encode(Gradient(60, 64, false), BlpFormat.Dxt1), "not a power of two");
            var r = BlpEncoder.Resize(Gradient(200, 150, false), 64, 64);
            A.Eq(64, r.Width); A.Eq(64, r.Height);
        }

        [Test] static void Tga_reads_uncompressed_and_rle()
        {
            byte[] Hdr(byte type, int w, int h, byte bpp, byte desc) { var b = new byte[18]; b[2] = type; b[12] = (byte)w; b[14] = (byte)h; b[16] = bpp; b[17] = desc; return b; }
            // 2x1 BGR uncompressed, bottom-up
            var t1 = Hdr(2, 2, 1, 24, 0).Concat(new byte[] { 1, 2, 3, 4, 5, 6 }).ToArray();
            var i1 = Tga.Decode(t1);
            A.Eq((byte)1, i1.Pixels[0]); A.Eq((byte)6, i1.Pixels[6]); A.Eq((byte)255, i1.Pixels[3]);
            // RLE 32-bit: one run packet of 3 identical pixels, top-down
            var t2 = Hdr(10, 3, 1, 32, 0x20).Concat(new byte[] { 0x82, 10, 20, 30, 40 }).ToArray();
            var i2 = Tga.Decode(t2);
            A.True(i2.Pixels.SequenceEqual(new byte[] { 10, 20, 30, 40, 10, 20, 30, 40, 10, 20, 30, 40 }), "rle");
            A.Throws(() => Tga.Decode(new byte[5]), "garbage");
        }

        [Test] static void M2_header_info()
        {
            var b = new byte[0x200];
            System.Text.Encoding.ASCII.GetBytes("MD20").CopyTo(b, 0);
            BitConverter.GetBytes(264u).CopyTo(b, 4);
            BitConverter.GetBytes(5).CopyTo(b, 0x08); BitConverter.GetBytes(0x100).CopyTo(b, 0x0C);
            System.Text.Encoding.ASCII.GetBytes("Wolf\0").CopyTo(b, 0x100);
            BitConverter.GetBytes(1234).CopyTo(b, 0x3C); BitConverter.GetBytes(1).CopyTo(b, 0x44);
            BitConverter.GetBytes(1).CopyTo(b, 0x50); BitConverter.GetBytes(0x140).CopyTo(b, 0x54);
            BitConverter.GetBytes(0u).CopyTo(b, 0x140); BitConverter.GetBytes(8).CopyTo(b, 0x148); BitConverter.GetBytes(0x180).CopyTo(b, 0x14C);
            System.Text.Encoding.ASCII.GetBytes("Wolf.blp").CopyTo(b, 0x180);
            var m = M2Info.Read(b);
            A.Eq("Wolf", m.Name); A.Eq(264u, m.Version); A.Eq(1234, m.Vertices); A.Eq("Wolf.blp", m.Textures[0]);
            A.Throws(() => M2Info.Read(new byte[100]), "not md20");
        }

        // ---------------------------------------------------------------- asset rows
        [Test] static void Asset_rows_for_icon_item_model_and_sound()
        {
            if (!Have) return;
            var dir = DbcRoundTrip.DefaultDir;
            var icons = DbcTable.Load(Path.Combine(dir, "SpellIcon.dbc"));
            var ir = AssetImport.IconRow(icons, "INV My Icon!", 900000);
            A.True(ir.V[0] >= 900000 && icons.Find(ir.V[0]) == null, "free id");
            A.Eq("Interface\\Icons\\INVMyIcon", icons.FieldText(ir, 1));
            var idi = DbcTable.Load(Path.Combine(dir, "ItemDisplayInfo.dbc"));
            var dr = AssetImport.ItemDisplayRow(idi, 1542, "INV_Custom", 900000);
            A.Eq("INV_Custom", idi.FieldText(dr, idi.Layout.FieldByName("InventoryIcon_1")));
            A.Eq(idi.FieldText(idi.Find(1542), idi.Layout.FieldByName("ModelName_1")), idi.FieldText(dr, idi.Layout.FieldByName("ModelName_1")));
            var cmd = DbcTable.Load(Path.Combine(dir, "CreatureModelData.dbc"));
            var cdi = DbcTable.Load(Path.Combine(dir, "CreatureDisplayInfo.dbc"));
            var (m, d) = AssetImport.CreatureRows(cmd, cdi, 49, 49, "Dragonling", new[] { "DragonSkin.blp", "DragonEyes.png" }, 900000, 1.5f);
            A.Eq("Creature\\Dragonling\\Dragonling.mdx", cmd.FieldText(m, cmd.Layout.FieldByName("ModelName")));
            A.Eq(m.V[0].ToString(), cdi.FieldText(d, cdi.Layout.FieldByName("ModelID")));
            A.Eq("DragonSkin", cdi.FieldText(d, cdi.Layout.FieldByName("TextureVariation_1")));
            A.Eq("DragonEyes", cdi.FieldText(d, cdi.Layout.FieldByName("TextureVariation_2")));
            A.Eq("", cdi.FieldText(d, cdi.Layout.FieldByName("TextureVariation_3")));
            A.Eq("1.5", cdi.FieldText(d, cdi.Layout.FieldByName("CreatureModelScale")));
            var se = DbcTable.Load(Path.Combine(dir, "SoundEntries.dbc"));
            var sr = AssetImport.SoundRow(se, 3, "Boom", "Boom.wav", "Sound\\Custom", 900000);
            A.Eq("Boom.wav", se.FieldText(sr, se.Layout.FieldByName("File_1"))); A.Eq("", se.FieldText(sr, se.Layout.FieldByName("File_2")));
            A.Eq("Sound\\Custom", se.FieldText(sr, se.Layout.FieldByName("DirectoryBase")));
            // staged as normal DBC row patches
            var wk = icons.Clone(); wk.Rows.Add(ir); wk.InvalidateIndex();
            var cs = new Changeset(); DbcStager.Restage(cs, icons, wk, "server");
            A.Eq(1, cs.Changes.Count); A.Eq(ChangeKind.Insert, cs.Changes[0].Kind);
        }

        [Test] static void Asset_place_and_manifest_stay_in_the_work_folder()
        {
            var work = Path.Combine(Path.GetTempPath(), "studio_assets_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            try
            {
                var full = AssetImport.Place(work, AssetImport.IconPath("Test"), new byte[] { 1, 2 });
                A.True(File.Exists(full) && full.EndsWith("Interface\\Icons\\Test.blp"), "placed");
                A.Throws(() => AssetImport.Place(work, "..\\..\\evil.txt", new byte[1]), "escape refused");
                AssetImport.AddToManifest(work, new AssetRecord { Kind = "icon", ArchivePath = "Interface\\Icons\\Test.blp" });
                AssetImport.AddToManifest(work, new AssetRecord { Kind = "icon", ArchivePath = "Interface\\Icons\\Test.blp", Note = "again" });
                var m = AssetImport.ReadManifest(work);
                A.Eq(1, m.Count); A.Eq("again", m[0].Note);
                var collected = MpqWriter.CollectFolder(work).Select(x => x.path).ToList();
                A.True(collected.Contains("Interface\\Icons\\Test.blp"), "patch build picks it up");
            }
            finally { if (Directory.Exists(work)) Directory.Delete(work, true); }
        }

        // ---------------------------------------------------------------- DBC tools / spell package
        [Test] static void Dbc_find_replace_bulk_sql_and_compare()
        {
            var t = DbcTable.Read("Made_Up.dbc", (byte[])typeof(M4Tests).GetMethod("Sample", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, null));
            var all = Enumerable.Range(0, t.Rows.Count).ToList();
            var other = t.Clone();
            A.Eq(3, DbcEdit.Replace(t, all, Enumerable.Range(0, t.Layout.FieldCount), "name", "Title", false));
            A.Eq("Title2", t.FieldText(t.Rows[1], 1));
            A.Eq(3, DbcEdit.Bulk(t, all, 18, DbcEdit.Op.Multiply, "2", out var err)); A.True(err == null, "no error");
            A.Eq("3", t.FieldText(t.Rows[0], 18));
            A.Eq(0, DbcEdit.Bulk(t, all, 0, DbcEdit.Op.Set, "5", out err)); A.Contains(err, "ID");
            A.Eq(0, DbcEdit.Bulk(t, all, 1, DbcEdit.Op.Add, "5", out err)); A.Contains(err, "Text");
            var sql = DbcSql.Export(t, all);
            A.Contains(sql, "CREATE TABLE `made_up_dbc`"); A.Contains(sql, "`Field18` FLOAT"); A.Contains(sql, "PRIMARY KEY (`ID`)");
            A.Contains(sql, "(1, 'Title1', '', ");
            var diff = DbcSql.Compare(t, other);
            A.True(diff.Any(d => d.key == 1 && d.field == "Text1_enUS" && d.here == "Title1" && d.there == "Name1"), "text diff");
            A.Eq(6, diff.Count, "3 names + 3 floats");
        }

        [Test] static void Spell_package_round_trip_reuses_identical_related_rows()
        {
            if (!Have) return;
            var refs = SpellProbe.Refs(out var spells);
            var copies = new Dictionary<string, DbcTable>();
            DbcTable Rel(string f) { if (!copies.TryGetValue(f, out var x)) copies[f] = x = refs.Get(f)?.Clone(); return x; }
            var json = SpellPackage.Export(new SpellData(spells, spells.Find(133)), Rel);
            A.Contains(json, "\"format\": \"AndersCraftStudio.Spell\"");
            A.Contains(json, "SpellRange.dbc");
            var work = spells.Clone();
            var res = SpellPackage.Import(json, work, Rel, 900000, new Changeset());
            A.True(res.NewId >= 900000, "new id");
            var s = new SpellData(work, work.Find(res.NewId));
            A.Eq("Fireball", s.Name); A.Eq(spells.FieldText(spells.Find(133), spells.Layout.FieldByName("RangeIndex")), s.Text("RangeIndex"));
            A.Eq(1, res.TouchedFiles.Count, "only Spell.dbc: related rows exist already");
            // on a server where the range row differs, a new one is added and the spell points at it
            var range = Rel("SpellRange.dbc"); var rr = range.Find(s.U("RangeIndex"));
            range.TrySetField(rr, range.Layout.FieldByName("RangeMax_1"), "999", out _);
            var res2 = SpellPackage.Import(json, work, Rel, 900000, new Changeset());
            var s2 = new SpellData(work, work.Find(res2.NewId));
            A.True(s2.U("RangeIndex") >= 900000, "remapped to a new range row");
            A.True(res2.TouchedFiles.Contains("SpellRange.dbc"), "range touched");
            A.Throws(() => SpellPackage.Import("{\"format\":\"x\"}", work, Rel, 900000, new Changeset()), "wrong file");
        }

        [Test] static void Defaults_are_portable()
        {
            var st = new AppSettings();
            A.True(st.BackupRoot.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)), "backups under Documents");
            A.True(st.ChangesetsDir.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)), "under Documents");
            A.Contains(st.ChangesetsDir, "AndersCraft Studio");
            A.Eq(Path.GetDirectoryName(st.ChangesetsDir), st.Workspace());
            st.WorkspaceDir = "X:\\w"; A.Eq("X:\\w", st.Workspace());
        }
    }
}
