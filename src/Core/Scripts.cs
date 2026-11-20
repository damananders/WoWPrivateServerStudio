using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AndersCraftStudio.Core
{
    /// <summary>Builds the copy-paste SQL that creates Studio's two limited MySQL accounts. Studio never runs it itself.</summary>
    public static class AccountScript
    {
        public static string Str(string s) => "'" + SqlGen.EscapeString(s ?? "") + "'";

        /// <summary>Database name for GRANT ... ON: '_' and '%' are wildcards there, so they are escaped.</summary>
        public static string GrantDb(string db) => "`" + db.Replace("`", "``").Replace("_", "\\_").Replace("%", "\\%") + "`.*";

        public static IList<string> HostsFor(Profile p)
        {
            var h = (p.AccountHost ?? "localhost").Trim();
            if (h == "localhost" || h == "127.0.0.1" || h == "::1") return new[] { "localhost", "127.0.0.1" };
            return new[] { h };
        }

        public static string Build(Profile p, string roPassword, string rwPassword)
        {
            var sb = new StringBuilder();
            sb.AppendLine("-- =====================================================================");
            sb.AppendLine("-- AndersCraft Studio - safe database accounts");
            sb.AppendLine($"-- Generated {DateTime.Now:yyyy-MM-dd HH:mm} for server {p.Host}:{p.Port}");
            sb.AppendLine("--");
            sb.AppendLine("-- Run this ONCE yourself, logged in as a MySQL administrator (for example root),");
            sb.AppendLine("-- in HeidiSQL, MySQL Workbench or the mysql command line. Studio never runs it.");
            sb.AppendLine("--");
            sb.AppendLine($"--   {p.RoUser}  can only READ. Studio uses it for browsing and searching.");
            sb.AppendLine($"--   {p.RwUser}  can change ROWS in the world database (and the practice");
            sb.AppendLine("--              sandbox) but cannot create, drop or alter tables, and has no");
            sb.AppendLine("--              access to accounts or characters. Studio only uses it when you");
            sb.AppendLine("--              press Apply, after a backup and a typed confirmation.");
            sb.AppendLine("--");
            sb.AppendLine("-- Running it again is safe: it resets the passwords to the ones below.");
            sb.AppendLine("-- =====================================================================");
            sb.AppendLine();
            foreach (var host in HostsFor(p))
            {
                var ro = Str(p.RoUser) + "@" + Str(host);
                var rw = Str(p.RwUser) + "@" + Str(host);
                sb.AppendLine($"-- accounts for connections from {host}");
                sb.AppendLine($"CREATE USER IF NOT EXISTS {ro} IDENTIFIED BY {Str(roPassword)};");
                sb.AppendLine($"ALTER USER {ro} IDENTIFIED BY {Str(roPassword)};");
                sb.AppendLine($"CREATE USER IF NOT EXISTS {rw} IDENTIFIED BY {Str(rwPassword)};");
                sb.AppendLine($"ALTER USER {rw} IDENTIFIED BY {Str(rwPassword)};");
                sb.AppendLine();
                sb.AppendLine($"-- read-only: look, never touch");
                sb.AppendLine($"GRANT SELECT, SHOW VIEW ON {GrantDb(p.WorldDb)} TO {ro};");
                sb.AppendLine($"GRANT SELECT, SHOW VIEW ON {GrantDb(p.SandboxDb)} TO {ro};");
                sb.AppendLine($"GRANT SELECT, SHOW VIEW ON {GrantDb(p.CharactersDb)} TO {ro};");
                if (p.AllowAuthView) sb.AppendLine($"GRANT SELECT, SHOW VIEW ON {GrantDb(p.AuthDb)} TO {ro};");
                sb.AppendLine();
                sb.AppendLine($"-- apply account: row changes in the world database and the sandbox only");
                sb.AppendLine($"GRANT SELECT, INSERT, UPDATE, DELETE, SHOW VIEW ON {GrantDb(p.WorldDb)} TO {rw};");
                sb.AppendLine($"GRANT SELECT, INSERT, UPDATE, DELETE, SHOW VIEW ON {GrantDb(p.SandboxDb)} TO {rw};");
                sb.AppendLine();
            }
            sb.AppendLine("-- check the result (optional):");
            foreach (var host in HostsFor(p))
            {
                sb.AppendLine($"SHOW GRANTS FOR {Str(p.RoUser)}@{Str(host)};");
                sb.AppendLine($"SHOW GRANTS FOR {Str(p.RwUser)}@{Str(host)};");
            }
            return sb.ToString();
        }
    }

    /// <summary>Reads SHOW GRANTS output to confirm an account can't write.</summary>
    public static class GrantCheck
    {
        /// <summary>Returns the privileges in the grants that allow writing. Empty means read-only.</summary>
        public static List<string> WritePrivileges(IEnumerable<string> grantLines)
        {
            var found = new List<string>();
            foreach (var line in grantLines)
            {
                var m = Regex.Match(line, @"^GRANT\s+(.+?)\s+ON\s+", RegexOptions.IgnoreCase);
                if (!m.Success)
                {
                    // "GRANT `role`@`%` TO user": a role could carry any privilege, so it can't be confirmed read-only
                    if (Regex.IsMatch(line, @"^GRANT\s+.+\s+TO\s+", RegexOptions.IgnoreCase) && !found.Contains("ROLE")) found.Add("ROLE");
                    continue;
                }
                if (Regex.IsMatch(line, @"\sON\s+\*\.\*", RegexOptions.IgnoreCase) && Regex.IsMatch(m.Groups[1].Value, @"^\s*USAGE\s*$", RegexOptions.IgnoreCase)) continue;
                foreach (var priv in m.Groups[1].Value.Split(','))
                {
                    var pv = Regex.Replace(priv.Trim().ToUpperInvariant(), @"\s*\(.*\)$", "");
                    if (pv == "SELECT" || pv == "SHOW VIEW" || pv == "USAGE" || pv == "SHOW DATABASES") continue;
                    if (pv == "ALL PRIVILEGES") pv = "ALL";
                    // anything beyond the read privileges above counts as able to change something
                    if (!found.Contains(pv)) found.Add(pv);
                }
                if (Regex.IsMatch(line, @"WITH\s+GRANT\s+OPTION", RegexOptions.IgnoreCase) && !found.Contains("GRANT OPTION")) found.Add("GRANT OPTION");
            }
            return found;
        }
    }

    /// <summary>The practice sandbox: a full copy of the world database you can Apply to without risk.</summary>
    public static class SandboxPlan
    {
        public static string Build(Profile p, IList<string> worldTables, string charset = "utf8mb4", string collation = null)
        {
            var sb = new StringBuilder();
            var sbx = SqlGen.Ident(p.SandboxDb); var w = SqlGen.Ident(p.WorldDb);
            sb.AppendLine("-- =====================================================================");
            sb.AppendLine("-- AndersCraft Studio - create the practice sandbox");
            sb.AppendLine($"-- Copies every table of {p.WorldDb} into {p.SandboxDb}.");
            sb.AppendLine("-- Run it yourself as a MySQL administrator. The server can stay online:");
            sb.AppendLine("-- it only reads the world database. It can take a few minutes and needs");
            sb.AppendLine("-- about as much disk space as the world database.");
            sb.AppendLine("-- The game server never uses the sandbox; it is only for practising Apply.");
            sb.AppendLine("-- To start over later: DROP DATABASE " + sbx + "; then run this again.");
            sb.AppendLine("-- =====================================================================");
            sb.AppendLine();
            sb.AppendLine($"CREATE DATABASE IF NOT EXISTS {sbx} CHARACTER SET {charset}" + (string.IsNullOrEmpty(collation) ? "" : " COLLATE " + collation) + ";");
            sb.AppendLine();
            if (worldTables == null || worldTables.Count == 0)
            {
                sb.AppendLine("-- Studio couldn't read the table list (finish step 3 and connect first),");
                sb.AppendLine("-- so here is the command-line way instead (run in a terminal, one line):");
                sb.AppendLine($"--   mysqldump -u root -p --single-transaction --no-tablespaces {p.WorldDb} | mysql -u root -p {p.SandboxDb}");
                return sb.ToString();
            }
            foreach (var t in worldTables)
            {
                sb.AppendLine($"CREATE TABLE IF NOT EXISTS {sbx}.{SqlGen.Ident(t)} LIKE {w}.{SqlGen.Ident(t)};");
                sb.AppendLine($"INSERT INTO {sbx}.{SqlGen.Ident(t)} SELECT * FROM {w}.{SqlGen.Ident(t)};");
            }
            sb.AppendLine();
            sb.AppendLine($"-- {worldTables.Count} tables. When it finishes, return to Studio and press \"Check again\".");
            return sb.ToString();
        }
    }
}
