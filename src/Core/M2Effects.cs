using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace AndersCraftStudio.Core
{
    /// <summary>
    /// Particle and ribbon emitters of a 3.3.5a M2 (spell effects, fires, glows, weapon trails) and a small simulation that
    /// approximates what the game draws: particles born at a bone with speed, spread, gravity and drag, fading through
    /// their colour/size/texture-cell keys; ribbons as strips trailing a bone. Written from the public format notes;
    /// the look is an approximation (no per-particle colour blending, no spline or "follow" modes).
    /// </summary>
    public sealed class M2Effects
    {
        public sealed class Emitter
        {
            public int Index, Bone, Texture; public uint Flags; public Vector3 Position;
            public byte Blend, Type, ParticleType; public ushort Rows = 1, Cols = 1;
            public M2Model.Track<float> Speed, SpeedVar, Vertical, Horizontal, Gravity, Life, Rate, AreaL, AreaW;
            public Vector3[] Colors = new Vector3[0]; public float[] Alphas = new float[0]; public Vector2[] Scales = new Vector2[0]; public ushort[] Cells = new ushort[0];
            public float Drag, Spin;
        }
        public sealed class Ribbon
        {
            public int Bone; public Vector3 Position; public int Texture = -1, Material = -1;
            public M2Model.Track<float> Above, Below; public float EdgesPerSecond, EdgeLifetime, Gravity;
            public Vector3 Color = Vector3.One; public float Alpha = 1;
        }

        public List<Emitter> Emitters = new List<Emitter>();
        public List<Ribbon> Ribbons = new List<Ribbon>();
        public int ParticleStride, RibbonStride;
        public string Problem;

        /// <summary>Reads the emitters. The record size differs between client builds, so it's worked out from the data.</summary>
        public static M2Effects Read(M2Model m)
        {
            var fx = new M2Effects();
            try { fx.ReadParticles(m); } catch (Exception ex) { fx.Problem = "Particles: " + ex.Message; fx.Emitters.Clear(); }
            try { fx.ReadRibbons(m); } catch (Exception ex) { fx.Problem = (fx.Problem == null ? "" : fx.Problem + " ") + "Ribbons: " + ex.Message; fx.Ribbons.Clear(); }
            return fx;
        }

        void ReadParticles(M2Model m)
        {
            var b = m.Bytes;
            int n = BitConverter.ToInt32(b, 0x128), ofs = BitConverter.ToInt32(b, 0x12C);
            if (n <= 0 || ofs <= 0) return;
            foreach (var stride in new[] { 476 })   // 3.3.5a: lifespan and emission rate each carry a 'vary' float after their track
            {
                if ((long)ofs + (long)n * stride > b.Length) continue;
                bool ok = true;
                for (int i = 0; i < n && ok; i++)
                {
                    int o = ofs + i * stride;
                    ok = BitConverter.ToUInt16(b, o + 0x14) < Math.Max(1, m.Bones.Count) && BitConverter.ToUInt16(b, o + 0x16) < Math.Max(1, m.Textures.Count)
                         && b[o + 0x29] >= 1 && b[o + 0x29] <= 3 && BitConverter.ToUInt16(b, o + 0x30) is var rows && rows >= 1 && rows <= 64
                         && TrackLooksValid(b, o + 0x34) && TrackLooksValid(b, o + 0xB0)
                         && (stride < 0x1C0 + 20 || TrackLooksValid(b, o + stride - 20));
                }
                if (!ok) continue;
                ParticleStride = stride;
                for (int i = 0; i < n; i++)
                {
                    int o = ofs + i * stride;
                    var e = new Emitter
                    {
                        Index = i, Flags = BitConverter.ToUInt32(b, o + 4), Position = new Vector3(BitConverter.ToSingle(b, o + 8), BitConverter.ToSingle(b, o + 12), BitConverter.ToSingle(b, o + 16)),
                        Bone = BitConverter.ToUInt16(b, o + 0x14), Texture = BitConverter.ToUInt16(b, o + 0x16), Blend = b[o + 0x28], Type = b[o + 0x29], ParticleType = b[o + 0x2C],
                        Rows = Math.Max((ushort)1, BitConverter.ToUInt16(b, o + 0x30)), Cols = Math.Max((ushort)1, BitConverter.ToUInt16(b, o + 0x32)),
                        Speed = m.FloatTrack(o + 0x34), SpeedVar = m.FloatTrack(o + 0x48), Vertical = m.FloatTrack(o + 0x5C), Horizontal = m.FloatTrack(o + 0x70),
                        Gravity = m.FloatTrack(o + 0x84), Life = m.FloatTrack(o + 0x98), Rate = m.FloatTrack(o + 0xB0), AreaL = m.FloatTrack(o + 0xC8), AreaW = m.FloatTrack(o + 0xDC),
                    };
                    e.Colors = FBlock(b, o + 0x104, 12, (d, p) => new Vector3(BitConverter.ToSingle(d, p), BitConverter.ToSingle(d, p + 4), BitConverter.ToSingle(d, p + 8)) / 255f);
                    e.Alphas = FBlock(b, o + 0x114, 2, (d, p) => BitConverter.ToInt16(d, p) / 32767f);
                    e.Scales = FBlock(b, o + 0x124, 8, (d, p) => new Vector2(BitConverter.ToSingle(d, p), BitConverter.ToSingle(d, p + 4)));
                    e.Cells = FBlock(b, o + 0x13C, 2, (d, p) => BitConverter.ToUInt16(d, p));
                    if (o + 0x178 < b.Length) e.Drag = BitConverter.ToSingle(b, o + 0x174);
                    Emitters.Add(e);
                }
                return;
            }
            throw new InvalidOperationException("the emitter records don't match a known layout");
        }

        void ReadRibbons(M2Model m)
        {
            var b = m.Bytes;
            int n = BitConverter.ToInt32(b, 0x120), ofs = BitConverter.ToInt32(b, 0x124);
            if (n <= 0 || ofs <= 0) return;
            foreach (var stride in new[] { 176, 172, 180 })
            {
                if ((long)ofs + (long)n * stride > b.Length) continue;
                bool ok = true;
                for (int i = 0; i < n && ok; i++)
                {
                    int o = ofs + i * stride;
                    int bone = BitConverter.ToInt32(b, o + 4), tn = BitConverter.ToInt32(b, o + 0x14);
                    ok = bone >= 0 && bone < Math.Max(1, m.Bones.Count) && tn >= 0 && tn < 16 && TrackLooksValid(b, o + 0x4C) && TrackLooksValid(b, o + 0x60);
                }
                if (!ok) continue;
                RibbonStride = stride;
                for (int i = 0; i < n; i++)
                {
                    int o = ofs + i * stride;
                    var r = new Ribbon
                    {
                        Bone = BitConverter.ToInt32(b, o + 4), Position = new Vector3(BitConverter.ToSingle(b, o + 8), BitConverter.ToSingle(b, o + 12), BitConverter.ToSingle(b, o + 16)),
                        Above = m.FloatTrack(o + 0x4C), Below = m.FloatTrack(o + 0x60),
                        EdgesPerSecond = BitConverter.ToSingle(b, o + 0x74), EdgeLifetime = BitConverter.ToSingle(b, o + 0x78), Gravity = BitConverter.ToSingle(b, o + 0x7C),
                    };
                    int tn = BitConverter.ToInt32(b, o + 0x14), to = BitConverter.ToInt32(b, o + 0x18);
                    if (tn > 0 && to > 0 && to + 2 <= b.Length) r.Texture = BitConverter.ToUInt16(b, to);
                    int mn = BitConverter.ToInt32(b, o + 0x1C), mo = BitConverter.ToInt32(b, o + 0x20);
                    if (mn > 0 && mo > 0 && mo + 2 <= b.Length) r.Material = BitConverter.ToUInt16(b, mo);
                    var col = m.Vector3Track(o + 0x24);
                    if (!col.IsEmpty) r.Color = Clamp01(First(col, Vector3.One * 255) / 255f);
                    var al = m.ShortTrack(o + 0x38);
                    if (!al.IsEmpty) r.Alpha = Math.Clamp(First(al, (short)32767) / 32767f, 0, 1);
                    Ribbons.Add(r);
                }
                return;
            }
            throw new InvalidOperationException("the ribbon records don't match a known layout");
        }

        static Vector3 Clamp01(Vector3 v) => Vector3.Clamp(v, Vector3.Zero, Vector3.One);
        static T First<T>(M2Model.Track<T> t, T def) { foreach (var v in t.Values) if (v != null && v.Length > 0) return v[0]; return def; }

        static bool TrackLooksValid(byte[] b, int o)
        {
            if (o < 0 || o + 20 > b.Length) return false;
            ushort interp = BitConverter.ToUInt16(b, o); short gs = BitConverter.ToInt16(b, o + 2);
            int tn = BitConverter.ToInt32(b, o + 4), to = BitConverter.ToInt32(b, o + 8), vn = BitConverter.ToInt32(b, o + 12), vo = BitConverter.ToInt32(b, o + 16);
            return interp <= 3 && gs >= -1 && gs < 1000 && tn >= 0 && tn < 2000 && vn >= 0 && vn < 2000 && (tn == 0 || (to > 0 && to < b.Length)) && (vn == 0 || (vo > 0 && vo < b.Length));
        }

        /// <summary>A "fake animation block": values over a particle's life (times are fractions of life, 0…32767).</summary>
        static T[] FBlock<T>(byte[] b, int o, int size, Func<byte[], int, T> read)
        {
            if (o + 16 > b.Length) return new T[0];
            int vn = BitConverter.ToInt32(b, o + 8), vo = BitConverter.ToInt32(b, o + 12);
            if (vn <= 0 || vn > 64 || vo <= 0 || (long)vo + (long)vn * size > b.Length) return new T[0];
            var r = new T[vn];
            for (int i = 0; i < vn; i++) r[i] = read(b, vo + i * size);
            return r;
        }

        /// <summary>Value of a life-time key list at fraction <paramref name="f"/> (keys spread evenly).</summary>
        public static T OverLife<T>(T[] keys, float f, T def, Func<T, T, float, T> lerp)
        {
            if (keys == null || keys.Length == 0) return def;
            if (keys.Length == 1) return keys[0];
            float x = Math.Clamp(f, 0, 1) * (keys.Length - 1);
            int i = Math.Min(keys.Length - 2, (int)x);
            return lerp(keys[i], keys[i + 1], x - i);
        }
    }

    /// <summary>The particles alive right now, stepped forward in time.</summary>
    public sealed class ParticleSim
    {
        public struct P { public Vector3 Pos, Vel; public float Age, Life; public int Emitter; public float Seed; }
        public readonly List<P> Live = new List<P>();
        readonly Dictionary<int, float> carry = new Dictionary<int, float>();
        readonly Random rnd = new Random(12345);
        public int MaxPerEmitter = 250;

        /// <summary>Advances <paramref name="dt"/> seconds with the model in <paramref name="pose"/> at animation <paramref name="seq"/>/<paramref name="ms"/>.</summary>
        public void Step(M2Model m, M2Effects fx, Matrix4x4[] pose, int seq, uint ms, float dt)
        {
            if (dt <= 0) return;
            for (int i = Live.Count - 1; i >= 0; i--)
            {
                var p = Live[i];
                p.Age += dt;
                if (p.Age >= p.Life) { Live.RemoveAt(i); continue; }
                var e = fx.Emitters[p.Emitter];
                float g = m.Sample(e.Gravity, seq, ms, 0f);
                p.Vel.Z -= g * dt;
                if (e.Drag > 0) p.Vel *= Math.Max(0, 1 - e.Drag * dt);
                p.Pos += p.Vel * dt;
                Live[i] = p;
            }
            foreach (var e in fx.Emitters)
            {
                float rate = m.Sample(e.Rate, seq, ms, 0f), life = m.Sample(e.Life, seq, ms, 1f);
                if (rate <= 0 || life <= 0) continue;
                int alive = Live.Count(p => p.Emitter == e.Index);
                carry.TryGetValue(e.Index, out var c);
                c += rate * dt;
                var at = Matrix4x4.CreateTranslation(e.Position) * (pose != null && e.Bone < pose.Length ? pose[e.Bone] : Matrix4x4.Identity);
                while (c >= 1 && alive < MaxPerEmitter)
                {
                    c -= 1; alive++;
                    float speed = m.Sample(e.Speed, seq, ms, 0f) * (1 + (float)(rnd.NextDouble() * 2 - 1) * m.Sample(e.SpeedVar, seq, ms, 0f));
                    float vert = m.Sample(e.Vertical, seq, ms, 0f), hor = m.Sample(e.Horizontal, seq, ms, 0f);
                    Vector3 local, dir;
                    if (e.Type == 1)        // plane: spawn on a rectangle, fly up within a cone
                    {
                        local = new Vector3((float)(rnd.NextDouble() - 0.5) * m.Sample(e.AreaL, seq, ms, 0f), (float)(rnd.NextDouble() - 0.5) * m.Sample(e.AreaW, seq, ms, 0f), 0);
                        float a = (float)(rnd.NextDouble() * 2 - 1) * vert, h = (float)(rnd.NextDouble() * 2 - 1) * MathF.PI;
                        dir = new Vector3(MathF.Sin(a) * MathF.Cos(h), MathF.Sin(a) * MathF.Sin(h), MathF.Cos(a));
                    }
                    else                    // sphere / spline: out in all directions within the ranges
                    {
                        float a = (float)(rnd.NextDouble() * 2 - 1) * Math.Max(vert, 0.0001f), h = (float)(rnd.NextDouble() * 2 - 1) * Math.Max(hor, MathF.PI);
                        dir = new Vector3(MathF.Sin(a) * MathF.Cos(h), MathF.Sin(a) * MathF.Sin(h), MathF.Cos(a));
                        float r = m.Sample(e.AreaL, seq, ms, 0f);
                        local = r > 0 ? dir * (float)rnd.NextDouble() * r : Vector3.Zero;
                    }
                    Live.Add(new P { Pos = Vector3.Transform(local, at), Vel = Vector3.TransformNormal(dir, at) * speed, Life = life, Emitter = e.Index, Seed = (float)rnd.NextDouble() });
                }
                carry[e.Index] = Math.Min(c, 5);
            }
        }

        public void Clear() { Live.Clear(); carry.Clear(); }
    }
}
