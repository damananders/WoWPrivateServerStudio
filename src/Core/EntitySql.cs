using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AndersCraftStudio.Core
{
    /// <summary>Bit helpers for flag columns (npcflag, unit_flags, item Flags, quest Flags...). Unknown bits are always preserved.</summary>
    public static class FlagMask
    {
        public static ulong Set(ulong value, ulong bit, bool on) => on ? value | bit : value & ~bit;
        public static bool Has(ulong value, ulong bit) => bit != 0 && (value & bit) == bit;
        /// <summary>Bits that are set but have no name in <paramref name="known"/>.</summary>
        public static ulong Unknown(ulong value, IEnumerable<ulong> known) => value & ~known.Aggregate(0UL, (a, b) => a | b);
        public static string Describe(ulong value, IDictionary<ulong, string> names)
        {
            var parts = names.Where(kv => Has(value, kv.Key)).Select(kv => kv.Value).ToList();
            var rest = Unknown(value, names.Keys);
            if (rest != 0) parts.Add("other bits 0x" + rest.ToString("X"));
            return parts.Count == 0 ? "none" : string.Join(", ", parts);
        }
    }

    /// <summary>
    /// Pending form edits for one or more rows, before they are staged. Each value is kept as a SQL literal.
    /// Preview() shows the exact UPDATE statements Stage() would put into the changeset.
    /// </summary>
    public sealed class EditSession
    {
        public sealed class Edit
        {
            public string Table, Column, Before, BeforeDisplay, After, AfterDisplay;
            public List<KeyPart> Key;
        }

        readonly List<Edit> edits = new List<Edit>();
        public IReadOnlyList<Edit> Edits => edits;
        public int Count => edits.Count;

        static string RowId(string table, IList<KeyPart> key) => table.ToLowerInvariant() + "|" + string.Join("|", key.Select(k => k.Column.ToLowerInvariant() + "=" + k.Value));

        /// <summary>Records (or replaces, or clears when back to the original) one field edit.</summary>
        public void Set(string table, IList<KeyPart> key, string column, string beforeLit, string beforeDisp, string afterLit, string afterDisp)
        {
            var id = RowId(table, key);
            edits.RemoveAll(e => RowId(e.Table, e.Key) == id && string.Equals(e.Column, column, StringComparison.OrdinalIgnoreCase));
            if (beforeLit == afterLit) return;
            edits.Add(new Edit { Table = table, Key = key.ToList(), Column = column, Before = beforeLit, BeforeDisplay = beforeDisp, After = afterLit, AfterDisplay = afterDisp });
        }

        public Edit Get(string table, IList<KeyPart> key, string column)
        {
            var id = RowId(table, key);
            return edits.FirstOrDefault(e => RowId(e.Table, e.Key) == id && string.Equals(e.Column, column, StringComparison.OrdinalIgnoreCase));
        }

        public void Clear() => edits.Clear();

        /// <summary>One UPDATE per edited row, in the order rows were first touched.</summary>
        public List<string> Preview()
        {
            var res = new List<string>();
            foreach (var g in edits.GroupBy(e => RowId(e.Table, e.Key)))
            {
                var first = g.First();
                res.Add(SqlGen.UpdateRow(first.Table, first.Key, g.Select(e => (e.Column, e.After))));
            }
            return res;
        }

        public List<string> PreviewUndo()
        {
            var res = new List<string>();
            foreach (var g in edits.GroupBy(e => RowId(e.Table, e.Key)))
            {
                var first = g.First();
                res.Add(SqlGen.UpdateRow(first.Table, first.Key, g.Select(e => (e.Column, e.Before))));
            }
            return res;
        }

        /// <summary>Moves every edit into the changeset (merging with earlier staged edits of the same rows). Returns the touched changes.</summary>
        public List<Change> Stage(Changeset cs)
        {
            var touched = new List<Change>();
            foreach (var e in edits)
            {
                var c = ChangeFactory.StageCellEdit(cs, e.Table, e.Key, e.Column, e.Before, e.BeforeDisplay, e.After, e.AfterDisplay);
                if (c != null && !touched.Contains(c)) touched.Add(c);
            }
            edits.Clear();
            return touched;
        }

        /// <summary>What <see cref="StageAsNewRow"/> would stage for this row (preview only, nothing changes), or null.</summary>
        public string PreviewNewRow(string table, IList<KeyPart> key, IList<string> columns, IList<string> defaults)
        {
            var id = RowId(table, key);
            var mine = edits.Where(e => RowId(e.Table, e.Key) == id).ToList();
            if (mine.Count == 0) return null;
            var lits = defaults.ToList();
            for (int i = 0; i < columns.Count; i++)
            {
                var kp = key.FirstOrDefault(k => string.Equals(k.Column, columns[i], StringComparison.OrdinalIgnoreCase));
                if (kp != null) lits[i] = kp.Value;
                var e = mine.FirstOrDefault(x => string.Equals(x.Column, columns[i], StringComparison.OrdinalIgnoreCase));
                if (e != null) lits[i] = e.After;
            }
            return SqlGen.InsertRow(table, columns, lits);
        }

        /// <summary>
        /// For a side-table row that doesn't exist yet (e.g. creature_template_addon): turns this row's edits into one staged
        /// INSERT of every column (<paramref name="defaults"/> = SQL literals for the columns nobody edited), or merges them
        /// into the INSERT already staged for the row. Other edits stay in the session. Returns null when there were none.
        /// </summary>
        public Change StageAsNewRow(Changeset cs, string table, IList<KeyPart> key, IList<string> columns, IList<string> defaults, IList<string> defaultDisplays = null)
        {
            var id = RowId(table, key);
            var mine = edits.Where(e => RowId(e.Table, e.Key) == id).ToList();
            if (mine.Count == 0) return null;
            var lits = defaults.ToList();
            var disp = (defaultDisplays ?? defaults).ToList();
            var touchedCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < columns.Count; i++)
            {
                var kp = key.FirstOrDefault(k => string.Equals(k.Column, columns[i], StringComparison.OrdinalIgnoreCase));
                if (kp != null) { lits[i] = kp.Value; disp[i] = kp.Display ?? kp.Value; touchedCols.Add(columns[i]); }
                var e = mine.FirstOrDefault(x => string.Equals(x.Column, columns[i], StringComparison.OrdinalIgnoreCase));
                if (e != null) { lits[i] = e.After; disp[i] = e.AfterDisplay; touchedCols.Add(columns[i]); }
            }
            var c = ChangeFactory.StageInsertOrMerge(cs, table, key, columns, lits, disp, touchedCols);
            edits.RemoveAll(e => RowId(e.Table, e.Key) == id);
            return c;
        }
    }

    /// <summary>Column defaults for rows Studio creates (from INFORMATION_SCHEMA.COLUMNS.COLUMN_DEFAULT).</summary>
    public static class ColumnDefaults
    {
        public static string Query(string table) =>
            "SELECT COLUMN_NAME, COLUMN_DEFAULT, IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = " + SqlGen.Literal(table) + " ORDER BY ORDINAL_POSITION";

        /// <summary>The literal a new row gets for this column when nobody sets it.</summary>
        public static string Literal(object columnDefault, Type type, bool nullable)
        {
            var d = columnDefault is null || columnDefault is DBNull ? null : Convert.ToString(columnDefault, CultureInfo.InvariantCulture);
            if (d != null && d.Equals("NULL", StringComparison.OrdinalIgnoreCase)) d = null;      // MariaDB spells it out
            if (d == null) return nullable ? "NULL" : type == typeof(string) ? "''" : type == typeof(byte[]) ? "''" : "0";
            if (d.Length >= 2 && d[0] == '\'' && d[^1] == '\'') d = d.Substring(1, d.Length - 2).Replace("''", "'");   // MariaDB quotes strings
            return SqlGen.TryTypedLiteral(d, type, nullable, out var lit, out _) ? lit : (type == typeof(string) ? SqlGen.Literal(d) : "0");
        }

        /// <summary>Typed value for a literal produced by <see cref="Literal"/> (for showing it in a form).</summary>
        public static object Value(string literal, Type type)
        {
            if (literal == "NULL") return DBNull.Value;
            if (type == typeof(string)) return literal.Length >= 2 && literal[0] == '\'' ? literal.Substring(1, literal.Length - 2).Replace("\\'", "'").Replace("''", "'").Replace("\\\\", "\\") : literal;
            try { return Convert.ChangeType(literal, type, CultureInfo.InvariantCulture); } catch { return DBNull.Value; }
        }
    }

    /// <summary>"Duplicate as new": copy a row under a fresh ID in the custom range, as a staged INSERT with DELETE undo.</summary>
    public static class Duplicator
    {
        public const long DefaultCustomStart = 900000;

        /// <summary>Read query that returns the first free ID at or above <paramref name="start"/> (the highest used one + 1).</summary>
        public static string FreeIdQuery(string table, string keyColumn, long start)
            => $"SELECT COALESCE(MAX({SqlGen.Ident(keyColumn)}) + 1, {start.ToString(CultureInfo.InvariantCulture)}) FROM {SqlGen.Ident(table)} WHERE {SqlGen.Ident(keyColumn)} >= {start.ToString(CultureInfo.InvariantCulture)}";

        /// <summary>Stages an INSERT of <paramref name="values"/> with the key replaced by <paramref name="newId"/>.
        /// When <paramref name="nameColumn"/> is given, " (copy)" is appended so the copy is easy to spot in game.</summary>
        public static Change Stage(Changeset cs, string table, string keyColumn, long newId, IList<string> columns, IList<object> values, string nameColumn = null)
        {
            int k = IndexOf(columns, keyColumn);
            if (k < 0) throw new ArgumentException("The key column isn't in the row.");
            var lits = values.Select(SqlGen.Literal).ToList();
            lits[k] = newId.ToString(CultureInfo.InvariantCulture);
            int n = nameColumn == null ? -1 : IndexOf(columns, nameColumn);
            string newName = null;
            if (n >= 0 && values[n] is string s) { newName = s + " (copy)"; lits[n] = SqlGen.Literal(newName); }
            var key = new List<KeyPart> { new KeyPart(columns[k], lits[k], lits[k]) };
            var c = ChangeFactory.StageRowInsert(cs, table, key, columns, lits);
            for (int i = 0; i < c.Fields.Count; i++) c.Fields[i].AfterDisplay = i == k ? newId.ToString(CultureInfo.InvariantCulture) : i == n && newName != null ? newName : SqlGen.Display(values[i]);
            c.Summary = $"New {table} #{newId}" + (newName != null ? $" \"{newName}\"" : "") + $" (copy of #{SqlGen.Display(values[k])})";
            return c;
        }

        /// <summary>The ID to use: the database's first free one, unless earlier staged copies already claimed it.</summary>
        public static long NextId(long dbFree, Changeset cs, string table, long start)
        {
            long next = Math.Max(dbFree, start);
            foreach (var c in cs.Changes.Where(c => c.Kind == ChangeKind.Insert && string.Equals(c.Table, table, StringComparison.OrdinalIgnoreCase) && c.Key.Count == 1))
                if (long.TryParse(c.Key[0].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var used) && used >= next) next = used + 1;
            return next;
        }

        static int IndexOf(IList<string> cols, string name)
        {
            for (int i = 0; i < cols.Count; i++) if (string.Equals(cols[i], name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }
    }
}
