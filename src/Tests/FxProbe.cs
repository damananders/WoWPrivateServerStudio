using System;
using System.IO;
using System.Linq;
using AndersCraftStudio.Core;

namespace AndersCraftStudio.Tests
{
    /// <summary>--probe-fx [model]: prints a model's emitter block layout (developer aid for the particle reader).</summary>
    static class FxProbe
    {
        public static int Run(string path)
        {
            var c = new ClientFiles(DevPaths.ClientData);
            var b = c.Read(path, out _);
            if (b == null) { Console.WriteLine("not found"); return 1; }
            var m = M2Model.Read(b);
            int n = BitConverter.ToInt32(b, 0x128), ofs = BitConverter.ToInt32(b, 0x12C);
            int rn = BitConverter.ToInt32(b, 0x120), ro = BitConverter.ToInt32(b, 0x124);
            Console.WriteLine($"bones {m.Bones.Count} textures {m.Textures.Count} particles {n} @0x{ofs:X} ribbons {rn} @0x{ro:X} filesize 0x{b.Length:X}");
            // the next M2Array offsets after the emitter block give its size
            var offsets = Enumerable.Range(0, 0x130 / 4).Select(i => BitConverter.ToInt32(b, i * 4)).Where(o => o > ofs).OrderBy(o => o).Take(3).ToList();
            Console.WriteLine("next header offsets after particles: " + string.Join(", ", offsets.Select(o => "0x" + o.ToString("X") + $" (+{o - ofs})")));
            for (int i = 0; i < n; i++)
            {
                int o = ofs + i * 476;
                Console.WriteLine($"[{i}] @476: id {BitConverter.ToInt32(b, o)} flags 0x{BitConverter.ToUInt32(b, o + 4):X} bone {BitConverter.ToUInt16(b, o + 0x14)} tex {BitConverter.ToUInt16(b, o + 0x16)} blend {b[o + 0x28]} type {b[o + 0x29]} rows {BitConverter.ToUInt16(b, o + 0x30)} cols {BitConverter.ToUInt16(b, o + 0x32)}");
                for (int t = 0x34; t < 0xE8 + 20; t += 20)
                    Console.WriteLine($"   track@0x{t:X}: interp {BitConverter.ToUInt16(b, o + t)} gs {BitConverter.ToInt16(b, o + t + 2)} tn {BitConverter.ToInt32(b, o + t + 4)} vn {BitConverter.ToInt32(b, o + t + 12)}");
            }
            if (n > 1)
                for (int s = 400; s <= 520; s += 4)
                {
                    int o = ofs + s;
                    if (o + 0x34 > b.Length) break;
                    if (b[o + 0x29] >= 1 && b[o + 0x29] <= 3 && BitConverter.ToUInt16(b, o + 0x14) < m.Bones.Count && BitConverter.ToUInt16(b, o + 0x30) is var r && r >= 1 && r < 64)
                        Console.WriteLine($"plausible stride {s}");
                }
            return 0;
        }
    }
}
