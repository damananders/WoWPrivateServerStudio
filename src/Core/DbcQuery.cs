using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace AndersCraftStudio.Core
{
    /// <summary>
    /// Row filter for the DBC grid. Forms:
    ///   fire            rows whose text (any text column, any locale) contains "fire"
    ///   133             rows with any number field equal to 133
    ///   Name:fire       one column contains (Loc columns: any locale)
    ///   RangeIndex=5    one column equals (number or text)
    ///   Level&gt;60  /  Level&lt;10   numeric comparison
    /// Several terms separated by spaces must all match.
    /// </summary>
    public static class DbcFilter
    {
        public static List<int> Apply(DbcTable t, string query, out string error)
        {
            error = null;
            var terms = Split(query ?? "");
            var preds = new List<Func<DbcRow, bool>>();
            foreach (var term in terms)
            {
                var p = Compile(t, term, out error);
                if (p == null) return null;
                preds.Add(p);
            }
            var res = new List<int>();
            for (int i = 0; i < t.Rows.Count; i++)
                if (preds.All(p => p(t.Rows[i]))) res.Add(i);
            return res;
        }

        static List<string> Split(string q)
        {
            var res = new List<string>(); var sb = new StringBuilder(); bool quote = false;
            foreach (var ch in q)
            {
                if (ch == '"') { quote = !quote; continue; }
                if (char.IsWhiteSpace(ch) && !quote) { if (sb.Length > 0) { res.Add(sb.ToString()); sb.Clear(); } continue; }
                sb.Append(ch);
            }
            if (sb.Length > 0) res.Add(sb.ToString());
            return res;
        }

        static Func<DbcRow, bool> Compile(DbcTable t, string term, out string error)
        {
            error = null;
            var l = t.Layout;
            int op = term.IndexOfAny(new[] { ':', '=', '>', '<' });
            if (op > 0)
            {
                var colName = term.Substring(0, op); char o = term[op]; var val = term.Substring(op + 1);
                var col = l.Columns.FirstOrDefault(c => string.Equals(c.Name, colName, StringComparison.OrdinalIgnoreCase));
                int single = col == null ? l.FieldByName(colName) : -1;
                if (col == null && single < 0) { error = $"No column called \"{colName}\" in {t.File}."; return null; }
                var fields = col != null ? Enumerable.Range(col.Field, col.Kind == DbcKind.Loc ? 16 : 1).ToArray() : new[] { single };
                if (o == ':') return r => fields.Any(f => t.FieldText(r, f).IndexOf(val, StringComparison.OrdinalIgnoreCase) >= 0);
                if (o == '=')
                {
                    var kind = (col ?? l.ColumnOf(single)).Kind;
                    if (!l.FieldIsString[fields[0]] && DbcTable.TryParse(kind == DbcKind.Loc ? DbcKind.Flags : kind, val, out var v, out _)) return r => fields.Any(f => r.V[f] == v);
                    return r => fields.Any(f => string.Equals(t.FieldText(r, f), val, StringComparison.OrdinalIgnoreCase));
                }
                if (!double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var num)) { error = $"\"{val}\" isn't a number."; return null; }
                return r => fields.Any(f => double.TryParse(t.FieldText(r, f).StartsWith("0x") ? Convert.ToUInt32(t.FieldText(r, f), 16).ToString() : t.FieldText(r, f), NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && (o == '>' ? x > num : x < num));
            }
            var strFields = Enumerable.Range(0, l.FieldCount).Where(f => l.FieldIsString[f]).ToArray();
            if (long.TryParse(term, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= int.MinValue && n <= uint.MaxValue)
            {
                uint v = unchecked((uint)n);
                var numFields = Enumerable.Range(0, l.FieldCount).Where(f => !l.FieldIsString[f] && l.ColumnOf(f).Kind != DbcKind.Float).ToArray();
                return r => numFields.Any(f => r.V[f] == v) || strFields.Any(f => (r.S[f] ?? "").Contains(term));
            }
            return r => strFields.Any(f => (r.S[f] ?? "").IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }

    /// <summary>CSV export/import of a whole DBC table, one column per field (localized columns expand to _enUS … _Mask).</summary>
    public static class DbcCsv
    {
        public static string Export(DbcTable t, IEnumerable<int> rows = null)
        {
            var sb = new StringBuilder();
            var l = t.Layout;
            sb.Append(string.Join(",", Enumerable.Range(0, l.FieldCount).Select(f => Quote(l.FieldName(f))))).Append("\r\n");
            foreach (var i in rows ?? Enumerable.Range(0, t.Rows.Count))
                sb.Append(string.Join(",", Enumerable.Range(0, l.FieldCount).Select(f => Quote(t.FieldText(t.Rows[i], f))))).Append("\r\n");
            return sb.ToString();
        }

        static string Quote(string s) => s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 || s.StartsWith(" ") || s.EndsWith(" ") ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

        public static List<List<string>> Parse(string text)
        {
            var rows = new List<List<string>>(); var row = new List<string>(); var sb = new StringBuilder();
            bool q = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (q)
                {
                    if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; } else q = false; }
                    else sb.Append(c);
                }
                else if (c == '"') q = true;
                else if (c == ',') { row.Add(sb.ToString()); sb.Clear(); }
                else if (c == '\r') { }
                else if (c == '\n') { row.Add(sb.ToString()); sb.Clear(); rows.Add(row); row = new List<string>(); }
                else sb.Append(c);
            }
            if (sb.Length > 0 || row.Count > 0) { row.Add(sb.ToString()); rows.Add(row); }
            return rows;
        }

        public sealed class ImportResult { public int Updated, Added, Unchanged; public List<string> Errors = new List<string>(); public List<string> Ignored = new List<string>(); }

        /// <summary>
        /// Applies a CSV to the table in place. Rows are matched by ID (or by position for tables without one); rows that
        /// aren't in the CSV are left alone. Unknown columns are ignored. Nothing changes if any value is invalid.
        /// </summary>
        public static ImportResult Import(DbcTable t, string text)
        {
            var res = new ImportResult();
            var rows = Parse(text.TrimStart('﻿'));
            if (rows.Count == 0) { res.Errors.Add("The file is empty."); return res; }
            var l = t.Layout;
            var map = new int[rows[0].Count];
            for (int c = 0; c < map.Length; c++)
            {
                map[c] = l.FieldByName(rows[0][c].Trim());
                if (map[c] < 0) res.Ignored.Add(rows[0][c]);
            }
            if (l.HasId && !map.Contains(0)) { res.Errors.Add($"The CSV needs the {l.FieldName(0)} column to match rows."); return res; }
            var work = t.Clone();
            for (int r = 1; r < rows.Count; r++)
            {
                var cells = rows[r];
                if (cells.Count == 1 && cells[0].Length == 0) continue;
                DbcRow row; bool isNew = false;
                if (l.HasId)
                {
                    int idCol = Array.IndexOf(map, 0);
                    if (idCol >= cells.Count || !DbcTable.TryParse(l.Columns[0].Kind, cells[idCol], out var id, out var e)) { res.Errors.Add($"Line {r + 1}: the ID isn't a number."); continue; }
                    row = work.Find(id);
                    if (row == null) { row = work.NewRow(); row.V[0] = id; isNew = true; }
                }
                else
                {
                    int idx = r - 1;
                    if (idx < work.Rows.Count) row = work.Rows[idx]; else { row = work.NewRow(); isNew = true; }
                }
                bool changed = false;
                var copy = row.Clone();
                for (int c = 0; c < map.Length && c < cells.Count; c++)
                {
                    if (map[c] < 0) continue;
                    if (!work.TrySetField(copy, map[c], cells[c], out var err)) { res.Errors.Add($"Line {r + 1}, {l.FieldName(map[c])}: {err}"); continue; }
                    if (work.FieldText(copy, map[c]) != work.FieldText(row, map[c]) || isNew) changed = true;
                }
                if (isNew) { work.Rows.Add(copy); work.InvalidateIndex(); res.Added++; }
                else if (changed) { row.V = copy.V; row.S = copy.S; res.Updated++; }
                else res.Unchanged++;
            }
            if (res.Errors.Count > 0) { res.Updated = res.Added = 0; return res; }
            t.Rows.Clear(); t.Rows.AddRange(work.Rows); t.InvalidateIndex();
            return res;
        }
    }
}
