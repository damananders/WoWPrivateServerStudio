using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MySqlConnector;

namespace AndersCraftStudio.Core
{
    /// <summary>
    /// The Admin console's connection: a MySQL account with admin rights, typed in for this session only. Studio never
    /// saves it (not in the profile, not encrypted, not anywhere). Statements run directly, like any MySQL client.
    /// </summary>
    public sealed class AdminDb : IDisposable
    {
        readonly MySqlConnection conn;
        MySqlTransaction tx;
        public string Host { get; }
        public int Port { get; }
        public string User { get; }
        public string Database => conn.Database;
        public bool InTransaction => tx != null;

        public AdminDb(string host, int port, string user, string pass, string database = null)
        {
            Host = host; Port = port; User = user;
            var b = new MySqlConnectionStringBuilder(ConnStrings.Build(host, port, user, pass, database, 10)) { DefaultCommandTimeout = 0, AllowUserVariables = true };
            conn = new MySqlConnection(b.ConnectionString);
            conn.Open();
        }

        public void Use(string database) { if (!string.IsNullOrEmpty(database)) conn.ChangeDatabase(database); }
        public void Begin() { if (tx == null) tx = conn.BeginTransaction(); }
        public void Commit() { tx?.Commit(); tx = null; }
        public void Rollback() { tx?.Rollback(); tx = null; }

        /// <summary>Runs one statement. Result sets come back as tables (up to <paramref name="maxRows"/> rows each).</summary>
        public AdminResult Run(string sql, int maxRows = 5000)
        {
            var res = new AdminResult { Sql = sql };
            var sw = Stopwatch.StartNew();
            try
            {
                using var cmd = new MySqlCommand(sql, conn, tx);
                using var r = cmd.ExecuteReader();
                do
                {
                    if (r.FieldCount == 0) continue;
                    var dt = new DataTable();
                    for (int i = 0; i < r.FieldCount; i++)
                    {
                        var n = r.GetName(i); while (dt.Columns.Contains(n)) n += "_";
                        dt.Columns.Add(n, typeof(object));
                    }
                    while (r.Read())
                    {
                        if (dt.Rows.Count >= maxRows) { res.Truncated = true; break; }
                        var row = new object[r.FieldCount]; r.GetValues(row); dt.Rows.Add(row);
                    }
                    res.Tables.Add(dt);
                } while (r.NextResult());
                res.Affected = r.RecordsAffected;
            }
            catch (Exception ex) { res.Error = ex.Message; }
            res.Ms = sw.ElapsedMilliseconds;
            return res;
        }

        public DataTable Table(string sql) { var r = Run(sql, 100000); if (r.Error != null) throw new InvalidOperationException(r.Error); return r.Tables.FirstOrDefault() ?? new DataTable(); }
        public string Scalar(string sql) { var t = Table(sql); return t.Rows.Count == 0 ? null : Convert.ToString(t.Rows[0][0], CultureInfo.InvariantCulture); }

        public void Dispose() { try { tx?.Rollback(); } catch { } try { conn.Dispose(); } catch { } }
    }

    public sealed class AdminResult
    {
        public string Sql, Error;
        public List<DataTable> Tables = new List<DataTable>();
        public int Affected;
        public long Ms;
        public bool Truncated;
        public bool Ok => Error == null;
        public string Summary => Error != null ? "Error: " + Error : Tables.Count > 0 ? $"{Tables.Sum(t => t.Rows.Count)} row(s){(Truncated ? " (cut off)" : "")} · {Ms} ms" : $"{Math.Max(0, Affected)} row(s) affected · {Ms} ms";
    }

