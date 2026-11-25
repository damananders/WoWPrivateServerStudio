using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AndersCraftStudio.Core
{
    /// <summary>
    /// Changesets live as JSON files in one folder:
    ///   pending.json            – what you have staged and not applied yet
    ///   applied_&lt;id&gt;.json       – each successful Apply (shown in History, can be rolled back)
    /// </summary>
    public sealed class ChangesetStore
    {
        public string Folder { get; }
        static readonly JsonSerializerOptions JO = new JsonSerializerOptions { WriteIndented = true };
        public Changeset Pending { get; private set; }

        public ChangesetStore(string folder)
        {
            Folder = folder;
            Directory.CreateDirectory(folder);
            Pending = LoadFile(PendingPath);
            if (Pending == null && File.Exists(PendingPath))
            {
                // the file is there but can't be read (e.g. the PC lost power mid-write): keep it and fall back to the last good copy
                var keep = Path.Combine(folder, $"pending.damaged-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                try { File.Copy(PendingPath, keep, true); } catch { }
                Pending = LoadFile(PendingPath + ".bak");
                RecoveryNote = Pending != null
                    ? $"Your staged changes file was damaged, so Studio restored the previous copy ({Pending.Changes.Count} change(s)). The damaged file is kept as {Path.GetFileName(keep)}."
                    : $"Your staged changes file was damaged and no earlier copy could be read. It is kept as {Path.GetFileName(keep)}; Studio started with an empty list.";
            }
            Pending ??= new Changeset { Name = "Staged changes" };
            Pending.FilePath = PendingPath;
            InferDbcSent();
        }

        public string PendingPath => Path.Combine(Folder, "pending.json");
        /// <summary>Set when the staged-changes file had to be recovered at start-up (shown to the user once).</summary>
        public string RecoveryNote { get; private set; }

        public static Changeset LoadFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var cs = JsonSerializer.Deserialize<Changeset>(File.ReadAllText(path), JO);
                if (cs != null) { cs.FilePath = path; cs.Changes ??= new List<Change>(); cs.Log ??= new List<ApplyRecord>(); }
                return cs;
            }
            catch { return null; }
        }

        public void Save(Changeset cs)
        {
            cs.FilePath ??= Path.Combine(Folder, "applied_" + cs.Id + ".json");
            var tmp = cs.FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(cs, JO));
            // swap the new file in and keep the previous one as .bak (both steps are all-or-nothing on NTFS)
            if (File.Exists(cs.FilePath)) File.Replace(tmp, cs.FilePath, cs.FilePath + ".bak", true);
            else File.Move(tmp, cs.FilePath, true);
        }

        public void SavePending() => Save(Pending);

        public List<Changeset> History() =>
            Directory.GetFiles(Folder, "applied_*.json").Select(LoadFile).Where(x => x != null).OrderByDescending(x => x.CreatedUtc).ToList();

        /// <summary>A history id that isn't taken yet (two applies in the same second must not overwrite each other).</summary>
        public string NewHistoryId(DateTime? now = null)
        {
            var baseId = (now ?? DateTime.Now).ToString("yyyyMMdd_HHmmss");
            var id = baseId;
            for (int n = 2; File.Exists(Path.Combine(Folder, "applied_" + id + ".json")); n++) id = baseId + "_" + n;
            return id;
        }

        /// <summary>Moves applied changes out of pending into their own history file.</summary>
        public Changeset RecordApplied(IList<Change> applied, ApplyRecord rec, DateTime? now = null)
        {
            var cs = new Changeset
            {
                Id = NewHistoryId(now), Name = rec.Kind == "DBC" ? "Client data sent out" : $"Applied to {rec.Target}", State = ChangesetState.Applied,
                AppliedTarget = rec.Target, AppliedDatabase = rec.Database, Changes = applied.ToList(),
            };
            cs.Log.Add(rec);
            Save(cs);
            var ids = new HashSet<string>(applied.Select(a => a.Id));
            Pending.Changes.RemoveAll(c => ids.Contains(c.Id));
            Pending.Log.Add(rec);
            SavePending();
            return cs;
        }

        /// <summary>
        /// Notes that DBC edits were written out (to the server's dbc folder and/or a patch work folder). DBC edits never go
        /// through the database Apply, so this is how they leave the staged list: once an edit has gone to BOTH places it moves
        /// to History on its own; after just one, <see cref="MoveDbcToHistory"/> moves it when you say it's done.
        /// Returns the History entry when something moved.
        /// </summary>
        public Changeset MarkDbcSent(IList<Change> sent, bool toServer, string where, DateTime? now = null)
        {
            var t = (now ?? DateTime.Now).ToUniversalTime();
            foreach (var c in sent.Where(c => c.Dbc != null))
            {
                if (toServer) { c.Dbc.SentToServerUtc = t; c.Dbc.ServerBackupDir = where; }
                else { c.Dbc.SentToPatchUtc = t; c.Dbc.PatchFolder = where; }
            }
            var done = sent.Where(c => c.Dbc?.SentToServerUtc != null && c.Dbc.SentToPatchUtc != null).ToList();
            if (done.Count == 0) { SavePending(); return null; }
            return MoveDbcToHistory(done, now);
        }

        /// <summary>Moves sent DBC edits out of the staged list into History.</summary>
        public Changeset MoveDbcToHistory(IList<Change> changes, DateTime? now = null)
        {
            var list = changes.Where(c => c.Dbc != null).ToList();
            if (list.Count == 0) return null;
            var where = new List<string>();
            if (list.Any(c => c.Dbc.SentToServerUtc != null)) where.Add("server dbc folder");
            if (list.Any(c => c.Dbc.SentToPatchUtc != null)) where.Add("patch work folder");
            var rec = new ApplyRecord
            {
                Utc = (now ?? DateTime.Now).ToUniversalTime(), Target = "Client data", Database = "dbc", Kind = "DBC", Success = true,
                BackupDir = list.Select(c => c.Dbc.ServerBackupDir).LastOrDefault(d => !string.IsNullOrEmpty(d)),
                Message = $"{list.Count} DBC edit(s) sent to the " + (where.Count == 0 ? "(nowhere yet)" : string.Join(" and ", where)) + ".",
            };
            return RecordApplied(list, rec, now);
        }

        /// <summary>
        /// Staged-changes files from before 0.9 didn't remember where DBC edits had been sent. Fill that in from the log
        /// (a successful send recorded after the edit was staged), so the Changes screen can say what's done.
        /// </summary>
        public bool InferDbcSent()
        {
            bool any = false;
            foreach (var c in Pending.Changes.Where(c => c.Dbc != null))
            {
                foreach (var l in Pending.Log.Where(l => l.Success && l.Database == "dbc" && l.Utc >= c.CreatedUtc))
                {
                    if (l.Kind == "DBC → server copy" && c.Dbc.SentToServerUtc == null) { c.Dbc.SentToServerUtc = l.Utc; c.Dbc.ServerBackupDir = l.BackupDir; any = true; }
                    if (l.Kind == "DBC → patch folder" && c.Dbc.SentToPatchUtc == null) { c.Dbc.SentToPatchUtc = l.Utc; c.Dbc.PatchFolder = l.BackupDir; any = true; }
                }
            }
            return any;
        }

        public string ExportSql(IEnumerable<Change> changes, bool undo)
        {
            var list = changes.Where(c => c.Dbc == null).ToList();
            if (undo) list.Reverse();
            var lines = new List<string> { $"-- AndersCraft Studio {(undo ? "undo" : "changes")} export, {DateTime.Now:yyyy-MM-dd HH:mm}", "START TRANSACTION;" };
            foreach (var c in list) { lines.Add("-- " + c.Summary); lines.Add(undo ? c.UndoSql : c.ForwardSql); }
            lines.Add("COMMIT;");
            return string.Join(Environment.NewLine, lines) + Environment.NewLine;
        }
    }
}
