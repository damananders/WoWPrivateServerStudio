using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AndersCraftStudio.Core
{
    /// <summary>A decoded image: BGRA, 4 bytes per pixel, top row first.</summary>
    public sealed class BgraImage
    {
        public int Width, Height;
        public byte[] Pixels;
    }

    /// <summary>
    /// Decoder for BLP2 textures (the 3.3.5a client's icons and textures): palettized with 0/1/4/8-bit alpha,
    /// DXT1, DXT3, DXT5 and plain BGRA. Only the first (largest) mip level is decoded. Studio's own code.
    /// </summary>
    public static class Blp
    {
        public static BgraImage Decode(byte[] b)
        {
            if (b == null || b.Length < 148 || Encoding.ASCII.GetString(b, 0, 4) != "BLP2") throw new InvalidDataException("Not a BLP2 texture.");
            uint type = BitConverter.ToUInt32(b, 4);
            byte compression = b[8], alphaDepth = b[9], alphaType = b[10];
            int w = (int)BitConverter.ToUInt32(b, 12), h = (int)BitConverter.ToUInt32(b, 16);
            uint off = BitConverter.ToUInt32(b, 20), size = BitConverter.ToUInt32(b, 20 + 64);
            if (type != 1) throw new InvalidDataException("JPEG-compressed BLP isn't supported.");
            if (w <= 0 || h <= 0 || w > 4096 || h > 4096 || off + size > b.Length) throw new InvalidDataException("BLP header doesn't match the file.");
            var img = new BgraImage { Width = w, Height = h, Pixels = new byte[w * h * 4] };
            switch (compression)
            {
                case 1: Palette(b, (int)off, w, h, alphaDepth, img.Pixels); break;
                case 2:
                    if (alphaType == 7) Dxt(b, (int)off, w, h, 5, img.Pixels, true);
                    else if (alphaType == 1 && alphaDepth > 1) Dxt(b, (int)off, w, h, 3, img.Pixels, true);
                    else Dxt(b, (int)off, w, h, 1, img.Pixels, alphaDepth > 0);
                    break;
                case 3: Array.Copy(b, off, img.Pixels, 0, Math.Min(img.Pixels.Length, (int)size)); break;
                default: throw new InvalidDataException("Unknown BLP compression " + compression + ".");
            }
            return img;
        }

        static void Palette(byte[] b, int off, int w, int h, int alphaDepth, byte[] o)
        {
            int n = w * h;
            for (int i = 0; i < n; i++)
            {
                int idx = b[off + i];
                int p = 148 + idx * 4;
                o[i * 4] = b[p]; o[i * 4 + 1] = b[p + 1]; o[i * 4 + 2] = b[p + 2];
                int a = 255;
                int ao = off + n;
                switch (alphaDepth)
                {
                    case 1: a = ((b[ao + i / 8] >> (i % 8)) & 1) != 0 ? 255 : 0; break;
                    case 4: { int v = (b[ao + i / 2] >> ((i % 2) * 4)) & 0xF; a = v * 17; break; }
                    case 8: a = b[ao + i]; break;
                }
                o[i * 4 + 3] = (byte)a;
            }
        }

        static void Color565(ushort c, out int r, out int g, out int bl)
        {
            r = (c >> 11) & 31; g = (c >> 5) & 63; bl = c & 31;
            r = (r << 3) | (r >> 2); g = (g << 2) | (g >> 4); bl = (bl << 3) | (bl >> 2);
        }

        static void Dxt(byte[] b, int off, int w, int h, int kind, byte[] o, bool alpha1)
        {
            int bw = (w + 3) / 4, bh = (h + 3) / 4;
            int blockSize = kind == 1 ? 8 : 16;
            var cols = new int[4, 4];
            var alphas = new byte[16];
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++)
                {
                    int p = off + (by * bw + bx) * blockSize;
                    if (p + blockSize > b.Length) return;
                    for (int k = 0; k < 16; k++) alphas[k] = 255;
                    if (kind == 3)
                    {
                        for (int k = 0; k < 16; k++) { int v = (b[p + k / 2] >> ((k % 2) * 4)) & 0xF; alphas[k] = (byte)(v * 17); }
                        p += 8;
                    }
                    else if (kind == 5)
                    {
                        int a0 = b[p], a1 = b[p + 1];
                        ulong bits = 0;
                        for (int k = 0; k < 6; k++) bits |= (ulong)b[p + 2 + k] << (8 * k);
                        var tbl = new int[8];
                        tbl[0] = a0; tbl[1] = a1;
                        if (a0 > a1) for (int k = 1; k < 7; k++) tbl[k + 1] = ((7 - k) * a0 + k * a1) / 7;
                        else { for (int k = 1; k < 5; k++) tbl[k + 1] = ((5 - k) * a0 + k * a1) / 5; tbl[6] = 0; tbl[7] = 255; }
                        for (int k = 0; k < 16; k++) alphas[k] = (byte)tbl[(int)((bits >> (3 * k)) & 7)];
                        p += 8;
                    }
                    ushort c0 = BitConverter.ToUInt16(b, p), c1 = BitConverter.ToUInt16(b, p + 2);
                    uint idx = BitConverter.ToUInt32(b, p + 4);
                    Color565(c0, out var r0, out var g0, out var b0);
                    Color565(c1, out var r1, out var g1, out var b1);
                    var pal = new int[4, 4];
                    pal[0, 0] = r0; pal[0, 1] = g0; pal[0, 2] = b0; pal[0, 3] = 255;
                    pal[1, 0] = r1; pal[1, 1] = g1; pal[1, 2] = b1; pal[1, 3] = 255;
                    if (c0 > c1 || kind != 1)
                    {
                        pal[2, 0] = (2 * r0 + r1) / 3; pal[2, 1] = (2 * g0 + g1) / 3; pal[2, 2] = (2 * b0 + b1) / 3; pal[2, 3] = 255;
                        pal[3, 0] = (r0 + 2 * r1) / 3; pal[3, 1] = (g0 + 2 * g1) / 3; pal[3, 2] = (b0 + 2 * b1) / 3; pal[3, 3] = 255;
                    }
                    else
                    {
                        pal[2, 0] = (r0 + r1) / 2; pal[2, 1] = (g0 + g1) / 2; pal[2, 2] = (b0 + b1) / 2; pal[2, 3] = 255;
                        pal[3, 0] = 0; pal[3, 1] = 0; pal[3, 2] = 0; pal[3, 3] = alpha1 ? 0 : 255;
                    }
                    for (int k = 0; k < 16; k++)
                    {
                        int x = bx * 4 + k % 4, y = by * 4 + k / 4;
                        if (x >= w || y >= h) continue;
                        int ci = (int)((idx >> (2 * k)) & 3);
                        int q = (y * w + x) * 4;
                        o[q] = (byte)pal[ci, 2]; o[q + 1] = (byte)pal[ci, 1]; o[q + 2] = (byte)pal[ci, 0];
                        o[q + 3] = kind == 1 ? (byte)pal[ci, 3] : alphas[k];
                    }
                }
        }

        /// <summary>Builds a small BLP2 (BGRA, compression 3) — used by tests and nothing else.</summary>
        public static byte[] BuildRaw(BgraImage img)
        {
            var ms = new MemoryStream(); var bw = new BinaryWriter(ms);
            bw.Write(Encoding.ASCII.GetBytes("BLP2")); bw.Write(1u); bw.Write((byte)3); bw.Write((byte)8); bw.Write((byte)8); bw.Write((byte)0);
            bw.Write(img.Width); bw.Write(img.Height);
            for (int i = 0; i < 16; i++) bw.Write(i == 0 ? 1172u : 0u);
            for (int i = 0; i < 16; i++) bw.Write(i == 0 ? (uint)img.Pixels.Length : 0u);
            for (int i = 0; i < 256; i++) bw.Write(0u);
            bw.Write(img.Pixels);
            return ms.ToArray();
        }
    }

    /// <summary>
    /// Reads any file from the client's archives the way the game does: the highest-priority archive that has it wins.
    /// Works without a (listfile): files are found by hashing the path. Read-only.
    /// </summary>
    public sealed class ClientFiles : IDisposable
    {
        readonly List<(string name, int rank, MpqReader reader)> archives = new List<(string, int, MpqReader)>();
        public string DataDir { get; }
        public int ArchiveCount => archives.Count;

        public ClientFiles(string dataDir)
        {
            DataDir = dataDir;
            if (!Directory.Exists(dataDir)) return;
            var files = Directory.GetFiles(dataDir, "*.mpq").Select(f => (f, false)).ToList();
            foreach (var sub in Directory.GetDirectories(dataDir)) files.AddRange(Directory.GetFiles(sub, "*.mpq").Select(f => (f, true)));
            foreach (var (f, loc) in files)
            {
                int rank = ClientDbcSource.Rank(Path.GetFileName(f), loc);
                if (rank < 0) continue;
                try { archives.Add((Path.GetRelativePath(dataDir, f), rank, new MpqReader(f))); } catch { }
            }
            archives.Sort((a, b) => b.rank.CompareTo(a.rank));   // highest priority first
        }

        /// <summary>The bytes of a client file (e.g. Interface\Icons\Spell_Fire_FlameBolt.blp), or null.</summary>
        public byte[] Read(string path, out string from)
        {
            from = null;
            path = path.Replace('/', '\\');
            lock (archives)
                foreach (var (name, _, r) in archives)
                {
                    var e = r.Find(path);
                    if (e == null) continue;
                    from = name;
                    return r.Read(e);
                }
            return null;
        }

        public void Dispose() { foreach (var a in archives) a.reader.Dispose(); archives.Clear(); }
    }

    /// <summary>Decoded icons by texture path, kept in memory (and optionally on disk as raw BGRA) so pickers stay fast.</summary>
    public sealed class IconCache
    {
        readonly Func<string, byte[]> read;
        readonly string diskDir;
        readonly Dictionary<string, BgraImage> mem = new Dictionary<string, BgraImage>(StringComparer.OrdinalIgnoreCase);
        public int Decoded { get; private set; }
        public int FromDisk { get; private set; }

        /// <param name="read">Returns the BLP bytes for a path (with .blp), or null.</param>
        /// <param name="diskDir">Folder for decoded icons (null = memory only). The caller makes it unique per client.</param>
        public IconCache(Func<string, byte[]> read, string diskDir = null) { this.read = read; this.diskDir = diskDir; }

        public static string BlpPath(string texture) => string.IsNullOrEmpty(texture) ? null : texture.EndsWith(".blp", StringComparison.OrdinalIgnoreCase) ? texture : texture + ".blp";

        public BgraImage Get(string texture)
        {
            var path = BlpPath(texture);
            if (path == null) return null;
            lock (mem) if (mem.TryGetValue(path, out var hit)) return hit;
            BgraImage img = null;
            string disk = diskDir == null ? null : Path.Combine(diskDir, Safe(path) + ".bgra");
            try
            {
                if (disk != null && File.Exists(disk))
                {
                    var d = File.ReadAllBytes(disk);
                    img = new BgraImage { Width = BitConverter.ToInt32(d, 0), Height = BitConverter.ToInt32(d, 4), Pixels = d.Skip(8).ToArray() };
                    FromDisk++;
                }
                else
                {
                    var b = read(path);
                    if (b != null)
                    {
                        img = Blp.Decode(b);
                        Decoded++;
                        if (disk != null)
                        {
                            Directory.CreateDirectory(diskDir);
                            var outp = new byte[8 + img.Pixels.Length];
                            BitConverter.GetBytes(img.Width).CopyTo(outp, 0); BitConverter.GetBytes(img.Height).CopyTo(outp, 4);
                            img.Pixels.CopyTo(outp, 8);
                            File.WriteAllBytes(disk, outp);
                        }
                    }
                }
            }
            catch { img = null; }
            lock (mem) mem[path] = img;
            return img;
        }

        static string Safe(string p) => new string(p.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_').ToArray());
    }
}