    /// <summary>Splits a .sql script into statements the way the mysql client does: ';' outside quotes and comments, and DELIMITER lines.</summary>
    public static class SqlBatch
    {
        static readonly Regex DelimiterLine = new Regex(@"^\s*DELIMITER\s+(\S+)\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline);

        public static List<string> Split(string text)
        {
            var res = new List<string>();
            text ??= "";
            string delim = ";";
            int pos = 0;
            foreach (Match m in DelimiterLine.Matches(text))
            {
                AddChunk(res, text.Substring(pos, m.Index - pos), delim);
                delim = m.Groups[1].Value;
                pos = m.Index + m.Length;
            }
            AddChunk(res, text.Substring(pos), delim);
            return res;
        }

        static void AddChunk(List<string> res, string chunk, string delim)
        {
            if (delim == ";")
            {
                foreach (var (raw, _) in SqlLexer.Split(chunk)) { var s = raw.Trim(); if (s.Length > 0 && !IsOnlyComment(s)) res.Add(s); }
                return;
            }
            foreach (var part in chunk.Split(new[] { delim }, StringSplitOptions.None)) { var s = part.Trim(); if (s.Length > 0 && !IsOnlyComment(s)) res.Add(s); }
        }

        static bool IsOnlyComment(string s)
        {
            var toks = SqlLexer.Lex(s);
            return toks.Count == 0;
        }

        /// <summary>The .sql files of a folder in the order to run them (by file name, like AzerothCore's own updater).</summary>
        public static List<string> FolderOrder(IEnumerable<string> files) =>
            files.Where(f => f.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)).OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>How risky an admin statement is: drives the typed confirmation and the warnings.</summary>
    public enum AdminRisk { Read, Change, Destructive, Irreversible }

    /// <summary>Statement builders and checks for the Admin console. Pure functions; nothing runs here.</summary>
    public static class AdminSql
    {
        public static string Ident(string n) => SqlGen.Ident(n);
        public static string Account(string user, string host) => SqlGen.Literal(user) + "@" + SqlGen.Literal(string.IsNullOrWhiteSpace(host) ? "%" : host);

        public static readonly string[] Charsets = { "utf8mb4", "utf8mb3", "utf8", "latin1" };

        public static string CreateDatabase(string name, string charset = "utf8mb4", string collation = null) =>
            $"CREATE DATABASE IF NOT EXISTS {Ident(name)} DEFAULT CHARACTER SET {charset}" + (string.IsNullOrEmpty(collation) ? "" : " COLLATE " + collation) + ";";
        public static string DropDatabase(string name) => $"DROP DATABASE {Ident(name)};";

        public static string CreateUser(string user, string host, string password) => $"CREATE USER IF NOT EXISTS {Account(user, host)} IDENTIFIED BY {SqlGen.Literal(password)};";
        public static string DropUser(string user, string host) => $"DROP USER {Account(user, host)};";
        public static string SetPassword(string user, string host, string password) => $"ALTER USER {Account(user, host)} IDENTIFIED BY {SqlGen.Literal(password)};";

        public static readonly string[] Privileges = { "ALL PRIVILEGES", "SELECT", "INSERT", "UPDATE", "DELETE", "CREATE", "DROP", "ALTER", "INDEX", "CREATE VIEW", "SHOW VIEW", "CREATE TEMPORARY TABLES", "LOCK TABLES", "EXECUTE", "CREATE ROUTINE", "ALTER ROUTINE", "EVENT", "TRIGGER", "REFERENCES", "PROCESS", "RELOAD", "CREATE USER" };

        static string On(string database, string table) => (string.IsNullOrWhiteSpace(database) || database == "*" ? "*" : Ident(database)) + "." + (string.IsNullOrWhiteSpace(table) || table == "*" ? "*" : Ident(table));

        public static string Grant(IEnumerable<string> privs, string database, string table, string user, string host, bool withGrant = false)
        {
            var p = privs.Select(x => x.Trim().ToUpperInvariant()).Where(x => x.Length > 0).Distinct().ToList();
            if (p.Count == 0) throw new ArgumentException("Pick at least one privilege.");
            foreach (var x in p) if (!Privileges.Contains(x)) throw new ArgumentException($"Unknown privilege {x}.");
            return $"GRANT {string.Join(", ", p)} ON {On(database, table)} TO {Account(user, host)}" + (withGrant ? " WITH GRANT OPTION" : "") + ";";
        }

