using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AndersCraftStudio.Core;

namespace AndersCraftStudio.Tests
{
    /// <summary>0.7: game object / NPC text / page text editors, CSV import into database tables, item model import.</summary>
    static class M7Tests
    {
        static bool Have => Directory.Exists(DbcRoundTrip.DefaultDir);

        // ---------------------------------------------------------------- game object type data
        [Test] static void GameObject_data_is_labelled_per_type()
        {
            var data1 = EntityDefs.GameObject.Fields.First(f => f.Column == "Data1");
            var chestLoot = GameObjectData.Relabel(data1, 3);
            A.Eq("Loot table", chestLoot.Label); A.Eq(FieldKind.Int, chestLoot.Kind); A.Eq("Type data", chestLoot.Tab);
            A.Eq("Chest (lootable)", chestLoot.Group);
            var doorLock = GameObjectData.Relabel(data1, 0);
            A.Eq("Lock", doorLock.Label);
            var data0 = EntityDefs.GameObject.Fields.First(f => f.Column == "Data0");
            A.Eq(FieldKind.Bool, GameObjectData.Relabel(data0, 0).Kind, "door: starts open is a switch");
            A.Eq(FieldKind.Ref, GameObjectData.Relabel(data0, 9).Kind, "text: page is a link");
            A.Eq("pagetext", GameObjectData.Relabel(data0, 9).RefKind);
            A.Eq("quest", GameObjectData.Relabel(EntityDefs.GameObject.Fields.First(f => f.Column == "Data8"), 3).RefKind);
            A.Eq("spell", GameObjectData.Relabel(EntityDefs.GameObject.Fields.First(f => f.Column == "Data3"), 6).RefKind, "trap spell");
            A.True(GameObjectData.Relabel(EntityDefs.GameObject.Fields.First(f => f.Column == "Data20"), 3) == null, "chests don't use Data20");
            A.True(GameObjectData.Relabel(data0, 19) == null, "mailbox uses no data");
            var name = EntityDefs.GameObject.Fields.First(f => f.Column == "name");
            A.True(ReferenceEquals(name, GameObjectData.Relabel(name, 3)), "other fields unchanged");
            A.Eq(1, GameObjectData.LootSlot(3)); A.Eq(1, GameObjectData.LootSlot(25)); A.Eq(-1, GameObjectData.LootSlot(0));
            A.Eq(0, GameObjectData.PageSlot(9)); A.Eq(7, GameObjectData.PageSlot(10)); A.Eq(-1, GameObjectData.PageSlot(3));
        }

        [Test] static void GameObject_type_table_is_complete()
        {
            var known = new HashSet<string> { "spell", "quest", "creature", "gameobject", "pagetext", "map", "area" };
            for (long t = 0; t < 36; t++)
            {
                A.True(GameObjectData.Types.ContainsKey(t), "type " + t + " named");
                foreach (var s in GameObjectData.SlotsOf(t))
                {
                    A.True(s.Index >= 0 && s.Index < EntityDefs.GameObjectDataSlots, $"type {t} slot {s.Index} in range");
                    A.True(!string.IsNullOrWhiteSpace(s.Label) && !string.IsNullOrWhiteSpace(s.Guide), $"type {t} slot {s.Index} has words");
                    A.True(s.RefKind == null || known.Contains(s.RefKind), $"type {t} slot {s.Index} ref kind {s.RefKind}");
                }
            }
            A.Eq(17, GameObjectData.SlotsOf(3).Count(), "chest uses Data0-16");
            A.Eq(21, GameObjectData.SlotsOf(10).Count(), "goober uses Data0-20");
            A.Eq(EntityDefs.GameObjectDataSlots, EntityDefs.GameObject.Fields.Count(f => f.Column.StartsWith("Data")));
            A.Contains(EntityDefs.GameObject.ListExtra, "'Chest (lootable)'");
        }

        [Test] static void New_editors_are_registered()
        {
            var keys = EntityDefs.All.Select(e => e.Key).ToList();
            foreach (var k in new[] { "gameobjects", "npctext", "pagetext" }) A.True(keys.Contains(k), k);
            A.Eq(keys.Count, keys.Distinct().Count(), "unique keys");
            var nt = EntityDefs.NpcText;
            A.Eq(8 * 11, nt.Fields.Count, "8 variants × (2 texts, chance, language, broadcast text, 3 emotes + 3 delays)");
            A.Eq(FieldKind.Enum, nt.Fields.First(f => f.Column == "em3_1").Kind, "odd em columns are the emote");
            A.Eq(FieldKind.Int, nt.Fields.First(f => f.Column == "em3_0").Kind, "even em columns are the delay");
            A.Eq("pagetext", EntityDefs.PageText.Fields.First(f => f.Column == "NextPageID").RefKind);
            A.Eq("pagetext", EntityDefs.Item.Fields.First(f => f.Column == "PageText").RefKind, "items link to their page");
            A.Eq("entry", EntityDefs.GameObject.SideTables["gameobject_template_addon"]);
        }

