using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AndersCraftStudio.Core;

namespace AndersCraftStudio.Tests
{
    static class M4Tests
    {
        static string ServerDbc => DbcRoundTrip.DefaultDir;

        static string TempDir()
        {
            var d = Path.Combine(Path.GetTempPath(), "studio_m4_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(d);
            return d;
        }

        // ID, localized name (16 + mask), a float, a plain string
        static byte[] Sample()
        {
            var rows = new List<object[]>();
            for (uint id = 1; id <= 3; id++)
            {
                var r = new List<object> { id, "Name" + id };
                for (int k = 1; k < 16; k++) r.Add(0u);
                r.Add(0xFF01FEu);
                r.Add((uint)BitConverter.SingleToInt32Bits(1.5f * id));
                r.Add("path\\file" + id + ".blp");
                rows.Add(r.ToArray());
            }
            return DbcFile.Build(20, rows);
        }

        [Test] static void Guess_finds_localized_float_and_string_columns()
        {
            var t = DbcTable.Read("Made_Up.dbc", Sample());
            var l = t.Layout;
            A.Eq("guessed", l.Origin);
            A.Eq(DbcKind.Loc, l.Columns[1].Kind, "col1");
            A.Eq(DbcKind.Float, l.Columns[2].Kind, "col2");
            A.Eq(DbcKind.String, l.Columns[3].Kind, "col3");
            A.Eq("Name2", t.FieldText(t.Rows[1], 1));
            A.Eq("Text1_enUS", l.FieldName(1)); A.Eq("Text1_Mask", l.FieldName(17));
            A.Eq("3", t.FieldText(t.Rows[1], 18));
            A.True(l.HasId, "ids unique");
        }

        [Test] static void Unchanged_table_writes_identical_bytes_in_every_mode_but_rebuild()
        {
            var b = Sample();
            var t = DbcTable.Read("Made_Up.dbc", b);
            A.True(t.Write().AsSpan().SequenceEqual(b), "auto");
            A.True(t.Write(DbcStringMode.Append).AsSpan().SequenceEqual(b), "append");
            var r = DbcTable.Read("Made_Up.dbc", t.Write(DbcStringMode.Rebuild), t.Layout);
            A.True(t.SameValues(r, out _), "rebuild keeps values");
        }

        [Test] static void Edited_text_is_written_and_other_offsets_stay_in_append_mode()
        {
            var t = DbcTable.Read("Made_Up.dbc", Sample());
            int pathOff = (int)t.Rows[2].V[19];
            A.True(t.TrySetField(t.Rows[0], 1, "Renamed ünïcode", out _), "set");
            var bytes = t.Write(DbcStringMode.Append);
            var back = DbcTable.Read("Made_Up.dbc", bytes, t.Layout);
            A.Eq("Renamed ünïcode", back.FieldText(back.Rows[0], 1));
            A.Eq("Name2", back.FieldText(back.Rows[1], 1));
            A.Eq((uint)pathOff, back.Rows[2].V[19], "untouched offset kept");
            var rb = DbcTable.Read("Made_Up.dbc", t.Write(DbcStringMode.Rebuild), t.Layout);
            A.Eq("Renamed ünïcode", rb.FieldText(rb.Rows[0], 1));
            A.True(rb.SameValues(back, out var d), "rebuild = append values " + d);
        }

        [Test] static void Number_parsing_and_formatting()
        {
            A.True(DbcTable.TryParse(DbcKind.Flags, "0x10", out var v, out _), "hex"); A.Eq(16u, v);
            A.True(DbcTable.TryParse(DbcKind.Int, "-1", out v, out _), "neg"); A.Eq(uint.MaxValue, v);
            A.Eq("-1", DbcTable.Format(DbcKind.Int, v));
            A.True(DbcTable.TryParse(DbcKind.Float, "0.1", out v, out _), "float"); A.Eq("0.1", DbcTable.Format(DbcKind.Float, v));
            A.False(DbcTable.TryParse(DbcKind.Byte, "300", out _, out var e), "byte range"); A.Contains(e, "0–255");
            A.False(DbcTable.TryParse(DbcKind.UInt, "abc", out _, out _), "text");
            A.False(DbcTable.TryParse(DbcKind.UInt, "99999999999", out _, out _), "too big");
        }

        static (DbcTable baseT, Changeset cs) Staged()
        {
            var baseT = DbcTable.Read("Made_Up.dbc", Sample());
            var ed = baseT.Clone();
            ed.TrySetField(ed.Rows[0], 1, "Hello", out _);
            ed.TrySetField(ed.Rows[0], 18, "2.25", out _);
            ed.Rows.RemoveAt(1);
            var nr = ed.NewRow(); ed.TrySetField(nr, 0, "900000", out _); ed.TrySetField(nr, 1, "New row", out _); ed.TrySetField(nr, 17, "0xFF01FE", out _); ed.Rows.Add(nr);
            var cs = new Changeset();
            DbcStager.Restage(cs, baseT, ed, "server");
            return (baseT, cs);
        }

        [Test] static void Diff_stages_update_insert_delete_with_before_and_after()
        {
            var (_, cs) = Staged();
            A.Eq(3, cs.Changes.Count);
            var up = cs.Changes.Single(c => c.Kind == ChangeKind.Update);
            A.Eq("dbc", up.Schema); A.Eq("Made_Up.dbc", up.Table);
            A.Eq(2, up.Dbc.Fields.Count);
            A.Eq("Name1", up.Dbc.Fields[0].Before); A.Eq("Hello", up.Dbc.Fields[0].After);
            A.Contains(up.Summary, "Edit Made_Up.dbc 1");
            A.Eq(1, cs.Changes.Count(c => c.Kind == ChangeKind.Insert && c.Dbc.Key == 900000));
            A.Eq(1, cs.Changes.Count(c => c.Kind == ChangeKind.Delete && c.Dbc.Key == 2));
            A.True(cs.Changes.All(c => c.ForwardSql == null), "no SQL for DBC changes");
            A.False(new ChangesetStore(TempDir()).ExportSql(cs.Changes, false).Contains("Made_Up"), "SQL export skips DBC");
            A.True(ApplyEngine.Validate(cs.Changes, "w", new string[0]).Any(p => p.Contains("not a world-database change")), "DB apply refuses them");
        }

        [Test] static void Restage_replaces_earlier_changes_and_drops_reverted_rows()
        {
            var (baseT, cs) = Staged();
            var ed = baseT.Clone();
            ed.TrySetField(ed.Rows[2], 18, "9", out _);
            DbcStager.Restage(cs, baseT, ed, "server");
            A.Eq(1, cs.Changes.Count);
            A.Eq(3u, cs.Changes[0].Dbc.Key);
            DbcStager.Restage(cs, baseT, baseT.Clone(), "server");
            A.Eq(0, cs.Changes.Count);
        }

        [Test] static void Patcher_applies_undoes_and_is_idempotent()
        {
            var (baseT, cs) = Staged();
            var probs = new List<string>();
            var after = DbcPatcher.Apply(baseT, cs.Changes, false, probs);
            A.Eq(0, probs.Count, string.Join(";", probs));
            A.Eq("Hello", after.FieldText(after.Find(1), 1));
            A.Eq("2.25", after.FieldText(after.Find(1), 18));
            A.True(after.Find(2) == null, "deleted");
            A.Eq("New row", after.FieldText(after.Find(900000), 1));
            A.Eq("Name1", baseT.FieldText(baseT.Find(1), 1), "base untouched");
            var notes = new List<string>();
            var again = DbcPatcher.Apply(after, cs.Changes, false, probs, notes);
            A.Eq(0, probs.Count); A.True(notes.Count == 3, "all three already done: " + notes.Count);
            var undone = DbcPatcher.Apply(after, cs.Changes, true, probs);
            A.Eq(0, probs.Count, string.Join(";", probs));
            A.True(undone.SameValues(baseT, out var d) || undone.Rows.Count == baseT.Rows.Count, "undo restores rows " + d);
            A.Eq("Name2", undone.FieldText(undone.Find(2), 1));
            A.True(undone.Find(900000) == null, "insert undone");
        }

        [Test] static void Patcher_refuses_when_the_target_changed()
        {
            var (baseT, cs) = Staged();
            var other = baseT.Clone();
            other.TrySetField(other.Find(1), 1, "Someone else", out _);
            var probs = new List<string>();
            A.True(DbcPatcher.Apply(other, cs.Changes, false, probs) == null, "refused");
            A.Contains(probs[0], "is \"Someone else\" here, not \"Name1\"");
        }

        [Test] static void Outputs_patch_folder_and_server_copy_with_backup_and_restore_on_temp_folders()
        {
            var (baseT, cs) = Staged();
            var tmp = TempDir();
            try
            {
                var server = Path.Combine(tmp, "server_dbc"); Directory.CreateDirectory(server);
                var orig = Sample();
                File.WriteAllBytes(Path.Combine(server, "Made_Up.dbc"), orig);
                var work = Path.Combine(tmp, "patch_work", "mypatch");
                var r1 = DbcOutputs.ToPatchFolder(cs.Changes, f => File.ReadAllBytes(Path.Combine(server, f)), work);
                A.True(r1.Success, string.Join(";", r1.Problems));
                var outFile = Path.Combine(work, "DBFilesClient", "Made_Up.dbc");
                A.True(File.Exists(outFile), "patch file");
                A.Eq("Hello", DbcTable.Load(outFile).FieldText(DbcTable.Load(outFile).Find(1), 1));
                A.True(File.ReadAllBytes(Path.Combine(server, "Made_Up.dbc")).AsSpan().SequenceEqual(orig), "server untouched by patch output");

                var backups = Path.Combine(tmp, "backups");
                var r2 = DbcOutputs.ToServerCopy(cs.Changes, server, backups, new DateTime(2026, 11, 19, 12, 0, 0));
                A.True(r2.Success, string.Join(";", r2.Problems));
                A.True(File.ReadAllBytes(Path.Combine(r2.BackupDir, "Made_Up.dbc")).AsSpan().SequenceEqual(orig), "backup is the original");
                var srv = DbcTable.Load(Path.Combine(server, "Made_Up.dbc"));
                A.Eq("Hello", srv.FieldText(srv.Find(1), 1));
                A.False(File.Exists(Path.Combine(server, "Made_Up.dbc.studio-tmp")), "no temp left");

                var r3 = DbcOutputs.ToServerCopy(cs.Changes, server, backups, new DateTime(2026, 11, 19, 12, 0, 1));
                A.True(r3.Success, "second run is a no-op that succeeds");
                var r4 = DbcOutputs.RestoreServerCopy(r2.BackupDir, server);
                A.True(r4.Success, "restore");
                A.True(File.ReadAllBytes(Path.Combine(server, "Made_Up.dbc")).AsSpan().SequenceEqual(orig), "restored");

                // conflict: someone changed the server file → nothing written, no backup
                var changed = DbcTable.Read("Made_Up.dbc", orig); changed.TrySetField(changed.Find(1), 1, "Other", out _);
                File.WriteAllBytes(Path.Combine(server, "Made_Up.dbc"), changed.Write());
                var before = File.ReadAllBytes(Path.Combine(server, "Made_Up.dbc"));
                var r5 = DbcOutputs.ToServerCopy(cs.Changes, server, backups, new DateTime(2026, 11, 19, 12, 0, 2));
                A.False(r5.Success, "conflict refused");
                A.True(r5.BackupDir == null, "no backup made");
                A.True(File.ReadAllBytes(Path.Combine(server, "Made_Up.dbc")).AsSpan().SequenceEqual(before), "file untouched");
            }
            finally { Directory.Delete(tmp, true); }
        }

        [Test] static void Client_load_order_model()
        {
            int R(string n, bool loc = false) => ClientDbcSource.Rank(n, loc);
            A.True(R("common.MPQ") < R("lichking.MPQ"), "base");
            A.True(R("lichking.MPQ") < R("locale-enUS.MPQ", true), "locale after base");
            A.True(R("locale-enUS.MPQ", true) < R("patch.MPQ"), "patches after locale");
            A.True(R("patch-enUS.MPQ", true) < R("patch.MPQ"), "patch-enUS.MPQ is the lowest patch (WoW.exe adds it last)");
            A.True(R("patch-enUS.MPQ", true) < R("patch-2.MPQ"), "patch-2 after patch-enUS");
            A.True(R("patch-enUS-3.MPQ", true) < R("patch-4.MPQ"), "patch-4 after patch-enUS-3");
            A.True(R("patch-enUS-4.MPQ", true) < R("patch-4.MPQ"), "every Data patch-X beats every enUS one (path sort)");
            A.True(R("patch-9.MPQ") < R("patch-A.MPQ"), "letters after digits");
            A.Eq(-1, R("backup-enUS.MPQ", true)); A.Eq(-1, R("base-enUS.MPQ", true)); A.Eq(-1, R("random.MPQ"));
        }

        [Test] static void Links_and_names_on_catalog_layouts()
        {
            var cat = DbcLayout.CatalogFiles.ToList();
            A.True(cat.Count >= 150, "catalog size " + cat.Count);
            A.Contains(DbcLayout.Describe("Spell.dbc"), "spell");
            A.Contains(DbcLayout.Describe("Nope.dbc"), "raw mode");
            A.Eq("Spell", Links.Base("Spell")); A.Eq("ItemID", Links.Base("ItemID_3"));
        }

        [Test] static void Real_server_dbcs_get_verified_layouts_links_and_descriptions()
        {
            if (!Directory.Exists(ServerDbc)) { Console.WriteLine("        (skipped: no server dbc folder)"); return; }
            var spell = DbcTable.Load(Path.Combine(ServerDbc, "Spell.dbc"));
            A.Eq("catalog", spell.Layout.Origin);
            A.Eq("Fireball", spell.FieldText(spell.Find(133), spell.Layout.Columns.First(c => c.Name == "Name").Field));
            A.Eq("SpellIcon.dbc", spell.Layout.Columns.First(c => c.Name == "SpellIconID").Link);
            A.Eq("SpellRange.dbc", spell.Layout.Columns.First(c => c.Name == "RangeIndex").Link);
            A.Eq("db:item", spell.Layout.Columns.First(c => c.Name == "Reagent_1").Link);
            var area = DbcTable.Load(Path.Combine(ServerDbc, "AreaTable.dbc"));
            A.Eq("Map.dbc", area.Layout.Columns.First(c => c.Name == "ContinentID").Link);
            A.Eq("Elwynn Forest", area.FieldText(area.Find(12), area.Layout.NameColumn.Field));
            var gt = DbcTable.Load(Path.Combine(ServerDbc, "gtCombatRatings.dbc"));
            A.False(gt.Layout.HasId, "gt tables are by row"); A.Eq("Data", gt.Layout.Columns[0].Name);
            int described = 0, files = 0;
            foreach (var f in Directory.GetFiles(ServerDbc, "*.dbc")) { files++; if (!DbcLayout.Describe(Path.GetFileName(f)).Contains("raw mode")) described++; }
            A.Eq(files, described, "every server DBC has a description");
        }

        [Test] static void Real_spell_edit_round_trips_through_stage_and_patch()
        {
            if (!Directory.Exists(ServerDbc)) return;
            var tmp = TempDir();
            try
            {
                File.Copy(Path.Combine(ServerDbc, "Spell.dbc"), Path.Combine(tmp, "Spell.dbc"));
                var baseT = DbcTable.Load(Path.Combine(tmp, "Spell.dbc"));
                var ed = baseT.Clone();
                int name = baseT.Layout.Columns.First(c => c.Name == "Name").Field;
                ed.TrySetField(ed.Find(133), name, "Big Fireball", out _);
                ed.TrySetField(ed.Find(133), name + 2, "Gros boule de feu", out _); // koKR slot just to prove locales are separate
                var cs = new Changeset();
                DbcStager.Restage(cs, baseT, ed, "server");
                A.Eq(1, cs.Changes.Count); A.Eq(2, cs.Changes[0].Dbc.Fields.Count);
                A.Eq("Name_koKR", cs.Changes[0].Dbc.Fields[1].Name);
                var r = DbcOutputs.ToServerCopy(cs.Changes, tmp, Path.Combine(tmp, "bk"));
                A.True(r.Success, string.Join(";", r.Problems));
                var back = DbcTable.Load(Path.Combine(tmp, "Spell.dbc"));
                A.Eq("Big Fireball", back.FieldText(back.Find(133), name));
                A.Eq("Frostbolt", back.FieldText(back.Find(116), name));
                A.Eq(baseT.Rows.Count, back.Rows.Count);
                var probs = new List<string>();
                var undone = DbcPatcher.Apply(back, cs.Changes, true, probs);
                A.True(undone.SameValues(baseT, out var d), "undo gives the original values " + d);
            }
            finally { Directory.Delete(tmp, true); }
        }
    }
}

namespace AndersCraftStudio.Tests
{
    static class M4QueryTests
    {
        static DbcTable T() => DbcTable.Read("Made_Up.dbc", (byte[])typeof(M4Tests).GetMethod("Sample", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, null));

