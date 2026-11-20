using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AndersCraftStudio.Core
{
    /// <summary>
    /// A .sql file turned into a staged changeset: write statements are staged (never run), harmless session statements
    /// that dump tools add (SET NAMES, LOCK/UNLOCK TABLES, transactions, version comments) are skipped, and anything
    /// else Studio refuses (GRANT, CALL, LOAD DATA…) stops the whole file.
    /// </summary>
    public static class SqlScript
    {
        public sealed class Plan
        {
            public List<StatementInfo> Writes = new List<StatementInfo>();
            public List<string> Skipped = new List<string>();
            public List<StatementInfo> Blocked = new List<StatementInfo>();
            public int Reads;
            public int Structure => Writes.Count(w => w.Kind == StatementKind.StructureWrite);
            public bool Ok => Blocked.Count == 0 && Writes.Count > 0;
        }

        static readonly Regex Harmless = new Regex(@"^\s*(/\*!\d+\s+)?(SET\s+(NAMES|CHARACTER_SET_CLIENT|@OLD_|@SAVED_|@@|SQL_MODE|FOREIGN_KEY_CHECKS|UNIQUE_CHECKS|TIME_ZONE|SQL_NOTES|character_set_client|@)|LOCK\s+TABLES|UNLOCK\s+TABLES|START\s+TRANSACTION|BEGIN\s*$|COMMIT|ROLLBACK|USE\s+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Strips a leading "/*!40101 … */" wrapper so the classifier sees the statement inside.</summary>
        static string Unwrap(string s)
        {
            var m = Regex.Match(s.Trim(), @"^/\*!\d+\s+(.*?)\s*\*/\s*$", RegexOptions.Singleline);
            return m.Success ? m.Groups[1].Value : s;
        }

        public static Plan Prepare(string text)
        {
            var plan = new Plan();
            foreach (var (raw, _) in SqlLexer.Split(text ?? ""))
            {
                var s = raw.Trim();
                if (s.Length == 0) continue;
                var inner = Unwrap(s);
                if (Harmless.IsMatch(inner) || Harmless.IsMatch(s)) { plan.Skipped.Add(Short(s)); continue; }
                var st = SqlClassifier.ClassifyOne(inner);
                if (st.Kind == StatementKind.Blocked) plan.Blocked.Add(st);
                else if (st.IsWrite) plan.Writes.Add(st);
                else plan.Reads++;
            }
            return plan;
        }

        static string Short(string s) { s = s.Replace("\r", " ").Replace("\n", " "); return s.Length > 70 ? s.Substring(0, 70) + "…" : s; }
    }

    /// <summary>Builds a .sql dump (structure + data) from what the read-only account can see.</summary>
    public static class SqlDump
    {
        public static void WriteHeader(TextWriter w, string database)
        {
            w.WriteLine($"-- AndersCraft Studio SQL export of {database}, {DateTime.Now:yyyy-MM-dd HH:mm}");
            w.WriteLine("-- Made with a read-only account. Import it with any MySQL client.");
            w.WriteLine("SET NAMES utf8mb4;");
            w.WriteLine();
        }

        /// <summary>DROP + CREATE (when <paramref name="createSql"/> is given) and INSERTs in batches of <paramref name="batch"/> rows.</summary>
        public static void WriteTable(TextWriter w, string table, string createSql, IList<string> columns, IEnumerable<object[]> rows, int batch = 100)
        {
            w.WriteLine($"-- Table {table}");
            if (!string.IsNullOrEmpty(createSql))
            {
                w.WriteLine($"DROP TABLE IF EXISTS {SqlGen.Ident(table)};");
                w.WriteLine(createSql.TrimEnd(';') + ";");
            }
            var cols = "(" + string.Join(", ", columns.Select(SqlGen.Ident)) + ")";
            var pending = new List<string>();
            void Flush()
            {
                if (pending.Count == 0) return;
                w.WriteLine($"INSERT INTO {SqlGen.Ident(table)} {cols} VALUES");
                w.WriteLine(string.Join(",\n", pending) + ";");
                pending.Clear();
            }
            foreach (var r in rows)
            {
                pending.Add("(" + string.Join(", ", r.Select(SqlGen.Literal)) + ")");
                if (pending.Count >= batch) Flush();
            }
            Flush();
            w.WriteLine();
        }

        /// <summary>WHERE condition that finds text in any column (CAST so numbers match too).</summary>
        public static string FindInAllColumns(IEnumerable<string> columns, string text)
        {
            var like = SqlGen.Literal("%" + (text ?? "").Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%");
            return string.Join(" OR ", columns.Select(c => $"CAST({SqlGen.Ident(c)} AS CHAR) LIKE {like}"));
        }
    }

    /// <summary>The last queries run in the SQL browser (per Windows user), newest first.</summary>
    public sealed class QueryHistory
    {
        readonly string path;
        public List<(DateTime utc, string db, string sql)> Items { get; } = new List<(DateTime, string, string)>();
        public const int Max = 100;

        sealed class Entry { public DateTime Utc { get; set; } public string Db { get; set; } public string Sql { get; set; } }

        public QueryHistory(string path)
        {
            this.path = path;
            try
            {
                if (File.Exists(path))
                    foreach (var e in JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(path)) ?? new List<Entry>())
                        Items.Add((e.Utc, e.Db, e.Sql));
            }
            catch { }
        }

        public void Add(string db, string sql)
        {
            sql = (sql ?? "").Trim();
            if (sql.Length == 0) return;
            Items.RemoveAll(x => x.sql == sql && x.db == db);
            Items.Insert(0, (DateTime.UtcNow, db, sql));
            while (Items.Count > Max) Items.RemoveAt(Items.Count - 1);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonSerializer.Serialize(Items.Select(x => new Entry { Utc = x.utc, Db = x.db, Sql = x.sql }).ToList()));
            }
            catch { }
        }
    }
}
