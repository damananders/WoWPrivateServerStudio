using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using MySqlConnector;

namespace AndersCraftStudio.Core
{
    public static class ConnStrings
    {
        public static string Build(string host, int port, string user, string pass, string db, int timeoutSec = 8)
        {
            var b = new MySqlConnectionStringBuilder
            {
                Server = host, Port = (uint)port, UserID = user, Password = pass, Database = db ?? "",
                ConnectionTimeout = (uint)timeoutSec, DefaultCommandTimeout = 60, TreatTinyAsBoolean = false,
                AllowZeroDateTime = true, ConvertZeroDateTime = false, UseAffectedRows = false, Pooling = false,
                CharacterSet = "utf8mb4", AllowUserVariables = false, AllowLoadLocalInfile = false,
            };
            return b.ConnectionString;
        }
    }

    /// <summary>
    /// Read-only connection (studio_ro). Every session is switched to READ ONLY before any query runs, and every
    /// statement is re-checked by <see cref="SqlClassifier"/> — so even a mis-configured account can't write through it.
    /// </summary>
    public sealed class ReadOnlyDb : ISqlReader, IDisposable
    {
        readonly MySqlConnection conn;
        public string Database { get; }

        public ReadOnlyDb(string host, int port, string user, string pass, string database)
        {
            Database = database;
            conn = new MySqlConnection(ConnStrings.Build(host, port, user, pass, database));
            conn.Open();
            using (var c = new MySqlCommand("SET SESSION TRANSACTION READ ONLY", conn)) c.ExecuteNonQuery();
        }

        public ResultTable Query(string sql, int maxRows)
        {
            var st = SqlClassifier.ClassifyOne(sql);
            if (st.Kind != StatementKind.Read) throw new InvalidOperationException("Blocked: the read-only browser only runs read statements. " + st.Reason);
            var t = new ResultTable();
            using var cmd = new MySqlCommand(sql, conn);
            using var r = cmd.ExecuteReader();
            for (int i = 0; i < r.FieldCount; i++) t.Columns.Add(r.GetName(i));
            while (r.Read())
            {
                if (t.Rows.Count >= maxRows) { t.Truncated = true; break; }
                var row = new object[r.FieldCount];
                r.GetValues(row);
                t.Rows.Add(row);
            }
            return t;
        }

        /// <summary>Streams a read query into a DataTable (column types preserved for typed editing).</summary>
        public DataTable QueryTable(string sql, int maxRows, out bool truncated)
        {
            var st = SqlClassifier.ClassifyOne(sql);
            if (st.Kind != StatementKind.Read) throw new InvalidOperationException("Blocked: " + st.Reason);
            truncated = false;
            var dt = new DataTable();
            using var cmd = new MySqlCommand(sql, conn);
            using var r = cmd.ExecuteReader();
            var schema = r.GetColumnSchema();
            for (int i = 0; i < r.FieldCount; i++)
            {
                var name = r.GetName(i);
                while (dt.Columns.Contains(name)) name += "_";
                var col = dt.Columns.Add(name, r.GetFieldType(i) ?? typeof(object));
                col.AllowDBNull = true;
                col.ExtendedProperties["nullable"] = schema[i].AllowDBNull != false;
                col.ExtendedProperties["dbtype"] = r.GetDataTypeName(i);
            }
            dt.BeginLoadData();
            var buf = new object[r.FieldCount];
            while (r.Read())
            {
                if (dt.Rows.Count >= maxRows) { truncated = true; break; }
                r.GetValues(buf);
                for (int i = 0; i < buf.Length; i++) if (buf[i] is MySqlDateTime m) buf[i] = m.IsValidDateTime ? m.GetDateTime() : (object)DBNull.Value;
                dt.LoadDataRow(buf, true);
            }
            dt.EndLoadData();
            return dt;
        }

