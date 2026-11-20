using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using AndersCraftStudio.Core;
using Microsoft.Win32;

namespace AndersCraftStudio
{
    /// <summary>Database tree, table list, paged data grid, filter, CSV export and the query editor (F5).</summary>
    public sealed class SqlBrowserPage : IPage
    {
        readonly MainWindow w;
        Studio S => w.S;
        readonly Grid view = new Grid();
        public UIElement View => view;

        // state
        string db;                 // database being browsed
        string role;               // world | sandbox | characters | auth
        string table;
        IList<string> pk = new List<string>();
        int page;
        long total;
        string filter = "";
        DataTable data;
        readonly HashSet<DataRow> stagedDeletes = new HashSet<DataRow>();
        List<(string name, long rows, string comment)> tables = new List<(string, long, string)>();
        public IEnumerable<string> LoadedTables => tables.Select(t => t.name);
        bool sandboxExists, authVisible = true;

        // controls
        readonly StackPanel dbList = new StackPanel();
        readonly ListBox tableList = new ListBox();
        readonly TextBox tableFilter = U.Box();
        readonly TextBlock tblName = U.T("", 20, "Text", FontWeights.SemiBold, false);
        readonly TextBlock tblDesc = U.T("", 13, "Muted", null, true);
        readonly WrapPanel tblPills = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        readonly DataGrid grid = new DataGrid();
        readonly TextBox whereBox = U.Box("", true);
        readonly TextBlock pageLabel = U.T("", 13, "Muted", null, false);
        readonly TextBlock gridHint = U.T("", 12.5, "Muted", null, true);
        Button prevBtn, nextBtn;
        RadioButton tabData, tabQuery;
        readonly Grid dataPane = new Grid(), queryPane = new Grid();
        readonly TextBox editor = U.Code("", 170, false);
        readonly DataGrid resultGrid = new DataGrid { IsReadOnly = true };
        readonly ContentControl queryMsg = new ContentControl { Focusable = false };
        readonly ContentControl mainHost = new ContentControl { Focusable = false };
        UIElement browser, setupNeeded;
        DataTable lastResult;

        public SqlBrowserPage(MainWindow w)
        {
            this.w = w;
            view.Margin = new Thickness(28, 22, 28, 20);
            view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            view.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
            var strip = U.StepStrip(1); DockPanel.SetDock(strip, Dock.Right); strip.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(strip);
            head.Children.Add(U.Stack(U.H1("SQL browser"), U.T("Look at any table, filter it, export it, or run read-only queries. Edits and write statements are staged, never run.", 13.5, "Muted")));
            view.Children.Add(head);

            Grid.SetRow(mainHost, 1);
            view.Children.Add(mainHost);
            browser = BuildBrowser();
            setupNeeded = BuildSetupNeeded();
            S.ChangesChanged += () => { if (data != null && w.CurrentPage == "sql") OverlayStaged(); };
        }

        public void OnShow()
        {
            if (!S.HasReadAccount) { mainHost.Content = setupNeeded; return; }
            mainHost.Content = browser;
            if (db == null) { LoadDatabases(); SelectDb("world"); }
            else if (data != null) OverlayStaged();
        }

        // ================================================================== layout
        UIElement BuildSetupNeeded()
        {
            var sp = new StackPanel { MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Left };
            sp.Children.Add(new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 30, Foreground = U.R("Amber"), Margin = new Thickness(0, 0, 0, 14) });
            sp.Children.Add(U.H2("Finish setup step 3 to start browsing"));
            sp.Children.Add(U.T("The SQL browser only ever connects with Studio's read-only account (studio_ro), so it can't change anything by accident. That account hasn't been set up yet, or its password isn't saved.", 13.5, "Muted", null, true, new Thickness(0, 0, 0, 10)));
            sp.Children.Add(U.T("Step 3 gives you a short SQL script. Run it once as a MySQL administrator, press \"Test the accounts\", and come back here.", 13.5, "Muted", null, true, new Thickness(0, 0, 0, 18)));
            sp.Children.Add(U.Row(U.Btn("Open setup at step 3", () => w.ShowWizard(3, true), "BtnPrimary", "Opens the setup wizard directly at Safe database accounts."),
                                  U.Btn("Why two accounts?", () => w.Toast("studio_ro can only read, so browsing is always safe. studio_rw can only change rows in the world database and the sandbox, and Studio uses it only when you press Apply.", "info"), "BtnGhost")));
            return U.Card(sp, new Thickness(32), new Thickness(0, 10, 0, 0));
        }

        UIElement BuildBrowser()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // ---- left: databases + tables
            var left = new DockPanel();
            var dbCard = U.Card(U.Stack(U.Eyebrow("Databases"), dbList), new Thickness(12, 14, 12, 10), new Thickness(0, 0, 0, 12));
            DockPanel.SetDock(dbCard, Dock.Top); left.Children.Add(dbCard);
            var tl = new DockPanel();
            tableFilter.TextChanged += (s, e) => FillTables();
            tableFilter.ToolTip = "Type part of a table name or description to narrow the list. Down arrow moves into the list.";
            U.AutomationName(tableFilter, "Filter tables");
            tableFilter.PreviewKeyDown += (s, e) => { if (e.Key == Key.Down && tableList.Items.Count > 0) { tableList.SelectedIndex = 0; ((ListBoxItem)tableList.Items[0]).Focus(); e.Handled = true; } };
            var tf = U.WithPlaceholder(tableFilter, "Filter tables…"); tf.Margin = new Thickness(0, 0, 0, 8);
            DockPanel.SetDock(tf, Dock.Top); tl.Children.Add(tf);
            tableList.SelectionChanged += (s, e) => { if (tableList.SelectedItem is ListBoxItem li && li.Tag is string t && t != table) OpenTable(null, t); };
            tl.Children.Add(tableList);
            left.Children.Add(U.Card(tl, new Thickness(10, 12, 6, 8), new Thickness(0)));
            g.Children.Add(left);

            // ---- right: table header + tabs
            var right = new DockPanel();
            Grid.SetColumn(right, 2);
            var headCard = U.Card(U.Stack(tblName, tblDesc, tblPills), new Thickness(20, 14, 20, 14), new Thickness(0, 0, 0, 12));
            tblName.FontFamily = U.Mono;
            DockPanel.SetDock(headCard, Dock.Top); right.Children.Add(headCard);