        public static string Revoke(IEnumerable<string> privs, string database, string table, string user, string host)
        {
            var p = privs.Select(x => x.Trim().ToUpperInvariant()).Where(x => x.Length > 0).Distinct().ToList();
            if (p.Count == 0) throw new ArgumentException("Pick at least one privilege.");
            foreach (var x in p) if (!Privileges.Contains(x)) throw new ArgumentException($"Unknown privilege {x}.");
            return $"REVOKE {string.Join(", ", p)} ON {On(database, table)} FROM {Account(user, host)};";
        }

        public static string Kill(long id) => $"KILL {id.ToString(CultureInfo.InvariantCulture)};";

        // ---- structure
        public sealed class ColumnSpec
        {
            public string Name, Type = "INT";
            public bool Unsigned, NotNull = true, AutoIncrement, PrimaryKey;
            public string Default, Comment;
        }

        static readonly Regex TypeOk = new Regex(@"^[A-Za-z]+(\s*\(\s*\d+(\s*,\s*\d+)?\s*\))?$|^(ENUM|SET)\s*\(.+\)$", RegexOptions.IgnoreCase);

        public static string ColumnDef(ColumnSpec c)
        {
            if (string.IsNullOrWhiteSpace(c.Name)) throw new ArgumentException("Every column needs a name.");
            if (string.IsNullOrWhiteSpace(c.Type) || !TypeOk.IsMatch(c.Type.Trim())) throw new ArgumentException($"Column {c.Name}: \"{c.Type}\" isn't a MySQL type like INT, VARCHAR(100), FLOAT or TEXT.");
            var sb = new StringBuilder(Ident(c.Name) + " " + c.Type.Trim().ToUpperInvariant());
            if (c.Unsigned) sb.Append(" UNSIGNED");
            sb.Append(c.NotNull ? " NOT NULL" : " NULL");
            if (c.Default != null) sb.Append(" DEFAULT " + (c.Default.Equals("NULL", StringComparison.OrdinalIgnoreCase) ? "NULL" : IsNumber(c.Default) ? c.Default : SqlGen.Literal(c.Default)));
            if (c.AutoIncrement) sb.Append(" AUTO_INCREMENT");
            if (!string.IsNullOrEmpty(c.Comment)) sb.Append(" COMMENT " + SqlGen.Literal(c.Comment));
            return sb.ToString();
        }

        static bool IsNumber(string s) => decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

        public static string CreateTable(string table, IList<ColumnSpec> cols, string engine = "InnoDB", string charset = "utf8mb4")
        {
            if (cols == null || cols.Count == 0) throw new ArgumentException("A table needs at least one column.");
            var dup = cols.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
            if (dup != null) throw new ArgumentException($"Column {dup.Key} appears twice.");
            var lines = cols.Select(c => "  " + ColumnDef(c)).ToList();
            var pk = cols.Where(c => c.PrimaryKey).Select(c => Ident(c.Name)).ToList();
            if (pk.Count > 0) lines.Add("  PRIMARY KEY (" + string.Join(", ", pk) + ")");
            return $"CREATE TABLE {Ident(table)} (\n" + string.Join(",\n", lines) + $"\n) ENGINE={engine} DEFAULT CHARSET={charset};";
        }

