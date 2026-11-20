using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace AndersCraftStudio.Core
{
    /// <summary>MPQ hashing and table encryption (the scheme used by Blizzard archives).</summary>
    public static class MpqCrypto
    {
        static readonly uint[] Table = Build();

        static uint[] Build()
        {
            var t = new uint[0x500];
            uint seed = 0x00100001;
            for (int i1 = 0; i1 < 0x100; i1++)
                for (int i2 = i1, n = 0; n < 5; n++, i2 += 0x100)
                {
                    seed = (seed * 125 + 3) % 0x2AAAAB; uint hi = (seed & 0xFFFF) << 16;
                    seed = (seed * 125 + 3) % 0x2AAAAB; uint lo = seed & 0xFFFF;
                    t[i2] = hi | lo;
                }
            return t;
        }

        public const uint TableOffset = 0, NameA = 1, NameB = 2, FileKey = 3;

        public static uint Hash(string s, uint type)
        {
            uint s1 = 0x7FED7FED, s2 = 0xEEEEEEEE;
            foreach (char c0 in s)
            {
                char c = c0 == '/' ? '\\' : char.ToUpperInvariant(c0);
                uint ch = (byte)c;
                s1 = Table[type * 0x100 + ch] ^ (s1 + s2);
                s2 = ch + s1 + s2 + (s2 << 5) + 3;
            }
            return s1;
        }

        public static void Encrypt(uint[] data, uint key)
        {
            uint s2 = 0xEEEEEEEE;
            for (int i = 0; i < data.Length; i++)
            {
                s2 += Table[0x400 + (key & 0xFF)];
                uint plain = data[i];
                data[i] = plain ^ (key + s2);
                key = ((~key << 21) + 0x11111111) | (key >> 11);
                s2 = plain + s2 + (s2 << 5) + 3;
            }
        }

        public static void Decrypt(uint[] data, uint key)
        {
            uint s2 = 0xEEEEEEEE;
            for (int i = 0; i < data.Length; i++)
            {
                s2 += Table[0x400 + (key & 0xFF)];
                uint plain = data[i] ^ (key + s2);
                data[i] = plain;
                key = ((~key << 21) + 0x11111111) | (key >> 11);
                s2 = plain + s2 + (s2 << 5) + 3;
            }
        }

        public static void DecryptBytes(byte[] b, int len, uint key)
        {
            int n = len / 4;
            var u = new uint[n];
            Buffer.BlockCopy(b, 0, u, 0, n * 4);
            Decrypt(u, key);
            Buffer.BlockCopy(u, 0, b, 0, n * 4);
        }

        /// <summary>Encryption key of a stored file: hash of its plain name (without folders), optionally adjusted by position and size.</summary>
        public static uint FileKeyOf(string path, long blockPos, uint fileSize, bool fixKey)
        {
            var name = path.Replace('/', '\\');
            int slash = name.LastIndexOf('\\');
            if (slash >= 0) name = name.Substring(slash + 1);
            uint key = Hash(name, FileKey);
            if (fixKey) key = (key + (uint)blockPos) ^ fileSize;
            return key;
        }
    }

    [Flags]
    public enum MpqFileFlags : uint
    {
        Implode = 0x00000100, Compress = 0x00000200, Encrypted = 0x00010000, FixKey = 0x00020000,
        PatchFile = 0x00100000, SingleUnit = 0x01000000, DeleteMarker = 0x02000000, SectorCrc = 0x04000000, Exists = 0x80000000,
    }

    public sealed class MpqEntry
    {
        public string Name;          // null when the archive has no name for it
        public long Offset;          // absolute position of the file data in the stream
        public uint CompressedSize, FileSize;
        public MpqFileFlags Flags;
        public int BlockIndex;
        public string GuessedExt;
        public bool Compressed => (Flags & (MpqFileFlags.Compress | MpqFileFlags.Implode)) != 0;
    }

    /// <summary>
    /// Read-only MPQ reader (format v1/v2, as used by the 3.3.5a client). Supports stored, zlib-compressed, single-unit
    /// and encrypted files. bzip2 / PKWARE-implode compressed files are listed but can't be extracted.
    /// </summary>
    public sealed class MpqReader : IDisposable
    {
        readonly Stream s;
        readonly long archiveStart;
        public int FormatVersion { get; private set; }
        public int SectorSize { get; private set; }
        uint[] hashTable;     // 4 uints per entry
        public List<MpqEntry> Blocks { get; } = new List<MpqEntry>();

        public MpqReader(string path) : this(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) { }

        public MpqReader(Stream stream)
        {
            s = stream;
            var br = new BinaryReader(s, Encoding.ASCII, true);
            // the header can sit at any 512-byte boundary (after a user-data block)
            long pos = 0;
            while (true)
            {
                if (pos + 32 > s.Length) throw new InvalidDataException("Not an MPQ archive (no header found).");
                s.Position = pos;
                var sig = br.ReadUInt32();
                if (sig == 0x1A51504D) break;                                      // 'MPQ\x1A'
                if (sig == 0x1B51504D) { br.ReadUInt32(); pos += br.ReadUInt32(); continue; } // 'MPQ\x1B' user data → points to header
                pos += 512;
                if (pos > 64 * 1024 * 1024) throw new InvalidDataException("Not an MPQ archive (no header found).");
            }
            archiveStart = pos;
            uint headerSize = br.ReadUInt32();
            br.ReadUInt32(); // archive size (v1)
            FormatVersion = br.ReadUInt16();
            SectorSize = 512 << br.ReadUInt16();
            long hashPos = br.ReadUInt32(), blockPos = br.ReadUInt32();
            uint hashCount = br.ReadUInt32(), blockCount = br.ReadUInt32();
            long hiBlockPos = 0;
            if (FormatVersion >= 1 && headerSize >= 44)
            {
                hiBlockPos = br.ReadInt64();
                hashPos |= (long)br.ReadUInt16() << 32;
                blockPos |= (long)br.ReadUInt16() << 32;
            }
            hashTable = ReadTable(archiveStart + hashPos, hashCount, MpqCrypto.Hash("(hash table)", MpqCrypto.FileKey));
            var blocks = ReadTable(archiveStart + blockPos, blockCount, MpqCrypto.Hash("(block table)", MpqCrypto.FileKey));
            ushort[] hi = null;
            if (hiBlockPos != 0)
            {
                s.Position = archiveStart + hiBlockPos;
                hi = new ushort[blockCount];
                for (int i = 0; i < blockCount; i++) hi[i] = br.ReadUInt16();
            }
            for (int i = 0; i < blockCount; i++)
                Blocks.Add(new MpqEntry
                {
                    BlockIndex = i,
                    Offset = archiveStart + (blocks[i * 4] | ((long)(hi?[i] ?? 0) << 32)),
                    CompressedSize = blocks[i * 4 + 1], FileSize = blocks[i * 4 + 2], Flags = (MpqFileFlags)blocks[i * 4 + 3],
                });
            // names from the archive's own (listfile), when it has one
            try
            {
                var lf = Find("(listfile)");
                if (lf != null)
                    foreach (var line in Encoding.UTF8.GetString(Read(lf)).Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries))
                        Name(line.Trim());
                var lfe = Find("(listfile)"); if (lfe != null) lfe.Name = "(listfile)";
                var at = Find("(attributes)"); if (at != null) at.Name = "(attributes)";
            }
            catch { }
        }

        uint[] ReadTable(long pos, uint entries, uint key)
        {
            var bytes = new byte[entries * 16];
            s.Position = pos;
            ReadExactly(bytes, bytes.Length);
            var u = new uint[entries * 4];
            Buffer.BlockCopy(bytes, 0, u, 0, bytes.Length);
            MpqCrypto.Decrypt(u, key);
            return u;
        }

        void ReadExactly(byte[] b, int n)
        {
            int got = 0;
            while (got < n) { int r = s.Read(b, got, n - got); if (r <= 0) throw new EndOfStreamException("The MPQ is truncated."); got += r; }
        }

        int HashCount => hashTable.Length / 4;

        /// <summary>Looks a path up in the hash table (locale-neutral entry preferred).</summary>
        public MpqEntry Find(string path)
        {
            if (HashCount == 0) return null;
            uint mask = (uint)HashCount - 1;
            uint start = MpqCrypto.Hash(path, MpqCrypto.TableOffset) & mask;
            uint a = MpqCrypto.Hash(path, MpqCrypto.NameA), b = MpqCrypto.Hash(path, MpqCrypto.NameB);
            MpqEntry any = null;
            for (uint i = start, n = 0; n < HashCount; i = (i + 1) & mask, n++)
            {
                uint block = hashTable[i * 4 + 3];
                if (block == 0xFFFFFFFF) break;           // empty: end of chain
                if (block == 0xFFFFFFFE) continue;        // deleted
                if (hashTable[i * 4] == a && hashTable[i * 4 + 1] == b && block < Blocks.Count)
                {
                    var e = Blocks[(int)block];
                    if ((hashTable[i * 4 + 2] & 0xFFFF) == 0) return e;  // neutral locale
                    any ??= e;
                }
            }
            return any;
        }

        /// <summary>Raw hash-table entries (name hash A, name hash B, block index) that point at a live block.</summary>
        public IEnumerable<(uint a, uint b, int block)> HashEntries()
        {
            for (int i = 0; i < HashCount; i++)
            {
                uint block = hashTable[i * 4 + 3];
                if (block < Blocks.Count) yield return (hashTable[i * 4], hashTable[i * 4 + 1], (int)block);
            }
        }

        /// <summary>Name shown for a file: its real path, or "unknown #XXXXXXXX.ext" (block index + a guessed extension).</summary>
        public string DisplayName(MpqEntry e)
        {
            if (e.Name != null) return e.Name;
            if (e.GuessedExt == null)
            {
                try { var d = Read(e); e.GuessedExt = NameRecovery.GuessExtension(d); } catch { e.GuessedExt = ""; }
            }
            return $"unknown #{e.BlockIndex:X5}{e.GuessedExt}";
        }

        public MpqEntry FindDisplay(string display)
        {
            var m = System.Text.RegularExpressions.Regex.Match(display, @"^unknown #([0-9A-F]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success) { int idx = Convert.ToInt32(m.Groups[1].Value, 16); return idx < Blocks.Count ? Blocks[idx] : null; }
            return Find(display);
        }

        public bool Name(string path)
        {
            var e = Find(path);
            if (e == null) return false;
            e.Name ??= path.Replace('/', '\\');
            return true;
        }

        public IEnumerable<MpqEntry> Files => Blocks.Where(b => (b.Flags & MpqFileFlags.Exists) != 0 && (b.Flags & MpqFileFlags.DeleteMarker) == 0);

        public byte[] Read(string path)
        {
            var e = Find(path) ?? throw new FileNotFoundException("Not in this archive: " + path);
            e.Name ??= path;
            return Read(e);
        }

        public byte[] Read(MpqEntry e)
        {
            if ((e.Flags & MpqFileFlags.PatchFile) != 0) throw new NotSupportedException("Incremental patch files aren't supported.");
            bool enc = (e.Flags & MpqFileFlags.Encrypted) != 0;
            if (enc && e.Name == null) throw new NotSupportedException("This file is encrypted and its name is unknown, so it can't be decrypted.");
            uint key = enc ? MpqCrypto.FileKeyOf(e.Name, e.Offset - archiveStart, e.FileSize, (e.Flags & MpqFileFlags.FixKey) != 0) : 0;
            var outp = new byte[e.FileSize];
            if (e.FileSize == 0) return outp;

            if ((e.Flags & MpqFileFlags.SingleUnit) != 0 || !e.Compressed && !enc)
            {
                var raw = new byte[e.CompressedSize];
                s.Position = e.Offset; ReadExactly(raw, raw.Length);
                if (enc) MpqCrypto.DecryptBytes(raw, raw.Length, key);
                if (e.Compressed && e.CompressedSize < e.FileSize) Decompress(raw, raw.Length, outp, 0, (int)e.FileSize, e.Flags);
                else Buffer.BlockCopy(raw, 0, outp, 0, (int)Math.Min(raw.Length, outp.Length));
                return outp;
            }

            int size = SectorSizeOf(e, enc, key);
            try { return ReadSectors(e, enc, key, size); }
            catch (InvalidDataException ex) when (ex.Data["sectorGot"] is int got && got > 0 && got < size && (got & (got - 1)) == 0)
            {
                // some archives (e.g. backup-enUS.MPQ) hold files written with a smaller sector size than the header says
                return ReadSectors(e, enc, key, got);
            }
        }

        /// <summary>The sector size a file really uses. Normally the archive's; but the sector-offset table starts with
        /// its own length, which reveals files written with a different size (e.g. in backup-enUS.MPQ).</summary>
        int SectorSizeOf(MpqEntry e, bool enc, uint key)
        {
            int size = SectorSize;
            if (!e.Compressed || (e.Flags & MpqFileFlags.SingleUnit) != 0) return size;
            var ob = new byte[4];
            s.Position = e.Offset; ReadExactly(ob, 4);
            var first = new[] { BitConverter.ToUInt32(ob, 0) };
            if (enc) MpqCrypto.Decrypt(first, key - 1);
            int tableSectors = (int)(first[0] / 4) - 1 - ((e.Flags & MpqFileFlags.SectorCrc) != 0 ? 1 : 0);
            int expected = (int)((e.FileSize + SectorSize - 1) / SectorSize);
            if (tableSectors > 0 && tableSectors != expected)
                for (int sz = 512; sz <= 1 << 20; sz <<= 1)
                    if ((e.FileSize + sz - 1) / sz == tableSectors) return sz;
            return size;
        }

        byte[] ReadSectors(MpqEntry e, bool enc, uint key, int sectorSize)
        {
            var outp = new byte[e.FileSize];
            int SectorSize = sectorSize;
            int sectors = (int)((e.FileSize + SectorSize - 1) / SectorSize);
            uint[] offs;
            if (e.Compressed)
            {
                int n = sectors + 1 + ((e.Flags & MpqFileFlags.SectorCrc) != 0 ? 1 : 0);
                var ob = new byte[n * 4];
                s.Position = e.Offset; ReadExactly(ob, ob.Length);
                offs = new uint[n];
                Buffer.BlockCopy(ob, 0, offs, 0, ob.Length);
                if (enc) MpqCrypto.Decrypt(offs, key - 1);
            }
            else
            {
                offs = new uint[sectors + 1];
                for (int i = 0; i <= sectors; i++) offs[i] = (uint)Math.Min((long)i * SectorSize, e.FileSize);
            }
            var buf = new byte[SectorSize + 64];
            for (int i = 0; i < sectors; i++)
            {
                int len = (int)(offs[i + 1] - offs[i]);
                int want = (int)Math.Min(SectorSize, e.FileSize - (long)i * SectorSize);
                if (len > buf.Length) buf = new byte[len];
                s.Position = e.Offset + offs[i]; ReadExactly(buf, len);
                if (enc) MpqCrypto.DecryptBytes(buf, len, key + (uint)i);
                if (e.Compressed && len < want) Decompress(buf, len, outp, i * SectorSize, want, e.Flags);
                else Buffer.BlockCopy(buf, 0, outp, i * SectorSize, want);
            }
            return outp;
        }

        static void Decompress(byte[] src, int len, byte[] dst, int dstOff, int want, MpqFileFlags flags)
        {
            byte[] r;
            if ((flags & MpqFileFlags.Implode) != 0)
            {
                // old-style "imploded" files: PKWARE stream with no compression-type byte
                var raw = new byte[len]; Buffer.BlockCopy(src, 0, raw, 0, len);
                r = Explode.Decompress(raw, want);
            }
            else r = MpqCompression.DecompressSector(src, len, want);
            if (r.Length != want) { var ex = new InvalidDataException("A compressed sector is shorter than expected."); ex.Data["sectorGot"] = r.Length; throw ex; }
            Buffer.BlockCopy(r, 0, dst, dstOff, want);
        }

        /// <summary>For diagnostics: the compression byte of a file's first stored sector (0 when stored raw), and whether it's encrypted.</summary>
        public (byte mask, bool fileImplode) FirstSectorMask(MpqEntry e)
        {
            if (!e.Compressed || e.FileSize == 0) return (0, false);
            if ((e.Flags & MpqFileFlags.Implode) != 0) return (0x08, true);
            bool enc = (e.Flags & MpqFileFlags.Encrypted) != 0;
            if (enc && e.Name == null) return (0xFF, false);
            uint key = enc ? MpqCrypto.FileKeyOf(e.Name, e.Offset - archiveStart, e.FileSize, (e.Flags & MpqFileFlags.FixKey) != 0) : 0;
            if ((e.Flags & MpqFileFlags.SingleUnit) != 0)
            {
                if (e.CompressedSize >= e.FileSize) return (0, false);
                var b = new byte[Math.Min(16, (int)e.CompressedSize)];
                s.Position = e.Offset; ReadExactly(b, b.Length);
                if (enc) MpqCrypto.DecryptBytes(b, b.Length & ~3, key);
                return (b[0], false);
            }
            var ob = new byte[8];
            s.Position = e.Offset; ReadExactly(ob, 8);
            var offs = new uint[2]; Buffer.BlockCopy(ob, 0, offs, 0, 8);
            if (enc) MpqCrypto.Decrypt(offs, key - 1);
            int want = (int)Math.Min(SectorSizeOf(e, enc, key), e.FileSize);
            if (offs[1] - offs[0] >= want) return (0, false);
            var sb = new byte[16];
            s.Position = e.Offset + offs[0]; ReadExactly(sb, (int)Math.Min(16, offs[1] - offs[0]));
            if (enc) MpqCrypto.DecryptBytes(sb, 16, key);
            return (sb[0], false);
        }

        public void Dispose() => s.Dispose();
    }

    /// <summary>
    /// Writes a new MPQ (format v1) that the 3.3.5a client loads as a patch: zlib-compressed sectors, a (listfile),
    /// unencrypted files, encrypted hash and block tables.
    /// </summary>
    public static class MpqWriter
    {
        public const int SectorShift = 3; // 4096-byte sectors, the client's usual size

        /// <summary>files: archive path (e.g. DBFilesClient\Spell.dbc) → contents.</summary>
        public static void Write(Stream output, IList<(string path, byte[] data)> files, bool compress = true)
        {
            var list = files.Select(f => (path: f.path.Replace('/', '\\').TrimStart('\\'), f.data)).ToList();
            var dupes = list.GroupBy(f => f.path, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (dupes.Count > 0) throw new ArgumentException("The same path appears twice: " + string.Join(", ", dupes));
            if (list.Any(f => f.path.Length == 0 || f.path.Contains("..") || f.path.Contains(':'))) throw new ArgumentException("Archive paths must be relative, like DBFilesClient\\Spell.dbc.");
            list.RemoveAll(f => f.path.Equals("(listfile)", StringComparison.OrdinalIgnoreCase));
            var listfile = Encoding.UTF8.GetBytes(string.Join("\r\n", list.Select(f => f.path)) + "\r\n");
            list.Add(("(listfile)", listfile));

            int sector = 512 << SectorShift;
            int hashSize = 16;
            while (hashSize < list.Count * 2) hashSize <<= 1;
            var bw = new BinaryWriter(output, Encoding.ASCII, true);
            long start = output.Position;
            bw.Write(new byte[32]); // header placeholder
            var blocks = new List<uint[]>();
            foreach (var (path, data) in list)
            {
                long pos = output.Position - start;
                if (pos > uint.MaxValue) throw new IOException("Patches larger than 4 GB aren't supported.");
                uint flags = (uint)MpqFileFlags.Exists;
                uint stored;
                if (data.Length == 0) { stored = 0; }
                else if (!compress)
                {
                    bw.Write(data); stored = (uint)data.Length;
                }
                else
                {
                    int sectors = (data.Length + sector - 1) / sector;
                    var chunks = new List<byte[]>();
                    for (int i = 0; i < sectors; i++)
                    {
                        int off = i * sector, len = Math.Min(sector, data.Length - off);
                        var z = Zlib(data, off, len);
                        if (z.Length + 1 < len) { var c = new byte[z.Length + 1]; c[0] = 0x02; Buffer.BlockCopy(z, 0, c, 1, z.Length); chunks.Add(c); }
                        else { var raw = new byte[len]; Buffer.BlockCopy(data, off, raw, 0, len); chunks.Add(raw); }
                    }
                    uint o = (uint)((sectors + 1) * 4);
                    var table = new uint[sectors + 1];
                    for (int i = 0; i < sectors; i++) { table[i] = o; o += (uint)chunks[i].Length; }
                    table[sectors] = o;
                    foreach (var t in table) bw.Write(t);
                    foreach (var c in chunks) bw.Write(c);
                    stored = o;
                    flags |= (uint)MpqFileFlags.Compress;
                }
                blocks.Add(new[] { (uint)pos, stored, (uint)data.Length, flags });
            }

            // hash table: linear probing from the table-offset hash
            var hash = new uint[hashSize * 4];
            for (int i = 0; i < hashSize; i++) { hash[i * 4] = 0xFFFFFFFF; hash[i * 4 + 1] = 0xFFFFFFFF; hash[i * 4 + 2] = 0xFFFFFFFF; hash[i * 4 + 3] = 0xFFFFFFFF; }
            for (int b = 0; b < list.Count; b++)
            {
                var p = list[b].path;
                uint i = MpqCrypto.Hash(p, MpqCrypto.TableOffset) & (uint)(hashSize - 1);
                while (hash[i * 4 + 3] != 0xFFFFFFFF) i = (i + 1) & (uint)(hashSize - 1);
                hash[i * 4] = MpqCrypto.Hash(p, MpqCrypto.NameA);
                hash[i * 4 + 1] = MpqCrypto.Hash(p, MpqCrypto.NameB);
                hash[i * 4 + 2] = 0;              // locale neutral (low 16 bits) + platform 0
                hash[i * 4 + 3] = (uint)b;
            }
            long hashPos = output.Position - start;
            MpqCrypto.Encrypt(hash, MpqCrypto.Hash("(hash table)", MpqCrypto.FileKey));
            foreach (var u in hash) bw.Write(u);
            long blockPos = output.Position - start;
            var bt = blocks.SelectMany(x => x).ToArray();
            MpqCrypto.Encrypt(bt, MpqCrypto.Hash("(block table)", MpqCrypto.FileKey));
            foreach (var u in bt) bw.Write(u);
            long end = output.Position;
            if (end - start > uint.MaxValue) throw new IOException("Patches larger than 4 GB aren't supported.");

            output.Position = start;
            bw.Write(0x1A51504Du); bw.Write(32u); bw.Write((uint)(end - start)); bw.Write((ushort)0); bw.Write((ushort)SectorShift);
            bw.Write((uint)hashPos); bw.Write((uint)blockPos); bw.Write((uint)hashSize); bw.Write((uint)blocks.Count);
            output.Position = end;
            bw.Flush();
        }

        static byte[] Zlib(byte[] data, int off, int len)
        {
            using var ms = new MemoryStream();
            using (var z = new ZLibStream(ms, CompressionLevel.SmallestSize, true)) z.Write(data, off, len);
            return ms.ToArray();
        }

        /// <summary>Builds the archive from a folder: each file's path under the folder becomes its archive path.</summary>
        public static List<(string path, byte[] data)> CollectFolder(string folder)
        {
            var root = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
            return Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Select(f => (f.Substring(root.Length), File.ReadAllBytes(f)))
                .OrderBy(f => f.Item1, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Writes to a temp file next to the target, verifies every file by reading it back, then moves it into place.</summary>
        public static List<string> BuildVerified(string target, IList<(string path, byte[] data)> files, bool compress = true)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target)));
            var tmp = target + ".building";
            using (var fs = File.Create(tmp)) Write(fs, files, compress);
            var problems = Verify(tmp, files);
            if (problems.Count > 0) { File.Delete(tmp); return problems; }
            File.Move(tmp, target, true);
            return problems;
        }

        public static List<string> Verify(string mpqPath, IList<(string path, byte[] data)> files)
        {
            var problems = new List<string>();
            using var r = new MpqReader(mpqPath);
            foreach (var (path, data) in files)
            {
                try
                {
                    var back = r.Read(path.Replace('/', '\\').TrimStart('\\'));
                    if (!back.AsSpan().SequenceEqual(data)) problems.Add(path + ": contents differ after reading back.");
                }
                catch (Exception ex) { problems.Add(path + ": " + ex.Message); }
            }
            return problems;
        }
    }

    /// <summary>What the 3.3.5a client does with patch archive names.</summary>
    public static class PatchNames
    {
        /// <summary>Base-folder patches the client loads: patch.MPQ, patch-2..9 and patch-A..Z (later letters win).</summary>
        public static bool IsLoadable(string fileName, out string why)
        {
            why = null;
            var n = fileName.ToLowerInvariant();
            if (!n.EndsWith(".mpq")) { why = "Patch files must end in .MPQ."; return false; }
            var stem = n.Substring(0, n.Length - 4);
            if (stem == "patch") return true;
            if (stem.StartsWith("patch-") && stem.Length == 7 && char.IsLetterOrDigit(stem[6]) && stem[6] != '0' && stem[6] != '1') return true;
            why = "The client only loads patch.MPQ, patch-2 … patch-9 and patch-A … patch-Z in the Data folder.";
            return false;
        }

        /// <summary>Load order rank: later (higher) wins when two patches contain the same file.</summary>
        public static int Rank(string fileName)
        {
            var stem = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
            if (stem == "patch") return 1;
            if (stem.Length == 7) { char c = stem[6]; if (char.IsDigit(c)) return c - '0'; if (c >= 'a' && c <= 'z') return 10 + (c - 'a'); }
            return 0;
        }
    }
}
