using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AndersCraftStudio.Core;

namespace AndersCraftStudio.Tests
{
    static class M5Tests
    {
        static bool Have => Directory.Exists(DbcRoundTrip.DefaultDir);

        static (DbcSpellRefs refs, DbcTable spells) Real()
        {
            var refs = SpellProbe.Refs(out var t);
            return (refs, t);
        }

        [Test] static void Catalog_has_effects_auras_targets_and_attribute_bits()
        {
            A.Eq("Damage", SpellCatalog.Label("effect", 2));
            A.Eq("Apply aura", SpellCatalog.Label("effect", 6));
            A.Eq("Damage over time", SpellCatalog.Label("aura", 3));
            A.Eq("Caster (self)", SpellCatalog.Label("target", 1));
            A.True(SpellCatalog.Of("effect").Count >= 160, "effects");
            A.True(SpellCatalog.Of("aura").Count >= 310, "auras");
            for (int i = 0; i < 8; i++) A.Eq(32, SpellCatalog.Of("attr" + i).Count, "attr" + i);
            A.Eq("Passive", SpellCatalog.Get("attr0", 0x40).Label); A.True(SpellCatalog.Get("attr0", 0x40).Studio, "own wording");
            A.Eq("Fire", SpellCatalog.Schools(4));
            A.Eq("Fire and Frost", SpellCatalog.Schools(4 | 16));
            A.Eq("all schools", SpellCatalog.Schools(0x7F));
            A.Eq("Magic", SpellCatalog.Label("dispel", 1));
        }

        [Test] static void Expression_evaluator()
        {
            A.True(SpellTooltip.Eval("2*(3+4)-1", out var v), "eval"); A.Eq(13.0, v);
            A.True(SpellTooltip.Eval("10/4", out v), "div"); A.Eq(2.5, v);
            A.False(SpellTooltip.Eval("$AP*2", out _), "unknown token");
        }

        [Test] static void Tooltip_for_real_spells_matches_the_game_text()
        {
            if (!Have) return;
            var (refs, t) = Real();
            var fb = SpellTooltip.Build(new SpellData(t, t.Find(133)), refs);
            A.Eq("Fireball", fb.Name); A.Eq("Rank 1", fb.Rank);
            A.Eq("35 yd range", fb.Range); A.Eq("1.5 sec cast", fb.Cast);
            A.Eq("Hurls a fiery ball that causes 14 to 22 Fire damage and an additional 2 Fire damage over 4 sec.", fb.Description);
            var renew = SpellTooltip.Build(new SpellData(t, t.Find(139)), refs);
            A.Eq("Heals the target for 45 over 15 sec.", renew.Description);       // $<total> from SpellDescriptionVariables
            var frost = SpellTooltip.Build(new SpellData(t, t.Find(116)), refs);
            A.Contains(frost.Description, "causing 18 to 20 Frost damage");          // ${$m2*$<mult>}
            var cs = SpellTooltip.Build(new SpellData(t, t.Find(2139)), refs);
            A.Eq("24 sec cooldown", cs.Cooldown); A.Eq("Instant", cs.Cast);
            var bs = SpellTooltip.Build(new SpellData(t, t.Find(53)), refs);
            A.Eq("60 Energy", bs.Cost); A.Eq("Melee range", bs.Range);
        }

        [Test] static void Effect_summaries_read_as_sentences()
        {
            if (!Have) return;
            var (refs, t) = Real();
            var fb = new SpellData(t, t.Find(133));
            A.Eq("Deals 14 to 22 Fire damage (selected enemy)", SpellTooltip.EffectSummary(fb, 1, refs));
            A.Eq("1 Fire damage every 2 sec for 4 sec (selected enemy)", SpellTooltip.EffectSummary(fb, 2, refs));
            A.True(SpellTooltip.EffectSummary(fb, 3, refs) == null, "empty slot");
            var ai = new SpellData(t, t.Find(1459));
            A.Eq("Changes Intellect by 2 for 30 min (selected friend)", SpellTooltip.EffectSummary(ai, 1, refs));
        }