        // ---------------------------------------------------------------- CSV → table
        static readonly List<CsvTableImport.Column> Cols = new List<CsvTableImport.Column>
        {
            new CsvTableImport.Column("entry", typeof(uint), false), new CsvTableImport.Column("name", typeof(string), false),
            new CsvTableImport.Column("Chance", typeof(float), false), new CsvTableImport.Column("note", typeof(string), true),
        };

        static Dictionary<string, IDictionary<string, object>> Existing() => new Dictionary<string, IDictionary<string, object>>
        {
            [CsvTableImport.KeyText(new[] { "10" })] = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { ["entry"] = 10u, ["name"] = "Old", ["Chance"] = 1.5f, ["note"] = DBNull.Value },
            [CsvTableImport.KeyText(new[] { "11" })] = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { ["entry"] = 11u, ["name"] = "Same", ["Chance"] = 2f, ["note"] = "x" },
        };

        static List<List<string>> Csv(string text) => DbcCsv.Parse(text);

        [Test] static void Csv_import_maps_columns_by_name()
        {
            var m = CsvTableImport.AutoMap(new[] { "ENTRY", "Name", "unknown", "`chance`", "name" }, Cols);
            A.Eq("entry", m[0]); A.Eq("name", m[1]); A.True(!m.ContainsKey(2), "unknown skipped"); A.Eq("Chance", m[3]); A.True(!m.ContainsKey(4), "a column is mapped once");
        }

        [Test] static void Csv_import_stages_inserts_and_changed_cells_only()
        {
            var all = Csv("entry,name,Chance,note\n10,New name,1.50,\n11,Same,2,x\n12,Brand new,3,NULL\n\n");
            var map = CsvTableImport.AutoMap(all[0], Cols);
            var cs = new Changeset();
            var r = CsvTableImport.Stage(cs, "t", Cols, new[] { "entry" }, all.Skip(1).ToList(), map, Existing(), CsvTableImport.Mode.AddAndUpdate);
            A.True(r.Ok, string.Join("; ", r.Errors));
            A.Eq(1, r.Added); A.Eq(1, r.Updated); A.Eq(1, r.Unchanged); A.Eq(1, r.CellsChanged, "1.50 equals 1.5; empty note left alone"); A.Eq(1, r.Skipped, "blank line");
            var up = cs.Changes.Single(c => c.Kind == ChangeKind.Update);
            A.Eq("UPDATE `t` SET `name` = 'New name' WHERE `entry` = 10 LIMIT 1;", up.ForwardSql);
            A.Eq("UPDATE `t` SET `name` = 'Old' WHERE `entry` = 10 LIMIT 1;", up.UndoSql);
            var ins = cs.Changes.Single(c => c.Kind == ChangeKind.Insert);
            A.Eq("INSERT INTO `t` (`entry`, `name`, `Chance`, `note`) VALUES (12, 'Brand new', 3, NULL);", ins.ForwardSql);
            A.Eq("DELETE FROM `t` WHERE `entry` = 12 LIMIT 1;", ins.UndoSql);
            A.Contains(ins.Summary, "from CSV");
        }

        [Test] static void Csv_import_modes_and_restaging()
        {
            var rows = Csv("entry,name\n10,Changed\n13,New").ToList();
            var map = CsvTableImport.AutoMap(rows[0], Cols);
            var body = rows.Skip(1).ToList();
            var a = new Changeset();
            var r = CsvTableImport.Stage(a, "t", Cols, new[] { "entry" }, body, map, Existing(), CsvTableImport.Mode.AddOnly);
            A.Eq(1, r.Added); A.Eq(0, r.Updated); A.Eq(1, r.Skipped);
            var u = new Changeset();
            r = CsvTableImport.Stage(u, "t", Cols, new[] { "entry" }, body, map, Existing(), CsvTableImport.Mode.UpdateOnly);
            A.Eq(0, r.Added); A.Eq(1, r.Updated); A.Eq(1, r.Skipped);
            // importing the same file twice doesn't double anything
            var twice = new Changeset();
            CsvTableImport.Stage(twice, "t", Cols, new[] { "entry" }, body, map, Existing(), CsvTableImport.Mode.AddAndUpdate);
            r = CsvTableImport.Stage(twice, "t", Cols, new[] { "entry" }, body, map, Existing(), CsvTableImport.Mode.AddAndUpdate);
            A.Eq(2, twice.Changes.Count); A.Eq(1, r.Notes.Count, "second insert noted, not repeated");
        }

