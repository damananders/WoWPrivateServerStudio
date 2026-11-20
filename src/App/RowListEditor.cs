using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AndersCraftStudio.Core;

namespace AndersCraftStudio
{
    /// <summary>
    /// An editable list of rows from one table (vendor items, spawns, loot rows...). You edit in the grid, add or remove
    /// rows, and "Stage" turns the difference into INSERT / UPDATE / DELETE changes with undo. Nothing is written directly.
    /// </summary>
    public sealed class RowListEditor
    {
        public sealed class NameColumn { public string IdColumn, Kind, Header; }

        readonly MainWindow w;
        Studio S => w.S;
        public string Table { get; }
        readonly string where, orderBy, title, help;
        readonly Dictionary<string, string> fixedValues;   // column → literal for rows that belong to this list
        readonly List<NameColumn> names;
        readonly Func<SP, long, string> resolve;
        public Func<RowValues, RowValues> NewRow;          // optional: build a new row (else fixed values + zeros)
        public Action<RowListEditor> Changed;              // called after load/edit (e.g. to refresh a simulator)
        public List<UIElement> ExtraButtons { get; } = new List<UIElement>();

        public List<string> Key { get; private set; } = new List<string>();
        /// <summary>For tables without a primary key (event_scripts, spell_scripts): rows are matched on every column.</summary>
        public bool MatchAllColumns { get; set; }
        bool keyless;
        public List<string> Columns { get; private set; } = new List<string>();
        Dictionary<string, Type> types = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, bool> nullable = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        List<RowValues> baseline = new List<RowValues>();
        public DataTable Data { get; private set; }
        readonly DataGrid grid = new DataGrid { AutoGenerateColumns = true, MaxHeight = 420, CanUserSortColumns = true, SelectionUnit = DataGridSelectionUnit.CellOrRowHeader, HeadersVisibility = DataGridHeadersVisibility.All, RowHeaderWidth = 14 };
        readonly TextBlock status = U.T("", 12.5, "Muted", null, true, new Thickness(0, 8, 0, 0));
        readonly StackPanel root = new StackPanel();
        bool dirty;
        string error;
        /// <summary>Rows per page. Lists longer than this are paged (no row cap).</summary>
        public int PageSize { get; set; } = 500;
        int page;
        long total;
        int Pages => (int)Math.Max(1, (total + PageSize - 1) / PageSize);
        public UIElement View => root;
        public DataGrid Grid => grid;

        public RowListEditor(MainWindow w, string title, string help, string table, string where, Dictionary<string, string> fixedValues, string orderBy, IEnumerable<NameColumn> nameColumns = null, Func<SP, long, string> resolve = null)
        {
            this.w = w; this.title = title; this.help = help; Table = table; this.where = where; this.fixedValues = fixedValues ?? new Dictionary<string, string>(); this.orderBy = orderBy;
            names = nameColumns?.ToList() ?? new List<NameColumn>(); this.resolve = resolve;
            grid.AutoGeneratingColumn += (s, e) =>
            {
                var n = e.PropertyName;
                bool isName = names.Any(x => x.Header == n);
                bool isKey = Key.Contains(n, StringComparer.OrdinalIgnoreCase);
                var head = U.Row(); if (isKey) head.Children.Add(new Border { Background = U.R("AmberDim"), CornerRadius = new CornerRadius(4), Padding = new Thickness(4, 0, 4, 1), Margin = new Thickness(0, 0, 6, 0), Child = U.T("key", 10.5, "Amber", FontWeights.Bold, false) });
                head.Children.Add(new TextBlock { Text = n });
                var meaning = TableCatalog.FieldMeaning(Table, n);
                head.ToolTip = isName ? "Looked up for you (read-only)." : n + (types.TryGetValue(n, out var t) ? $" ({t.Name})" : "") + (meaning.Length > 0 ? "\n" + meaning : "") + (isKey ? "\nPart of the key: changing it stages a remove + add." : "");
                e.Column.Header = head;
                e.Column.MaxWidth = 340;
                if (isName) { e.Column.IsReadOnly = true; if (e.Column is DataGridTextColumn tc) { var st = new Style(typeof(TextBlock)); st.Setters.Add(new Setter(TextBlock.ForegroundProperty, U.R("Amber"))); tc.ElementStyle = st; } }
            };
            grid.CellEditEnding += (s, e) => w.Dispatcher.BeginInvoke(new Action(() => { MarkDirty(); RefreshNames(); }));
        }

