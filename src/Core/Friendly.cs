using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using MySqlConnector;

namespace AndersCraftStudio.Core
{
    /// <summary>Turns errors into plain English with what to do next, for people who aren't database experts.</summary>
    public static class Friendly
    {
        public static string Text(Exception ex)
        {
            if (ex == null) return "Unknown problem.";
            if (ex is AggregateException ag && ag.InnerExceptions.Count == 1) ex = ag.InnerException;
            if (ex is System.Reflection.TargetInvocationException ti && ti.InnerException != null) ex = ti.InnerException;
            var my = ex as MySqlException ?? ex.InnerException as MySqlException;
            if (my != null) return Log.Scrub(Sql(my));
            switch (ex)
            {
                case TimeoutException _: return "The server took too long to answer. It may be busy or unreachable; try again, and check that MySQL is running.";
                case SocketException _: return "Couldn't reach the database server. Check that MySQL/MariaDB is running and that the host and port in Settings are right.";
                case FileNotFoundException f: return $"A file wasn't found: {Path.GetFileName(f.FileName ?? f.Message)}. Check the folder in Settings (or setup step 2 / 4).";
                case DirectoryNotFoundException _: return "A folder wasn't found. Check the folders in Settings (server, client, Studio's own folder).";
                case UnauthorizedAccessException _: return "Windows didn't allow access to a file or folder. Close programs that may be using it (the game, an editor), or pick a folder you can write to.";
                case IOException io when IsLocked(io): return "A file is in use by another program (often the game or worldserver). Close it and try again.";
                case InvalidDataException d: return "A file isn't in the expected format (" + Log.Scrub(d.Message) + "). It may be damaged, or from a different game version than 3.3.5a (build 12340).";
                case FormatException fe: return Log.Scrub(fe.Message);
                case OutOfMemoryException _: return "Studio ran out of memory. Close other programs or work with smaller pieces (filters, fewer rows).";
            }
            return Log.Scrub(ex.Message);
        }

        static bool IsLocked(IOException io) => (io.HResult & 0xFFFF) is 32 or 33;

        /// <summary>The error number's meaning for the common MySQL / MariaDB errors.</summary>
        public static string Sql(MySqlException e)
        {
            var hint = e.Number switch
            {
                1045 => "The database refused the login: wrong user name or password. Run setup step 3 again (or check the Admin console login).",
                1044 or 1142 or 1143 => "That account isn't allowed to do this on this database. Studio's accounts only have the rights setup step 3 gave them.",
                1049 => "That database doesn't exist on this server. Check the database names in Settings (they come from worldserver.conf).",
                1146 => "A table Studio expected isn't there. Your database may be from a different AzerothCore version, or not fully set up.",
                1054 => "A column Studio expected isn't there. Your database may be from a different AzerothCore version.",
                1205 => "The database was busy (a lock wait timed out). Something else is changing the same rows; try again in a moment.",
                1213 => "The database stopped this because of a conflict with another change happening at the same time. Try again.",
                1062 => "A row with that key already exists.",
                1451 or 1452 => "Another table's row depends on this one (a foreign key), so the database refused.",
                1064 => "The SQL has a syntax error.",
                1040 => "The database server has too many connections open. Try again shortly, or raise max_connections.",
                1129 => "The database server has blocked this computer after too many failed connections. Run FLUSH HOSTS as an admin, or restart MySQL.",
                1251 => "The database uses a login method this connection can't use. Re-create the account with a password (setup step 3).",
                _ => null,
            };
            if (hint == null && (e.ErrorCode == MySqlErrorCode.UnableToConnectToHost || e.Number == 1042 || e.Number == 2003 || e.Number == 2002))
                hint = "Couldn't reach the database server. Check that MySQL/MariaDB is running and that the host and port in Settings are right.";
            return hint == null ? e.Message : hint + " (" + e.Message + ")";
        }
    }

    /// <summary>
    /// A daily log file in %APPDATA%\AndersCraftStudio\logs (last 14 days kept), so problems can be reported. Passwords,
    /// connection strings and IDENTIFIED BY clauses are removed before anything is written.
    /// </summary>
    public static class Log
    {
        static readonly object gate = new object();
        public static string Folder => Path.Combine(ProfileStore.DefaultRoot, "logs");
        public static string Today => Path.Combine(Folder, $"studio-{DateTime.Now:yyyy-MM-dd}.log");

        static readonly Regex[] Secrets =
        {
            new Regex(@"(IDENTIFIED\s+(WITH\s+\w+\s+)?BY\s+)('(?:[^'\\]|\\.|'')*'|""(?:[^""\\]|\\.)*"")", RegexOptions.IgnoreCase),
            new Regex(@"((?:password|pwd|pass)\s*[=:]\s*)(""[^""]*""|'[^']*'|[^;\s,]+)", RegexOptions.IgnoreCase),
            new Regex(@"(PASSWORD\s*\(\s*)('(?:[^'\\]|\\.)*')", RegexOptions.IgnoreCase),
            new Regex(@"((?:Login|World|Character)DatabaseInfo\s*=\s*""[^;""]*;[^;""]*;[^;""]*;)([^;""]*)", RegexOptions.IgnoreCase),
        };

        /// <summary>The text with anything that looks like a password replaced by ********.</summary>
        public static string Scrub(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            foreach (var r in Secrets) s = r.Replace(s, m => m.Groups[1].Value + "********");
            return s;
        }

        public static void Write(string level, string text)
        {
            try
            {
                lock (gate)
                {
                    Directory.CreateDirectory(Folder);
                    File.AppendAllText(Today, $"{DateTime.Now:HH:mm:ss} {level.ToUpperInvariant(),-5} {Scrub(text)}{Environment.NewLine}");
                    Prune();
                }
            }
            catch { }   // logging must never break the app
        }

        public static void Error(string what, Exception ex) => Write("error", what + ": " + (ex == null ? "" : ex.GetType().Name + ": " + ex.Message + Environment.NewLine + ex.StackTrace));
        public static void Info(string text) => Write("info", text);

        static DateTime pruned;
        static void Prune()
        {
            if ((DateTime.Now - pruned).TotalHours < 6) return;
            pruned = DateTime.Now;
            foreach (var f in Directory.GetFiles(Folder, "studio-*.log").OrderByDescending(f => f).Skip(14)) try { File.Delete(f); } catch { }
        }
    }
}
