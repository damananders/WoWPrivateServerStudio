using System;
using System.Collections.Generic;
using System.Linq;

namespace AndersCraftStudio.Core
{
    /// <summary>One row of an editable list, as SQL literals (with the text shown to the user).</summary>
    public sealed class RowValues
    {
        public readonly Dictionary<string, (string lit, string disp)> Cells = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        public RowValues Set(string col, string lit, string disp = null) { Cells[col] = (lit, disp ?? Unquote(lit)); return this; }
        public string Lit(string col) => Cells.TryGetValue(col, out var v) ? v.lit : null;
        public string Disp(string col) => Cells.TryGetValue(col, out var v) ? v.disp : null;
        public RowValues Clone() { var r = new RowValues(); foreach (var kv in Cells) r.Cells[kv.Key] = kv.Value; return r; }
        static string Unquote(string lit) => lit == null ? null : lit == "NULL" ? "NULL" : lit.Length >= 2 && lit[0] == '\'' && lit[^1] == '\'' ? lit.Substring(1, lit.Length - 2).Replace("\\'", "'").Replace("\\\\", "\\") : lit;
        public static RowValues FromObjects(IList<string> cols, IList<object> vals)
        {
            var r = new RowValues();
            for (int i = 0; i < cols.Count; i++) r.Cells[cols[i]] = (SqlGen.Literal(vals[i]), SqlGen.Display(vals[i]));
            return r;
        }
    }

    /// <summary>
    /// Turns "the list as it was" vs "the list as edited" into staged changes: removed rows → DELETE, new rows → INSERT,
    /// changed cells → UPDATE (key changes become DELETE + INSERT). Works on top of earlier staged changes, so editing a
    /// row you already staged updates that change instead of piling up a second one.
    /// </summary>
    public static class RowDiff
    {
        static string KeyOf(RowValues r, IList<string> keyCols) => string.Join("|", keyCols.Select(k => r.Lit(k) ?? "NULL"));
        static string KeyOf(IList<KeyPart> key) => string.Join("|", key.Select(k => k.Value));
        static List<KeyPart> KeyParts(RowValues r, IList<string> keyCols) => keyCols.Select(k => new KeyPart(k, r.Lit(k), r.Disp(k))).ToList();

        static Change Pending(Changeset cs, string table, ChangeKind kind, string key) =>
            cs.Changes.FirstOrDefault(c => c.Kind == kind && string.Equals(c.Table, table, StringComparison.OrdinalIgnoreCase) && c.Key != null && KeyOf(c.Key) == key);

        /// <summary>What the list looks like once staged changes are taken into account (DB rows + staged edits/inserts − staged deletes).</summary>
        public static List<RowValues> Overlay(Changeset cs, string table, IList<string> keyCols, IList<RowValues> dbRows, Func<RowValues, bool> belongs)
        {
            var res = new List<RowValues>();
            foreach (var r in dbRows)
            {
                var k = KeyOf(r, keyCols);
                if (Pending(cs, table, ChangeKind.Delete, k) != null) continue;
                var row = r.Clone();
                var up = Pending(cs, table, ChangeKind.Update, k);
                if (up != null) foreach (var f in up.Fields) row.Cells[f.Name] = (f.After, f.AfterDisplay);
                res.Add(row);
            }
            foreach (var c in cs.Changes.Where(c => c.Kind == ChangeKind.Insert && string.Equals(c.Table, table, StringComparison.OrdinalIgnoreCase)))
            {
                var row = new RowValues();
                foreach (var f in c.Fields) row.Cells[f.Name] = (f.After, f.AfterDisplay);
                if (belongs(row) && !res.Any(x => KeyOf(x, keyCols) == KeyOf(row, keyCols))) res.Add(row);
            }
            return res;
        }