        [Test] static void Csv_import_reports_problems_and_stages_nothing()
        {
            var cs = new Changeset();
            var noKey = CsvTableImport.Stage(cs, "t", Cols, new[] { "entry" }, Csv("name\nx").Skip(1).ToList(), new Dictionary<int, string> { [0] = "name" }, Existing(), CsvTableImport.Mode.AddAndUpdate);
            A.True(!noKey.Ok && noKey.Errors[0].Contains("key column entry"), "key must be mapped");
            var all = Csv("entry,Chance\n10,abc\n,2\n14,1\n14,2");
            var bad = CsvTableImport.Stage(cs, "t", Cols, new[] { "entry" }, all.Skip(1).ToList(), CsvTableImport.AutoMap(all[0], Cols), Existing(), CsvTableImport.Mode.AddAndUpdate);
            A.True(bad.Errors.Any(e => e.StartsWith("Line 2, Chance")), "bad number with line: " + string.Join("; ", bad.Errors));
            A.True(bad.Errors.Any(e => e.StartsWith("Line 3") && e.Contains("key")), "empty key");
            A.True(bad.Errors.Any(e => e.StartsWith("Line 5") && e.Contains("twice")), "duplicate key");
            var none = CsvTableImport.Stage(new Changeset(), "t", Cols, new string[0], all.Skip(1).ToList(), CsvTableImport.AutoMap(all[0], Cols), Existing(), CsvTableImport.Mode.AddAndUpdate);
            A.True(!none.Ok && none.Errors[0].Contains("no primary key"), "tables without a key are refused");
        }

        [Test] static void Csv_import_reads_existing_rows_by_key()
        {
            var all = Csv("entry,name\n10,a\n12,b\nx,c");
            var keys = CsvTableImport.Keys(Cols, new[] { "entry" }, all.Skip(1).ToList(), CsvTableImport.AutoMap(all[0], Cols));
            A.Eq(2, keys.Count, "invalid key skipped");
            A.Eq("SELECT * FROM `t` WHERE `entry` IN (10, 12)", CsvTableImport.LookupSql("t", new[] { "entry" }, keys));
            A.Eq("SELECT * FROM `t` WHERE (`a` = 1 AND `b` = 'x') OR (`a` = 2 AND `b` = 'y')",
                 CsvTableImport.LookupSql("t", new[] { "a", "b" }, new List<IList<string>> { new[] { "1", "'x'" }, new[] { "2", "'y'" } }));
            A.True(CsvTableImport.LookupSql("t", new[] { "entry" }, new List<IList<string>>()) == null, "no keys, no query");
            A.True(SqlClassifier.ClassifyOne(CsvTableImport.LookupSql("t", new[] { "entry" }, keys)).Kind == StatementKind.Read, "lookup is a read");
        }

        // ---------------------------------------------------------------- item model → ItemDisplayInfo
        [Test] static void Item_model_row_points_at_the_new_model()
        {
            if (!Have) return;
            var idi = DbcTable.Load(Path.Combine(DbcRoundTrip.DefaultDir, "ItemDisplayInfo.dbc"));
            int F(string n) => idi.Layout.FieldByName(n);
            var r = AssetImport.ItemModelRow(idi, 1542, "My Blade!", "MyBladeSkin", "Weapon", "", 900000);
            A.True(r.V[0] >= 900000 && idi.Find(r.V[0]) == null, "free id");
            A.Eq("MyBlade.mdx", idi.FieldText(r, F("ModelName_1"))); A.Eq("", idi.FieldText(r, F("ModelName_2")));
            A.Eq("MyBladeSkin", idi.FieldText(r, F("ModelTexture_1")));
            A.Eq(idi.FieldText(idi.Find(1542), F("InventoryIcon_1")), idi.FieldText(r, F("InventoryIcon_1")), "icon kept");
            var sh = AssetImport.ItemModelRow(idi, 1542, "Pads", "PadsTex", "Shoulder", "INV_Shoulder_01", 900000);
            A.Eq("Pads.mdx", idi.FieldText(sh, F("ModelName_2")), "shoulders on both sides"); A.Eq("PadsTex", idi.FieldText(sh, F("ModelTexture_2")));
            A.Eq("INV_Shoulder_01", idi.FieldText(sh, F("InventoryIcon_1")));
            A.Eq(@"Item\ObjectComponents\Shield\", AssetImport.ItemModelFolder("Shield"));
            A.Eq(@"Item\ObjectComponents\Weapon\", AssetImport.ItemModelFolder("nonsense"));
            // the staged row shows up in the Items editor's display picker, by model name
            var cs = new Changeset();
            var wk = idi.Clone(); wk.Rows.Add(r); wk.InvalidateIndex();
            DbcStager.Restage(cs, idi, wk, "server");
            var names = new DbcNames(DbcRoundTrip.DefaultDir) { Pending = () => cs.Changes };
            A.Contains(names.Name("itemicon", r.V[0]), "· MyBlade");
            A.True(names.Search("itemicon", "MyBlade").Any(x => x.id == r.V[0]), "searchable by model name");
        }
    }
}
