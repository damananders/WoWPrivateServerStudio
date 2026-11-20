using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AndersCraftStudio.Core;

namespace AndersCraftStudio
{
    /// <summary>
    /// Friendly form editor for one kind of entity (creature, item, quest): search list, header, tabs of labelled fields,
    /// a Guide card for the focused field, and a live SQL preview. Edits are staged into the changeset, never written.
    /// </summary>
    public sealed partial class EntityEditorPage : IPage
    {
        readonly MainWindow w;
        Studio S => w.S;
        public readonly EntityDef D;
        readonly Refs refs;
        readonly Grid view = new Grid();
        public UIElement View => view;

        // left
        readonly TextBox searchBox = U.Box();
        readonly ListBox results = new ListBox();
        readonly TextBlock resultInfo = U.T("", 12, "Muted", null, true, new Thickness(2, 8, 0, 0));
        readonly DispatcherTimer searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        // middle
        readonly ContentControl centre = new ContentControl { Focusable = false };
        readonly ContentControl tabBody = new ContentControl { Focusable = false };
        readonly WrapPanel tabBar = new WrapPanel();
        // right
        readonly ContentControl guide = new ContentControl { Focusable = false };
        readonly TextBox sqlBox = U.Code("", double.NaN);
        readonly TextBlock sqlNote = U.T("", 12, "Muted", null, true, new Thickness(0, 0, 0, 8));
        Button stageBtn, discardBtn;
        Grid main;
        UIElement setupNeeded;

        // state
        long? id;
        readonly Dictionary<string, DataRow> rows = new Dictionary<string, DataRow>(StringComparer.OrdinalIgnoreCase);
        readonly EditSession session = new EditSession();
        string tab;
        readonly List<FieldUi> fieldUis = new List<FieldUi>();
        bool loadedOnce;

        sealed class FieldUi
        {
            public FieldDef F;
            public Border Box;
            public Border WasPill;
            public TextBlock WasText;
            public TextBlock Error;
            public Action Sync;
        }

        public EntityEditorPage(MainWindow w, EntityDef def)
        {
            this.w = w; D = def; refs = new Refs(w.S);
            view.Margin = new Thickness(28, 22, 28, 20);
            view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            view.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
            var strip = U.StepStrip(1); DockPanel.SetDock(strip, Dock.Right); strip.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(strip);
            head.Children.Add(U.Stack(U.H1(D.Title), U.T($"Find a {D.Singular}, change it with plain fields, and stage the edits. The Guide on the right explains whatever field you're in.", 13.5, "Muted")));
            view.Children.Add(head);

            main = BuildMain();
            setupNeeded = BuildSetupNeeded();
            var host = new ContentControl { Focusable = false, Content = main };
            Grid.SetRow(host, 1); view.Children.Add(host);
            view.Tag = host;

            searchTimer.Tick += (s, e) => { searchTimer.Stop(); RunSearch(); };
            S.ChangesChanged += () => { if (w.CurrentPage == D.Key && id != null) RefreshAllFields(); };
            U.OnKey(view, Key.S, ModifierKeys.Control, StageEdits);
            U.OnKey(view, Key.F, ModifierKeys.Control, () => { searchBox.Focus(); searchBox.SelectAll(); });
            U.OnKey(view, Key.D, ModifierKeys.Control, DuplicateAsNew);
            U.OnKey(view, Key.PageDown, ModifierKeys.Control, () => StepTab(1));
            U.OnKey(view, Key.PageUp, ModifierKeys.Control, () => StepTab(-1));
        }

        public void OnShow()
        {
            var host = (ContentControl)view.Tag;
            if (!S.HasReadAccount) { host.Content = setupNeeded; return; }
            host.Content = main;
            if (!loadedOnce) { loadedOnce = true; RunSearch(); }
        }

        UIElement BuildSetupNeeded()
        {
            var sp = new StackPanel { MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Left };
            sp.Children.Add(new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 30, Foreground = U.R("Amber"), Margin = new Thickness(0, 0, 0, 14) });
            sp.Children.Add(U.H2("Finish setup step 3 to open the editors"));
            sp.Children.Add(U.T($"The {D.Title} editor reads your world database with Studio's read-only account. Once that account is set up and tested, everything here works; edits are always staged, never written directly.", 13.5, "Muted", null, true, new Thickness(0, 0, 0, 18)));
            sp.Children.Add(U.Btn("Open setup at step 3", () => w.ShowWizard(3, true), "BtnPrimary"));
            return U.Card(sp, new Thickness(32), new Thickness(0, 10, 0, 0));
        }

        Grid BuildMain()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            sqlBox.TextWrapping = TextWrapping.Wrap;
            sqlBox.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;

