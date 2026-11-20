using System;
using System.Collections.Generic;
using System.Linq;

namespace AndersCraftStudio.Core
{
    public sealed class ApplyOutcome
    {
        public bool Success;
        public string Message;
        public List<string> Log = new List<string>();
    }

    /// <summary>
    /// Runs staged changes in ONE transaction on the write account. Before each change it re-reads the row
    /// (locking it) and stops if somebody else changed it since you staged the edit. Any problem rolls the whole
    /// transaction back, so either every selected change lands or none does.
    /// </summary>
    public static class ApplyEngine
    {
        /// <summary>Problems that block Apply before anything is touched.</summary>
        public static List<string> Validate(IList<Change> changes, string targetDb, IEnumerable<string> otherDatabases)
        {
            var problems = new List<string>();
            if (changes.Count == 0) problems.Add("No changes are selected.");
            var others = new HashSet<string>(otherDatabases.Where(d => !string.IsNullOrEmpty(d) && !string.Equals(d, targetDb, StringComparison.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);
            foreach (var c in changes)
            {
                if (c.Structure) problems.Add($"\"{c.Summary}\" changes table structure. Studio only applies row changes; untick it.");
                if (c.Schema != "world") problems.Add($"\"{c.Summary}\" is not a world-database change.");
                if (c.Kind == ChangeKind.Raw && c.Raw != null)
                {
                    if (!string.IsNullOrEmpty(c.Raw.Database) && !string.Equals(c.Raw.Database, targetDb, StringComparison.OrdinalIgnoreCase))
                        problems.Add($"\"{c.Summary}\" names database {c.Raw.Database} directly. Remove the database name so it goes to the target you pick here.");
                    foreach (var q in c.Raw.Qualifiers.Where(others.Contains))
                        problems.Add($"\"{c.Summary}\" mentions database {q}, which isn't the Apply target.");
                }
            }
            return problems.Distinct().ToList();
        }

        public static ApplyOutcome Apply(IList<Change> changes, ISqlExecutor db)
        {
            var o = new ApplyOutcome();
            db.Begin();
            o.Log.Add("START TRANSACTION");
            try
            {
                foreach (var c in changes)
                {
                    switch (c.Kind)
                    {
                        case ChangeKind.Update:
                        {
                            var cur = db.Query(SqlGen.SelectRow(c.Table, c.Key, c.Fields.Select(f => f.Name), true), 2);
                            if (cur.Rows.Count != 1) throw new ApplyConflict($"{c.Table} ({c.KeyText}) {(cur.Rows.Count == 0 ? "no longer exists" : "matches more than one row, so Studio won't guess which to change")}.");
                            foreach (var f in c.Fields)
                            {
                                var now = SqlGen.Literal(cur.Rows[0][cur.ColumnIndex(f.Name)]);
                                if (now != f.Before) throw new ApplyConflict($"{c.Table} ({c.KeyText}) {f.Name} is now {now}, not {f.Before} as when you staged it. Someone changed it; re-stage the edit.");
                            }
                            Exec(db, c.ForwardSql, 1, o);
                            break;
                        }
                        case ChangeKind.Delete:
                        {
                            var cur = db.Query(SqlGen.SelectRow(c.Table, c.Key, c.Fields.Select(f => f.Name), true), 2);
                            if (cur.Rows.Count != 1) throw new ApplyConflict($"{c.Table} ({c.KeyText}) {(cur.Rows.Count == 0 ? "no longer exists" : "matches more than one row, so Studio won't guess which to change")}.");
                            foreach (var f in c.Fields)
                            {
                                var now = SqlGen.Literal(cur.Rows[0][cur.ColumnIndex(f.Name)]);
                                if (now != f.Before) throw new ApplyConflict($"{c.Table} ({c.KeyText}) changed since you staged its removal ({f.Name}). Re-stage it.");
                            }
                            Exec(db, c.ForwardSql, 1, o);
                            break;
                        }
                        case ChangeKind.Insert:
                        {
                            var cur = db.Query(SqlGen.SelectRow(c.Table, c.Key, c.Key.Select(k => k.Column), true), 2);
                            if (cur.Rows.Count != 0) throw new ApplyConflict($"{c.Table} already has a row with {c.KeyText}.");
                            Exec(db, c.ForwardSql, 1, o);
                            break;
                        }
                        case ChangeKind.Raw:
                        {
                            if (c.Structure) throw new ApplyConflict("Structure changes can't be applied by Studio.");
                            if (c.Raw != null && c.Raw.UndoSupported)
                            {
                                // capture undo again, under lock, so it matches exactly what this statement changes
                                var r = RawUndoBuilder.Build(c.Raw, db, true);
                                if (r.Available) { c.UndoSql = r.UndoSql; c.UndoAvailable = true; c.UndoNote = $"Undo captured from {r.RowsCaptured} row(s) inside the Apply transaction."; }
                            }
                            Exec(db, c.ForwardSql, -1, o);
                            break;
                        }
                    }
                }
                db.Commit();
                o.Log.Add("COMMIT");
                o.Success = true;
                o.Message = $"Applied {changes.Count} change(s).";
            }
            catch (Exception ex)
            {
                try { db.Rollback(); o.Log.Add("ROLLBACK"); } catch (Exception rb) { o.Log.Add("ROLLBACK failed: " + rb.Message); }
                o.Success = false;
                o.Message = (ex is ApplyConflict ? "" : "Database error: ") + ex.Message + " Nothing was changed.";
            }
            return o;
        }

        /// <summary>Runs the undo SQL of applied changes, newest first, in one transaction.</summary>
        public static ApplyOutcome Rollback(IList<Change> applied, ISqlExecutor db)
        {
            var o = new ApplyOutcome();
            var missing = applied.Where(c => !c.UndoAvailable).ToList();
            if (missing.Count > 0)
            {
                o.Message = $"{missing.Count} change(s) have no automatic undo (e.g. \"{missing[0].Summary}\"). Restore the backup from that Apply instead.";
                return o;
            }
            db.Begin();
            o.Log.Add("START TRANSACTION");
            try
            {
                foreach (var c in applied.Reverse())
                {
                    switch (c.Kind)
                    {
                        case ChangeKind.Update:
                        {
                            var cur = db.Query(SqlGen.SelectRow(c.Table, c.Key, c.Fields.Select(f => f.Name), true), 2);
                            if (cur.Rows.Count != 1) throw new ApplyConflict($"{c.Table} ({c.KeyText}) {(cur.Rows.Count == 0 ? "no longer exists" : "matches more than one row, so Studio won't guess which to change")}.");
                            foreach (var f in c.Fields)
                            {
                                var now = SqlGen.Literal(cur.Rows[0][cur.ColumnIndex(f.Name)]);
                                if (now != f.After) throw new ApplyConflict($"{c.Table} ({c.KeyText}) {f.Name} was changed again after Apply (now {now}). Roll back by hand or restore the backup.");
                            }
                            Exec(db, c.UndoSql, 1, o);
                            break;
                        }
                        case ChangeKind.Insert:
                            Exec(db, c.UndoSql, 1, o);
                            break;
                        case ChangeKind.Delete:
                        {
                            var cur = db.Query(SqlGen.SelectRow(c.Table, c.Key, c.Key.Select(k => k.Column), true), 2);
                            if (cur.Rows.Count != 0) throw new ApplyConflict($"{c.Table} ({c.KeyText}) exists again, so it can't be restored.");
                            Exec(db, c.UndoSql, 1, o);
                            break;
                        }
                        case ChangeKind.Raw:
                            foreach (var st in SqlLexer.Split(c.UndoSql).Where(s => s.toks.Count > 0)) Exec(db, st.text + ";", -1, o);
                            break;
                    }
                }
                db.Commit();
                o.Log.Add("COMMIT");
                o.Success = true;
                o.Message = $"Rolled back {applied.Count} change(s).";
            }
            catch (Exception ex)
            {
                try { db.Rollback(); o.Log.Add("ROLLBACK"); } catch (Exception rb) { o.Log.Add("ROLLBACK failed: " + rb.Message); }
                o.Success = false;
                o.Message = (ex is ApplyConflict ? "" : "Database error: ") + ex.Message + " Nothing was changed.";
            }
            return o;
        }

        static void Exec(ISqlExecutor db, string sql, int expectRows, ApplyOutcome o)
        {
            int n = db.Execute(sql);
            o.Log.Add(sql + $"   -- {n} row(s)");
            if (expectRows >= 0 && n != expectRows) throw new ApplyConflict($"Expected {expectRows} row(s) to change but {n} did: {sql}");
        }
    }

    public sealed class ApplyConflict : Exception { public ApplyConflict(string m) : base(m) { } }
}
