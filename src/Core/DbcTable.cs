using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace AndersCraftStudio.Core
{
    /// <summary>One record. V holds every field's raw value (string offsets for text fields, as read); S holds the text of text fields.</summary>
    public sealed class DbcRow
    {
        public uint[] V;
        public string[] S;
        public DbcRow Clone() => new DbcRow { V = (uint[])V.Clone(), S = (string[])S.Clone() };
    }

    /// <summary>How the string block is written when text changed.</summary>
    public enum DbcStringMode
    {
        /// <summary>Rebuild from scratch when the layout's text fields are certain, otherwise append.</summary>
        Auto,
        /// <summary>Keep the original block and add new text at its end; untouched fields keep their exact offsets.</summary>
        Append,
        /// <summary>Write a new, compact block (text in order of first use, duplicates shared, empty text at offset 0).</summary>
        Rebuild,
    }

    /// <summary>A DBC file loaded for editing. Writing with no edits gives back the exact original bytes.</summary>
    public sealed class DbcTable
    {
        public string File { get; private set; }
        public DbcLayout Layout { get; private set; }
        public List<DbcRow> Rows { get; private set; } = new List<DbcRow>();
        int headerFields, headerRecordSize;
        byte[] origStrings, trailer;
        Dictionary<uint, string> origStringAt;

        public static DbcTable Read(string file, byte[] bytes, DbcLayout layout = null)
        {
            var f = new DbcFile(bytes);
            var l = layout ?? DbcLayout.For(file, f);
            var t = new DbcTable { File = Path.GetFileName(file), Layout = l, headerFields = f.Fields, headerRecordSize = f.RecordSize, origStrings = f.StringBlock(), trailer = f.Trailer() };
            t.origStringAt = new Dictionary<uint, string>();
            for (int r = 0; r < f.Records; r++)
            {
                var row = new DbcRow { V = new uint[l.FieldCount], S = new string[l.FieldCount] };
                for (int i = 0; i < l.FieldCount; i++)
                {
                    row.V[i] = l.FieldSize[i] == 1 ? f.ByteAt(r, l.FieldOffset[i]) : f.UIntAt(r, l.FieldOffset[i]);
                    if (l.FieldIsString[i])
                    {
                        if (!t.origStringAt.TryGetValue(row.V[i], out var s)) t.origStringAt[row.V[i]] = s = f.IsStringStart(row.V[i]) ? f.StringAt(row.V[i]) : null;
                        row.S[i] = s ?? "";
                    }
                }
                t.Rows.Add(row);
            }
            return t;
        }

        public static DbcTable Load(string path, DbcLayout layout = null) => Read(path, System.IO.File.ReadAllBytes(path), layout);

        public DbcTable Clone()
        {
            var t = (DbcTable)MemberwiseClone();
            t.Rows = Rows.Select(r => r.Clone()).ToList();
            t.index = null;
            return t;
        }

        public DbcRow NewRow()
        {
            var r = new DbcRow { V = new uint[Layout.FieldCount], S = new string[Layout.FieldCount] };
            for (int i = 0; i < Layout.FieldCount; i++) if (Layout.FieldIsString[i]) { r.S[i] = ""; r.V[i] = uint.MaxValue; }
            return r;
        }

        // ---------- keys ----------
        Dictionary<uint, int> index;
        public void InvalidateIndex() => index = null;
        public uint KeyOf(DbcRow r) => Layout.HasId ? r.V[0] : (uint)Rows.IndexOf(r);
        public int IndexOf(uint key)
        {
            if (!Layout.HasId) return key < Rows.Count ? (int)key : -1;
            if (index == null || index.Count != Rows.Count)
            {
                index = new Dictionary<uint, int>();
                for (int i = 0; i < Rows.Count; i++) index[Rows[i].V[0]] = i;
            }
            return index.TryGetValue(key, out var x) && x < Rows.Count && Rows[x].V[0] == key ? x : RebuildAndFind(key);
        }
        int RebuildAndFind(uint key) { index = null; if (!Layout.HasId) return -1; index = new Dictionary<uint, int>(); for (int i = 0; i < Rows.Count; i++) index[Rows[i].V[0]] = i; return index.TryGetValue(key, out var x) ? x : -1; }
        public DbcRow Find(uint key) { int i = IndexOf(key); return i < 0 ? null : Rows[i]; }
        public uint NextFreeId(uint start) { if (!Layout.HasId) return (uint)Rows.Count; var used = new HashSet<uint>(Rows.Select(r => r.V[0])); var id = Math.Max(start, 1); while (used.Contains(id)) id++; return id; }

        // ---------- text values ----------
        /// <summary>Canonical text of one field, used by the grid, CSV and staged changes. Numbers keep their exact bits.</summary>
        public string FieldText(DbcRow r, int field)
        {
            if (Layout.FieldIsString[field]) return r.S[field] ?? "";
            var c = Layout.ColumnOf(field);
            uint v = r.V[field];
            var kind = c.Kind == DbcKind.Loc ? DbcKind.Flags : c.Kind;
            return Format(kind, v);
        }

        public static string Format(DbcKind kind, uint v) => kind switch
        {
            DbcKind.Int => unchecked((int)v).ToString(CultureInfo.InvariantCulture),
            DbcKind.Float => BitConverter.Int32BitsToSingle(unchecked((int)v)).ToString("R", CultureInfo.InvariantCulture),
            DbcKind.Flags => "0x" + v.ToString("X"),
            _ => v.ToString(CultureInfo.InvariantCulture),
        };

        /// <summary>Parses text for a numeric field: decimal, negative, 0x hex or a float. Returns false with a reason.</summary>
        public static bool TryParse(DbcKind kind, string text, out uint v, out string error)
        {
            v = 0; error = null;
            var s = (text ?? "").Trim();
            if (s.Length == 0) { error = "A number is needed."; return false; }
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && kind != DbcKind.Float)
            {
                if (uint.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) return CheckByte(kind, v, out error);
                error = "Not a valid hex number."; return false;
            }
            if (kind == DbcKind.Float)
            {
                if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) { v = unchecked((uint)BitConverter.SingleToInt32Bits(f)); return true; }
                error = "Not a valid decimal number (use a dot: 1.5)."; return false;
            }
            if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
            {
                if (l < int.MinValue || l > uint.MaxValue) { error = "Too large for a 32-bit field."; return false; }
                v = unchecked((uint)l);
                return CheckByte(kind, v, out error);
            }
            error = kind == DbcKind.Flags ? "Enter a number or 0x… hex." : "Enter a whole number.";
            return false;
        }

        static bool CheckByte(DbcKind kind, uint v, out string error)
        {
            error = null;
            if (kind == DbcKind.Byte && v > 255) { error = "This field is one byte (0–255)."; return false; }
            return true;
        }

        /// <summary>Sets one field from text (the same text FieldText produces).</summary>
        public bool TrySetField(DbcRow r, int field, string text, out string error)
        {
            error = null;
            if (Layout.FieldIsString[field]) { r.S[field] = text ?? ""; return true; }
            var c = Layout.ColumnOf(field);
            var kind = c.Kind == DbcKind.Loc ? DbcKind.Flags : c.Kind;
            if (!TryParse(kind, text, out var v, out error)) return false;
            r.V[field] = v;
            if (field == 0) index = null;
            return true;
        }

        // ---------- writing ----------
        bool StringsUnchanged()
        {
            foreach (var r in Rows)
                for (int i = 0; i < Layout.FieldCount; i++)
                    if (Layout.FieldIsString[i] && (!origStringAt.TryGetValue(r.V[i], out var s) || s == null || s != (r.S[i] ?? "")))
                        return false;
            return true;
        }

        public byte[] Write(DbcStringMode mode = DbcStringMode.Auto)
        {
            var l = Layout;
            var vals = Rows.Select(r => (uint[])r.V.Clone()).ToList();
            byte[] block;
            bool keep = mode != DbcStringMode.Rebuild && StringsUnchanged();
            if (keep) block = origStrings;
            else if (mode == DbcStringMode.Rebuild || (mode == DbcStringMode.Auto && l.StringsCertain))
            {
                var ms = new MemoryStream(); ms.WriteByte(0);
                var at = new Dictionary<string, uint>(StringComparer.Ordinal) { [""] = 0 };
                for (int r = 0; r < Rows.Count; r++)
                    for (int i = 0; i < l.FieldCount; i++)
                        if (l.FieldIsString[i]) vals[r][i] = Intern(ms, at, Rows[r].S[i] ?? "");
                block = ms.ToArray();
            }
            else
            {
                // Append: untouched text keeps its offset, changed or new text goes to the end of the original block.
                var ms = new MemoryStream(); ms.Write(origStrings, 0, origStrings.Length);
                if (ms.Length == 0) ms.WriteByte(0);
                var at = new Dictionary<string, uint>(StringComparer.Ordinal);
                foreach (var kv in origStringAt) if (kv.Value != null && !at.ContainsKey(kv.Value)) at[kv.Value] = kv.Key;
                if (!at.ContainsKey("") && origStrings.Length > 0 && origStrings[0] == 0) at[""] = 0;
                for (int r = 0; r < Rows.Count; r++)
                    for (int i = 0; i < l.FieldCount; i++)
                        if (l.FieldIsString[i])
                        {
                            var s = Rows[r].S[i] ?? "";
                            if (origStringAt.TryGetValue(Rows[r].V[i], out var o) && o == s) continue;
                            vals[r][i] = Intern(ms, at, s);
                        }
                block = ms.ToArray();
            }
            var outp = new MemoryStream();
            var w = new BinaryWriter(outp);
            w.Write(Encoding.ASCII.GetBytes("WDBC"));
            w.Write(Rows.Count);
            w.Write(headerFields);
            w.Write(headerRecordSize);
            w.Write(block.Length);
            foreach (var v in vals)
            {
                for (int i = 0; i < l.FieldCount; i++)
                    if (l.FieldSize[i] == 1) w.Write((byte)v[i]); else w.Write(v[i]);
                for (int pad = l.RecordSize; pad < headerRecordSize; pad++) w.Write((byte)0);
            }
            w.Write(block);
            if (keep && trailer.Length > 0) w.Write(trailer);
            return outp.ToArray();
        }

        static uint Intern(MemoryStream ms, Dictionary<string, uint> at, string s)
        {
            if (at.TryGetValue(s, out var o)) return o;
            o = (uint)ms.Length;
            var b = Encoding.UTF8.GetBytes(s);
            ms.Write(b, 0, b.Length); ms.WriteByte(0);
            at[s] = o;
            return o;
        }

        /// <summary>True when every value (numbers and text) equals the other table's.</summary>
        public bool SameValues(DbcTable o, out string firstDifference)
        {
            firstDifference = null;
            if (o.Rows.Count != Rows.Count) { firstDifference = $"row count {Rows.Count} vs {o.Rows.Count}"; return false; }
            for (int r = 0; r < Rows.Count; r++)
                for (int i = 0; i < Layout.FieldCount; i++)
                {
                    bool same = Layout.FieldIsString[i] ? Rows[r].S[i] == o.Rows[r].S[i] : Rows[r].V[i] == o.Rows[r].V[i];
                    if (!same) { firstDifference = $"row {r} field {i} ({Layout.FieldName(i)})"; return false; }
                }
            return true;
        }
    }
}
