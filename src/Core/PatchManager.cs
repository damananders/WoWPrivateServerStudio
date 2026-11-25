using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace AndersCraftStudio.Core
{
    public sealed class PatchInfo
    {
        public string FileName, FullPath, Error;
        public long Size;
        public DateTime Modified;
        public HashSet<string> Files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public int Rank => PatchNames.Rank(FileName);
        public bool InstalledByStudio;
        /// <summary>Files whose name is unknown (no listfile entry and not recovered).</summary>
        public int Unnamed;
        public int FileCount;
    }

    /// <summary>The set of patch archives in a client's Data folder, and which one wins for each file.</summary>
    public static class PatchSet
    {
        /// <summary>Reads the name lists of every loadable patch in the Data folder (read-only).</summary>
        public static List<PatchInfo> Scan(string dataDir, string nameCacheDir = null)
        {
            var res = new List<PatchInfo>();
            if (!Directory.Exists(dataDir)) return res;
            foreach (var f in Directory.GetFiles(dataDir, "*.mpq"))
            {
                var name = Path.GetFileName(f);
                if (!PatchNames.IsLoadable(name, out _)) continue;
                var fi = new FileInfo(f);
                var p = new PatchInfo { FileName = name, FullPath = f, Size = fi.Length, Modified = fi.LastWriteTime };
                try
                {
                    using var r = new MpqReader(f);
                    NameRecovery.ApplyCache(nameCacheDir, f, r);
                    foreach (var e in r.Files) { if (e.Name != null && e.Name.StartsWith("(")) continue; p.FileCount++; if (e.Name != null) p.Files.Add(e.Name); else p.Unnamed++; }
                }
                catch (Exception ex) { p.Error = ex.Message; }
                res.Add(p);
            }
            return res.OrderBy(p => p.Rank).ToList();
        }

        public static PatchInfo Winner(IEnumerable<PatchInfo> patches, string path)
            => patches.Where(p => p.Files.Contains(path.Replace('/', '\\'))).OrderByDescending(p => p.Rank).FirstOrDefault();

        public static bool NameInUse(IEnumerable<PatchInfo> patches, string name) => patches.Any(p => string.Equals(p.FileName, name, StringComparison.OrdinalIgnoreCase) || p.Rank == PatchNames.Rank(name) && PatchNames.Rank(name) > 1);

        /// <summary>The highest free letter, so the new patch wins over as many others as possible.</summary>
        public static string SuggestFreeName(IEnumerable<PatchInfo> patches)
        {
            var used = new HashSet<int>(patches.Select(p => p.Rank));
            for (char c = 'Z'; c >= 'A'; c--) { var n = $"patch-{c}.MPQ"; if (!used.Contains(PatchNames.Rank(n))) return n; }
            for (char c = '9'; c >= '2'; c--) { var n = $"patch-{c}.MPQ"; if (!used.Contains(PatchNames.Rank(n))) return n; }
            return null;
        }

        /// <summary>Files of a new patch that another, higher-ranked installed patch would still override (path → that patch).</summary>
        public static Dictionary<string, string> Shadowed(IEnumerable<PatchInfo> installed, string newName, IEnumerable<string> paths)
        {
            int rank = PatchNames.Rank(newName);
            var res = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in paths)
            {
                var w = Winner(installed.Where(i => !string.Equals(i.FileName, newName, StringComparison.OrdinalIgnoreCase)), p);
                if (w != null && w.Rank > rank) res[p.Replace('/', '\\')] = w.FileName;
            }
            return res;
        }
    }

    /// <summary>Installing, uninstalling and exporting patches. Every change to the client folder is backed up and recorded.</summary>
    public sealed class PatchInstaller
    {
        public sealed class Record
        {
            public string Name { get; set; }
            public string DataDir { get; set; }
            public DateTime InstalledUtc { get; set; }
            public string Sha256 { get; set; }
            public string BackupOfPrevious { get; set; } // file that was there before (null if none)
        }

        readonly string stateFile;
        public string BackupRoot { get; }
        readonly Func<string, List<string>> running;
        public PatchInstaller(string stateFile, string backupRoot, Func<string, List<string>> runningCheck = null)
        { this.stateFile = stateFile; BackupRoot = backupRoot; running = runningCheck ?? RunningClients; }

        public List<Record> Records()
        {
            try { return File.Exists(stateFile) ? JsonSerializer.Deserialize<List<Record>>(File.ReadAllText(stateFile)) ?? new List<Record>() : new List<Record>(); }
            catch { return new List<Record>(); }
        }
        void Save(List<Record> r) { Directory.CreateDirectory(Path.GetDirectoryName(stateFile)); File.WriteAllText(stateFile, JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true })); }

        /// <summary>Processes that look like the game client: Wow/WoW, or any .exe that lives in the client folder.</summary>
        public static List<string> RunningClients(string clientDir)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Wow", "Wow-64", "WowB", "WowT" };
            try { if (Directory.Exists(clientDir)) foreach (var exe in Directory.GetFiles(clientDir, "*.exe")) names.Add(Path.GetFileNameWithoutExtension(exe)); } catch { }
            names.RemoveWhere(n => n.IndexOf("launcher", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("repair", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("BackgroundDownloader", StringComparison.OrdinalIgnoreCase) >= 0);
            var running = new List<string>();
            foreach (var p in Process.GetProcesses())
            {
                try { if (names.Contains(p.ProcessName)) running.Add(p.ProcessName + " (pid " + p.Id + ")"); } catch { }
                finally { p.Dispose(); }
            }
            return running;
        }

        public static string Sha256Of(string path) { using var s = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(s)); }

        /// <summary>Copies a built patch into Data\, backing up any file of the same name first. Refuses while the game runs.</summary>
        public Record Install(string builtMpq, string clientDir, string name, string localeFolder = null)
        {
            string why = null;
            if (localeFolder == null ? !PatchNames.IsLoadable(name, out why) : !PatchNames.IsLocaleLoadable(name, localeFolder, out why)) throw new InvalidOperationException(why);
            var running = this.running(clientDir);
            if (running.Count > 0) throw new InvalidOperationException("Close the game first: " + string.Join(", ", running) + " is running and has the patches open.");
            var data = Path.Combine(clientDir, "Data");
            if (!Directory.Exists(data)) throw new DirectoryNotFoundException("No Data folder in " + clientDir);
            if (localeFolder != null) { data = Path.Combine(data, localeFolder); if (!Directory.Exists(data)) throw new DirectoryNotFoundException("No " + localeFolder + " folder in " + Path.Combine(clientDir, "Data")); }
            var problems = MpqWriter.Verify(builtMpq, new List<(string, byte[])>());
            var target = Path.Combine(data, name);
            var existing = Directory.GetFiles(data, "*.mpq").FirstOrDefault(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase));
            string backup = null;
            if (existing != null)
            {
                var dir = Path.Combine(BackupRoot, "client_" + DateTime.Now.ToString("yyyy-MM-dd_HHmmss"));
                Directory.CreateDirectory(dir);
                backup = Path.Combine(dir, Path.GetFileName(existing));
                File.Copy(existing, backup, false);
                target = existing; // keep the original spelling
            }
            var tmp = target + ".studio-new";
            File.Copy(builtMpq, tmp, true);
            File.Move(tmp, target, true);
            var rec = new Record { Name = Path.GetFileName(target), DataDir = data, InstalledUtc = DateTime.UtcNow, Sha256 = Sha256Of(target), BackupOfPrevious = backup };
            var all = Records(); all.RemoveAll(r => string.Equals(r.Name, rec.Name, StringComparison.OrdinalIgnoreCase) && r.DataDir == data); all.Add(rec); Save(all);
            return rec;
        }

        /// <summary>Removes a patch Studio installed and puts back whatever was there before. Refuses if the file was changed by something else.</summary>
        public string Uninstall(string clientDir, string name)
        {
            var running = this.running(clientDir);
            if (running.Count > 0) throw new InvalidOperationException("Close the game first: " + string.Join(", ", running) + " is running.");
            var data = Path.Combine(clientDir, "Data");
            var all = Records();
            var rec = all.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase) && r.DataDir != null &&
                                              (string.Equals(r.DataDir, data, StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetDirectoryName(r.DataDir), data, StringComparison.OrdinalIgnoreCase)))
                      ?? throw new InvalidOperationException($"{name} wasn't installed by Studio, so Studio won't remove it.");
            var path = Path.Combine(rec.DataDir, rec.Name);
            if (File.Exists(path) && Sha256Of(path) != rec.Sha256) throw new InvalidOperationException($"{rec.Name} has changed since Studio installed it; remove it by hand if you're sure.");
            // keep a copy of what we remove, too
            if (File.Exists(path))
            {
                var dir = Path.Combine(BackupRoot, "client_" + DateTime.Now.ToString("yyyy-MM-dd_HHmmss") + "_uninstalled");
                Directory.CreateDirectory(dir);
                File.Move(path, Path.Combine(dir, rec.Name));
            }
            string msg = $"Removed {rec.Name}.";
            if (rec.BackupOfPrevious != null && File.Exists(rec.BackupOfPrevious)) { File.Copy(rec.BackupOfPrevious, path, false); msg += " Restored the previous version from the backup."; }
            all.Remove(rec); Save(all);
            return msg;
        }

        /// <summary>A zip players can unpack into their WoW folder: Data\&lt;patch&gt; plus a short how-to.</summary>
        public static void ExportZip(string builtMpq, string patchName, string zipPath, string notes = null)
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            using var z = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            z.CreateEntryFromFile(builtMpq, "Data/" + patchName, CompressionLevel.NoCompression);
            var e = z.CreateEntry("HOW TO INSTALL.txt");
            using var w = new StreamWriter(e.Open());
            w.WriteLine($"{patchName} for World of Warcraft 3.3.5a");
            w.WriteLine();
            w.WriteLine("1. Close the game.");
            w.WriteLine("2. Unzip this into your World of Warcraft folder (the one with Wow.exe),");
            w.WriteLine($"   so the file ends up as  Data\\{patchName}");
            w.WriteLine("3. Delete the Cache folder in your WoW folder, then start the game.");
            w.WriteLine();
            w.WriteLine($"To remove it: delete Data\\{patchName} and the Cache folder.");
            if (!string.IsNullOrWhiteSpace(notes)) { w.WriteLine(); w.WriteLine(notes); }
        }

        /// <summary>Extracts named files from an archive into a folder, keeping their paths. Returns (extracted, failures).</summary>
        public static (int ok, List<string> failed) Extract(string mpqPath, IEnumerable<string> paths, string outDir, string nameCacheDir = null)
        {
            int ok = 0; var failed = new List<string>();
            using var r = new MpqReader(mpqPath);
            NameRecovery.ApplyCache(nameCacheDir, mpqPath, r);
            var root = Path.GetFullPath(outDir);
            foreach (var p in paths)
            {
                try
                {
                    var rel = p.StartsWith("unknown #", StringComparison.OrdinalIgnoreCase) ? Path.Combine("unknown", p.Substring(8).Trim()) : p.Replace('/', '\\');
                    var dest = Path.GetFullPath(Path.Combine(root, rel));
                    if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("unsafe path");
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    File.WriteAllBytes(dest, r.Read(r.FindDisplay(p) ?? throw new FileNotFoundException("not in the archive")));
                    ok++;
                }
                catch (Exception ex) { failed.Add(p + ": " + ex.Message); }
            }
            return (ok, failed);
        }
    }
}
