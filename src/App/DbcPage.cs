using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using AndersCraftStudio.Core;

namespace AndersCraftStudio
{
    /// <summary>
    /// Generic editor for every 3.3.5a DBC: table list with descriptions, paged grid, filter, find by ID, CSV,
    /// localized columns, foreign-key names and links, server / client sources. Edits are staged as DBC row patches.
    /// </summary>
    public sealed class DbcPage : IPage
    {
        readonly MainWindow w;
        Studio S => w.S;
        readonly Grid view = new Grid();
        public UIElement View => view;

        // left
        readonly TextBox tableSearch = U.Box();
        readonly ListBox tableList = new ListBox();
        RadioButton srcServer, srcClient;
        readonly TextBlock srcNote = U.T("", 11.5, "Faint", null, true, new Thickness(2, 6, 0, 0));
        // right
        readonly ContentControl main = new ContentControl { Focusable = false };
        readonly DataGrid grid = new DataGrid();
        readonly TextBox filterBox = U.Box("", true);
        readonly TextBox findBox = U.Box("", true);
        readonly TextBlock pageText = U.T("", 12.5, "Muted", null, false);
        readonly TextBlock statusText = U.T("", 12.5, "Muted", null, true);
        readonly CheckBox allLocales = new CheckBox { Content = "All locales", ToolTip = "Show every language column of localized text (enGB, deDE, frFR, …) and the language mask. Off = English (enUS) only." };
        readonly CheckBox onlyDiff = new CheckBox { Content = "Only rows that differ from the other copy", ToolTip = "Compare this copy with the other source (server ↔ client) and show only rows that are different, added or missing." };
        readonly StackPanel detail = new StackPanel();
        readonly StackPanel header = new StackPanel();

        // state
        string source = "server";
        IDbcSource serverSrc; ClientDbcSource clientSrc; bool clientTried;
        string file;
        DbcTable baseTable, working;
        List<int> filtered = new List<int>();
        int page;
        int PageSize => Math.Max(50, S.Profiles.Settings.PageSize);
        bool dirty;
        string stageProblem;

        /// <summary>One open table's state while another tab is shown (unstaged edits stay).</summary>
        sealed class TabState { public DbcTable B, Wk; public bool Dirty; public string Filter, Problem, Source; public int Page; }
        readonly OpenTabs<TabState> tabs = new OpenTabs<TabState>(8, t => t != null && t.Dirty);
        TabState Snapshot() => new TabState { B = baseTable, Wk = working, Dirty = dirty, Filter = filterBox.Text, Problem = stageProblem, Source = source, Page = page };
        public IReadOnlyList<string> OpenFiles => tabs.Names;
        HashSet<uint> diffKeys; string diffNote;
        readonly Dictionary<string, Dictionary<uint, string>> linkNames = new Dictionary<string, Dictionary<uint, string>>(StringComparer.OrdinalIgnoreCase);
        readonly Refs refs;
        DataTable pageData;
        Grid fbHost, ibHost;

        public DbcPage(MainWindow w)
        {
            this.w = w; refs = new Refs(w.S);
            view.Margin = new Thickness(28, 22, 28, 20);
            view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            view.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
            var strip = U.StepStrip(1); DockPanel.SetDock(strip, Dock.Right); strip.VerticalAlignment = VerticalAlignment.Center; head.Children.Add(strip);
            head.Children.Add(U.Stack(U.H1("DBC tables"), U.T("The client's data tables (spells, zones, races, item looks…). Read any of the 246, edit rows, and stage the edits. Originals are never overwritten: staged edits leave Studio as a patch for the client and an updated server copy, from the Changes screen.", 13.5, "Muted")));
            view.Children.Add(head);

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(g, 1); view.Children.Add(g);

            // ---- left: source + table list
            var left = new DockPanel();
            srcServer = new RadioButton { Style = U.S("SegBtn"), Content = "Server", GroupName = "dbcsrc", IsChecked = true, ToolTip = "The server's extracted copies (worldserver.conf DataDir\\dbc). This is what the server reads." };
            srcClient = new RadioButton { Style = U.S("SegBtn"), Content = "Client", GroupName = "dbcsrc", ToolTip = "What the game client really sees: each DBC taken from the archive that wins in the client's load order (base, locale and patch MPQs)." };
            srcServer.Checked += (s, e) => SwitchSource("server");
            srcClient.Checked += (s, e) => SwitchSource("client");
            var seg = new Border { Background = U.R("Panel2"), BorderBrush = U.R("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), HorizontalAlignment = HorizontalAlignment.Left, Child = U.Row(srcServer, srcClient) };
            var top = U.Stack(U.Eyebrow("Source"), seg, srcNote);
            top.Margin = new Thickness(0, 0, 0, 10);
            DockPanel.SetDock(top, Dock.Top); left.Children.Add(top);
            tableSearch.ToolTip = "Filter the list by file name or description (e.g. \"spell\", \"zone\", \"sound\").";
            tableSearch.TextChanged += (s, e) => FillTableList();
            tableSearch.KeyDown += (s, e) => { if (e.Key == Key.Down && tableList.Items.Count > 0) { tableList.SelectedIndex = 0; (tableList.ItemContainerGenerator.ContainerFromIndex(0) as UIElement)?.Focus(); e.Handled = true; } };
            var sb = U.WithPlaceholder(tableSearch, "Find a table (name or what it holds)"); sb.Margin = new Thickness(0, 0, 0, 8);
            DockPanel.SetDock(sb, Dock.Top); left.Children.Add(sb);
            tableList.SelectionChanged += (s, e) => { if (tableList.SelectedItem is ListBoxItem li && li.Tag is string f && f != file) Open(f); };
            ScrollViewer.SetHorizontalScrollBarVisibility(tableList, ScrollBarVisibility.Disabled);
            left.Children.Add(tableList);
            g.Children.Add(U.Card(left, new Thickness(10, 12, 6, 8), new Thickness(0)));

            Grid.SetColumn(main, 2); g.Children.Add(main);
            main.Content = U.Card(U.Stack(U.H2("Pick a table"), U.T("Every DBC has a one-line description. Tables with a verified layout show named, typed columns; the rest open in raw mode with the column types guessed from the data.", 13.5, "Muted")), new Thickness(28), new Thickness(0));

            BuildGrid();
            filterBox.ToolTip = "Filter rows. Examples:\n  fire              text contains \"fire\"\n  133               any number field is 133\n  Name:fire         one column contains\n  RangeIndex=5      one column equals\n  SpellLevel>60     compare numbers\nSeveral terms must all match. Enter to apply.";
            filterBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) { ApplyFilter(); e.Handled = true; } };
            findBox.ToolTip = "Jump to a row by its ID (Enter).";
            findBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) { FindId(); e.Handled = true; } };
            allLocales.Checked += (s, e) => RebuildColumns(); allLocales.Unchecked += (s, e) => RebuildColumns();
            onlyDiff.Checked += (s, e) => { EnsureDiff(); ApplyFilter(); }; onlyDiff.Unchecked += (s, e) => ApplyFilter();