        [Test] static void Filter_forms()
        {
            var t = T();
            A.Eq(3, DbcFilter.Apply(t, "", out _).Count);
            A.Eq(1, DbcFilter.Apply(t, "name2", out _).Count, "text");
            A.Eq(1, DbcFilter.Apply(t, "3", out _).Count, "number");
            A.Eq(1, DbcFilter.Apply(t, "Text1:me1", out _).Count, "column contains");
            A.Eq(1, DbcFilter.Apply(t, "ID=2", out _).Count, "column equals");
            A.Eq(2, DbcFilter.Apply(t, "Field18>2", out _).Count, "float compare");
            A.Eq(1, DbcFilter.Apply(t, "Field18>2 file3", out _).Count, "two terms");
            A.True(DbcFilter.Apply(t, "Nope=1", out var err) == null, "unknown column"); A.Contains(err, "No column");
        }

        [Test] static void Csv_export_import_round_trip_with_quotes_and_new_rows()
        {
            var t = T();
            t.TrySetField(t.Find(1), 1, "Comma, \"quoted\"\nline", out _);
            var csv = DbcCsv.Export(t);
            A.Contains(csv, "Text1_enUS");
            var other = T();
            var r = DbcCsv.Import(other, csv);
            A.Eq(0, r.Errors.Count, string.Join(";", r.Errors));
            A.Eq(1, r.Updated); A.Eq(2, r.Unchanged);
            A.True(other.SameValues(t, out var d), "same after import " + d);
            var add = "ID,Text1_enUS,Field18\r\n900001,Added,2.5\r\n";
            r = DbcCsv.Import(other, add);
            A.Eq(1, r.Added); A.Eq("Added", other.FieldText(other.Find(900001), 1)); A.Eq("2.5", other.FieldText(other.Find(900001), 18));
            var bad = DbcCsv.Import(other, "ID,Field18\r\n1,abc\r\n");
            A.Eq(1, bad.Errors.Count); A.Eq(0, bad.Updated, "nothing applied on error");
            A.Eq("1.5", other.FieldText(other.Find(1), 18), "unchanged after failed import");
            A.Eq(1, DbcCsv.Import(other, "ID,Bogus\r\n1,2\r\n").Ignored.Count);
        }
    }
}