        public void StreamCsv(string sql, TextWriter w, Action<int> progress = null)
        {
            var st = SqlClassifier.ClassifyOne(sql);
            if (st.Kind != StatementKind.Read) throw new InvalidOperationException("Blocked: " + st.Reason);
            using var cmd = new MySqlCommand(sql, conn) { CommandTimeout = 600 };
            using var r = cmd.ExecuteReader();
            w.WriteLine(string.Join(",", Enumerable.Range(0, r.FieldCount).Select(i => Csv.Escape(r.GetName(i)))));
            var buf = new object[r.FieldCount];
            int n = 0;
            while (r.Read())
            {
                r.GetValues(buf);
                w.WriteLine(string.Join(",", buf.Select(v => Csv.Escape(v is DBNull ? "" : SqlGen.Display(v)))));
                if (++n % 5000 == 0) progress?.Invoke(n);
            }
        }

        public IList<string> PrimaryKey(string table)
        {
            var res = new List<string>();
            using var cmd = new MySqlCommand("SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t AND CONSTRAINT_NAME = 'PRIMARY' ORDER BY ORDINAL_POSITION", conn);
            cmd.Parameters.AddWithValue("@t", table);
            using var r = cmd.ExecuteReader();
            while (r.Read()) res.Add(r.GetString(0));
            return res;
        }

        public List<(string name, long rows, string comment)> Tables()
        {
            var res = new List<(string, long, string)>();
            using var cmd = new MySqlCommand("SELECT TABLE_NAME, COALESCE(TABLE_ROWS,0), COALESCE(TABLE_COMMENT,'') FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE IN ('BASE TABLE','VIEW') ORDER BY TABLE_NAME", conn);
            using var r = cmd.ExecuteReader();
            while (r.Read()) res.Add((r.GetString(0), Convert.ToInt64(r.GetValue(1)), r.GetString(2)));
            return res;
        }

        public long Count(string table, string where)
        {
            var sql = "SELECT COUNT(*) FROM " + SqlGen.Ident(table) + (string.IsNullOrWhiteSpace(where) ? "" : " WHERE " + where);
            var t = Query(sql, 1);
            return Convert.ToInt64(t.Rows[0][0]);
        }

        public string Scalar(string sql) { var t = Query(sql, 1); return t.Rows.Count == 0 ? null : SqlGen.Display(t.Rows[0][0]); }

        public List<string> Grants()
        {
            var t = Query("SHOW GRANTS", 200);
            return t.Rows.Select(r => Convert.ToString(r[0])).ToList();
        }

        public List<string> Databases() => Query("SHOW DATABASES", 1000).Rows.Select(r => Convert.ToString(r[0])).ToList();

        public void Dispose() { try { conn.Dispose(); } catch { } }
    }

    /// <summary>The write connection (studio_rw). Only <see cref="ApplyEngine"/> uses it, after a backup and a typed confirmation.</summary>
    public sealed class WriteDb : ISqlExecutor, IDisposable
    {
        readonly MySqlConnection conn;
        MySqlTransaction tx;
        public WriteDb(string host, int port, string user, string pass, string database)
        {
            if (string.IsNullOrEmpty(database)) throw new ArgumentException("A target database is required.");
            conn = new MySqlConnection(ConnStrings.Build(host, port, user, pass, database, 10));
            conn.Open();
        }
        public void Begin() { tx = conn.BeginTransaction(IsolationLevel.RepeatableRead); }
        public void Commit() { tx?.Commit(); tx = null; }
        public void Rollback() { tx?.Rollback(); tx = null; }
        public int Execute(string sql) { using var c = new MySqlCommand(sql, conn, tx); return c.ExecuteNonQuery(); }
        public ResultTable Query(string sql, int maxRows)
        {
            var t = new ResultTable();
            using var cmd = new MySqlCommand(sql, conn, tx);
            using var r = cmd.ExecuteReader();
            for (int i = 0; i < r.FieldCount; i++) t.Columns.Add(r.GetName(i));
            while (r.Read())
            {
                if (t.Rows.Count >= maxRows) { t.Truncated = true; break; }
                var row = new object[r.FieldCount]; r.GetValues(row); t.Rows.Add(row);
            }
            return t;
        }
        public IList<string> PrimaryKey(string table)
        {
            var res = new List<string>();
            using var cmd = new MySqlCommand("SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t AND CONSTRAINT_NAME = 'PRIMARY' ORDER BY ORDINAL_POSITION", conn, tx);
            cmd.Parameters.AddWithValue("@t", table);
            using var r = cmd.ExecuteReader();
            while (r.Read()) res.Add(r.GetString(0));
            return res;
        }
        public void Dispose() { try { tx?.Rollback(); } catch { } try { conn.Dispose(); } catch { } }
    }

