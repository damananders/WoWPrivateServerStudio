using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace AndersCraftStudio.Core
{
    /// <summary>
    /// Builds a dressed character for the viewer from 3.3.5a character data: the race's body model, the geosets (hair,
    /// beard, sleeves, boots, robe, cloak…) the look and the worn items switch on, the body texture (the NPC's pre-baked
    /// texture, or skin + face + underwear + armour pieces composed onto it), the hair texture, and the item models worn
    /// on attachment points (helmet, shoulders, weapons). Read-only; written from the public format notes.
    /// </summary>
    public sealed class CharacterLook
    {
        public int Race, Sex, Skin, Face, HairStyle, HairColor, FacialHair;
        /// <summary>ItemDisplayInfo IDs: helm, shoulder, shirt, chest, belt, legs, boots, wrist, gloves, tabard, cape.</summary>
        public uint[] Items = new uint[11];
        public string BakeName;
        /// <summary>Weapons/shields held: ItemDisplayInfo ID and the item's equip slot (InventoryType).</summary>
        public List<(uint display, int inventoryType)> Held = new List<(uint, int)>();
        /// <summary>Extra pictures painted onto the body last (previewing armour textures before they're imported).</summary>
        public List<(CharacterBuilder.Region region, BgraImage image)> Paint = new List<(CharacterBuilder.Region, BgraImage)>();

        public const int Helm = 0, Shoulder = 1, Shirt = 2, Chest = 3, Belt = 4, Legs = 5, Boots = 6, Wrist = 7, Gloves = 8, Tabard = 9, Cape = 10;
    }

    public sealed class CharacterBuild
    {
        public string ModelPath;
        public HashSet<ushort> Geosets = new HashSet<ushort>();
        public BgraImage Body, Hair, Extra;
        public List<(int attachment, string model, string texture)> Attached = new List<(int, string, string)>();
        public List<string> Notes = new List<string>();
        public bool Baked;
    }

    public static class CharacterBuilder
    {
        // texture regions on a 256×256 character skin (scaled for larger skins)
        public enum Region { ArmUpper, ArmLower, Hand, FaceUpper, FaceLower, TorsoUpper, TorsoLower, LegUpper, LegLower, Foot }
        public static readonly Dictionary<Region, (int x, int y, int w, int h)> Rects = new Dictionary<Region, (int, int, int, int)>
        {
            [Region.ArmUpper] = (0, 0, 128, 64), [Region.ArmLower] = (0, 64, 128, 64), [Region.Hand] = (0, 128, 128, 32),
            [Region.FaceUpper] = (0, 160, 128, 32), [Region.FaceLower] = (0, 192, 128, 64),
            [Region.TorsoUpper] = (128, 0, 128, 64), [Region.TorsoLower] = (128, 64, 128, 32), [Region.LegUpper] = (128, 96, 128, 64),
            [Region.LegLower] = (128, 160, 128, 64), [Region.Foot] = (128, 224, 128, 32),
        };

        /// <summary>ItemDisplayInfo Texture_1…8 → region and the folder under Item\TextureComponents.</summary>
        public static readonly (Region region, string folder)[] ItemTextureSlots =
        {
            (Region.ArmUpper, "ArmUpperTexture"), (Region.ArmLower, "ArmLowerTexture"), (Region.Hand, "HandTexture"), (Region.TorsoUpper, "TorsoUpperTexture"),
            (Region.TorsoLower, "TorsoLowerTexture"), (Region.LegUpper, "LegUpperTexture"), (Region.LegLower, "LegLowerTexture"), (Region.Foot, "FootTexture"),
        };

        /// <summary>Candidate files for an armour piece's texture: gender-specific first, then unisex.</summary>
        public static IEnumerable<string> ComponentPaths(string folder, string name, int sex)
        {
            if (string.IsNullOrEmpty(name)) yield break;
            var b = $@"Item\TextureComponents\{folder}\{name}";
            yield return b + (sex == 0 ? "_M.blp" : "_F.blp");
            yield return b + "_U.blp";
            yield return b + ".blp";
        }

        /// <summary>Draws <paramref name="src"/> into a region of <paramref name="dst"/> (alpha-blended, resized).</summary>
        public static void Paste(BgraImage dst, BgraImage src, Region r)
        {
            if (dst == null || src == null) return;
            var (x, y, w, h) = Rects[r];
            float sx = dst.Width / 256f, sy = dst.Height / 256f;
            int X = (int)(x * sx), Y = (int)(y * sy), W = Math.Max(1, (int)(w * sx)), H = Math.Max(1, (int)(h * sy));
            var s = src.Width == W && src.Height == H ? src : BlpEncoder.Resize(src, W, H);
            for (int j = 0; j < H && Y + j < dst.Height; j++)
                for (int i = 0; i < W && X + i < dst.Width; i++)
                {
                    int si = (j * s.Width + i) * 4, di = ((Y + j) * dst.Width + X + i) * 4;
                    int a = s.Pixels[si + 3];
                    if (a == 0) continue;
                    for (int c = 0; c < 3; c++) dst.Pixels[di + c] = (byte)((s.Pixels[si + c] * a + dst.Pixels[di + c] * (255 - a)) / 255);
                    dst.Pixels[di + 3] = 255;
                }
        }

        /// <summary>Which geosets to show: default parts, the hair style and beard, and what the worn items switch on.</summary>
        public static HashSet<ushort> Geosets(int hairGeoset, int[] facial, Func<int, int[]> itemGroups)
        {
            // group → variant (0 = hidden)
            var v = new Dictionary<int, int> { [1] = 1, [2] = 1, [3] = 1, [4] = 1, [5] = 1, [7] = 2, [8] = 0, [9] = 1, [10] = 1, [11] = 1, [12] = 0, [13] = 0, [15] = 0, [17] = 0, [18] = 0 };
            if (facial != null && facial.Length >= 3) { v[1] = 1 + facial[0]; v[2] = 1 + facial[2]; v[3] = 1 + facial[1]; }
            int[] G(int slot) => itemGroups?.Invoke(slot) ?? new int[3];
            var shirt = G(CharacterLook.Shirt); var chest = G(CharacterLook.Chest);
            if (shirt[0] > 0) v[8] = 1 + shirt[0];
            if (chest[0] > 0) v[8] = 1 + chest[0];
            if (chest[2] > 0) v[13] = 1 + chest[2];          // robe
            var legs = G(CharacterLook.Legs);
            if (legs[1] > 0) v[9] = 1 + legs[1];
            if (legs[2] > 0) v[13] = 1 + legs[2];
            var gloves = G(CharacterLook.Gloves); if (gloves[0] > 0) v[4] = 1 + gloves[0];
            var boots = G(CharacterLook.Boots); if (boots[0] > 0) v[5] = 1 + boots[0];
            var tabard = G(CharacterLook.Tabard); if (tabard[0] > 0 || itemGroups?.Invoke(-CharacterLook.Tabard) != null) v[12] = 1 + tabard[0];
            var cape = G(CharacterLook.Cape); if (itemGroups?.Invoke(-CharacterLook.Cape) != null) v[15] = 1 + cape[0];
            var belt = G(CharacterLook.Belt); if (belt[0] > 0) v[18] = 1 + belt[0];
            var set = new HashSet<ushort> { 0 };
            if (hairGeoset > 0) set.Add((ushort)hairGeoset); else set.Add(1);
            foreach (var kv in v) if (kv.Value > 0) set.Add((ushort)(kv.Key * 100 + kv.Value));
            return set;
        }

        public static string RaceSexSuffix(string clientPrefix, int sex) => (clientPrefix ?? "Hu") + (sex == 0 ? "M" : "F");

        /// <summary>The helmet model for a race and gender: Helm_X.mdx → Item\ObjectComponents\Head\Helm_X_HuM.m2.</summary>
        public static string HelmPath(string modelName, string suffix) =>
            string.IsNullOrEmpty(modelName) ? null : $@"Item\ObjectComponents\Head\{Path.GetFileNameWithoutExtension(modelName)}_{suffix}.m2";

        public static string ShoulderPath(string modelName) =>
            string.IsNullOrEmpty(modelName) ? null : $@"Item\ObjectComponents\Shoulder\{Path.GetFileNameWithoutExtension(modelName)}.m2";

        /// <summary>
        /// Puts a character together. <paramref name="dbc"/> opens a server DBC file (raw, by name); <paramref name="read"/> reads client files.
        /// </summary>
        public static CharacterBuild Build(CharacterLook look, Func<string, DbcFile> dbc, Func<string, byte[]> read)
        {
            var b = new CharacterBuild();
            var races = dbc("ChrRaces.dbc");
            string raceFile = null, prefix = null;
            if (races != null)
                for (int i = 0; i < races.Records; i++)
                    if (races.UInt(i, 0) == look.Race) { prefix = races.Str(i, 6); raceFile = races.Str(i, 11); break; }
            if (string.IsNullOrEmpty(raceFile)) { b.Notes.Add($"Race {look.Race} isn't in ChrRaces.dbc."); return b; }
            var g = look.Sex == 0 ? "Male" : "Female";
            b.ModelPath = $@"Character\{raceFile}\{g}\{raceFile}{g}.m2";

            // hair and beard geosets
            int hairGeo = 0; int[] facial = null;
            var hg = dbc("CharHairGeosets.dbc");
            if (hg != null)
                for (int i = 0; i < hg.Records; i++)
                    if (hg.UInt(i, 1) == look.Race && hg.UInt(i, 2) == look.Sex && hg.UInt(i, 3) == look.HairStyle) { hairGeo = hg.Int(i, 4); break; }
            var fh = dbc("CharacterFacialHairStyles.dbc");
            if (fh != null)
                for (int i = 0; i < fh.Records; i++)
                    if (fh.UInt(i, 0) == look.Race && fh.UInt(i, 1) == look.Sex && fh.UInt(i, 2) == look.FacialHair) { facial = new[] { fh.Int(i, 3), fh.Int(i, 4), fh.Int(i, 5) }; break; }

            // worn items
            var idi = dbc("ItemDisplayInfo.dbc");
            int Row(uint id) { if (idi == null || id == 0) return -1; for (int i = 0; i < idi.Records; i++) if (idi.UInt(i, 0) == id) return i; return -1; }
            var rows = look.Items.Select(Row).ToArray();
            int[] Groups(int slot)
            {
                if (slot < 0) return rows[-slot] >= 0 ? new int[3] : null;   // "is something worn there?"
                int r = rows[slot]; return r < 0 ? new int[3] : new[] { idi.Int(r, 7), idi.Int(r, 8), idi.Int(r, 9) };
            }
            b.Geosets = Geosets(hairGeo, facial, Groups);

            // textures: hair, then the body (baked for NPCs, else composed)
            var cs = dbc("CharSections.dbc");
            string Section(int section, int variation, int color, int tex)
            {
                if (cs == null) return null;
                for (int i = 0; i < cs.Records; i++)
                    if (cs.UInt(i, 1) == look.Race && cs.UInt(i, 2) == look.Sex && cs.UInt(i, 3) == section && cs.UInt(i, 8) == variation && cs.UInt(i, 9) == color)
                        return cs.Str(i, 4 + tex);
                return null;
            }
            BgraImage Img(string p) { if (string.IsNullOrEmpty(p)) return null; var bytes = read(p); if (bytes == null) return null; try { return Blp.Decode(bytes); } catch { return null; } }
            b.Hair = Img(Section(3, look.HairStyle, look.HairColor, 0));
            b.Extra = Img(Section(0, 0, look.Skin, 1));
            if (!string.IsNullOrEmpty(look.BakeName))
            {
                b.Body = Img(@"Textures\BakedNpcTextures\" + look.BakeName);
                b.Baked = b.Body != null;
                if (!b.Baked) b.Notes.Add($"The baked texture {look.BakeName} wasn't found; the body is composed instead.");
            }
            if (b.Body == null)
            {
                var skin = Img(Section(0, 0, look.Skin, 0));
                if (skin == null) { b.Notes.Add("No skin texture found for this race/skin colour."); }
                else
                {
                    var body = new BgraImage { Width = skin.Width, Height = skin.Height, Pixels = (byte[])skin.Pixels.Clone() };
                    Paste(body, Img(Section(1, look.Face, look.Skin, 0)), Region.FaceLower);
                    Paste(body, Img(Section(1, look.Face, look.Skin, 1)), Region.FaceUpper);
                    Paste(body, Img(Section(4, 0, look.Skin, 0)), Region.LegUpper);
                    Paste(body, Img(Section(4, 0, look.Skin, 1)), Region.TorsoUpper);
                    // worn armour, under-layers first
                    foreach (var slot in new[] { CharacterLook.Shirt, CharacterLook.Legs, CharacterLook.Boots, CharacterLook.Chest, CharacterLook.Wrist, CharacterLook.Belt, CharacterLook.Gloves, CharacterLook.Tabard })
                    {
                        int r = rows[slot]; if (r < 0) continue;
                        for (int t = 0; t < 8; t++)
                        {
                            var name = idi.Str(r, 15 + t);
                            if (string.IsNullOrEmpty(name)) continue;
                            var img = ComponentPaths(ItemTextureSlots[t].folder, name, look.Sex).Select(Img).FirstOrDefault(x => x != null);
                            Paste(body, img, ItemTextureSlots[t].region);
                        }
                    }
                    b.Body = body;
                }
            }
            if (b.Body != null && look.Paint.Count > 0)
            {
                b.Body = new BgraImage { Width = b.Body.Width, Height = b.Body.Height, Pixels = (byte[])b.Body.Pixels.Clone() };
                foreach (var (region, img) in look.Paint) Paste(b.Body, img, region);
            }

            // attached item models
            string Sfx() => RaceSexSuffix(prefix, look.Sex);
            int hr = rows[CharacterLook.Helm];
            if (hr >= 0 && !string.IsNullOrEmpty(idi.Str(hr, 1))) b.Attached.Add((11, HelmPath(idi.Str(hr, 1), Sfx()), Tex(@"Item\ObjectComponents\Head", idi.Str(hr, 3))));
            int sr = rows[CharacterLook.Shoulder];
            if (sr >= 0)
            {
                if (!string.IsNullOrEmpty(idi.Str(sr, 1))) b.Attached.Add((6, ShoulderPath(idi.Str(sr, 1)), Tex(@"Item\ObjectComponents\Shoulder", idi.Str(sr, 3))));
                if (!string.IsNullOrEmpty(idi.Str(sr, 2))) b.Attached.Add((5, ShoulderPath(idi.Str(sr, 2)), Tex(@"Item\ObjectComponents\Shoulder", idi.Str(sr, 4))));
            }
            int hand = 0;
            foreach (var (display, inv) in look.Held)
            {
                int r = Row(display); if (r < 0 || string.IsNullOrEmpty(idi.Str(r, 1))) { hand++; continue; }
                var model = ItemModels.ModelPaths(idi.Str(r, 1), inv).FirstOrDefault(p => read(p) != null);
                if (model == null) { hand++; continue; }
                int att = inv == 14 ? 0 : hand == 0 ? 1 : 2;      // shield on the arm, else right hand then left hand
                b.Attached.Add((att, model, Tex(Path.GetDirectoryName(model), idi.Str(r, 3))));
                hand++;
            }
            return b;
        }

        static string Tex(string folder, string name) => string.IsNullOrEmpty(name) ? null : Path.Combine(folder, name + ".blp");

        /// <summary>A look from a CreatureDisplayInfoExtra row (raw DBC record).</summary>
        public static CharacterLook FromExtra(DbcFile extra, uint id)
        {
            if (extra == null) return null;
            for (int i = 0; i < extra.Records; i++)
            {
                if (extra.UInt(i, 0) != id) continue;
                var l = new CharacterLook { Race = extra.Int(i, 1), Sex = extra.Int(i, 2), Skin = extra.Int(i, 3), Face = extra.Int(i, 4), HairStyle = extra.Int(i, 5), HairColor = extra.Int(i, 6), FacialHair = extra.Int(i, 7) };
                for (int k = 0; k < 11; k++) l.Items[k] = extra.UInt(i, 8 + k);
                l.BakeName = extra.Fields > 20 ? extra.Str(i, 20) : null;
                return l;
            }
            return null;
        }
    }
}