            // ---- search list
            var left = new DockPanel();
            searchBox.ToolTip = $"Type part of a {D.Singular} name, or its ID. Down arrow moves into the list, Enter opens. (Ctrl+F)";
            U.AutomationName(searchBox, "Search " + D.Title);
            searchBox.TextChanged += (s, e) => { searchTimer.Stop(); searchTimer.Start(); };
            searchBox.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Down && results.Items.Count > 0) { results.SelectedIndex = 0; ((ListBoxItem)results.Items[0]).Focus(); e.Handled = true; }
                if (e.Key == Key.Enter && results.Items.Count > 0) { Open((long)((ListBoxItem)results.Items[0]).Tag); e.Handled = true; }
            };
            var sb = U.WithPlaceholder(searchBox, $"Search {D.Title.ToLowerInvariant()} by name or ID…");
            var top = U.Stack(sb, resultInfo);
            top.Margin = new Thickness(0, 0, 0, 8);
            DockPanel.SetDock(top, Dock.Top); left.Children.Add(top);
            results.SelectionChanged += (s, e) => { if (results.SelectedItem is ListBoxItem li && li.IsKeyboardFocusWithin) Open((long)li.Tag); };
            results.MouseLeftButtonUp += (s, e) => { if (results.SelectedItem is ListBoxItem li) Open((long)li.Tag); };
            results.PreviewKeyDown += (s, e) => { if (e.Key == Key.Enter && results.SelectedItem is ListBoxItem li) { Open((long)li.Tag); e.Handled = true; } };
            left.Children.Add(results);
            g.Children.Add(U.Card(left, new Thickness(12, 12, 6, 8), new Thickness(0)));

            // ---- centre
            Grid.SetColumn(centre, 2); g.Children.Add(centre);
            centre.Content = U.Card(U.Stack(U.H2($"Pick a {D.Singular}"), U.T($"Search on the left by name or #ID, or press Ctrl+K anywhere and type a name.", 13.5, "Muted")), new Thickness(28), new Thickness(0));

            // ---- right: guide + SQL preview
            var right = new Grid();
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            guide.Content = GuideIntro();
            right.Children.Add(U.Card(U.Scroll(guide), new Thickness(18, 16, 18, 16), new Thickness(0)));
            var sqlDock = new DockPanel();
            var sh = U.Stack(U.Row(U.Eyebrow("SQL preview"), new Border { Width = 8 }, U.Pill("live", "OkBg", "Ok")), sqlNote);
            DockPanel.SetDock(sh, Dock.Top); sqlDock.Children.Add(sh);
            sqlBox.ToolTip = "Exactly what Stage edits will put into your changeset. Nothing runs until you Apply on the Changes screen.";
            sqlDock.Children.Add(sqlBox);
            var sqlCard = U.Card(sqlDock, new Thickness(18, 16, 18, 16), new Thickness(0));
            Grid.SetRow(sqlCard, 2); right.Children.Add(sqlCard);
            Grid.SetColumn(right, 4); g.Children.Add(right);
            UpdatePreview();
            return g;
        }

        UIElement GuideIntro() => U.Stack(U.Eyebrow("Guide"), U.H2(D.Title),
            U.T($"Click into any field and this card explains it in game terms, with a tip. Changed fields get an amber outline and show what they were before.", 13, "Muted", null, true, new Thickness(0, 0, 0, 10)),
            U.Callout("Ctrl+S stages your edits · Ctrl+D duplicates as new · Ctrl+PgUp/PgDn switches tabs · Ctrl+F searches", "info"));

        // ================================================================== search
        void RunSearch()
        {
            if (!S.HasReadAccount) return;
            var q = searchBox.Text.Trim();
            results.Items.Clear();
            try
            {
                bool num = long.TryParse(q.TrimStart('#'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n);
                var k = SqlGen.Ident(D.KeyCol); var nm = SqlGen.Ident(D.NameCol);
                var where = q.Length == 0 ? "1=1" : num ? $"{k} = {n} OR {nm} LIKE {Refs.LikeLiteral(q)}" : $"{nm} LIKE {Refs.LikeLiteral(q)}";
                var order = num ? $"({k} = {n}) DESC, {k}" : q.Length > 0 ? $"({nm} LIKE {Refs.LikeLiteral(q).Insert(1, "").Replace("'%", "'")}) DESC, {k}" : k;
                using var db = S.OpenRead(S.Profile.WorldDb);
                var t = db.Query($"SELECT {k}, {nm}, {D.ListExtra} FROM {SqlGen.Ident(D.Table)} WHERE {where} ORDER BY {order} LIMIT 200", 200);
                foreach (var r in t.Rows)
                {
                    var rid = Convert.ToInt64(r[0], CultureInfo.InvariantCulture);
                    var top = new DockPanel();
                    var idt = U.T("#" + rid, 11.5, "Faint", null, false); DockPanel.SetDock(idt, Dock.Right); top.Children.Add(idt);
                    top.Children.Add(U.T(Convert.ToString(r[1]), 13, "Text", FontWeights.SemiBold, false));
                    results.Items.Add(new ListBoxItem { Content = U.Stack(top, U.T(Convert.ToString(r[2]), 11.5, "Muted", null, false, new Thickness(0, 2, 0, 0))), Tag = rid, IsSelected = rid == id });
                }
                resultInfo.Text = t.Rows.Count == 0 ? "No matches." : t.Rows.Count >= 200 ? "First 200 matches. Type more to narrow it down." : $"{t.Rows.Count} match(es).";
            }
            catch (Exception ex) { resultInfo.Text = "Search failed: " + ex.Message; }
        }

        public void SearchFor(string q) { searchBox.Text = q; RunSearch(); }

        // ================================================================== load an entity
        public void Open(long newId)
        {
            if (!S.HasReadAccount) { OnShow(); return; }
            if (id == newId) return;
            if (session.Count > 0 && !ConfirmLeave()) return;
            session.Clear();
            id = newId;
            rows.Clear();
            try
            {
                using var db = S.OpenRead(S.Profile.WorldDb);
                foreach (var (table, keyCol) in AllTables())
                {
                    var dt = db.QueryTable($"SELECT * FROM {SqlGen.Ident(table)} WHERE {SqlGen.Ident(keyCol)} = {newId.ToString(CultureInfo.InvariantCulture)} LIMIT 1", 1, out _);
                    rows[table] = dt.Rows.Count > 0 ? dt.Rows[0] : null;
                }
            }
            catch (Exception ex) { w.Toast($"Couldn't load {D.Singular} #{newId}: {ex.Message}", "bad"); id = null; return; }
            if (rows[D.Table] == null) { w.Toast($"There's no {D.Singular} with ID {newId}.", "warn"); id = null; return; }
            foreach (ListBoxItem li in results.Items) li.IsSelected = (long)li.Tag == newId;
            tab ??= D.Tabs[0];
            guide.Content = GuideIntro();
            BuildCentre();
            UpdatePreview();
        }

        IEnumerable<(string table, string key)> AllTables()
        {
            yield return (D.Table, D.KeyCol);
            foreach (var kv in D.SideTables) yield return (kv.Key, kv.Value);
        }

        bool ConfirmLeave()
        {
            if (w.IsScreens) { session.Clear(); return true; }
            var r = ChoiceWindow.Ask(w, "Unstaged edits", $"You have {session.Count} unstaged edit(s) on this {D.Singular}. Stage them before leaving?", "Stage them", "Discard them", "Stay here");
            if (r == 0) { StageEdits(); return true; }
            if (r == 1) { session.Clear(); return true; }
            return false;
        }

        public bool HasUnstaged => session.Count > 0;

        string TableOf(FieldDef f) => f.Table ?? D.Table;
        string KeyColOf(string table) => string.Equals(table, D.Table, StringComparison.OrdinalIgnoreCase) ? D.KeyCol : D.SideTables[table];
        List<KeyPart> KeyFor(string table) => new List<KeyPart> { new KeyPart(KeyColOf(table), id.Value.ToString(CultureInfo.InvariantCulture), "#" + id.Value) };
        DataRow RowOf(FieldDef f) => rows.TryGetValue(TableOf(f), out var r) ? r : null;
        bool Exists(FieldDef f) { var r = RowOf(f); return r != null && r.Table.Columns.Contains(f.Column); }

        object OrigValue(FieldDef f) => RowOf(f)[f.Column];
        string OrigLit(FieldDef f) => SqlGen.Literal(OrigValue(f));
        string OrigDisp(FieldDef f) => SqlGen.Display(OrigValue(f));

        FieldChange Staged(FieldDef f)
        {
            var key = KeyFor(TableOf(f));
            var c = S.Changes.Pending.Changes.FirstOrDefault(x => x.Kind == ChangeKind.Update && x.SameRow(TableOf(f), key));
            return c?.Fields.FirstOrDefault(x => string.Equals(x.Name, f.Column, StringComparison.OrdinalIgnoreCase));
        }

        (string lit, string disp) Baseline(FieldDef f)
        {
            var st = Staged(f);
            return st != null ? (st.After, st.AfterDisplay) : (OrigLit(f), OrigDisp(f));
        }

        (string lit, string disp) Current(FieldDef f)
        {
            var e = session.Get(TableOf(f), KeyFor(TableOf(f)), f.Column);
            return e != null ? (e.After, e.AfterDisplay) : Baseline(f);
        }

        /// <summary>Text form of the current value (what a text box shows): strings unquoted, NULL as NULL.</summary>
        string CurrentText(FieldDef f) => Current(f).disp;

        long CurrentLong(FieldDef f) => long.TryParse(CurrentText(f), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

        // ================================================================== centre: header + tabs + fields
        void BuildCentre()
        {
            if (tabBar.Parent is Border oldBar) oldBar.Child = null;
            if (tabBody.Parent is ScrollViewer oldSv) oldSv.Content = null;
            var dock = new DockPanel();
            dock.Children.Add(BuildHeader());
            var tabs = new Border { Background = U.R("Panel"), BorderBrush = U.R("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12), Child = tabBar };
            DockPanel.SetDock(tabs, Dock.Top); dock.Children.Add(tabs);
            BuildTabBar();
            var sv = U.Scroll(tabBody);
            dock.Children.Add(sv);
            centre.Content = dock;
            BuildTab();
        }

        UIElement BuildHeader()
        {
            var r = rows[D.Table];
            var name = Convert.ToString(r[D.NameCol]);
            stageBtn = U.Btn("Stage edits", StageEdits, "BtnPrimary", "Put your edits into the changeset (Ctrl+S). Nothing is written to the database until you Apply on the Changes screen.");
            discardBtn = U.Btn("Discard", () => { session.Clear(); RefreshAllFields(); UpdatePreview(); w.Toast("Unstaged edits discarded.", "info"); }, "BtnGhost", "Throw away edits you haven't staged yet.");
            var dup = U.Btn("Duplicate as new", DuplicateAsNew, null, $"Copy this {D.Singular} to a free ID in your custom range ({S.Profiles.Settings.CustomIdStart}+) as a staged INSERT. (Ctrl+D)");
            var sql = U.Btn("Open in SQL browser", () => w.Navigate("sql", pg => { var sp = (SqlBrowserPage)pg; sp.OpenTable("world", D.Table); sp.ScreensFilter($"{SqlGen.Ident(D.KeyCol)} = {id}"); }), "BtnGhost", "See the raw row in the SQL browser.");
            var links = CrossLinks();
            var buttons = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
            foreach (var b in new UIElement[] { dup, stageBtn, discardBtn, sql }.Concat(links)) { b.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 6)); buttons.Children.Add(b); }
            var title = U.Row(U.T(name, 22, "Text", FontWeights.SemiBold, false), new Border { Width = 12 }, U.Pill("#" + id, "AmberDim", "Amber", $"{D.KeyCol} = {id}"), U.Pill(D.Table, "Panel2", "Muted"));
            var card = U.Card(U.Stack(title, U.T(HeaderSubtitle(), 13, "Muted", null, true, new Thickness(0, 4, 0, 0)), buttons), new Thickness(20, 16, 20, 10), new Thickness(0, 0, 0, 12));
            DockPanel.SetDock(card, Dock.Top);
            UpdateStageButton();
            return card;
        }

        void BuildTabBar()
        {
            tabBar.Children.Clear();
            foreach (var t in D.Tabs)
            {
                var rb = new RadioButton { Style = U.S("SegBtn"), Content = t, GroupName = "tab_" + D.Key, IsChecked = t == tab, ToolTip = TabTip(t) };
                var tt = t;
                rb.Checked += (s, e) => { if (tab != tt) { tab = tt; guide.Content = GuideIntro(); BuildTab(); } };
                tabBar.Children.Add(rb);
            }
        }

        void StepTab(int d)
        {
            if (id == null) return;
            int i = Array.IndexOf(D.Tabs, tab);
            i = (i + d + D.Tabs.Length) % D.Tabs.Length;
            tab = D.Tabs[i];
            BuildTabBar(); BuildTab();
        }

        public void ShowTab(string t) { if (id == null || !D.Tabs.Contains(t)) return; tab = t; BuildTabBar(); BuildTab(); }

        void BuildTab()
        {
            fieldUis.Clear();
            var sp = new StackPanel();
            foreach (var grp in D.InTab(tab).GroupBy(f => (f.Group ?? "", f.Table ?? "")))
            {
                var fields = grp.ToList();
                var wrap = new WrapPanel();
                var side = fields[0].Table;
                foreach (var f in fields) wrap.Children.Add(BuildField(f));
                var head = U.Row(U.T(grp.Key.Item1.Length > 0 ? grp.Key.Item1 : tab, 15, "Text", FontWeights.SemiBold, false));
                if (side != null) head.Children.Add(U.Pill(side, "Panel2", "Muted", "These fields live in a separate table that shares the same ID."));
                ((FrameworkElement)head.Children[0]).Margin = new Thickness(0, 0, 10, 0);
                head.Margin = new Thickness(0, 0, 0, 12);
                var body = U.Stack(head);
                if (side != null && (!rows.TryGetValue(side, out var sr) || sr == null))
                    body.Children.Add(U.Callout($"This {D.Singular} has no {side} row, so these fields use the server's defaults. Studio can't edit them here yet; add the row with SQL in the SQL browser if you need them.", "info"));
                body.Children.Add(wrap);
                sp.Children.Add(U.Card(body, new Thickness(18, 14, 8, 6), new Thickness(0, 0, 0, 12)));
            }
            foreach (var extra in Extras(tab)) sp.Children.Add(extra);
            tabBody.Content = sp;
        }

        // ================================================================== one field
        UIElement BuildField(FieldDef f)
        {
            bool exists = Exists(f);
            bool wide = f.Kind == FieldKind.Flags || f.Kind == FieldKind.LongText;
            var ui = new FieldUi { F = f };
            var label = U.T(f.Label, 13, "Text", FontWeights.SemiBold, false);
            ui.WasText = U.T("", 11.5, "Amber", FontWeights.SemiBold, false);
            ui.WasPill = new Border { Background = U.R("AmberDim"), CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 1, 8, 2), Margin = new Thickness(8, 0, 0, 0), Child = ui.WasText, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
            var top = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(ui.WasPill, Dock.Right); top.Children.Add(ui.WasPill); top.Children.Add(label);
            ui.Error = U.T("", 12, "Bad", null, true, new Thickness(2, 4, 0, 0)); ui.Error.Visibility = Visibility.Collapsed;
            var hint = U.T(f.Hint, 11.5, "Faint", null, false, new Thickness(2, 5, 0, 0));
            hint.FontFamily = U.Mono;
            FrameworkElement control = exists ? BuildControl(f, ui) : U.T("Not in your database (column missing or no row).", 12.5, "Faint", null, true);
            var stack = U.Stack(top, control, ui.Error, hint);
            ui.Box = new Border { Child = stack, Width = wide ? 476 : 232, Margin = new Thickness(0, 0, 12, 12), Padding = new Thickness(12, 10, 12, 10), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1.5), BorderBrush = Brushes.Transparent, Background = U.R("Panel2") };
            ui.Box.IsKeyboardFocusWithinChanged += (s, e) => { if (ui.Box.IsKeyboardFocusWithin) ShowGuide(f); };
            ui.Box.MouseLeftButtonDown += (s, e) => ShowGuide(f);
            ui.Box.ToolTip = f.Guide + "\n\nTip: " + f.Tip + "\n" + f.Hint;
            ToolTipService.SetInitialShowDelay(ui.Box, 900);
            U.AutomationName(control, f.Label);
            fieldUis.Add(ui);
            if (exists) RefreshField(ui);
            return ui.Box;
        }

        Type ColType(FieldDef f) => RowOf(f).Table.Columns[f.Column].DataType;
        bool ColNullable(FieldDef f) => RowOf(f).Table.Columns[f.Column].ExtendedProperties["nullable"] is bool b ? b : true;

        /// <summary>A new value typed or picked for a field. Validates against the column type, then records it in the edit session.</summary>
        void Input(FieldUi ui, string text)
        {
            var f = ui.F;
            if (!SqlGen.TryTypedLiteral(text, ColType(f), ColNullable(f), out var lit, out var err))
            {
                ui.Error.Text = err; ui.Error.Visibility = Visibility.Visible;
                ui.Box.BorderBrush = U.R("Bad");
                return;
            }
            ui.Error.Visibility = Visibility.Collapsed;
            var b = Baseline(f);
            session.Set(TableOf(f), KeyFor(TableOf(f)), f.Column, b.lit, b.disp, lit, lit == "NULL" ? "NULL" : text);
            RefreshField(ui, false);
            UpdatePreview();
            ShowGuide(f);
        }

        void RefreshField(FieldUi ui, bool syncControl = true)
        {
            var f = ui.F;
            if (!Exists(f)) return;
            var cur = Current(f).lit;
            var orig = OrigLit(f);
            bool pending = session.Get(TableOf(f), KeyFor(TableOf(f)), f.Column) != null;
            bool staged = Staged(f) != null;
            if (cur != orig || pending)
            {
                ui.Box.BorderBrush = U.R("Amber");
                ui.WasText.Text = (pending ? "Was " : "Staged · was ") + Short(OrigDisp(f));
                ui.WasPill.Visibility = Visibility.Visible;
                ui.WasPill.ToolTip = pending ? "Edited here, not staged yet. Ctrl+S stages it." : "Already staged in your changeset. Apply it on the Changes screen.";
            }
            else
            {
                ui.Box.BorderBrush = staged ? U.R("LineHi") : Brushes.Transparent;
                ui.WasPill.Visibility = Visibility.Collapsed;
            }
            if (syncControl) ui.Sync?.Invoke();
        }

        void RefreshAllFields()
        {
            foreach (var ui in fieldUis) RefreshField(ui);
            UpdatePreview();
        }

        static string Short(string s) => s == null ? "" : s.Length > 26 ? s.Substring(0, 26) + "…" : s;

        FrameworkElement BuildControl(FieldDef f, FieldUi ui)
        {
            switch (f.Kind)
            {
                case FieldKind.Bool:
                {
                    var c = new CheckBox { Content = U.T("Yes", 13.5, "Text", null, false) };
                    bool syncing = false;
                    ui.Sync = () => { syncing = true; c.IsChecked = CurrentLong(f) != 0; syncing = false; };
                    c.Checked += (s, e) => { if (!syncing) Input(ui, "1"); };
                    c.Unchecked += (s, e) => { if (!syncing) Input(ui, "0"); };
                    return c;
                }
                case FieldKind.Enum:
                {
                    var cb = new ComboBox { MaxDropDownHeight = 360 };
                    bool isText = ColType(f) == typeof(string);
                    var items = new List<(string val, string label)>();
                    if (isText && f.Column == "AIName") foreach (var n in EntityDefs.AiNameList) items.Add((n, n == "" ? "(none)" : n));
                    else foreach (var kv in f.Options) items.Add((kv.Key.ToString(CultureInfo.InvariantCulture), $"{kv.Key} · {kv.Value}"));
                    bool syncing = false;
                    ui.Sync = () =>
                    {
                        syncing = true;
                        var cur = CurrentText(f);
                        if (!items.Any(i => i.val == cur)) items.Add((cur, cur + " · (not in Studio's list)"));
                        cb.Items.Clear();
                        foreach (var it in items) cb.Items.Add(new ComboBoxItem { Content = it.label, Tag = it.val });
                        cb.SelectedIndex = items.FindIndex(i => i.val == cur);
                        syncing = false;
                    };
                    cb.SelectionChanged += (s, e) => { if (!syncing && cb.SelectedItem is ComboBoxItem ci) Input(ui, (string)ci.Tag); };
                    return cb;
                }
                case FieldKind.Flags:
                {
                    var wrap = new WrapPanel();
                    var val = U.T("", 12, "Muted", null, true, new Thickness(0, 6, 0, 0));
                    bool signed = ColType(f) == typeof(int) || ColType(f) == typeof(long) || ColType(f) == typeof(short) || ColType(f) == typeof(sbyte);
                    var boxes = new List<(CheckBox cb, ulong bit)>();
                    bool syncing = false;
                    foreach (var kv in f.Bits)
                    {
                        var cb = new CheckBox { Content = U.T(kv.Value, 12.5, "Text", null, false), Margin = new Thickness(0, 0, 12, 8), Width = 214, ToolTip = $"Bit 0x{kv.Key:X} ({kv.Key})" };
                        var bit = kv.Key;
                        RoutedEventHandler h = (s, e) =>
                        {
                            if (syncing) return;
                            var cur = CurrentLong(f);
                            ulong mask = cur < 0 && signed ? 0 : unchecked((ulong)cur);
                            mask = FlagMask.Set(mask, bit, cb.IsChecked == true);
                            Input(ui, signed ? unchecked((long)mask).ToString(CultureInfo.InvariantCulture) : mask.ToString(CultureInfo.InvariantCulture));
                            ui.Sync();
                        };
                        cb.Checked += h; cb.Unchecked += h;
                        boxes.Add((cb, bit)); wrap.Children.Add(cb);
                    }
                    ui.Sync = () =>
                    {
                        syncing = true;
                        var cur = CurrentLong(f);
                        if (cur < 0 && signed) { foreach (var b in boxes) b.cb.IsChecked = false; val.Text = $"Value {cur} = everyone / all bits. Ticking a box switches to a specific list."; }
                        else
                        {
                            var m = unchecked((ulong)cur);
                            foreach (var b in boxes) b.cb.IsChecked = FlagMask.Has(m, b.bit);
                            var unk = FlagMask.Unknown(m, f.Bits.Keys);
                            val.Text = $"Value {m} (0x{m:X})" + (unk != 0 ? $" · keeps other bits 0x{unk:X} that Studio doesn't name" : "");
                        }
                        syncing = false;
                    };
                    return U.Stack(wrap, val);
                }
                case FieldKind.Ref:
                {
                    var box = U.Box("", true); box.Width = 96;
                    var name = U.T("", 12.5, "Amber", null, false, new Thickness(2, 6, 0, 0));
                    var pick = U.Btn("Pick…", null, null, "Search by name (reads the database / DBC files, read-only).");
                    pick.Padding = new Thickness(10, 6, 10, 6); pick.Margin = new Thickness(8, 0, 0, 0);
                    var ws = Refs.Workspace(f.RefKind);
                    Button open = null;
                    if (ws != null) { open = U.Btn("Open", null, "BtnGhost", $"Open it in the {ws} editor."); open.Padding = new Thickness(8, 6, 8, 6); open.Margin = new Thickness(2, 0, 0, 0); }
                    bool syncing = false;
                    void Resolve()
                    {
                        long v = CurrentLong(f);
                        var n = v == 0 ? "(none)" : refs.Name(f.RefKind, v);
                        name.Text = n ?? (f.RefKind == "creature" && v < 0 ? "game object" : "(no name found)");
                        name.ToolTip = name.Text;
                        if (open != null) open.IsEnabled = v > 0 && n != "(not found)";
                    }
                    ui.Sync = () => { syncing = true; box.Text = CurrentText(f); syncing = false; Resolve(); };
                    box.TextChanged += (s, e) => { if (!syncing) { Input(ui, box.Text.Trim()); Resolve(); } };
                    pick.Click += (s, e) =>
                    {
                        var dlg = new PickerWindow(w, "Pick " + f.Label.ToLowerInvariant(), f.Guide, q => refs.Search(f.RefKind, q), CurrentLong(f) != 0 ? (refs.Name(f.RefKind, CurrentLong(f)) ?? "") : "");
                        if (dlg.ShowDialog() == true && dlg.Picked != null) { Input(ui, dlg.Picked.Value.ToString(CultureInfo.InvariantCulture)); ui.Sync(); }
                    };
                    if (open != null) open.Click += (s, e) => { var v = CurrentLong(f); if (v > 0) w.Navigate(ws, pg => ((EntityEditorPage)pg).Open(v)); };
                    var row = U.Row(box, pick); if (open != null) row.Children.Add(open);
                    return U.Stack(row, name);
                }
                case FieldKind.LongText:
                {
                    var box = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 96, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top };
                    bool syncing = false;
                    ui.Sync = () => { syncing = true; box.Text = CurrentText(f); syncing = false; };
                    box.TextChanged += (s, e) => { if (!syncing) Input(ui, box.Text); };
                    return box;
                }
                default:
                {
                    var box = U.Box("", f.Kind != FieldKind.Text);
                    bool syncing = false;
                    if (f.Kind == FieldKind.Money)
                    {
                        var money = U.T("", 12.5, "Amber", null, false, new Thickness(10, 0, 0, 0)); money.VerticalAlignment = VerticalAlignment.Center;
                        box.Width = 140;
                        void M() => money.Text = Money(CurrentLong(f));
                        ui.Sync = () => { syncing = true; box.Text = CurrentText(f); syncing = false; M(); };
                        box.TextChanged += (s, e) => { if (!syncing) { Input(ui, box.Text.Trim()); M(); } };
                        return U.Row(box, money);
                    }
                    ui.Sync = () => { syncing = true; box.Text = CurrentText(f); syncing = false; };
                    box.TextChanged += (s, e) => { if (!syncing) Input(ui, f.Kind == FieldKind.Text ? box.Text : box.Text.Trim()); };
                    return box;
                }
            }
        }

        public static string Money(long copper)
        {
            var neg = copper < 0; copper = Math.Abs(copper);
            long g = copper / 10000, s = copper / 100 % 100, c = copper % 100;
            var parts = new List<string>();
            if (g > 0) parts.Add(g.ToString("N0", CultureInfo.InvariantCulture) + "g");
            if (s > 0) parts.Add(s + "s");
            if (c > 0 || parts.Count == 0) parts.Add(c + "c");
            return (neg ? "-" : "") + string.Join(" ", parts);
        }

        // ================================================================== guide + preview
        void ShowGuide(FieldDef f)
        {
            var sp = new StackPanel();
            sp.Children.Add(U.Eyebrow("Guide · " + tab));
            sp.Children.Add(U.H2(f.Label));
            var colPill = U.Pill(f.Hint.Replace("Column: ", ""), "Panel2", "Muted"); colPill.HorizontalAlignment = HorizontalAlignment.Left; colPill.Margin = new Thickness(0, 0, 0, 12);
            sp.Children.Add(colPill);
            sp.Children.Add(U.T(f.Guide, 13.5, "Text", null, true, new Thickness(0, 0, 0, 12)));
            sp.Children.Add(U.Callout(f.Tip, "warn", "Tip"));
            if (id != null && Exists(f))
            {
                var meaning = Meaning(f, Current(f).disp);
                sp.Children.Add(U.CheckRow(U.Check.Info, "Now", Current(f).disp + (meaning != null ? "  ·  " + meaning : ""), null, 60));
                if (Current(f).lit != OrigLit(f)) sp.Children.Add(U.CheckRow(U.Check.Warn, "Was", OrigDisp(f) + (Meaning(f, OrigDisp(f)) is string m2 ? "  ·  " + m2 : ""), null, 60));
            }
            guide.Content = sp;
        }

        string Meaning(FieldDef f, string disp)
        {
            if (!long.TryParse(disp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return null;
            switch (f.Kind)
            {
                case FieldKind.Enum: return f.Options != null && f.Options.TryGetValue(v, out var o) ? o : null;
                case FieldKind.Flags: return v < 0 ? "everyone" : FlagMask.Describe(unchecked((ulong)v), f.Bits);
                case FieldKind.Money: return Money(v);
                case FieldKind.Ref: return v == 0 ? "none" : refs.Name(f.RefKind, v);
                case FieldKind.Bool: return v != 0 ? "yes" : "no";
            }
            return null;
        }

        void UpdatePreview()
        {
            var p = session.Preview();
            if (p.Count == 0)
            {
                sqlBox.Text = "-- No unstaged edits.\n-- Change a field and the UPDATE it\n-- would stage appears here.";
                sqlNote.Text = id == null ? "Open a " + D.Singular + " to start." : "Nothing to stage yet.";
            }
            else
            {
                sqlBox.Text = "-- will be staged (Ctrl+S)\n" + string.Join("\n", p) + "\n\n-- undo, saved with it\n" + string.Join("\n", session.PreviewUndo());
                sqlNote.Text = $"{session.Count} field edit(s) waiting. Stage them to add them to your changeset.";
            }
            UpdateStageButton();
        }

        void UpdateStageButton()
        {
            if (stageBtn == null) return;
            stageBtn.Content = session.Count == 0 ? "Stage edits" : $"Stage edits ({session.Count})";
            stageBtn.IsEnabled = session.Count > 0;
            discardBtn.IsEnabled = session.Count > 0;
        }

        // ================================================================== actions
        void StageEdits()
        {
            if (session.Count == 0) { w.Toast("Nothing to stage: change a field first.", "info"); return; }
            int n = session.Count;
            var touched = session.Stage(S.Changes.Pending);
            S.SaveChanges();
            RefreshAllFields();
            w.Toast($"Staged {n} field edit(s) on {D.Singular} #{id} ({touched.Count} row change(s) in your changeset). Nothing written yet; review it on Changes.", "ok");
        }

        void DuplicateAsNew()
        {
            if (id == null) return;
            try
            {
                long start = S.Profiles.Settings.CustomIdStart;
                long free;
                using (var db = S.OpenRead(S.Profile.WorldDb)) free = long.Parse(db.Scalar(Duplicator.FreeIdQuery(D.Table, D.KeyCol, start)), CultureInfo.InvariantCulture);
                var newId = Duplicator.NextId(free, S.Changes.Pending, D.Table, start);
                var main = rows[D.Table];
                var cols = main.Table.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
                var c1 = Duplicator.Stage(S.Changes.Pending, D.Table, D.KeyCol, newId, cols, cols.Select(c => main[c]).ToList(), D.NameCol);
                int extra = 0;
                foreach (var kv in D.SideTables)
                    if (rows.TryGetValue(kv.Key, out var sr) && sr != null)
                    {
                        var sc = sr.Table.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
                        Duplicator.Stage(S.Changes.Pending, kv.Key, kv.Value, newId, sc, sc.Select(c => sr[c]).ToList());
                        extra++;
                    }
                S.SaveChanges();
                w.Toast($"Staged a copy as {D.Singular} #{newId}" + (extra > 0 ? $" (plus {extra} linked row)" : "") + ". It's an INSERT in your changeset; it shows up here after you apply it. " +
                        (session.Count > 0 ? "Your unstaged edits were not copied; the copy uses the database values." : ""), "ok");
            }
            catch (Exception ex) { w.Toast("Couldn't stage the copy: " + ex.Message, "bad"); }
        }

        // ================================================================== for screenshots
        public void ScreensEdit(string column, string text)
        {
            var ui = fieldUis.FirstOrDefault(x => x.F.Column == column);
            if (ui == null) return;
            Input(ui, text); ui.Sync?.Invoke();
        }
        public void ScreensFocus(string column) { var f = D.Fields.FirstOrDefault(x => x.Column == column); if (f != null) ShowGuide(f); }
        public void ScreensStage() => StageEdits();
        public void ScreensDiscard() { session.Clear(); RefreshAllFields(); }
        public void ScreensDuplicate() => DuplicateAsNew();
    }

    /// <summary>Small themed three-way question (e.g. stage / discard / stay).</summary>
    public sealed class ChoiceWindow : Window
    {
        int result = 2;
        public static int Ask(Window owner, string title, string text, string a, string b, string c)
        {
            var d = new ChoiceWindow(owner, title, text, a, b, c);
            d.ShowDialog();
            return d.result;
        }
        ChoiceWindow(Window owner, string title, string text, string a, string b, string c)
        {
            Owner = owner; Title = title; Width = 460; SizeToContent = SizeToContent.Height; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Background = U.R("Panel"); FontFamily = U.Font;
            System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome { CaptionHeight = 30, GlassFrameThickness = new Thickness(0), ResizeBorderThickness = new Thickness(0) });
            var ba = U.Btn(a, () => { result = 0; Close(); }, "BtnPrimary"); ba.IsDefault = true;
            var bb = U.Btn(b, () => { result = 1; Close(); });
            var bc = U.Btn(c, () => { result = 2; Close(); }, "BtnGhost"); bc.IsCancel = true;
            var row = U.Row(ba, bb, bc); row.Margin = new Thickness(0, 16, 0, 0);
            Content = new Border { BorderBrush = U.R("LineHi"), BorderThickness = new Thickness(1), Padding = new Thickness(22, 18, 22, 18), Child = U.Stack(U.H2(title), U.T(text, 13.5, "Muted"), row) };
        }
    }
}
