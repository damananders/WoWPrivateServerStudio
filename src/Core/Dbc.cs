using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AndersCraftStudio.Core
{
    /// <summary>
    /// Reader for WDBC files (the client data tables of WoW 3.3.5a). Read-only: Studio never writes DBC files here.
    /// Layout: "WDBC", record count, field count, record size, string block size, records, string block.
    /// </summary>
    public sealed class DbcFile
    {
        public int Records { get; }
        public int Fields { get; }
        public int RecordSize { get; }
        readonly byte[] data;
        readonly int recStart, strStart, strSize;

        public DbcFile(byte[] bytes)
        {
            if (bytes.Length < 20 || Encoding.ASCII.GetString(bytes, 0, 4) != "WDBC") throw new InvalidDataException("Not a WDBC file.");
            Records = BitConverter.ToInt32(bytes, 4);
            Fields = BitConverter.ToInt32(bytes, 8);
            RecordSize = BitConverter.ToInt32(bytes, 12);
            strSize = BitConverter.ToInt32(bytes, 16);
            recStart = 20;
            strStart = recStart + Records * RecordSize;
            if (Records < 0 || Fields <= 0 || RecordSize < 1 || strStart + strSize > bytes.Length) throw new InvalidDataException("DBC header doesn't match the file size.");
            data = bytes;
        }

        public static DbcFile Load(string path) => new DbcFile(File.ReadAllBytes(path));

        public uint UInt(int rec, int field)
        {
            if (rec < 0 || rec >= Records || field < 0 || field >= Fields) throw new ArgumentOutOfRangeException();
            return BitConverter.ToUInt32(data, recStart + rec * RecordSize + field * 4);
        }

        public int Int(int rec, int field) => unchecked((int)UInt(rec, field));

        public int StringBlockSize => strSize;

        /// <summary>4 bytes at a byte offset inside a record.</summary>
        public uint UIntAt(int rec, int byteOffset) => BitConverter.ToUInt32(data, recStart + rec * RecordSize + byteOffset);
        public byte ByteAt(int rec, int byteOffset) => data[recStart + rec * RecordSize + byteOffset];

        /// <summary>True when the value is a valid string-block offset pointing at the start of a string.</summary>
        public bool IsStringStart(uint off) => off == 0 ? strSize > 0 : off < strSize && data[strStart + (int)off - 1] == 0;

        public string StringAt(uint off)
        {
            if (off >= strSize) return "";
            int s = strStart + (int)off, e = s;
            while (e < strStart + strSize && data[e] != 0) e++;
            return Encoding.UTF8.GetString(data, s, e - s);
        }

        public byte[] StringBlock() { var b = new byte[strSize]; Array.Copy(data, strStart, b, 0, strSize); return b; }
        /// <summary>Bytes after the string block (normally none). Kept so a file is written back exactly as read.</summary>
        public byte[] Trailer() { int t = strStart + strSize; var b = new byte[data.Length - t]; Array.Copy(data, t, b, 0, b.Length); return b; }

        public string Str(int rec, int field)
        {
            var off = UInt(rec, field);
            if (off >= strSize) return "";
            int s = strStart + (int)off, e = s;
            while (e < strStart + strSize && data[e] != 0) e++;
            return Encoding.UTF8.GetString(data, s, e - s);
        }

        /// <summary>id (field 0 by default) → string field, skipping empty strings.</summary>
        public Dictionary<uint, string> Names(int nameField, int idField = 0)
        {
            var d = new Dictionary<uint, string>();
            for (int r = 0; r < Records; r++)
            {
                var s = Str(r, nameField);
                if (s.Length > 0) d[UInt(r, idField)] = s;
            }
            return d;
        }

        public Dictionary<uint, uint> Map(int valueField, int idField = 0)
        {
            var d = new Dictionary<uint, uint>();
            for (int r = 0; r < Records; r++) d[UInt(r, idField)] = UInt(r, valueField);
            return d;
        }

        /// <summary>Builds a WDBC image (used by tests).</summary>
        public static byte[] Build(int fields, IList<object[]> rows)
        {
            var strings = new MemoryStream(); strings.WriteByte(0);
            var offsets = new Dictionary<string, uint> { [""] = 0 };
            var recs = new MemoryStream();
            var w = new BinaryWriter(recs);
            foreach (var row in rows)
                for (int f = 0; f < fields; f++)
                {
                    var v = f < row.Length ? row[f] : 0u;
                    if (v is string s)
                    {
                        if (!offsets.TryGetValue(s, out var o)) { o = (uint)strings.Length; var b = Encoding.UTF8.GetBytes(s); strings.Write(b, 0, b.Length); strings.WriteByte(0); offsets[s] = o; }
                        w.Write(o);
                    }
                    else w.Write(Convert.ToUInt32(v));
                }
            var outp = new MemoryStream();
            var ow = new BinaryWriter(outp);
            ow.Write(Encoding.ASCII.GetBytes("WDBC")); ow.Write(rows.Count); ow.Write(fields); ow.Write(fields * 4); ow.Write((int)strings.Length);
            ow.Write(recs.ToArray()); ow.Write(strings.ToArray());
            return outp.ToArray();
        }
    }

    /// <summary>Names looked up from the server's extracted DBC folder (Data\dbc). Loaded lazily; missing files just mean "no names".</summary>
    public sealed class DbcNames
    {
        readonly string dir;
        readonly Dictionary<string, Dictionary<uint, string>> cache = new Dictionary<string, Dictionary<uint, string>>();
        Dictionary<uint, uint> factionTemplateToFaction;
        readonly object gate = new object();

        public DbcNames(string dbcDir) { dir = dbcDir; }

        /// <summary>Staged changes to lay over the files (new rows from Asset import, Spell editor…), or null.</summary>
        public Func<IList<Change>> Pending { get; set; }

        DbcTable LoadT(string file)
        {
            var t = DbcTable.Load(Path.Combine(dir, file));
            var pend = Pending?.Invoke();
            if (pend == null || !pend.Any(c => c.Dbc != null && string.Equals(c.Table, file, StringComparison.OrdinalIgnoreCase))) return t;
            return DbcPatcher.Apply(t, pend, false, new List<string>()) ?? t;
        }
        public bool Available => dir != null && Directory.Exists(dir);

        // 3.3.5a (build 12340) field positions of the English name column
        public static readonly Dictionary<string, (string file, int field)> Sources = new Dictionary<string, (string, int)>
        {
            ["spell"] = ("Spell.dbc", 136),
            ["faction"] = ("Faction.dbc", 23),
            ["family"] = ("CreatureFamily.dbc", 10),
            ["questsort"] = ("QuestSort.dbc", 1),
            ["area"] = ("AreaTable.dbc", 11),
            ["map"] = ("Map.dbc", 5),
            ["skill"] = ("SkillLine.dbc", 3),
            ["title"] = ("CharTitles.dbc", 2),
            ["race"] = ("ChrRaces.dbc", 14),
            ["class"] = ("ChrClasses.dbc", 4),
            ["itemicon"] = ("ItemDisplayInfo.dbc", 5),
            ["modelfile"] = ("CreatureModelData.dbc", 2),
        };

        public Dictionary<uint, string> Table(string kind)
        {
            lock (gate)
            {
                if (cache.TryGetValue(kind, out var d)) return d;
                d = new Dictionary<uint, string>();
                try
                {
                    if (kind == "factiontemplate")
                    {
                        var fac = Table("faction");
                        foreach (var kv in FactionTemplates()) if (fac.TryGetValue(kv.Value, out var n)) d[kv.Key] = n;
                    }
                    else if (kind == "creaturedisplay")
                    {
                        var models = Table("modelfile");
                        var f = LoadT("CreatureDisplayInfo.dbc");
                        foreach (var r in f.Rows)
                            if (models.TryGetValue(r.V[1], out var m)) d[r.V[0]] = Path.GetFileNameWithoutExtension(m.Replace('\\', '/'));
                    }
                    else if (Sources.TryGetValue(kind, out var src) && Available)
                    {
                        var p = Path.Combine(dir, src.file);
                        if (File.Exists(p))
                        {
                            var t = LoadT(src.file);
                            foreach (var r in t.Rows)
                            {
                                var nm = src.field < r.S.Length ? r.S[src.field] : null;
                                // item displays: "icon · model" so models (e.g. imported ones) can be found by file name too
                                if (kind == "itemicon" && r.S.Length > 1 && !string.IsNullOrEmpty(r.S[1]))
                                    nm = (string.IsNullOrEmpty(nm) ? "(no icon)" : nm) + " · " + Path.GetFileNameWithoutExtension(r.S[1]);
                                if (!string.IsNullOrEmpty(nm)) d[r.V[0]] = nm;
                            }
                        }
                    }
                }
                catch { d = new Dictionary<uint, string>(); }
                cache[kind] = d;
                return d;
            }
        }

        Dictionary<uint, uint> FactionTemplates()
        {
            if (factionTemplateToFaction != null) return factionTemplateToFaction;
            var p = Path.Combine(dir ?? "", "FactionTemplate.dbc");
            factionTemplateToFaction = File.Exists(p) ? DbcFile.Load(p).Map(1) : new Dictionary<uint, uint>();
            return factionTemplateToFaction;
        }

        public string Name(string kind, long id) => id >= 0 && id <= uint.MaxValue && Table(kind).TryGetValue((uint)id, out var n) ? n : null;

        public IEnumerable<(long id, string name)> Search(string kind, string q, int max = 200)
        {
            var t = Table(kind);
            q = (q ?? "").Trim();
            bool num = long.TryParse(q, out var id);
            return t.Where(kv => q.Length == 0 || (num && kv.Key == id) || kv.Value.Contains(q, StringComparison.OrdinalIgnoreCase) || kv.Key.ToString().StartsWith(q))
                    .OrderBy(kv => num && kv.Key == id ? 0 : 1).ThenBy(kv => kv.Value.Length).ThenBy(kv => kv.Key)
                    .Take(max).Select(kv => ((long)kv.Key, kv.Value));
        }
    }
}
