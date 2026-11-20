using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AndersCraftStudio.Core
{
    /// <summary>
    /// Recovers file names for archives that have no (listfile). MPQs store only two hashes of each name, so names are
    /// found by hashing candidate paths and matching them against the hash table. Candidates come from other archives'
    /// listfiles, DBC names, paths written inside DBC string blocks, paths found inside the archive's own files (models,
    /// map tiles and WMOs list the textures and models they use), and standard folder patterns.
    /// </summary>
    public static class NameRecovery
    {
        public sealed class Result
        {
            public string Archive;
            public int Total, Recovered;
            public double Percent => Total == 0 ? 100 : 100.0 * Recovered / Total;
            public Dictionary<int, string> Names = new Dictionary<int, string>();
        }

        /// <summary>Tries every candidate against the archive's unnamed entries and names those that match.</summary>
        public static Result Recover(MpqReader r, IEnumerable<string> candidates, int contentRounds = 3)
        {
            var res = new Result();
            var wanted = new Dictionary<(uint, uint), int>();
            foreach (var (a, b, block) in r.HashEntries())
                if (r.Blocks[block].Name == null) wanted[(a, b)] = block;
            var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            int Try(IEnumerable<string> names)
            {
                int hits = 0;
                foreach (var raw in names)
                {
                    if (wanted.Count == 0) break;
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    var n = raw.Trim().Replace('/', '\\').TrimStart('\\');
                    if (n.Length > 250 || !tried.Add(n)) continue;
                    var key = (MpqCrypto.Hash(n, MpqCrypto.NameA), MpqCrypto.Hash(n, MpqCrypto.NameB));
                    if (wanted.TryGetValue(key, out var block))
                    {
                        r.Blocks[block].Name = n; res.Names[block] = n; wanted.Remove(key); hits++;
                    }
                }
                return hits;
            }

            Try(new[] { "(attributes)", "(signature)", "(listfile)" });
            Try(candidates.SelectMany(Variants));
            // names found inside the archive's own files lead to more names (models → textures → ...)
            for (int round = 0; round < contentRounds && wanted.Count > 0; round++)
            {
                var found = new List<string>();
                foreach (var e in r.Files)
                {
                    if (e.FileSize > 64 * 1024 * 1024) continue;
                    byte[] d;
                    try { d = r.Read(e); } catch { continue; }
                    found.AddRange(PathsInside(d));
                    if (e.Name != null) found.AddRange(Siblings(e.Name));
                }
                if (Try(found.SelectMany(Variants)) == 0) break;
            }
            // last resort: every known texture / skin file name, tried in every folder this archive already uses
            if (wanted.Count > 0)
            {
                var dirs = r.Files.Where(e => e.Name != null && !IsSpecial(e)).Select(e => Path.GetDirectoryName(e.Name)).Where(d => !string.IsNullOrEmpty(d)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var bases = candidates.Where(c => c.EndsWith(".blp", StringComparison.OrdinalIgnoreCase) || c.EndsWith(".skin", StringComparison.OrdinalIgnoreCase) || c.EndsWith(".m2", StringComparison.OrdinalIgnoreCase))
                                      .Select(Path.GetFileName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if ((long)dirs.Count * bases.Count <= 60_000_000)
                    Try(dirs.SelectMany(d => bases.Select(b => d + "\\" + b)));
            }
            res.Total = r.Files.Count(e => !IsSpecial(e));
            res.Recovered = r.Files.Count(e => !IsSpecial(e) && e.Name != null);
            return res;
        }

        static bool IsSpecial(MpqEntry e) => e.Name != null && e.Name.StartsWith("(");

        /// <summary>A path plus the forms the client actually stores (.mdx/.mdl → .m2 and its .skin files, textures without extension → .blp).</summary>
        public static IEnumerable<string> Variants(string p)
        {
            p = p.Trim().Replace('/', '\\');
            yield return p;
            var ext = Path.GetExtension(p).ToLowerInvariant();
            var stem = ext.Length > 0 ? p.Substring(0, p.Length - ext.Length) : p;
            if (ext == ".mdx" || ext == ".mdl" || ext == ".m2")
            {
                yield return stem + ".m2";
                for (int i = 0; i < 4; i++) yield return stem + i.ToString("00") + ".skin";
            }
            if (ext.Length == 0) { yield return p + ".blp"; yield return p + ".m2"; yield return p + ".wav"; yield return p + ".mp3"; }
            if (ext == ".blp" || ext == ".tga") yield return stem + ".blp";
            if (ext == ".wmo")
                for (int i = 0; i < 64; i++) yield return stem + "_" + i.ToString("000") + ".wmo";
        }

        /// <summary>Files that commonly sit next to a known one (map tiles of the same map, WMO groups, model skins).</summary>
        static IEnumerable<string> Siblings(string name)
        {
            var ext = Path.GetExtension(name).ToLowerInvariant();
            if (ext == ".wdt" || ext == ".adt" || ext == ".wdl")
            {
                var dir = Path.GetDirectoryName(name);
                var map = Path.GetFileName(dir);
                if (map == null) yield break;
                yield return Path.Combine(dir, map + ".wdt"); yield return Path.Combine(dir, map + ".wdl");
                foreach (var t in MapTiles(dir, map)) yield return t;
            }
        }

        public static IEnumerable<string> MapTiles(string dir, string map)
        {
            for (int x = 0; x < 64; x++) for (int y = 0; y < 64; y++) yield return $"{dir}\\{map}_{x}_{y}.adt";
        }

        static readonly Regex PathLike = new Regex(@"[A-Za-z0-9_\-\s\.\(\)'&]+(\\[A-Za-z0-9_\-\s\.\(\)'&]+)+\.(blp|m2|mdx|mdl|wmo|wav|mp3|ogg|tga|skin|adt|wdt|wdl|dbc|lua|xml|toc|ttf|anim|bls|zmp)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Path-looking strings inside a file (NUL-separated string blocks in M2, WMO, ADT and DBC files).</summary>
        public static IEnumerable<string> PathsInside(byte[] d)
        {
            var sb = new StringBuilder();
            var res = new List<string>();
            void Flush()
            {
                if (sb.Length >= 6)
                {
                    var s = sb.ToString();
                    foreach (Match m in PathLike.Matches(s.Replace('/', '\\'))) res.Add(m.Value.Trim());
                }
                sb.Clear();
            }
            foreach (var b in d)
            {
                if (b >= 0x20 && b < 0x7F) sb.Append((char)b);
                else Flush();
                if (sb.Length > 300) sb.Clear();
            }
            Flush();
            return res;
        }

        /// <summary>Strings from a DBC's string block that look like file paths or icon names.</summary>
        public static IEnumerable<string> DbcPaths(string dbcName, byte[] dbc)
        {
            DbcFile f;
            try { f = new DbcFile(dbc); } catch { yield break; }
            int strStart = 20 + f.Records * f.RecordSize;
            var all = Encoding.UTF8.GetString(dbc, strStart, dbc.Length - strStart).Split('\0');
            bool icons = dbcName.Equals("ItemDisplayInfo.dbc", StringComparison.OrdinalIgnoreCase);
            foreach (var s in all)
            {
                if (s.Length < 3 || s.Length > 200) continue;
                if (s.Contains('\\') || s.Contains('/')) yield return s;
                else if (icons) { yield return "Interface\\Icons\\" + s + ".blp"; yield return "Item\\ObjectComponents\\Weapon\\" + s + ".blp"; }
            }
        }

        /// <summary>Every candidate Studio can think of for a client: other archives' file lists, server DBC names and
        /// the paths inside them, and every map's tile names.</summary>
        public static List<string> ClientCandidates(string dataDir, string serverDbcDir, string exceptArchive = null)
        {
            var c = new List<string>();
            foreach (var f in Directory.GetFiles(dataDir, "*.mpq", SearchOption.AllDirectories))
            {
                if (exceptArchive != null && string.Equals(Path.GetFullPath(f), Path.GetFullPath(exceptArchive), StringComparison.OrdinalIgnoreCase)) continue;
                try { using var r = new MpqReader(f); foreach (var e in r.Files) if (e.Name != null && !e.Name.StartsWith("(")) c.Add(e.Name); } catch { }
            }
            if (serverDbcDir != null && Directory.Exists(serverDbcDir))
                foreach (var f in Directory.GetFiles(serverDbcDir, "*.dbc"))
                {
                    var n = Path.GetFileName(f);
                    c.Add("DBFilesClient\\" + n);
                    byte[] d;
                    try { d = File.ReadAllBytes(f); } catch { continue; }
                    c.AddRange(DbcPaths(n, d));
                    if (n.Equals("Map.dbc", StringComparison.OrdinalIgnoreCase))
                        try
                        {
                            var map = new DbcFile(d);
                            for (int i = 0; i < map.Records; i++)
                            {
                                var dir = map.Str(i, 1);
                                if (dir.Length == 0) continue;
                                var root = "World\\Maps\\" + dir;
                                c.Add(root + "\\" + dir + ".wdt"); c.Add(root + "\\" + dir + ".wdl");
                                c.AddRange(MapTiles(root, dir));
                            }
                        }
                        catch { }
                }
            return c;
        }

        // ------------------------------------------------------------------ cache of recovered names (one text file per archive)
        public static string CacheFile(string cacheDir, string mpqPath)
        {
            var fi = new FileInfo(mpqPath);
            return Path.Combine(cacheDir, $"{fi.Name}_{fi.Length}.listfile.txt");
        }

        public static void SaveCache(string cacheDir, string mpqPath, Result res)
        {
            Directory.CreateDirectory(cacheDir);
            File.WriteAllLines(CacheFile(cacheDir, mpqPath), res.Names.Values.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>Names a reader's entries from a saved recovery (if one exists for this exact archive size).</summary>
        public static int ApplyCache(string cacheDir, string mpqPath, MpqReader r)
        {
            if (cacheDir == null) return 0;
            var f = CacheFile(cacheDir, mpqPath);
            if (!File.Exists(f)) return 0;
            int n = 0;
            foreach (var line in File.ReadAllLines(f)) if (r.Name(line.Trim())) n++;
            return n;
        }

        /// <summary>A file type guess from its first bytes, for naming files that stay unknown.</summary>
        public static string GuessExtension(byte[] d)
        {
            if (d.Length < 4) return "";
            string m4 = Encoding.ASCII.GetString(d, 0, 4);
            switch (m4)
            {
                case "BLP2": case "BLP1": return ".blp";
                case "MD20": return ".m2";
                case "SKIN": return ".skin";
                case "WDBC": return ".dbc";
                case "RIFF": return ".wav";
                case "OggS": return ".ogg";
                case "MPQ\x1A": return ".mpq";
                case "REVM":
                    if (d.Length >= 16) { var c = Encoding.ASCII.GetString(d, 12, 4); if (c == "DHOM" || c == "PGOM") return ".wmo"; if (c == "DHPM") return ".wdt"; if (c == "RDHM") return ".adt"; }
                    return ".chunked";
            }
            if (d[0] == 'I' && d[1] == 'D' && d[2] == '3' || d[0] == 0xFF && (d[1] & 0xE0) == 0xE0) return ".mp3";
            if (m4 == "<Ui " || m4.StartsWith("<?xm") || m4.StartsWith("<Ui")) return ".xml";
            return "";
        }
    }
}
