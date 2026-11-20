using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AndersCraftStudio.Core
{
    /// <summary>Bulk operations on a DBC table in memory (the result is staged like any other edit).</summary>
    public static class DbcEdit
    {
        /// <summary>Replaces text in text fields of the given rows. Returns how many fields changed.</summary>
        public static int Replace(DbcTable t, IEnumerable<int> rows, IEnumerable<int> fields, string find, string replace, bool matchCase)
        {
            if (string.IsNullOrEmpty(find)) return 0;
            var cmp = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var fl = fields.Where(f => t.Layout.FieldIsString[f]).ToList();
            int n = 0;
            foreach (var i in rows)
                foreach (var f in fl)
                {
                    var s = t.Rows[i].S[f] ?? "";
                    if (s.IndexOf(find, cmp) < 0) continue;
                    t.Rows[i].S[f] = ReplaceAll(s, find, replace ?? "", cmp);
                    n++;
                }
            return n;
        }

        static string ReplaceAll(string s, string find, string rep, StringComparison cmp)
        {
            var sb = new StringBuilder(); int pos = 0;
            while (true) { int i = s.IndexOf(find, pos, cmp); if (i < 0) break; sb.Append(s, pos, i - pos).Append(rep); pos = i + find.Length; }
            return sb.Append(s, pos, s.Length - pos).ToString();
        }

        public enum Op { Set, Add, Multiply }

        /// <summary>Sets / adds to / multiplies one field on the given rows. Returns how many rows changed; error explains a refusal.</summary>
        public static int Bulk(DbcTable t, IEnumerable<int> rows, int field, Op op, string value, out string error)
        {
            error = null;
            var col = t.Layout.ColumnOf(field);
            bool text = t.Layout.FieldIsString[field];
            if (text && op != Op.Set) { error = "Text can only be set, not added to or multiplied."; return 0; }
            if (field == 0 && t.Layout.HasId) { error = "The ID column can't be bulk-edited (IDs must stay unique)."; return 0; }
            double num = 0;
            if (op != Op.Set && !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out num)) { error = "Enter a number (use a dot for decimals)."; return 0; }
            var kind = col.Kind == DbcKind.Loc ? DbcKind.Flags : col.Kind;
            int n = 0;
            foreach (var i in rows)
            {
                var r = t.Rows[i];
                string after;
                if (op == Op.Set) after = value ?? "";
                else
                {
                    double cur = kind == DbcKind.Float ? BitConverter.Int32BitsToSingle(unchecked((int)r.V[field])) : kind == DbcKind.Int ? unchecked((int)r.V[field]) : r.V[field];
                    double res = op == Op.Add ? cur + num : cur * num;
                    after = kind == DbcKind.Float ? ((float)res).ToString("R", CultureInfo.InvariantCulture) : Math.Round(res).ToString(CultureInfo.InvariantCulture);
                }
                var before = t.FieldText(r, field);
                if (!t.TrySetField(r, field, after, out error)) { error = $"Row {i}: {error}"; return n; }
                if (t.FieldText(r, field) != before) n++;
            }
            return n;
        }
    }

    /// <summary>A DBC table as SQL (CREATE TABLE + INSERTs), e.g. to load it into MySQL or compare it with spell_dbc.</summary>
    public static class DbcSql
    {
        public static string SqlType(DbcTable t, int field)
        {
            var c = t.Layout.ColumnOf(field);
            if (t.Layout.FieldIsString[field]) return "TEXT";
            return c.Kind switch { DbcKind.Float => "FLOAT", DbcKind.Int => "INT", DbcKind.Byte => "TINYINT UNSIGNED", _ => "INT UNSIGNED" };
        }

        public static string Export(DbcTable t, IEnumerable<int> rows, string tableName = null, int batch = 200)
        {
            var name = tableName ?? System.IO.Path.GetFileNameWithoutExtension(t.File).ToLowerInvariant() + "_dbc";
            var l = t.Layout;
            var sb = new StringBuilder();
            sb.Append("-- ").Append(t.File).Append(" exported by AndersCraft Studio, ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append("\n");
            sb.Append("DROP TABLE IF EXISTS ").Append(SqlGen.Ident(name)).Append(";\n");
            sb.Append("CREATE TABLE ").Append(SqlGen.Ident(name)).Append(" (\n");
            sb.Append(string.Join(",\n", Enumerable.Range(0, l.FieldCount).Select(f => "  " + SqlGen.Ident(l.FieldName(f)) + " " + SqlType(t, f) + (l.FieldIsString[f] ? "" : " NOT NULL DEFAULT 0"))));
            if (l.HasId) sb.Append(",\n  PRIMARY KEY (").Append(SqlGen.Ident(l.FieldName(0))).Append(")");
            sb.Append("\n) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;\n\n");
            var cols = "(" + string.Join(", ", Enumerable.Range(0, l.FieldCount).Select(f => SqlGen.Ident(l.FieldName(f)))) + ")";
            var list = rows.ToList();
            for (int i = 0; i < list.Count; i += batch)
            {
                sb.Append("INSERT INTO ").Append(SqlGen.Ident(name)).Append(' ').Append(cols).Append(" VALUES\n");
                sb.Append(string.Join(",\n", list.Skip(i).Take(batch).Select(ri => "(" + string.Join(", ", Enumerable.Range(0, l.FieldCount).Select(f => Literal(t, t.Rows[ri], f))) + ")")));
                sb.Append(";\n");
            }
            return sb.ToString();
        }

        static string Literal(DbcTable t, DbcRow r, int f)
        {
            if (t.Layout.FieldIsString[f]) return SqlGen.Literal(r.S[f] ?? "");
            var kind = t.Layout.ColumnOf(f).Kind;
            return kind switch
            {
                DbcKind.Float => SqlGen.Literal(BitConverter.Int32BitsToSingle(unchecked((int)r.V[f]))),
                DbcKind.Int => unchecked((int)r.V[f]).ToString(CultureInfo.InvariantCulture),
                _ => r.V[f].ToString(CultureInfo.InvariantCulture),
            };
        }

        /// <summary>Field-level differences between two copies of a table (rows matched by ID, or by position).</summary>
        public static List<(uint key, string field, string here, string there)> Compare(DbcTable a, DbcTable b, int max = 2000)
        {
            var res = new List<(uint, string, string, string)>();
            foreach (var r in a.Rows)
            {
                var k = a.KeyOf(r);
                var o = b.Layout.HasId ? b.Find(k) : k < b.Rows.Count ? b.Rows[(int)k] : null;
                if (o == null) { res.Add((k, "(row)", "present", "missing")); }
                else
                    for (int f = 0; f < a.Layout.FieldCount && f < b.Layout.FieldCount; f++)
                    {
                        var x = a.FieldText(r, f); var y = b.FieldText(o, f);
                        if (x != y) res.Add((k, a.Layout.FieldName(f), x, y));
                    }
                if (res.Count >= max) return res;
            }
            foreach (var o in b.Rows) if (a.IndexOf(b.KeyOf(o)) < 0) { res.Add((b.KeyOf(o), "(row)", "missing", "present")); if (res.Count >= max) break; }
            return res;
        }
    }
}