        public static string AddColumn(string table, ColumnSpec c, string after = null) => $"ALTER TABLE {Ident(table)} ADD COLUMN {ColumnDef(c)}" + (string.IsNullOrEmpty(after) ? "" : after == "FIRST" ? " FIRST" : " AFTER " + Ident(after)) + ";";
        public static string ChangeColumn(string table, string oldName, ColumnSpec c) => $"ALTER TABLE {Ident(table)} CHANGE COLUMN {Ident(oldName)} {ColumnDef(c)};";
        public static string DropColumn(string table, string col) => $"ALTER TABLE {Ident(table)} DROP COLUMN {Ident(col)};";
        public static string AddIndex(string table, string name, IList<string> cols, bool unique) =>
            cols == null || cols.Count == 0 ? throw new ArgumentException("An index needs at least one column.") :
            $"ALTER TABLE {Ident(table)} ADD {(unique ? "UNIQUE " : "")}INDEX {Ident(name)} ({string.Join(", ", cols.Select(Ident))});";
        public static string DropIndex(string table, string name) => name.Equals("PRIMARY", StringComparison.OrdinalIgnoreCase) ? $"ALTER TABLE {Ident(table)} DROP PRIMARY KEY;" : $"ALTER TABLE {Ident(table)} DROP INDEX {Ident(name)};";
        public static string RenameTable(string from, string to) => $"RENAME TABLE {Ident(from)} TO {Ident(to)};";
        public static string DropTable(string table) => $"DROP TABLE {Ident(table)};";
        public static string TruncateTable(string table) => $"TRUNCATE TABLE {Ident(table)};";
        public static string CreateView(string name, string select, bool replace = true)
        {
            var st = SqlClassifier.ClassifyOne(select);
            if (st.Kind != StatementKind.Read) throw new ArgumentException("A view must be a single SELECT.");
            return $"CREATE {(replace ? "OR REPLACE " : "")}VIEW {Ident(name)} AS {select.Trim().TrimEnd(';')};";
        }
        public static string DropView(string name) => $"DROP VIEW {Ident(name)};";

        // ---- risk, confirmation and masking
        static readonly Regex Lead = new Regex(@"^\s*(/\*!\d+\s*)?(?<v>\w+)(\s+(?<w>\w+))?", RegexOptions.Compiled);

        public static AdminRisk Risk(string sql)
        {
            var m = Lead.Match(StripComments(sql));
            var v = m.Groups["v"].Value.ToUpperInvariant(); var w = m.Groups["w"].Value.ToUpperInvariant();
            switch (v)
            {
                case "SELECT": case "SHOW": case "DESCRIBE": case "DESC": case "EXPLAIN": case "USE": case "SET": case "WITH":
                case "BEGIN": case "START": case "COMMIT": case "ROLLBACK": case "HELP": case "":
                    return AdminRisk.Read;
                case "DROP": case "TRUNCATE":
                    return w == "USER" ? AdminRisk.Irreversible : AdminRisk.Destructive;
                case "KILL": case "GRANT": case "REVOKE": case "RENAME" when w == "USER":
                    return AdminRisk.Irreversible;
                case "CREATE": case "ALTER":
                    return w == "USER" || w == "ROLE" ? AdminRisk.Irreversible : AdminRisk.Change;
                case "DELETE": case "UPDATE":
                    return Regex.IsMatch(sql, @"\bWHERE\b", RegexOptions.IgnoreCase) ? AdminRisk.Change : AdminRisk.Destructive;
                default:
                    return AdminRisk.Change;
            }
        }

        public static string RiskText(AdminRisk r) => r switch
        {
            AdminRisk.Read => "reads only",
            AdminRisk.Change => "changes data or structure",
            AdminRisk.Destructive => "removes data — only a backup brings it back",
            _ => "can't be undone (accounts, permissions or connections)",
        };

        static string StripComments(string sql) => Regex.Replace(sql ?? "", @"^\s*((--|#)[^\n]*\n|/\*(?!!).*?\*/)*", "", RegexOptions.Singleline);

        static readonly Regex Password = new Regex(@"(IDENTIFIED\s+(WITH\s+\w+\s+)?BY\s+|PASSWORD\s*(=|\()\s*)('(?:[^'\\]|\\.|'')*'|""(?:[^""\\]|\\.)*"")", RegexOptions.IgnoreCase);

        /// <summary>The statement with passwords replaced by ******** (for previews, logs and history).</summary>
        public static string Mask(string sql) => Password.Replace(sql ?? "", m => m.Groups[1].Value + "'********'");

        /// <summary>What the user must type to confirm a risky batch (the object name when there's one, else RUN).</summary>
        public static string ConfirmWord(IEnumerable<string> statements)
        {
            var risky = statements.Where(s => Risk(s) >= AdminRisk.Destructive).ToList();
            if (risky.Count == 1)
            {
                var m = Regex.Match(StripComments(risky[0]), @"^\s*(DROP|TRUNCATE)\s+(TABLE|DATABASE|SCHEMA|VIEW|INDEX)?\s*(IF\s+EXISTS\s+)?`?(?<n>[\w$]+)`?", RegexOptions.IgnoreCase);
                if (m.Success) return m.Groups["n"].Value;
            }
            return "RUN";
        }
    }

