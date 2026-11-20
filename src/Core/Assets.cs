using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace AndersCraftStudio.Core
{
    public enum BlpFormat { Dxt1, Dxt5, Palette, Raw }

    /// <summary>BLP2 writer (Studio's own): DXT1, DXT5, 8-bit palette with 8-bit alpha, or raw BGRA, with a full mip chain.</summary>
    public static class BlpEncoder
    {
        public static bool IsPowerOfTwo(int n) => n > 0 && (n & (n - 1)) == 0;

        public static byte[] Encode(BgraImage img, BlpFormat fmt, bool mips = true)
        {
            if (!IsPowerOfTwo(img.Width) || !IsPowerOfTwo(img.Height)) throw new ArgumentException("BLP textures must be a power of two wide and high (e.g. 64×64).");
            var levels = new List<BgraImage> { img };
            if (mips) while (levels.Count < 16 && (levels[^1].Width > 1 || levels[^1].Height > 1)) levels.Add(Half(levels[^1]));
            uint[] palette = null;
            if (fmt == BlpFormat.Palette) palette = MedianCut(img, 256);
            var data = levels.Select(l => fmt switch
            {
                BlpFormat.Dxt1 => Dxt(l, false),
                BlpFormat.Dxt5 => Dxt(l, true),
                BlpFormat.Palette => Paletted(l, palette),
                _ => (byte[])l.Pixels.Clone(),
            }).ToList();
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            w.Write(Encoding.ASCII.GetBytes("BLP2")); w.Write(1u);
            byte comp = fmt switch { BlpFormat.Palette => 1, BlpFormat.Raw => 3, _ => 2 };
            byte alphaDepth = fmt switch { BlpFormat.Dxt1 => 0, _ => 8 };
            byte alphaType = fmt == BlpFormat.Dxt5 ? (byte)7 : (byte)0;
            w.Write(comp); w.Write(alphaDepth); w.Write(alphaType); w.Write((byte)(levels.Count > 1 ? 1 : 0));
            w.Write(img.Width); w.Write(img.Height);
            uint off = 148 + 1024;
            for (int i = 0; i < 16; i++) { w.Write(i < data.Count ? off : 0u); if (i < data.Count) off += (uint)data[i].Length; }
            for (int i = 0; i < 16; i++) w.Write(i < data.Count ? (uint)data[i].Length : 0u);
            for (int i = 0; i < 256; i++) w.Write(palette != null ? palette[i] : 0u);
            foreach (var d in data) w.Write(d);
            return ms.ToArray();
        }

        public static BgraImage Half(BgraImage s)
        {
            int w = Math.Max(1, s.Width / 2), h = Math.Max(1, s.Height / 2);
            var o = new BgraImage { Width = w, Height = h, Pixels = new byte[w * h * 4] };
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    for (int c = 0; c < 4; c++)
                    {
                        int sum = 0, n = 0;
                        for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++)
                        {
                            int sx = Math.Min(s.Width - 1, x * 2 + dx), sy = Math.Min(s.Height - 1, y * 2 + dy);
                            sum += s.Pixels[(sy * s.Width + sx) * 4 + c]; n++;
                        }
                        o.Pixels[(y * w + x) * 4 + c] = (byte)((sum + n / 2) / n);
                    }
            return o;
        }

        /// <summary>Bilinear resize (used to fit imported pictures to icon size).</summary>
        public static BgraImage Resize(BgraImage s, int w, int h)
        {
            if (s.Width == w && s.Height == h) return s;
            // shrink by halves first for quality, then bilinear
            while (s.Width >= w * 2 && s.Height >= h * 2) s = Half(s);
            var o = new BgraImage { Width = w, Height = h, Pixels = new byte[w * h * 4] };
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    double fx = (x + 0.5) * s.Width / w - 0.5, fy = (y + 0.5) * s.Height / h - 0.5;
                    int x0 = Math.Max(0, (int)Math.Floor(fx)), y0 = Math.Max(0, (int)Math.Floor(fy));
                    int x1 = Math.Min(s.Width - 1, x0 + 1), y1 = Math.Min(s.Height - 1, y0 + 1);
                    double ax = Math.Clamp(fx - x0, 0, 1), ay = Math.Clamp(fy - y0, 0, 1);
                    for (int c = 0; c < 4; c++)
                    {
                        double a = s.Pixels[(y0 * s.Width + x0) * 4 + c] * (1 - ax) + s.Pixels[(y0 * s.Width + x1) * 4 + c] * ax;
                        double b = s.Pixels[(y1 * s.Width + x0) * 4 + c] * (1 - ax) + s.Pixels[(y1 * s.Width + x1) * 4 + c] * ax;
                        o.Pixels[(y * w + x) * 4 + c] = (byte)Math.Round(a * (1 - ay) + b * ay);
                    }
                }
            return o;
        }

        static ushort To565(int r, int g, int b) => (ushort)(((r * 31 + 127) / 255) << 11 | ((g * 63 + 127) / 255) << 5 | ((b * 31 + 127) / 255));
        static void From565(ushort c, out int r, out int g, out int b) { r = (c >> 11) & 31; g = (c >> 5) & 63; b = c & 31; r = (r << 3) | (r >> 2); g = (g << 2) | (g >> 4); b = (b << 3) | (b >> 2); }

        static byte[] Dxt(BgraImage img, bool alpha)
        {
            int bw = Math.Max(1, (img.Width + 3) / 4), bh = Math.Max(1, (img.Height + 3) / 4);
            int size = alpha ? 16 : 8;
            var o = new byte[bw * bh * size];
            var px = new int[16, 4];
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++)
                {
                    for (int k = 0; k < 16; k++)
                    {
                        int x = Math.Min(img.Width - 1, bx * 4 + k % 4), y = Math.Min(img.Height - 1, by * 4 + k / 4);
                        int q = (y * img.Width + x) * 4;
                        px[k, 0] = img.Pixels[q + 2]; px[k, 1] = img.Pixels[q + 1]; px[k, 2] = img.Pixels[q]; px[k, 3] = img.Pixels[q + 3];
                    }
                    int p = (by * bw + bx) * size;
                    if (alpha)
                    {
                        int amin = 255, amax = 0;
                        for (int k = 0; k < 16; k++) { amin = Math.Min(amin, px[k, 3]); amax = Math.Max(amax, px[k, 3]); }
                        o[p] = (byte)amax; o[p + 1] = (byte)amin;
                        var tbl = new int[8]; tbl[0] = amax; tbl[1] = amin;
                        if (amax > amin) for (int k = 1; k < 7; k++) tbl[k + 1] = ((7 - k) * amax + k * amin) / 7;
                        else for (int k = 2; k < 8; k++) tbl[k] = amax;
                        ulong bits = 0;
                        for (int k = 0; k < 16; k++)
                        {
                            int best = 0, bd = int.MaxValue;
                            for (int j = 0; j < 8; j++) { int d = Math.Abs(tbl[j] - px[k, 3]); if (d < bd) { bd = d; best = j; } }
                            bits |= (ulong)best << (3 * k);
                        }
                        for (int k = 0; k < 6; k++) o[p + 2 + k] = (byte)(bits >> (8 * k));
                        p += 8;
                    }
                    // colour endpoints: the two most distant pixels along the bounding box diagonal
                    int[] mn = { 255, 255, 255 }, mx = { 0, 0, 0 };
                    for (int k = 0; k < 16; k++) for (int c = 0; c < 3; c++) { mn[c] = Math.Min(mn[c], px[k, c]); mx[c] = Math.Max(mx[c], px[k, c]); }
                    ushort c0 = To565(mx[0], mx[1], mx[2]), c1 = To565(mn[0], mn[1], mn[2]);
                    if (c0 < c1) (c0, c1) = (c1, c0);
                    From565(c0, out var r0, out var g0, out var b0); From565(c1, out var r1, out var g1, out var b1);
                    var pal = new[,] { { r0, g0, b0 }, { r1, g1, b1 }, { (2 * r0 + r1) / 3, (2 * g0 + g1) / 3, (2 * b0 + b1) / 3 }, { (r0 + 2 * r1) / 3, (g0 + 2 * g1) / 3, (b0 + 2 * b1) / 3 } };
                    uint idx = 0;
                    if (c0 != c1)
                        for (int k = 0; k < 16; k++)
                        {
                            int best = 0, bd = int.MaxValue;
                            for (int j = 0; j < 4; j++) { int dr = pal[j, 0] - px[k, 0], dg = pal[j, 1] - px[k, 1], db = pal[j, 2] - px[k, 2]; int d = dr * dr + dg * dg + db * db; if (d < bd) { bd = d; best = j; } }
                            idx |= (uint)best << (2 * k);
                        }
                    o[p] = (byte)c0; o[p + 1] = (byte)(c0 >> 8); o[p + 2] = (byte)c1; o[p + 3] = (byte)(c1 >> 8);
                    BitConverter.GetBytes(idx).CopyTo(o, p + 4);
                }
            return o;
        }

        /// <summary>Median-cut quantisation to at most <paramref name="n"/> colours (palette as BGRA uint, alpha byte unused).</summary>
        static uint[] MedianCut(BgraImage img, int n)
        {
            var colors = new List<int>();
            for (int i = 0; i < img.Pixels.Length; i += 4) colors.Add(img.Pixels[i + 2] << 16 | img.Pixels[i + 1] << 8 | img.Pixels[i]);
            var boxes = new List<List<int>> { colors.Distinct().ToList() };
            while (boxes.Count < n)
            {
                var box = boxes.Where(b => b.Count > 1).OrderByDescending(b => b.Count).FirstOrDefault();
                if (box == null) break;
                int Ch(int c, int k) => (c >> (16 - 8 * k)) & 255;
                int axis = Enumerable.Range(0, 3).OrderByDescending(k => box.Max(c => Ch(c, k)) - box.Min(c => Ch(c, k))).First();
                box.Sort((a, b) => Ch(a, axis).CompareTo(Ch(b, axis)));
                boxes.Remove(box);
                boxes.Add(box.Take(box.Count / 2).ToList()); boxes.Add(box.Skip(box.Count / 2).ToList());
            }
            var pal = new uint[256];
            for (int i = 0; i < boxes.Count; i++)
            {
                var b = boxes[i];
                int r = (int)b.Average(c => (c >> 16) & 255), g = (int)b.Average(c => (c >> 8) & 255), bl = (int)b.Average(c => c & 255);
                pal[i] = (uint)(bl | g << 8 | r << 16);
            }
            return pal;
        }

        static byte[] Paletted(BgraImage img, uint[] pal)
        {
            int n = img.Width * img.Height;
            var o = new byte[n * 2];
            var cache = new Dictionary<int, byte>();
            for (int i = 0; i < n; i++)
            {
                int b = img.Pixels[i * 4], g = img.Pixels[i * 4 + 1], r = img.Pixels[i * 4 + 2];
                int key = r << 16 | g << 8 | b;
                if (!cache.TryGetValue(key, out var best))
                {
                    int bd = int.MaxValue;
                    for (int j = 0; j < 256; j++)
                    {
                        int pr = (int)(pal[j] >> 16) & 255, pg = (int)(pal[j] >> 8) & 255, pb = (int)pal[j] & 255;
                        int d = (pr - r) * (pr - r) + (pg - g) * (pg - g) + (pb - b) * (pb - b);
                        if (d < bd) { bd = d; best = (byte)j; }
                    }
                    cache[key] = best;
                }
                o[i] = best;
                o[n + i] = img.Pixels[i * 4 + 3];
            }
            return o;
        }
    }

    /// <summary>TGA reader (uncompressed and RLE; 24/32-bit colour and 8-bit grey). Studio's own code.</summary>
    public static class Tga
    {
        public static BgraImage Decode(byte[] b)
        {
            if (b == null || b.Length < 18) throw new InvalidDataException("Not a TGA file.");
            int idLen = b[0], cmapType = b[1], type = b[2];
            int w = BitConverter.ToUInt16(b, 12), h = BitConverter.ToUInt16(b, 14), bpp = b[16], desc = b[17];
            if (cmapType != 0 || !(type == 2 || type == 3 || type == 10 || type == 11)) throw new InvalidDataException("Only true-colour and greyscale TGA files are supported.");
            if (w == 0 || h == 0 || w > 8192 || h > 8192) throw new InvalidDataException("TGA size isn't valid.");
            int bytes = bpp / 8;
            if (!(bytes == 1 || bytes == 3 || bytes == 4)) throw new InvalidDataException($"{bpp}-bit TGA isn't supported.");
            int p = 18 + idLen;
            var raw = new byte[w * h * bytes];
            if (type == 2 || type == 3) { if (p + raw.Length > b.Length) throw new InvalidDataException("TGA file is cut short."); Array.Copy(b, p, raw, 0, raw.Length); }
            else
            {
                int o = 0;
                while (o < raw.Length)
                {
                    if (p >= b.Length) throw new InvalidDataException("TGA file is cut short.");
                    int hdr = b[p++], count = (hdr & 0x7F) + 1;
                    if ((hdr & 0x80) != 0) { for (int i = 0; i < count && o < raw.Length; i++) { Array.Copy(b, p, raw, o, bytes); o += bytes; } p += bytes; }
                    else { int n = Math.Min(count * bytes, raw.Length - o); Array.Copy(b, p, raw, o, n); o += n; p += count * bytes; }
                }
            }
            var img = new BgraImage { Width = w, Height = h, Pixels = new byte[w * h * 4] };
            bool topDown = (desc & 0x20) != 0;
            for (int y = 0; y < h; y++)
            {
                int sy = topDown ? y : h - 1 - y;
                for (int x = 0; x < w; x++)
                {
                    int s = (sy * w + x) * bytes, d = (y * w + x) * 4;
                    if (bytes == 1) { img.Pixels[d] = img.Pixels[d + 1] = img.Pixels[d + 2] = raw[s]; img.Pixels[d + 3] = 255; }
                    else { img.Pixels[d] = raw[s]; img.Pixels[d + 1] = raw[s + 1]; img.Pixels[d + 2] = raw[s + 2]; img.Pixels[d + 3] = bytes == 4 ? raw[s + 3] : (byte)255; }
                }
            }
            return img;
        }
    }

    /// <summary>Header facts of a WotLK M2 model (for the import preview).</summary>
    public sealed class M2Info
    {
        public uint Version; public string Name; public int Vertices, Views, Sequences, Bones; public List<string> Textures = new List<string>();
        public static M2Info Read(byte[] b)
        {
            if (b == null || b.Length < 0x60 || Encoding.ASCII.GetString(b, 0, 4) != "MD20") throw new InvalidDataException("Not an M2 model (MD20). Studio reads 3.3.5a .m2 files.");
            var m = new M2Info { Version = BitConverter.ToUInt32(b, 4) };
            string Str(int cnt, int ofs) => cnt <= 0 || ofs <= 0 || ofs + cnt > b.Length ? "" : Encoding.ASCII.GetString(b, ofs, cnt).TrimEnd('\0');
            m.Name = Str(BitConverter.ToInt32(b, 0x08), BitConverter.ToInt32(b, 0x0C));
            m.Sequences = BitConverter.ToInt32(b, 0x1C);
            m.Bones = BitConverter.ToInt32(b, 0x2C);
            m.Vertices = BitConverter.ToInt32(b, 0x3C);
            m.Views = BitConverter.ToInt32(b, 0x44);
            int tc = BitConverter.ToInt32(b, 0x50), to = BitConverter.ToInt32(b, 0x54);
            for (int i = 0; i < tc && i < 64 && to + i * 16 + 16 <= b.Length; i++)
            {
                int q = to + i * 16;
                uint type = BitConverter.ToUInt32(b, q);
                var fn = Str(BitConverter.ToInt32(b, q + 8), BitConverter.ToInt32(b, q + 12));
                m.Textures.Add(type == 0 ? fn : $"(replaceable texture type {type})");
            }
            return m;
        }
    }

    /// <summary>One imported file, kept in the work folder's studio_manifest.json.</summary>
    public sealed class AssetRecord
    {
        public DateTime Utc { get; set; } = DateTime.UtcNow;
        public string Kind { get; set; }           // icon, model, sound, file
        public string Source { get; set; }
        public string ArchivePath { get; set; }
        public string Note { get; set; }
        public List<string> DbcRows { get; set; } = new List<string>();
    }

    /// <summary>Paths, names and DBC rows for imported assets. Pure functions over tables: nothing is written here.</summary>
    public static class AssetImport
    {
        public const string Manifest = "studio_manifest.json";

        public static string SafeName(string s)
        {
            var name = new string((s ?? "").Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
            return name.Length == 0 ? "Custom" : name;
        }

        public static string IconPath(string name) => @"Interface\Icons\" + SafeName(name) + ".blp";
        public static string ModelFolder(string name) => @"Creature\" + SafeName(name) + @"\";
        public static string SoundPath(string name, string ext) => @"Sound\Custom\" + SafeName(name) + ext.ToLowerInvariant();

        /// <summary>Where a file of this kind usually goes inside a patch.</summary>
        public static readonly (string kind, string folder, string what)[] Guidance =
        {
            ("Spell / item icon", @"Interface\Icons\", "64×64 BLP. SpellIcon.dbc names it without .blp; ItemDisplayInfo uses just the file name."),
            ("Creature model", @"Creature\<Name>\", "<Name>.m2, <Name>00.skin (and 01-03), textures .blp next to them. CreatureModelData uses the path with .mdx."),
            ("Item model", @"Item\ObjectComponents\<Slot>\", "Weapon/shield/helm models and textures (Weapon, Shield, Head, Shoulder…)."),
            ("Sound", @"Sound\<Folder>\", "WAV or MP3, like the client's own sounds. SoundEntries lists the file and its folder."),
            ("Music", @"Sound\Music\", "Zone music, referenced from SoundEntries and ZoneMusic.dbc."),
            ("Interface", @"Interface\", "Textures and UI files; AddOns go in the AddOns folder of the client instead."),
            ("Loading screen", @"Interface\Glues\LoadingScreens\", "BLP referenced from LoadingScreens.dbc."),
            ("Minimap / world map", @"Interface\WorldMap\ and textures\Minimap\", "Map tiles for custom zones."),
        };

        /// <summary>A new SpellIcon.dbc row (next free ID at or above start) pointing at Interface\Icons\name.</summary>
        public static DbcRow IconRow(DbcTable spellIcon, string name, uint start)
        {
            var r = spellIcon.NewRow();
            r.V[0] = spellIcon.NextFreeId(start);
            spellIcon.TrySetField(r, 1, @"Interface\Icons\" + SafeName(name), out _);
            return r;
        }

        /// <summary>A new ItemDisplayInfo.dbc row copied from <paramref name="baseId"/> with the inventory icon replaced.</summary>
        public static DbcRow ItemDisplayRow(DbcTable idi, uint baseId, string iconName, uint start)
        {
            var src = idi.Find(baseId) ?? throw new ArgumentException($"ItemDisplayInfo {baseId} doesn't exist.");
            var r = src.Clone();
            r.V[0] = idi.NextFreeId(start);
            idi.TrySetField(r, idi.Layout.FieldByName("InventoryIcon_1"), SafeName(iconName), out _);
            return r;
        }

        /// <summary>CreatureModelData + CreatureDisplayInfo rows for a model at Creature\Name\Name.m2 with up to 3 skin textures.</summary>
        public static (DbcRow model, DbcRow display) CreatureRows(DbcTable cmd, DbcTable cdi, uint baseModelId, uint baseDisplayId, string name, IList<string> textures, uint start, float scale = 1f)
        {
            var bm = cmd.Find(baseModelId) ?? throw new ArgumentException($"CreatureModelData {baseModelId} doesn't exist.");
            var model = bm.Clone();
            model.V[0] = cmd.NextFreeId(start);
            cmd.TrySetField(model, cmd.Layout.FieldByName("ModelName"), ModelFolder(name) + SafeName(name) + ".mdx", out _);
            var bd = cdi.Find(baseDisplayId) ?? throw new ArgumentException($"CreatureDisplayInfo {baseDisplayId} doesn't exist.");
            var disp = bd.Clone();
            disp.V[0] = cdi.NextFreeId(start);
            cdi.TrySetField(disp, cdi.Layout.FieldByName("ModelID"), model.V[0].ToString(CultureInfo.InvariantCulture), out _);
            cdi.TrySetField(disp, cdi.Layout.FieldByName("ExtendedDisplayInfoID"), "0", out _);
            cdi.TrySetField(disp, cdi.Layout.FieldByName("CreatureModelScale"), scale.ToString(CultureInfo.InvariantCulture), out _);
            for (int i = 0; i < 3; i++)
                cdi.TrySetField(disp, cdi.Layout.FieldByName("TextureVariation_" + (i + 1)), textures != null && i < textures.Count ? Path.GetFileNameWithoutExtension(textures[i]) : "", out _);
            return (model, disp);
        }

        /// <summary>A SoundEntries.dbc row for one file in Sound\Custom (copied from a base row for volume/distances).</summary>
        public static DbcRow SoundRow(DbcTable se, uint baseId, string name, string fileName, string folder, uint start)
        {
            var src = se.Find(baseId);
            var r = src != null ? src.Clone() : se.NewRow();
            r.V[0] = se.NextFreeId(start);
            var l = se.Layout;
            se.TrySetField(r, l.FieldByName("Name"), SafeName(name), out _);
            for (int i = 1; i <= 10; i++)
            {
                se.TrySetField(r, l.FieldByName("File_" + i), i == 1 ? fileName : "", out _);
                se.TrySetField(r, l.FieldByName("Freq_" + i), i == 1 ? "1" : "0", out _);
            }
            se.TrySetField(r, l.FieldByName("DirectoryBase"), folder.TrimEnd('\\'), out _);
            return r;
        }

        public static List<AssetRecord> ReadManifest(string workFolder)
        {
            try { var p = Path.Combine(workFolder, Manifest); return File.Exists(p) ? JsonSerializer.Deserialize<List<AssetRecord>>(File.ReadAllText(p)) ?? new List<AssetRecord>() : new List<AssetRecord>(); }
            catch { return new List<AssetRecord>(); }
        }

        public static void AddToManifest(string workFolder, AssetRecord rec)
        {
            var list = ReadManifest(workFolder);
            list.RemoveAll(x => string.Equals(x.ArchivePath, rec.ArchivePath, StringComparison.OrdinalIgnoreCase));
            list.Add(rec);
            Directory.CreateDirectory(workFolder);
            File.WriteAllText(Path.Combine(workFolder, Manifest), JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
        }

        /// <summary>Copies a file into the work folder at its archive path (folders created). Returns the full path.</summary>
        public static string Place(string workFolder, string archivePath, byte[] bytes)
        {
            var full = Path.GetFullPath(Path.Combine(workFolder, archivePath.Replace('/', '\\')));
            if (!full.StartsWith(Path.GetFullPath(workFolder), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("The path must stay inside the work folder.");
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllBytes(full, bytes);
            return full;
        }
    }
}
