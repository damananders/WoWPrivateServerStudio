using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AndersCraftStudio.Core;

namespace AndersCraftStudio.Tests
{
    static class CompressionTests
    {
        static byte[] SmallExpected()
        {
            var d = new List<byte>();
            for (int i = 0; i < 40; i++) d.AddRange(Encoding.ASCII.GetBytes("AndersCraft Studio bzip2 test. "));
            for (int k = 0; k < 3; k++) for (int b = 0; b < 256; b++) d.Add((byte)b);
            d.AddRange(Enumerable.Repeat((byte)'a', 248));
            return d.ToArray();
        }

        [Test] static void Bzip2MatchesReferenceEncoder()
        {
            var exp = SmallExpected();
            var got = BZip2.Decompress(Convert.FromBase64String(Bz2Vectors.Small), exp.Length);
            int diff = Enumerable.Range(0, Math.Min(got.Length, exp.Length)).FirstOrDefault(i => got[i] != exp[i], -1);
            A.True(got.SequenceEqual(exp), $"small vector ({got.Length} vs {exp.Length}), first difference at {diff}: got {(diff >= 0 ? got[diff] : -1)} expected {(diff >= 0 ? exp[diff] : -1)}");
        }

        [Test] static void Bzip2MultipleBlocks()
        {
            var exp = Enumerable.Range(0, 250000).Select(i => (byte)(((i * 7) + (i / 300)) % 251)).ToArray();
            var got = BZip2.Decompress(Convert.FromBase64String(Bz2Vectors.MultiBlock), exp.Length);
            A.True(got.SequenceEqual(exp), "three 100k blocks");
        }

        [Test] static void Bzip2RejectsGarbage()
        {
            A.Throws(() => BZip2.Decompress(new byte[] { 1, 2, 3, 4, 5, 6 }, 10), "not bzip2");
        }

        [Test] static void ExplodeBinaryAndAscii()
        {
            var text = Encoding.ASCII.GetBytes("When aggro, cast Fireball on victim every 8-12s. {AndersCraft}\r\n\t~");
            foreach (var ascii in new[] { false, true })
                foreach (var dict in new[] { 4, 5, 6 })
                {
                    var c = Explode.StoreLiterals(text, dict, null, ascii);
                    A.True(Explode.Decompress(c, text.Length).SequenceEqual(text), $"literals ascii={ascii} dict={dict}");
                }
            var all = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
            A.True(Explode.Decompress(Explode.StoreLiterals(all, 6, null, true), 256).SequenceEqual(all), "every byte in ASCII mode");
        }

        [Test] static void ExplodeBackReferences()
        {
            var data = Encoding.ASCII.GetBytes("abcabcabcabcXYabcabc");
            // "abc" then a 9-byte copy from distance 3 (overlapping), then literals
            var c = Explode.StoreLiterals(data, 6, (3, 9, 3));
            A.True(Explode.Decompress(c, data.Length).SequenceEqual(data), "overlapping copy");
            var d2 = Encoding.ASCII.GetBytes("xyQQxy");
            A.True(Explode.Decompress(Explode.StoreLiterals(d2, 4, (4, 2, 4)), d2.Length).SequenceEqual(d2), "length-2 copy uses 2 low bits");
            var big = new byte[600]; for (int i = 0; i < 300; i++) big[i] = (byte)(i * 13); Array.Copy(big, 0, big, 300, 300);
            A.True(Explode.Decompress(Explode.StoreLiterals(big, 6, (300, 300, 300)), 600).SequenceEqual(big), "long copy with extra length bits");
        }

        [Test] static void HuffmanRoundTripsEveryTable()
        {
            var rnd = new Random(3);
            var samples = new List<byte[]>
            {
                Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("The quick brown fox jumps over the lazy dog. ", 60))),
                Enumerable.Range(0, 3000).Select(_ => (byte)rnd.Next(256)).ToArray(),
                Enumerable.Range(0, 2000).Select(i => (byte)(i % 7)).ToArray(),
                new byte[] { 42 },
            };
            for (int type = 0; type < 9; type++)
                foreach (var s in samples)
                {
                    var c = MpqHuffman.Compress(s, type);
                    var back = MpqHuffman.Decompress(c, s.Length);
                    A.True(back.SequenceEqual(s), $"type {type}, {s.Length} bytes");
                    A.True(MpqHuffman.Decompress(c, -1).SequenceEqual(s), $"type {type} until end marker");
                }
        }

        [Test] static void AdpcmDecodesSpecialCodes()
        {
            // header: unused, shift 0; mono start sample 1000; then: 0x80 (repeat), 0x00 (+step), 0x40 (-step), 0x81 (bigger step, no sample)
            var src = new byte[] { 0, 0, 0xE8, 0x03, 0x80, 0x00, 0x40, 0x81, 0x00 };
            var pcm = Adpcm.Decompress(src, -1, 1);
            var s = Enumerable.Range(0, pcm.Length / 2).Select(i => (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8))).ToArray();
            A.Eq(5, s.Length, "start + 4 samples (0x81 emits none)");
            A.Eq((short)1000, s[0]); A.Eq((short)1000, s[1], "0x80 repeats");
            A.True(s[2] > s[1], "0x00 steps up"); A.True(s[3] < s[2], "0x40 steps down");
            A.True(s[4] - s[3] > s[2] - s[1], "after 0x81 the step is larger");
            var st = Adpcm.Decompress(new byte[] { 0, 2, 0x10, 0x00, 0x20, 0x00, 0x00, 0x00 }, -1, 2);
            A.Eq(8, st.Length, "stereo: two starts + one sample each");
        }

        [Test] static void SectorMaskDispatch()
        {
            var text = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("sector ", 200)));
            // Huffman-only sector (mask 0x01)
            var h = MpqHuffman.Compress(text, 2);
            var sec = new byte[h.Length + 1]; sec[0] = 0x01; Buffer.BlockCopy(h, 0, sec, 1, h.Length);
            A.True(MpqCompression.DecompressSector(sec, sec.Length, text.Length).SequenceEqual(text), "0x01");
            // PKWARE sector (mask 0x08)
            var p = Explode.StoreLiterals(text);
            var sec2 = new byte[p.Length + 1]; sec2[0] = 0x08; Buffer.BlockCopy(p, 0, sec2, 1, p.Length);
            A.True(MpqCompression.DecompressSector(sec2, sec2.Length, text.Length).SequenceEqual(text), "0x08");
            // bzip2 sector (mask 0x10)
            var bz = Convert.FromBase64String(Bz2Vectors.Small); var exp = SmallExpected();
            var sec3 = new byte[bz.Length + 1]; sec3[0] = 0x10; Buffer.BlockCopy(bz, 0, sec3, 1, bz.Length);
            A.True(MpqCompression.DecompressSector(sec3, sec3.Length, exp.Length).SequenceEqual(exp), "0x10");
            A.Throws(() => MpqCompression.DecompressSector(new byte[] { 0x12, 1, 2 }, 3, 10), "LZMA reported");
        }
    }
}
