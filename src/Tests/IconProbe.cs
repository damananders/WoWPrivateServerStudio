using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using AndersCraftStudio.Core;

namespace AndersCraftStudio.Tests
{
    /// <summary>--icons [clientData]: decodes every icon SpellIcon.dbc names, from the client's archives (read-only). Reports counts and failures.</summary>
    static class IconProbe
    {
        public static int Run(string data)
        {
            var sw = Stopwatch.StartNew();
            using var cf = new ClientFiles(data);
            var t = DbcTable.Load(Path.Combine(DbcRoundTrip.DefaultDir, "SpellIcon.dbc"));
            var cache = new IconCache(p => cf.Read(p, out _));
            int ok = 0, missing = 0, bad = 0; var kinds = new System.Collections.Generic.Dictionary<string, int>();
            foreach (var r in t.Rows)
            {
                var tex = r.S[1];
                if (string.IsNullOrEmpty(tex)) continue;
                var b = cf.Read(IconCache.BlpPath(tex), out _);
                if (b == null) { missing++; continue; }
                var k = $"comp{b[8]} alpha{b[9]} type{b[10]}"; kinds.TryGetValue(k, out var kc); kinds[k] = kc + 1;
                try { var img = Blp.Decode(b); if (img.Width > 0 && img.Pixels.Any(x => x != 0)) ok++; else bad++; }
                catch (Exception ex) { bad++; if (bad < 5) Console.WriteLine("  " + tex + ": " + ex.Message); }
            }
            var first = cache.Get(t.Rows[1].S[1]); var t1 = sw.Elapsed;
            Console.WriteLine($"{cf.ArchiveCount} archives; icons: {ok} decoded, {missing} not in the client, {bad} failed; {sw.Elapsed.TotalSeconds:0.0}s");
            foreach (var kv in kinds) Console.WriteLine($"  {kv.Key}: {kv.Value}");
            return bad;
        }
    }
}