        [Test] static void Tokens_on_an_edited_copy()
        {
            if (!Have) return;
            var (refs, t) = Real();
            var w = t.Clone();
            var row = w.Find(133);
            var s = new SpellData(w, row);
            A.True(s.Set("Description_enUS", "Deals $s1 for $d; total $o2 every $t2 sec; ${$m1*2} doubled; $lpoint:points; $?s123[yes][no] $133s1 $x", out _), "set");
            refs.SpellTable = w;
            var txt = SpellTooltip.Substitute(s.S("Description_enUS"), s, refs);
            A.Eq("Deals 14 to 22 for 4 sec; total 2 every 2 sec; 28 doubled; points no 14 to 22 0", txt);
            s.Set("EffectBasePoints_1", "99", out _); s.Set("EffectDieSides_1", "1", out _);
            A.Eq("Deals 100 Fire damage (selected enemy)", SpellTooltip.EffectSummary(s, 1, refs));
        }

        [Test] static void Duplicate_ids_skip_existing_staged_and_server_rows()
        {
            if (!Have) return;
            var (_, t) = Real();
            var cs = new Changeset();
            uint first = SpellIds.FreeId(t, cs, 900000);
            A.True(t.Find(first) == null && first >= 900000, "free in dbc");
            cs.Changes.Add(new Change { Kind = ChangeKind.Insert, Schema = "dbc", Table = "Spell.dbc", Dbc = new DbcPatch { File = "Spell.dbc", Key = first } });
            uint second = SpellIds.FreeId(t, cs, 900000);
            A.True(second > first, "staged id skipped");
            uint third = SpellIds.FreeId(t, cs, 900000, new HashSet<uint> { second });
            A.True(third > second, "spell_dbc id skipped");
            A.True(SpellIds.FreeId(t, new Changeset(), 133) != 133, "existing id skipped");
        }

        [Test] static void Spell_dbc_row_generation()
        {
            if (!Have) return;
            var (_, t) = Real();
            var w = t.Clone();
            var copy = w.Find(133).Clone(); copy.V[0] = 900123; w.Rows.Add(copy); w.InvalidateIndex();
            var s = new SpellData(w, copy);
            A.Eq("Name_Lang_enUS", SpellServerRow.Column(w.Layout, s.Field("Name_enUS")));
            A.Eq("Name_Lang_Mask", SpellServerRow.Column(w.Layout, s.Field("Name_enUS") + 16));
            A.Eq("Speed", SpellServerRow.Column(w.Layout, s.Field("Speed")));
            var cs = new Changeset();
            var c = SpellServerRow.Stage(cs, s);
            A.Eq("spell_dbc", c.Table); A.Eq(ChangeKind.Insert, c.Kind);
            A.Contains(c.ForwardSql, "INSERT INTO `spell_dbc`");
            A.Contains(c.ForwardSql, "'Fireball'");
            A.Contains(c.UndoSql, "DELETE FROM `spell_dbc` WHERE `ID` = 900123");
            A.Contains(c.ForwardSql, "`Speed`");
            var c2 = SpellServerRow.Stage(cs, s, new[] { "ID", "Name_Lang_enUS", "Speed" });
            A.Eq(1, cs.Changes.Count, "restaging replaces the earlier row");
            A.Contains(c2.ForwardSql, "(`ID`, `Speed`, `Name_Lang_enUS`) VALUES (900123, 24, 'Fireball')");
            A.True(ApplyEngine.Validate(cs.Changes, "w", new string[0]).Count == 0, "a normal world change");
        }

        static byte[] Header(byte comp, byte alphaDepth, byte alphaType, int dataLen)
        {
            var ms = new MemoryStream(); var bw = new BinaryWriter(ms);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("BLP2")); bw.Write(1u); bw.Write(comp); bw.Write(alphaDepth); bw.Write(alphaType); bw.Write((byte)0);
            bw.Write(4); bw.Write(4);
            for (int i = 0; i < 16; i++) bw.Write(i == 0 ? 1172u : 0u);
            for (int i = 0; i < 16; i++) bw.Write(i == 0 ? (uint)dataLen : 0u);
            for (int i = 0; i < 256; i++) bw.Write(i == 1 ? 0xFF00FF00u : 0u);   // palette entry 1 = green
            return ms.ToArray();
        }

