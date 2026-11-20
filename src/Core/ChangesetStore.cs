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
            Pending = LoadFile(PendingPath) ?? new Changeset { Name = "Staged changes" };
            Pending.FilePath = PendingPath;
        }

        public string PendingPath => Path.Combine(Folder, "pending.json");

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
            File.Move(tmp, cs.FilePath, true);
        }

        public void SavePending() => Save(Pending);

        public List<Changeset> History() =>
            Directory.GetFiles(Folder, "applied_*.json").Select(LoadFile).Where(x => x != null).OrderByDescending(x => x.CreatedUtc).ToList();

        /// <summary>Moves applied changes out of pending into their own history file.</summary>
        public Changeset RecordApplied(IList<Change> applied, ApplyRecord rec)
        {
            var cs = new Changeset
            {
                Id = DateTime.Now.ToString("yyyyMMdd_HHmmss"), Name = $"Applied to {rec.Target}", State = ChangesetState.Applied,
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
