using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using AndersCraftStudio.Core;

namespace AndersCraftStudio.Tests
{
    /// <summary>
    /// --dbc-roundtrip [serverDbcDir]: copies every DBC into a temp folder, then for each copy:
    ///   1. read it into the editable table model and write it back: the bytes must be identical;
    ///   2. write it with a freshly rebuilt string block, read that back: every value must be identical
    ///      (and we count how many are even byte-identical after the rebuild);
    ///   3. report which layout was used (published + verified, or guessed).
    /// Only the temp copies are read or written; the source folder is opened read-only for the copy.
    /// </summary>
    static class DbcRoundTrip
    {
        public static string DefaultDir => DevPaths.ServerDbc;

        public static int Run(string src)
        {
            var tmp = Path.Combine(Path.GetTempPath(), "studio_dbc_roundtrip_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tmp);
            try
            {
                foreach (var f in Directory.GetFiles(src, "*.dbc")) File.Copy(f, Path.Combine(tmp, Path.GetFileName(f)));
                var files = Directory.GetFiles(tmp, "*.dbc").OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                int ok = 0, rebuildSame = 0, rebuildByteSame = 0, catalog = 0, guessed = 0, rejected = 0;
                var failures = new List<string>();
                var empty = new List<string>();
                var sw = Stopwatch.StartNew();
                long bytes = 0;
                foreach (var path in files)
                {
                    var name = Path.GetFileName(path);
                    try
                    {
                        var orig = File.ReadAllBytes(path);
                        bytes += orig.Length;
                        if (orig.Length == 0) { empty.Add(name); continue; }
                        var t = DbcTable.Load(path);
                        if (t.Layout.Origin == "catalog") catalog++; else { guessed++; if (t.Layout.Note != null && DbcLayout.InCatalog(name)) { rejected++; Console.WriteLine($"  layout rejected  {name}: {t.Layout.Note}"); } }
                        var outPath = Path.Combine(tmp, name + ".out");
                        File.WriteAllBytes(outPath, t.Write());
                        var back = File.ReadAllBytes(outPath);
                        if (back.AsSpan().SequenceEqual(orig)) ok++;
                        else failures.Add($"{name}: written bytes differ (size {orig.Length} -> {back.Length})");
                        var rebuilt = t.Write(DbcStringMode.Rebuild);
                        var t2 = DbcTable.Read(name, rebuilt, t.Layout);
                        if (t.SameValues(t2, out var diff)) rebuildSame++;
                        else failures.Add($"{name}: rebuilt string block changed a value at {diff}");
                        if (rebuilt.AsSpan().SequenceEqual(orig)) rebuildByteSame++;
                        File.Delete(outPath);
                    }
                    catch (Exception ex) { failures.Add($"{name}: {ex.Message}"); }
                }
                Console.WriteLine();
                Console.WriteLine($"DBC files:                      {files.Count} ({bytes / 1024.0 / 1024.0:0.0} MB) in {sw.Elapsed.TotalSeconds:0.0}s");
                if (empty.Count > 0) Console.WriteLine($"Empty (0-byte) files, not DBC data: {string.Join(", ", empty)}");
                Console.WriteLine($"Read + write, byte-identical:   {ok}/{files.Count - empty.Count}");
                Console.WriteLine($"Rebuilt string block, same values: {rebuildSame}/{files.Count - empty.Count} (byte-identical even after rebuild: {rebuildByteSame})");
                Console.WriteLine($"Layouts: published+verified {catalog}, guessed {guessed} (published but didn't fit: {rejected})");
                Console.WriteLine($"Failures: {failures.Count}");
                foreach (var f in failures) Console.WriteLine("  " + f);
                return failures.Count;
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }
    }
}
