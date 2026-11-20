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

        // ---------------------------------------------------------------- WMO buildings
        public static string FirstWmo()
        {
            var p = Path.Combine(DbcRoundTrip.DefaultDir, "GameObjectDisplayInfo.dbc");
            if (!File.Exists(p)) return null;
            var f = DbcFile.Load(p);
            for (int i = 0; i < f.Records; i++) { var s = f.Str(i, 1); if (s != null && s.EndsWith(".wmo", StringComparison.OrdinalIgnoreCase) && Read(s) != null) return s; }
            return null;
        }

        [Test] static void Real_wmo_building()
        {
            if (Client == null) return;
            var path = FirstWmo();
            A.True(path != null, "a game object uses a WMO");
            var w = Wmo.Load(path, Read, 64);
            A.True(w.GroupCount > 0 && w.Groups.Count > 0, $"{path}: groups {w.Groups.Count}/{w.GroupCount}");
            A.True(w.Triangles > 10, "triangles " + w.Triangles);
            A.True(w.Materials.Count > 0 && w.Materials.Any(m => m.Texture != null && Read(m.Texture) != null), "a material texture is in the client");
            foreach (var g in w.Groups) foreach (var b in g.Batches) A.True(b.StartIndex + b.Count <= g.Indices.Length, "batch in range");
            A.True(w.Groups.All(g => g.Indices.All(i => i < g.Positions.Length)), "indices point at vertices");
            A.Eq(@"World\wmo\X\Barn_002.wmo", Wmo.GroupPath(@"World\wmo\X\Barn.wmo", 2));
            A.Throws(() => Wmo.ReadRoot(new byte[4]), "not a WMO");
        }

        // ---------------------------------------------------------------- particles and ribbons
        public static IEnumerable<string> EffectModels()
        {
            var p = Path.Combine(DbcRoundTrip.DefaultDir, "SpellVisualEffectName.dbc");
            if (!File.Exists(p)) yield break;
            var f = DbcFile.Load(p);
            for (int i = 0; i < f.Records; i++) { var s = f.Str(i, 2); if (!string.IsNullOrEmpty(s)) yield return M2Model.M2Path(s); }
        }

        [Test] static void Real_spell_effects_particles_and_ribbons()
        {
            if (Client == null) return;
            int withParticles = 0, withRibbons = 0, emitted = 0, read = 0;
            foreach (var path in EffectModels().Take(400))
            {
                var bytes = Read(path); if (bytes == null) continue;
                M2Model m; try { m = M2Model.Read(bytes); } catch { continue; }
                if (m.ParticleEmitters == 0 && m.RibbonEmitters == 0) continue;
                read++;
                var fx = M2Effects.Read(m);
                A.True(fx.Problem == null, path + ": " + fx.Problem);
                A.Eq(m.ParticleEmitters, fx.Emitters.Count, path + " emitters");
                A.Eq(m.RibbonEmitters, fx.Ribbons.Count, path + " ribbons");
                foreach (var e in fx.Emitters) A.True(e.Texture < m.Textures.Count && e.Bone < Math.Max(1, m.Bones.Count), path + " emitter refs");
                if (fx.Emitters.Count > 0)
                {
                    withParticles++;
                    var sim = new ParticleSim();
                    var pose = m.Pose(m.Sequences.Count > 0 ? 0 : -1, 0);
                    for (int k = 0; k < 30; k++) sim.Step(m, fx, pose, 0, (uint)(k * 33), 0.033f);
                    emitted += sim.Live.Count;
                    A.True(sim.Live.All(q => !float.IsNaN(q.Pos.X) && !float.IsInfinity(q.Pos.X)), path + " finite particles");
                }
                if (fx.Ribbons.Count > 0) withRibbons++;
                if (read >= 60) break;
            }
            A.True(withParticles > 5, $"effect models with particles read: {withParticles}");
            A.True(withRibbons > 0, $"effect models with ribbons read: {withRibbons}");
            A.True(emitted > 0, "particles were emitted");
            A.Eq(0.5f, M2Effects.OverLife(new[] { 0f, 1f }, 0.5f, 0f, (a, b, t) => a + (b - a) * t));
        }

        // ---------------------------------------------------------------- dressed characters
        [Test] static void Real_dressed_character_and_armor_paths()
        {
            A.Eq(@"Item\TextureComponents\TorsoUpperTexture\Plate_A_01_TU_M.blp", CharacterBuilder.ComponentPaths("TorsoUpperTexture", "Plate_A_01_TU", 0).First());
            A.Eq(@"Item\ObjectComponents\Head\Helm_Plate_A_01_HuM.m2", CharacterBuilder.HelmPath("Helm_Plate_A_01.mdx", CharacterBuilder.RaceSexSuffix("Hu", 0)));
            A.Eq(@"Item\TextureComponents\LegLowerTexture\MyPants_U.blp", AssetImport.ArmorTexturePath(6, "My Pants!"));
            var g = CharacterBuilder.Geosets(5, new[] { 2, 1, 3 }, s => s == CharacterLook.Boots ? new[] { 2, 0, 0 } : s == -CharacterLook.Cape ? new int[3] : s < 0 ? null : new int[3]);
            A.True(g.Contains(0) && g.Contains(5) && g.Contains(503) && g.Contains(1501) && g.Contains(103) && !g.Contains(1301), string.Join(",", g));
            var dst = new BgraImage { Width = 256, Height = 256, Pixels = new byte[256 * 256 * 4] };
            var red = new BgraImage { Width = 2, Height = 2, Pixels = Enumerable.Repeat(new byte[] { 0, 0, 255, 255 }, 4).SelectMany(x => x).ToArray() };
            CharacterBuilder.Paste(dst, red, CharacterBuilder.Region.TorsoUpper);
            A.Eq((byte)255, dst.Pixels[(10 * 256 + 140) * 4 + 2], "torso region painted"); A.Eq((byte)0, dst.Pixels[(10 * 256 + 10) * 4 + 2], "arm region untouched");
            if (Client == null || !Directory.Exists(DbcRoundTrip.DefaultDir)) return;
            DbcFile Dbc(string f) { var p = Path.Combine(DbcRoundTrip.DefaultDir, f); return File.Exists(p) ? DbcFile.Load(p) : null; }
            // the first few NPC looks that have a baked texture build with a body, hair and some geosets
            var extra = Dbc("CreatureDisplayInfoExtra.dbc");
            int built = 0;
            for (int i = 0; i < extra.Records && built < 5; i++)
            {
                var look = CharacterBuilder.FromExtra(extra, extra.UInt(i, 0));
                if (string.IsNullOrEmpty(look.BakeName)) continue;
                var b = CharacterBuilder.Build(look, Dbc, Read);
                A.True(b.ModelPath != null && Read(b.ModelPath) != null, "body model " + b.ModelPath);
                A.True(b.Body != null && b.Baked, "baked body " + look.BakeName);
                A.True(b.Geosets.Count >= 5 && b.Geosets.Contains(0), "geosets");
                foreach (var (_, model, _) in b.Attached) A.True(Read(model) != null, "attached model is in the client: " + model);
                built++;
            }
            A.True(built == 5, "built five NPC looks");
            // a player look without a bake is composed from skin and face
            var human = CharacterBuilder.Build(new CharacterLook { Race = 1, Sex = 0 }, Dbc, Read);
            A.True(human.Body != null && !human.Baked, "composed human skin");
        }

        // ---------------------------------------------------------------- hardening for strangers
        [Test] static void Logs_never_contain_passwords()
        {
            A.Eq("CREATE USER 'a'@'h' IDENTIFIED BY ********;", Log.Scrub("CREATE USER 'a'@'h' IDENTIFIED BY 's3cr3t';"));
            A.Eq("Server=x;User=root;Password=********;Database=w", Log.Scrub("Server=x;User=root;Password=hunter2;Database=w"));
            A.Eq("LoginDatabaseInfo = \"127.0.0.1;3306;acore;********;acore_auth\"", Log.Scrub("LoginDatabaseInfo = \"127.0.0.1;3306;acore;topsecret;acore_auth\""));
            A.True(!Log.Scrub("SET PASSWORD = PASSWORD('abc')").Contains("abc"), "old syntax");
            A.Eq("nothing secret here", Log.Scrub("nothing secret here"));
        }

        [Test] static void Errors_are_explained_in_plain_words()
        {
            A.Contains(Friendly.Text(new FileNotFoundException("x", @"C:\a\worldserver.conf")), "worldserver.conf");
            A.Contains(Friendly.Text(new UnauthorizedAccessException()), "didn't allow access");
            A.Contains(Friendly.Text(new InvalidDataException("Not a WMO file.")), "build 12340");
            A.Contains(Friendly.Text(new TimeoutException()), "too long");
            A.Contains(Friendly.Text(new AggregateException(new System.Net.Sockets.SocketException(10061))), "Couldn't reach the database server");
            A.Eq("hello Password=********", Friendly.Text(new Exception("hello Password=abc")), "scrubbed");
        }

        [Test] static void Damaged_changeset_is_recovered_not_lost()
        {
            var dir = Path.Combine(Path.GetTempPath(), "studio_cs_" + Guid.NewGuid().ToString("N"));
            try
            {
                var st = new ChangesetStore(dir);
                ChangeFactory.StageCellEdit(st.Pending, "t", new[] { new KeyPart("id", "1") }, "a", "1", "1", "2", "2");
                st.SavePending();
                ChangeFactory.StageCellEdit(st.Pending, "t", new[] { new KeyPart("id", "2") }, "a", "1", "1", "3", "3");
                st.SavePending();                                  // pending.json has 2 changes, pending.json.bak has 1
                A.True(File.Exists(st.PendingPath + ".bak"), "previous copy kept");
                File.WriteAllText(st.PendingPath, "{ this is not json");   // e.g. power cut mid-write
                var again = new ChangesetStore(dir);
                A.Eq(1, again.Pending.Changes.Count, "restored from the previous copy");
                A.Contains(again.RecoveryNote, "restored the previous copy");
                A.True(Directory.GetFiles(dir, "pending.damaged-*.json").Length == 1, "damaged file kept");
                var fresh = new ChangesetStore(Path.Combine(dir, "new"));
                A.True(fresh.RecoveryNote == null && fresh.Pending.Changes.Count == 0, "a new folder is just empty");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Test] static void MariaDb_backup_tool_gets_compatible_options()
        {
            var my = Backup.BuildArgs("o.cnf", "h", 3306, "w", new[] { "t" }, "out.sql");
            var ma = Backup.BuildArgs("o.cnf", "h", 3306, "w", new[] { "t" }, "out.sql", true);
            A.True(my.Contains("--set-gtid-purged=OFF") && !ma.Contains("--set-gtid-purged=OFF"), "gtid option only for MySQL's tool");
            A.True(ma.Contains("--single-transaction") && ma.Contains("--no-tablespaces") && ma.Last() == "t", "the rest is the same");
            A.True(Backup.IsMariaDbTool(@"C:\Program Files\MariaDB 11.4\bin\mysqldump.exe") && Backup.IsMariaDbTool(@"C:\x\mariadb-dump.exe") && !Backup.IsMariaDbTool(@"C:\Program Files\MySQL\MySQL Server 8.0\bin\mysqldump.exe"), "detection");
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
