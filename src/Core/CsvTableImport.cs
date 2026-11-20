using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AndersCraftStudio.Core
{
    /// <summary>
    /// CSV → database table import. Maps CSV columns to table columns, compares each CSV row with the row already in the
    /// table (read beforehand with the read-only account) and stages INSERTs for new rows and UPDATEs for changed cells.
    /// Nothing is written here: everything lands in the changeset with its undo.
    /// </summary>
    public static class CsvTableImport
    {
        public enum Mode { AddAndUpdate, AddOnly, UpdateOnly }

        public sealed class Column
        {
            public string Name;
            public Type Type;
            public bool Nullable = true;
            public Column() { }
            public Column(string name, Type type, bool nullable = true) { Name = name; Type = type; Nullable = nullable; }
        }

        public sealed class Result
        {
            public int Added, Updated, Unchanged, Skipped, CellsChanged;
            public List<string> Errors = new List<string>();
            public List<string> Notes = new List<string>();
            public bool Ok => Errors.Count == 0;
        }

        /// <summary>CSV header → table column (case-insensitive exact name match; unknown headers are skipped).</summary>
        public static Dictionary<int, string> AutoMap(IList<string> header, IEnumerable<Column> columns)
        {
            var names = columns.Select(c => c.Name).ToList();
            var map = new Dictionary<int, string>();
            for (int i = 0; i < header.Count; i++)
            {
                var h = (header[i] ?? "").Trim().Trim('`');
                var hit = names.FirstOrDefault(n => string.Equals(n, h, StringComparison.OrdinalIgnoreCase));
                if (hit != null && !map.ContainsValue(hit)) map[i] = hit;
            }
            return map;
        }

        /// <summary>Key literal text used to match CSV rows with database rows: "lit1|lit2…".</summary>
        public static string KeyText(IEnumerable<string> literals) => string.Join("\u001f", literals);

        /// <summary>A read query for the existing rows with these keys (single or composite primary key), or null if none.</summary>
        public static string LookupSql(string table, IList<string> keyCols, IList<IList<string>> keyLiterals)
        {
            if (keyLiterals.Count == 0) return null;
            string where;
            if (keyCols.Count == 1) where = SqlGen.Ident(keyCols[0]) + " IN (" + string.Join(", ", keyLiterals.Select(k => k[0])) + ")";
            else where = string.Join(" OR ", keyLiterals.Select(k => "(" + string.Join(" AND ", keyCols.Select((c, i) => SqlGen.Ident(c) + " = " + k[i])) + ")"));
            return $"SELECT * FROM {SqlGen.Ident(table)} WHERE {where}";
        }

        /// <summary>The key literals of every CSV row whose key cells are valid (used to read the existing rows first).</summary>
        public static List<IList<string>> Keys(IList<Column> columns, IList<string> keyCols, IList<List<string>> rows, IDictionary<int, string> map)
        {
            var byName = columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
            var keyIdx = keyCols.Select(k => map.FirstOrDefault(kv => string.Equals(kv.Value, k, StringComparison.OrdinalIgnoreCase))).ToList();
            var res = new List<IList<string>>();
            if (keyIdx.Any(kv => kv.Value == null)) return res;
            foreach (var row in rows)
            {
                var lits = new List<string>();
                foreach (var (kv, k) in keyIdx.Zip(keyCols))
                {
                    var text = kv.Key < row.Count ? row[kv.Key] : "";
                    if (text.Length == 0 || !SqlGen.TryTypedLiteral(text, byName[k].Type, false, out var lit, out _)) { lits = null; break; }
                    lits.Add(lit);
                }
                if (lits != null) res.Add(lits);
            }
            return res;
        }

        /// <summary>
        /// Stages the import into <paramref name="cs"/>. <paramref name="existing"/> maps <see cref="KeyText"/> of a key to that
        /// row's values (by column name). Empty CSV cells mean "leave as is" (update) or "use the column default" (insert);
        /// write NULL to set NULL.
        /// </summary>
        public static Result Stage(Changeset cs, string table, IList<Column> columns, IList<string> keyCols, IList<List<string>> rows,
                                   IDictionary<int, string> map, IDictionary<string, IDictionary<string, object>> existing, Mode mode, int maxRows = 50000)
        {
            var res = new Result();
            var byName = columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
            if (keyCols == null || keyCols.Count == 0) { res.Errors.Add($"{table} has no primary key, so CSV rows can't be matched to table rows. Use Stage .sql script instead."); return res; }
            foreach (var k in keyCols)
                if (!map.Values.Contains(k, StringComparer.OrdinalIgnoreCase)) res.Errors.Add($"The key column {k} isn't mapped to a CSV column.");
            foreach (var kv in map) if (!byName.ContainsKey(kv.Value)) res.Errors.Add($"{kv.Value} isn't a column of {table}.");
            if (rows.Count > maxRows) res.Errors.Add($"The CSV has {rows.Count:N0} rows; Studio stages at most {maxRows:N0} at a time. Split the file.");
            if (!res.Ok) return res;

            var mapped = map.OrderBy(kv => kv.Key).ToList();
            var seen = new HashSet<string>();
            for (int r = 0; r < rows.Count; r++)
            {
                var row = rows[r];
                int line = r + 2; // header is line 1
                if (row.All(string.IsNullOrWhiteSpace)) { res.Skipped++; continue; }
                var lits = new Dictionary<string, (string lit, string disp)>(StringComparer.OrdinalIgnoreCase);
                bool bad = false;
                foreach (var (ci, col) in mapped)
                {
                    var text = ci < row.Count ? row[ci] : "";
                    if (text.Length == 0) continue;
                    var c = byName[col];
                    if (!SqlGen.TryTypedLiteral(text, c.Type, c.Nullable, out var lit, out var err)) { res.Errors.Add($"Line {line}, {col}: {err}"); bad = true; continue; }
                    lits[col] = (lit, lit == "NULL" ? "NULL" : text);
                }
                if (bad) { if (res.Errors.Count > 50) { res.Errors.Add("… stopped after 50 problems."); return res; } continue; }
                if (keyCols.Any(k => !lits.ContainsKey(k) || lits[k].lit == "NULL")) { res.Errors.Add($"Line {line}: the key ({string.Join(", ", keyCols)}) is empty."); continue; }
                var keyLits = keyCols.Select(k => lits[k].lit).ToList();
                var kt = KeyText(keyLits);
                if (!seen.Add(kt)) { res.Errors.Add($"Line {line}: the same key appears twice in the CSV ({string.Join(", ", keyCols.Select(k => k + "=" + lits[k].disp))})."); continue; }
                var key = keyCols.Select(k => new KeyPart(k, lits[k].lit, lits[k].disp)).ToList();

                if (existing.TryGetValue(kt, out var cur))
                {
                    if (mode == Mode.AddOnly) { res.Skipped++; continue; }
                    int changed = 0;
                    foreach (var (col, v) in lits)
                    {
                        if (keyCols.Contains(col, StringComparer.OrdinalIgnoreCase)) continue;
                        var before = cur.TryGetValue(col, out var bv) ? bv : null;
                        var beforeLit = SqlGen.Literal(before);
                        if (Same(beforeLit, v.lit, byName[col].Type)) continue;
                        ChangeFactory.StageCellEdit(cs, table, key, col, beforeLit, SqlGen.Display(before), v.lit, v.disp);
                        changed++;
                    }
                    if (changed == 0) res.Unchanged++; else { res.Updated++; res.CellsChanged += changed; }
                }
                else
                {
                    if (mode == Mode.UpdateOnly) { res.Skipped++; continue; }
                    if (cs.Changes.Any(c => c.Kind == ChangeKind.Insert && c.SameRow(table, key))) { res.Notes.Add($"Line {line}: a new row with this key is already staged; left as it is."); res.Skipped++; continue; }
                    var cols = lits.Keys.ToList();
                    var ch = ChangeFactory.StageRowInsert(cs, table, key, cols, cols.Select(c => lits[c].lit).ToList());
                    for (int i = 0; i < cols.Count; i++) ch.Fields[i].AfterDisplay = lits[cols[i]].disp;
                    ch.Summary = $"Add row to {table} ({ch.KeyText}) from CSV";
                    res.Added++;
                }
            }
            return res;
        }

        /// <summary>Equal values, also when the number is written differently (1.50 vs 1.5).</summary>
        static bool Same(string a, string b, Type t)
        {
            if (a == b) return true;
            if (a == "NULL" || b == "NULL") return false;
            if (t == typeof(string) || t == typeof(byte[])) return false;
            return decimal.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && decimal.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out var y) && x == y;
        }

        /// <summary>Short human summary of a result.</summary>
        public static string Describe(Result r)
        {
            var sb = new StringBuilder();
            sb.Append($"{r.Added} new row(s), {r.Updated} changed row(s) ({r.CellsChanged} cell(s)), {r.Unchanged} unchanged");
            if (r.Skipped > 0) sb.Append($", {r.Skipped} skipped");
            return sb.ToString();
        }
    }
}
