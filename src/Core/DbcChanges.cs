using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace AndersCraftStudio.Core
{
    public sealed class DbcFieldEdit
    {
        public int Field { get; set; }
        public string Name { get; set; }
        /// <summary>Canonical text before (null for a new row) and after (null for a removed row).</summary>
        public string Before { get; set; }
        public string After { get; set; }
    }

    /// <summary>A staged change to one DBC row. Lives on Change.Dbc; the Change has Schema "dbc" and Table = file name.</summary>
    public sealed class DbcPatch
    {
        public string File { get; set; }
        /// <summary>Which copy the edit was made against ("server" / "client"), for the record.</summary>
        public string Source { get; set; }
        public bool ById { get; set; } = true;
        public uint Key { get; set; }
        public int FieldCount { get; set; }
        public List<DbcFieldEdit> Fields { get; set; } = new List<DbcFieldEdit>();
    }

    public static class DbcStager
    {
        public const string Schema = "dbc";

        public static bool IsDbc(Change c) => c.Schema == Schema && c.Dbc != null;

        /// <summary>Row changes that turn baseTable into edited (both loaded with the same layout).</summary>
        public static List<Change> Diff(DbcTable baseTable, DbcTable edited, string source)
        {
            var res = new List<Change>();
            var l = baseTable.Layout;
            var nameCol = l.NameColumn;
            if (l.HasId)
            {
                var editedKeys = new HashSet<uint>();
                foreach (var r in edited.Rows)
                {
                    uint key = r.V[0];
                    editedKeys.Add(key);
                    var b = baseTable.Find(key);
                    if (b == null) res.Add(Make(ChangeKind.Insert, baseTable, null, edited, r, key, source, nameCol));
                    else if (SameRow(b, r)) continue;
                    else { var c = Make(ChangeKind.Update, baseTable, b, edited, r, key, source, nameCol); if (c != null) res.Add(c); }
                }
                foreach (var b in baseTable.Rows)
                    if (!editedKeys.Contains(b.V[0])) res.Add(Make(ChangeKind.Delete, baseTable, b, edited, null, b.V[0], source, nameCol));
            }
            else
            {
                int n = Math.Max(baseTable.Rows.Count, edited.Rows.Count);
                for (int i = 0; i < n; i++)
                {
                    var b = i < baseTable.Rows.Count ? baseTable.Rows[i] : null;
                    var e = i < edited.Rows.Count ? edited.Rows[i] : null;
                    var kind = b == null ? ChangeKind.Insert : e == null ? ChangeKind.Delete : ChangeKind.Update;
                    var c = Make(kind, baseTable, b, edited, e, (uint)i, source, nameCol);
                    if (c != null) res.Add(c);
                }
                // removing rows by position must go from the end
                var dels = res.Where(c => c.Kind == ChangeKind.Delete).OrderByDescending(c => c.Dbc.Key).ToList();
                res.RemoveAll(c => c.Kind == ChangeKind.Delete);
                res.AddRange(dels);
            }
            return res;
        }

        /// <summary>Fast check: identical numbers and text (most rows of a big table).</summary>
        static bool SameRow(DbcRow a, DbcRow b)
        {
            if (a.V.Length != b.V.Length) return false;
            for (int i = 0; i < a.V.Length; i++)
                if (a.S[i] != null || b.S[i] != null) { if ((a.S[i] ?? "") != (b.S[i] ?? "")) return false; }
                else if (a.V[i] != b.V[i]) return false;
            return true;
        }

        static Change Make(ChangeKind kind, DbcTable bt, DbcRow b, DbcTable et, DbcRow e, uint key, string source, DbcColumn nameCol)
        {
            var l = bt.Layout;
            var p = new DbcPatch { File = bt.File, Source = source, ById = l.HasId, Key = key, FieldCount = l.FieldCount };
            for (int i = 0; i < l.FieldCount; i++)
            {
                var before = b == null ? null : bt.FieldText(b, i);
                var after = e == null ? null : et.FieldText(e, i);
                if (kind == ChangeKind.Update && before == after) continue;
                p.Fields.Add(new DbcFieldEdit { Field = i, Name = l.FieldName(i), Before = before, After = after });
            }
            if (kind == ChangeKind.Update && p.Fields.Count == 0) return null;
            var row = e ?? b;
            var label = nameCol != null ? (row.S[nameCol.Field] ?? "") : "";
            var keyText = l.HasId ? key.ToString(CultureInfo.InvariantCulture) : "row " + key;
            var what = kind == ChangeKind.Insert ? "Add" : kind == ChangeKind.Delete ? "Remove" : "Edit";
            var c = new Change
            {
                Kind = kind, Schema = Schema, Table = bt.File, Category = "Client data (DBC)",
                Summary = $"{what} {bt.File} {keyText}" + (label.Length > 0 ? $" ({Trim(label)})" : "") + (kind == ChangeKind.Update ? ": " + string.Join(", ", p.Fields.Take(3).Select(f => f.Name)) + (p.Fields.Count > 3 ? $" +{p.Fields.Count - 3}" : "") : ""),
                Key = new List<KeyPart> { new KeyPart(l.KeyName, key.ToString(CultureInfo.InvariantCulture)) },
                Dbc = p, UndoAvailable = true, UndoNote = "Undo puts the old values back in the file.",
            };
            foreach (var f in p.Fields.Where(f => kind == ChangeKind.Update || !string.IsNullOrEmpty(f.After ?? f.Before) && (f.After ?? f.Before) != "0" && (f.After ?? f.Before) != "0x0"))
                c.Fields.Add(new FieldChange { Name = f.Name, Before = f.Before, After = f.After, BeforeDisplay = f.Before, AfterDisplay = f.After });
            return c;
        }

        static string Trim(string s) => s.Length > 40 ? s.Substring(0, 40) + "…" : s;

        /// <summary>Replaces the staged changes for one file with a fresh diff. Returns how many changes the file now has.</summary>
        public static int Restage(Changeset pending, DbcTable baseTable, DbcTable edited, string source)
        {
            pending.Changes.RemoveAll(c => IsDbc(c) && string.Equals(c.Table, baseTable.File, StringComparison.OrdinalIgnoreCase));
            var add = Diff(baseTable, edited, source);
            pending.Changes.AddRange(add);
            return add.Count;
        }
    }

    public static class DbcPatcher
    {
        /// <summary>
        /// Applies (or undoes) staged row changes to a copy of the table. Every row is checked first: a field must hold the
        /// staged "before" value (or already the "after" value, which is then skipped). Returns null with problems if any check fails.
        /// </summary>
        public static DbcTable Apply(DbcTable table, IEnumerable<Change> changes, bool undo, List<string> problems, List<string> notes = null)
        {
            var t = table.Clone();
            var list = changes.Where(DbcStager.IsDbc).Where(c => string.Equals(c.Table, t.File, StringComparison.OrdinalIgnoreCase)).ToList();
            if (undo) list.Reverse();
            int before = problems.Count;
            foreach (var c in list)
            {
                var p = c.Dbc;
                if (p.FieldCount != 0 && p.FieldCount != t.Layout.FieldCount) { problems.Add($"{c.Summary}: {t.File} has {t.Layout.FieldCount} fields here but {p.FieldCount} when staged."); continue; }
                var kind = c.Kind;
                if (undo) kind = kind == ChangeKind.Insert ? ChangeKind.Delete : kind == ChangeKind.Delete ? ChangeKind.Insert : kind;
                string Exp(DbcFieldEdit f) => undo ? f.After : f.Before;
                string Tgt(DbcFieldEdit f) => undo ? f.Before : f.After;
                string where = $"{t.File} {(p.ById ? "ID " + p.Key : "row " + p.Key)}";
                var row = t.Find(p.Key);
                switch (kind)
                {
                    case ChangeKind.Update:
                        if (row == null) { problems.Add($"{where} doesn't exist in this copy."); break; }
                        var set = new List<DbcFieldEdit>();
                        foreach (var f in p.Fields)
                        {
                            var cur = t.FieldText(row, f.Field);
                            if (cur == Tgt(f)) continue;
                            if (cur != Exp(f)) { problems.Add($"{where} {f.Name} is {Show(cur)} here, not {Show(Exp(f))} as when staged."); continue; }
                            set.Add(f);
                        }
                        if (set.Count == 0) notes?.Add($"{where}: already has these values.");
                        foreach (var f in set) if (!t.TrySetField(row, f.Field, Tgt(f), out var err)) problems.Add($"{where} {f.Name}: {err}");
                        break;
                    case ChangeKind.Insert:
                        if (row != null)
                        {
                            if (p.Fields.All(f => t.FieldText(row, f.Field) == (Tgt(f) ?? ""))) { notes?.Add($"{where}: already there."); break; }
                            problems.Add($"{where} already exists with other values."); break;
                        }
                        if (!p.ById && p.Key != t.Rows.Count) { problems.Add($"{where}: rows without an ID can only be added at the end."); break; }
                        var nr = t.NewRow();
                        foreach (var f in p.Fields) if (Tgt(f) != null && !t.TrySetField(nr, f.Field, Tgt(f), out var err)) problems.Add($"{where} {f.Name}: {err}");
                        t.Rows.Add(nr); t.InvalidateIndex();
                        break;
                    case ChangeKind.Delete:
                        if (row == null) { notes?.Add($"{where}: already gone."); break; }
                        var diff = p.Fields.Where(f => Exp(f) != null && t.FieldText(row, f.Field) != Exp(f)).ToList();
                        if (diff.Count > 0) { problems.Add($"{where} {diff[0].Name} is {Show(t.FieldText(row, diff[0].Field))} here, not {Show(Exp(diff[0]))} as when staged."); break; }
                        if (!p.ById && p.Key != t.Rows.Count - 1) { problems.Add($"{where}: rows without an ID can only be removed from the end."); break; }
                        t.Rows.Remove(row); t.InvalidateIndex();
                        break;
                }
            }
            return problems.Count > before ? null : t;
        }

        static string Show(string s) => s == null ? "(no row)" : "\"" + (s.Length > 30 ? s.Substring(0, 30) + "…" : s) + "\"";
    }

    public sealed class DbcOutputResult
    {
        public bool Success;
        public List<string> Problems = new List<string>();
        public List<string> Log = new List<string>();
        public List<string> Written = new List<string>();
        public string BackupDir;
    }

    /// <summary>
    /// The two ways staged DBC changes leave Studio. Both check every file first and write nothing if any row conflicts.
    ///   ToPatchFolder: patched copies go to a Client patches work folder (DBFilesClient\…), ready to build a patch-X.MPQ.
    ///   ToServerCopy:  the server's Data\dbc files are backed up, then replaced by patched copies (worldserver must restart).
    /// </summary>
    public static class DbcOutputs
    {
        static Dictionary<string, DbcTable> Patch(IList<Change> changes, Func<string, byte[]> baseReader, DbcOutputResult res)
        {
            var outp = new Dictionary<string, DbcTable>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in changes.Where(DbcStager.IsDbc).Select(c => c.Table).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                byte[] bytes;
                try { bytes = baseReader(file); } catch (Exception ex) { res.Problems.Add($"{file}: can't read it ({ex.Message})."); continue; }
                if (bytes == null) { res.Problems.Add($"{file}: not found in the source."); continue; }
                var t = DbcTable.Read(file, bytes);
                var notes = new List<string>();
                var patched = DbcPatcher.Apply(t, changes, false, res.Problems, notes);
                res.Log.AddRange(notes);
                if (patched != null) { outp[file] = patched; res.Log.Add($"{file}: {changes.Count(c => DbcStager.IsDbc(c) && string.Equals(c.Table, file, StringComparison.OrdinalIgnoreCase))} row change(s) applied."); }
            }
            return outp;
        }

        public static DbcOutputResult ToPatchFolder(IList<Change> changes, Func<string, byte[]> baseReader, string workFolder)
        {
            var res = new DbcOutputResult();
            var tables = Patch(changes, baseReader, res);
            if (res.Problems.Count > 0) { res.Log.Add("Nothing was written."); return res; }
            var dir = Path.Combine(workFolder, "DBFilesClient");
            Directory.CreateDirectory(dir);
            foreach (var kv in tables)
            {
                var path = Path.Combine(dir, kv.Key);
                WriteAtomic(path, kv.Value.Write());
                res.Written.Add(path);
            }
            res.Success = true;
            res.Log.Add($"Wrote {res.Written.Count} file(s) to {dir}. Build the patch on the Client patches screen.");
            return res;
        }

        public static DbcOutputResult ToServerCopy(IList<Change> changes, string serverDbcDir, string backupRoot, DateTime? now = null)
        {
            var res = new DbcOutputResult();
            if (!Directory.Exists(serverDbcDir)) { res.Problems.Add("The server's dbc folder doesn't exist: " + serverDbcDir); return res; }
            var tables = Patch(changes, f => { var p = Path.Combine(serverDbcDir, f); return File.Exists(p) ? File.ReadAllBytes(p) : null; }, res);
            if (res.Problems.Count > 0) { res.Log.Add("Nothing was written."); return res; }
            var stamp = (now ?? DateTime.Now).ToString("yyyy-MM-dd_HHmmss");
            res.BackupDir = Path.Combine(backupRoot, "dbc_" + stamp);
            Directory.CreateDirectory(res.BackupDir);
            foreach (var kv in tables)
                File.Copy(Path.Combine(serverDbcDir, kv.Key), Path.Combine(res.BackupDir, kv.Key), false);
            File.WriteAllText(Path.Combine(res.BackupDir, "restore_to.txt"), serverDbcDir);
            res.Log.Add($"Backed up {tables.Count} file(s) to {res.BackupDir}.");
            foreach (var kv in tables)
            {
                var path = Path.Combine(serverDbcDir, kv.Key);
                WriteAtomic(path, kv.Value.Write());
                res.Written.Add(path);
            }
            res.Success = true;
            res.Log.Add($"Updated {res.Written.Count} server DBC file(s). Restart worldserver to load them.");
            return res;
        }

        /// <summary>Puts the files of a server-copy backup back (the undo of ToServerCopy).</summary>
        public static DbcOutputResult RestoreServerCopy(string backupDir, string serverDbcDir)
        {
            var res = new DbcOutputResult { BackupDir = backupDir };
            if (!Directory.Exists(backupDir)) { res.Problems.Add("Backup folder not found: " + backupDir); return res; }
            foreach (var f in Directory.GetFiles(backupDir, "*.dbc"))
            {
                var dest = Path.Combine(serverDbcDir, Path.GetFileName(f));
                WriteAtomic(dest, File.ReadAllBytes(f));
                res.Written.Add(dest);
            }
            res.Success = true;
            res.Log.Add($"Restored {res.Written.Count} file(s) from {backupDir}. Restart worldserver to load them.");
            return res;
        }

        static void WriteAtomic(string path, byte[] bytes)
        {
            var tmp = path + ".studio-tmp";
            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, path, true);
        }
    }

    // ---------- sources ----------
    public interface IDbcSource
    {
        string Label { get; }
        IReadOnlyList<string> Files { get; }
        byte[] Read(string file);
        /// <summary>Where this file comes from (folder or archive name).</summary>
        string OriginOf(string file);
    }

    public sealed class FolderDbcSource : IDbcSource
    {
        readonly string dir;
        public FolderDbcSource(string dir, string label) { this.dir = dir; Label = label; }
        public string Label { get; }
        public string Folder => dir;
        public IReadOnlyList<string> Files => Directory.Exists(dir) ? Directory.GetFiles(dir, "*.dbc").Select(Path.GetFileName).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList() : new List<string>();
        public byte[] Read(string file) { var p = Path.Combine(dir, file); return File.Exists(p) ? File.ReadAllBytes(p) : null; }
        public string OriginOf(string file) => Path.Combine(dir, file);
    }

    /// <summary>
    /// "What the client really sees": every DBFilesClient\*.dbc in the client's archives, taken from the archive that wins.
    /// Load order model (later wins): common, common-2, expansion, lichking, locale archives, then patch, patch-xxXX,
    /// patch-2, patch-xxXX-2, … patch-9, then patch-A … patch-Z (each followed by its locale twin).
    /// </summary>
    public sealed class ClientDbcSource : IDbcSource
    {
        public sealed class Archive { public string Path; public string Name; public int Rank; public HashSet<string> Dbcs = new HashSet<string>(StringComparer.OrdinalIgnoreCase); public string Error; }

        public string DataDir { get; }
        public List<Archive> Archives { get; } = new List<Archive>();
        readonly Dictionary<string, Archive> winner = new Dictionary<string, Archive>(StringComparer.OrdinalIgnoreCase);

        public ClientDbcSource(string dataDir, string nameCacheDir = null)
        {
            DataDir = dataDir;
            if (!Directory.Exists(dataDir)) return;
            var files = Directory.GetFiles(dataDir, "*.mpq").ToList();
            foreach (var sub in Directory.GetDirectories(dataDir)) files.AddRange(Directory.GetFiles(sub, "*.mpq"));
            foreach (var f in files)
            {
                int rank = Rank(System.IO.Path.GetFileName(f), !string.Equals(System.IO.Path.GetDirectoryName(f), dataDir.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase));
                if (rank < 0) continue;
                var a = new Archive { Path = f, Name = System.IO.Path.GetRelativePath(dataDir, f), Rank = rank };
                try
                {
                    using var r = new MpqReader(f);
                    NameRecovery.ApplyCache(nameCacheDir, f, r);
                    foreach (var e in r.Files)
                        if (e.Name != null && e.Name.StartsWith("DBFilesClient\\", StringComparison.OrdinalIgnoreCase) && e.Name.EndsWith(".dbc", StringComparison.OrdinalIgnoreCase))
                            a.Dbcs.Add(System.IO.Path.GetFileName(e.Name));
                }
                catch (Exception ex) { a.Error = ex.Message; }
                Archives.Add(a);
            }
            Archives.Sort((x, y) => x.Rank != y.Rank ? x.Rank.CompareTo(y.Rank) : string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase));
            foreach (var a in Archives) foreach (var d in a.Dbcs) winner[d] = a;
        }

        /// <summary>Load rank; higher wins. -1 for archives the client doesn't load as game data.</summary>
        public static int Rank(string name, bool inLocaleFolder)
        {
            var n = System.IO.Path.GetFileNameWithoutExtension(name).ToLowerInvariant();
            if (!inLocaleFolder)
            {
                switch (n) { case "common": return 1; case "common-2": return 2; case "expansion": return 3; case "lichking": return 4; }
                if (PatchNames.IsLoadable(name, out _)) return 100 + 2 * PatchNames.Rank(name);
                return -1;
            }
            if (n.StartsWith("locale-") || n.StartsWith("speech-")) return 10;
            if (n.StartsWith("expansion-")) return 11;
            if (n.StartsWith("lichking-")) return 12;
            if (n.StartsWith("patch-"))
            {
                var parts = n.Split('-');           // patch, enus[, N]
                if (parts.Length == 2) return 100 + 2 * 1 + 1;
                if (parts.Length == 3 && parts[2].Length == 1) return 100 + 2 * PatchNames.Rank("patch-" + parts[2] + ".mpq") + 1;
            }
            return -1; // base-xx / backup-xx are launcher archives
        }

        public string Label => "Client (what the game sees)";
        public IReadOnlyList<string> Files => winner.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        public string OriginOf(string file) => winner.TryGetValue(file, out var a) ? a.Name : null;
        /// <summary>Every archive that has the file, lowest to highest priority.</summary>
        public List<string> ArchivesWith(string file) => Archives.Where(a => a.Dbcs.Contains(file)).Select(a => a.Name).ToList();

        public byte[] Read(string file) => winner.TryGetValue(file, out var a) ? ReadFrom(a, file) : null;

        public byte[] ReadFrom(Archive a, string file)
        {
            using var r = new MpqReader(a.Path);
            return r.Read("DBFilesClient\\" + file);
        }
    }
}