        /// <summary>Stages the difference between two versions of a list. Returns how many changes were added or updated.</summary>
        public static int Stage(Changeset cs, string table, IList<string> keyCols, IList<string> columns, IList<RowValues> before, IList<RowValues> after)
        {
            if (keyCols == null || keyCols.Count == 0) throw new ArgumentException($"{table} has no primary key, so rows can't be told apart.");
            var b = before.ToDictionary(r => KeyOf(r, keyCols));
            var a = new Dictionary<string, RowValues>();
            foreach (var r in after)
            {
                var k = KeyOf(r, keyCols);
                if (a.ContainsKey(k)) throw new ArgumentException($"Two rows in {table} would have the same key ({string.Join(", ", keyCols.Select(c => c + "=" + r.Disp(c)))}).");
                a[k] = r;
            }
            int n = 0;
            // removed rows
            foreach (var kv in b.Where(kv => !a.ContainsKey(kv.Key)))
            {
                var ins = Pending(cs, table, ChangeKind.Insert, kv.Key);
                if (ins != null) { cs.Changes.Remove(ins); n++; continue; }      // it was only ever staged: just forget it
                var original = OriginalOf(cs, table, kv.Value, kv.Key);   // before dropping the staged edits it undoes
                cs.Changes.RemoveAll(c => c.Kind == ChangeKind.Update && string.Equals(c.Table, table, StringComparison.OrdinalIgnoreCase) && KeyOf(c.Key) == kv.Key);
                StageDelete(cs, table, KeyParts(kv.Value, keyCols), columns, original);
                n++;
            }
            // new rows
            foreach (var kv in a.Where(kv => !b.ContainsKey(kv.Key)))
            {
                var del = Pending(cs, table, ChangeKind.Delete, kv.Key);
                if (del != null)
                {
                    // re-adding a row that was staged for removal: cancel the removal, keep only real differences
                    cs.Changes.Remove(del);
                    foreach (var f in del.Fields)
                    {
                        var now = kv.Value.Lit(f.Name);
                        if (now != null && now != f.Before)
                            ChangeFactory.StageCellEdit(cs, table, KeyParts(kv.Value, keyCols), f.Name, f.Before, f.BeforeDisplay, now, kv.Value.Disp(f.Name));
                    }
                    n++; continue;
                }
                var cols = columns.Where(c => kv.Value.Cells.ContainsKey(c)).ToList();
                var c2 = ChangeFactory.StageRowInsert(cs, table, KeyParts(kv.Value, keyCols), cols, cols.Select(c => kv.Value.Lit(c)).ToList());
                for (int i = 0; i < cols.Count; i++) c2.Fields[i].AfterDisplay = kv.Value.Disp(cols[i]);
                n++;
            }
            // changed rows
            foreach (var kv in a.Where(kv => b.ContainsKey(kv.Key)))
            {
                var old = b[kv.Key];
                var changed = columns.Where(c => kv.Value.Lit(c) != null && kv.Value.Lit(c) != old.Lit(c)).ToList();
                if (changed.Count == 0) continue;
                var ins = Pending(cs, table, ChangeKind.Insert, kv.Key);
                if (ins != null)
                {
                    foreach (var c in changed)
                    {
                        var f = ins.Fields.FirstOrDefault(x => string.Equals(x.Name, c, StringComparison.OrdinalIgnoreCase));
                        if (f == null) { f = new FieldChange { Name = c, Before = null, BeforeDisplay = "(no row)" }; ins.Fields.Add(f); }
                        f.After = kv.Value.Lit(c); f.AfterDisplay = kv.Value.Disp(c);
                    }
                    ins.ForwardSql = SqlGen.InsertRow(table, ins.Fields.Select(f => f.Name).ToList(), ins.Fields.Select(f => f.After).ToList());
                    n++; continue;
                }
                foreach (var c in changed)
                    ChangeFactory.StageCellEdit(cs, table, KeyParts(kv.Value, keyCols), c, old.Lit(c), old.Disp(c), kv.Value.Lit(c), kv.Value.Disp(c));
                n++;
            }
            return n;
        }

        /// <summary>The database version of a row (staged edits undone), so a staged DELETE's undo restores the real row.</summary>
        static RowValues OriginalOf(Changeset cs, string table, RowValues row, string key)
        {
            var orig = row.Clone();
            var up = Pending(cs, table, ChangeKind.Update, key);
            if (up != null) foreach (var f in up.Fields) orig.Cells[f.Name] = (f.Before, f.BeforeDisplay);
            return orig;
        }

        static void StageDelete(Changeset cs, string table, List<KeyPart> key, IList<string> columns, RowValues row)
        {
            var cols = columns.Where(c => row.Cells.ContainsKey(c)).ToList();
            var c = new Change { Kind = ChangeKind.Delete, Table = table, Key = key, Category = TableCatalog.CategoryOf(table) };
            foreach (var col in cols) c.Fields.Add(new FieldChange { Name = col, Before = row.Lit(col), BeforeDisplay = row.Disp(col), After = null, AfterDisplay = "(removed)" });
            c.ForwardSql = SqlGen.DeleteRow(table, key);
            c.UndoSql = SqlGen.InsertRow(table, cols, cols.Select(x => row.Lit(x)).ToList());
            c.Summary = $"Remove row from {table} ({c.KeyText})";
            cs.Changes.Add(c);
        }
    }
}
