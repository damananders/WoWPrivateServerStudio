using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using AndersCraftStudio.Core;

namespace AndersCraftStudio.Tests
{
    /// <summary>0.9: M2/.skin reading, meshes, animation; script rows without a key; several DBCs open.</summary>
    static class M9Tests
    {
        static ClientFiles client;
        static ClientFiles Client => client ??= Directory.Exists(DevPaths.ClientData) ? new ClientFiles(DevPaths.ClientData) : null;
        static byte[] Read(string p) => Client?.Read(p, out _);

        public static (M2Model m, M2Skin s) Load(string path)
        {
            var m = M2Model.Read(Read(path) ?? throw new FileNotFoundException(path));
            var s = M2Skin.Read(Read(M2Model.SkinPath(path)) ?? throw new FileNotFoundException(M2Model.SkinPath(path)));
            return (m, s);
        }

        [Test] static void Paths_for_skins_anims_and_items()
        {
            A.Eq(@"Creature\Wolf\Wolf00.skin", M2Model.SkinPath(@"Creature\Wolf\Wolf.m2"));
            A.Eq(@"Creature\Wolf\Wolf.m2", M2Model.M2Path("Creature/Wolf/Wolf.mdx"));
            A.Eq(@"Creature\Wolf\Wolf0004-01.anim", M2Model.AnimPath(@"Creature\Wolf\Wolf.m2", new M2Model.Sequence { Id = 4, Variation = 1 }));
            var paths = ItemModels.ModelPaths("Helm_Plate_D_01.mdx", 1).ToList();
            A.Eq(@"Item\ObjectComponents\Head\Helm_Plate_D_01_HuM.m2", paths[0]);
            A.Eq(@"Item\ObjectComponents\Weapon\Sword_2H_A_01.m2", ItemModels.ModelPaths("Sword_2H_A_01.mdx", 17).First());
            A.True(M2Mesh.DefaultGeoset(0) && M2Mesh.DefaultGeoset(101) && M2Mesh.DefaultGeoset(702) && !M2Mesh.DefaultGeoset(102) && !M2Mesh.DefaultGeoset(1501), "character geosets");
            var t = new M2Model.Texture { Type = M2Textures.Monster1 };
            A.Eq(@"Creature\Wolf\WolfSkinGrey.blp", M2Textures.Resolve(t, @"Creature\Wolf\Wolf.m2", new[] { "WolfSkinGrey" }));
            A.Eq(null, M2Textures.Resolve(new M2Model.Texture { Type = M2Textures.Monster2 }, @"Creature\Wolf\Wolf.m2", new[] { "A" }), "missing variation");
            A.Eq(@"Item\ObjectComponents\Weapon\Sword_Blue.blp", M2Textures.Resolve(new M2Model.Texture { Type = M2Textures.ObjectSkin }, @"Item\ObjectComponents\Weapon\Sword.m2", null, "Sword_Blue"));
        }

        // ---------------------------------------------------------------- tables without a key
        [Test] static void Keyless_rows_are_matched_on_every_column()
        {
            A.Eq("`a` = 1 AND ABS(`x` - 0.1) < 0.0005 AND `s` = 'p.q' AND `n` IS NULL",
                 SqlGen.WhereKey(new[] { new KeyPart("a", "1"), new KeyPart("x", "0.1"), new KeyPart("s", "'p.q'"), new KeyPart("n", "NULL") }));
            A.True(SqlGen.IsFraction("1.5") && SqlGen.IsFraction("-2E-05") && !SqlGen.IsFraction("15") && !SqlGen.IsFraction("'1.5'") && !SqlGen.IsFraction("X'0A'"), "fractions");
            var cols = new[] { "id", "delay", "command", "x" };
            var before = new List<RowValues> { new RowValues().Set("id", "5").Set("delay", "0").Set("command", "10").Set("x", "1.25"), new RowValues().Set("id", "5").Set("delay", "2").Set("command", "15").Set("x", "0") };
            var after = new List<RowValues> { before[0].Clone().Set("delay", "3"), before[1].Clone() };
            var cs = new Changeset();
            RowDiff.Stage(cs, "event_scripts", cols, cols, before, after);
            A.Eq(2, cs.Changes.Count, "a changed row = remove + add");
            var del = cs.Changes.Single(c => c.Kind == ChangeKind.Delete);
            A.Eq("DELETE FROM `event_scripts` WHERE `id` = 5 AND `delay` = 0 AND `command` = 10 AND ABS(`x` - 1.25) < 0.0005 LIMIT 1;", del.ForwardSql);
            A.Contains(cs.Changes.Single(c => c.Kind == ChangeKind.Insert).ForwardSql, "VALUES (5, 3, 10, 1.25)");
        }

