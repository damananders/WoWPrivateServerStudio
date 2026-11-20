using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace AndersCraftStudio.Core
{
    /// <summary>Connection details from one "*DatabaseInfo" line: host;port;user;password;database.</summary>
    public sealed class DbInfo
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public string User { get; set; }
        public string Password { get; set; }
        public string Database { get; set; }
        /// <summary>Port field was "." (named pipe / unix socket); Port then defaults to 3306.</summary>
        public bool UsesSocket { get; set; }
        public override string ToString() => $"{Host}:{Port}/{Database}";
    }

    /// <summary>Result of reading a worldserver.conf.</summary>
    public sealed class ServerConf
    {
        public string Path { get; set; }
        public string Core { get; set; }
        public Dictionary<string, string> Values { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public DbInfo World { get; set; }
        public DbInfo Characters { get; set; }
        public DbInfo Login { get; set; }
        public string DataDir { get; set; }
        public int? RealmId { get; set; }
        public List<string> Warnings { get; } = new List<string>();

        public string Get(string key) => Values.TryGetValue(key, out var v) ? v : null;
    }

    /// <summary>
    /// Reads the "Key = Value" format used by AzerothCore / TrinityCore-family worldserver.conf files.
    /// Lines starting with '#' are comments, values may be wrapped in double quotes, and later
    /// definitions of the same key win (that is how the server itself treats duplicates).
    /// </summary>
    public static class ConfParser
    {
        public static ServerConf ParseFile(string path)
        {
            var c = Parse(File.ReadAllText(path));
            c.Path = path;
            return c;
        }

        public static ServerConf Parse(string text)
        {
            var conf = new ServerConf();
            if (text == null) text = "";
            using (var sr = new StringReader(text))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    var t = line.Trim();
                    if (t.Length == 0 || t[0] == '#' || t[0] == '[') continue;
                    int eq = t.IndexOf('=');
                    if (eq <= 0) continue;
                    var key = t.Substring(0, eq).Trim();
                    if (key.Length == 0 || key.IndexOf(' ') >= 0) continue;
                    conf.Values[key] = Unquote(t.Substring(eq + 1).Trim());
                }
            }

            conf.Core = DetectCore(text);
            conf.World = ParseDbInfo(conf.Get("WorldDatabaseInfo"));
            conf.Characters = ParseDbInfo(conf.Get("CharacterDatabaseInfo"));
            conf.Login = ParseDbInfo(conf.Get("LoginDatabaseInfo"));
            conf.DataDir = conf.Get("DataDir");
            if (int.TryParse(conf.Get("RealmID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int rid)) conf.RealmId = rid;

            if (conf.World == null) conf.Warnings.Add("WorldDatabaseInfo was not found or is not in host;port;user;password;database form.");
            if (conf.Characters == null) conf.Warnings.Add("CharacterDatabaseInfo was not found.");
            if (conf.Login == null) conf.Warnings.Add("LoginDatabaseInfo was not found.");
            return conf;
        }

        /// <summary>Strips one pair of surrounding double quotes and any trailing "# comment" outside quotes.</summary>
        public static string Unquote(string v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            if (v[0] == '"')
            {
                int end = v.IndexOf('"', 1);
                return end > 0 ? v.Substring(1, end - 1) : v.Substring(1);
            }
            int hash = v.IndexOf(" #", StringComparison.Ordinal);
            if (hash >= 0) v = v.Substring(0, hash);
            return v.Trim();
        }

        public static DbInfo ParseDbInfo(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var p = value.Split(';');
            if (p.Length < 5) return null;
            var info = new DbInfo { Host = p[0].Trim(), User = p[2], Password = p[3], Database = p[4].Trim() };
            // A password may itself contain ';' – everything between field 3 and the last field belongs to it.
            if (p.Length > 5)
            {
                info.Password = string.Join(";", p, 3, p.Length - 4);
                info.Database = p[p.Length - 1].Trim();
            }
            var port = p[1].Trim();
            if (port == ".") { info.UsesSocket = true; info.Port = 3306; }
            else if (int.TryParse(port, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n > 0 && n < 65536) info.Port = n;
            else return null;
            if (info.Host.Length == 0 || info.Database.Length == 0) return null;
            return info;
        }

        public static string DetectCore(string text)
        {
            if (text.IndexOf("AzerothCore", StringComparison.OrdinalIgnoreCase) >= 0) return "AzerothCore";
            if (text.IndexOf("TrinityCore", StringComparison.OrdinalIgnoreCase) >= 0) return "TrinityCore";
            if (text.IndexOf("CMaNGOS", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("MaNGOS", StringComparison.OrdinalIgnoreCase) >= 0) return "MaNGOS family";
            if (text.IndexOf("WorldDatabaseInfo", StringComparison.OrdinalIgnoreCase) >= 0) return "AzerothCore-compatible (not confirmed)";
            return "Unknown";
        }
    }

    /// <summary>Looks for worldserver.conf in the places AzerothCore builds normally put it.</summary>
    public static class ConfLocator
    {
        static readonly string[] SubPaths =
        {
            @"Build\bin\RelWithDebInfo\configs", @"Build\bin\Release\configs", @"Build\bin\Debug\configs",
            @"build\bin\RelWithDebInfo\configs", @"build\bin\Release\configs", @"build\bin\configs",
            @"bin\RelWithDebInfo\configs", @"bin\Release\configs", @"bin\configs", @"env\dist\etc", @"etc", @"configs", "",
        };

        public static List<string> Find(IEnumerable<string> roots, int depth = 2, int maxResults = 10)
        {
            var found = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in roots)
            {
                if (found.Count >= maxResults) break;
                try { Walk(root, depth, found, seen, maxResults); } catch { }
            }
            return found;
        }

        static void Walk(string dir, int depth, List<string> found, HashSet<string> seen, int max)
        {
            if (found.Count >= max || !Directory.Exists(dir)) return;
            foreach (var sub in SubPaths)
            {
                var f = System.IO.Path.Combine(dir, sub, "worldserver.conf");
                if (File.Exists(f) && seen.Add(System.IO.Path.GetFullPath(f))) found.Add(System.IO.Path.GetFullPath(f));
            }
            if (depth <= 0) return;
            string[] subs;
            try { subs = Directory.GetDirectories(dir); } catch { return; }
            foreach (var s in subs)
            {
                var n = System.IO.Path.GetFileName(s).ToLowerInvariant();
                if (n.StartsWith("$") || n == "windows" || n == "program files" || n == "program files (x86)" || n == "programdata" || n.StartsWith(".") || n == "node_modules") continue;
                bool likely = n.Contains("azeroth") || n.Contains("acore") || n.Contains("wow") || n.Contains("server") || n.Contains("core") || n.Contains("craft") || n.Contains("master") || n.Contains("games");
                if (!likely && depth < 2) continue;
                Walk(s, depth - 1, found, seen, max);
                if (found.Count >= max) return;
            }
        }

        /// <summary>Test hook: replaces the drives searched by Auto-detect.</summary>
        public static Func<IEnumerable<string>> RootsOverride;

        public static IEnumerable<string> DefaultRoots()
        {
            if (RootsOverride != null) { foreach (var r in RootsOverride()) yield return r; yield break; }
            foreach (var d in DriveInfo.GetDrives())
            {
                bool ok = false;
                try { ok = d.DriveType == DriveType.Fixed && d.IsReady; } catch { }
                if (ok) yield return d.RootDirectory.FullName;
            }
            yield return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
    }
}