    /// <summary>Writes a .sql dump of whole databases or chosen tables (structure + data) through any connection.</summary>
    public static class DatabaseDump
    {
        public static string DefaultFolder() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AndersCraft Studio", "dumps");

        public static string FileName(string database, int tables, DateTime now) => $"{AssetImport.SafeName(database)}_{(tables > 0 ? tables + "_tables_" : "")}{now:yyyy-MM-dd_HHmmss}.sql";

        /// <summary>Header for a dump that can be imported into an empty server (creates and selects the database).</summary>
        public static void WriteHeader(TextWriter w, string database, bool createDatabase)
        {
            SqlDump.WriteHeader(w, database);
            w.WriteLine("SET FOREIGN_KEY_CHECKS = 0;");
            if (createDatabase)
            {
                w.WriteLine($"CREATE DATABASE IF NOT EXISTS {SqlGen.Ident(database)} DEFAULT CHARACTER SET utf8mb4;");
                w.WriteLine($"USE {SqlGen.Ident(database)};");
            }
            w.WriteLine();
        }

        public static void WriteFooter(TextWriter w) { w.WriteLine("SET FOREIGN_KEY_CHECKS = 1;"); w.WriteLine("-- end of dump"); }
    }

    /// <summary>The steps to set up AzerothCore's databases on an empty MySQL server.</summary>
    public static class ServerSetup
    {
        public sealed class Plan
        {
            public string World = "acore_world", Characters = "acore_characters", Auth = "acore_auth";
            public string User = "acore", Host = "localhost", Password;
            public bool CreateUser = true, ImportBase = true;
            public string SqlRoot;      // AzerothCore's data\sql folder
        }

        /// <summary>The folders under data\sql\base that hold each database's starting tables.</summary>
        public static IEnumerable<(string db, string folder)> BaseFolders(Plan p)
        {
            yield return (p.Auth, Path.Combine(p.SqlRoot ?? "", "base", "db_auth"));
            yield return (p.Characters, Path.Combine(p.SqlRoot ?? "", "base", "db_characters"));
            yield return (p.World, Path.Combine(p.SqlRoot ?? "", "base", "db_world"));
        }

        /// <summary>Statements that create the three databases and (optionally) the server's account with rights on them.</summary>
        public static List<string> Statements(Plan p)
        {
            foreach (var n in new[] { p.World, p.Characters, p.Auth })
                if (string.IsNullOrWhiteSpace(n) || !Regex.IsMatch(n, @"^[\w$]+$")) throw new ArgumentException($"\"{n}\" isn't a valid database name (letters, digits and _ only).");
            var s = new List<string> { AdminSql.CreateDatabase(p.World), AdminSql.CreateDatabase(p.Characters), AdminSql.CreateDatabase(p.Auth) };
            if (p.CreateUser)
            {
                if (string.IsNullOrWhiteSpace(p.User)) throw new ArgumentException("Enter the account name the server logs in with.");
                if (string.IsNullOrEmpty(p.Password)) throw new ArgumentException("Enter a password for the server's account.");
                s.Add(AdminSql.CreateUser(p.User, p.Host, p.Password));
                foreach (var n in new[] { p.World, p.Characters, p.Auth }) s.Add(AdminSql.Grant(new[] { "ALL PRIVILEGES" }, n, "*", p.User, p.Host));
            }
            return s;
        }

        /// <summary>The worldserver.conf / authserver.conf login lines matching the plan (password left for the user to fill in).</summary>
        public static string ConfLines(Plan p, string host = "127.0.0.1", int port = 3306) =>
            $"LoginDatabaseInfo     = \"{host};{port};{p.User};<password>;{p.Auth}\"\n" +
            $"WorldDatabaseInfo     = \"{host};{port};{p.User};<password>;{p.World}\"\n" +
            $"CharacterDatabaseInfo = \"{host};{port};{p.User};<password>;{p.Characters}\"";
    }
}
