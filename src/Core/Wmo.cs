using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace AndersCraftStudio.Core
{
    /// <summary>
    /// Reader for 3.3.5a WMO world models (buildings, bridges, big game objects): the root file's textures and materials,
    /// and each group file's vertices, triangles and render batches. Written from the public format description. Read-only.
    /// </summary>
    public sealed class Wmo
    {
        public sealed class Material { public uint Flags, Shader, Blend; public string Texture; }
        public sealed class Batch { public int StartIndex, Count; public int Material; }
        public sealed class Group
        {
            public Vector3[] Positions = new Vector3[0];
            public Vector3[] Normals = new Vector3[0];
            public Vector2[] Uvs = new Vector2[0];
            public ushort[] Indices = new ushort[0];
            public List<Batch> Batches = new List<Batch>();
            public uint Flags;
            public bool Indoor => (Flags & 0x2000) != 0;
        }

        public int GroupCount, TextureCount;
        public List<Material> Materials = new List<Material>();
        public Vector3 BoundsMin, BoundsMax;
        public List<Group> Groups = new List<Group>();
        public int DoodadCount;

        /// <summary>Chunks in a WMO file: their 4-letter name (as the format spells it, e.g. MOHD) and body.</summary>
        public static IEnumerable<(string id, int ofs, int size)> Chunks(byte[] b, int start = 0, int end = -1)
        {
            if (end < 0) end = b.Length;
            int p = start;
            while (p + 8 <= end)
            {
                var id = new string(Encoding.ASCII.GetString(b, p, 4).Reverse().ToArray());
                int size = BitConverter.ToInt32(b, p + 4);
                if (size < 0 || p + 8 + (long)size > end) yield break;
                yield return (id, p + 8, size);
                p += 8 + size;
            }
        }

        public static Wmo ReadRoot(byte[] b)
        {
            if (b == null || b.Length < 12) throw new InvalidDataException("Not a WMO file.");
            var w = new Wmo();
            var ch = Chunks(b).ToList();
            if (ch.Count == 0 || ch[0].id != "MVER") throw new InvalidDataException("Not a WMO file (no MVER).");
            if (!ch.Any(c => c.id == "MOHD")) throw new InvalidDataException("This is a WMO group file, not the root file.");
            string texBlob = "";
            int texOfs = 0;
            foreach (var (id, o, size) in ch)
            {
                switch (id)
                {
                    case "MOHD":
                        w.TextureCount = BitConverter.ToInt32(b, o); w.GroupCount = BitConverter.ToInt32(b, o + 4);
                        w.DoodadCount = BitConverter.ToInt32(b, o + 20);
                        w.BoundsMin = new Vector3(BitConverter.ToSingle(b, o + 0x24), BitConverter.ToSingle(b, o + 0x28), BitConverter.ToSingle(b, o + 0x2C));
                        w.BoundsMax = new Vector3(BitConverter.ToSingle(b, o + 0x30), BitConverter.ToSingle(b, o + 0x34), BitConverter.ToSingle(b, o + 0x38));
                        break;
                    case "MOTX": texBlob = Encoding.ASCII.GetString(b, o, size); texOfs = o; break;
                    case "MOMT":
                        for (int i = 0; i + 64 <= size; i += 64)
                        {
                            uint t1 = BitConverter.ToUInt32(b, o + i + 12);
                            w.Materials.Add(new Material { Flags = BitConverter.ToUInt32(b, o + i), Shader = BitConverter.ToUInt32(b, o + i + 4), Blend = BitConverter.ToUInt32(b, o + i + 8), Texture = StringAt(texBlob, (int)t1) });
                        }
                        break;
                }
            }
            return w;
        }

        static string StringAt(string blob, int ofs)
        {
            if (ofs < 0 || ofs >= blob.Length) return null;
            int e = blob.IndexOf('\0', ofs);
            var s = (e < 0 ? blob.Substring(ofs) : blob.Substring(ofs, e - ofs)).Trim();
            return s.Length == 0 ? null : s;
        }

        public static Group ReadGroup(byte[] b)
        {
            var g = new Group();
            var top = Chunks(b).ToList();
            var mogp = top.FirstOrDefault(c => c.id == "MOGP");
            if (mogp.id == null) throw new InvalidDataException("Not a WMO group file (no MOGP).");
            g.Flags = BitConverter.ToUInt32(b, mogp.ofs + 8);
            const int header = 0x44;
            foreach (var (id, o, size) in Chunks(b, mogp.ofs + header, mogp.ofs + mogp.size))
            {
                switch (id)
                {
                    case "MOVI": g.Indices = new ushort[size / 2]; for (int i = 0; i < g.Indices.Length; i++) g.Indices[i] = BitConverter.ToUInt16(b, o + i * 2); break;
                    case "MOVT": g.Positions = new Vector3[size / 12]; for (int i = 0; i < g.Positions.Length; i++) g.Positions[i] = new Vector3(BitConverter.ToSingle(b, o + i * 12), BitConverter.ToSingle(b, o + i * 12 + 4), BitConverter.ToSingle(b, o + i * 12 + 8)); break;
                    case "MONR": g.Normals = new Vector3[size / 12]; for (int i = 0; i < g.Normals.Length; i++) g.Normals[i] = new Vector3(BitConverter.ToSingle(b, o + i * 12), BitConverter.ToSingle(b, o + i * 12 + 4), BitConverter.ToSingle(b, o + i * 12 + 8)); break;
                    case "MOTV": if (g.Uvs.Length == 0) { g.Uvs = new Vector2[size / 8]; for (int i = 0; i < g.Uvs.Length; i++) g.Uvs[i] = new Vector2(BitConverter.ToSingle(b, o + i * 8), BitConverter.ToSingle(b, o + i * 8 + 4)); } break;
                    case "MOBA":
                        for (int i = 0; i + 24 <= size; i += 24)
                            g.Batches.Add(new Batch { StartIndex = BitConverter.ToInt32(b, o + i + 12), Count = BitConverter.ToUInt16(b, o + i + 16), Material = b[o + i + 23] });
                        break;
                }
            }
            return g;
        }

        /// <summary>Group files sit next to the root: Name_000.wmo, Name_001.wmo …</summary>
        public static string GroupPath(string rootPath, int i) =>
            Path.Combine(Path.GetDirectoryName(rootPath) ?? "", Path.GetFileNameWithoutExtension(rootPath) + $"_{i:D3}.wmo");

        /// <summary>Loads the root and up to <paramref name="maxGroups"/> groups through <paramref name="read"/>. Missing groups are skipped.</summary>
        public static Wmo Load(string rootPath, Func<string, byte[]> read, int maxGroups = 512)
        {
            var w = ReadRoot(read(rootPath) ?? throw new FileNotFoundException(rootPath + " wasn't found."));
            for (int i = 0; i < Math.Min(w.GroupCount, maxGroups); i++)
            {
                var gb = read(GroupPath(rootPath, i));
                if (gb == null) continue;
                try { w.Groups.Add(ReadGroup(gb)); } catch { }
            }
            return w;
        }

        public int Triangles => Groups.Sum(g => g.Batches.Sum(x => x.Count) / 3);
    }
}
