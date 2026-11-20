using System;
using System.Collections.Generic;
using System.Linq;

namespace AndersCraftStudio.Core
{
    /// <summary>Turns edits into <see cref="Change"/> records with forward and undo SQL.</summary>
    public static class ChangeFactory
    {
        /// <summary>One edited cell. Merges into an existing staged edit of the same row when there is one.</summary>
        public static Change StageCellEdit(Changeset cs, string table, IList<KeyPart> key, string column, string beforeLiteral, string beforeDisplay, string afterLiteral, string afterDisplay)
        {
            var existing = cs.Changes.FirstOrDefault(c => c.Kind == ChangeKind.Update && c.SameRow(table, key));
            if (existing == null)
            {
                if (beforeLiteral == afterLiteral) return null;
                existing = new Change { Kind = ChangeKind.Update, Table = table, Key = key.ToList(), Category = TableCatalog.CategoryOf(table) };
                cs.Changes.Add(existing);
            }
            var f = existing.Fields.FirstOrDefault(x => string.Equals(x.Name, column, StringComparison.OrdinalIgnoreCase));
            if (f == null)
            {
                f = new FieldChange { Name = column, Before = beforeLiteral, BeforeDisplay = beforeDisplay };
                existing.Fields.Add(f);
            }
            f.After = afterLiteral; f.AfterDisplay = afterDisplay;
            existing.Fields.RemoveAll(x => x.Before == x.After);
            if (existing.Fields.Count == 0) { cs.Changes.Remove(existing); return null; }
            RebuildUpdate(existing);
            return existing;
        }

        public static void RebuildUpdate(Change c)
        {
            c.ForwardSql = SqlGen.UpdateRow(c.Table, c.Key, c.Fields.Select(f => (f.Name, f.After)));
            c.UndoSql = SqlGen.UpdateRow(c.Table, c.Key, c.Fields.Select(f => (f.Name, f.Before)));
            c.UndoAvailable = true; c.UndoNote = null;
            c.Summary = $"Edit {c.Table} ({c.KeyText}): " + string.Join(", ", c.Fields.Select(f => f.Name));
        }

        /// <summary>Removing a whole row. <paramref name="row"/> is every column with its current value, so undo can re-insert it exactly.</summary>
        public static Change StageRowDelete(Changeset cs, string table, IList<KeyPart> key, IList<string> columns, IList<object> row)
        {
            cs.Changes.RemoveAll(c => c.Kind == ChangeKind.Update && c.SameRow(table, key)); // edits to a removed row are moot
            var c = new Change { Kind = ChangeKind.Delete, Table = table, Key = key.ToList(), Category = TableCatalog.CategoryOf(table) };
            for (int i = 0; i < columns.Count; i++)
                c.Fields.Add(new FieldChange { Name = columns[i], Before = SqlGen.Literal(row[i]), BeforeDisplay = SqlGen.Display(row[i]), After = null, AfterDisplay = "(removed)" });
            c.ForwardSql = SqlGen.DeleteRow(table, key);
            c.UndoSql = SqlGen.InsertRow(table, columns, row.Select(SqlGen.Literal).ToList());
            c.Summary = $"Remove row from {table} ({c.KeyText})";
            cs.Changes.Add(c);
            return c;
        }

        /// <summary>A new row. <paramref name="literals"/> are already SQL literals.</summary>
        public static Change StageRowInsert(Changeset cs, string table, IList<KeyPart> key, IList<string> columns, IList<string> literals)
        {
            var c = new Change { Kind = ChangeKind.Insert, Table = table, Key = key.ToList(), Category = TableCatalog.CategoryOf(table) };
            for (int i = 0; i < columns.Count; i++)
                c.Fields.Add(new FieldChange { Name = columns[i], Before = null, BeforeDisplay = "(no row)", After = literals[i], AfterDisplay = literals[i] });
            c.ForwardSql = SqlGen.InsertRow(table, columns, literals);
            c.UndoSql = SqlGen.DeleteRow(table, key);
            c.Summary = $"Add row to {table} ({c.KeyText})";
            cs.Changes.Add(c);
            return c;
        }

        /// <summary>A statement typed in the query editor. Never executed here; undo data is captured with a read query.</summary>
        public static Change StageRawStatement(Changeset cs, StatementInfo st, ISqlReader reader)
        {
            if (!st.IsWrite) throw new ArgumentException("Only write statements are staged.");
            var plan = RawStatementParser.Parse(st);
            var c = new Change { Kind = ChangeKind.Raw, Raw = plan, Table = plan.Table ?? "", Structure = st.Kind == StatementKind.StructureWrite };
            c.Category = c.Structure ? "Structure" : TableCatalog.CategoryOf(plan.Table ?? "");
            c.ForwardSql = st.Text.Trim().TrimEnd(';') + ";";
            if (c.Structure)
            {
                c.UndoAvailable = false;
                c.UndoNote = "Structure changes have no automatic undo, and Studio won't apply them. Run them yourself with an admin account after taking a backup.";
                c.UndoSql = "-- " + c.UndoNote;
                c.Summary = $"{st.Verb} statement (structure change)";
            }
            else
            {
                RawUndoBuilder.Result r;
                try { r = reader != null ? RawUndoBuilder.Build(plan, reader) : new RawUndoBuilder.Result { Note = "Not connected, so the current rows couldn't be captured." }; }
                catch (Exception ex) { r = new RawUndoBuilder.Result { Note = "Couldn't capture the current rows: " + ex.Message }; }
                c.UndoAvailable = r.Available;
                c.UndoNote = r.Available ? $"Undo captured from {r.RowsCaptured} row(s) at staging time; it is captured again inside the Apply transaction." : r.Note;
                c.UndoSql = r.Available ? r.UndoSql : "-- No automatic undo: " + r.Note + "\n-- The backup taken before Apply is your way back.";
                c.Fields = r.Fields;
                c.Key = r.FirstKey;
                var rows = r.Available ? $", {r.RowsCaptured} row(s)" : "";
                c.Summary = $"{st.Verb} on {plan.Table}{rows} (typed SQL)";
            }
            cs.Changes.Add(c);
            return c;
        }
    }
}