    public static class Csv
    {
        public static string Escape(string s)
        {
            if (s == null) return "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 || s.StartsWith(" ") || s.EndsWith(" ")) return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }

    /// <summary>Backs up just the tables an Apply will touch, using mysqldump. The password goes in a temporary option file, never on the command line.</summary>
    public static class Backup
    {
        public static List<string> AffectedTables(IEnumerable<Change> changes) =>
            changes.Select(c => c.Table).Where(t => !string.IsNullOrEmpty(t)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t).ToList();

        public static List<string> BuildArgs(string optionFile, string host, int port, string database, IList<string> tables, string outFile)
        {
            var a = new List<string>
            {
                "--defaults-extra-file=" + optionFile,
                "--host=" + host, "--port=" + port,
                "--single-transaction", "--no-tablespaces", "--skip-triggers", "--set-gtid-purged=OFF",
                "--default-character-set=utf8mb4", "--complete-insert", "--hex-blob",
                "--result-file=" + outFile,
                database,
            };
            a.AddRange(tables);
            return a;
        }

        public static string OptionFileText(string user, string pass) =>
            "[client]\nuser=\"" + user.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"\npassword=\"" + pass.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"\n";

        public static string BackupFolder(string root, DateTime now) => Path.Combine(root, now.ToString("yyyy-MM-dd_HHmmss"));

        /// <summary>Runs mysqldump. Returns the .sql path; throws with mysqldump's message on failure.</summary>
        public static string Run(string mysqldumpExe, string host, int port, string user, string pass, string database, IList<string> tables, string folder)
        {
            if (!File.Exists(mysqldumpExe)) throw new FileNotFoundException("mysqldump.exe wasn't found. Set its location in Settings.", mysqldumpExe);
            Directory.CreateDirectory(folder);
            var outFile = Path.Combine(folder, database + "_" + (tables.Count <= 4 ? string.Join("+", tables) : tables.Count + "_tables") + ".sql");
            var opt = Path.Combine(Path.GetTempPath(), "acstudio_" + Guid.NewGuid().ToString("N") + ".cnf");
            File.WriteAllText(opt, OptionFileText(user, pass), new UTF8Encoding(false));
            try
            {
                var psi = new ProcessStartInfo(mysqldumpExe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
                foreach (var a in BuildArgs(opt, host, port, database, tables, outFile)) psi.ArgumentList.Add(a);
                using var p = Process.Start(psi);
                var err = p.StandardError.ReadToEndAsync();
                p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(600000)) { try { p.Kill(); } catch { } throw new Exception("mysqldump took longer than 10 minutes."); }
                var e = err.Result.Trim();
                if (p.ExitCode != 0) throw new Exception("mysqldump failed: " + e);
                if (!File.Exists(outFile) || new FileInfo(outFile).Length == 0) throw new Exception("mysqldump produced no output.");
                File.WriteAllText(Path.Combine(folder, "README.txt"),
                    $"Backup taken by AndersCraft Studio before an Apply, {DateTime.Now:yyyy-MM-dd HH:mm:ss}.\r\nDatabase: {database}\r\nTables: {string.Join(", ", tables)}\r\n\r\nTo restore: mysql -u <admin> -p {database} < \"{Path.GetFileName(outFile)}\"\r\n");
                return outFile;
            }
            finally { try { File.Delete(opt); } catch { } }
        }
    }
}