        public void Load()
        {
            error = null;
            try
            {
                using var db = S.OpenRead(S.Profile.WorldDb);
                Key = db.PrimaryKey(Table).ToList();
                var cnt = db.Query($"SELECT COUNT(*) FROM {SqlGen.Ident(Table)} WHERE {where}", 1);
                total = cnt.Rows.Count > 0 ? Convert.ToInt64(cnt.Rows[0][0]) : 0;
                page = Math.Max(0, Math.Min(page, Pages - 1));
                // a stable order (the key last) so pages never overlap or skip rows
                var order = string.Join(", ", new[] { orderBy }.Where(o => !string.IsNullOrEmpty(o)).Concat(Key.Select(SqlGen.Ident)));
                var dt = db.QueryTable($"SELECT * FROM {SqlGen.Ident(Table)} WHERE {where}" + (order.Length > 0 ? " ORDER BY " + order : "") + $" LIMIT {PageSize} OFFSET {(long)page * PageSize}", PageSize, out _);
                Columns = dt.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
                keyless = Key.Count == 0 && MatchAllColumns;
                if (keyless) Key = Columns.ToList();
                types = dt.Columns.Cast<DataColumn>().ToDictionary(c => c.ColumnName, c => c.DataType, StringComparer.OrdinalIgnoreCase);
                nullable = dt.Columns.Cast<DataColumn>().ToDictionary(c => c.ColumnName, c => c.ExtendedProperties["nullable"] is bool b ? b : true, StringComparer.OrdinalIgnoreCase);
                var dbRows = dt.Rows.Cast<DataRow>().Select(r => RowValues.FromObjects(Columns, r.ItemArray)).ToList();
                baseline = RowDiff.Overlay(S.Changes.Pending, Table, Key, dbRows, Belongs);
                // staged new rows are shown once, on the last page
                if (page < Pages - 1)
                {
                    var onPage = new HashSet<string>(dbRows.Select(KeyText));
                    baseline = baseline.Where(r => onPage.Contains(KeyText(r))).ToList();
                }
            }
            catch (Exception ex) { error = Friendly.Text(ex); baseline = new List<RowValues>(); }
            BuildData(baseline);
            dirty = false;
            Render();
        }

        string KeyText(RowValues r) => string.Join("|", Key.Select(k => r.Lit(k)));

        bool Belongs(RowValues r) => fixedValues.All(kv => string.Equals(r.Lit(kv.Key), kv.Value, StringComparison.OrdinalIgnoreCase));

        void BuildData(IEnumerable<RowValues> rows)
        {
            Data = new DataTable();
            foreach (var c in Columns)
            {
                Data.Columns.Add(c, typeof(string));
                var nc = names.FirstOrDefault(n => string.Equals(n.IdColumn, c, StringComparison.OrdinalIgnoreCase));
                if (nc != null) Data.Columns.Add(nc.Header, typeof(string));
            }
            foreach (var r in rows)
            {
                var dr = Data.NewRow();
                foreach (var c in Columns) dr[c] = r.Disp(c) ?? "";
                Data.Rows.Add(dr);
            }
            RefreshNames();
            grid.ItemsSource = Data.DefaultView;
        }

        void RefreshNames()
        {
            if (Data == null || resolve == null) return;
            foreach (var nc in names)
                foreach (DataRow dr in Data.Rows)
                {
                    long.TryParse(Convert.ToString(dr[nc.IdColumn]), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id);
                    var kind = nc.Kind switch { "spell" => SP.Spell, "creature" => SP.Creature, "item" => SP.Item, "quest" => SP.Quest, "map" => SP.Map, "gameobject" => SP.GameObject, _ => SP.Int };
                    dr[nc.Header] = id == 0 && nc.Kind != "map" ? "" : (kind == SP.Int || kind == SP.Map ? S.Dbc.Name(nc.Kind, id) : resolve(kind, id)) ?? "?";
                }
        }

        void MarkDirty() { dirty = true; Changed?.Invoke(this); UpdateStatus(); }

        /// <summary>The rows as currently edited, as literals. Throws with a friendly message on a bad value.</summary>
        public List<RowValues> CurrentRows()
        {
            var res = new List<RowValues>();
            int i = 0;
            foreach (DataRow dr in Data.Rows)
            {
                i++;
                if (dr.RowState == DataRowState.Deleted) continue;
                var r = new RowValues();
                foreach (var c in Columns)
                {
                    var text = Convert.ToString(dr[c]);
                    if (!SqlGen.TryTypedLiteral(text, types.GetValueOrDefault(c, typeof(string)), nullable.GetValueOrDefault(c, true), out var lit, out var err))
                        throw new FormatException($"Row {i}, {c}: {err}");
                    r.Set(c, lit, text);
                }
                res.Add(r);
            }
            return res;
        }