            U.OnKey(view, Key.S, ModifierKeys.Control, Stage);
            U.OnKey(view, Key.F, ModifierKeys.Control, () => { filterBox.Focus(); filterBox.SelectAll(); });
            U.OnKey(view, Key.G, ModifierKeys.Control, () => { findBox.Focus(); findBox.SelectAll(); });
            U.OnKey(view, Key.PageDown, ModifierKeys.Alt, () => GoPage(page + 1));
            U.OnKey(view, Key.PageUp, ModifierKeys.Alt, () => GoPage(page - 1));
            U.OnKey(view, Key.D, ModifierKeys.Control, DuplicateRow);
            S.ChangesChanged += () => tabs.DropClean(n => string.Equals(n, file, StringComparison.OrdinalIgnoreCase));   // other tabs reload with the new changeset
        }

        // ================================================================ sources
        string ServerDbcDir { get { try { var d = ConfParser.ParseFile(S.Profile.ConfPath).DataDir; return string.IsNullOrEmpty(d) ? null : Path.Combine(d, "dbc"); } catch { return null; } } }
        string ClientDataDir => string.IsNullOrEmpty(S.Profile?.ClientDir) ? null : Path.Combine(S.Profile.ClientDir, "Data");
        static string NameCache => Path.Combine(ProfileStore.DefaultRoot, "name_cache");

        IDbcSource Source
        {
            get
            {
                if (source == "client")
                {
                    if (!clientTried)
                    {
                        clientTried = true;
                        var dd = ClientDataDir;
                        if (dd != null && Directory.Exists(dd))
                        {
                            using (new WaitCursor()) clientSrc = new ClientDbcSource(dd, NameCache);
                        }
                    }
                    return clientSrc;
                }
                if (serverSrc == null && ServerDbcDir != null) serverSrc = new FolderDbcSource(ServerDbcDir, "Server copy (Data\\dbc)");
                return serverSrc;
            }
        }
        IDbcSource Other => source == "client" ? (serverSrc ??= ServerDbcDir != null ? new FolderDbcSource(ServerDbcDir, "Server copy") : null) : (clientTried ? clientSrc : CreateClientQuietly());
        IDbcSource CreateClientQuietly() { clientTried = true; var dd = ClientDataDir; if (dd != null && Directory.Exists(dd)) using (new WaitCursor()) clientSrc = new ClientDbcSource(dd, NameCache); return clientSrc; }

        void SwitchSource(string s)
        {
            if (source == s) return;
            if (dirty && MessageBox.Show(w, "You have edits in this table that aren't staged yet. Switch source and drop them?", "Unstaged edits", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            { (s == "client" ? srcServer : srcClient).IsChecked = true; return; }
            source = s;
            linkNames.Clear(); diffKeys = null;
            FillTableList();
            if (file != null) Open(file, force: true);
        }

        public void OnShow()
        {
            FillTableList();
            if (file != null && working != null) RefreshAfterExternalChange();
        }

        /// <summary>Called when staged changes may have changed elsewhere (e.g. removed on the Changes screen).</summary>
        void RefreshAfterExternalChange()
        {
            if (dirty) return;
            Open(file, force: true, keepPage: true);
        }

        void FillTableList()
        {
            var src = Source;
            tableList.Items.Clear();
            if (src == null)
            {
                srcNote.Text = source == "client" ? "Set the game client folder in Settings to read the client's DBCs." : "worldserver.conf has no DataDir, so the server's dbc folder can't be found.";
                return;
            }
            var files = src.Files;
            var staged = S.Changes.Pending.Changes.Where(DbcStager.IsDbc).GroupBy(c => c.Table, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);
            srcNote.Text = source == "client"
                ? $"{files.Count} DBCs across {clientSrc?.Archives.Count ?? 0} archives, each from the archive that wins."
                : $"{files.Count} files in {ServerDbcDir}";
            var q = tableSearch.Text.Trim();
            foreach (var f in files)
            {
                var desc = DbcLayout.Describe(f);
                if (q.Length > 0 && f.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 && desc.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var name = U.Row(U.T(Path.GetFileNameWithoutExtension(f), 13, "Text", FontWeights.SemiBold, false));
                if (staged.TryGetValue(f, out var n)) name.Children.Add(new Border { Background = U.R("Amber"), CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 0, 6, 1), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = U.T(n.ToString(), 10.5, "AmberInk", FontWeights.Bold, false), ToolTip = $"{n} staged change(s)" });
                if (!DbcLayout.InCatalog(f)) name.Children.Add(U.T("  raw", 10.5, "Faint", null, false));
                var item = new ListBoxItem { Content = U.Stack(name, U.T(desc, 11.5, "Muted", null, true)), Tag = f, ToolTip = f + "\n" + desc };
                tableList.Items.Add(item);
                if (string.Equals(f, file, StringComparison.OrdinalIgnoreCase)) item.IsSelected = true;
            }
        }

        // ================================================================ open
        /// <summary>Opens a table, optionally jumping to an ID. Used by links from other screens.</summary>
        public void OpenTable(string f, long? id = null)
        {
            if (!string.Equals(f, file, StringComparison.OrdinalIgnoreCase)) Open(f);
            if (id != null) { findBox.Text = id.Value.ToString(CultureInfo.InvariantCulture); FindId(); }
        }

        void Open(string f, bool force = false, bool keepPage = false)
        {
            // keep the table we're leaving open in its tab (with any unstaged edits)
            if (file != null && working != null && !string.Equals(f, file, StringComparison.OrdinalIgnoreCase)) tabs.Put(file, Snapshot());
            if (!force && !string.Equals(f, file, StringComparison.OrdinalIgnoreCase) && tabs.TryGet(f, out var saved) && saved != null && saved.Source == source) { Restore(f, saved); return; }
            var src = Source;
            if (src == null) return;
            byte[] bytes;
            try { using (new WaitCursor()) bytes = src.Read(f); }
            catch (Exception ex) { main.Content = U.Callout($"Couldn't read {f}: {Friendly.Text(ex)}", "bad"); return; }
            if (bytes == null) { main.Content = U.Callout($"{f} isn't in this source.", "warn"); return; }
            if (bytes.Length == 0) { file = f; baseTable = working = null; main.Content = U.Card(U.Stack(U.H2(f), U.T(DbcLayout.Describe(f), 13.5, "Muted"), U.Callout("This file is empty (0 bytes), so there is nothing to show or edit.", "info")), new Thickness(24), new Thickness(0)); return; }
            try
            {
                using (new WaitCursor())
                {
                    baseTable = DbcTable.Read(f, bytes);
                    var probs = new List<string>();
                    working = DbcPatcher.Apply(baseTable, S.Changes.Pending.Changes, false, probs);
                    stageProblem = probs.Count > 0 ? string.Join("\n", probs.Take(5)) + (probs.Count > 5 ? $"\n… and {probs.Count - 5} more" : "") : null;
                    working ??= baseTable.Clone();
                }
            }
            catch (Exception ex) { main.Content = U.Callout($"{f} couldn't be read as a DBC: {Friendly.Text(ex)}", "bad"); return; }
            int keep = page;
            file = f; dirty = false; diffKeys = null; onlyDiff.IsChecked = false; linkNames.Clear();
            if (!keepPage) { filterBox.Text = ""; findBox.Text = ""; }
            filtered = Enumerable.Range(0, working.Rows.Count).ToList(); page = 0;
            BuildMain();
            ApplyFilter();
            if (keepPage) GoPage(keep);
            FillTableList();
            tabs.Put(f, Snapshot());
            BuildMain();
        }

        void Restore(string f, TabState st)
        {
            baseTable = st.B; working = st.Wk; dirty = st.Dirty; stageProblem = st.Problem;
            file = f; diffKeys = null; onlyDiff.IsChecked = false; linkNames.Clear();
            filterBox.Text = st.Filter ?? ""; findBox.Text = "";
            filtered = Enumerable.Range(0, working.Rows.Count).ToList(); page = 0;
            tabs.Put(f, st);
            BuildMain();
            ApplyFilter();
            GoPage(st.Page);
            FillTableList();
        }

        void CloseTab(string f)
        {
            bool isCurrent = string.Equals(f, file, StringComparison.OrdinalIgnoreCase);
            bool d = isCurrent ? dirty : tabs.TryGet(f, out var st) && st != null && st.Dirty;
            if (d && MessageBox.Show(w, $"{f} has edits that aren't staged yet. Close it and drop them?", "Unstaged edits", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            if (isCurrent) { file = null; working = baseTable = null; dirty = false; }
            var next = tabs.Close(f);
            if (isCurrent)
            {
                if (next != null) Open(next);
                else main.Content = U.Card(U.Stack(U.H2("Pick a table"), U.T("Every table you open gets a tab, so you can switch between several.", 13.5, "Muted")), new Thickness(28), new Thickness(0));
            }
            else BuildMain();
        }

        UIElement TabStrip()
        {
            var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            foreach (var n in tabs.Names.ToList())
            {
                bool cur = string.Equals(n, file, StringComparison.OrdinalIgnoreCase);
                bool d = cur ? dirty : tabs.TryGet(n, out var st) && st != null && st.Dirty;
                var name = n;
                var label = new Button { Content = Path.GetFileNameWithoutExtension(n) + (d ? " •" : ""), Style = U.S(cur ? "BtnPrimary" : "BtnGhost"), Padding = new Thickness(10, 4, 6, 4), ToolTip = n + (d ? " — has unstaged edits" : "") };
                label.Click += (s, e) => { if (!cur) Open(name); };
                var x = new Button { Content = "×", Style = U.S("BtnGhost"), Padding = new Thickness(6, 4, 8, 4), ToolTip = "Close this tab" };
                x.Click += (s, e) => CloseTab(name);
                var pair = U.Row(label, x); pair.Margin = new Thickness(0, 0, 8, 4);
                wrap.Children.Add(pair);
            }
            return wrap;
        }

        void BuildMain()
        {
            var l = baseTable.Layout;
            header.Children.Clear();
            if (tabs.Names.Count > 1) header.Children.Add(TabStrip());
            var title = U.Row(U.T(file, 20, "Text", FontWeights.SemiBold, false));
            title.Children.Add(new Border { Width = 10 });
            title.Children.Add(U.Pill($"{baseTable.Rows.Count:N0} rows"));
            title.Children.Add(U.Pill($"{l.Columns.Count} columns · {l.FieldCount} fields"));
            if (l.Origin == "catalog") title.Children.Add(U.Pill("verified layout", "OkBg", "Ok", DbcLayout.InCatalog(file) ? "Named, typed columns from the published 3.3.5a layout, checked against this file (column count, record size, every text field)." : null));
            else title.Children.Add(U.Pill("raw mode: types guessed", "AmberDim", "Amber", (l.Note ?? "No published layout for this file.") + "\nText, localized text, decimal numbers and negative numbers are recognized from the values; other columns show as plain numbers."));
            if (!l.HasId) title.Children.Add(U.Pill("rows have no ID", "Panel2", "Muted", "This table has no ID column: rows are identified by their position (row 0, 1, 2…). Rows can only be added or removed at the end."));
            header.Children.Add(title);
            header.Children.Add(U.T(DbcLayout.Describe(file), 13, "Muted", null, true, new Thickness(0, 4, 0, 0)));
            var origin = Source.OriginOf(file);
            if (source == "client" && clientSrc != null)
            {
                var all = clientSrc.ArchivesWith(file);
                var others = all.Where(a => a != origin).Reverse().ToList();
                header.Children.Add(U.T($"The client loads this from {origin}" + (others.Count > 0 ? $" (it wins over {string.Join(", ", others)})." : "."), 12.5, "Faint", null, true, new Thickness(0, 4, 0, 0)));
            }
            else header.Children.Add(U.T("Read from " + origin, 12.5, "Faint", null, true, new Thickness(0, 4, 0, 0)));
            if (stageProblem != null) header.Children.Add(new Border { Margin = new Thickness(0, 10, 0, 0), Child = U.Callout("Some staged changes for this table don't fit this copy, so they aren't shown in the grid (they stay staged):\n" + stageProblem, "warn") });

            // toolbar
            var bar = new WrapPanel { Margin = new Thickness(0, 12, 0, 10) };
            fbHost ??= U.WithPlaceholder(filterBox, "Filter rows (Ctrl+F) – hover for examples"); fbHost.Width = 330; fbHost.Margin = new Thickness(0, 0, 10, 6);
            ibHost ??= U.WithPlaceholder(findBox, "Find ID (Ctrl+G)"); ibHost.Width = 140; ibHost.Margin = new Thickness(0, 0, 10, 6);
            var fb = fbHost; var ib = ibHost;
            foreach (var e in new FrameworkElement[] { fb, ib, pageText, header, statusText, detail }) DetachFrom(e);
            bar.Children.Add(fb); bar.Children.Add(ib);
            pageText.Margin = new Thickness(6, 0, 6, 6); pageText.VerticalAlignment = VerticalAlignment.Center;
            bar.Children.Add(U.Row(Small("◀", () => GoPage(page - 1), "Previous page (Alt+PgUp)"), pageText, Small("▶", () => GoPage(page + 1), "Next page (Alt+PgDn)")));
            allLocales.Margin = new Thickness(14, 0, 14, 6); allLocales.VerticalAlignment = VerticalAlignment.Center;
            onlyDiff.Margin = new Thickness(0, 0, 0, 6); onlyDiff.VerticalAlignment = VerticalAlignment.Center;
            DetachFrom(allLocales); DetachFrom(onlyDiff);
            if (l.Columns.Any(c => c.Kind == DbcKind.Loc)) bar.Children.Add(allLocales);
            bar.Children.Add(onlyDiff);
            var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            actions.Children.Add(U.Btn("Stage edits", Stage, "BtnPrimary", "Write your edits to this table into the changeset (Ctrl+S). Nothing touches any DBC file until you send them out from the Changes screen."));
            actions.Children.Add(U.Btn("Discard", Discard, null, "Drop edits that aren't staged yet."));
            actions.Children.Add(U.Btn("Add row", AddRow, null, l.HasId ? $"New row with the first free ID at or above {S.Profiles.Settings.CustomIdStart} (Settings > Custom ID range)." : "New row at the end."));
            actions.Children.Add(U.Btn("Duplicate row", DuplicateRow, null, "Copy the selected row under a new ID (Ctrl+D)."));
            actions.Children.Add(U.Btn("Remove row", RemoveRows, null, "Remove the selected rows (staged like any other edit)."));
            actions.Children.Add(U.Btn("Export CSV…", ExportCsv, null, "Save the rows shown by the filter (or all rows) as CSV, one column per field."));
            actions.Children.Add(U.Btn("Import CSV…", ImportCsv, null, "Update or add rows from a CSV with the same column names (as exported). Rows not in the CSV are left alone. Imported values are edits: stage them afterwards."));
            actions.Children.Add(U.Btn("Find & replace…", FindReplace, null, "Replace text in the rows the filter shows (one text column or all of them)."));
            actions.Children.Add(U.Btn("Bulk edit…", BulkEdit, null, "Set, add to or multiply one column on every row the filter shows (e.g. all spells of one school)."));
            actions.Children.Add(U.Btn("Columns", ShowColumns, "BtnGhost", "The column definitions: field number, name, type, link and notes."));
            actions.Children.Add(U.Btn("Compare…", CompareCopies, "BtnGhost", "Side by side: every field that differs between this copy and the other source (server ↔ client)."));
            actions.Children.Add(U.Btn("Export SQL…", ExportSql, "BtnGhost", "CREATE TABLE + INSERTs for the rows the filter shows, to load into MySQL."));
            actions.Children.Add(U.Btn("Save copy as…", SaveCopy, "BtnGhost", "Write this table (with your edits) to a new .dbc file of your choice. Never the server or client folders."));
            var top = U.Stack(header, bar, actions);
            var dock = new DockPanel();
            DockPanel.SetDock(top, Dock.Top); dock.Children.Add(top);
            var bottom = U.Stack(statusText, detail);
            bottom.Margin = new Thickness(0, 10, 0, 0);
            DockPanel.SetDock(bottom, Dock.Bottom); dock.Children.Add(bottom);
            DetachFrom(grid);
            dock.Children.Add(grid);
            main.Content = U.Card(dock, new Thickness(20, 16, 20, 14), new Thickness(0));
            RebuildColumns();
        }

        static void DetachFrom(FrameworkElement e) { if (e.Parent is Panel p) p.Children.Remove(e); else if (e.Parent is Decorator d) d.Child = null; else if (e.Parent is ContentControl cc) cc.Content = null; }

        static Button Small(string text, Action a, string tip) { var b = U.Btn(text, a, null, tip); b.MinWidth = 34; b.Padding = new Thickness(8, 4, 8, 4); b.Margin = new Thickness(0, 0, 0, 6); return b; }

        // ================================================================ grid
        void BuildGrid()
        {
            grid.AutoGenerateColumns = false;
            grid.CanUserAddRows = false; grid.CanUserDeleteRows = false; grid.CanUserSortColumns = false; grid.CanUserReorderColumns = false;
            grid.SelectionMode = DataGridSelectionMode.Extended; grid.SelectionUnit = DataGridSelectionUnit.FullRow;
            grid.HeadersVisibility = DataGridHeadersVisibility.Column;
            grid.FrozenColumnCount = 1;
            EnableRowVirtualization(grid);
            grid.CellEditEnding += Grid_CellEditEnding;
            grid.SelectionChanged += (s, e) => ShowDetail();
            grid.MouseDoubleClick += (s, e) =>
            {
                if (grid.CurrentColumn?.Header is FrameworkElement h && h.Tag is DbcColumn c && c.Link != null && grid.CurrentColumn.IsReadOnly) FollowLink(c, SelectedRow());
            };
        }

        static void EnableRowVirtualization(DataGrid g)
        {
            g.EnableRowVirtualization = true; g.EnableColumnVirtualization = true;
            VirtualizingPanel.SetVirtualizationMode(g, VirtualizationMode.Recycling);
        }

        IEnumerable<(DbcColumn col, int field, string header)> VisibleFields()
        {
            foreach (var c in working.Layout.Columns)
            {
                if (c.Kind == DbcKind.Loc)
                {
                    yield return (c, c.Field, c.Name);
                    if (allLocales.IsChecked == true)
                    {
                        for (int k = 1; k < 16; k++) yield return (c, c.Field + k, c.Name + " " + DbcLayout.Locales[k]);
                        yield return (c, c.Field + 16, c.Name + " mask");
                    }
                }
                else yield return (c, c.Field, c.Name);
            }
        }

        void RebuildColumns()
        {
            if (working == null) return;
            grid.Columns.Clear();
            foreach (var (c, f, head) in VisibleFields())
            {
                var tip = $"{head}  ·  {(c.Kind == DbcKind.Loc && f - c.Field < 16 ? "text (" + DbcLayout.Locales[f - c.Field] + ")" : c.Kind == DbcKind.Loc ? "language mask" : c.TypeName)}  ·  field {f}"
                    + (string.IsNullOrEmpty(c.Hint) ? "" : "\n" + c.Hint)
                    + (c.Link != null ? "\nPoints to " + LinkLabel(c.Link) + " (the next column shows the name; double-click it to open)." : "")
                    + (f == 0 && working.Layout.HasId ? "\nThe row's ID." : "")
                    + (c.Guessed ? "\nType guessed from the data (raw mode)." : "");
                var hb = new TextBlock { Text = head, Tag = c, ToolTip = tip };
                var col = new DataGridTextColumn { Header = hb, Binding = new Binding("f" + f) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus }, MaxWidth = c.Kind == DbcKind.String || c.Kind == DbcKind.Loc ? 360 : 200, ElementStyle = CellText(c.Kind != DbcKind.String && c.Kind != DbcKind.Loc), EditingElementStyle = EditStyle() };
                col.CellStyle = ChangedCellStyle(f);
                grid.Columns.Add(col);
                if (c.Link != null)
                {
                    var lh = new TextBlock { Text = "→ " + LinkShort(c.Link), Tag = c, ToolTip = $"Name of the {LinkLabel(c.Link)} row that {head} points to. Double-click to open it." };
                    var lc = new DataGridTextColumn { Header = lh, Binding = new Binding("n" + f), IsReadOnly = true, MaxWidth = 260, ElementStyle = LinkText() };
                    grid.Columns.Add(lc);
                }
            }
            FillPage();
        }

        static string LinkShort(string link) => link.StartsWith("db:") ? link.Substring(3) : Path.GetFileNameWithoutExtension(link);
        static string LinkLabel(string link) => link switch { "db:item" => "item (item_template)", "db:creature" => "creature (creature_template)", "db:quest" => "quest (quest_template)", _ => link };

        static Style CellText(bool right)
        {
            var s = new Style(typeof(TextBlock));
            s.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            s.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
            s.Setters.Add(new Setter(TextBlock.FontFamilyProperty, U.Mono));
            s.Setters.Add(new Setter(TextBlock.FontSizeProperty, 12.5));
            if (right) s.Setters.Add(new Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Right));
            return s;
        }

        static Style LinkText()
        {
            var s = new Style(typeof(TextBlock));
            s.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            s.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
            s.Setters.Add(new Setter(TextBlock.ForegroundProperty, U.R("Amber")));
            s.Setters.Add(new Setter(TextBlock.FontStyleProperty, FontStyles.Italic));
            s.Setters.Add(new Setter(TextBlock.FontSizeProperty, 12.5));
            return s;
        }

        static Style EditStyle()
        {
            var es = new Style(typeof(TextBox));
            es.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(2, 0, 2, 0)));
            es.Setters.Add(new Setter(Control.FontFamilyProperty, U.Mono));
            es.Setters.Add(new Setter(Control.BackgroundProperty, U.R("Input")));
            es.Setters.Add(new Setter(Control.ForegroundProperty, U.R("Text")));
            es.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            es.Setters.Add(new Setter(TextBox.CaretBrushProperty, U.R("Amber")));
            es.Setters.Add(new Setter(TextBox.SelectionBrushProperty, U.R("Maroon")));
            es.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            return es;
        }

        Style ChangedCellStyle(int field)
        {
            var cs = new Style(typeof(DataGridCell), (Style)Application.Current.TryFindResource(typeof(DataGridCell)));
            var mb = new MultiBinding { Converter = new ChangedConverter(this, field) };
            mb.Bindings.Add(new Binding(".")); mb.Bindings.Add(new Binding("f" + field));
            var trig = new DataTrigger { Binding = mb, Value = true };
            trig.Setters.Add(new Setter(Control.BackgroundProperty, U.R("AmberDim")));
            trig.Setters.Add(new Setter(Control.ForegroundProperty, U.R("Amber")));
            cs.Triggers.Add(trig);
            var tipB = new MultiBinding { Converter = new ChangedConverter(this, field, tip: true) };
            tipB.Bindings.Add(new Binding(".")); tipB.Bindings.Add(new Binding("f" + field));
            cs.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, tipB));
            return cs;
        }

        sealed class ChangedConverter : IMultiValueConverter
        {
            readonly DbcPage pg; readonly int field; readonly bool tip;
            public ChangedConverter(DbcPage pg, int field, bool tip = false) { this.pg = pg; this.field = field; this.tip = tip; }
            public object Convert(object[] v, Type t, object p, CultureInfo c)
            {
                if (!(v[0] is DataRowView rv) || pg.working == null) return tip ? null : (object)false;
                int idx = (int)rv["i"];
                if (idx >= pg.working.Rows.Count) return tip ? null : (object)false;
                var row = pg.working.Rows[idx];
                var orig = pg.baseTable.Layout.HasId ? pg.baseTable.Find(row.V[0]) : idx < pg.baseTable.Rows.Count ? pg.baseTable.Rows[idx] : null;
                if (orig == null) return tip ? "New row (not in the source copy)." : (object)true;
                var was = pg.baseTable.FieldText(orig, field);
                bool changed = was != pg.working.FieldText(row, field);
                if (tip) return changed ? "Was " + (was.Length == 0 ? "(empty)" : was) : null;
                return changed;
            }
            public object[] ConvertBack(object v, Type[] t, object p, CultureInfo c) => throw new NotSupportedException();
        }

        // ================================================================ filter / paging
        void ApplyFilter()
        {
            if (working == null) return;
            List<int> rows;
            using (new WaitCursor()) rows = DbcFilter.Apply(working, filterBox.Text, out var err) ?? new List<int>();
            if (onlyDiff.IsChecked == true && diffKeys != null) rows = rows.Where(i => diffKeys.Contains(working.KeyOf(working.Rows[i]))).ToList();
            filtered = rows;
            page = 0;
            FillPage();
        }

        int Pages => Math.Max(1, (filtered.Count + PageSize - 1) / PageSize);

        void GoPage(int p)
        {
            if (working == null) return;
            CommitGridEdit();
            page = Math.Max(0, Math.Min(Pages - 1, p));
            FillPage();
        }

        void CommitGridEdit() { try { grid.CommitEdit(DataGridEditingUnit.Row, true); } catch { } }

        void FillPage()
        {
            if (working == null) return;
            var dt = new DataTable();
            dt.Columns.Add("i", typeof(int));
            var fields = VisibleFields().ToList();
            foreach (var (c, f, _) in fields) { dt.Columns.Add("f" + f, typeof(string)); if (c.Link != null && f == c.Field) dt.Columns.Add("n" + f, typeof(string)); }
            var slice = filtered.Skip(page * PageSize).Take(PageSize).Where(i => i < working.Rows.Count).ToList();
            var linkCols = fields.Where(x => x.col.Link != null && x.field == x.col.Field).Select(x => x.col).ToList();
            foreach (var lc in linkCols) PrimeNames(lc.Link, slice.Select(i => working.Rows[i].V[lc.Field]));
            foreach (var i in slice)
            {
                var r = working.Rows[i];
                var dr = dt.NewRow();
                dr["i"] = i;
                foreach (var (c, f, _) in fields)
                {
                    dr["f" + f] = working.FieldText(r, f);
                    if (c.Link != null && f == c.Field) dr["n" + f] = LinkName(c.Link, r.V[f]);
                }
                dt.Rows.Add(dr);
            }
            dt.AcceptChanges();
            pageData = dt;
            grid.ItemsSource = dt.DefaultView;
            int from = filtered.Count == 0 ? 0 : page * PageSize + 1, to = Math.Min(filtered.Count, (page + 1) * PageSize);
            pageText.Text = $"{from:N0}–{to:N0} of {filtered.Count:N0}" + (filtered.Count != working.Rows.Count ? $" (filtered from {working.Rows.Count:N0})" : "") + $"  ·  page {page + 1}/{Pages}";
            UpdateStatus();
            ShowDetail();
        }

        void UpdateStatus()
        {
            int staged = S.Changes.Pending.Changes.Count(c => DbcStager.IsDbc(c) && string.Equals(c.Table, file, StringComparison.OrdinalIgnoreCase));
            var parts = new List<string>();
            if (working != null && filtered.Count == 0) parts.Add(onlyDiff.IsChecked == true ? "No rows differ from the other copy." : "No rows match the filter.");
            parts.Add(dirty ? "You have edits that aren't staged yet: Stage edits (Ctrl+S) to keep them." : "No unstaged edits.");
            if (staged > 0) parts.Add($"{staged} staged change(s) for this table are shown in amber.");
            if (diffNote != null) parts.Add(diffNote);
            statusText.Text = string.Join("  ", parts);
            statusText.Foreground = U.R(dirty ? "Amber" : "Muted");
        }

        void FindId()
        {
            if (working == null) return;
            uint id;
            if (uint.TryParse(findBox.Text.Trim(), out var u)) id = u;
            else if (int.TryParse(findBox.Text.Trim(), out var neg)) id = unchecked((uint)neg);
            else { w.Toast("Type an ID number.", "warn"); return; }
            int idx = working.IndexOf(id);
            if (idx < 0) { w.Toast($"{file} has no row {(working.Layout.HasId ? "with ID " : "")}{id}.", "warn"); return; }
            int pos = filtered.IndexOf(idx);
            if (pos < 0) { filterBox.Text = ""; onlyDiff.IsChecked = false; ApplyFilter(); pos = filtered.IndexOf(idx); }
            page = pos / PageSize;
            FillPage();
            var item = grid.Items.OfType<DataRowView>().FirstOrDefault(rv => (int)rv["i"] == idx);
            if (item != null) { grid.SelectedItem = item; grid.ScrollIntoView(item); }
        }

        void EnsureDiff()
        {
            if (diffKeys != null) return;
            var other = Other;
            if (other == null) { diffNote = "The other copy isn't available (set the client folder in Settings)."; diffKeys = new HashSet<uint>(); return; }
            try
            {
                using (new WaitCursor())
                {
                    var b = other.Read(file);
                    if (b == null || b.Length == 0) { diffNote = $"The {(source == "client" ? "server" : "client")} has no {file}."; diffKeys = new HashSet<uint>(); return; }
                    var o = DbcTable.Read(file, b, working.Layout.FieldCount == new DbcFile(b).Fields ? working.Layout : null);
                    var keys = new HashSet<uint>();
                    int added = 0, changed = 0, missing = 0;
                    foreach (var r in working.Rows)
                    {
                        var k = working.KeyOf(r);
                        var orow = o.Layout.HasId ? o.Find(k) : k < o.Rows.Count ? o.Rows[(int)k] : null;
                        if (orow == null) { keys.Add(k); added++; continue; }
                        for (int f = 0; f < working.Layout.FieldCount && f < o.Layout.FieldCount; f++)
                            if (working.FieldText(r, f) != o.FieldText(orow, f)) { keys.Add(k); changed++; break; }
                    }
                    missing = o.Rows.Count(r => working.IndexOf(o.KeyOf(r)) < 0);
                    diffKeys = keys;
                    var otherName = source == "client" ? "server copy" : "client";
                    diffNote = keys.Count == 0 && missing == 0 ? $"Same as the {otherName}." : $"Compared with the {otherName}: {changed} row(s) differ, {added} only here, {missing} only there.";
                }
            }
            catch (Exception ex) { diffNote = "Couldn't compare: " + Friendly.Text(ex); diffKeys = new HashSet<uint>(); }
        }

        // ================================================================ link names
        void PrimeNames(string link, IEnumerable<uint> ids)
        {
            if (!link.StartsWith("db:")) { LinkTable(link); return; }
            if (!linkNames.TryGetValue(link, out var d)) linkNames[link] = d = new Dictionary<uint, string>();
            var need = ids.Where(i => i != 0 && !d.ContainsKey(i)).Distinct().Take(500).ToList();
            if (need.Count == 0 || !S.HasReadAccount) return;
            var (table, key, name) = link switch { "db:item" => ("item_template", "entry", "name"), "db:creature" => ("creature_template", "entry", "name"), _ => ("quest_template", "ID", "LogTitle") };
            try
            {
                using var db = S.OpenRead(S.Profile.WorldDb);
                var t = db.Query($"SELECT {key}, {name} FROM {table} WHERE {key} IN ({string.Join(",", need)})", need.Count);
                foreach (var r in t.Rows) d[Convert.ToUInt32(r[0])] = Convert.ToString(r[1]);
                foreach (var i in need) if (!d.ContainsKey(i)) d[i] = "(not in the database)";
            }
            catch { foreach (var i in need) d[i] = null; }
        }

        Dictionary<uint, string> LinkTable(string link)
        {
            if (linkNames.TryGetValue(link, out var d)) return d;
            d = new Dictionary<uint, string>();
            try
            {
                var b = Source?.Read(link) ?? serverSrc?.Read(link);
                if (b != null && b.Length > 0)
                {
                    var t = DbcTable.Read(link, b);
                    var nc = t.Layout.NameColumn;
                    // tables without text (SpellCastTimes, SpellDuration…) are summed up by their first values
                    var summary = t.Layout.Columns.Skip(1).Where(c => c.Kind != DbcKind.String && c.Kind != DbcKind.Loc).Take(2).ToList();
                    if (t.Layout.HasId)
                        foreach (var r in t.Rows)
                            d[r.V[0]] = nc != null && !string.IsNullOrEmpty(r.S[nc.Field]) ? r.S[nc.Field]
                                : summary.All(c => t.FieldText(r, c.Field) is "0" or "0x0") ? "" : string.Join(", ", summary.Select(c => c.Name + " " + t.FieldText(r, c.Field)));
                }
            }
            catch { }
            linkNames[link] = d;
            return d;
        }

        string LinkName(string link, uint id)
        {
            if (id == 0 || id == uint.MaxValue) return "";
            if (link.StartsWith("db:")) return linkNames.TryGetValue(link, out var dd) && dd.TryGetValue(id, out var n) ? n ?? "" : "";
            var d = LinkTable(link);
            if (!d.TryGetValue(id, out var s)) return d.Count == 0 ? "" : "(missing)";
            return s.Length == 0 ? "#" + id : s;
        }

        // ================================================================ editing
        void Grid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit || working == null) return;
            if (!(e.Row.Item is DataRowView rv) || !(e.EditingElement is TextBox tb)) return;
            if (!(e.Column.Header is TextBlock hb) || !(hb.Tag is DbcColumn c) || !(((DataGridTextColumn)e.Column).Binding is Binding b) || !b.Path.Path.StartsWith("f")) return;
            int field = int.Parse(b.Path.Path.Substring(1), CultureInfo.InvariantCulture);
            var row = working.Rows[(int)rv["i"]];
            var before = working.FieldText(row, field);
            if (tb.Text == before) return;
            if (field == 0 && working.Layout.HasId && uint.TryParse(tb.Text, out var nid) && nid != row.V[0] && working.Find(nid) != null)
            { e.Cancel = true; w.Toast($"ID {nid} is already used in {file}. Press Esc to cancel.", "warn"); return; }
            if (!working.TrySetField(row, field, tb.Text, out var err)) { e.Cancel = true; w.Toast($"{working.Layout.FieldName(field)}: {err} Press Esc to cancel.", "warn"); return; }
            if (field == 0) working.InvalidateIndex();
            tb.Text = working.FieldText(row, field);
            dirty = true; diffKeys = null; onlyDiff.IsChecked = false;
            if (c.Link != null && field == c.Field)
            {
                PrimeNames(c.Link, new[] { row.V[field] });
                var nm = LinkName(c.Link, row.V[field]);
                grid.Dispatcher.BeginInvoke(new Action(() => { try { rv.Row["n" + field] = nm; } catch { } }));
            }
            UpdateStatus();
            grid.Dispatcher.BeginInvoke(new Action(ShowDetail));
        }

        DbcRow SelectedRow() => grid.SelectedItem is DataRowView rv && working != null ? working.Rows[(int)rv["i"]] : null;
        List<int> SelectedIndexes() => grid.SelectedItems.OfType<DataRowView>().Select(rv => (int)rv["i"]).ToList();

        void AddRow()
        {
            if (working == null) return;
            CommitGridEdit();
            var r = working.NewRow();
            if (working.Layout.HasId) r.V[0] = working.NextFreeId((uint)Math.Min(uint.MaxValue, Math.Max(1, S.Profiles.Settings.CustomIdStart)));
            foreach (var c in working.Layout.Columns.Where(c => c.Kind == DbcKind.Loc)) r.V[c.Field + 16] = MaskOf(c);
            working.Rows.Add(r); working.InvalidateIndex();
            AfterRowChange(working.Rows.Count - 1, $"Added row {(working.Layout.HasId ? "ID " + r.V[0] : "#" + (working.Rows.Count - 1))}. Fill it in, then Stage edits.");
        }

        /// <summary>Language mask copied from the first row (the client's usual value), so new localized text shows.</summary>
        uint MaskOf(DbcColumn c) => working.Rows.Count > 0 ? working.Rows[0].V[c.Field + 16] : 0xFF01FEu;

        void DuplicateRow()
        {
            var src = SelectedRow();
            if (src == null) { w.Toast("Select a row to duplicate.", "warn"); return; }
            CommitGridEdit();
            var r = src.Clone();
            if (working.Layout.HasId) r.V[0] = working.NextFreeId((uint)Math.Min(uint.MaxValue, Math.Max(1, S.Profiles.Settings.CustomIdStart)));
            working.Rows.Add(r); working.InvalidateIndex();
            AfterRowChange(working.Rows.Count - 1, $"Copied as {(working.Layout.HasId ? "ID " + r.V[0] : "a new last row")}. Stage edits to keep it.");
        }

        void RemoveRows()
        {
            var idx = SelectedIndexes();
            if (idx.Count == 0) { w.Toast("Select the rows to remove.", "warn"); return; }
            if (!working.Layout.HasId && idx.Any(i => i < working.Rows.Count - idx.Count)) { w.Toast("This table has no ID column, so only rows at the end can be removed.", "warn"); return; }
            CommitGridEdit();
            foreach (var i in idx.OrderByDescending(x => x)) working.Rows.RemoveAt(i);
            working.InvalidateIndex();
            dirty = true; diffKeys = null;
            ApplyFilterKeepPage();
            w.Toast($"Removed {idx.Count} row(s). Stage edits to keep the removal.", "ok");
        }

        void AfterRowChange(int index, string toast)
        {
            dirty = true; diffKeys = null; onlyDiff.IsChecked = false;
            filterBox.Text = "";
            ApplyFilter();
            int pos = filtered.IndexOf(index);
            page = Math.Max(0, pos / PageSize);
            FillPage();
            var item = grid.Items.OfType<DataRowView>().FirstOrDefault(rv => (int)rv["i"] == index);
            if (item != null) { grid.SelectedItem = item; grid.ScrollIntoView(item); }
            w.Toast(toast, "ok");
        }

        void ApplyFilterKeepPage() { int p = page; ApplyFilter(); page = Math.Min(p, Pages - 1); FillPage(); }

        void Stage()
        {
            if (working == null) return;
            CommitGridEdit();
            int n = DbcStager.Restage(S.Changes.Pending, baseTable, working, source);
            S.SaveChanges();
            dirty = false;
            stageProblem = null;
            UpdateStatus(); FillTableList();
            w.Toast(n == 0 ? $"{file} has no changes against the {(source == "client" ? "client" : "server")} copy; nothing staged." : $"Staged {n} row change(s) for {file}. Nothing is written to any DBC file until you send them out from Changes.", "ok");
        }

        void Discard()
        {
            if (working == null) return;
            dirty = false;
            Open(file, force: true, keepPage: true);
            w.Toast("Unstaged edits dropped.", "info");
        }

        // ================================================================ CSV
        void ExportCsv()
        {
            if (working == null) return;
            var dlg = new Microsoft.Win32.SaveFileDialog { FileName = Path.GetFileNameWithoutExtension(file) + (filtered.Count != working.Rows.Count ? "_filtered" : "") + ".csv", Filter = "CSV (*.csv)|*.csv" };
            if (dlg.ShowDialog(w) != true) return;
            File.WriteAllText(dlg.FileName, DbcCsv.Export(working, filtered), new UTF8Encoding(true));
            w.Toast($"Saved {filtered.Count:N0} rows to {dlg.FileName}.", "ok");
        }

        void ImportCsv()
        {
            if (working == null) return;
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "CSV (*.csv)|*.csv" };
            if (dlg.ShowDialog(w) != true) return;
            CommitGridEdit();
            DbcCsv.ImportResult r;
            using (new WaitCursor()) r = DbcCsv.Import(working, File.ReadAllText(dlg.FileName));
            if (r.Errors.Count > 0)
            {
                MessageBox.Show(w, "Nothing was imported:\n\n" + string.Join("\n", r.Errors.Take(12)) + (r.Errors.Count > 12 ? $"\n… and {r.Errors.Count - 12} more" : ""), "CSV import", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (r.Updated + r.Added > 0) { dirty = true; diffKeys = null; }
            ApplyFilterKeepPage();
            w.Toast($"CSV: {r.Updated} row(s) updated, {r.Added} added, {r.Unchanged} unchanged" + (r.Ignored.Count > 0 ? $"; ignored unknown columns: {string.Join(", ", r.Ignored.Take(5))}" : "") + ". Stage edits to keep them.", "ok");
        }

        // ================================================================ WDBX-style tools
        Window Small(string title, UIElement body, double width = 560)
        {
            var win = new Window { Owner = w, Width = width, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Background = U.R("Panel"), FontFamily = U.Font, Foreground = U.R("Text") };
            System.Windows.Shell.WindowChrome.SetWindowChrome(win, new System.Windows.Shell.WindowChrome { CaptionHeight = 30, GlassFrameThickness = new Thickness(0), ResizeBorderThickness = new Thickness(0) });
            win.Content = new Border { BorderBrush = U.R("LineHi"), BorderThickness = new Thickness(1), Padding = new Thickness(22, 18, 22, 18), Child = U.Stack(U.H2(title), body) };
            return win;
        }

        ComboBox FieldPicker(bool textOnly, bool allOption)
        {
            var cb = new ComboBox { Width = 320 };
            if (allOption) cb.Items.Add(new ComboBoxItem { Content = "(all text columns)", Tag = -1 });
            for (int f = 0; f < working.Layout.FieldCount; f++)
                if (!textOnly || working.Layout.FieldIsString[f]) cb.Items.Add(new ComboBoxItem { Content = $"{working.Layout.FieldName(f)}  ({(working.Layout.FieldIsString[f] ? "text" : working.Layout.ColumnOf(f).TypeName)})", Tag = f });
            cb.SelectedIndex = 0;
            return cb;
        }

        void FindReplace()
        {
            if (working == null) return;
            if (!Enumerable.Range(0, working.Layout.FieldCount).Any(f => working.Layout.FieldIsString[f])) { w.Toast($"{file} has no text columns.", "warn"); return; }
            CommitGridEdit();
            var find = U.Box(); var repl = U.Box(); var col = FieldPicker(true, true);
            var mc = new CheckBox { Content = U.T("Match case", 13, "Text", null, false), Margin = new Thickness(0, 4, 0, 10) };
            Window win = null;
            var go = U.Btn("Replace in " + filtered.Count.ToString("N0") + " row(s)", () =>
            {
                var fields = col.SelectedItem is ComboBoxItem ci && (int)ci.Tag >= 0 ? new[] { (int)ci.Tag } : Enumerable.Range(0, working.Layout.FieldCount).ToArray();
                int n = DbcEdit.Replace(working, filtered, fields, find.Text, repl.Text, mc.IsChecked == true);
                win.Close();
                if (n > 0) { dirty = true; diffKeys = null; FillPage(); }
                w.Toast(n == 0 ? "Nothing matched." : $"Replaced in {n} field(s). Stage edits to keep it.", n == 0 ? "info" : "ok");
            }, "BtnPrimary");
            var cancel = U.Btn("Cancel", () => win.Close(), "BtnGhost"); cancel.IsCancel = true;
            win = Small("Find & replace in " + file, U.Stack(U.Field("Find", null, find), U.Field("Replace with", null, repl), U.Field("In column", "Only the rows the current filter shows are changed.", col), mc, U.Row(go, cancel)));
            win.ShowDialog();
        }

        void BulkEdit()
        {
            if (working == null) return;
            CommitGridEdit();
            var col = FieldPicker(false, false); col.SelectedIndex = Math.Min(1, col.Items.Count - 1);
            var op = new ComboBox { Width = 200 }; foreach (var o in new[] { "Set to", "Add", "Multiply by" }) op.Items.Add(o); op.SelectedIndex = 0;
            var val = U.Box("", true);
            Window win = null;
            var go = U.Btn($"Apply to {filtered.Count:N0} row(s)", () =>
            {
                int f = (int)((ComboBoxItem)col.SelectedItem).Tag;
                int n = DbcEdit.Bulk(working, filtered, f, (DbcEdit.Op)op.SelectedIndex, val.Text, out var err);
                if (err != null && n == 0) { w.Toast(err, "warn"); return; }
                win.Close();
                if (n > 0) { dirty = true; diffKeys = null; FillPage(); }
                w.Toast((err != null ? err + " " : "") + $"Changed {n} row(s). Stage edits to keep it.", n == 0 ? "info" : "ok");
            }, "BtnPrimary");
            var cancel = U.Btn("Cancel", () => win.Close(), "BtnGhost"); cancel.IsCancel = true;
            win = Small("Bulk edit " + file, U.Stack(U.T($"Changes one column on the {filtered.Count:N0} row(s) the current filter shows. Filter first (e.g. SchoolMask=4 for fire spells).", 12.5, "Muted", null, true, new Thickness(0, 0, 0, 10)), U.Field("Column", null, col), U.Field("Operation", null, op), U.Field("Value", "A number, 0x… hex, a decimal, or text.", val), U.Row(go, cancel)));
            win.ShowDialog();
        }

        void ShowColumns()
        {
            if (working == null) return;
            var l = working.Layout;
            var rows = l.Columns.Select(c => new { Field = c.FieldCount > 1 ? $"{c.Field}–{c.Field + c.FieldCount - 1}" : c.Field.ToString(), c.Name, Type = c.TypeName, Link = c.Link ?? "", Notes = (c.Guessed ? "guessed · " : "") + (c.Hint ?? "") }).ToList();
            var g = new DataGrid { IsReadOnly = true, AutoGenerateColumns = true, ItemsSource = rows, MaxHeight = 520 };
            g.AutoGeneratingColumn += (s, e) => { e.Column.Header = new TextBlock { Text = e.PropertyName }; e.Column.MaxWidth = 360; };
            var close = U.Btn("Close", null, "BtnPrimary"); close.Margin = new Thickness(0, 12, 0, 0); close.HorizontalAlignment = HorizontalAlignment.Left;
            var win = Small($"{file}: {l.Columns.Count} columns, {l.FieldCount} fields, {l.RecordSize}-byte records ({(l.Origin == "catalog" ? "verified layout" : "guessed")})", U.Stack(g, close), 900);
            close.Click += (s, e) => win.Close();
            if (w.IsScreens) { ScreensLastWindow = win; win.WindowStartupLocation = WindowStartupLocation.Manual; win.Left = -20000; win.Top = -20000; win.ShowActivated = false; win.Show(); } else win.ShowDialog();
        }
        public Window ScreensLastWindow;
        public void ScreensColumns() => ShowColumns();

        void CompareCopies()
        {
            if (working == null) return;
            var other = Other;
            var b = other?.Read(file);
            if (b == null || b.Length == 0) { w.Toast($"The {(source == "client" ? "server" : "client")} has no {file} to compare with.", "warn"); return; }
            List<(uint key, string field, string here, string there)> diff;
            using (new WaitCursor()) diff = DbcSql.Compare(working, DbcTable.Read(file, b, working.Layout.FieldCount == new DbcFile(b).Fields ? working.Layout : null));
            var here = source == "client" ? "Client (with your edits)" : "Server (with your edits)";
            var there = source == "client" ? "Server copy" : "Client";
            var g = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, MaxHeight = 520, ItemsSource = diff.Select(d => new { Row = d.key, Field = d.field, Here = d.here, There = d.there }).ToList() };
            g.Columns.Add(new DataGridTextColumn { Header = "Row", Binding = new Binding("Row"), Width = new DataGridLength(90) });
            g.Columns.Add(new DataGridTextColumn { Header = "Field", Binding = new Binding("Field"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            g.Columns.Add(new DataGridTextColumn { Header = here, Binding = new Binding("Here"), Width = new DataGridLength(1.3, DataGridLengthUnitType.Star) });
            g.Columns.Add(new DataGridTextColumn { Header = there, Binding = new Binding("There"), Width = new DataGridLength(1.3, DataGridLengthUnitType.Star) });
            var close = U.Btn("Close", null, "BtnPrimary"); close.Margin = new Thickness(0, 12, 0, 0); close.HorizontalAlignment = HorizontalAlignment.Left;
            var win = Small($"{file}: {here} vs {there}", U.Stack(U.T(diff.Count == 0 ? "The two copies are identical." : $"{diff.Count:N0} difference(s){(diff.Count >= 2000 ? " (first 2,000 shown)" : "")}.", 13, "Muted", null, true, new Thickness(0, 0, 0, 10)), g, close), 960);
            close.Click += (s, e) => win.Close();
            if (w.IsScreens) { ScreensLastWindow = win; win.WindowStartupLocation = WindowStartupLocation.Manual; win.Left = -20000; win.Top = -20000; win.ShowActivated = false; win.Show(); } else win.ShowDialog();
        }

        void ExportSql()
        {
            if (working == null) return;
            var dlg = new Microsoft.Win32.SaveFileDialog { FileName = Path.GetFileNameWithoutExtension(file).ToLowerInvariant() + "_dbc.sql", Filter = "SQL (*.sql)|*.sql" };
            if (dlg.ShowDialog(w) != true) return;
            File.WriteAllText(dlg.FileName, DbcSql.Export(working, filtered), new UTF8Encoding(false));
            w.Toast($"Saved {filtered.Count:N0} rows as SQL to {dlg.FileName}.", "ok");
        }

        void SaveCopy()
        {
            if (working == null) return;
            var dlg = new Microsoft.Win32.SaveFileDialog { FileName = file, Filter = "DBC (*.dbc)|*.dbc" };
            if (dlg.ShowDialog(w) != true) return;
            var target = Path.GetFullPath(dlg.FileName);
            var forbidden = new[] { ServerDbcDir, ClientDataDir }.Where(x => x != null).Select(x => Path.GetFullPath(x).TrimEnd('\\') + "\\");
            if (forbidden.Any(fd => target.StartsWith(fd, StringComparison.OrdinalIgnoreCase)))
            { w.Toast("Studio doesn't write into the server's dbc folder or the game client. Pick another folder; use Changes to send edits out safely.", "warn"); return; }
            CommitGridEdit();
            File.WriteAllBytes(target, working.Write());
            w.Toast($"Saved {working.Rows.Count:N0} rows to {target}.", "ok");
        }

        // ================================================================ detail / links
        void ShowDetail()
        {
            detail.Children.Clear();
            var row = SelectedRow();
            if (row == null || working == null) return;
            var links = working.Layout.Columns.Where(c => c.Link != null && row.V[c.Field] != 0 && row.V[c.Field] != uint.MaxValue).ToList();
            var key = working.Layout.HasId ? "ID " + row.V[0] : "row " + working.Rows.IndexOf(row);
            var nc = working.Layout.NameColumn;
            var title = $"Selected: {key}" + (nc != null && !string.IsNullOrEmpty(row.S[nc.Field]) ? " · " + row.S[nc.Field] : "");
            detail.Children.Add(U.T(title, 13, "Text", FontWeights.SemiBold, true, new Thickness(0, 6, 0, 4)));
            if (links.Count == 0) { detail.Children.Add(U.T("No links from this row.", 12.5, "Faint")); return; }
            var wrap = new WrapPanel();
            foreach (var c in links.Take(24))
            {
                var id = row.V[c.Field];
                var nm = LinkName(c.Link, id);
                var label = $"{c.Name} {(c.Kind == DbcKind.Int ? unchecked((int)id).ToString() : id.ToString())} → {LinkShort(c.Link)}" + (string.IsNullOrEmpty(nm) ? "" : ": " + (nm.Length > 34 ? nm.Substring(0, 34) + "…" : nm));
                var b = U.Btn(label, () => FollowLink(c, row), null, c.Link.StartsWith("db:") ? $"Open this {LinkShort(c.Link)} in its editor." : $"Open {c.Link} at ID {id}.");
                b.Margin = new Thickness(0, 0, 8, 6); b.Padding = new Thickness(10, 3, 10, 3); b.FontSize = 12;
                wrap.Children.Add(b);
            }
            if (links.Count > 24) wrap.Children.Add(U.T($"+{links.Count - 24} more", 12, "Faint"));
            detail.Children.Add(wrap);
        }

        void FollowLink(DbcColumn c, DbcRow row)
        {
            if (row == null) return;
            long id = c.Kind == DbcKind.Int ? unchecked((int)row.V[c.Field]) : row.V[c.Field];
            if (c.Link.StartsWith("db:"))
            {
                var ws = Refs.Workspace(c.Link.Substring(3));
                if (ws != null) w.Navigate(ws, pg => ((EntityEditorPage)pg).Open(id));
                else w.Toast("No editor for that yet.", "warn");
                return;
            }
            if (dirty && MessageBox.Show(w, $"{file} has edits that aren't staged yet. Stage them before opening {c.Link}?", "Unstaged edits", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) Stage();
            dirty = false;
            OpenTable(c.Link, id);
        }

        sealed class WaitCursor : IDisposable
        {
            readonly Cursor prev;
            public WaitCursor() { prev = Mouse.OverrideCursor; Mouse.OverrideCursor = Cursors.Wait; }
            public void Dispose() => Mouse.OverrideCursor = prev;
        }

        // ================================================================ screenshots
        public void ScreensFilter(string text) { filterBox.Text = text; ApplyFilter(); }
        public void ScreensAllLocales(bool on) => allLocales.IsChecked = on;
        public void ScreensSelectFirst() { if (grid.Items.Count > 0) grid.SelectedIndex = 0; }
        public void ScreensSource(string s) { (s == "client" ? srcClient : srcServer).IsChecked = true; }
        public void ScreensOnlyDiff(bool on) => onlyDiff.IsChecked = on;
        public void ScreensEdit(long id, string column, string text)
        {
            var r = working.Find(unchecked((uint)id)); if (r == null) return;
            int f = working.Layout.FieldByName(column); if (f < 0) f = working.Layout.Columns.First(c => c.Name == column).Field;
            working.TrySetField(r, f, text, out _); dirty = true; FillPage();
        }
        public void ScreensStage() => Stage();
        public void ScreensScrollToColumn(string name)
        {
            var col = grid.Columns.FirstOrDefault(c => c.Header is TextBlock tb && tb.Text == name);
            var item = grid.SelectedItem ?? (grid.Items.Count > 0 ? grid.Items[0] : null);
            if (col == null || item == null) return;
            grid.UpdateLayout();
            // scroll past the column, then back, so it ends up near the left edge
            var last = grid.Columns[grid.Columns.Count - 1];
            grid.ScrollIntoView(item, last); grid.UpdateLayout();
            grid.ScrollIntoView(item, col);
        }
        public void ScreensTableSearch(string q) => tableSearch.Text = q;
    }
}