            tabData = new RadioButton { Style = U.S("SegBtn"), Content = "Data", GroupName = "sqltab", IsChecked = true, ToolTip = "Browse the table page by page. Double-click a cell to edit (staged)." };
            tabQuery = new RadioButton { Style = U.S("SegBtn"), Content = "Query  (F5)", GroupName = "sqltab", ToolTip = "Type SQL. SELECT runs on the read-only account; INSERT/UPDATE/DELETE are staged, not run." };
            tabStructure = new RadioButton { Style = U.S("SegBtn"), Content = "Structure", GroupName = "sqltab", ToolTip = "Columns, indexes, foreign keys and the CREATE TABLE statement (read-only)." };
            tabData.Checked += (s, e) => ShowTab(false);
            tabQuery.Checked += (s, e) => ShowTab(true);
            tabStructure.Checked += (s, e) => ShowStructure();
            var tabs = new Border { Background = U.R("Panel"), BorderBrush = U.R("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12), Child = U.Row(tabData, tabQuery, tabStructure) };
            DockPanel.SetDock(tabs, Dock.Top); right.Children.Add(tabs);

            BuildDataPane();
            BuildQueryPane();
            var panes = new Grid(); panes.Children.Add(dataPane); panes.Children.Add(queryPane); panes.Children.Add(structPane);
            structPane.Visibility = Visibility.Collapsed;
            queryPane.Visibility = Visibility.Collapsed;
            right.Children.Add(panes);
            g.Children.Add(right);

            U.OnKey(view, Key.F5, ModifierKeys.None, () => { if (tabQuery.IsChecked != true) tabQuery.IsChecked = true; RunQuery(); });
            U.OnKey(view, Key.Enter, ModifierKeys.Control, () => { if (tabQuery.IsChecked == true) RunQuery(); });
            return g;
        }

        RadioButton tabStructure;
        readonly ContentControl structPane = new ContentControl { Focusable = false };
        readonly QueryHistory history = new QueryHistory(System.IO.Path.Combine(ProfileStore.DefaultRoot, "query_history.json"));
        List<DataTable> results = new List<DataTable>();
        readonly WrapPanel resultTabs = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };

        public void ScreensStructure() => tabStructure.IsChecked = true;

        void ShowStructure()
        {
            dataPane.Visibility = Visibility.Collapsed; queryPane.Visibility = Visibility.Collapsed; structPane.Visibility = Visibility.Visible;
            if (table == null) { structPane.Content = U.Callout("Pick a table on the left to see its structure.", "info"); return; }
            var sp = new StackPanel();
            try
            {
                using var c = S.OpenRead(db);
                string Q(string x) => SqlGen.Literal(x);
                var cols = c.QueryTable($"SELECT COLUMN_NAME AS `Column`, COLUMN_TYPE AS `Type`, IS_NULLABLE AS `Null`, COLUMN_KEY AS `Key`, COLUMN_DEFAULT AS `Default`, EXTRA AS `Extra`, COLUMN_COMMENT AS `Comment` FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = {Q(db)} AND TABLE_NAME = {Q(table)} ORDER BY ORDINAL_POSITION", 2000, out _);
                var idx = c.QueryTable($"SELECT INDEX_NAME AS `Index`, GROUP_CONCAT(COLUMN_NAME ORDER BY SEQ_IN_INDEX SEPARATOR ', ') AS `Columns`, IF(NON_UNIQUE = 0, 'unique', '') AS `Unique`, INDEX_TYPE AS `Type` FROM INFORMATION_SCHEMA.STATISTICS WHERE TABLE_SCHEMA = {Q(db)} AND TABLE_NAME = {Q(table)} GROUP BY INDEX_NAME, NON_UNIQUE, INDEX_TYPE ORDER BY INDEX_NAME = 'PRIMARY' DESC, INDEX_NAME", 500, out _);
                var fks = c.QueryTable($"SELECT CONSTRAINT_NAME AS `Foreign key`, COLUMN_NAME AS `Column`, REFERENCED_TABLE_NAME AS `Points to table`, REFERENCED_COLUMN_NAME AS `Points to column` FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE WHERE TABLE_SCHEMA = {Q(db)} AND TABLE_NAME = {Q(table)} AND REFERENCED_TABLE_NAME IS NOT NULL", 500, out _);
                var create = c.Query($"SHOW CREATE TABLE {SqlGen.Ident(table)}", 1);
                DataGrid G(DataTable t) { var g = new DataGrid { IsReadOnly = true, AutoGenerateColumns = true, ItemsSource = t.DefaultView, MaxHeight = 420, Margin = new Thickness(0, 0, 0, 14) }; g.AutoGeneratingColumn += (s2, e2) => { e2.Column.Header = new TextBlock { Text = e2.PropertyName }; e2.Column.MaxWidth = 380; }; return g; }
                sp.Children.Add(U.H2($"Columns ({cols.Rows.Count})"));
                sp.Children.Add(G(cols));
                sp.Children.Add(U.H2($"Indexes ({idx.Rows.Count})"));
                sp.Children.Add(G(idx));
                sp.Children.Add(U.H2($"Foreign keys ({fks.Rows.Count})"));
                if (fks.Rows.Count == 0) sp.Children.Add(U.T("None. AzerothCore's world tables don't declare foreign keys; Studio's editors link IDs for you instead.", 12.5, "Muted", null, true, new Thickness(0, 0, 0, 14)));
                else sp.Children.Add(G(fks));
                sp.Children.Add(U.H2("CREATE TABLE"));
                var code = U.Code(create.Rows.Count > 0 ? Convert.ToString(create.Rows[0][1]) : "", 260);
                sp.Children.Add(code);
                sp.Children.Add(U.Btn("Copy", () => { try { Clipboard.SetText(code.Text); w.Toast("Copied.", "ok"); } catch { } }, "BtnGhost"));
            }
            catch (Exception ex) { sp.Children.Add(U.Callout("Couldn't read the structure: " + Friendly.Text(ex), "bad")); }
            structPane.Content = U.Scroll(sp);
        }

        void ShowTab(bool query)
        {
            structPane.Visibility = Visibility.Collapsed;
            dataPane.Visibility = query ? Visibility.Collapsed : Visibility.Visible;
            queryPane.Visibility = query ? Visibility.Visible : Visibility.Collapsed;
            if (query) { if (string.IsNullOrWhiteSpace(editor.Text) && table != null) editor.Text = $"SELECT *\nFROM {SqlGen.Ident(table)}\nLIMIT 100;"; editor.Focus(); }
        }