        [Test] static void Blp_decodes_raw_dxt1_dxt5_and_palette()
        {
            var raw = new BgraImage { Width = 2, Height = 2, Pixels = new byte[] { 1, 2, 3, 255, 4, 5, 6, 255, 7, 8, 9, 128, 10, 11, 12, 0 } };
            var back = Blp.Decode(Blp.BuildRaw(raw));
            A.Eq(2, back.Width); A.True(back.Pixels.SequenceEqual(raw.Pixels), "raw BGRA");
            // DXT1: c0 = red (0xF800) > c1 = blue (0x001F); indices 0 → red
            var i1 = Blp.Decode(Header(2, 0, 0, 8).Concat(new byte[] { 0x00, 0xF8, 0x1F, 0x00, 0, 0, 0, 0 }).ToArray());
            A.Eq((byte)255, i1.Pixels[2], "red"); A.Eq((byte)0, i1.Pixels[0], "no blue"); A.Eq((byte)255, i1.Pixels[3], "opaque");
            var i1b = Blp.Decode(Header(2, 0, 0, 8).Concat(new byte[] { 0x00, 0xF8, 0x1F, 0x00, 0x55, 0x55, 0x55, 0x55 }).ToArray());
            A.Eq((byte)255, i1b.Pixels[0], "index 1 = blue");
            // DXT5: alpha endpoints 255/0, every alpha index 1 → alpha 0
            var d5 = Header(2, 8, 7, 16).Concat(new byte[] { 255, 0, 0x49, 0x92, 0x24, 0x49, 0x92, 0x24, 0x00, 0xF8, 0x1F, 0x00, 0, 0, 0, 0 }).ToArray();
            A.Eq((byte)0, Blp.Decode(d5).Pixels[3], "dxt5 alpha");
            // palette with 8-bit alpha
            var ip = Blp.Decode(Header(1, 8, 0, 32).Concat(Enumerable.Repeat((byte)1, 16)).Concat(Enumerable.Repeat((byte)77, 16)).ToArray());
            A.Eq((byte)255, ip.Pixels[1], "green"); A.Eq((byte)77, ip.Pixels[3], "alpha");
            A.Throws(() => Blp.Decode(new byte[10]), "garbage");
        }

        [Test] static void Icon_cache_decodes_once_and_reuses_disk()
        {
            var dir = Path.Combine(Path.GetTempPath(), "studio_icons_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            try
            {
                var img = new BgraImage { Width = 1, Height = 1, Pixels = new byte[] { 9, 8, 7, 255 } };
                int reads = 0;
                var c1 = new IconCache(p => { reads++; return Blp.BuildRaw(img); }, dir);
                A.Eq((byte)9, c1.Get("Interface\\Icons\\Test").Pixels[0]);
                c1.Get("Interface\\Icons\\Test");
                A.Eq(1, reads, "memory cache");
                var c2 = new IconCache(p => { reads++; return null; }, dir);
                A.Eq((byte)7, c2.Get("interface\\icons\\test.blp").Pixels[2]);
                A.Eq(1, c2.FromDisk); A.Eq(1, reads, "disk cache, no read");
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        [Test] static void Real_client_icons_decode()
        {
            var data = DevPaths.ClientData;
            if (!Directory.Exists(data)) return;
            using var cf = new ClientFiles(data);
            var b = cf.Read("Interface\\Icons\\Spell_Fire_FlameBolt.blp", out var from);
            A.True(b != null, "found"); A.True(from != null, "archive");
            var img = Blp.Decode(b);
            A.Eq(64, img.Width); A.Eq(64, img.Height);
            A.True(img.Pixels.Where((x, i) => i % 4 == 2).Average(x => (double)x) > 80, "a fire icon is reddish");
        }

        [Test] static void Staging_a_spell_with_a_new_related_row()
        {
            if (!Have) return;
            var (refs, t) = Real();
            var rangeBase = refs.Get("SpellRange.dbc"); var rangeWork = rangeBase.Clone();
            var nr = rangeWork.Find(4).Clone(); nr.V[0] = 900001; rangeWork.Rows.Add(nr); rangeWork.InvalidateIndex();
            rangeWork.TrySetField(nr, rangeWork.Layout.FieldByName("RangeMax_1"), "55", out _);
            rangeWork.TrySetField(nr, rangeWork.Layout.FieldByName("RangeMax_2"), "55", out _);
            var w = t.Clone(); var s = new SpellData(w, w.Find(133));
            s.Set("RangeIndex", "900001", out _);
            var cs = new Changeset();
            DbcStager.Restage(cs, t, w, "server");
            DbcStager.Restage(cs, rangeBase, rangeWork, "server");
            A.Eq(2, cs.Changes.Count);
            A.True(cs.Changes.Any(c => c.Table == "SpellRange.dbc" && c.Kind == ChangeKind.Insert && c.Dbc.Key == 900001), "range row");
            refs.Override(rangeWork); refs.SpellTable = w;
            A.Eq("55 yd range", SpellTooltip.Build(s, refs).Range);
        }
    }
}
