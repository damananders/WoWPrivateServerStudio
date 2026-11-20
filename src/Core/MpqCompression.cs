using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace AndersCraftStudio.Core
{
    /// <summary>
    /// Decompressors for every compression type found in 3.3.5a-era MPQs: zlib (0x02), PKWARE DCL implode (0x08),
    /// bzip2 (0x10), Blizzard adaptive Huffman (0x01) and IMA-style ADPCM for mono (0x40) / stereo (0x80) audio.
    /// Written for Studio. The PKWARE code tables, the Huffman weight tables and the ADPCM step tables are format
    /// constants (as documented by StormLib, MIT licence, Ladislav Zezula).
    /// </summary>
    public static class MpqCompression
    {
        public const byte Huffman = 0x01, Zlib = 0x02, Pkware = 0x08, Bzip2 = 0x10, Sparse = 0x20, AdpcmMono = 0x40, AdpcmStereo = 0x80, Lzma = 0x12;

        /// <summary>Decompresses one sector/unit whose first byte is the compression mask.</summary>
        public static byte[] DecompressSector(byte[] src, int len, int want)
        {
            byte mask = src[0];
            var data = new byte[len - 1];
            Buffer.BlockCopy(src, 1, data, 0, len - 1);
            if (mask == Lzma) throw new NotSupportedException("LZMA-compressed files (newer MPQ format) aren't supported.");
            // undo in reverse order of compression: bzip2/implode/zlib → Huffman → ADPCM
            if ((mask & Bzip2) != 0) data = BZip2.Decompress(data, want);
            if ((mask & Pkware) != 0) data = Explode.Decompress(data, want);
            if ((mask & Zlib) != 0) data = ZlibInflate(data, want);
            if ((mask & Huffman) != 0) data = MpqHuffman.Decompress(data, (mask & (AdpcmMono | AdpcmStereo)) != 0 ? -1 : want);
            if ((mask & AdpcmStereo) != 0) data = Adpcm.Decompress(data, want, 2);
            else if ((mask & AdpcmMono) != 0) data = Adpcm.Decompress(data, want, 1);
            if ((mask & ~(Huffman | Zlib | Pkware | Bzip2 | AdpcmMono | AdpcmStereo)) != 0) throw new NotSupportedException($"Unknown compression type 0x{mask:X2}.");
            if (data.Length != want)
            {
                if (data.Length < want) { var ex = new InvalidDataException($"Decompressed to {data.Length} bytes, expected {want}."); ex.Data["sectorGot"] = data.Length; throw ex; }
                Array.Resize(ref data, want);
            }
            return data;
        }

        public static byte[] ZlibInflate(byte[] src, int want)
        {
            var dst = new byte[want];
            using var zs = new ZLibStream(new MemoryStream(src), CompressionMode.Decompress);
            int got = 0;
            while (got < want) { int r = zs.Read(dst, got, want - got); if (r <= 0) break; got += r; }
            if (got < want) Array.Resize(ref dst, got);
            return dst;
        }
    }

    // ======================================================================= PKWARE Data Compression Library "explode"
    public static class Explode
    {
        static readonly byte[] LenBits = { 3, 2, 3, 3, 4, 4, 4, 5, 5, 5, 5, 6, 6, 6, 7, 7 };
        static readonly byte[] LenCode = { 0x05, 0x03, 0x01, 0x06, 0x0A, 0x02, 0x0C, 0x14, 0x04, 0x18, 0x08, 0x30, 0x10, 0x20, 0x40, 0x00 };
        static readonly byte[] ExLenBits = { 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8 };
        static readonly ushort[] LenBase = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 0x0A, 0x0E, 0x16, 0x26, 0x46, 0x86, 0x106 };
        static readonly byte[] DistBits =
        {
            2, 4, 4, 5, 5, 5, 5, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7,
            7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8,
        };
        static readonly byte[] DistCode =
        {
            0x03, 0x0D, 0x05, 0x19, 0x09, 0x11, 0x01, 0x3E, 0x1E, 0x2E, 0x0E, 0x36, 0x16, 0x26, 0x06, 0x3A,
            0x1A, 0x2A, 0x0A, 0x32, 0x12, 0x22, 0x42, 0x02, 0x7C, 0x3C, 0x5C, 0x1C, 0x6C, 0x2C, 0x4C, 0x0C,
            0x74, 0x34, 0x54, 0x14, 0x64, 0x24, 0x44, 0x04, 0x78, 0x38, 0x58, 0x18, 0x68, 0x28, 0x48, 0x08,
            0xF0, 0x70, 0xB0, 0x30, 0xD0, 0x50, 0x90, 0x10, 0xE0, 0x60, 0xA0, 0x20, 0xC0, 0x40, 0x80, 0x00,
        };
        static readonly byte[] ChBitsAsc =
        {
            11,12,12,12,12,12,12,12,12, 8, 7,12,12, 7,12,12,12,12,12,12,12,12,12,12,12,12,13,12,12,12,12,12,
             4,10, 8,12,10,12,10, 8, 7, 7, 8, 9, 7, 6, 7, 8, 7, 6, 7, 7, 7, 7, 8, 7, 7, 8, 8,12,11, 7, 9,11,
            12, 6, 7, 6, 6, 5, 7, 8, 8, 6,11, 9, 6, 7, 6, 6, 7,11, 6, 6, 6, 7, 9, 8, 9, 9,11, 8,11, 9,12, 8,
            12, 5, 6, 6, 6, 5, 6, 6, 6, 5,11, 7, 5, 6, 5, 5, 6,10, 5, 5, 5, 5, 8, 7, 8, 8,10,11,11,12,12,12,
            13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,
            13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,12,12,12,12,12,12,12,12,12,12,12,12,12,12,12,12,
            12,12,12,12,12,12,12,12,12,12,12,12,12,12,12,12,12,12,12,12,12,12,12,12,12,12,12,12,12,12,12,12,
            13,12,13,13,13,12,13,13,13,12,13,13,13,13,12,13,13,13,12,12,12,13,13,13,13,13,13,13,13,13,13,13,
        };
        static readonly ushort[] ChCodeAsc =
        {
            0x0490,0x0FE0,0x07E0,0x0BE0,0x03E0,0x0DE0,0x05E0,0x09E0,0x01E0,0x00B8,0x0062,0x0EE0,0x06E0,0x0022,0x0AE0,0x02E0,
            0x0CE0,0x04E0,0x08E0,0x00E0,0x0F60,0x0760,0x0B60,0x0360,0x0D60,0x0560,0x1240,0x0960,0x0160,0x0E60,0x0660,0x0A60,
            0x000F,0x0250,0x0038,0x0260,0x0050,0x0C60,0x0390,0x00D8,0x0042,0x0002,0x0058,0x01B0,0x007C,0x0029,0x003C,0x0098,
            0x005C,0x0009,0x001C,0x006C,0x002C,0x004C,0x0018,0x000C,0x0074,0x00E8,0x0068,0x0460,0x0090,0x0034,0x00B0,0x0710,
            0x0860,0x0031,0x0054,0x0011,0x0021,0x0017,0x0014,0x00A8,0x0028,0x0001,0x0310,0x0130,0x003E,0x0064,0x001E,0x002E,
            0x0024,0x0510,0x000E,0x0036,0x0016,0x0044,0x0030,0x00C8,0x01D0,0x00D0,0x0110,0x0048,0x0610,0x0150,0x0060,0x0088,
            0x0FA0,0x0007,0x0026,0x0006,0x003A,0x001B,0x001A,0x002A,0x000A,0x000B,0x0210,0x0004,0x0013,0x0032,0x0003,0x001D,
            0x0012,0x0190,0x000D,0x0015,0x0005,0x0019,0x0008,0x0078,0x00F0,0x0070,0x0290,0x0410,0x0010,0x07A0,0x0BA0,0x03A0,
            0x0240,0x1C40,0x0C40,0x1440,0x0440,0x1840,0x0840,0x1040,0x0040,0x1F80,0x0F80,0x1780,0x0780,0x1B80,0x0B80,0x1380,
            0x0380,0x1D80,0x0D80,0x1580,0x0580,0x1980,0x0980,0x1180,0x0180,0x1E80,0x0E80,0x1680,0x0680,0x1A80,0x0A80,0x1280,
            0x0280,0x1C80,0x0C80,0x1480,0x0480,0x1880,0x0880,0x1080,0x0080,0x1F00,0x0F00,0x1700,0x0700,0x1B00,0x0B00,0x1300,
            0x0DA0,0x05A0,0x09A0,0x01A0,0x0EA0,0x06A0,0x0AA0,0x02A0,0x0CA0,0x04A0,0x08A0,0x00A0,0x0F20,0x0720,0x0B20,0x0320,
            0x0D20,0x0520,0x0920,0x0120,0x0E20,0x0620,0x0A20,0x0220,0x0C20,0x0420,0x0820,0x0020,0x0FC0,0x07C0,0x0BC0,0x03C0,
            0x0DC0,0x05C0,0x09C0,0x01C0,0x0EC0,0x06C0,0x0AC0,0x02C0,0x0CC0,0x04C0,0x08C0,0x00C0,0x0F40,0x0740,0x0B40,0x0340,
            0x0300,0x0D40,0x1D00,0x0D00,0x1500,0x0540,0x0500,0x1900,0x0900,0x0940,0x1100,0x0100,0x1E00,0x0E00,0x0140,0x1600,
            0x0600,0x1A00,0x0E40,0x0640,0x0A40,0x0A00,0x1200,0x0200,0x1C00,0x0C00,0x1400,0x0400,0x1800,0x0800,0x1000,0x0000,
        };

        static readonly byte[] LenTable = Lookup(LenCode, LenBits, 8);
        static readonly byte[] DistTable = Lookup(DistCode, DistBits, 8);
        static byte[] ascTable;

        /// <summary>Maps every n-bit window to the symbol whose (LSB-first) code is its prefix.</summary>
        static byte[] Lookup(IList<byte> codes, IList<byte> bits, int n)
        {
            var t = new byte[1 << n];
            for (int s = 0; s < codes.Count; s++)
                for (int i = codes[s]; i < t.Length; i += 1 << bits[s]) t[i] = (byte)s;
            return t;
        }

        static byte[] AscTable()
        {
            if (ascTable != null) return ascTable;
            var t = new byte[1 << 13];
            for (int s = 0; s < 256; s++)
                for (int i = ChCodeAsc[s]; i < t.Length; i += 1 << ChBitsAsc[s]) t[i] = (byte)s;
            return ascTable = t;
        }

        sealed class Bits
        {
            readonly byte[] d; int pos; ulong buf; int cnt;
            public Bits(byte[] d, int start) { this.d = d; pos = start; }
            void Fill(int n) { while (cnt < n) { buf |= (ulong)(pos < d.Length ? d[pos] : 0) << cnt; pos++; cnt += 8; } }
            public bool Exhausted(int n) => pos - (cnt / 8) > d.Length + 1 && n > 0;
            public uint Peek(int n) { Fill(n); return (uint)(buf & ((1UL << n) - 1)); }
            public void Skip(int n) { Fill(n); buf >>= n; cnt -= n; if (pos > d.Length + 4) throw new InvalidDataException("PKWARE stream ended early."); }
            public uint Get(int n) { var v = Peek(n); Skip(n); return v; }
        }

        public static byte[] Decompress(byte[] src, int want)
        {
            if (src.Length < 3) throw new InvalidDataException("PKWARE stream too short.");
            int type = src[0], dictBits = src[1];
            if (type > 1) throw new InvalidDataException("Unknown PKWARE literal mode " + type);
            if (dictBits < 4 || dictBits > 6) throw new InvalidDataException("Bad PKWARE dictionary size " + dictBits);
            var asc = type == 1 ? AscTable() : null;
            var b = new Bits(src, 2);
            var outp = new byte[want];
            int o = 0;
            while (o < want)
            {
                if (b.Get(1) == 1)
                {
                    int sym = LenTable[b.Peek(8)];
                    b.Skip(LenBits[sym]);
                    int value = LenBase[sym] + (ExLenBits[sym] > 0 ? (int)b.Get(ExLenBits[sym]) : 0);
                    if (value == 0x205) break;               // end of stream
                    int len = value + 2;
                    int dsym = DistTable[b.Peek(8)];
                    b.Skip(DistBits[dsym]);
                    int dist = len == 2 ? (dsym << 2) | (int)b.Get(2) : (dsym << dictBits) | (int)b.Get(dictBits);
                    dist++;
                    if (dist > o) throw new InvalidDataException("PKWARE back-reference before the start.");
                    for (int k = 0; k < len && o < want; k++, o++) outp[o] = outp[o - dist];
                }
                else if (asc == null) outp[o++] = (byte)b.Get(8);
                else
                {
                    int s = asc[b.Peek(13)];
                    b.Skip(ChBitsAsc[s]);
                    outp[o++] = (byte)s;
                }
            }
            if (o < want) Array.Resize(ref outp, o);
            return outp;
        }

        /// <summary>Literal-only binary-mode stream (used by tests to exercise the decoder).</summary>
        public static byte[] StoreLiterals(byte[] data, int dictBits = 6, (int at, int len, int dist)? backRef = null, bool ascii = false)
        {
            var bw = new List<byte> { (byte)(ascii ? 1 : 0), (byte)dictBits };
            ulong buf = 0; int cnt = 0;
            void Put(uint v, int n) { buf |= (ulong)v << cnt; cnt += n; while (cnt >= 8) { bw.Add((byte)buf); buf >>= 8; cnt -= 8; } }
            void PutLen(int value)
            {
                int sym = 15; for (int s = 0; s < 16; s++) if (value >= LenBase[s] && value < LenBase[s] + (1 << ExLenBits[s])) { sym = s; break; }
                Put(1, 1); Put(LenCode[sym], LenBits[sym]); if (ExLenBits[sym] > 0) Put((uint)(value - LenBase[sym]), ExLenBits[sym]);
            }
            for (int i = 0; i < data.Length; i++)
            {
                if (backRef != null && i == backRef.Value.at)
                {
                    var (_, len, dist) = backRef.Value;
                    PutLen(len - 2);
                    int d = dist - 1, low = len == 2 ? 2 : dictBits;
                    Put(DistCode[d >> low], DistBits[d >> low]); Put((uint)(d & ((1 << low) - 1)), low);
                    i += len - 1; continue;
                }
                Put(0, 1); if (ascii) Put(ChCodeAsc[data[i]], ChBitsAsc[data[i]]); else Put(data[i], 8);
            }
            PutLen(0x205);
            if (cnt > 0) bw.Add((byte)buf);
            return bw.ToArray();
        }
    }

    // ======================================================================= bzip2
    public static class BZip2
    {
        sealed class BitReader
        {
            readonly byte[] d; int pos; uint buf; int cnt;
            public BitReader(byte[] d) { this.d = d; }
            public int Bit() => (int)Get(1);
            public uint Get(int n)
            {
                while (cnt < n)
                {
                    if (pos >= d.Length) throw new InvalidDataException("bzip2 stream ended early.");
                    buf = (buf << 8) | d[pos++]; cnt += 8;
                }
                uint v = (uint)((buf >> (cnt - n)) & ((1UL << n) - 1));
                cnt -= n;
                return v;
            }
        }

        public static byte[] Decompress(byte[] src, int want)
        {
            var br = new BitReader(src);
            if (br.Get(8) != 'B' || br.Get(8) != 'Z' || br.Get(8) != 'h') throw new InvalidDataException("Not a bzip2 stream.");
            int level = (int)br.Get(8) - '0';
            if (level < 1 || level > 9) throw new InvalidDataException("Bad bzip2 block size.");
            var outp = new MemoryStream(want > 0 ? want : 1024);
            var tt = new int[level * 100000];
            while (true)
            {
                ulong magic = ((ulong)br.Get(24) << 24) | br.Get(24);
                if (magic == 0x177245385090UL) break;               // end of stream
                if (magic != 0x314159265359UL) throw new InvalidDataException("Bad bzip2 block header.");
                br.Get(32);                                           // block CRC (not checked; the MPQ read-back is compared by the caller)
                if (br.Bit() != 0) throw new NotSupportedException("Randomised bzip2 blocks aren't supported.");
                int origPtr = (int)br.Get(24);

                // symbols in use
                var used = new List<byte>();
                int groups = (int)br.Get(16);
                for (int i = 0; i < 16; i++)
                    if ((groups & (0x8000 >> i)) != 0)
                    {
                        int bits = (int)br.Get(16);
                        for (int j = 0; j < 16; j++) if ((bits & (0x8000 >> j)) != 0) used.Add((byte)(i * 16 + j));
                    }
                if (used.Count == 0) throw new InvalidDataException("bzip2 block uses no symbols.");
                int alpha = used.Count + 2;                           // RUNA, RUNB, symbols..., EOB

                int nTrees = (int)br.Get(3);
                if (nTrees < 2 || nTrees > 6) throw new InvalidDataException("Bad bzip2 tree count.");
                int nSel = (int)br.Get(15);
                if (nSel < 1) throw new InvalidDataException("Bad bzip2 selector count.");
                var mtfSel = new byte[nTrees];
                for (int i = 0; i < nTrees; i++) mtfSel[i] = (byte)i;
                var selectors = new byte[nSel];
                for (int i = 0; i < nSel; i++)
                {
                    int j = 0;
                    while (br.Bit() == 1) { j++; if (j >= nTrees) throw new InvalidDataException("Bad bzip2 selector."); }
                    byte v = mtfSel[j];
                    for (; j > 0; j--) mtfSel[j] = mtfSel[j - 1];
                    mtfSel[0] = v; selectors[i] = v;
                }

                // code lengths per tree → canonical decode tables
                var limit = new int[nTrees][]; var bases = new int[nTrees][]; var perms = new int[nTrees][]; var minLen = new int[nTrees];
                for (int t = 0; t < nTrees; t++)
                {
                    var lens = new int[alpha];
                    int cur = (int)br.Get(5);
                    for (int s = 0; s < alpha; s++)
                    {
                        while (true)
                        {
                            if (cur < 1 || cur > 20) throw new InvalidDataException("Bad bzip2 code length.");
                            if (br.Bit() == 0) break;
                            cur += br.Bit() == 0 ? 1 : -1;
                        }
                        lens[s] = cur;
                    }
                    int mn = 32, mx = 0;
                    foreach (var l in lens) { mn = Math.Min(mn, l); mx = Math.Max(mx, l); }
                    minLen[t] = mn;
                    var perm = new List<int>();
                    for (int l = mn; l <= mx; l++) for (int s = 0; s < alpha; s++) if (lens[s] == l) perm.Add(s);
                    var lim = new int[mx + 2]; var bas = new int[mx + 2];
                    int code = 0, idx = 0;
                    for (int l = mn; l <= mx; l++)
                    {
                        int count = 0; foreach (var x in lens) if (x == l) count++;
                        bas[l] = idx - code;                 // perm index = code + base
                        code += count; idx += count;
                        lim[l] = code - 1;                   // largest code of this length
                        code <<= 1;
                    }
                    for (int l = 0; l < mn; l++) lim[l] = -1;
                    lim[mx + 1] = int.MaxValue;
                    limit[t] = lim; bases[t] = bas; perms[t] = perm.ToArray();
                }

                // Huffman → MTF → RUNA/RUNB → bytes (with counts for the BWT inverse)
                var mtf = new byte[256];
                for (int i = 0; i < used.Count; i++) mtf[i] = (byte)i;
                var counts = new int[256];
                int n = 0, group = -1, groupLeft = 0, run = 0, runBit = 1;
                int maxN = tt.Length;
                while (true)
                {
                    if (groupLeft == 0) { group++; if (group >= nSel) throw new InvalidDataException("bzip2 ran out of selectors."); groupLeft = 50; }
                    groupLeft--;
                    int t = selectors[group];
                    var lim = limit[t];
                    int len = minLen[t];
                    int c = (int)br.Get(len);
                    while (len + 1 < lim.Length && c > lim[len]) { c = (c << 1) | br.Bit(); len++; }
                    if (len >= lim.Length - 1 && c > lim[len]) throw new InvalidDataException("Bad bzip2 Huffman code.");
                    int sym = perms[t][c + bases[t][len]];
                    if (sym <= 1)                            // RUNA / RUNB
                    {
                        run += (sym + 1) * runBit; runBit <<= 1;
                        if (run > maxN) throw new InvalidDataException("bzip2 run too long.");
                        continue;
                    }
                    if (run > 0)
                    {
                        byte b = used[mtf[0]];
                        if (n + run > maxN) throw new InvalidDataException("bzip2 block too large.");
                        counts[b] += run;
                        for (; run > 0; run--) tt[n++] = b;
                        runBit = 1;
                    }
                    if (sym == alpha - 1) break;             // end of block
                    int m = sym - 1;
                    byte v = mtf[m];
                    Buffer.BlockCopy(mtf, 0, mtf, 1, m);
                    mtf[0] = v;
                    byte ch = used[v];
                    if (n >= maxN) throw new InvalidDataException("bzip2 block too large.");
                    counts[ch]++;
                    tt[n++] = ch;
                }
                if (origPtr >= n) throw new InvalidDataException("Bad bzip2 origin pointer.");

                // inverse BWT: tt[i] low byte = symbol, high bits = next index
                var start = new int[256];
                for (int i = 0, sum = 0; i < 256; i++) { start[i] = sum; sum += counts[i]; }
                for (int i = 0; i < n; i++) { int ch = tt[i] & 0xFF; tt[start[ch]++] |= i << 8; }
                int p = tt[origPtr] >> 8;
                // undo the initial run-length encoding: 4 equal bytes are followed by a repeat count
                int last = -1, same = 0;
                for (int i = 0; i < n; i++)
                {
                    int e = tt[p]; byte b = (byte)e; p = e >> 8;
                    if (same == 4) { for (int k = 0; k < b; k++) outp.WriteByte((byte)last); same = 0; last = -1; continue; }
                    if (b == last) same++; else { same = 1; last = b; }
                    outp.WriteByte(b);
                }
                Array.Clear(tt, 0, n);
            }
            return outp.ToArray();
        }
    }

    // ======================================================================= Blizzard adaptive Huffman
    /// <summary>
    /// The adaptive Huffman coder Storm uses for audio (and some other data). The tree starts from one of nine built-in
    /// byte-weight tables (chosen by the first byte of the stream), grows when a new byte appears (escape 0x101), and
    /// rebalances as weights change. 0x100 ends the stream.
    /// </summary>
    public sealed class MpqHuffman
    {
        sealed class Node { public Node Prev, Next, Parent, ChildLo; public int Value, Weight; }

        readonly Node head = new Node();
        readonly Node[] byValue = new Node[0x102];
        int used;
        const int MaxItems = 0x203;
        bool sparse;

        MpqHuffman() { head.Next = head.Prev = head; }

        static void Unlink(Node n) { if (n.Next != null) { n.Prev.Next = n.Next; n.Next.Prev = n.Prev; n.Next = n.Prev = null; } }
        static void LinkAfter(Node at, Node n) { n.Next = at.Next; n.Prev = at; at.Next.Prev = n; at.Next = n; }

        Node FindHigherOrEqual(Node from, int w)
        {
            if (from != null) for (var n = from; n != head; n = n.Prev) if (n.Weight >= w) return n;
            return head;
        }

        Node Create(int value, int weight, bool atFront)
        {
            if (used >= MaxItems) throw new InvalidDataException("Huffman tree overflow.");
            used++;
            var n = new Node { Value = value, Weight = weight };
            if (atFront) LinkAfter(head, n); else LinkAfter(head.Prev, n);
            return n;
        }

        int FixPos(Node n, int maxW)
        {
            if (n.Weight < maxW) { var hi = FindHigherOrEqual(head.Prev, n.Weight); Unlink(n); LinkAfter(hi, n); }
            else maxW = n.Weight;
            return maxW;
        }

        void Build(int type)
        {
            type &= 0x0F;
            if (type >= HuffmanTables.Distributions.Length) throw new InvalidDataException("Unknown Huffman data type " + type);
            var table = HuffmanTables.Distributions[type];
            int maxW = 0;
            for (int i = 0; i < 0x100; i++)
                if (table[i] != 0) { var n = byValue[i] = Create(i, table[i], true); maxW = FixPos(n, maxW); }
            byValue[0x100] = Create(0x100, 1, false);
            byValue[0x101] = Create(0x101, 1, false);
            var lo = head.Prev;
            while (lo != head)
            {
                var hi = lo.Prev;
                if (hi == head) break;
                var parent = Create(0, hi.Weight + lo.Weight, true);
                lo.Parent = parent; hi.Parent = parent; parent.ChildLo = lo;
                maxW = FixPos(parent, maxW);
                lo = hi.Prev;
            }
        }

        void IncWeights(Node n)
        {
            for (; n != null; n = n.Parent)
            {
                n.Weight++;
                var higher = FindHigherOrEqual(n.Prev, n.Weight);
                var childHi = higher.Next;
                if (childHi == n) continue;
                Unlink(childHi); LinkAfter(n, childHi);
                Unlink(n); LinkAfter(higher, n);
                var childLo = childHi.Parent.ChildLo;
                var parent = n.Parent;
                if (parent.ChildLo == n) parent.ChildLo = childHi;
                if (childLo == childHi) childHi.Parent.ChildLo = n;
                parent = n.Parent;
                n.Parent = childHi.Parent;
                childHi.Parent = parent;
            }
        }

        void AddBranch(int v1, int v2)
        {
            var last = head.Prev;
            var hi = Create(v1, last.Weight, false); hi.Parent = last; byValue[v1] = hi;
            var lo = Create(v2, 0, false); lo.Parent = last; last.ChildLo = lo; byValue[v2] = lo;
            IncWeights(lo);
        }

        /// <param name="want">expected output size, or -1 to decode until the end marker</param>
        public static byte[] Decompress(byte[] src, int want)
        {
            int pos = 0; uint buf = 0; int cnt = 0;
            int Bit() { if (cnt == 0) { if (pos >= src.Length) throw new InvalidDataException("Huffman stream ended early."); buf = src[pos++]; cnt = 8; } int b = (int)(buf & 1); buf >>= 1; cnt--; return b; }
            int Byte8() { if (cnt < 8) { if (pos >= src.Length) throw new InvalidDataException("Huffman stream ended early."); buf |= (uint)src[pos++] << cnt; cnt += 8; } int b = (int)(buf & 0xFF); buf >>= 8; cnt -= 8; return b; }

            var h = new MpqHuffman();
            int type = Byte8();
            h.sparse = type == 0;
            h.Build(type);
            var outp = new MemoryStream(want > 0 ? want : 4096);
            while (true)
            {
                var n = h.head.Next;
                if (n == h.head) throw new InvalidDataException("Empty Huffman tree.");
                while (n.ChildLo != null) n = Bit() == 1 ? n.ChildLo.Prev : n.ChildLo;
                int v = n.Value;
                if (v == 0x100) break;
                if (v == 0x101)
                {
                    v = Byte8();
                    h.AddBranch(h.head.Prev.Value, v);
                    if (!h.sparse) h.IncWeights(h.byValue[v]);
                }
                if (want >= 0 && outp.Length >= want) break;
                outp.WriteByte((byte)v);
                if (h.sparse) h.IncWeights(h.byValue[v]);
            }
            return outp.ToArray();
        }

        /// <summary>Encoder (mirror image of the decoder); used by tests to check the tree logic round-trips.</summary>
        public static byte[] Compress(byte[] data, int type)
        {
            var bits = new List<byte>(); uint buf = 0; int cnt = 0;
            void Put(uint v, int n) { buf |= v << cnt; cnt += n; while (cnt >= 8) { bits.Add((byte)buf); buf >>= 8; cnt -= 8; } }
            var h = new MpqHuffman { sparse = type == 0 };
            h.Build(type);
            Put((uint)type, 8);
            void Encode(Node n)
            {
                uint b = 0; int c = 0;
                for (var p = n.Parent; p != null; n = p, p = p.Parent) { b = (b << 1) | (p.ChildLo != n ? 1u : 0u); c++; }
                // bits were collected leaf→root; the decoder reads root→leaf, LSB first
                Put(b, c);
            }
            foreach (var x in data)
            {
                if (h.byValue[x] == null)
                {
                    Encode(h.byValue[0x101]);
                    Put(x, 8);
                    h.AddBranch(h.head.Prev.Value, x);
                    h.IncWeights(h.byValue[x]);   // a new byte is counted once, in either mode
                    continue;
                }
                Encode(h.byValue[x]);
                if (h.sparse) h.IncWeights(h.byValue[x]);
            }
            Encode(h.byValue[0x100]);
            if (cnt > 0) bits.Add((byte)buf);
            return bits.ToArray();
        }
    }

    // ======================================================================= ADPCM (wave)
    public static class Adpcm
    {
        static readonly int[] NextStep = { -1, 0, -1, 4, -1, 2, -1, 6, -1, 1, -1, 5, -1, 3, -1, 7, -1, 1, -1, 5, -1, 3, -1, 7, -1, 2, -1, 4, -1, 6, -1, 8 };
        static readonly int[] StepSize =
        {
            7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97, 107, 118, 130, 143,
            157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658, 724, 796, 876, 963, 1060, 1166, 1282, 1411,
            1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358, 5894, 6484, 7132, 7845, 8630, 9493,
            10442, 11487, 12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767,
        };

        /// <summary>Byte 0 is unused, byte 1 the bit shift, then one 16-bit start sample per channel, then one code byte per sample.</summary>
        public static byte[] Decompress(byte[] src, int want, int channels)
        {
            var outp = new MemoryStream(want > 0 ? want : 4096);
            void W(int s) { if (want < 0 || outp.Length + 2 <= want) { outp.WriteByte((byte)s); outp.WriteByte((byte)(s >> 8)); } }
            if (src.Length < 2) return outp.ToArray();
            int shift = src[1], pos = 2;
            var pred = new int[2]; var step = new int[] { 0x2C, 0x2C };
            for (int c = 0; c < channels; c++)
            {
                if (pos + 2 > src.Length) return outp.ToArray();
                pred[c] = (short)(src[pos] | (src[pos + 1] << 8)); pos += 2;
                W(pred[c]);
            }
            int ch = channels - 1;
            while (pos < src.Length)
            {
                int e = src[pos++];
                ch = (ch + 1) % channels;
                if (e == 0x80) { if (step[ch] != 0) step[ch]--; W(pred[ch]); }
                else if (e == 0x81) { step[ch] = Math.Min(step[ch] + 8, 0x58); ch = (ch + 1) % channels; }
                else
                {
                    int ss = StepSize[step[ch]];
                    int diff = ss >> shift;
                    for (int b = 0; b < 6; b++) if ((e & (1 << b)) != 0) diff += ss >> b;
                    if ((e & 0x40) != 0) pred[ch] = Math.Max(pred[ch] - diff, -32768);
                    else pred[ch] = Math.Min(pred[ch] + diff, 32767);
                    W(pred[ch]);
                    step[ch] = Math.Clamp(step[ch] + NextStep[e & 0x1F], 0, 88);
                }
            }
            return outp.ToArray();
        }
    }
}