        // ---------------------------------------------------------------- several DBCs open
        [Test] static void Several_tables_stay_open_in_tabs()
        {
            var t = new OpenTabs<string>(3, s => s == "dirty");
            t.Put("Spell.dbc", "clean"); t.Put("Map.dbc", "dirty"); t.Put("Faction.dbc", "clean");
            A.Eq(3, t.Names.Count); A.Eq("Faction.dbc", t.Active);
            var closed = t.Put("Item.dbc", "clean");
            A.Eq("Spell.dbc", closed.Single(), "the oldest clean tab closes first");
            A.True(t.Contains("Map.dbc"), "tabs with unstaged edits stay");
            t.Put("Talent.dbc", "clean");
            A.True(t.Contains("Map.dbc") && t.Names.Count == 3, "still kept");
            A.Eq("Item.dbc", t.Close("Talent.dbc"), "closing the active tab shows its neighbour");
            t.DropClean();
            A.True(t.Contains("Item.dbc") && !t.HasState("Item.dbc") && t.HasState("Map.dbc"), "clean tabs reload, dirty ones keep their edits");
            A.True(t.TryGet("Map.dbc", out var m) && m == "dirty", "dirty state kept");
        }

        [Test] static void Rejects_files_that_are_not_models()
        {
            A.Throws(() => M2Model.Read(new byte[0x200]), "no MD20");
            var bad = new byte[0x200]; System.Text.Encoding.ASCII.GetBytes("MD20").CopyTo(bad, 0); BitConverter.GetBytes(256u).CopyTo(bad, 4);
            A.Throws(() => M2Model.Read(bad), "wrong version");
            A.Throws(() => M2Skin.Read(new byte[10]), "no SKIN");
        }

        [Test] static void Real_creature_model_wolf()
        {
            if (Client == null) return;
            var (m, s) = Load(@"Creature\Wolf\Wolf.m2");
            A.Eq(264u, m.Version);
            A.True(m.Vertices.Length > 300, "vertices " + m.Vertices.Length);
            A.True(m.Bones.Count > 10, "bones " + m.Bones.Count);
            A.True(m.Sequences.Count > 5, "sequences " + m.Sequences.Count);
            A.True(m.Textures.Any(t => t.Type == M2Textures.Monster1), "creature skin texture");
            var parts = M2Mesh.Build(m, s);
            A.True(parts.Count > 0 && parts.Sum(p => p.Triangles.Length) / 3 > 200, "triangles");
            A.True(parts.All(p => p.Triangles.All(i => i >= 0 && i < p.Vertices.Length)), "indices in range");
            // rest pose: bones are identity
            var rest = m.Pose(-1, 0);
            A.True(rest.All(x => x == Matrix4x4.Identity), "rest pose");
            // the stand animation (id 0) is embedded and moves at least one vertex
            int stand = m.Sequences.FindIndex(q => q.Id == 0);
            A.True(stand >= 0, "has Stand");
            if (m.NeedsAnimFile(stand)) m.LoadAnimFile(stand, Read(M2Model.AnimPath(@"Creature\Wolf\Wolf.m2", m.Sequences[stand])));
            var seq = m.Sequences[stand];
            var pose = m.Pose(stand, seq.Duration / 2);
            A.True(pose.All(x => !float.IsNaN(x.M11) && !float.IsNaN(x.M41)), "finite");
            int moved = m.Vertices.Count(v => Vector3.Distance(M2Model.Skin(v, pose).pos, v.Pos) > 0.001f);
            A.True(moved > 0, "Stand moves the model");
            // walk (id 4)
            int walk = m.Sequences.FindIndex(q => q.Id == 4);
            if (walk >= 0)
            {
                if (m.NeedsAnimFile(walk)) m.LoadAnimFile(walk, Read(M2Model.AnimPath(@"Creature\Wolf\Wolf.m2", m.Sequences[walk])));
                var a = m.Pose(walk, 0); var b = m.Pose(walk, m.Sequences[walk].Duration / 2);
                A.True(m.Vertices.Any(v => Vector3.Distance(M2Model.Skin(v, a).pos, M2Model.Skin(v, b).pos) > 0.01f), "Walk changes over time");
            }
        }

        [Test] static void Real_item_and_object_models()
        {
            if (Client == null) return;
            foreach (var path in new[] { @"Item\ObjectComponents\Weapon\Sword_1H_Short_A_01.m2", @"World\Generic\Human\Passive Doodads\Barrel\Barrel01.m2" })
            {
                var bytes = Read(path);
                if (bytes == null) continue;
                var m = M2Model.Read(bytes);
                var s = M2Skin.Read(Read(M2Model.SkinPath(path)));
                var parts = M2Mesh.Build(m, s);
                A.True(parts.Count > 0, path + " has pieces");
                A.True(parts.Any(p => p.TextureIndex >= 0 && p.TextureIndex < m.Textures.Count), path + " has a texture");
                var tex = parts.Select(p => p.TextureIndex).Where(i => i >= 0 && i < m.Textures.Count).Select(i => m.Textures[i]).FirstOrDefault(t => t.Type == 0);
                if (tex != null) A.True(Read(tex.File) != null, "texture file is in the client: " + tex.File);
            }
        }
    }
}
