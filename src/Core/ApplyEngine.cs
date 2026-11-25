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
        /// <summary>When Apply stopped because a row isn't what it was at staging time: which change, and what kind of mismatch.</summary>
        public string ConflictChangeId;
        public ConflictKind Conflict;
    }

    public enum ConflictKind { None, Changed, Missing, Several, Exists, Other }

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
                            if (cur.Rows.Count != 1) throw RowCountConflict(c, cur.Rows.Count);
                            foreach (var f in c.Fields)
                            {
                                var now = SqlGen.Literal(cur.Rows[0][cur.ColumnIndex(f.Name)]);
                                if (now != f.Before) throw new ApplyConflict($"\"{c.Summary}\": in the database, {f.Name} is now {Show(now)}, but when you staged this edit it was {Show(f.Before)}. Something changed that row since (an earlier Apply, the game server, or another tool).", c, ConflictKind.Changed);
                            }
                            Exec(db, c.ForwardSql, 1, o);
                            break;
                        }
                        case ChangeKind.Delete:
                        {
                            var cur = db.Query(SqlGen.SelectRow(c.Table, c.Key, c.Fields.Select(f => f.Name), true), 2);
                            if (cur.Rows.Count != 1) throw RowCountConflict(c, cur.Rows.Count);
                            foreach (var f in c.Fields)
                            {
                                var now = SqlGen.Literal(cur.Rows[0][cur.ColumnIndex(f.Name)]);
                                if (now != f.Before) throw new ApplyConflict($"\"{c.Summary}\": the row changed since you staged its removal ({f.Name} is now {Show(now)}, was {Show(f.Before)}).", c, ConflictKind.Changed);
                            }
                            Exec(db, c.ForwardSql, 1, o);
                            break;
                        }
                        case ChangeKind.Insert:
                        {
                            var cur = db.Query(SqlGen.SelectRow(c.Table, c.Key, c.Key.Select(k => k.Column), true), 2);
                            if (cur.Rows.Count != 0) throw new ApplyConflict($"\"{c.Summary}\": {c.Table} already has a row with {c.KeyText}, so this new row can't be added (maybe it was applied already).", c, ConflictKind.Exists);
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
                if (ex is ApplyConflict ac) { o.ConflictChangeId = ac.ChangeId; o.Conflict = ac.Kind; }
            }
            return o;
        }

        static ApplyConflict RowCountConflict(Change c, int n) => n == 0
            ? new ApplyConflict($"\"{c.Summary}\": that row isn't in the database any more ({c.Table}, {c.KeyText}).", c, ConflictKind.Missing)
            : new ApplyConflict($"\"{c.Summary}\": {c.Table} has more than one row with {c.KeyText}, so Studio won't guess which one to change.", c, ConflictKind.Several);

        static string Show(string literal) => literal == null ? "(nothing)" : literal.Length > 60 ? literal.Substring(0, 60) + "…" : literal;

        /// <summary>
        /// "Refresh baseline": makes a staged edit start from what the row holds now (<paramref name="current"/> = that row,
        /// read again), keeping your new values. Fields that already hold your value drop out. Returns false when the change
        /// can't be refreshed (the row is gone, or it's not an edit or removal). After an edit is refreshed it may have no
        /// fields left (the database already has your values); then it can be discarded.
        /// </summary>
        public static bool RefreshBaseline(Change c, ResultTable current)
        {
            if (current == null || current.Rows.Count != 1) return false;
            var row = current.Rows[0];
            if (c.Kind == ChangeKind.Update)
            {
                foreach (var f in c.Fields)
                    if (current.ColumnIndex(f.Name) < 0) return false;
                foreach (var f in c.Fields)
                {
                    var v = row[current.ColumnIndex(f.Name)];
                    f.Before = SqlGen.Literal(v); f.BeforeDisplay = SqlGen.Display(v);
                }
                c.Fields.RemoveAll(f => f.Before == f.After);
                if (c.Fields.Count > 0) ChangeFactory.RebuildUpdate(c);
                return true;
            }
            if (c.Kind == ChangeKind.Delete)
            {
                foreach (var f in c.Fields)
                    if (current.ColumnIndex(f.Name) < 0) return false;
                var cols = new List<string>(); var vals = new List<string>();
                foreach (var f in c.Fields)
                {
                    var v = row[current.ColumnIndex(f.Name)];
                    f.Before = SqlGen.Literal(v); f.BeforeDisplay = SqlGen.Display(v);
                    cols.Add(f.Name); vals.Add(f.Before);
                }
                c.UndoSql = SqlGen.InsertRow(c.Table, cols, vals);
                return true;
            }
            return false;
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

    public sealed class ApplyConflict : Exception
    {
        public string ChangeId; public ConflictKind Kind = ConflictKind.Other;
        public ApplyConflict(string m) : base(m) { }
        public ApplyConflict(string m, Change c, ConflictKind k) : base(m) { ChangeId = c?.Id; Kind = k; }
    }
}
