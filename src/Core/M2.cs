using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace AndersCraftStudio.Core
{
    /// <summary>
    /// Reader for 3.3.5a (build 12340, version 264) .m2 models and their .skin files, written from the public format
    /// description: geometry, textures, materials, bones, animation sequences. Produces meshes and skinned poses for the
    /// 3D viewer. Read-only.
    /// </summary>
    public sealed class M2Model
    {
        public struct Vertex { public Vector3 Pos, Normal; public Vector2 Uv; public byte W0, W1, W2, W3, B0, B1, B2, B3; }
        public sealed class Texture { public uint Type, Flags; public string File; }
        public sealed class Material { public ushort Flags, Blend; }
        public sealed class Sequence { public ushort Id, Variation; public uint Duration, Flags; public short Next; public ushort Alias; public bool Embedded => (Flags & 0x20) != 0; }
        public sealed class Track<T>
        {
            public ushort Interp; public short GlobalSeq;
            public List<uint[]> Times = new List<uint[]>();
            public List<T[]> Values = new List<T[]>();
            public bool IsEmpty => Times.All(t => t == null || t.Length == 0);
        }
        public sealed class Bone
        {
            public int KeyBone; public uint Flags; public short Parent; public Vector3 Pivot;
            public Track<Vector3> Translation; public Track<Quaternion> Rotation; public Track<Vector3> Scale;
        }

        public uint Version;
        public string Name;
        public uint GlobalFlags;
        public uint[] GlobalSequences = new uint[0];
        public List<Sequence> Sequences = new List<Sequence>();
        public List<Bone> Bones = new List<Bone>();
        public Vertex[] Vertices = new Vertex[0];
        public int Views;
        public List<Texture> Textures = new List<Texture>();
        public List<Material> Materials = new List<Material>();
        public short[] TextureLookup = new short[0];
        /// <summary>Texture coordinate lookup (M2 "texture unit lookup"): -1 = the layer is sphere-mapped (a reflection), not UV-mapped.</summary>
        public short[] TexCoordLookup = new short[0];
        public Vector3 BoundsMin, BoundsMax;
        public float BoundsRadius;
        public int ParticleEmitters, RibbonEmitters;
        public sealed class Attachment { public int Id; public ushort Bone; public Vector3 Position; }
        /// <summary>Points where other models are worn or held (11 head, 5/6 shoulders, 1/2 hands, 0 shield…).</summary>
        public List<Attachment> Attachments = new List<Attachment>();

        readonly byte[] b;
        M2Model(byte[] bytes) { b = bytes; }
        /// <summary>The file's bytes (emitters and other blocks are read from them on demand).</summary>
        public byte[] Bytes => b;

        public Track<float> FloatTrack(int o) => ReadTrack(o, 4, (d, p) => BitConverter.ToSingle(d, p));
        public Track<Vector3> Vector3Track(int o) => ReadTrack(o, 12, (d, p) => new Vector3(BitConverter.ToSingle(d, p), BitConverter.ToSingle(d, p + 4), BitConverter.ToSingle(d, p + 8)));
        public Track<short> ShortTrack(int o) => ReadTrack(o, 2, (d, p) => BitConverter.ToInt16(d, p));

        /// <summary>A float track's value at a time; falls back to the first sequence that has keys, then to <paramref name="def"/>.</summary>
        public float Sample(Track<float> t, int seq, uint ms, float def)
        {
            if (t == null) return def;
            var v = Value(t, seq < 0 ? 0 : seq, ms, ms, float.NaN, (x, y, f) => x + (y - x) * f);
            if (!float.IsNaN(v)) return v;
            for (int i = 0; i < t.Times.Count; i++) if (t.Times[i] != null && t.Times[i].Length > 0) return t.Values[i][0];
            return def;
        }

        // ---- little helpers over the file bytes
        int I(int o) => BitConverter.ToInt32(b, o);
        uint U(int o) => BitConverter.ToUInt32(b, o);
        ushort U16(int o) => BitConverter.ToUInt16(b, o);
        short S16(int o) => BitConverter.ToInt16(b, o);
        float F(int o) => BitConverter.ToSingle(b, o);
        Vector3 V3(int o) => new Vector3(F(o), F(o + 4), F(o + 8));
        (int n, int ofs) Arr(int o) => (I(o), I(o + 4));
        bool Fits(int ofs, int n, int size) => n >= 0 && ofs >= 0 && (long)ofs + (long)n * size <= b.Length;

        public static M2Model Read(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 0x130 || Encoding.ASCII.GetString(bytes, 0, 4) != "MD20") throw new InvalidDataException("Not an M2 model (MD20).");
            var m = new M2Model(bytes);
            m.Parse();
            return m;
        }

        void Parse()
        {
            Version = U(4);
            if (Version < 260 || Version > 264) throw new InvalidDataException($"M2 version {Version}: Studio reads 3.3.5a models (version 264).");
            var (nn, no) = Arr(0x08); Name = Fits(no, nn, 1) && nn > 0 ? Encoding.ASCII.GetString(b, no, nn).TrimEnd('\0') : "";
            GlobalFlags = U(0x10);
            var (gn, go) = Arr(0x14); if (Fits(go, gn, 4)) { GlobalSequences = new uint[gn]; for (int i = 0; i < gn; i++) GlobalSequences[i] = U(go + i * 4); }
            var (sn, so) = Arr(0x1C);
            if (Fits(so, sn, 64))
                for (int i = 0; i < sn; i++)
                {
                    int o = so + i * 64;
                    Sequences.Add(new Sequence { Id = U16(o), Variation = U16(o + 2), Duration = U(o + 4), Flags = U(o + 12), Next = S16(o + 60), Alias = U16(o + 62) });
                }
            var (vn, vo) = Arr(0x3C);
            if (!Fits(vo, vn, 48)) throw new InvalidDataException("The vertex list is cut short.");
            Vertices = new Vertex[vn];
            for (int i = 0; i < vn; i++)
            {
                int o = vo + i * 48;
                Vertices[i] = new Vertex
                {
                    Pos = V3(o), W0 = b[o + 12], W1 = b[o + 13], W2 = b[o + 14], W3 = b[o + 15], B0 = b[o + 16], B1 = b[o + 17], B2 = b[o + 18], B3 = b[o + 19],
                    Normal = V3(o + 20), Uv = new Vector2(F(o + 32), F(o + 36)),
                };
            }
            Views = I(0x44);
            var (tn, to) = Arr(0x50);
            if (Fits(to, tn, 16))
                for (int i = 0; i < tn; i++)
                {
                    int o = to + i * 16;
                    var (fn, fo) = Arr(o + 8);
                    Textures.Add(new Texture { Type = U(o), Flags = U(o + 4), File = Fits(fo, fn, 1) && fn > 0 ? Encoding.ASCII.GetString(b, fo, fn).TrimEnd('\0') : "" });
                }
            var (mn, mo) = Arr(0x70);
            if (Fits(mo, mn, 4)) for (int i = 0; i < mn; i++) Materials.Add(new Material { Flags = U16(mo + i * 4), Blend = U16(mo + i * 4 + 2) });
            var (ln, lo) = Arr(0x80);
            if (Fits(lo, ln, 2)) { TextureLookup = new short[ln]; for (int i = 0; i < ln; i++) TextureLookup[i] = S16(lo + i * 2); }
            var (cn, co) = Arr(0x88);
            if (Fits(co, cn, 2)) { TexCoordLookup = new short[cn]; for (int i = 0; i < cn; i++) TexCoordLookup[i] = S16(co + i * 2); }
            BoundsMin = V3(0xA0); BoundsMax = V3(0xAC); BoundsRadius = F(0xB8);
            RibbonEmitters = I(0x120); ParticleEmitters = I(0x128);
            var (an, ao) = Arr(0xF0);
            if (Fits(ao, an, 40)) for (int i = 0; i < an; i++) { int o = ao + i * 40; Attachments.Add(new Attachment { Id = I(o), Bone = U16(o + 4), Position = V3(o + 8) }); }
            var (bn, bo) = Arr(0x2C);
            if (Fits(bo, bn, 88))
                for (int i = 0; i < bn; i++)
                {
                    int o = bo + i * 88;
                    Bones.Add(new Bone
                    {
                        KeyBone = I(o), Flags = U(o + 4), Parent = S16(o + 8), Pivot = V3(o + 76),
                        Translation = ReadTrack(o + 16, 12, (d, p) => new Vector3(BitConverter.ToSingle(d, p), BitConverter.ToSingle(d, p + 4), BitConverter.ToSingle(d, p + 8))),
                        Rotation = ReadTrack(o + 36, 8, (d, p) => Quat(d, p)),
                        Scale = ReadTrack(o + 56, 12, (d, p) => new Vector3(BitConverter.ToSingle(d, p), BitConverter.ToSingle(d, p + 4), BitConverter.ToSingle(d, p + 8))),
                    });
                }
        }

        static float Q(short v) => (v < 0 ? v + 32768 : v - 32767) / 32767f;
        static Quaternion Quat(byte[] d, int p) => Quaternion.Normalize(new Quaternion(Q(BitConverter.ToInt16(d, p)), Q(BitConverter.ToInt16(d, p + 2)), Q(BitConverter.ToInt16(d, p + 4)), Q(BitConverter.ToInt16(d, p + 6))));

        // track header: u16 interp, s16 globalSeq, M2Array<M2Array<u32>> times, M2Array<M2Array<T>> values
        sealed class RawTrack { public int TimesN, TimesOfs, ValuesN, ValuesOfs, Size; public Func<byte[], int, object> Read; }
        readonly Dictionary<object, RawTrack> raw = new Dictionary<object, RawTrack>();

        Track<T> ReadTrack<T>(int o, int size, Func<byte[], int, T> read)
        {
            var t = new Track<T> { Interp = U16(o), GlobalSeq = S16(o + 2) };
            raw[t] = new RawTrack { TimesN = I(o + 4), TimesOfs = I(o + 8), ValuesN = I(o + 12), ValuesOfs = I(o + 16), Size = size, Read = (d, p) => read(d, p) };
            // embedded sub-arrays (global sequences and sequences whose data lives in the .m2)
            int n = Math.Max(0, Math.Min(I(o + 4), 100000));
            for (int s = 0; s < n; s++) { t.Times.Add(null); t.Values.Add(null); }
            for (int s = 0; s < n; s++)
            {
                bool embedded = t.GlobalSeq >= 0 || s >= Sequences.Count || Sequences[s].Embedded;
                if (embedded) LoadSub(t, s, b);
            }
            return t;
        }

        void LoadSub<T>(Track<T> t, int s, byte[] data)
        {
            var r = raw[t];
            int stride = t.Interp >= 2 ? 3 : 1;   // hermite / bezier keep in- and out-tangents after each value
            int th = r.TimesOfs + s * 8, vh = r.ValuesOfs + s * 8;
            if (th + 8 > b.Length || vh + 8 > b.Length) return;
            int tn = BitConverter.ToInt32(b, th), to = BitConverter.ToInt32(b, th + 4);
            int vn = BitConverter.ToInt32(b, vh), vo = BitConverter.ToInt32(b, vh + 4);
            if (tn <= 0 || tn > 100000 || to < 0 || (long)to + tn * 4L > data.Length) return;
            if (vn < tn * stride || vo < 0 || (long)vo + (long)vn * r.Size > data.Length) return;
            var times = new uint[tn]; var vals = new T[tn];
            for (int i = 0; i < tn; i++) { times[i] = BitConverter.ToUInt32(data, to + i * 4); vals[i] = (T)r.Read(data, vo + i * stride * r.Size); }
            t.Times[s] = times; t.Values[s] = vals;
        }

        /// <summary>Loads a sequence whose keyframes live in an external .anim file (Name{id:0000}-{variation:00}.anim).</summary>
        public void LoadAnimFile(int seq, byte[] anim)
        {
            if (anim == null || seq < 0 || seq >= Sequences.Count) return;
            foreach (var bone in Bones)
            {
                if (bone.Translation.GlobalSeq < 0 && seq < bone.Translation.Times.Count && bone.Translation.Times[seq] == null) LoadSub(bone.Translation, seq, anim);
                if (bone.Rotation.GlobalSeq < 0 && seq < bone.Rotation.Times.Count && bone.Rotation.Times[seq] == null) LoadSub(bone.Rotation, seq, anim);
                if (bone.Scale.GlobalSeq < 0 && seq < bone.Scale.Times.Count && bone.Scale.Times[seq] == null) LoadSub(bone.Scale, seq, anim);
            }
            loadedAnims.Add(seq);
        }

        readonly HashSet<int> loadedAnims = new HashSet<int>();
        public bool NeedsAnimFile(int seq) => seq >= 0 && seq < Sequences.Count && !Sequences[seq].Embedded && !loadedAnims.Contains(seq);

        /// <summary>The .anim file name for a sequence, next to the model (e.g. Creature\Wolf\Wolf0004-00.anim).</summary>
        public static string AnimPath(string modelPath, Sequence s) =>
            Path.Combine(Path.GetDirectoryName(modelPath) ?? "", Path.GetFileNameWithoutExtension(modelPath) + $"{s.Id:D4}-{s.Variation:D2}.anim");

        /// <summary>The .skin file names that go with a model: Name00.skin … Name03.skin.</summary>
        public static string SkinPath(string modelPath, int view = 0) =>
            Path.Combine(Path.GetDirectoryName(modelPath) ?? "", Path.GetFileNameWithoutExtension(modelPath) + $"{view:D2}.skin");

        /// <summary>Client paths use .mdx in the DBCs; the files are .m2.</summary>
        public static string M2Path(string path) => string.IsNullOrEmpty(path) ? path : Path.ChangeExtension(path.Replace('/', '\\'), ".m2");

        // ================================================================== animation
        static T Sample<T>(uint[] times, T[] vals, uint t, Func<T, T, float, T> lerp)
        {
            if (times.Length == 1 || t <= times[0]) return vals[0];
            if (t >= times[^1]) return vals[^1];
            int hi = Array.BinarySearch(times, t);
            if (hi >= 0) return vals[hi];
            hi = ~hi; int lo = hi - 1;
            float f = (t - times[lo]) / (float)Math.Max(1, times[hi] - times[lo]);
            return lerp(vals[lo], vals[hi], f);
        }

        T Value<T>(Track<T> tr, int seq, uint time, uint globalTime, T def, Func<T, T, float, T> lerp)
        {
            int sub; uint t;
            if (tr.GlobalSeq >= 0)
            {
                sub = 0;
                uint len = tr.GlobalSeq < GlobalSequences.Length ? GlobalSequences[tr.GlobalSeq] : 0;
                t = len == 0 ? 0 : globalTime % len;
            }
            else { sub = seq; t = time; }
            if (sub < 0 || sub >= tr.Times.Count) return def;
            var times = tr.Times[sub]; var vals = tr.Values[sub];
            if (times == null || vals == null || times.Length == 0) return def;
            return Sample(times, vals, t, lerp);
        }

        /// <summary>Every bone's matrix for sequence <paramref name="seq"/> at <paramref name="time"/> ms (seq -1 = rest pose).</summary>
        public Matrix4x4[] Pose(int seq, uint time, uint globalTime = 0)
        {
            var m = new Matrix4x4[Bones.Count];
            var done = new bool[Bones.Count];
            Matrix4x4 Calc(int i)
            {
                if (done[i]) return m[i];
                var bone = Bones[i];
                Matrix4x4 local = Matrix4x4.Identity;
                if (seq >= 0)
                {
                    var tr = Value(bone.Translation, seq, time, globalTime, Vector3.Zero, Vector3.Lerp);
                    var rot = Value(bone.Rotation, seq, time, globalTime, Quaternion.Identity, Quaternion.Slerp);
                    var sc = Value(bone.Scale, seq, time, globalTime, Vector3.One, Vector3.Lerp);
                    local = Matrix4x4.CreateTranslation(-bone.Pivot) * Matrix4x4.CreateScale(sc) * Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(tr) * Matrix4x4.CreateTranslation(bone.Pivot);
                }
                done[i] = true;   // guards against parent loops in broken files
                m[i] = bone.Parent >= 0 && bone.Parent < Bones.Count && bone.Parent != i ? local * Calc(bone.Parent) : local;
                return m[i];
            }
            for (int i = 0; i < Bones.Count; i++) Calc(i);
            return m;
        }

        /// <summary>Where an attachment point is in the posed model (null when the model has no such point).</summary>
        public Matrix4x4? AttachmentMatrix(int id, Matrix4x4[] pose)
        {
            var a = Attachments.FirstOrDefault(x => x.Id == id);
            if (a == null) return null;
            var bone = pose != null && a.Bone < pose.Length ? pose[a.Bone] : Matrix4x4.Identity;
            return Matrix4x4.CreateTranslation(a.Position) * bone;
        }

        /// <summary>A vertex moved by its bones.</summary>
        public static (Vector3 pos, Vector3 normal) Skin(Vertex v, Matrix4x4[] bones)
        {
            if (bones == null || bones.Length == 0) return (v.Pos, v.Normal);
            Vector3 p = Vector3.Zero, n = Vector3.Zero; float total = 0;
            void Add(byte w, byte bi)
            {
                if (w == 0 || bi >= bones.Length) return;
                float f = w / 255f; total += f;
                p += Vector3.Transform(v.Pos, bones[bi]) * f;
                n += Vector3.TransformNormal(v.Normal, bones[bi]) * f;
            }
            Add(v.W0, v.B0); Add(v.W1, v.B1); Add(v.W2, v.B2); Add(v.W3, v.B3);
            if (total < 0.001f) return (v.Pos, v.Normal);
            return (p / total, n.LengthSquared() > 0 ? Vector3.Normalize(n) : v.Normal);
        }
    }

    /// <summary>A 3.3.5a .skin file: which vertices and triangles make up each piece of the model, and how it's textured.</summary>
    public sealed class M2Skin
    {
        public sealed class Section { public ushort Id, Level, VertexStart, VertexCount, IndexStart, IndexCount; public int FirstIndex => IndexStart + Level * 65536; }
        public sealed class Batch { public byte Flags; public ushort Shader, Section, Material, TextureCount, TextureCombo, TexCoordCombo; }
        public ushort[] VertexMap = new ushort[0];
        public ushort[] Indices = new ushort[0];
        public List<Section> Sections = new List<Section>();
        public List<Batch> Batches = new List<Batch>();

        public static M2Skin Read(byte[] b)
        {
            if (b == null || b.Length < 44 || Encoding.ASCII.GetString(b, 0, 4) != "SKIN") throw new InvalidDataException("Not a .skin file.");
            var s = new M2Skin();
            int I(int o) => BitConverter.ToInt32(b, o);
            ushort U16(int o) => BitConverter.ToUInt16(b, o);
            bool Fits(int ofs, int n, int size) => n >= 0 && ofs >= 0 && (long)ofs + (long)n * size <= b.Length;
            int vn = I(4), vo = I(8), inn = I(12), io = I(16), sn = I(28), so = I(32), bn = I(36), bo = I(40);
            if (!Fits(vo, vn, 2) || !Fits(io, inn, 2) || !Fits(so, sn, 48) || !Fits(bo, bn, 24)) throw new InvalidDataException("The .skin file is cut short.");
            s.VertexMap = new ushort[vn]; for (int i = 0; i < vn; i++) s.VertexMap[i] = U16(vo + i * 2);
            s.Indices = new ushort[inn]; for (int i = 0; i < inn; i++) s.Indices[i] = U16(io + i * 2);
            for (int i = 0; i < sn; i++)
            {
                int o = so + i * 48;
                s.Sections.Add(new Section { Id = U16(o), Level = U16(o + 2), VertexStart = U16(o + 4), VertexCount = U16(o + 6), IndexStart = U16(o + 8), IndexCount = U16(o + 10) });
            }
            for (int i = 0; i < bn; i++)
            {
                int o = bo + i * 24;
                s.Batches.Add(new Batch { Flags = b[o], Shader = U16(o + 2), Section = U16(o + 4), Material = U16(o + 12), TextureCount = U16(o + 14), TextureCombo = U16(o + 16), TexCoordCombo = U16(o + 18) });
            }
            return s;
        }
    }

    /// <summary>One drawable piece of a model: triangles over model vertices with one texture and blend mode.</summary>
    public sealed class MeshPart
    {
        public int[] Vertices;        // model vertex index per local vertex
        public int[] Triangles;       // local vertex indices, 3 per triangle
        public int TextureIndex = -1; // into M2Model.Textures
        public ushort Blend, MaterialFlags;
        public ushort SectionId;
        public bool TwoSided => (MaterialFlags & 0x4) != 0;
        /// <summary>A reflection layer: its texture is mapped from the surface direction, not from UVs.</summary>
        public bool EnvMap;
        public bool Transparent => Blend >= 2;
    }

    public static class M2Mesh
    {
        /// <summary>Texture coordinates for a reflection layer, from the surface normal (sphere mapping).</summary>
        public static Vector2 SphereUv(Vector3 normal) { var n = normal.LengthSquared() > 0 ? Vector3.Normalize(normal) : Vector3.UnitZ; return new Vector2(0.5f + n.Y * 0.5f, 0.5f - n.Z * 0.5f); }

        /// <summary>The model's real extent (the header's box can be off), from its vertices.</summary>
        public static (Vector3 min, Vector3 max) VertexBounds(M2Model m, IEnumerable<MeshPart> parts)
        {
            Vector3 min = new Vector3(float.MaxValue), max = new Vector3(float.MinValue);
            foreach (var p in parts) foreach (var vi in p.Vertices) { min = Vector3.Min(min, m.Vertices[vi].Pos); max = Vector3.Max(max, m.Vertices[vi].Pos); }
            return min.X == float.MaxValue ? (m.BoundsMin, m.BoundsMax) : (min, max);
        }

        /// <summary>Character geosets: which section IDs to show by default (bare body, one hair style, ears; no cloak or tabard).</summary>
        public static bool DefaultGeoset(ushort id)
        {
            if (id == 0) return true;
            int group = id / 100, variant = id % 100;
            if (group == 7) return variant == 2;               // ears
            if (group is 12 or 13 or 15 or 17 or 18) return false;   // tabard, robe, cloak, belt, ...
            return variant == 1;
        }

        /// <summary>The model's pieces from one .skin, in drawing order (opaque first). <paramref name="geoset"/> chooses sections (null = all).</summary>
        public static List<MeshPart> Build(M2Model m, M2Skin s, Func<ushort, bool> geoset = null)
        {
            var parts = new List<MeshPart>();
            var used = new HashSet<int>();
            foreach (var bt in s.Batches)
            {
                if (bt.Section >= s.Sections.Count) continue;
                var sec = s.Sections[bt.Section];
                if (geoset != null && !geoset(sec.Id)) continue;
                var p = MakePart(m, s, sec);
                if (p == null) continue;
                p.SectionId = sec.Id;
                if (bt.Material < m.Materials.Count) { p.Blend = m.Materials[bt.Material].Blend; p.MaterialFlags = m.Materials[bt.Material].Flags; }
                if (bt.TextureCombo < m.TextureLookup.Length) p.TextureIndex = m.TextureLookup[bt.TextureCombo];
                if (bt.TexCoordCombo < m.TexCoordLookup.Length) p.EnvMap = m.TexCoordLookup[bt.TexCoordCombo] < 0;
                parts.Add(p);
                used.Add(bt.Section);
            }
            // sections without a batch (rare) still get drawn untextured
            for (int i = 0; i < s.Sections.Count; i++)
                if (!used.Contains(i) && (geoset == null || geoset(s.Sections[i].Id)) && s.Batches.Count == 0) { var p = MakePart(m, s, s.Sections[i]); if (p != null) parts.Add(p); }
            return parts.OrderBy(p => p.Transparent ? 1 : 0).ToList();
        }

        static MeshPart MakePart(M2Model m, M2Skin s, M2Skin.Section sec)
        {
            int first = sec.FirstIndex;
            if (sec.IndexCount == 0 || first + sec.IndexCount > s.Indices.Length) return null;
            var local = new Dictionary<int, int>();
            var verts = new List<int>(); var tris = new List<int>(sec.IndexCount);
            for (int i = 0; i < sec.IndexCount; i++)
            {
                int li = s.Indices[first + i];
                if (li >= s.VertexMap.Length) return null;
                int mv = s.VertexMap[li];
                if (mv >= m.Vertices.Length) return null;
                if (!local.TryGetValue(mv, out var k)) { k = verts.Count; local[mv] = k; verts.Add(mv); }
                tris.Add(k);
            }
            return new MeshPart { Vertices = verts.ToArray(), Triangles = tris.ToArray() };
        }
    }

    /// <summary>Where a model's textures come from when the model only says "creature skin 1" or "item skin".</summary>
    public static class M2Textures
    {
        public const uint File = 0, Body = 1, ObjectSkin = 2, CharHair = 6, Fur = 8, Monster1 = 11, Monster2 = 12, Monster3 = 13;

        /// <summary>The .blp path for a texture: its own file name, or the replaceable skin the display supplies.</summary>
        public static string Resolve(M2Model.Texture t, string modelPath, string[] creatureSkins = null, string itemSkin = null, string itemFolder = null)
        {
            if (t == null) return null;
            if (t.Type == File) return string.IsNullOrEmpty(t.File) ? null : t.File;
            var dir = Path.GetDirectoryName(modelPath ?? "") ?? "";
            if (t.Type >= Monster1 && t.Type <= Monster3)
            {
                var n = creatureSkins != null && t.Type - Monster1 < creatureSkins.Length ? creatureSkins[t.Type - Monster1] : null;
                return string.IsNullOrEmpty(n) ? null : Path.Combine(dir, n + ".blp");
            }
            if (t.Type == ObjectSkin && !string.IsNullOrEmpty(itemSkin)) return Path.Combine(itemFolder ?? dir, itemSkin + ".blp");
            return null;
        }
    }

    /// <summary>Item model folders by equip slot (ItemDisplayInfo.ModelName_1 is only a file name).</summary>
    public static class ItemModels
    {
        public static string[] Folders(long inventoryType) => inventoryType switch
        {
            1 => new[] { "Head" },
            3 => new[] { "Shoulder" },
            14 => new[] { "Shield" },
            27 => new[] { "Quiver" },
            24 => new[] { "Ammo" },
            18 => new[] { "Pouch" },
            _ => new[] { "Weapon", "Shield" },
        };

        /// <summary>Candidate client paths for an item's model (helmets have one model per race/gender: the human male one is used).</summary>
        public static IEnumerable<string> ModelPaths(string modelName, long inventoryType)
        {
            if (string.IsNullOrWhiteSpace(modelName)) yield break;
            var stem = Path.GetFileNameWithoutExtension(modelName);
            foreach (var f in Folders(inventoryType))
            {
                if (f == "Head") { yield return $@"Item\ObjectComponents\Head\{stem}_HuM.m2"; yield return $@"Item\ObjectComponents\Head\{stem}_HuF.m2"; }
                yield return $@"Item\ObjectComponents\{f}\{stem}.m2";
            }
        }
    }
}
