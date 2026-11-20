using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AndersCraftStudio.Core
{
    /// <summary>The shape of a hand-written row statement, as far as Studio needs it to build undo SQL.</summary>
    public sealed class RawPlan
    {
        public string Statement { get; set; }
        public string Verb { get; set; }
        public string Database { get; set; }
        public string Table { get; set; }
        public string Where { get; set; }
        public string OrderBy { get; set; }
        public string Limit { get; set; }
        public List<string> SetColumns { get; set; } = new List<string>();
        public List<string> SetExprs { get; set; } = new List<string>();
        public List<string> InsertColumns { get; set; } = new List<string>();
        public List<List<string>> InsertRows { get; set; } = new List<List<string>>();
        /// <summary>Studio understood the statement well enough to build undo SQL automatically.</summary>
        public bool UndoSupported { get; set; }
        public string UndoProblem { get; set; }
        /// <summary>Every "x." qualifier in the statement (could be a database or a table alias).</summary>
        public List<string> Qualifiers { get; set; } = new List<string>();
    }

    public static class RawStatementParser
    {
        static readonly HashSet<string> Clause = new HashSet<string> { "WHERE", "ORDER", "LIMIT" };

        public static RawPlan Parse(StatementInfo st)
        {
            var p = new RawPlan { Statement = st.Text.Trim(), Verb = st.Verb };
            var t = st.Tokens;
            for (int k = 0; k + 2 < t.Count; k++)
                if ((t[k].Kind == TokKind.Word || t[k].Kind == TokKind.QuotedIdent) && t[k + 1].IsSym(".") && !p.Qualifiers.Contains(t[k].Value, StringComparer.OrdinalIgnoreCase))
                    p.Qualifiers.Add(t[k].Kind == TokKind.Word ? t[k].Text : t[k].Value);

            if (st.Kind != StatementKind.RowWrite) return Fail(p, "Only INSERT, UPDATE and DELETE get automatic undo.");
            if (t.Count > 0 && t[0].Is("WITH")) return Fail(p, "Statements that start with WITH don't get automatic undo.");
            try
            {
                switch (st.Verb)
                {
                    case "UPDATE": return ParseUpdate(p, st);
                    case "DELETE": return ParseDelete(p, st);
                    case "INSERT": return ParseInsert(p, st);
                    case "REPLACE": return Fail(p, "REPLACE may silently delete and re-create rows, so Studio can't build undo for it. Use INSERT or UPDATE instead, or rely on the backup.");
                }
            }
            catch (Exception ex) { return Fail(p, "Couldn't read the statement: " + ex.Message); }
            return Fail(p, "Unsupported statement.");
        }

        static RawPlan Fail(RawPlan p, string why) { p.UndoSupported = false; p.UndoProblem = why; return p; }

        static string Slice(StatementInfo st, int fromTok, int toTokExclusive)
        {
            if (fromTok >= toTokExclusive) return "";
            int a = st.Tokens[fromTok].Start - st.Tokens[0].Start;
            int b = st.Tokens[toTokExclusive - 1].End - st.Tokens[0].Start;
            return st.Text.Substring(a, b - a).Trim();
        }

        /// <summary>Reads [db.]table at i. Returns the index after it.</summary>
        static int ReadTable(StatementInfo st, int i, RawPlan p)
        {
            var t = st.Tokens;
            if (i >= t.Count || (t[i].Kind != TokKind.Word && t[i].Kind != TokKind.QuotedIdent)) throw new FormatException("expected a table name");
            string a = t[i].Kind == TokKind.Word ? t[i].Text : t[i].Value;
            if (i + 2 < t.Count && t[i + 1].IsSym(".") && (t[i + 2].Kind == TokKind.Word || t[i + 2].Kind == TokKind.QuotedIdent))
            {
                p.Database = a;
                p.Table = t[i + 2].Kind == TokKind.Word ? t[i + 2].Text : t[i + 2].Value;
                return i + 3;
            }
            p.Table = a;
            return i + 1;
        }

        static int FindTop(List<Tok> t, int from, Func<Tok, bool> pred)
        {
            for (int k = from; k < t.Count; k++) if (t[k].Depth == 0 && pred(t[k])) return k;
            return -1;
        }

        static void ReadTail(StatementInfo st, int i, RawPlan p)
        {
            var t = st.Tokens;
            int w = FindTop(t, i, x => x.Is("WHERE"));
            int o = FindTop(t, i, x => x.Is("ORDER"));
            int l = FindTop(t, i, x => x.Is("LIMIT"));
            int end = t.Count;
            if (w >= 0) p.Where = Slice(st, w + 1, new[] { o, l, end }.Where(x => x > w).Min());
            if (o >= 0) p.OrderBy = Slice(st, o + 2, new[] { l, end }.Where(x => x > o).Min()); // skip ORDER BY
            if (l >= 0) p.Limit = Slice(st, l + 1, end);
        }

        static RawPlan ParseUpdate(RawPlan p, StatementInfo st)
        {
            var t = st.Tokens;
            int i = 1;
            while (i < t.Count && (t[i].Is("LOW_PRIORITY") || t[i].Is("IGNORE"))) i++;
            if (t.Take(i).Any(x => x.Is("IGNORE"))) { ReadTable(st, i, p); return Fail(p, "UPDATE IGNORE may skip rows unpredictably; Studio can't build undo for it."); }
            i = ReadTable(st, i, p);
            if (i >= t.Count || !t[i].Is("SET")) return Fail(p, "Updates that join several tables or use an alias don't get automatic undo.");
            int setStart = i + 1;
            int tail = FindTop(t, setStart, x => x.Kind == TokKind.Word && Clause.Contains(x.Value));
            int setEnd = tail < 0 ? t.Count : tail;
            // split assignments on top-level commas
            int s = setStart;
            for (int k = setStart; k <= setEnd; k++)
            {
                if (k == setEnd || (t[k].Depth == 0 && t[k].IsSym(",")))
                {
                    int eq = -1;
                    for (int q = s; q < k; q++) if (t[q].Depth == 0 && t[q].IsSym("=")) { eq = q; break; }
                    if (eq < 0) return Fail(p, "Couldn't read the SET list.");
                    var colToks = t.Skip(s).Take(eq - s).ToList();
                    var col = colToks.LastOrDefault(x => x.Kind == TokKind.Word || x.Kind == TokKind.QuotedIdent);
                    if (col == null) return Fail(p, "Couldn't read the SET list.");
                    if (colToks.Count > 1 && !(colToks.Count == 3 && colToks[1].IsSym(".") && string.Equals(colToks[0].Kind == TokKind.Word ? colToks[0].Text : colToks[0].Value, p.Table, StringComparison.OrdinalIgnoreCase)))
                        return Fail(p, "Couldn't read the SET list.");
                    p.SetColumns.Add(col.Kind == TokKind.Word ? col.Text : col.Value);
                    p.SetExprs.Add(Slice(st, eq + 1, k));
                    s = k + 1;
                }
            }
            ReadTail(st, setEnd, p);
            p.UndoSupported = true;
            return p;
        }

        static RawPlan ParseDelete(RawPlan p, StatementInfo st)
        {
            var t = st.Tokens;
            int i = 1;
            while (i < t.Count && (t[i].Is("LOW_PRIORITY") || t[i].Is("QUICK") || t[i].Is("IGNORE"))) i++;
            if (i >= t.Count || !t[i].Is("FROM")) return Fail(p, "Deletes from several tables at once don't get automatic undo.");
            i = ReadTable(st, i + 1, p);
            if (FindTop(t, i, x => x.Is("USING") || x.Is("JOIN")) >= 0 || (i < t.Count && t[i].IsSym(",")))
                return Fail(p, "Deletes that join several tables don't get automatic undo.");
            if (i < t.Count && !(t[i].Kind == TokKind.Word && Clause.Contains(t[i].Value)))
                return Fail(p, "Deletes that use a table alias or partitions don't get automatic undo.");
            ReadTail(st, i, p);
            p.UndoSupported = true;
            return p;
        }

        static RawPlan ParseInsert(RawPlan p, StatementInfo st)
        {
            var t = st.Tokens;
            int i = 1;
            bool ignore = false;
            while (i < t.Count && (t[i].Is("LOW_PRIORITY") || t[i].Is("DELAYED") || t[i].Is("HIGH_PRIORITY") || t[i].Is("IGNORE"))) { if (t[i].Is("IGNORE")) ignore = true; i++; }
            if (i < t.Count && t[i].Is("INTO")) i++;
            i = ReadTable(st, i, p);
            if (ignore) return Fail(p, "INSERT IGNORE may skip rows silently; Studio can't build undo for it.");
            if (FindTop(t, i, x => x.Is("DUPLICATE")) >= 0) return Fail(p, "INSERT ... ON DUPLICATE KEY UPDATE can change existing rows; Studio can't build undo for it.");
            if (i < t.Count && t[i].IsSym("("))
            {
                int close = i + 1;
                while (close < t.Count && !(t[close].IsSym(")") && t[close].Depth == 0)) close++;
                for (int k = i + 1; k < close; k++)
                    if (t[k].Kind == TokKind.Word || t[k].Kind == TokKind.QuotedIdent) p.InsertColumns.Add(t[k].Kind == TokKind.Word ? t[k].Text : t[k].Value);
                i = close + 1;
            }
            if (i < t.Count && t[i].Is("SET"))
            {
                // INSERT ... SET a = 1, b = 2
                int s = i + 1; var row = new List<string>();
                for (int k = s; k <= t.Count; k++)
                {
                    if (k == t.Count || (t[k].Depth == 0 && t[k].IsSym(",")))
                    {
                        int eq = -1;
                        for (int q = s; q < k; q++) if (t[q].IsSym("=") && t[q].Depth == 0) { eq = q; break; }
                        if (eq < 0) return Fail(p, "Couldn't read the SET list.");
                        var col = t[eq - 1];
                        p.InsertColumns.Add(col.Kind == TokKind.Word ? col.Text : col.Value);
                        row.Add(Slice(st, eq + 1, k));
                        s = k + 1;
                    }
                }
                p.InsertRows.Add(row);
                p.UndoSupported = true;
                return p;
            }
            if (i >= t.Count || !(t[i].Is("VALUES") || t[i].Is("VALUE"))) return Fail(p, "INSERT ... SELECT doesn't get automatic undo.");
            if (p.InsertColumns.Count == 0) return Fail(p, "List the column names in the INSERT so Studio knows which row to remove on undo.");
            i++;
            while (i < t.Count)
            {
                if (t[i].Is("ROW")) i++;
                if (i >= t.Count || !t[i].IsSym("(")) break;
                int open = i, depth0 = t[i].Depth;
                var row = new List<string>();
                int s = open + 1, k = open + 1;
                for (; k < t.Count; k++)
                {
                    if (t[k].Depth == depth0 && t[k].IsSym(")")) { row.Add(Slice(st, s, k)); break; }
                    if (t[k].Depth == depth0 + 1 && t[k].IsSym(",")) { row.Add(Slice(st, s, k)); s = k + 1; }
                }
                if (row.Count != p.InsertColumns.Count) return Fail(p, "The number of values doesn't match the number of columns.");
                p.InsertRows.Add(row);
                i = k + 1;
                if (i < t.Count && t[i].IsSym(",")) { i++; continue; }
                break;
            }
            if (i < t.Count && t[i].Is("AS")) return Fail(p, "INSERT ... AS alias isn't supported for automatic undo.");
            if (p.InsertRows.Count == 0) return Fail(p, "Couldn't read the VALUES list.");
            p.UndoSupported = true;
            return p;
        }

        /// <summary>True when the text is one plain literal (number, string, NULL, hex) — safe to reuse in a WHERE.</summary>
        public static bool IsSimpleLiteral(string expr)
        {
            var toks = SqlLexer.Lex(expr);
            if (toks.Count == 1) return toks[0].Kind == TokKind.Number || toks[0].Kind == TokKind.String || toks[0].Is("NULL") || toks[0].Is("TRUE") || toks[0].Is("FALSE");
            if (toks.Count == 2) return (toks[0].IsSym("-") || toks[0].IsSym("+")) && toks[1].Kind == TokKind.Number;
            return false;
        }
    }

    /// <summary>Captures the rows a raw statement will touch and writes SQL that puts them back.</summary>
    public static class RawUndoBuilder
    {
        public const int MaxRows = 5000;

        public sealed class Result
        {
            public bool Available;
            public string UndoSql;
            public string Note;
            public int RowsCaptured;
            public List<FieldChange> Fields = new List<FieldChange>();
            public List<KeyPart> FirstKey = new List<KeyPart>();
        }

        public static string SnapshotQuery(RawPlan p, bool forUpdate)
        {
            var sb = new StringBuilder("SELECT * FROM ").Append(SqlGen.Ident(p.Table));
            if (!string.IsNullOrWhiteSpace(p.Where)) sb.Append(" WHERE ").Append(p.Where);
            if (!string.IsNullOrWhiteSpace(p.OrderBy)) sb.Append(" ORDER BY ").Append(p.OrderBy);
            if (!string.IsNullOrWhiteSpace(p.Limit)) sb.Append(" LIMIT ").Append(p.Limit);
            else sb.Append(" LIMIT ").Append(MaxRows + 1);
            if (forUpdate) sb.Append(" FOR UPDATE");
            return sb.ToString();
        }

        public static Result Build(RawPlan p, ISqlReader db, bool forUpdate = false)
        {
            var r = new Result();
            if (!p.UndoSupported) { r.Note = p.UndoProblem; return r; }
            var pk = db.PrimaryKey(p.Table);
            if (pk == null || pk.Count == 0) { r.Note = $"Table {p.Table} has no primary key, so Studio can't pinpoint rows to restore."; return r; }

            if (p.Verb == "INSERT")
            {
                var idx = pk.Select(c => p.InsertColumns.FindIndex(x => string.Equals(x, c, StringComparison.OrdinalIgnoreCase))).ToList();
                if (idx.Any(x => x < 0)) { r.Note = "The INSERT doesn't list every primary key column (" + string.Join(", ", pk) + "), so Studio can't tell which row to remove on undo."; return r; }
                var sb = new StringBuilder();
                foreach (var row in p.InsertRows)
                {
                    var key = pk.Select((c, n) => new KeyPart(c, row[idx[n]])).ToList();
                    if (key.Any(k => !RawStatementParser.IsSimpleLiteral(k.Value))) { r.Note = "Primary key values in the INSERT must be plain values (not expressions) for automatic undo."; return r; }
                    sb.AppendLine(SqlGen.DeleteRow(p.Table, key));
                    if (r.FirstKey.Count == 0) r.FirstKey = key;
                }
                for (int c = 0; c < p.InsertColumns.Count && p.InsertRows.Count == 1; c++)
                    r.Fields.Add(new FieldChange { Name = p.InsertColumns[c], Before = null, BeforeDisplay = "(no row)", After = p.InsertRows[0][c], AfterDisplay = p.InsertRows[0][c] });
                r.RowsCaptured = p.InsertRows.Count;
                r.UndoSql = sb.ToString().TrimEnd();
                r.Available = true;
                return r;
            }

            if (p.Verb == "UPDATE" && p.SetColumns.Any(c => pk.Contains(c, StringComparer.OrdinalIgnoreCase)))
            { r.Note = "The UPDATE changes a primary key column, so Studio can't find the rows again for undo."; return r; }

            var snap = db.Query(SnapshotQuery(p, forUpdate), MaxRows + 1);
            if (snap.Rows.Count > MaxRows) { r.Note = $"The statement touches more than {MaxRows} rows, which is too many to capture for undo. The backup taken before Apply is your way back."; return r; }
            var pkIdx = pk.Select(c => snap.ColumnIndex(c)).ToList();
            if (pkIdx.Any(x => x < 0)) { r.Note = "Couldn't read the primary key columns."; return r; }
            r.RowsCaptured = snap.Rows.Count;
            var undo = new StringBuilder();

            if (p.Verb == "UPDATE")
            {
                var setIdx = p.SetColumns.Select(c => snap.ColumnIndex(c)).ToList();
                if (setIdx.Any(x => x < 0)) { r.Note = "A column in the SET list doesn't exist in " + p.Table + "."; return r; }
                foreach (var row in snap.Rows)
                {
                    var key = pk.Select((c, n) => new KeyPart(c, SqlGen.Literal(row[pkIdx[n]]), SqlGen.Display(row[pkIdx[n]]))).ToList();
                    undo.AppendLine(SqlGen.UpdateRow(p.Table, key, p.SetColumns.Select((c, n) => (snap.Columns[setIdx[n]], SqlGen.Literal(row[setIdx[n]])))));
                    if (r.FirstKey.Count == 0) r.FirstKey = key;
                    if (snap.Rows.Count == 1)
                        for (int n = 0; n < setIdx.Count; n++)
                            r.Fields.Add(new FieldChange { Name = snap.Columns[setIdx[n]], Before = SqlGen.Literal(row[setIdx[n]]), BeforeDisplay = SqlGen.Display(row[setIdx[n]]), After = p.SetExprs[n], AfterDisplay = p.SetExprs[n] });
                }
            }
            else if (p.Verb == "DELETE")
            {
                foreach (var row in snap.Rows)
                {
                    undo.AppendLine(SqlGen.InsertRow(p.Table, snap.Columns, row.Select(SqlGen.Literal).ToList()));
                    if (r.FirstKey.Count == 0) r.FirstKey = pk.Select((c, n) => new KeyPart(c, SqlGen.Literal(row[pkIdx[n]]), SqlGen.Display(row[pkIdx[n]]))).ToList();
                    if (snap.Rows.Count == 1)
                        for (int n = 0; n < snap.Columns.Count; n++)
                            r.Fields.Add(new FieldChange { Name = snap.Columns[n], Before = SqlGen.Literal(row[n]), BeforeDisplay = SqlGen.Display(row[n]), After = null, AfterDisplay = "(removed)" });
                }
            }
            if (snap.Rows.Count == 0) { r.UndoSql = "-- The statement matched no rows when it was staged, so there is nothing to undo."; r.Available = true; return r; }
            r.UndoSql = undo.ToString().TrimEnd();
            r.Available = true;
            return r;
        }
    }
}