        void BuildDataPane()
        {
            dataPane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            dataPane.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            dataPane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            prevBtn = U.Btn("‹ Prev", () => { if (page > 0) { page--; LoadPage(); } }, null, "Previous page (Ctrl+Page Up)");
            nextBtn = U.Btn("Next ›", () => { page++; LoadPage(); }, null, "Next page (Ctrl+Page Down)");
            pageLabel.VerticalAlignment = VerticalAlignment.Center; pageLabel.Margin = new Thickness(4, 0, 14, 0);
            var export = U.Btn("Export CSV…", ExportTableCsv, null, "Save every row that matches the current filter (not just this page) as a CSV file for Excel or Sheets.");
            var refresh = U.Btn("Refresh", () => LoadPage(), "BtnGhost", "Reload this page from the database.");
            var dump = U.Btn("Export SQL…", ExportTableSql, null, "Save this table as a .sql file: its CREATE TABLE and INSERTs for every row that matches the filter. Read-only.");
            var find = U.Btn("Find text…", FindText, null, "Find text in any column of this table (builds the filter for you).");
            var import = U.Btn("Import CSV…", ImportTableCsv, null, "Add or update rows of this table from a CSV file: map its columns, preview, then stage it (with undo). Nothing is written directly.");
            var rightBar = U.Row(prevBtn, pageLabel, nextBtn, find, export, import, dump, refresh);
            rightBar.Children[rightBar.Children.Count - 1].SetValue(FrameworkElement.MarginProperty, new Thickness(0));
            DockPanel.SetDock(rightBar, Dock.Right); bar.Children.Add(rightBar);

            whereBox.ToolTip = "A SQL condition (the part after WHERE). Examples:  entry = 1234   ·   name LIKE '%wolf%'   ·   minlevel >= 70 AND rank = 3.  Press Enter to apply, Esc to clear.";
            U.AutomationName(whereBox, "Row filter (WHERE)");
            whereBox.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { ApplyFilter(); e.Handled = true; }
                else if (e.Key == Key.Escape) { whereBox.Text = ""; ApplyFilter(); e.Handled = true; }
            };
            var wl = new Border { Background = U.R("Panel2"), BorderBrush = U.R("Line"), BorderThickness = new Thickness(1, 1, 0, 1), CornerRadius = new CornerRadius(9, 0, 0, 9), Padding = new Thickness(10, 0, 10, 0),
                Child = U.T("WHERE", 12, "Amber", FontWeights.Bold, false) };
            ((TextBlock)wl.Child).VerticalAlignment = VerticalAlignment.Center;
            var wg = new DockPanel { Margin = new Thickness(0, 0, 10, 0) };
            DockPanel.SetDock(wl, Dock.Left); wg.Children.Add(wl);
            wg.Children.Add(U.WithPlaceholder(whereBox, "e.g. entry = 1234  or  name LIKE '%wolf%'   (Enter)"));
            var apply = U.Btn("Filter", ApplyFilter, "BtnPrimary", "Show only rows that match the condition. Runs a read-only SELECT.");
            DockPanel.SetDock(apply, Dock.Right); bar.Children.Add(apply);
            bar.Children.Add(wg);
            dataPane.Children.Add(bar);

