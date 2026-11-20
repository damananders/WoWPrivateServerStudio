using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using AndersCraftStudio.Core;

namespace AndersCraftStudio.Tests
{
    /// <summary>
    /// Read-only survey of a client's patch archives: compression types per file and, with --extract, a full extraction
    /// attempt of every file into memory (nothing is written anywhere).
    /// </summary>
    static class ClientScan
    {
        /// <summary>Name recovery for archives without a complete (listfile). Read-only: results go to the console (and a cache dir if given).</summary>
        public static int Recover(string dataDir, string serverDbcDir, string cacheDir)
        {
            foreach (var f in Directory.GetFiles(dataDir, "*.mpq").Where(f => PatchNames.IsLoadable(Path.GetFileName(f), out _)))
            {
                using var probe = new MpqReader(f);
                if (probe.Files.All(e => e.Name != null)) continue;
                var sw = Stopwatch.StartNew();
                var cands = NameRecovery.ClientCandidates(dataDir, serverDbcDir, f);
                var res = NameRecovery.Recover(probe, cands);
                Console.WriteLine($"{Path.GetFileName(f),-14} {res.Recovered}/{res.Total} named ({res.Percent:0.0}%) from {cands.Count:N0} candidates in {sw.ElapsedMilliseconds} ms");
                var unknown = probe.Files.Where(e => e.Name == null).ToList();
                var kinds = unknown.GroupBy(e => { try { return NameRecovery.GuessExtension(probe.Read(e)); } catch { return "?"; } }).Select(g => $"{(g.Key == "" ? "(no magic)" : g.Key)}={g.Count()}");
                if (unknown.Count > 0) Console.WriteLine("      still unknown by type: " + string.Join(", ", kinds));
                foreach (var n in res.Names.Values.Take(5)) Console.WriteLine("      e.g. " + n);
                if (cacheDir != null) NameRecovery.SaveCache(cacheDir, f, res);
            }
            return 0;
        }

        public static int Run(string dataDir, bool extract, bool all = false)
        {
            int totalFail = 0;
            var masks = new Dictionary<string, int>();
            foreach (var f in Directory.GetFiles(dataDir, "*.mpq", all ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly).Where(f => all || PatchNames.IsLoadable(Path.GetFileName(f), out _)).OrderBy(f => all ? 0 : PatchNames.Rank(Path.GetFileName(f))).ThenBy(f => f))
            {
                var sw = Stopwatch.StartNew();
                using var r = new MpqReader(f);
                var files = r.Files.ToList();
                int named = files.Count(x => x.Name != null), ok = 0, fail = 0;
                var failKinds = new Dictionary<string, int>();
                foreach (var e in files)
                {
                    string kind;
                    try { var (m, imp) = r.FirstSectorMask(e); kind = imp ? "file-implode" : "0x" + m.ToString("X2"); }
                    catch (Exception ex) { kind = "maskerr:" + ex.GetType().Name; }
                    masks[kind] = masks.TryGetValue(kind, out var c) ? c + 1 : 1;
                    if (!extract) continue;
                    try { r.Read(e); ok++; }
                    catch (Exception ex)
                    {
                        fail++;
                        var k = kind + " " + (ex.Message.Length > 60 ? ex.Message.Substring(0, 60) : ex.Message);
                        failKinds[k] = failKinds.TryGetValue(k, out var c2) ? c2 + 1 : 1;
                    }
                }
                totalFail += fail;
                Console.WriteLine($"{Path.GetFileName(f),-14} files {files.Count,6}  named {named,6} ({(files.Count == 0 ? 100 : 100.0 * named / files.Count):0.0}%)" + (extract ? $"  extracted {ok,6}  failed {fail,5}" : "") + $"  {sw.ElapsedMilliseconds} ms");
                foreach (var kv in failKinds.OrderByDescending(k => k.Value).Take(6)) Console.WriteLine($"      fail {kv.Value,5} × {kv.Key}");
            }
            Console.WriteLine("compression bytes (first sector): " + string.Join(", ", masks.OrderByDescending(k => k.Value).Select(k => $"{k.Key}={k.Value}")));
            return totalFail == 0 ? 0 : 1;
        }
    }
}