        void Render()
        {
            root.Children.Clear();
            var head = U.Row(U.T(title, 15, "Text", FontWeights.SemiBold, false), new Border { Width = 10 }, U.Pill(total > Data.Rows.Count ? $"{total:N0} rows" : Data.Rows.Count.ToString(), "Panel2", "Muted"), U.Pill("edits are staged", "AmberDim", "Amber", "Changes here go into your changeset, with undo. Nothing is written until Apply."));
            root.Children.Add(head);
            if (Pages > 1)
            {
                var prev = U.Btn("◀", () => GoPage(page - 1), null, "Previous page"); prev.MinWidth = 34; prev.IsEnabled = page > 0;
                var next = U.Btn("▶", () => GoPage(page + 1), null, "Next page"); next.MinWidth = 34; next.IsEnabled = page < Pages - 1;
                long from = (long)page * PageSize + 1, to = Math.Min(total, (long)(page + 1) * PageSize);
                var pager = U.Row(prev, U.T($"Rows {from:N0}–{to:N0} of {total:N0} · page {page + 1} of {Pages}", 12.5, "Muted", null, false, new Thickness(0, 0, 10, 0)), next);
                pager.Margin = new Thickness(0, 6, 0, 4);
                ((FrameworkElement)pager.Children[1]).VerticalAlignment = VerticalAlignment.Center;
                root.Children.Add(pager);
            }
            if (!string.IsNullOrEmpty(help)) root.Children.Add(U.T(help, 12.5, "Muted", null, true, new Thickness(0, 4, 0, 10)));
            if (error != null) { root.Children.Add(U.Callout("Not available on this server: " + error, "warn")); return; }
            if (Key.Count == 0) { root.Children.Add(U.Callout($"{Table} has no primary key, so Studio can't edit its rows safely. Use the SQL browser.", "warn")); }
            if (keyless) root.Children.Add(U.Callout($"{Table} has no primary key, so Studio finds each row by all of its values (numbers with decimals match to 0.0005). Changing a row is staged as remove + add. If two rows are exactly the same, Apply stops rather than guess which one you meant.", "info", "Rows matched on every column"));
            root.Children.Add(grid);
            grid.IsReadOnly = Key.Count == 0;
            var add = U.Btn("Add row", AddRow, null, "Adds a row that belongs to this list. Fill in the cells, then Stage.");
            var dup = U.Btn("Duplicate row", DuplicateRow, null, "Copies the selected row. Change its key (e.g. item) before staging.");
            var rem = U.Btn("Remove row", RemoveRows, null, "Removes the selected rows from the list (staged as DELETE with undo).");
            var stage = U.Btn("Stage", Stage, "BtnPrimary", "Turn the differences into staged changes with undo SQL.");
            var undo = U.Btn("Discard", () => { Load(); Changed?.Invoke(this); }, "BtnGhost", "Throw away unstaged edits in this list.");
            var bar = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            foreach (var b in new UIElement[] { add, dup, rem }.Concat(ExtraButtons).Concat(new UIElement[] { stage, undo })) { b.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 6)); bar.Children.Add(b); }
            if (Key.Count == 0) bar.IsEnabled = false;
            root.Children.Add(bar);
            root.Children.Add(status);
            UpdateStatus();
        }

        /// <summary>Moves to another page. Unstaged edits on this page are staged first, or dropped, as the user picks.</summary>
        public void GoPage(int p)
        {
            p = Math.Max(0, Math.Min(Pages - 1, p));
            if (p == page) return;
            if (Dirty)
            {
                var r = MessageBox.Show(w, "This page has edits that aren't staged yet. Stage them before changing page?\n\nYes = stage, No = drop them, Cancel = stay here.", title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (r == MessageBoxResult.Cancel) return;
                if (r == MessageBoxResult.Yes) { if (!TryStage()) return; }
            }
            page = p;
            Load();
            Changed?.Invoke(this);
        }

        void UpdateStatus()
        {
            if (Data == null) return;
            int changes = 0;
            try { changes = CountDifferences(); }
            catch (Exception ex) { status.Text = Friendly.Text(ex); status.Foreground = U.R("Bad"); return; }
            status.Foreground = changes > 0 ? U.R("Amber") : U.R("Muted");
            status.Text = changes > 0 ? $"{changes} row(s) changed here and not staged yet. Press Stage." : "No unstaged edits in this list.";
        }

        int CountDifferences()
        {
            var now = CurrentRows();
            string K(RowValues r) => string.Join("|", Key.Select(k => r.Lit(k)));
            var b = baseline.ToDictionary(K);
            int n = 0;
            var seen = new HashSet<string>();
            foreach (var r in now)
            {
                var k = K(r); seen.Add(k);
                if (!b.TryGetValue(k, out var old)) { n++; continue; }
                if (Columns.Any(c => r.Lit(c) != old.Lit(c))) n++;
            }
            n += b.Keys.Count(k => !seen.Contains(k));
            return n;
        }

        void AddRow()
        {
            var r = new RowValues();
            foreach (var c in Columns)
            {
                var t = types.GetValueOrDefault(c, typeof(string));
                r.Set(c, t == typeof(string) ? "''" : "0", t == typeof(string) ? "" : "0");
            }
            foreach (var kv in fixedValues) r.Set(kv.Key, kv.Value);
            if (NewRow != null) r = NewRow(r);
            var dr = Data.NewRow();
            foreach (var c in Columns) dr[c] = r.Disp(c) ?? "";
            Data.Rows.Add(dr);
            MarkDirty(); RefreshNames();
            grid.ScrollIntoView(grid.Items[grid.Items.Count - 1]);
        }

        DataRowView Selected() => grid.CurrentCell.Item as DataRowView ?? grid.SelectedCells.Select(c => c.Item).OfType<DataRowView>().FirstOrDefault();

        void DuplicateRow()
        {
            var sel = Selected();
            if (sel == null) { w.Toast("Click a cell in the row to copy first.", "warn"); return; }
            var dr = Data.NewRow();
            foreach (DataColumn c in Data.Columns) dr[c] = sel.Row[c];
            if (NewRow != null)
            {
                var r = new RowValues(); foreach (var c in Columns) r.Set(c, "0", Convert.ToString(dr[c]));
                r = NewRow(r);
                foreach (var c in Key) dr[c] = r.Disp(c);
            }
            Data.Rows.Add(dr);
            MarkDirty(); RefreshNames();
        }

        void RemoveRows()
        {
            var rows = grid.SelectedCells.Select(c => c.Item).OfType<DataRowView>().Distinct().ToList();
            if (rows.Count == 0 && Selected() != null) rows.Add(Selected());
            if (rows.Count == 0) { w.Toast("Select the rows to remove first.", "warn"); return; }
            foreach (var r in rows) r.Row.Delete();
            Data.AcceptChanges();
            MarkDirty();
        }

        public void Stage() => TryStage();

        bool TryStage()
        {
            try
            {
                var now = CurrentRows();
                foreach (var r in now) foreach (var kv in fixedValues) if (r.Lit(kv.Key) != kv.Value) throw new FormatException($"Every row here must keep {kv.Key} = {kv.Value}.");
                // the list may be paged: a new key must not exist on another page (or anywhere in the table)
                var known = new HashSet<string>(baseline.Select(KeyText));
                var fresh = now.Where(r => !known.Contains(KeyText(r))).Take(100).ToList();
                if (fresh.Count > 0 && Key.Count > 0)
                {
                    using var db = S.OpenRead(S.Profile.WorldDb);
                    foreach (var r in fresh)
                    {
                        var cond = string.Join(" AND ", Key.Select(k => SqlGen.Ident(k) + " = " + r.Lit(k)));
                        if (db.Query($"SELECT 1 FROM {SqlGen.Ident(Table)} WHERE {cond} LIMIT 1", 1).Rows.Count > 0 &&
                            !S.Changes.Pending.Changes.Any(c => c.Kind == ChangeKind.Delete && c.SameRow(Table, Key.Select(k => new KeyPart(k, r.Lit(k))).ToList())))
                            throw new FormatException($"A row with {string.Join(", ", Key.Select(k => k + " = " + r.Disp(k)))} already exists in {Table}" + (Pages > 1 ? " (maybe on another page)." : "."));
                    }
                }
                int n = RowDiff.Stage(S.Changes.Pending, Table, Key, Columns, baseline, now);
                S.SaveChanges();
                w.Toast(n == 0 ? "Nothing changed in this list." : $"Staged {n} change(s) to {Table}. Nothing written yet; review them on Changes.", n == 0 ? "info" : "ok");
                Load();
                Changed?.Invoke(this);
                return true;
            }
            catch (Exception ex) { w.Toast("Not staged: " + Friendly.Text(ex), "bad"); return false; }
        }

        public bool Dirty { get { try { return CountDifferences() > 0; } catch { return true; } } }

        // for screenshots
        public void ScreensEditCell(int row, string col, string value) { if (Data.Rows.Count > row) { Data.Rows[row][col] = value; MarkDirty(); RefreshNames(); } }
        public void ScreensAddRow(Action<DataRow> fill) { AddRow(); fill(Data.Rows[Data.Rows.Count - 1]); MarkDirty(); RefreshNames(); }
    }
}