            grid.AutoGenerateColumns = true;
            grid.AutoGeneratingColumn += Grid_AutoGeneratingColumn;
            grid.BeginningEdit += Grid_BeginningEdit;
            grid.CellEditEnding += Grid_CellEditEnding;
            grid.RowStyle = RowStyle();
            grid.ContextMenu = BuildRowMenu();
            grid.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.PageDown && Keyboard.Modifiers == ModifierKeys.Control) { nextBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); e.Handled = true; }
                if (e.Key == Key.PageUp && Keyboard.Modifiers == ModifierKeys.Control) { prevBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); e.Handled = true; }
            };
            U.AutomationName(grid, "Table rows");
            Grid.SetRow(grid, 1); dataPane.Children.Add(grid);
            gridHint.Margin = new Thickness(2, 10, 0, 0);
            Grid.SetRow(gridHint, 2); dataPane.Children.Add(gridHint);
        }

        void BuildQueryPane()
        {
            queryPane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            queryPane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            queryPane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            queryPane.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            editor.ToolTip = "Type one or more SQL statements separated by ;  F5 or Ctrl+Enter runs them (or just the selected text). SELECT / SHOW / DESCRIBE / EXPLAIN run read-only. INSERT / UPDATE / DELETE are added to your changeset instead of running.";
            U.AutomationName(editor, "SQL query editor");
            editor.Margin = new Thickness(0, 0, 0, 10);
            queryPane.Children.Add(editor);
            var bar = U.Row(
                U.Btn("Run  (F5)", RunQuery, "BtnPrimary", "Run the query (or just the selected text). Reads run now; writes are staged."),
                U.Btn("Export result CSV…", ExportResultCsv, null, "Save the result grid below as CSV."),
                U.Btn("History…", ShowHistory, null, "The last 100 queries you ran (per Windows user). Pick one to load it."),
                U.Btn("Open .sql…", OpenSqlFile, null, "Load a .sql file into the editor. F5 then runs its reads and stages its writes."),
                U.Btn("Stage .sql script…", StageSqlFile, null, "Turn a whole .sql file (e.g. a module's or a dump's INSERTs) into staged changes. Nothing runs; session lines like SET NAMES are skipped."),
                U.Btn("Clear", () => { editor.Clear(); queryMsg.Content = null; resultGrid.ItemsSource = null; editor.Focus(); }, "BtnGhost", "Empty the editor."));
            bar.Children.Add(U.T("Reads run on the read-only account. Writes are staged, never run.", 12.5, "Ok", null, false, new Thickness(8, 0, 0, 0)));
            ((TextBlock)bar.Children[bar.Children.Count - 1]).VerticalAlignment = VerticalAlignment.Center;
            bar.Margin = new Thickness(0, 0, 0, 10);
            Grid.SetRow(bar, 1); queryPane.Children.Add(bar);
            var msgStack = U.Stack(queryMsg, resultTabs);
            Grid.SetRow(msgStack, 2); queryPane.Children.Add(msgStack);
            resultGrid.AutoGeneratingColumn += (s, e) => { e.Column.Header = new TextBlock { Text = e.PropertyName }; if (e.Column is DataGridTextColumn tc) tc.ElementStyle = CellTextStyle(); };
            Grid.SetRow(resultGrid, 3); queryPane.Children.Add(resultGrid);
        }

        // ================================================================== databases & tables
        void LoadDatabases()
        {
            try
            {
                using var c = S.OpenRead(S.Profile.WorldDb);
                var dbs = c.Databases();
                sandboxExists = dbs.Contains(S.Profile.SandboxDb, StringComparer.OrdinalIgnoreCase);
                authVisible = dbs.Contains(S.Profile.AuthDb, StringComparer.OrdinalIgnoreCase) && S.Profile.AllowAuthView;
                if (S.Profile.SandboxExists != sandboxExists) { S.Profile.SandboxExists = sandboxExists; if (!S.DemoConf) S.Profiles.Save(S.Profile); }
            }
            catch (Exception ex) { w.Toast("Couldn't list databases: " + Friendly.Text(ex), "bad"); }
            BuildDbList();
        }

        void BuildDbList()
        {
            dbList.Children.Clear();
            void Item(string r, string name, string badge, string bbg, string bfg, string tip)
            {
                var g = new DockPanel();
                var pill = U.Pill(badge, bbg, bfg); DockPanel.SetDock(pill, Dock.Right); pill.Margin = new Thickness(6, 0, 0, 0);
                g.Children.Add(pill);
                g.Children.Add(new TextBlock { Text = name, FontFamily = U.Mono, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                var rb = new RadioButton { Style = U.S("NavBtn"), Content = g, GroupName = "db", Tag = r, ToolTip = tip, IsChecked = role == r };
                rb.Checked += (s, e) => { if (role != r) SelectDb(r); };
                dbList.Children.Add(rb);
            }
            var p = S.Profile;
            Item("world", p.WorldDb, "world · read-only", "OkBg", "Ok", "Your live world database. Browsing is read-only; edits you make are staged in the changeset, not written.");
            if (sandboxExists) Item("sandbox", p.SandboxDb, "sandbox", "AmberDim", "Amber", "The practice copy of the world database. Edits are staged the same way; Apply can target it safely.");
            Item("characters", p.CharactersDb, "view-only", "Panel2", "Muted", "Characters, inventories, guilds. View-only in Studio: edits and write statements are refused here.");
            if (authVisible) Item("auth", p.AuthDb, "view-only", "Panel2", "Muted", "Accounts, bans and the realm list. View-only in Studio.");
        }

        public void SelectDb(string r)
        {
            var p = S.Profile;
            role = r;
            db = r switch { "sandbox" => p.SandboxDb, "characters" => p.CharactersDb, "auth" => p.AuthDb, _ => p.WorldDb };
            foreach (RadioButton rb in dbList.Children) rb.IsChecked = (string)rb.Tag == r;
            try
            {
                using var c = S.OpenRead(db);
                tables = c.Tables();
            }
            catch (Exception ex) { tables = new List<(string, long, string)>(); w.Toast($"Couldn't read the table list of {db}: {Friendly.Text(ex)}", "bad"); }
            table = null;
            FillTables();
            var first = tables.FirstOrDefault(t => t.name == "creature_template").name ?? tables.FirstOrDefault().name;
            if (first != null) OpenTable(null, first);
        }

        bool Editable => role == "world" || role == "sandbox";

        void FillTables()
        {
            var q = tableFilter.Text.Trim();
            tableList.Items.Clear();
            foreach (var t in tables)
            {
                var desc = TableCatalog.Describe(t.name) ?? (string.IsNullOrEmpty(t.comment) ? null : t.comment);
                if (q.Length > 0 && !t.name.Contains(q, StringComparison.OrdinalIgnoreCase) && !(desc ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
                var top = new DockPanel();
                var cnt = U.T(FormatCount(t.rows), 11.5, "Faint", null, false); DockPanel.SetDock(cnt, Dock.Right);
                top.Children.Add(cnt);
                top.Children.Add(new TextBlock { Text = t.name, FontFamily = U.Mono, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis });
                var sp = U.Stack(top);
                if (desc != null) sp.Children.Add(U.T(desc, 11.5, "Muted", null, false, new Thickness(0, 2, 0, 0)));
                var li = new ListBoxItem { Content = sp, Tag = t.name, ToolTip = desc == null ? t.name : t.name + "\n" + desc };
                tableList.Items.Add(li);
                if (t.name == table) li.IsSelected = true;
            }
        }

        static string FormatCount(long n) => n >= 1_000_000 ? (n / 1_000_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "M" : n >= 10_000 ? (n / 1000.0).ToString("0", CultureInfo.InvariantCulture) + "k" : n.ToString("N0", CultureInfo.InvariantCulture);

        /// <summary>Opens a table. dbRole null = keep the current database (world when nothing is open yet).</summary>
        public void OpenTable(string dbRole, string name)
        {
            if (!S.HasReadAccount) { OnShow(); return; }
            if (db == null) { mainHost.Content = browser; LoadDatabases(); SelectDb(dbRole ?? "world"); }
            else if (dbRole != null && dbRole != role) SelectDb(dbRole);
            if (!tables.Any(t => t.name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                if (role != "world" && dbRole == null) { SelectDb("world"); }
                if (!tables.Any(t => t.name.Equals(name, StringComparison.OrdinalIgnoreCase))) { w.Toast($"{name} isn't in {db}.", "warn"); return; }
            }
            table = tables.First(t => t.name.Equals(name, StringComparison.OrdinalIgnoreCase)).name;
            foreach (ListBoxItem li in tableList.Items) if ((string)li.Tag == table) { li.IsSelected = true; li.BringIntoView(); }
            page = 0; filter = ""; whereBox.Text = "";
            try { using var c = S.OpenRead(db); pk = c.PrimaryKey(table); } catch { pk = new List<string>(); }
            tblName.Text = table;
            tblDesc.Text = TableCatalog.Describe(table) ?? "No description yet for this table. Hover column headers for key information.";
            tblPills.Children.Clear();
            tblPills.Children.Add(U.Pill(TableCatalog.CategoryOf(table), "MaroonDim", "Text", "Which Studio area this table belongs to."));
            tblPills.Children.Add(U.Pill(db, "Panel2", "Muted", "Database"));
            tblPills.Children.Add(U.Pill(pk.Count > 0 ? "key: " + string.Join(", ", pk) : "no primary key", "Panel2", pk.Count > 0 ? "Muted" : "Amber",
                pk.Count > 0 ? "The primary key identifies each row. Studio uses it to target edits precisely." : "Without a primary key Studio can't pinpoint a single row, so cell editing is off for this table. You can still stage SQL in the Query tab."));
            if (!Editable) tblPills.Children.Add(U.Pill("view-only", "Panel2", "Muted", "Studio only stages changes for the world database; this one is view-only."));
            else if (pk.Count > 0) tblPills.Children.Add(U.Pill("double-click to edit → staged", "AmberDim", "Amber", "Your edit goes into the changeset. The database is untouched until you Apply."));
            if (tabQuery.IsChecked == true && string.IsNullOrWhiteSpace(editor.Text)) editor.Text = $"SELECT *\nFROM {SqlGen.Ident(table)}\nLIMIT 100;";
            LoadPage();
        }

        void ApplyFilter()
        {
            var f = whereBox.Text.Trim();
            if (f.Length > 0)
            {
                var st = SqlClassifier.ClassifyOne("SELECT 1 FROM " + SqlGen.Ident(table ?? "t") + " WHERE " + f);
                if (st.Kind != StatementKind.Read) { w.Toast("That filter isn't a plain condition: " + st.Reason, "bad"); return; }
            }
            filter = f; page = 0; LoadPage();
        }

        string BaseQuery() => "SELECT * FROM " + SqlGen.Ident(table) + (filter.Length > 0 ? " WHERE " + filter : "") + (pk.Count > 0 ? " ORDER BY " + string.Join(", ", pk.Select(SqlGen.Ident)) : "");

        public void LoadPage()
        {
            if (table == null) return;
            int size = Math.Max(20, S.Profiles.Settings.PageSize);
            try
            {
                using var c = S.OpenRead(db);
                total = c.Count(table, filter.Length > 0 ? filter : null);
                long pages = Math.Max(1, (total + size - 1) / size);
                if (page >= pages) page = (int)pages - 1;
                data = c.QueryTable(BaseQuery() + $" LIMIT {size} OFFSET {(long)page * size}", size, out _);
                pageLabel.Text = $"Page {page + 1:N0} of {pages:N0} · {total:N0} rows";
                prevBtn.IsEnabled = page > 0; nextBtn.IsEnabled = page + 1 < pages;
            }
            catch (Exception ex)
            {
                data = null; grid.ItemsSource = null;
                w.Toast("Couldn't load rows: " + Friendly.Text(ex), "bad");
                return;
            }
            stagedDeletes.Clear();
            grid.IsReadOnly = !Editable || pk.Count == 0;
            grid.ItemsSource = data.DefaultView;
            OverlayStaged();
            gridHint.Text = !Editable ? "View-only database. You can filter, export and query, but not stage changes here."
                : pk.Count == 0 ? "This table has no primary key, so cell editing is off. Use the Query tab to stage SQL."
                : "Double-click a cell (or press F2) to edit. Your edit is staged in the changeset (amber), not saved. Right-click a row for more. Type NULL to clear a value.";
        }

        // ================================================================== staged overlay
        List<KeyPart> KeyOf(DataRow r) => pk.Select(c => new KeyPart(c, SqlGen.Literal(r.HasVersion(DataRowVersion.Original) ? r[c, DataRowVersion.Original] : r[c]),
                                                                      SqlGen.Display(r.HasVersion(DataRowVersion.Original) ? r[c, DataRowVersion.Original] : r[c]))).ToList();

        void OverlayStaged()
        {
            if (data == null || pk.Count == 0 || !Editable) return;
            data.RejectChanges();
            stagedDeletes.Clear();
            var mine = S.Changes.Pending.Changes.Where(c => string.Equals(c.Table, table, StringComparison.OrdinalIgnoreCase) && (c.Kind == ChangeKind.Update || c.Kind == ChangeKind.Delete)).ToList();
            if (mine.Count > 0)
            {
                var index = new Dictionary<string, DataRow>();
                foreach (DataRow r in data.Rows) index[string.Join("|", KeyOf(r).Select(k => k.Value))] = r;
                foreach (var c in mine)
                {
                    if (!index.TryGetValue(string.Join("|", c.Key.Select(k => k.Value)), out var row)) continue;
                    if (c.Kind == ChangeKind.Delete) { stagedDeletes.Add(row); continue; }
                    foreach (var f in c.Fields)
                    {
                        if (!data.Columns.Contains(f.Name)) continue;
                        var v = FromDisplay(f.AfterDisplay, f.After, data.Columns[f.Name].DataType);
                        if (v != null) try { row[f.Name] = v; } catch { }
                    }
                }
            }
            grid.Items.Refresh();
        }

        static object FromDisplay(string display, string literal, Type t)
        {
            if (literal == "NULL") return DBNull.Value;
            try
            {
                if (t == typeof(string)) return display;
                if (t == typeof(byte[])) return null;
                if (t == typeof(DateTime)) return DateTime.Parse(display, CultureInfo.InvariantCulture);
                return Convert.ChangeType(display, t, CultureInfo.InvariantCulture);
            }
            catch { return null; }
        }

        // ================================================================== grid columns & editing
        Style CellTextStyle()
        {
            var s = new Style(typeof(TextBlock));
            s.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            s.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
            return s;
        }

        void Grid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            var name = e.PropertyName;
            bool isKey = pk.Contains(name, StringComparer.OrdinalIgnoreCase);
            var col = data?.Columns.Contains(name) == true ? data.Columns[name] : null;
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            if (isKey) head.Children.Add(new Border { Background = U.R("AmberDim"), CornerRadius = new CornerRadius(4), Padding = new Thickness(4, 0, 4, 1), Margin = new Thickness(0, 0, 6, 0), Child = U.T("key", 10.5, "Amber", FontWeights.Bold, false) });
            head.Children.Add(new TextBlock { Text = name });
            var meaning = TableCatalog.FieldMeaning(table, name);
            var type = col?.ExtendedProperties["dbtype"] as string;
            head.ToolTip = name + (type != null ? "  (" + type.ToLowerInvariant() + ")" : "") + (isKey ? "\nPart of the primary key: identifies the row, so it can't be edited here." : "") + (meaning.Length > 0 ? "\n" + meaning : "");
            e.Column.Header = head;
            e.Column.MaxWidth = 420;
            if (isKey) e.Column.IsReadOnly = true;
            if (e.Column is DataGridTextColumn tc)
            {
                tc.ElementStyle = CellTextStyle();
                var es = new Style(typeof(TextBox));
                es.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(2, 0, 2, 0)));
                es.Setters.Add(new Setter(Control.FontFamilyProperty, U.Mono));
                es.Setters.Add(new Setter(Control.BackgroundProperty, U.R("Input")));
                es.Setters.Add(new Setter(Control.ForegroundProperty, U.R("Text")));
                es.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
                es.Setters.Add(new Setter(TextBox.CaretBrushProperty, U.R("Amber")));
                es.Setters.Add(new Setter(TextBox.SelectionBrushProperty, U.R("Maroon")));
                es.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
                tc.EditingElementStyle = es;
                // staged cells turn amber
                var cs = new Style(typeof(DataGridCell), (Style)Application.Current.TryFindResource(typeof(DataGridCell)));
                var mb = new MultiBinding { Converter = new StagedCellConverter(name, false) };
                mb.Bindings.Add(new Binding(".")); mb.Bindings.Add(tc.Binding is Binding b ? new Binding(b.Path.Path) : new Binding(name));
                cs.Setters.Add(new Setter(Control.BackgroundProperty, mb));
                var mf = new MultiBinding { Converter = new StagedCellConverter(name, true) };
                mf.Bindings.Add(new Binding(".")); mf.Bindings.Add(tc.Binding is Binding b2 ? new Binding(b2.Path.Path) : new Binding(name));
                cs.Setters.Add(new Setter(Control.ForegroundProperty, mf));
                tc.CellStyle = cs;
                if (col != null && (col.DataType != typeof(string))) tc.ElementStyle.Setters.Add(new Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Right));
            }
        }

        sealed class StagedCellConverter : IMultiValueConverter
        {
            readonly string col; readonly bool fg;
            public StagedCellConverter(string col, bool fg) { this.col = col; this.fg = fg; }
            public object Convert(object[] v, Type t, object p, CultureInfo c)
            {
                if (v.Length > 0 && v[0] is DataRowView rv && rv.Row.HasVersion(DataRowVersion.Original) && rv.Row.Table.Columns.Contains(col))
                {
                    try
                    {
                        if (!Equals(rv.Row[col, DataRowVersion.Original], rv.Row[col, DataRowVersion.Current]) && !(rv.Row[col, DataRowVersion.Original] is byte[]))
                            return fg ? U.R("Amber") : U.R("AmberDim");
                    }
                    catch { }
                }
                return fg ? U.R("Text") : Brushes.Transparent;
            }
            public object[] ConvertBack(object v, Type[] t, object p, CultureInfo c) => throw new NotSupportedException();
        }

        Style RowStyle()
        {
            var s = new Style(typeof(DataGridRow));
            var bg = new Binding(".") { Converter = new DeletedRowConverter(this, false) };
            s.Setters.Add(new Setter(DataGridRow.ToolTipProperty, new Binding(".") { Converter = new DeletedRowConverter(this, true) }));
            var trig = new DataTrigger { Binding = bg, Value = true };
            trig.Setters.Add(new Setter(Control.BackgroundProperty, U.R("BadBg")));
            trig.Setters.Add(new Setter(Control.ForegroundProperty, U.R("Bad")));
            trig.Setters.Add(new Setter(UIElement.OpacityProperty, 0.85));
            s.Triggers.Add(trig);
            return s;
        }

        sealed class DeletedRowConverter : IValueConverter
        {
            readonly SqlBrowserPage pg; readonly bool tip;
            public DeletedRowConverter(SqlBrowserPage pg, bool tip) { this.pg = pg; this.tip = tip; }
            public object Convert(object v, Type t, object p, CultureInfo c)
            {
                bool del = v is DataRowView rv && pg.stagedDeletes.Contains(rv.Row);
                if (tip) return del ? "This row is staged for removal. Remove the change on the Changes screen to keep it." : null;
                return del;
            }
            public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
        }

        void Grid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            if (!Editable || pk.Count == 0) { e.Cancel = true; return; }
            var name = ColumnName(e.Column);
            if (pk.Contains(name, StringComparer.OrdinalIgnoreCase)) { e.Cancel = true; w.Toast("Key columns identify the row, so they can't be edited here. To move a row, stage SQL in the Query tab.", "warn"); return; }
            if (e.Row.Item is DataRowView rv && stagedDeletes.Contains(rv.Row)) { e.Cancel = true; w.Toast("This row is staged for removal. Remove that change first to edit it.", "warn"); return; }
            if (data.Columns[name].DataType == typeof(byte[])) { e.Cancel = true; w.Toast("Binary columns can't be edited in the grid. Stage an UPDATE in the Query tab with a hex value (X'0A1B').", "warn"); }
        }

        static string ColumnName(DataGridColumn c) => c.Header is StackPanel sp ? ((TextBlock)sp.Children[sp.Children.Count - 1]).Text : c.Header as string ?? c.Header?.ToString();

        void Grid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (!(e.Row.Item is DataRowView rv) || !(e.EditingElement is TextBox tb)) return;
            var name = ColumnName(e.Column);
            var col = data.Columns[name];
            var row = rv.Row;
            var original = row.HasVersion(DataRowVersion.Original) ? row[name, DataRowVersion.Original] : row[name];
            var current = row[name];
            var text = tb.Text;
            var dg = (DataGrid)sender;
            if (SqlGen.Display(current) == text || (current is DBNull && text == "NULL")) { e.Cancel = true; dg.Dispatcher.BeginInvoke(new Action(() => dg.CancelEdit(DataGridEditingUnit.Row))); return; }
            bool nullable = col.ExtendedProperties["nullable"] is bool nb ? nb : true;
            if (!SqlGen.TryTypedLiteral(text, col.DataType, nullable, out var lit, out var err))
            {
                e.Cancel = true;
                w.Toast($"{name}: {err} Press Esc to cancel.", "warn");
                return;
            }
            var key = KeyOf(row);
            var change = ChangeFactory.StageCellEdit(S.Changes.Pending, table, key, name, SqlGen.Literal(original), SqlGen.Display(original), lit, lit == "NULL" ? "NULL" : text);
            e.Cancel = true;
            dg.Dispatcher.BeginInvoke(new Action(() =>
            {
                dg.CancelEdit(DataGridEditingUnit.Row);
                S.SaveChanges(); // fires ChangesChanged → OverlayStaged refreshes the amber cells
            }));
            w.Toast(change == null ? $"{name} is back to its original value; that edit was removed from the changeset." : $"Staged: {table} {string.Join(", ", key.Select(k => k.Column + "=" + k.Display))} · {name} → {(lit == "NULL" ? "NULL" : text)}. Nothing written yet.", "ok");
        }

        ContextMenu BuildRowMenu()
        {
            var m = new ContextMenu();
            MenuItem Item(string h, Action a) { var i = new MenuItem { Header = h }; i.Click += (s, e) => a(); m.Items.Add(i); return i; }
            var del = Item("Stage removing this row", StageDeleteSelected);
            Item("Copy row as INSERT statement", CopyRowInsert);
            Item("Copy value", () => { if (grid.CurrentCell.Item is DataRowView rv && grid.CurrentCell.Column != null) try { Clipboard.SetText(SqlGen.Display(rv.Row[ColumnName(grid.CurrentCell.Column)])); } catch { } });
            Item("Filter by this value", () =>
            {
                if (grid.CurrentCell.Item is DataRowView rv && grid.CurrentCell.Column != null)
                {
                    var n = ColumnName(grid.CurrentCell.Column); var v = rv.Row.HasVersion(DataRowVersion.Original) ? rv.Row[n, DataRowVersion.Original] : rv.Row[n];
                    whereBox.Text = v is DBNull ? SqlGen.Ident(n) + " IS NULL" : SqlGen.Ident(n) + " = " + SqlGen.Literal(v); ApplyFilter();
                }
            });
            m.Opened += (s, e) => del.IsEnabled = Editable && pk.Count > 0;
            return m;
        }

        DataRow SelectedRow() => (grid.CurrentCell.Item as DataRowView ?? grid.SelectedCells.Select(c => c.Item).OfType<DataRowView>().FirstOrDefault())?.Row;

        void StageDeleteSelected()
        {
            var row = SelectedRow();
            if (row == null) { w.Toast("Click a cell in the row first.", "warn"); return; }
            if (stagedDeletes.Contains(row)) { w.Toast("That row is already staged for removal.", "info"); return; }
            var cols = data.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
            var vals = cols.Select(c => row.HasVersion(DataRowVersion.Original) ? row[c, DataRowVersion.Original] : row[c]).ToList();
            var key = KeyOf(row);
            ChangeFactory.StageRowDelete(S.Changes.Pending, table, key, cols, vals);
            S.SaveChanges();
            w.Toast($"Staged removing {table} ({string.Join(", ", key.Select(k => k.Column + "=" + k.Display))}). Undo SQL re-inserts the full row.", "ok");
        }

        void CopyRowInsert()
        {
            var row = SelectedRow(); if (row == null) return;
            var cols = data.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
            var sql = SqlGen.InsertRow(table, cols, cols.Select(c => SqlGen.Literal(row.HasVersion(DataRowVersion.Original) ? row[c, DataRowVersion.Original] : row[c])).ToList());
            try { Clipboard.SetText(sql); w.Toast("INSERT statement copied. Paste it in the Query tab, change the key, and run it to stage a new row.", "ok"); } catch { }
        }

        // ================================================================== export
        void ExportTableCsv()
        {
            if (table == null) return;
            var d = new SaveFileDialog { FileName = table + (filter.Length > 0 ? "_filtered" : "") + ".csv", Filter = "CSV file|*.csv" };
            if (d.ShowDialog(w) != true) return;
            var sql = BaseQuery(); var file = d.FileName; var dbName = db;
            w.Toast($"Exporting {total:N0} rows…", "info");
            Task.Run(() =>
            {
                using var c = S.OpenRead(dbName);
                using var sw = new StreamWriter(file, false, new UTF8Encoding(true));
                c.StreamCsv(sql, sw);
            }).ContinueWith(t => w.Dispatcher.Invoke(() => w.Toast(t.Exception == null ? $"Exported to {file}" : "Export failed: " + t.Exception.GetBaseException().Message, t.Exception == null ? "ok" : "bad")));
        }

        public CsvImportWindow ScreensCsvImport(string file) => table == null || data == null ? null : new CsvImportWindow(w, db, table, pk, data, file);

        void ImportTableCsv()
        {
            if (table == null || data == null) { w.Toast("Open a table first.", "warn"); return; }
            if (!Editable) { w.Toast("CSV import stages changes, so it works on the world and sandbox databases only.", "warn"); return; }
            var d = new OpenFileDialog { Filter = "CSV file|*.csv|All files|*.*" };
            if (d.ShowDialog(w) != true) return;
            try
            {
                var dlg = new CsvImportWindow(w, db, table, pk, data, d.FileName);
                if (dlg.ShowDialog() == true) OverlayStaged();
            }
            catch (Exception ex) { w.Toast("Can't read that CSV: " + Friendly.Text(ex), "bad"); }
        }

        void ExportResultCsv()
        {
            if (lastResult == null) { w.Toast("Run a query first.", "warn"); return; }
            var d = new SaveFileDialog { FileName = "query_result.csv", Filter = "CSV file|*.csv" };
            if (d.ShowDialog(w) != true) return;
            using var sw = new StreamWriter(d.FileName, false, new UTF8Encoding(true));
            sw.WriteLine(string.Join(",", lastResult.Columns.Cast<DataColumn>().Select(c => Csv.Escape(c.ColumnName))));
            foreach (DataRow r in lastResult.Rows) sw.WriteLine(string.Join(",", r.ItemArray.Select(v => Csv.Escape(v is DBNull ? "" : SqlGen.Display(v)))));
            w.Toast("Saved " + d.FileName, "ok");
        }

        // ================================================================== query editor
        public void SetQuery(string sql) { tabQuery.IsChecked = true; editor.Text = sql; }

        public void RunQuery()
        {
            if (db == null) return;
            var text = editor.SelectionLength > 0 ? editor.SelectedText : editor.Text;
            var all = SqlClassifier.Classify(text);
            if (all.Count == 0) { queryMsg.Content = U.Callout("Type a statement first, for example SELECT * FROM creature_template WHERE entry = 1;", "info"); return; }
            var blocked = all.Where(s => s.Kind == StatementKind.Blocked).ToList();
            if (blocked.Count > 0)
            {
                queryMsg.Content = U.Callout(string.Join("\n", blocked.Select(b => "• " + Short(b.Text) + "\n   " + b.Reason)) + "\n\nNothing was run or staged.", "bad", "Studio won't run this");
                return;
            }
            var writes = all.Where(s => s.IsWrite).ToList();
            var reads = all.Where(s => s.Kind == StatementKind.Read).ToList();
            var notes = new List<string>();
            if (writes.Count > 0)
            {
                if (!Editable)
                {
                    queryMsg.Content = U.Callout($"{db} is view-only in Studio. Write statements here are refused, not staged. Switch to {S.Profile.WorldDb} to stage world changes.", "bad", "View-only database");
                    return;
                }
                int staged = 0;
                try
                {
                    using var c = S.OpenRead(db);
                    foreach (var st in writes) { ChangeFactory.StageRawStatement(S.Changes.Pending, st, c); staged++; }
                }
                catch (Exception ex) { notes.Add("Couldn't capture undo data: " + Friendly.Text(ex)); foreach (var st in writes.Skip(staged)) ChangeFactory.StageRawStatement(S.Changes.Pending, st, null); }
                S.SaveChanges();
                var last = S.Changes.Pending.Changes.Skip(S.Changes.Pending.Changes.Count - writes.Count).ToList();
                var noUndo = last.Count(x => !x.UndoAvailable);
                var sp = U.Stack(
                    U.T($"Staged {writes.Count} write statement(s) in your changeset. Nothing ran on your database.", 13.5, "Text", FontWeights.SemiBold),
                    U.T(noUndo == 0 ? "Studio captured the current rows so it can undo them." : $"{noUndo} of them have no automatic undo; the backup taken before Apply covers them. See the Undo SQL tab on the Changes screen.", 12.5, "Muted", null, true, new Thickness(0, 4, 0, 10)),
                    U.Btn("Review on the Changes screen", () => w.Navigate("changes"), "BtnAmber", "Ctrl+Shift+R"));
                queryMsg.Content = new Border { Background = U.R("AmberDim"), BorderBrush = U.R("Amber"), BorderThickness = new Thickness(3, 0, 0, 0), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 14, 12), Margin = new Thickness(0, 0, 0, 12), Child = sp };
                if (reads.Count == 0) return;
            }
            history.Add(db, text);
            var sw = Stopwatch.StartNew();
            try
            {
                using var c = S.OpenRead(db);
                results = new List<DataTable>();
                bool anyTrunc = false;
                foreach (var q in reads.Take(10))
                {
                    var t = c.QueryTable(q.Text, 5000, out bool trunc);
                    anyTrunc |= trunc;
                    t.TableName = Short(q.Text);
                    results.Add(t);
                }
                sw.Stop();
                ShowResult(results.Count - 1);
                if (writes.Count == 0)
                    queryMsg.Content = U.Callout($"{results.Count} result set(s), {results.Sum(r => r.Rows.Count):N0} row(s) in {sw.ElapsedMilliseconds} ms{(anyTrunc ? " · each shows at most 5,000 rows" : "")}{(reads.Count > 10 ? " · only the first 10 queries ran" : "")}. Read-only.", "ok");
            }
            catch (Exception ex)
            {
                resultGrid.ItemsSource = null; lastResult = null;
                queryMsg.Content = U.Callout(Friendly.Text(ex), "bad", "MySQL said");
            }
        }

        void ShowResult(int i)
        {
            resultTabs.Children.Clear();
            if (results.Count == 0) return;
            lastResult = results[i];
            resultGrid.ItemsSource = lastResult.DefaultView;
            if (results.Count < 2) return;
            for (int k = 0; k < results.Count; k++)
            {
                int n = k;
                var rb = new RadioButton { Style = U.S("SegBtn"), Content = $"Result {k + 1} ({results[k].Rows.Count:N0})", GroupName = "results", IsChecked = k == i, ToolTip = results[k].TableName, Margin = new Thickness(0, 0, 4, 4) };
                rb.Checked += (s2, e2) => { lastResult = results[n]; resultGrid.ItemsSource = lastResult.DefaultView; };
                resultTabs.Children.Add(rb);
            }
        }

        void ShowHistory()
        {
            var items = history.Items.ToList();
            var dlg = new PickerWindow(w, "Query history", "Your last queries, newest first. Pick one to load it into the editor.", q =>
                items.Select((x, i) => (i, x)).Where(t => (q ?? "").Length == 0 || t.x.sql.Contains(q, StringComparison.OrdinalIgnoreCase))
                     .Take(200).Select(t => ((long)t.i, Short(t.x.sql), t.x.db + " · " + t.x.utc.ToLocalTime().ToString("MMM d HH:mm"))).ToList(), "");
            if (dlg.ShowDialog() == true && dlg.Picked != null) { editor.Text = items[(int)dlg.Picked.Value].sql; tabQuery.IsChecked = true; editor.Focus(); }
        }

        void OpenSqlFile()
        {
            var d = new Microsoft.Win32.OpenFileDialog { Filter = "SQL files (*.sql)|*.sql|All files|*.*" };
            if (d.ShowDialog(w) != true) return;
            var fi = new System.IO.FileInfo(d.FileName);
            if (fi.Length > 5_000_000) { w.Toast("That file is large; use Stage .sql script… instead of loading it into the editor.", "warn"); return; }
            editor.Text = System.IO.File.ReadAllText(d.FileName);
            tabQuery.IsChecked = true;
            w.Toast($"Loaded {fi.Name}. F5 runs its reads and stages its writes; nothing runs on its own.", "info");
        }

        void StageSqlFile()
        {
            if (!Editable) { w.Toast($"{db} is view-only in Studio. Switch to {S.Profile.WorldDb} to stage a script.", "warn"); return; }
            var d = new Microsoft.Win32.OpenFileDialog { Filter = "SQL files (*.sql)|*.sql|All files|*.*" };
            if (d.ShowDialog(w) != true) return;
            StageScript(System.IO.File.ReadAllText(d.FileName), System.IO.Path.GetFileName(d.FileName));
        }

        public void StageScript(string text, string name)
        {
            var plan = SqlScript.Prepare(text);
            tabQuery.IsChecked = true;
            if (plan.Blocked.Count > 0)
            {
                queryMsg.Content = U.Callout($"{name}: Studio won't stage this file because of {plan.Blocked.Count} statement(s) it doesn't allow:\n" + string.Join("\n", plan.Blocked.Take(5).Select(b => "• " + Short(b.Text) + " — " + b.Reason)) + "\n\nNothing was staged.", "bad", "Script refused");
                return;
            }
            if (plan.Writes.Count == 0) { queryMsg.Content = U.Callout($"{name} has no INSERT / UPDATE / DELETE / REPLACE statements to stage.", "info"); return; }
            int n = 0;
            try { using var c = S.OpenRead(db); foreach (var st in plan.Writes) { ChangeFactory.StageRawStatement(S.Changes.Pending, st, c); n++; } }
            catch { foreach (var st in plan.Writes.Skip(n)) ChangeFactory.StageRawStatement(S.Changes.Pending, st, null); }
            S.SaveChanges();
            queryMsg.Content = U.Callout($"{name}: staged {plan.Writes.Count} statement(s)" + (plan.Structure > 0 ? $" ({plan.Structure} change table structure: recorded, but Studio won't apply them)" : "") + $"; skipped {plan.Skipped.Count} session line(s); {plan.Reads} read(s) ignored. Nothing ran. Review it on Changes.", "ok", "Script staged");
        }

        void ExportTableSql()
        {
            if (table == null) return;
            var d = new Microsoft.Win32.SaveFileDialog { FileName = table + ".sql", Filter = "SQL file|*.sql" };
            if (d.ShowDialog(w) != true) return;
            var file = d.FileName; var t = table; var f = filter; var dbn = db;
            w.Toast($"Exporting {t}…", "info");
            System.Threading.Tasks.Task.Run(() =>
            {
                using var c = S.OpenRead(dbn);
                var create = c.Query($"SHOW CREATE TABLE {SqlGen.Ident(t)}", 1);
                var rows = c.Query($"SELECT * FROM {SqlGen.Ident(t)}" + (f.Length > 0 ? " WHERE " + f : ""), 2_000_000);
                using var sw = new System.IO.StreamWriter(file, false, new System.Text.UTF8Encoding(false));
                SqlDump.WriteHeader(sw, dbn);
                SqlDump.WriteTable(sw, t, f.Length > 0 ? null : Convert.ToString(create.Rows[0][1]), rows.Columns, rows.Rows);
                return rows.Rows.Count;
            }).ContinueWith(x => w.Dispatcher.Invoke(() => w.Toast(x.Exception == null ? $"Saved {x.Result:N0} rows to {file}" + (f.Length > 0 ? " (filtered: INSERTs only, no CREATE TABLE)." : ".") : "Export failed: " + x.Exception.GetBaseException().Message, x.Exception == null ? "ok" : "bad")));
        }

        void FindText()
        {
            if (data == null) return;
            var q = TextAsk.Ask(w, "Find text in " + table, "Shows rows where any column contains this text (numbers too).", "");
            if (string.IsNullOrWhiteSpace(q)) return;
            whereBox.Text = SqlDump.FindInAllColumns(data.Columns.Cast<DataColumn>().Select(c => c.ColumnName), q.Trim());
            ApplyFilter();
        }

        static string Short(string s) { s = s.Replace("\r", " ").Replace("\n", " ").Trim(); return s.Length > 90 ? s.Substring(0, 90) + "…" : s; }

        // ================================================================== for screenshots
        public void ScreensEditCell(string column, string newText)
        {
            if (data == null || data.Rows.Count == 0) return;
            var row = data.Rows[0];
            var original = row.HasVersion(DataRowVersion.Original) ? row[column, DataRowVersion.Original] : row[column];
            SqlGen.TryTypedLiteral(newText, data.Columns[column].DataType, true, out var lit, out _);
            ChangeFactory.StageCellEdit(S.Changes.Pending, table, KeyOf(row), column, SqlGen.Literal(original), SqlGen.Display(original), lit, newText);
            S.SaveChanges();
            OverlayStaged();
            var colObj = grid.Columns.FirstOrDefault(c => ColumnName(c) == column);
            if (colObj != null && grid.Items.Count > 0) grid.ScrollIntoView(grid.Items[0], colObj);
        }
        public void ScreensFilter(string f) { whereBox.Text = f; ApplyFilter(); }
        public void ScreensDataTab() => tabData.IsChecked = true;
    }
}
