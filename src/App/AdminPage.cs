using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shell;
using AndersCraftStudio.Core;
using Microsoft.Win32;

namespace AndersCraftStudio
{
    /// <summary>
    /// Admin console: the MySQL administration Studio's content editors deliberately leave out (direct SQL, databases,
    /// tables and indexes, users and grants, connections, imports and exports, setting up AzerothCore's databases).
    /// It needs an admin login typed in for this session; Studio never saves it. Risky statements ask for a typed confirmation.
    /// </summary>
    public sealed class AdminPage : IPage
    {
        readonly MainWindow w;
        Studio S => w.S;
        readonly Grid view = new Grid();
        public UIElement View => view;
        readonly ContentControl host = new ContentControl { Focusable = false };
        AdminDb admin;
        string adminPass;   // in memory for this session only (imports open their own connection); cleared on disconnect

        // login
        readonly TextBox hostBox = U.Box("127.0.0.1", true), portBox = U.Box("3306", true), userBox = U.Box("root", true);
        readonly PasswordBox passBox = new PasswordBox();

        // tabs
        string tab = "console";
        readonly ContentControl tabBody = new ContentControl { Focusable = false };
        readonly WrapPanel tabBar = new WrapPanel();

        public AdminPage(MainWindow w)
        {
            this.w = w;
            view.Margin = new Thickness(28, 22, 28, 20);
            view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            view.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var head = U.Stack(U.Row(U.H1("Admin console"), new Border { Width = 12 }, U.Pill("runs directly", "MaroonDim", "Text", "Unlike the editors, statements here run straight away on the server.")),
                U.T("Everything you'd open HeidiSQL for: run any SQL, manage databases, tables, indexes and views, users and permissions, connections, imports and exports, and set up AzerothCore's databases from scratch. It needs an admin login that you type in each time; Studio never saves it.", 13.5, "Muted"));
            head.Margin = new Thickness(0, 0, 0, 16);
            view.Children.Add(head);
            Grid.SetRow(host, 1); view.Children.Add(host);
            if (S.Profile != null) { hostBox.Text = S.Profile.Host ?? "127.0.0.1"; portBox.Text = S.Profile.Port.ToString(CultureInfo.InvariantCulture); }
            passBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) Connect(); };
            ShowLogin();
        }

        public void OnShow() { }
        public bool Connected => admin != null;

        // ================================================================== login
        void ShowLogin(string error = null)
        {
            var sp = new StackPanel { MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
            sp.Children.Add(U.H2("Sign in as a MySQL administrator"));
            sp.Children.Add(U.T("Use an account that can manage the server, usually root (the one you chose when installing MySQL). This is separate from Studio's own read-only and apply accounts, and it is only kept in memory until you disconnect or close Studio. It is never written to disk.", 13.5, "Muted", null, true, new Thickness(0, 0, 0, 14)));
            hostBox.Width = 200; portBox.Width = 90; userBox.Width = 200; passBox.Width = 220; passBox.Padding = new Thickness(10, 7, 10, 7);
            Detach(hostBox); Detach(portBox); Detach(userBox); Detach(passBox);
            sp.Children.Add(U.Wrap(Fld("Server", "Host name or IP of MySQL.", hostBox, 210), Fld("Port", "3306 unless you changed it.", portBox, 100), Fld("Admin account", "e.g. root", userBox, 210), Fld("Password", "Typed each time, never saved.", passBox, 230)));
            var go = U.Btn("Connect", Connect, "BtnPrimary"); go.HorizontalAlignment = HorizontalAlignment.Left;
            sp.Children.Add(go);
            if (error != null) { var c = U.Callout(error, "bad", "Couldn't connect"); c.Margin = new Thickness(0, 14, 0, 0); sp.Children.Add(c); }
            sp.Children.Add(new Border { Height = 18 });
            sp.Children.Add(U.Callout("Statements here run immediately, like in any MySQL client. Studio still protects you: anything that deletes data or tables asks you to type the object's name, user and connection changes are marked as permanent, and you're offered a backup copy before risky table changes. For everyday content editing, use the editors: they stage, preview and back up every change.", "warn", "How this differs from the editors"));
            host.Content = U.Card(sp, new Thickness(28), new Thickness(0));
        }

        static void Detach(FrameworkElement e) { if (e.Parent is Panel p) p.Children.Remove(e); else if (e.Parent is ContentControl c) c.Content = null; }
        static FrameworkElement Fld(string label, string hint, FrameworkElement c, double wd = 220) { var f = U.Field(label, hint, c, null, wd); f.Margin = new Thickness(0, 0, 14, 12); return f; }

        void Connect()
        {
            if (!int.TryParse(portBox.Text.Trim(), out var port)) { ShowLogin("Port: enter a number."); return; }
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                admin?.Dispose();
                admin = new AdminDb(hostBox.Text.Trim(), port, userBox.Text.Trim(), passBox.Password);
                adminPass = passBox.Password;
                passBox.Password = "";
            }
            catch (Exception ex) { admin = null; adminPass = null; ShowLogin(ex.Message); return; }
            finally { Mouse.OverrideCursor = null; }
            ShowConsole();
        }

        public void Disconnect()
        {
            try { admin?.Dispose(); } catch { }
            admin = null; adminPass = null; passBox.Password = "";
            ShowLogin();
        }

        AdminDb NewConnection(string db = null) => new AdminDb(admin.Host, admin.Port, admin.User, adminPass, db);

        // ================================================================== connected
        void ShowConsole()
        {
            var dock = new DockPanel();
            var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            var who = U.Row(U.Pill($"● {admin.User} @ {admin.Host}:{admin.Port}", "OkBg", "Ok", "Admin session. The password is kept in memory only."), new Border { Width = 8 }, U.Btn("Disconnect", Disconnect, "BtnGhost", "Forget the admin login now."));
            DockPanel.SetDock(who, Dock.Right); bar.Children.Add(who);
            var tabs = new Border { Background = U.R("Panel"), BorderBrush = U.R("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), HorizontalAlignment = HorizontalAlignment.Left, Child = tabBar };
            bar.Children.Add(tabs);
            DockPanel.SetDock(bar, Dock.Top); dock.Children.Add(bar);
            dock.Children.Add(tabBody);
            host.Content = dock;
            BuildTabBar(); ShowTab(tab);
        }

        static readonly (string key, string title, string tip)[] Tabs =
        {
            ("console", "SQL console", "Run any SQL directly: several statements, several result sets, transactions."),
            ("databases", "Databases & tables", "Create, change and drop databases, tables, columns, indexes and views."),
            ("io", "Import & export", "Run .sql files or whole folders; export databases or tables to .sql or CSV."),
            ("users", "Users & permissions", "MySQL accounts, passwords and GRANTs."),
            ("connections", "Connections", "Who is connected and what they're running; end a connection."),
            ("server", "Server", "Version, status counters and settings."),
            ("setup", "Set up AzerothCore", "Create the databases and account from scratch and load the base tables."),
        };

        void BuildTabBar()
        {
            tabBar.Children.Clear();
            foreach (var (key, title, tip) in Tabs)
            {
                var rb = new RadioButton { Style = U.S("SegBtn"), Content = title, GroupName = "admintab", IsChecked = key == tab, ToolTip = tip };
                var k = key;
                rb.Checked += (s, e) => { if (tab != k) ShowTab(k); };
                tabBar.Children.Add(rb);
            }
        }

        public void ShowTab(string key)
        {
            tab = key;
            foreach (RadioButton rb in tabBar.Children) rb.IsChecked = Tabs.First(t => t.title == (string)rb.Content).key == key;
            try
            {
                tabBody.Content = key switch
                {
                    "console" => ConsoleTab(),
                    "databases" => DatabasesTab(),
                    "io" => IoTab(),
                    "users" => UsersTab(),
                    "connections" => ConnectionsTab(),
                    "server" => ServerTab(),
                    _ => SetupTab(),
                };
            }
            catch (Exception ex) { tabBody.Content = U.Callout(ex.Message, "bad"); }
        }

        List<string> Databases() { try { return admin.Table("SHOW DATABASES").Rows.Cast<DataRow>().Select(r => Convert.ToString(r[0])).ToList(); } catch { return new List<string>(); } }

        ComboBox DbCombo(string selected = null, bool any = false)
        {
            var cb = new ComboBox { Width = 240 };
            if (any) cb.Items.Add("(none: statements say which)");
            foreach (var d in Databases()) cb.Items.Add(d);
            var pick = selected ?? S.Profile?.WorldDb;
            cb.SelectedItem = cb.Items.Cast<object>().FirstOrDefault(x => string.Equals(x as string, pick, StringComparison.OrdinalIgnoreCase)) ?? (cb.Items.Count > 0 ? cb.Items[0] : null);
            return cb;
        }

        static string Db(ComboBox cb) => cb.SelectedItem is string s && !s.StartsWith("(") ? s : null;

        // ================================================================== confirmation + running
        /// <summary>Shows the statements (passwords masked) and asks for the right confirmation. Returns whether to go ahead and whether a backup was asked for.</summary>
        (bool go, bool backup) Confirm(string title, IList<string> stmts, IList<string> backupTables = null, string backupDb = null)
        {
            var max = stmts.Select(AdminSql.Risk).DefaultIfEmpty(AdminRisk.Read).Max();
            if (max == AdminRisk.Read) return (true, false);
            var dlg = new AdminConfirmWindow(w, title, stmts, max, AdminSql.ConfirmWord(stmts), backupTables != null && backupTables.Count > 0 ? $"First save a copy of {string.Join(", ", backupTables)} (structure + data) to Documents\\AndersCraft Studio\\dumps" : null);
            if (w.IsScreens) { ScreensLastConfirm = dlg; return (false, false); }
            var ok = dlg.ShowDialog() == true;
            return (ok, ok && dlg.Backup);
        }

        public AdminConfirmWindow ScreensLastConfirm;

        /// <summary>Runs statements one by one on the session connection; returns the results (stops at the first error unless asked not to).</summary>
        List<AdminResult> Run(IList<string> stmts, string db = null, bool stopOnError = true)
        {
            var res = new List<AdminResult>();
            try { Mouse.OverrideCursor = Cursors.Wait; if (db != null) admin.Use(db); }
            catch (Exception ex) { Mouse.OverrideCursor = null; res.Add(new AdminResult { Sql = "USE " + db, Error = ex.Message }); return res; }
            try
            {
                foreach (var s in stmts)
                {
                    var r = admin.Run(s);
                    res.Add(r);
                    if (!r.Ok && stopOnError) break;
                }
            }
            finally { Mouse.OverrideCursor = null; }
            return res;
        }

        /// <summary>Confirm, optional backup, run, report. For the buttons that build statements for you.</summary>
        bool Do(string title, IList<string> stmts, string db = null, IList<string> backupTables = null, Action after = null)
        {
            var (go, backup) = Confirm(title, stmts, backupTables, db);
            if (!go) return false;
            if (backup && backupTables != null && db != null)
            {
                try { var f = DumpTables(db, backupTables, true, null, CancellationToken.None); w.Toast("Copy saved: " + f, "ok"); }
                catch (Exception ex) { if (MessageBox.Show(w, "The backup copy failed: " + ex.Message + "\n\nGo ahead anyway?", "Backup", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return false; }
            }
            var res = Run(stmts, db);
            var bad = res.FirstOrDefault(r => !r.Ok);
            if (bad != null) { MessageBox.Show(w, AdminSql.Mask(bad.Sql) + "\n\n" + bad.Error, title + " — not done", MessageBoxButton.OK, MessageBoxImage.Error); return false; }
            w.Toast(title + ": done.", "ok");
            after?.Invoke();
            return true;
        }

        // ================================================================== SQL console
        readonly TextBox editor = U.Code("", 220, false);
        readonly TabControl results = new TabControl { MinHeight = 260 };
        readonly TextBox messages = U.Code("", 120);
        readonly CheckBox txBox = new CheckBox { Content = U.T("Run inside a transaction (Commit or Roll back afterwards)", 13, "Text", null, false), VerticalAlignment = VerticalAlignment.Center };
        readonly List<string> history = new List<string>();
        ComboBox consoleDb;
        Button commitBtn, rollbackBtn;

        UIElement ConsoleTab()
        {
            Detach(editor); Detach(results); Detach(messages); Detach(txBox);
            consoleDb = DbCombo(consoleDb != null ? Db(consoleDb) : null, true);
            editor.FontSize = 13.5; editor.AcceptsTab = true;
            editor.ToolTip = "Any SQL, several statements separated by ;  F5 runs everything (or just the selected text).";
            U.OnKey(editor, Key.F5, ModifierKeys.None, RunConsole);
            var run = U.Btn("Run  (F5)", RunConsole, "BtnPrimary", "Runs the statements now. Risky ones ask for confirmation first.");
            commitBtn = U.Btn("Commit", () => { admin.Commit(); Log("COMMIT — changes are saved."); UpdateTx(); }, null, "Keep the changes made in this transaction.");
            rollbackBtn = U.Btn("Roll back", () => { admin.Rollback(); Log("ROLLBACK — changes since Run were thrown away."); UpdateTx(); }, null, "Throw away the changes made in this transaction.");
            var hist = U.Btn("History…", ShowHistory, "BtnGhost", "Statements run in this session (passwords masked; not saved to disk).");
            var open = U.Btn("Open .sql…", () => { var d = new OpenFileDialog { Filter = "SQL files|*.sql|All files|*.*" }; if (d.ShowDialog(w) == true) editor.Text = File.ReadAllText(d.FileName); }, "BtnGhost");
            UpdateTx();
            var top = U.Wrap(Fld("Database", "Where unqualified table names go.", consoleDb, 250), U.Row(run, commitBtn, rollbackBtn, hist, open));
            ((FrameworkElement)top.Children[1]).VerticalAlignment = VerticalAlignment.Center;
            var sp = new StackPanel();
            sp.Children.Add(U.Card(U.Stack(top, editor, U.Row(txBox)), new Thickness(18, 14, 18, 14), new Thickness(0, 0, 0, 12)));
            sp.Children.Add(U.Card(U.Stack(U.H2("Results"), results), new Thickness(18, 14, 18, 14), new Thickness(0, 0, 0, 12)));
            sp.Children.Add(U.Card(U.Stack(U.H2("Messages"), messages), new Thickness(18, 14, 18, 14), new Thickness(0)));
            return U.Scroll(sp);
        }

        void UpdateTx() { if (commitBtn == null) return; commitBtn.IsEnabled = rollbackBtn.IsEnabled = admin?.InTransaction == true; }
        void Log(string s) { messages.AppendText($"[{DateTime.Now:HH:mm:ss}] {s}\n"); messages.ScrollToEnd(); }

        public void RunConsole()
        {
            var text = editor.SelectionLength > 0 ? editor.SelectedText : editor.Text;
            var stmts = SqlBatch.Split(text);
            if (stmts.Count == 0) return;
            var (go, _) = Confirm("Run these statements", stmts);
            if (!go) { Log("Cancelled."); return; }
            if (txBox.IsChecked == true) admin.Begin();
            var res = Run(stmts, Db(consoleDb), stopOnError: true);
            results.Items.Clear();
            int n = 0;
            foreach (var r in res)
            {
                history.Add(AdminSql.Mask(r.Sql)); if (history.Count > 100) history.RemoveAt(0);
                Log(Short(AdminSql.Mask(r.Sql)) + " → " + r.Summary);
                foreach (var t in r.Tables)
                {
                    if (++n > 20) break;
                    var g = new DataGrid { IsReadOnly = true, AutoGenerateColumns = true, ItemsSource = t.DefaultView, MaxHeight = 420 };
                    g.AutoGeneratingColumn += (s, e) => { e.Column.Header = new TextBlock { Text = e.PropertyName }; e.Column.MaxWidth = 360; };
                    results.Items.Add(new TabItem { Header = $"Result {n} ({t.Rows.Count})", Content = g });
                }
            }
            if (results.Items.Count > 0) results.SelectedIndex = 0;
            if (res.Any(r => !r.Ok)) Log("Stopped at the error above." + (admin.InTransaction ? " The transaction is still open: Commit or Roll back." : ""));
            UpdateTx();
        }

        static string Short(string s) { s = s.Replace("\r", " ").Replace("\n", " ").Trim(); return s.Length > 120 ? s.Substring(0, 120) + "…" : s; }

        void ShowHistory()
        {
            if (history.Count == 0) { w.Toast("Nothing run yet in this session.", "info"); return; }
            var items = history.AsEnumerable().Reverse().Select((h, i) => ((long)i, Short(h), "")).ToList();
            var dlg = new PickerWindow(w, "Run again", "Statements from this session (passwords masked).", q => items.Where(x => x.Item2.Contains(q ?? "", StringComparison.OrdinalIgnoreCase)).ToList(), "");
            if (dlg.ShowDialog() == true && dlg.Picked != null) editor.Text = history[history.Count - 1 - (int)dlg.Picked.Value];
        }

        // ================================================================== databases & tables
        string dbSel, tableSel;

        UIElement DatabasesTab()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var dbs = new ListBox { MinHeight = 300 };
            foreach (var d in Databases()) dbs.Items.Add(d);
            dbs.SelectedItem = dbSel ?? (dbs.Items.Contains(S.Profile?.WorldDb ?? "") ? S.Profile.WorldDb : null);
            var right = new ContentControl { Focusable = false };
            dbs.SelectionChanged += (s, e) => { dbSel = dbs.SelectedItem as string; tableSel = null; right.Content = DbDetail(); };
            var newDb = U.Btn("New database…", () =>
            {
                var f = new AdminFormWindow(w, "New database", "Creates an empty database (utf8mb4).", ("name", "Name", "", null));
                if (f.ShowDialog() == true) Do("Create database", new[] { AdminSql.CreateDatabase(f["name"]) }, null, null, () => { dbSel = f["name"]; ShowTab("databases"); });
            });
            var dropDb = U.Btn("Drop database…", () =>
            {
                if (dbSel == null) return;
                Do("Drop database " + dbSel, new[] { AdminSql.DropDatabase(dbSel) }, null, null, () => { dbSel = null; ShowTab("databases"); });
            }, "BtnGhost", "Deletes the database and every table in it. You'll type its name to confirm.");
            g.Children.Add(U.Card(U.Stack(U.Eyebrow("Databases"), dbs, U.Row(newDb, dropDb)), new Thickness(10, 12, 10, 10), new Thickness(0)));
            Grid.SetColumn(right, 2); g.Children.Add(right);
            right.Content = DbDetail();
            return g;
        }

        UIElement DbDetail()
        {
            if (dbSel == null) return U.Card(U.T("Pick a database on the left.", 13.5, "Muted"), new Thickness(24), new Thickness(0));
            var sp = new StackPanel();
            var t = admin.Table($"SELECT TABLE_NAME, TABLE_TYPE, COALESCE(TABLE_ROWS, 0) AS approx_rows, ROUND((COALESCE(DATA_LENGTH,0) + COALESCE(INDEX_LENGTH,0)) / 1048576, 2) AS size_mb, ENGINE FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = {SqlGen.Literal(dbSel)} ORDER BY TABLE_NAME");
            var grid = new DataGrid { IsReadOnly = true, AutoGenerateColumns = true, ItemsSource = t.DefaultView, MaxHeight = 330, SelectionMode = DataGridSelectionMode.Single };
            var detail = new ContentControl { Focusable = false };
            grid.SelectionChanged += (s, e) => { if (grid.SelectedItem is DataRowView rv) { tableSel = Convert.ToString(rv["TABLE_NAME"]); detail.Content = TableDetail(Convert.ToString(rv["TABLE_TYPE"]) == "VIEW"); } };
            var newTable = U.Btn("New table…", NewTable, "BtnPrimary", "Build a CREATE TABLE from a list of columns.");
            var newView = U.Btn("New view…", () =>
            {
                var f = new AdminFormWindow(w, "New view", "A saved SELECT that works like a read-only table.", ("name", "View name", "", null), ("sql", "SELECT …", "SELECT entry, name FROM creature_template WHERE `rank` = 3", "multi"));
                if (f.ShowDialog() == true) { try { Do("Create view", new[] { AdminSql.CreateView(f["name"], f["sql"]) }, dbSel, null, () => ShowTab("databases")); } catch (Exception ex) { w.Toast(ex.Message, "bad"); } }
            });
            sp.Children.Add(U.Card(U.Stack(U.Row(U.H2($"{dbSel} · {t.Rows.Count} table(s)"), new Border { Width = 12 }, newTable, newView), grid), new Thickness(18, 14, 18, 14), new Thickness(0, 0, 0, 12)));
            sp.Children.Add(detail);
            return U.Scroll(sp);
        }

        UIElement TableDetail(bool isView)
        {
            var sp = new StackPanel();
            var cols = admin.Table($"SHOW FULL COLUMNS FROM {SqlGen.Ident(dbSel)}.{SqlGen.Ident(tableSel)}");
            var colNames = cols.Rows.Cast<DataRow>().Select(r => Convert.ToString(r["Field"])).ToList();
            var colGrid = new DataGrid { IsReadOnly = true, AutoGenerateColumns = true, ItemsSource = cols.DefaultView, MaxHeight = 300, SelectionMode = DataGridSelectionMode.Single };
            string SelCol() => colGrid.SelectedItem is DataRowView rv ? Convert.ToString(rv["Field"]) : null;
            var bk = new List<string> { tableSel };
            var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            void A(string text, Action a, string style = null, string tip = null) { var b = U.Btn(text, a, style, tip); b.Margin = new Thickness(0, 0, 8, 6); actions.Children.Add(b); }
            if (!isView)
            {
                A("Add column…", () =>
                {
                    var c = AskColumn("Add column", null); if (c == null) return;
                    var f = new AdminFormWindow(w, "Where", "Put the new column after…", ("after", "After column (empty = at the end, FIRST = first)", colNames.LastOrDefault() ?? "", null));
                    if (f.ShowDialog() != true) return;
                    Run1("Add column", () => AdminSql.AddColumn(tableSel, c, f["after"].Trim()), bk);
                });
                A("Change column…", () =>
                {
                    var col = SelCol(); if (col == null) { w.Toast("Pick a column first.", "info"); return; }
                    var row = cols.Rows.Cast<DataRow>().First(r => Convert.ToString(r["Field"]) == col);
                    var type = Convert.ToString(row["Type"]);
                    var c = AskColumn("Change column " + col, new AdminSql.ColumnSpec { Name = col, Type = type.Replace(" unsigned", ""), Unsigned = type.Contains("unsigned"), NotNull = Convert.ToString(row["Null"]) == "NO", Default = row["Default"] is DBNull ? null : Convert.ToString(row["Default"]), AutoIncrement = Convert.ToString(row["Extra"]).Contains("auto_increment"), Comment = Convert.ToString(row["Comment"]) });
                    if (c != null) Run1("Change column", () => AdminSql.ChangeColumn(tableSel, col, c), bk);
                });
                A("Drop column…", () => { var col = SelCol(); if (col == null) { w.Toast("Pick a column first.", "info"); return; } Run1("Drop column " + col, () => AdminSql.DropColumn(tableSel, col), bk); }, "BtnGhost");
                A("Add index…", () =>
                {
                    var f = new AdminFormWindow(w, "Add index", "Speeds up searches on these columns. Unique also stops duplicates.", ("name", "Index name", "idx_" + (SelCol() ?? "col"), null), ("cols", "Columns (comma separated)", SelCol() ?? "", null), ("unique", "Unique", "0", "bool"));
                    if (f.ShowDialog() == true) Run1("Add index", () => AdminSql.AddIndex(tableSel, f["name"], f["cols"].Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList(), f["unique"] == "1"), bk);
                });
                A("Drop index…", () =>
                {
                    var f = new AdminFormWindow(w, "Drop index", "The index name from the list below (PRIMARY = the primary key).", ("name", "Index name", "", null));
                    if (f.ShowDialog() == true) Run1("Drop index", () => AdminSql.DropIndex(tableSel, f["name"]), bk);
                }, "BtnGhost");
                A("Rename…", () =>
                {
                    var f = new AdminFormWindow(w, "Rename table", "", ("to", "New name", tableSel, null));
                    if (f.ShowDialog() == true) Run1("Rename table", () => AdminSql.RenameTable(tableSel, f["to"]), null, () => ShowTab("databases"));
                });
                A("Empty it (truncate)…", () => Run1("Empty " + tableSel, () => AdminSql.TruncateTable(tableSel), bk), "BtnGhost", "Removes every row. You'll type its name to confirm.");
                A("Save a copy…", () => { try { w.Toast("Saved: " + DumpTables(dbSel, new[] { tableSel }, true, null, CancellationToken.None), "ok"); } catch (Exception ex) { w.Toast(ex.Message, "bad"); } }, null, "Structure + data to Documents\\AndersCraft Studio\\dumps.");
            }
            A(isView ? "Drop view…" : "Drop table…", () => Run1((isView ? "Drop view " : "Drop table ") + tableSel, () => isView ? AdminSql.DropView(tableSel) : AdminSql.DropTable(tableSel), isView ? null : bk, () => ShowTab("databases")), "BtnGhost");
            sp.Children.Add(U.Card(U.Stack(U.H2((isView ? "View " : "Table ") + tableSel), colGrid, actions), new Thickness(18, 14, 18, 14), new Thickness(0, 0, 0, 12)));
            if (!isView)
            {
                var idx = admin.Table($"SHOW INDEX FROM {SqlGen.Ident(dbSel)}.{SqlGen.Ident(tableSel)}");
                sp.Children.Add(U.Card(U.Stack(U.H2("Indexes"), new DataGrid { IsReadOnly = true, AutoGenerateColumns = true, ItemsSource = idx.DefaultView, MaxHeight = 220 }), new Thickness(18, 14, 18, 14), new Thickness(0, 0, 0, 12)));
            }
            var create = admin.Table($"SHOW CREATE {(isView ? "VIEW" : "TABLE")} {SqlGen.Ident(dbSel)}.{SqlGen.Ident(tableSel)}");
            sp.Children.Add(U.Card(U.Stack(U.H2("Definition"), U.Code(create.Rows.Count > 0 ? Convert.ToString(create.Rows[0][1]) : "", 160)), new Thickness(18, 14, 18, 14), new Thickness(0)));
            return sp;
        }

        void Run1(string title, Func<string> build, IList<string> backupTables, Action after = null)
        {
            string sql;
            try { sql = build(); } catch (Exception ex) { w.Toast(ex.Message, "bad"); return; }
            Do(title, new[] { sql }, dbSel, backupTables, after ?? (() => ShowTab("databases")));
        }

        AdminSql.ColumnSpec AskColumn(string title, AdminSql.ColumnSpec c)
        {
            c ??= new AdminSql.ColumnSpec { Type = "INT", Unsigned = true, Default = "0" };
            var f = new AdminFormWindow(w, title, "Types: INT, SMALLINT, TINYINT, BIGINT, FLOAT, VARCHAR(100), TEXT, LONGTEXT…",
                ("name", "Name", c.Name ?? "", null), ("type", "Type", c.Type, null), ("unsigned", "Unsigned (no negative numbers)", c.Unsigned ? "1" : "0", "bool"),
                ("notnull", "Not NULL", c.NotNull ? "1" : "0", "bool"), ("default", "Default (empty = none, NULL = NULL)", c.Default ?? "", null), ("ai", "Auto increment", c.AutoIncrement ? "1" : "0", "bool"), ("comment", "Comment", c.Comment ?? "", null));
            if (f.ShowDialog() != true) return null;
            return new AdminSql.ColumnSpec { Name = f["name"].Trim(), Type = f["type"].Trim(), Unsigned = f["unsigned"] == "1", NotNull = f["notnull"] == "1", Default = f["default"].Length == 0 ? null : f["default"], AutoIncrement = f["ai"] == "1", Comment = f["comment"] };
        }

        void NewTable()
        {
            var f = new AdminFormWindow(w, "New table", "One column per line: name type [unsigned] [null] [default=value] [ai] [pk]. Example:\nentry INT unsigned pk\nname VARCHAR(100) default=\ncount INT default=0",
                ("name", "Table name", "", null), ("cols", "Columns", "id INT unsigned ai pk\nname VARCHAR(100) default=", "multi"));
            if (f.ShowDialog() != true) return;
            try
            {
                var specs = f["cols"].Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Select(ParseColumnLine).ToList();
                Do("Create table", new[] { AdminSql.CreateTable(f["name"].Trim(), specs) }, dbSel, null, () => { tableSel = f["name"].Trim(); ShowTab("databases"); });
            }
            catch (Exception ex) { w.Toast(ex.Message, "bad"); }
        }

        public static AdminSql.ColumnSpec ParseColumnLine(string line)
        {
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) throw new ArgumentException($"\"{line}\": write the name and the type.");
            var c = new AdminSql.ColumnSpec { Name = parts[0], Type = parts[1] };
            foreach (var p in parts.Skip(2))
            {
                var l = p.ToLowerInvariant();
                if (l == "unsigned") c.Unsigned = true;
                else if (l == "null") c.NotNull = false;
                else if (l == "ai") c.AutoIncrement = true;
                else if (l == "pk") c.PrimaryKey = true;
                else if (l.StartsWith("default=")) c.Default = p.Substring(8);
                else throw new ArgumentException($"\"{p}\" in \"{line}\" isn't understood (unsigned, null, default=…, ai, pk).");
            }
            return c;
        }

        // ================================================================== import & export
        CancellationTokenSource running;
        readonly ProgressBar progress = new ProgressBar { Height = 8, Margin = new Thickness(0, 10, 0, 6), Maximum = 1 };
        readonly TextBox ioLog = U.Code("", 220);

        UIElement IoTab()
        {
            Detach(progress); Detach(ioLog);
            var sp = new StackPanel();
            // import
            var target = DbCombo(null, true);
            var stop = new CheckBox { Content = U.T("Stop at the first error", 13, "Text", null, false), IsChecked = true, Margin = new Thickness(0, 0, 0, 8) };
            var file = U.Btn("Run a .sql file…", () => { var d = new OpenFileDialog { Filter = "SQL files|*.sql|All files|*.*", Multiselect = true }; if (d.ShowDialog(w) == true) Import(d.FileNames.OrderBy(Path.GetFileName).ToList(), Db(target), stop.IsChecked == true); }, "BtnPrimary", "Runs every statement in the file(s), in order.");
            var folder = U.Btn("Run a whole folder…", () =>
            {
                var d = new OpenFolderDialog { Title = "Folder of .sql files (run in name order)" };
                if (d.ShowDialog(w) == true) Import(SqlBatch.FolderOrder(Directory.GetFiles(d.FolderName)), Db(target), stop.IsChecked == true);
            }, null, "Runs every .sql file in the folder, sorted by name (how AzerothCore names its base and update files).");
            var cancel = U.Btn("Stop", () => running?.Cancel(), "BtnGhost");
            sp.Children.Add(U.Card(U.Stack(U.H2("Import / restore"), U.T("Load a dump or any .sql file: base tables, module SQL, update files, or a backup Studio made. Statements run directly. A file that starts with DROP TABLE replaces those tables.", 12.5, "Muted", null, true, new Thickness(0, 0, 0, 10)),
                U.Wrap(Fld("Into database", "Used for files that don't say USE.", target, 250)), stop, U.Row(file, folder, cancel), progress, ioLog), new Thickness(18, 14, 18, 14), new Thickness(0, 0, 0, 12)));
            // export
            var src = DbCombo();
            var tables = new ListBox { SelectionMode = SelectionMode.Extended, MaxHeight = 220, MinHeight = 120 };
            void Fill() { tables.Items.Clear(); if (Db(src) == null) return; foreach (DataRow r in admin.Table($"SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = {SqlGen.Literal(Db(src))} AND TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_NAME").Rows) tables.Items.Add(Convert.ToString(r[0])); }
            src.SelectionChanged += (s, e) => Fill(); Fill();
            var data = new CheckBox { Content = U.T("Include the rows (not just the structure)", 13, "Text", null, false), IsChecked = true, Margin = new Thickness(0, 6, 0, 4) };
            var createDb = new CheckBox { Content = U.T("Add CREATE DATABASE + USE (restores into an empty server)", 13, "Text", null, false), IsChecked = true, Margin = new Thickness(0, 0, 0, 8) };
            var exportSql = U.Btn("Export to .sql", () => Export(Db(src), tables.SelectedItems.Cast<string>().ToList(), data.IsChecked == true, createDb.IsChecked == true, false), "BtnPrimary", "No tables selected = the whole database.");
            var exportCsv = U.Btn("Export to CSV (one file per table)", () => Export(Db(src), tables.SelectedItems.Cast<string>().ToList(), true, false, true));
            sp.Children.Add(U.Card(U.Stack(U.H2("Export / back up"), U.T($"Saves to {DatabaseDump.DefaultFolder()}. Select tables to export only those; select none for the whole database.", 12.5, "Muted", null, true, new Thickness(0, 0, 0, 10)),
                U.Wrap(Fld("Database", "", src, 250)), tables, data, createDb, U.Row(exportSql, exportCsv)), new Thickness(18, 14, 18, 14), new Thickness(0)));
            return U.Scroll(sp);
        }

        void IoLog(string s) => w.Dispatcher.Invoke(() => { ioLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {s}\n"); ioLog.ScrollToEnd(); });

        /// <summary>Runs .sql files on a separate admin connection in the background, with progress and a log.</summary>
        public void Import(IList<string> files, string db, bool stopOnError, Action done = null)
        {
            if (running != null) { w.Toast("Something is already running.", "warn"); return; }
            if (files.Count == 0) { w.Toast("No .sql files there.", "warn"); return; }
            var preview = files.Take(12).Select(f => "-- " + Path.GetFileName(f)).ToList();
            if (files.Count > 12) preview.Add($"-- … and {files.Count - 12} more file(s)");
            var risky = files.Take(50).Any(f => { try { using var r = new StreamReader(f); var head = new char[20000]; var n = r.Read(head, 0, head.Length); return SqlBatch.Split(new string(head, 0, n)).Take(30).Any(s => AdminSql.Risk(s) >= AdminRisk.Destructive); } catch { return false; } });
            if (risky) preview.Insert(0, "DROP TABLE … -- (these files replace existing tables)");
            var (go, _) = Confirm($"Run {files.Count} file(s)" + (db != null ? " into " + db : ""), preview);
            if (!go) return;
            running = new CancellationTokenSource(); var ct = running.Token;
            progress.Value = 0; ioLog.Clear();
            string host = admin.Host, user = admin.User, pass = adminPass; int port = admin.Port;
            Task.Run(() =>
            {
                int ok = 0, bad = 0; long stmtCount = 0;
                using var conn = new AdminDb(host, port, user, pass, db);
                for (int i = 0; i < files.Count && !ct.IsCancellationRequested; i++)
                {
                    var f = files[i];
                    List<string> stmts;
                    try { stmts = SqlBatch.Split(File.ReadAllText(f)); } catch (Exception ex) { IoLog($"{Path.GetFileName(f)}: can't read: {ex.Message}"); bad++; if (stopOnError) break; continue; }
                    int fileErr = 0;
                    foreach (var s in stmts)
                    {
                        if (ct.IsCancellationRequested) break;
                        var r = conn.Run(s, 1);
                        stmtCount++;
                        if (!r.Ok) { fileErr++; IoLog($"{Path.GetFileName(f)}: {r.Error}\n    {Short(AdminSql.Mask(s))}"); if (stopOnError) break; }
                    }
                    if (fileErr == 0) { ok++; IoLog($"✓ {Path.GetFileName(f)} ({stmts.Count} statement(s))"); } else bad++;
                    var p = (i + 1.0) / files.Count;
                    w.Dispatcher.Invoke(() => progress.Value = p);
                    if (fileErr > 0 && stopOnError) break;
                }
                IoLog(ct.IsCancellationRequested ? "Stopped." : $"Done: {ok} file(s) fine, {bad} with errors, {stmtCount:N0} statement(s).");
                return bad;
            }).ContinueWith(t => w.Dispatcher.Invoke(() =>
            {
                running = null;
                if (t.Exception != null) IoLog("Failed: " + t.Exception.GetBaseException().Message);
                w.Toast(t.Exception == null && t.Result == 0 ? "Import finished." : "Import finished with problems: see the log.", t.Exception == null && t.Result == 0 ? "ok" : "warn");
                done?.Invoke();
            }));
        }

        void Export(string db, List<string> tables, bool data, bool createDb, bool csv)
        {
            if (db == null) return;
            if (running != null) { w.Toast("Something is already running.", "warn"); return; }
            running = new CancellationTokenSource(); var ct = running.Token;
            progress.Value = 0; ioLog.Clear();
            Task.Run(() => csv ? DumpCsv(db, tables, ct) : DumpTables(db, tables, data, (i, n, t) => { IoLog($"{t}"); w.Dispatcher.Invoke(() => progress.Value = (double)i / n); }, ct, createDb))
                .ContinueWith(t => w.Dispatcher.Invoke(() =>
                {
                    running = null; progress.Value = 1;
                    if (t.Exception != null) { IoLog("Failed: " + t.Exception.GetBaseException().Message); w.Toast("Export failed.", "bad"); }
                    else { IoLog("Saved: " + t.Result); w.Toast("Exported to " + t.Result, "ok"); }
                }));
        }

        /// <summary>Structure (+ data) of tables (all when none given) to Documents\AndersCraft Studio\dumps. Returns the file path.</summary>
        string DumpTables(string db, IList<string> tables, bool data, Action<int, int, string> step, CancellationToken ct, bool createDb = false)
        {
            using var conn = NewConnection(db);
            var list = tables != null && tables.Count > 0 ? tables.ToList() : conn.Table($"SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = {SqlGen.Literal(db)} AND TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_NAME").Rows.Cast<DataRow>().Select(r => Convert.ToString(r[0])).ToList();
            var dir = DatabaseDump.DefaultFolder(); Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, DatabaseDump.FileName(db, tables?.Count ?? 0, DateTime.Now));
            using var sw = new StreamWriter(file, false, new UTF8Encoding(false));
            DatabaseDump.WriteHeader(sw, db, createDb);
            for (int i = 0; i < list.Count; i++)
            {
                if (ct.IsCancellationRequested) throw new OperationCanceledException("Stopped.");
                var t = list[i];
                step?.Invoke(i, list.Count, t);
                var create = Convert.ToString(conn.Table($"SHOW CREATE TABLE {SqlGen.Ident(t)}").Rows[0][1]);
                if (!data) { SqlDump.WriteTable(sw, t, create, new List<string>(), Enumerable.Empty<object[]>()); continue; }
                var r = conn.Run($"SELECT * FROM {SqlGen.Ident(t)}", int.MaxValue);
                if (!r.Ok) throw new InvalidOperationException(t + ": " + r.Error);
                var dt = r.Tables[0];
                SqlDump.WriteTable(sw, t, create, dt.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList(), dt.Rows.Cast<DataRow>().Select(x => x.ItemArray));
            }
            DatabaseDump.WriteFooter(sw);
            return file;
        }

        string DumpCsv(string db, IList<string> tables, CancellationToken ct)
        {
            using var conn = NewConnection(db);
            var list = tables != null && tables.Count > 0 ? tables.ToList() : conn.Table($"SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = {SqlGen.Literal(db)} AND TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_NAME").Rows.Cast<DataRow>().Select(r => Convert.ToString(r[0])).ToList();
            var dir = Path.Combine(DatabaseDump.DefaultFolder(), AssetImport.SafeName(db) + "_csv_" + DateTime.Now.ToString("yyyy-MM-dd_HHmmss"));
            Directory.CreateDirectory(dir);
            for (int i = 0; i < list.Count; i++)
            {
                if (ct.IsCancellationRequested) throw new OperationCanceledException("Stopped.");
                IoLog(list[i]); var p = (double)i / list.Count; w.Dispatcher.Invoke(() => progress.Value = p);
                var r = conn.Run($"SELECT * FROM {SqlGen.Ident(list[i])}", int.MaxValue);
                if (!r.Ok) throw new InvalidOperationException(list[i] + ": " + r.Error);
                var dt = r.Tables[0];
                using var sw = new StreamWriter(Path.Combine(dir, list[i] + ".csv"), false, new UTF8Encoding(true));
                sw.WriteLine(string.Join(",", dt.Columns.Cast<DataColumn>().Select(c => Csv.Escape(c.ColumnName))));
                foreach (DataRow row in dt.Rows) sw.WriteLine(string.Join(",", row.ItemArray.Select(v => Csv.Escape(v is DBNull ? "" : SqlGen.Display(v)))));
            }
            return dir;
        }

        // ================================================================== users
        UIElement UsersTab()
        {
            var sp = new StackPanel();
            DataTable users;
            try { users = admin.Table("SELECT User, Host, plugin, account_locked, password_expired FROM mysql.user ORDER BY User, Host"); }
            catch { users = admin.Table("SELECT User, Host FROM mysql.user ORDER BY User, Host"); }
            var grid = new DataGrid { IsReadOnly = true, AutoGenerateColumns = true, ItemsSource = users.DefaultView, MaxHeight = 320, SelectionMode = DataGridSelectionMode.Single };
            var grants = U.Code("", 140);
            (string user, string host)? Sel() => grid.SelectedItem is DataRowView rv ? (Convert.ToString(rv["User"]), Convert.ToString(rv["Host"])) : ((string, string)?)null;
            grid.SelectionChanged += (s, e) =>
            {
                var u = Sel(); if (u == null) return;
                try { grants.Text = string.Join("\n", admin.Table($"SHOW GRANTS FOR {AdminSql.Account(u.Value.user, u.Value.host)}").Rows.Cast<DataRow>().Select(r => Convert.ToString(r[0]))); }
                catch (Exception ex) { grants.Text = ex.Message; }
            };
            var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            void A(string text, Action a, string style = null) { var b = U.Btn(text, a, style); b.Margin = new Thickness(0, 0, 8, 6); actions.Children.Add(b); }
            A("New account…", () =>
            {
                var f = new AdminFormWindow(w, "New MySQL account", "Host: localhost = only from this PC, % = from anywhere (needs a firewall you trust), or an IP.", ("user", "Account name", "", null), ("host", "Allowed from (host)", "localhost", null), ("pass", "Password", "", "password"), ("pass2", "Password again", "", "password"));
                if (f.ShowDialog() != true) return;
                if (f["pass"] != f["pass2"]) { w.Toast("The two passwords differ.", "bad"); return; }
                Do("Create account", new[] { AdminSql.CreateUser(f["user"], f["host"], f["pass"]) }, null, null, () => ShowTab("users"));
            }, "BtnPrimary");
            A("Change password…", () =>
            {
                var u = Sel(); if (u == null) { w.Toast("Pick an account.", "info"); return; }
                var f = new AdminFormWindow(w, $"New password for {u.Value.user}@{u.Value.host}", "Programs that log in with this account (worldserver, Studio…) need the new password too.", ("pass", "New password", "", "password"), ("pass2", "Again", "", "password"));
                if (f.ShowDialog() != true) return;
                if (f["pass"] != f["pass2"]) { w.Toast("The two passwords differ.", "bad"); return; }
                Do("Change password", new[] { AdminSql.SetPassword(u.Value.user, u.Value.host, f["pass"]) }, null, null, () => ShowTab("users"));
            });
            A("Grant…", () => GrantOrRevoke(Sel(), true));
            A("Revoke…", () => GrantOrRevoke(Sel(), false), "BtnGhost");
            A("Delete account…", () => { var u = Sel(); if (u == null) { w.Toast("Pick an account.", "info"); return; } Do($"Delete account {u.Value.user}@{u.Value.host}", new[] { AdminSql.DropUser(u.Value.user, u.Value.host) }, null, null, () => ShowTab("users")); }, "BtnGhost");
            sp.Children.Add(U.Card(U.Stack(U.H2("Accounts"), grid, actions), new Thickness(18, 14, 18, 14), new Thickness(0, 0, 0, 12)));
            sp.Children.Add(U.Card(U.Stack(U.H2("Permissions of the selected account"), grants), new Thickness(18, 14, 18, 14), new Thickness(0, 0, 0, 12)));
            sp.Children.Add(U.Callout("Account and permission changes take effect at once and can't be undone with a click: to go back, change them again. Passwords are never shown or saved by Studio.", "warn", "Permanent"));
            return U.Scroll(sp);
        }

        void GrantOrRevoke((string user, string host)? u, bool grant)
        {
            if (u == null) { w.Toast("Pick an account.", "info"); return; }
            var f = new AdminFormWindow(w, (grant ? "Grant to " : "Revoke from ") + $"{u.Value.user}@{u.Value.host}",
                "Privileges, comma separated: " + string.Join(", ", AdminSql.Privileges) + ". Database * = every database.",
                ("privs", "Privileges", grant ? "SELECT, INSERT, UPDATE, DELETE" : "ALL PRIVILEGES", null), ("db", "Database", S.Profile?.WorldDb ?? "*", null), ("table", "Table (* = all)", "*", null), ("wg", "Also let them grant these to others", "0", grant ? "bool" : "hidden"));
            if (f.ShowDialog() != true) return;
            try
            {
                var privs = f["privs"].Split(',').Select(x => x.Trim()).ToList();
                var sql = grant ? AdminSql.Grant(privs, f["db"], f["table"], u.Value.user, u.Value.host, f["wg"] == "1") : AdminSql.Revoke(privs, f["db"], f["table"], u.Value.user, u.Value.host);
                Do(grant ? "Grant" : "Revoke", new[] { sql }, null, null, () => ShowTab("users"));
            }
            catch (Exception ex) { w.Toast(ex.Message, "bad"); }
        }

        // ================================================================== connections
        UIElement ConnectionsTab()
        {
            var t = admin.Table("SHOW FULL PROCESSLIST");
            var grid = new DataGrid { IsReadOnly = true, AutoGenerateColumns = true, ItemsSource = t.DefaultView, MaxHeight = 480, SelectionMode = DataGridSelectionMode.Single };
            var refresh = U.Btn("Refresh", () => ShowTab("connections"));
            var kill = U.Btn("End the selected connection…", () =>
            {
                if (!(grid.SelectedItem is DataRowView rv)) { w.Toast("Pick a connection.", "info"); return; }
                var idv = Convert.ToInt64(rv["Id"], CultureInfo.InvariantCulture);
                Do($"End connection {idv} ({rv["User"]})", new[] { AdminSql.Kill(idv) }, null, null, () => ShowTab("connections"));
            }, "BtnGhost");
            return U.Scroll(U.Stack(U.Card(U.Stack(U.Row(U.H2($"Connections ({t.Rows.Count})"), new Border { Width = 12 }, refresh, kill), U.T("Info = the statement it's running now; Time = seconds in that state. Ending a connection stops its query and logs it out; a program like worldserver will usually reconnect, but its unsaved work in that query is lost.", 12.5, "Muted", null, true, new Thickness(0, 4, 0, 10)), grid), new Thickness(18, 14, 18, 14), new Thickness(0))));
        }

        // ================================================================== server
        UIElement ServerTab()
        {
            var sp = new StackPanel();
            var ver = admin.Scalar("SELECT VERSION()");
            var up = long.TryParse(admin.Scalar("SELECT VARIABLE_VALUE FROM performance_schema.global_status WHERE VARIABLE_NAME = 'Uptime'") ?? "0", out var u) ? TimeSpan.FromSeconds(u) : TimeSpan.Zero;
            sp.Children.Add(U.Card(U.Stack(U.H2("Server"), U.CheckRow(U.Check.Info, "Version", ver), U.CheckRow(U.Check.Info, "Running for", up == TimeSpan.Zero ? "?" : $"{(int)up.TotalDays} d {up.Hours} h {up.Minutes} min"), U.CheckRow(U.Check.Info, "Signed in as", admin.User + "@" + admin.Host)), new Thickness(18, 14, 18, 14), new Thickness(0, 0, 0, 12)));
            foreach (var (title, sql) in new[] { ("Status counters", "SHOW GLOBAL STATUS"), ("Settings (variables)", "SHOW GLOBAL VARIABLES") })
            {
                var t = admin.Table(sql);
                var filter = U.Box(); filter.Width = 260;
                var g = new DataGrid { IsReadOnly = true, AutoGenerateColumns = true, ItemsSource = t.DefaultView, MaxHeight = 300 };
                filter.TextChanged += (s, e) => { var q = filter.Text.Replace("'", "''").Replace("[", "[[]").Replace("*", "[*]").Replace("%", "[%]"); t.DefaultView.RowFilter = q.Length == 0 ? "" : $"Variable_name LIKE '%{q}%'"; };
                sp.Children.Add(U.Card(U.Stack(U.Row(U.H2(title), new Border { Width = 12 }, U.WithPlaceholder(filter, "filter, e.g. max_connections")), g), new Thickness(18, 14, 18, 14), new Thickness(0, 0, 0, 12)));
            }
            sp.Children.Add(U.T("Change a setting for the running server with SET GLOBAL name = value in the SQL console; to keep it after a restart, put it in MySQL's my.ini.", 12.5, "Muted"));
            return U.Scroll(sp);
        }

        // ================================================================== set up AzerothCore
        readonly ServerSetup.Plan plan = new ServerSetup.Plan();

        UIElement SetupTab()
        {
            plan.SqlRoot ??= GuessSqlRoot();
            var sp = new StackPanel();
            var world = U.Box(plan.World, true); var chars = U.Box(plan.Characters, true); var auth = U.Box(plan.Auth, true);
            var user = U.Box(plan.User, true); var uhost = U.Box(plan.Host, true); var pass = new PasswordBox { Width = 200, Padding = new Thickness(10, 7, 10, 7) };
            var mkUser = new CheckBox { Content = U.T("Create the account the server logs in with, with full rights on these three databases", 13, "Text", null, false), IsChecked = plan.CreateUser, Margin = new Thickness(0, 4, 0, 8) };
            var imp = new CheckBox { Content = U.T("Load the base tables from AzerothCore's data\\sql\\base folders", 13, "Text", null, false), IsChecked = plan.ImportBase, Margin = new Thickness(0, 0, 0, 8) };
            var root = U.Box(plan.SqlRoot ?? "", true); root.Width = 520;
            var browse = U.Btn("Browse…", () => { var d = new OpenFolderDialog { Title = "AzerothCore's data\\sql folder" }; if (d.ShowDialog(w) == true) root.Text = d.FolderName; }, "BtnGhost");
            var status = new StackPanel();
            void Sync() { plan.World = world.Text.Trim(); plan.Characters = chars.Text.Trim(); plan.Auth = auth.Text.Trim(); plan.User = user.Text.Trim(); plan.Host = uhost.Text.Trim(); plan.Password = pass.Password; plan.CreateUser = mkUser.IsChecked == true; plan.ImportBase = imp.IsChecked == true; plan.SqlRoot = root.Text.Trim(); }
            void Check()
            {
                Sync(); status.Children.Clear();
                var existing = Databases();
                foreach (var d in new[] { plan.World, plan.Characters, plan.Auth })
                {
                    int tables = 0;
                    if (existing.Contains(d, StringComparer.OrdinalIgnoreCase)) int.TryParse(admin.Scalar($"SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = {SqlGen.Literal(d)}"), out tables);
                    status.Children.Add(U.CheckRow(tables > 0 ? U.Check.Warn : U.Check.Ok, d, !existing.Contains(d, StringComparer.OrdinalIgnoreCase) ? "will be created" : tables > 0 ? $"exists with {tables} tables — loading the base files replaces them" : "exists, empty", null, 170));
                }
                foreach (var (db, folder) in ServerSetup.BaseFolders(plan))
                {
                    var n = Directory.Exists(folder) ? SqlBatch.FolderOrder(Directory.GetFiles(folder)).Count : 0;
                    status.Children.Add(U.CheckRow(n > 0 ? U.Check.Ok : (plan.ImportBase ? U.Check.Bad : U.Check.Info), Path.GetFileName(folder), n > 0 ? $"{n} file(s) → {db}" : "folder not found", folder, 170));
                }
            }
            var check = U.Btn("Check", Check, null, "Looks at the server and the folders (reads only).");
            var go = U.Btn("Set it up", () =>
            {
                Check();
                List<string> stmts;
                try { stmts = ServerSetup.Statements(plan); } catch (Exception ex) { w.Toast(ex.Message, "bad"); return; }
                var folders = plan.ImportBase ? ServerSetup.BaseFolders(plan).ToList() : new List<(string, string)>();
                if (plan.ImportBase && folders.Any(f => !Directory.Exists(f.Item2))) { w.Toast("A base folder is missing: point Studio at AzerothCore's data\\sql folder.", "bad"); return; }
                var shown = stmts.ToList();
                foreach (var (db, folder) in folders) shown.Add($"-- then run {SqlBatch.FolderOrder(Directory.GetFiles(folder)).Count} file(s) from {folder} into {db}");
                var existing = Databases().Intersect(new[] { plan.World, plan.Characters, plan.Auth }, StringComparer.OrdinalIgnoreCase).ToList();
                if (plan.ImportBase && existing.Count > 0) shown.Insert(0, $"DROP TABLE … -- the base files replace existing tables in {string.Join(", ", existing)}");
                var (ok, _) = Confirm("Set up AzerothCore's databases", shown);
                if (!ok) return;
                var res = Run(stmts);
                var bad = res.FirstOrDefault(r => !r.Ok);
                if (bad != null) { MessageBox.Show(w, AdminSql.Mask(bad.Sql) + "\n\n" + bad.Error, "Setup stopped", MessageBoxButton.OK, MessageBoxImage.Error); return; }
                if (!plan.ImportBase) { Finish(); return; }
                ShowTab("io");
                ImportChain(folders, 0);
            }, "BtnPrimary", "Creates the databases (and account), then loads the base tables. You'll see everything first.");
            sp.Children.Add(U.Card(U.Stack(U.H2("Set up a new AzerothCore server's databases"),
                U.T("For a fresh MySQL install: this creates AzerothCore's three databases, the account worldserver and authserver log in with, and loads the starting tables shipped with AzerothCore. When you then start authserver and worldserver for the first time, they apply the newer update files themselves (Updates.EnableDatabases is on by default). Already have a server? You don't need this.", 13, "Muted", null, true, new Thickness(0, 0, 0, 14)),
                U.Wrap(Fld("World database", "", world, 200), Fld("Characters database", "", chars, 200), Fld("Login (auth) database", "", auth, 200)),
                mkUser, U.Wrap(Fld("Server account", "Goes into worldserver.conf / authserver.conf.", user, 200), Fld("Allowed from", "localhost when MySQL runs on the same PC.", uhost, 200), Fld("Its password", "Typed here only; not saved.", pass, 220)),
                imp, U.Row(Fld("AzerothCore data\\sql folder", "Contains base\\db_auth, base\\db_characters, base\\db_world.", root, 530), browse),
                U.Row(check, go), status), new Thickness(20, 16, 20, 16), new Thickness(0, 0, 0, 12)));
            sp.Children.Add(GuideCard.Make("After it's done", "1. Put the account into authserver.conf (LoginDatabaseInfo) and worldserver.conf (all three lines). Studio shows the lines when setup finishes.\n2. Start authserver, then worldserver. On the first start they apply AzerothCore's update files and finish the databases.\n3. Run Studio's setup wizard (Settings › Run setup again) so Studio makes its own safe read-only and apply accounts.",
                "The base files replace tables with the same names, so only point this at new or throw-away databases."));
            Check();
            return U.Scroll(sp);
        }

        void ImportChain(List<(string db, string folder)> folders, int i)
        {
            if (i >= folders.Count) { Finish(); return; }
            var (db, folder) = folders[i];
            ImportNoConfirm(SqlBatch.FolderOrder(Directory.GetFiles(folder)), db, () => ImportChain(folders, i + 1));
        }

        /// <summary>Like Import, but already confirmed (as part of setup).</summary>
        void ImportNoConfirm(IList<string> files, string db, Action next)
        {
            running = new CancellationTokenSource(); var ct = running.Token;
            string host = admin.Host, user = admin.User, pass = adminPass; int port = admin.Port;
            IoLog($"Loading {files.Count} file(s) into {db}…");
            Task.Run(() =>
            {
                using var conn = new AdminDb(host, port, user, pass, db);
                int bad = 0;
                for (int k = 0; k < files.Count && !ct.IsCancellationRequested; k++)
                {
                    foreach (var s in SqlBatch.Split(File.ReadAllText(files[k])))
                    {
                        var r = conn.Run(s, 1);
                        if (!r.Ok) { bad++; IoLog($"{Path.GetFileName(files[k])}: {r.Error}"); break; }
                    }
                    var p = (k + 1.0) / files.Count;
                    w.Dispatcher.Invoke(() => progress.Value = p);
                }
                return bad;
            }).ContinueWith(t => w.Dispatcher.Invoke(() =>
            {
                running = null;
                if (t.Exception != null || t.Result > 0) { IoLog("Stopped: fix the error above, then run the folder again from Import."); w.Toast("Setup stopped at an error.", "bad"); return; }
                IoLog($"✓ {db} loaded.");
                next();
            }));
        }

        void Finish()
        {
            IoLog("Setup finished. Lines for authserver.conf / worldserver.conf (put the password in):\n" + ServerSetup.ConfLines(plan, admin.Host, admin.Port));
            w.Toast("AzerothCore's databases are set up. The conf lines are in the log.", "ok");
        }

        string GuessSqlRoot()
        {
            try
            {
                var conf = S.Profile?.ConfPath;
                for (var d = conf == null ? null : new DirectoryInfo(Path.GetDirectoryName(conf)); d != null; d = d.Parent)
                {
                    var p = Path.Combine(d.FullName, "data", "sql");
                    if (Directory.Exists(Path.Combine(p, "base"))) return p;
                }
            }
            catch { }
            return null;
        }

        // screenshots
        public void ScreensSql(string sql) { editor.Text = sql; }
        public void ScreensNeutralLogin() { hostBox.Text = "127.0.0.1"; portBox.Text = "3306"; userBox.Text = "root"; }
    }

    /// <summary>Shows the statements an admin action will run, how risky they are, and asks for the right confirmation.</summary>
    public sealed class AdminConfirmWindow : Window
    {
        public bool Backup { get; private set; }

        public AdminConfirmWindow(Window owner, string title, IList<string> stmts, AdminRisk risk, string word, string backupOffer)
        {
            Owner = owner; Title = title; Width = 720; SizeToContent = SizeToContent.Height; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Background = U.R("Panel"); FontFamily = U.Font; Foreground = U.R("Text");
            WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 30, GlassFrameThickness = new Thickness(0), ResizeBorderThickness = new Thickness(0) });
            var sp = new StackPanel();
            sp.Children.Add(U.Eyebrow("Admin · runs directly"));
            sp.Children.Add(U.H2(title));
            var shown = stmts.Take(40).Select(AdminSql.Mask).ToList();
            if (stmts.Count > 40) shown.Add($"-- … and {stmts.Count - 40} more statement(s)");
            sp.Children.Add(U.Code(string.Join("\n", shown), Math.Min(260, 40 + 18 * shown.Sum(s => s.Split('\n').Length))));
            var kind = risk == AdminRisk.Irreversible ? "bad" : risk == AdminRisk.Destructive ? "bad" : "warn";
            var text = risk switch
            {
                AdminRisk.Irreversible => "This changes accounts, permissions or connections. It takes effect at once and there's no undo: to go back you'd have to change it again by hand.",
                AdminRisk.Destructive => "This deletes tables, rows or a whole database. Only a backup brings them back.",
                _ => "This changes the server directly (not through Studio's Changes screen). Structure changes may not be cleanly reversible.",
            };
            var c = U.Callout(text, kind, "Check before you run it"); c.Margin = new Thickness(0, 12, 0, 12); sp.Children.Add(c);
            CheckBox bk = null;
            if (backupOffer != null) { bk = new CheckBox { Content = U.T(backupOffer, 13, "Text", null, true), IsChecked = risk >= AdminRisk.Destructive || risk == AdminRisk.Change, Margin = new Thickness(0, 0, 0, 12) }; sp.Children.Add(bk); }
            TextBox typed = null;
            var go = U.Btn("Run it", null, "BtnPrimary");
            if (risk >= AdminRisk.Destructive)
            {
                typed = U.Box("", true); typed.Width = 260; typed.HorizontalAlignment = HorizontalAlignment.Left;
                sp.Children.Add(U.T($"Type  {word}  to confirm:", 13, "Text", FontWeights.SemiBold, false, new Thickness(0, 0, 0, 6)));
                sp.Children.Add(typed);
                go.IsEnabled = false;
                typed.TextChanged += (s, e) => go.IsEnabled = typed.Text.Trim() == word;
                Loaded += (s, e) => typed.Focus();
            }
            go.Click += (s, e) => { Backup = bk?.IsChecked == true; DialogResult = true; };
            var cancel = U.Btn("Cancel  (Esc)", () => DialogResult = false, "BtnGhost"); cancel.IsCancel = true;
            var row = U.Row(go, cancel); row.Margin = new Thickness(0, 16, 0, 0); row.HorizontalAlignment = HorizontalAlignment.Right;
            sp.Children.Add(row);
            Content = new Border { BorderBrush = U.R("LineHi"), BorderThickness = new Thickness(1), Padding = new Thickness(22, 18, 22, 18), Child = sp };
        }
    }

    /// <summary>
    /// Runs a script Studio prepared (setup accounts, sandbox) with an admin login typed in here and forgotten afterwards,
    /// so a fresh install needs no other MySQL tool. Shows the script (passwords masked) before anything runs.
    /// </summary>
    public sealed class AdminRunWindow : Window
    {
        public bool Succeeded { get; private set; }

        public AdminRunWindow(Window owner, string title, string script, string host, int port)
        {
            Owner = owner; Title = title; Width = 760; SizeToContent = SizeToContent.Height; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Background = U.R("Panel"); FontFamily = U.Font; Foreground = U.R("Text");
            WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 30, GlassFrameThickness = new Thickness(0), ResizeBorderThickness = new Thickness(0) });
            var stmts = SqlBatch.Split(script);
            var sp = new StackPanel();
            sp.Children.Add(U.Eyebrow("Run as MySQL administrator"));
            sp.Children.Add(U.H2(title));
            sp.Children.Add(U.T("Studio runs the script below with an admin account (usually root). The login is used for this one run and then forgotten; it's never saved.", 12.5, "Muted", null, true, new Thickness(0, 0, 0, 10)));
            sp.Children.Add(U.Code(string.Join("\n", stmts.Select(AdminSql.Mask)), 220));
            var hb = U.Box(host ?? "127.0.0.1", true); var pb = U.Box(port.ToString(CultureInfo.InvariantCulture), true); var ub = U.Box("root", true); var pw = new PasswordBox { Padding = new Thickness(10, 7, 10, 7) };
            FrameworkElement F(string l, FrameworkElement c, double wd) { var f = U.Field(l, null, c, null, wd); f.Margin = new Thickness(0, 12, 12, 0); return f; }
            sp.Children.Add(U.Wrap(F("Server", hb, 180), F("Port", pb, 90), F("Admin account", ub, 170), F("Password", pw, 200)));
            var result = new ContentControl { Focusable = false, Margin = new Thickness(0, 12, 0, 0) };
            sp.Children.Add(result);
            var go = U.Btn("Run it", null, "BtnPrimary");
            var close = U.Btn("Close  (Esc)", () => DialogResult = Succeeded, "BtnGhost"); close.IsCancel = true;
            go.Click += (s, e) =>
            {
                if (!int.TryParse(pb.Text.Trim(), out var port2)) { result.Content = U.Callout("Port: enter a number.", "bad"); return; }
                var log = new StringBuilder(); int ok = 0;
                try
                {
                    Mouse.OverrideCursor = Cursors.Wait;
                    using var db = new AdminDb(hb.Text.Trim(), port2, ub.Text.Trim(), pw.Password);
                    foreach (var st in stmts)
                    {
                        var r = db.Run(st, 10);
                        if (!r.Ok) { log.AppendLine(AdminSql.Mask(st)).AppendLine("→ " + r.Error); break; }
                        ok++;
                    }
                }
                catch (Exception ex) { log.AppendLine(ex.Message); }
                finally { Mouse.OverrideCursor = null; pw.Password = ""; }
                Succeeded = ok == stmts.Count && stmts.Count > 0;
                result.Content = Succeeded ? U.Callout($"Done: {ok} statement(s) ran.", "ok") : U.Callout($"{ok} of {stmts.Count} statement(s) ran, then:\n{log}", "bad", "Stopped");
                if (Succeeded) { go.IsEnabled = false; close.Content = "Continue"; }
            };
            pw.KeyDown += (s, e) => { if (e.Key == Key.Enter) go.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };
            var row = U.Row(go, close); row.Margin = new Thickness(0, 16, 0, 0); row.HorizontalAlignment = HorizontalAlignment.Right;
            sp.Children.Add(row);
            Content = new Border { BorderBrush = U.R("LineHi"), BorderThickness = new Thickness(1), Padding = new Thickness(22, 18, 22, 18), Child = sp };
            Loaded += (s, e) => pw.Focus();
        }
    }

    /// <summary>A small themed form: text boxes, password boxes, multi-line boxes and switches.</summary>
    public sealed class AdminFormWindow : Window
    {
        readonly Dictionary<string, Func<string>> get = new Dictionary<string, Func<string>>();
        public string this[string key] => get.TryGetValue(key, out var g) ? g() : "";

        public AdminFormWindow(Window owner, string title, string help, params (string key, string label, string value, string kind)[] fields)
        {
            Owner = owner; Title = title; Width = 560; SizeToContent = SizeToContent.Height; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Background = U.R("Panel"); FontFamily = U.Font; Foreground = U.R("Text");
            WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 30, GlassFrameThickness = new Thickness(0), ResizeBorderThickness = new Thickness(0) });
            var sp = new StackPanel();
            sp.Children.Add(U.H2(title));
            if (!string.IsNullOrEmpty(help)) sp.Children.Add(U.T(help, 12.5, "Muted", null, true, new Thickness(0, 0, 0, 12)));
            FrameworkElement first = null;
            foreach (var (key, label, value, kind) in fields)
            {
                if (kind == "hidden") { get[key] = () => value; continue; }
                FrameworkElement ctl;
                if (kind == "bool") { var cb = new CheckBox { Content = U.T(label, 13, "Text", null, false), IsChecked = value == "1", Margin = new Thickness(0, 0, 0, 10) }; get[key] = () => cb.IsChecked == true ? "1" : "0"; sp.Children.Add(cb); continue; }
                if (kind == "password") { var pb = new PasswordBox { Padding = new Thickness(10, 7, 10, 7) }; get[key] = () => pb.Password; ctl = pb; }
                else if (kind == "multi") { var tb = U.Code(value, 120, false); tb.AcceptsReturn = true; get[key] = () => tb.Text; ctl = tb; }
                else { var tb = U.Box(value, true); get[key] = () => tb.Text; ctl = tb; }
                first ??= ctl;
                var f = U.Field(label, null, ctl); f.Margin = new Thickness(0, 0, 0, 10);
                sp.Children.Add(f);
            }
            var ok = U.Btn("OK", () => DialogResult = true, "BtnPrimary"); ok.IsDefault = fields.All(f => f.kind != "multi");
            var cancel = U.Btn("Cancel  (Esc)", () => DialogResult = false, "BtnGhost"); cancel.IsCancel = true;
            var row = U.Row(ok, cancel); row.Margin = new Thickness(0, 8, 0, 0); row.HorizontalAlignment = HorizontalAlignment.Right;
            sp.Children.Add(row);
            Content = new Border { BorderBrush = U.R("LineHi"), BorderThickness = new Thickness(1), Padding = new Thickness(22, 18, 22, 18), Child = sp };
            Loaded += (s, e) => first?.Focus();
        }
    }
}
