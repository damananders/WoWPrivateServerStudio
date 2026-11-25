using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AndersCraftStudio.Core;
using Microsoft.Win32;

namespace AndersCraftStudio
{
    /// <summary>Review staged changes (Diff / SQL / Undo SQL), apply them to the sandbox or live, and roll back past applies.</summary>
    public sealed class ChangesPage : IPage
    {
        readonly MainWindow w;
        Studio S => w.S;
        readonly Grid view = new Grid();
        public UIElement View => view;

        RadioButton tabStaged, tabHistory;
        readonly Grid stagedView = new Grid();
        readonly ContentControl historyHost = new ContentControl { Focusable = false };
        readonly ListBox list = new ListBox();
        readonly ContentControl detail = new ContentControl { Focusable = false };
        readonly ContentControl applyHost = new ContentControl { Focusable = false };
        readonly ContentControl emptyHost = new ContentControl { Focusable = false };
        string selectedId;
        int detailTab; // 0 diff, 1 sql, 2 undo
        string confirmText = "";
        bool busy;
        string busyText;
        string lastError;
        string rollbackFor; string rollbackConfirm = "";
        string conflictId; ConflictKind conflictKind;

        public ChangesPage(MainWindow w)
        {
            this.w = w;
            view.Margin = new Thickness(28, 22, 28, 20);
            view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            view.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
            var strip = U.StepStrip(3); DockPanel.SetDock(strip, Dock.Right); strip.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(strip);
            head.Children.Add(U.Stack(U.H1("Changes"), U.T("Everything you've staged, with the exact SQL and how to undo it. Tick what you want, pick a target, and apply.", 13.5, "Muted")));
            view.Children.Add(head);

            tabStaged = new RadioButton { Style = U.S("SegBtn"), GroupName = "chg", IsChecked = true, ToolTip = "Changes waiting to be applied." };
            tabHistory = new RadioButton { Style = U.S("SegBtn"), Content = "History", GroupName = "chg", ToolTip = "Past applies, their backups, and Roll back." };
            tabStaged.Checked += (s, e) => Refresh();
            tabHistory.Checked += (s, e) => Refresh();
            var tabs = new Border { Background = U.R("Panel"), BorderBrush = U.R("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 14), Child = U.Row(tabStaged, tabHistory) };
            Grid.SetRow(tabs, 1); view.Children.Add(tabs);

            BuildStaged();
            var body = new Grid(); body.Children.Add(stagedView); body.Children.Add(historyHost); body.Children.Add(emptyHost);
            Grid.SetRow(body, 2); view.Children.Add(body);
            S.ChangesChanged += () => { if (w.CurrentPage == "changes") Refresh(); };
            U.OnKey(view, Key.Delete, ModifierKeys.None, () => { if (tabStaged.IsChecked == true && list.IsKeyboardFocusWithin) DiscardSelected(); });
        }

        public void OnShow() { Refresh(); }

        public void Select(string id) { tabStaged.IsChecked = true; selectedId = id; Refresh(); }

        void BuildStaged()
        {
            stagedView.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
            stagedView.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            stagedView.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            stagedView.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            stagedView.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });

            var lp = new DockPanel();
            var tb = U.Row(
                U.Btn("All", () => { foreach (var c in S.Changes.Pending.Changes) c.Selected = !c.Structure; S.SaveChanges(); }, "BtnGhost", "Tick every change (structure changes stay unticked)."),
                U.Btn("None", () => { foreach (var c in S.Changes.Pending.Changes) c.Selected = false; S.SaveChanges(); }, "BtnGhost", "Untick everything."),
                U.Btn("Discard", DiscardSelected, "BtnGhost", "Remove the highlighted change from the changeset (Delete key). The database isn't touched."),
                U.Btn("Export SQL…", ExportSql, "BtnGhost", "Save the ticked changes (and a matching undo file) as .sql you can read or run elsewhere."));
            tb.Margin = new Thickness(0, 0, 0, 8);
            DockPanel.SetDock(tb, Dock.Top); lp.Children.Add(tb);
            list.SelectionChanged += (s, e) => { if (list.SelectedItem is ListBoxItem li && li.Tag is string id && id != selectedId) { selectedId = id; ShowDetail(); } };
            U.AutomationName(list, "Staged changes");
            lp.Children.Add(list);
            stagedView.Children.Add(U.Card(lp, new Thickness(10, 10, 6, 8), new Thickness(0)));

            Grid.SetColumn(detail, 2); stagedView.Children.Add(detail);
            Grid.SetColumn(applyHost, 4); stagedView.Children.Add(applyHost);
        }

        void Refresh()
        {
            int n = S.StagedCount;
            tabStaged.Content = $"Staged ({n})";
            bool hist = tabHistory.IsChecked == true;
            historyHost.Visibility = hist ? Visibility.Visible : Visibility.Collapsed;
            if (hist) { stagedView.Visibility = Visibility.Collapsed; emptyHost.Visibility = Visibility.Collapsed; BuildHistory(); return; }
            if (n == 0)
            {
                stagedView.Visibility = Visibility.Collapsed; emptyHost.Visibility = Visibility.Visible;
                var sp = new StackPanel { MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
                sp.Children.Add(new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 30, Foreground = U.R("Ok"), Margin = new Thickness(0, 0, 0, 12) });
                sp.Children.Add(U.H2("Nothing staged"));
                sp.Children.Add(U.T("When you edit a cell in the SQL browser, remove a row, or run an INSERT / UPDATE / DELETE in the query editor, the change lands here instead of in your database. Then you can review it and apply it in one go.", 13.5, "Muted", null, true, new Thickness(0, 0, 0, 16)));
                sp.Children.Add(U.Row(U.Btn("Open the SQL browser", () => w.Navigate("sql"), "BtnPrimary", "Alt+8"), U.Btn("See history", () => tabHistory.IsChecked = true, null, "Past applies and roll back")));
                if (lastError != null) sp.Children.Add(U.Callout(lastError, "bad", "Last apply"));
                emptyHost.Content = U.Card(sp, new Thickness(32), new Thickness(0));
                return;
            }
            stagedView.Visibility = Visibility.Visible; emptyHost.Visibility = Visibility.Collapsed;
            FillList();
            ShowDetail();
            BuildApply();
        }

        void FillList()
        {
            list.Items.Clear();
            var changes = S.Changes.Pending.Changes;
            if (selectedId == null || !changes.Any(c => c.Id == selectedId)) selectedId = changes.FirstOrDefault()?.Id;
            foreach (var c in changes)
            {
                var chk = new CheckBox { IsChecked = c.Selected && !c.Structure, IsEnabled = !c.Structure, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 10, 0),
                    ToolTip = c.Structure ? "Structure changes can't be applied by Studio." : "Include this change in the next Apply." };
                U.AutomationName(chk, "Include " + c.Summary);
                var change = c;
                chk.Checked += (s, e) => { change.Selected = true; S.Changes.SavePending(); BuildApply(); };
                chk.Unchecked += (s, e) => { change.Selected = false; S.Changes.SavePending(); BuildApply(); };
                var pills = U.Row(U.Pill(c.Category ?? "Database", CategoryBg(c.Category), "Text"), U.Pill(KindText(c), "Panel2", "Muted"));
                if (!c.UndoAvailable) pills.Children.Add(U.Pill("no auto-undo", "AmberDim", "Amber", c.UndoNote));
                if (c.Dbc?.SentToServerUtc != null) pills.Children.Add(U.Pill("server ✓", "OkBg", "Ok", "Written to the server's dbc folder " + c.Dbc.SentToServerUtc.Value.ToLocalTime().ToString("MMM d, HH:mm")));
                if (c.Dbc?.SentToPatchUtc != null) pills.Children.Add(U.Pill("patch ✓", "OkBg", "Ok", "Written to the patch work folder " + c.Dbc.SentToPatchUtc.Value.ToLocalTime().ToString("MMM d, HH:mm")));
                if (c.Id == conflictId) pills.Children.Add(U.Pill("needs a look", "BadBg", "Bad", "Apply stopped at this change. See the Apply card."));
                var text = U.Stack(pills,
                    U.T(c.Summary, 13, "Text", FontWeights.SemiBold, true, new Thickness(0, 6, 0, 2)),
                    U.T(c.Table + (c.KeyText.Length > 0 ? " · " + c.KeyText : ""), 12, "Muted", null, false));
                var dp = new DockPanel(); DockPanel.SetDock(chk, Dock.Left); dp.Children.Add(chk); dp.Children.Add(text);
                var li = new ListBoxItem { Content = dp, Tag = c.Id };
                list.Items.Add(li);
                if (c.Id == selectedId) li.IsSelected = true;
            }
        }

        static string KindText(Change c) => c.Kind switch { ChangeKind.Update => "edit", ChangeKind.Insert => "new row", ChangeKind.Delete => "remove row", _ => c.Structure ? "structure" : "typed SQL" };
        static string CategoryBg(string cat) => cat switch { "Creatures" => "MaroonDim", "Items" => "MaroonDim", "Quests" => "MaroonDim", "Loot" => "MaroonDim", "SmartAI" => "MaroonDim", "Structure" => "BadBg", "Client data (DBC)" => "AmberDim", _ => "Panel2" };

        void ShowDetail()
        {
            var c = S.Changes.Pending.Changes.FirstOrDefault(x => x.Id == selectedId);
            if (c == null) { detail.Content = null; return; }
            var sp = new DockPanel();
            var head = U.Stack(U.T(c.Summary, 17, "Text", FontWeights.SemiBold, true),
                U.Wrap(U.Pill(c.Category, CategoryBg(c.Category), "Text"), U.Pill(KindText(c), "Panel2", "Muted"), U.Pill(c.Table, "Panel2", "Muted"), U.Pill("staged " + c.CreatedUtc.ToLocalTime().ToString("MMM d, HH:mm"), "Panel2", "Muted")));
            ((WrapPanel)head.Children[1]).Margin = new Thickness(0, 8, 0, 12);
            DockPanel.SetDock(head, Dock.Top); sp.Children.Add(head);

            RadioButton Tab(string text, int idx, string tip)
            {
                var r = new RadioButton { Style = U.S("SegBtn"), Content = text, GroupName = "dtab", IsChecked = detailTab == idx, ToolTip = tip };
                r.Checked += (s, e) => { detailTab = idx; ShowDetail(); };
                return r;
            }
            if (c.Dbc != null) { detailTab = 0; ShowDbcDetail(c, sp); return; }
            var tabs = new Border { Background = U.R("Panel2"), BorderBrush = U.R("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12),
                Child = U.Row(Tab("Diff", 0, "Before and after, field by field, with what each field means."), Tab("SQL", 1, "The exact statement Apply will run."), Tab("Undo SQL", 2, "The statement that puts things back. Roll back runs it.")) };
            DockPanel.SetDock(tabs, Dock.Top); sp.Children.Add(tabs);

            if (detailTab == 0)
            {
                if (c.Fields.Count == 0)
                    sp.Children.Add(U.Stack(U.Callout(c.Structure ? c.UndoNote : c.Kind == ChangeKind.Raw ? (c.UndoAvailable ? "This statement touches several rows, so there's no single before/after. Open the SQL and Undo SQL tabs to see exactly what runs and how it's undone." : c.UndoNote) : "No field details.", c.UndoAvailable ? "info" : "warn")));
                else
                {
                    var g = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, HeadersVisibility = DataGridHeadersVisibility.Column, SelectionUnit = DataGridSelectionUnit.FullRow, RowHeight = double.NaN, VerticalAlignment = VerticalAlignment.Top };
                    g.Columns.Add(Col("Field", "Field", 1.4, true));
                    g.Columns.Add(Col("Before", "Before", 1, true));
                    g.Columns.Add(Col("After", "After", 1, true));
                    g.Columns.Add(Col("What it means", "Meaning", 2, false));
                    g.ItemsSource = c.Fields.Select(f => new DiffRow { Field = f.Name, Before = f.BeforeDisplay ?? f.Before ?? "(no row)", After = f.AfterDisplay ?? f.After ?? "(removed)", Meaning = TableCatalog.FieldMeaning(c.Table, f.Name) }).ToList();
                    sp.Children.Add(g);
                }
            }
            else
            {
                var code = U.Code(detailTab == 1 ? c.ForwardSql : c.UndoSql, double.NaN);
                if (detailTab == 2)
                {
                    var note = U.Callout(c.UndoAvailable ? (c.UndoNote ?? "Generated from the values you saw when you staged the edit. Roll back checks the row still matches what Apply wrote before running it.") : c.UndoNote,
                        c.UndoAvailable ? "info" : "warn", c.UndoAvailable ? "How undo works" : "No automatic undo");
                    DockPanel.SetDock(note, Dock.Top); sp.Children.Add(note);
                }
                var copy = U.Btn("Copy", () => { try { Clipboard.SetText(code.Text); w.Toast("Copied.", "ok"); } catch { } }, "BtnGhost", "Copy this SQL to the clipboard.");
                copy.HorizontalAlignment = HorizontalAlignment.Left; copy.Margin = new Thickness(0, 10, 0, 0);
                DockPanel.SetDock(copy, Dock.Bottom); sp.Children.Add(copy);
                sp.Children.Add(code);
            }
            detail.Content = U.Card(sp, new Thickness(20, 16, 20, 16), new Thickness(0));
        }

        void ShowDbcDetail(Change c, DockPanel sp)
        {
            var p = c.Dbc;
            var note = U.Callout(c.Kind == ChangeKind.Insert ? $"Adds row {p.Key} to {p.File}." : c.Kind == ChangeKind.Delete ? $"Removes row {p.Key} from {p.File}." : $"Changes {p.Fields.Count} field(s) of {(p.ById ? "ID " : "row ")}{p.Key} in {p.File}.",
                "info", "Client data change (no SQL)");
            ((StackPanel)note.Child).Children.Add(U.T("DBC changes never touch the database, so they don't use Apply. Send them out with the Send out DBC edits card on the right: as files for a client patch, and as an updated server copy (backed up first). Once a change has gone to both, it moves to History by itself; after just one, press Done: move to History. Undo after updating the server copy: restore the backup listed in that card.", 12.5, "Muted", null, true, new Thickness(0, 6, 0, 0)));
            if (p.SentToServerUtc != null || p.SentToPatchUtc != null)
                ((StackPanel)note.Child).Children.Add(U.T("Sent: " + string.Join(" · ", new[] { p.SentToServerUtc != null ? "server copy " + p.SentToServerUtc.Value.ToLocalTime().ToString("MMM d, HH:mm") : null, p.SentToPatchUtc != null ? "patch work folder " + p.SentToPatchUtc.Value.ToLocalTime().ToString("MMM d, HH:mm") : null }.Where(x => x != null)), 12.5, "Ok", FontWeights.SemiBold, true, new Thickness(0, 6, 0, 0)));
            DockPanel.SetDock(note, Dock.Top); sp.Children.Add(note);
            var open = U.Btn("Open in DBC tables", () => w.Navigate("dbc", pg => ((DbcPage)pg).OpenTable(p.File, p.ById ? p.Key : (long?)null)), null, "Show this row in the DBC tables screen.");
            open.HorizontalAlignment = HorizontalAlignment.Left; open.Margin = new Thickness(0, 0, 0, 12);
            DockPanel.SetDock(open, Dock.Top); sp.Children.Add(open);
            var g = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, HeadersVisibility = DataGridHeadersVisibility.Column, SelectionUnit = DataGridSelectionUnit.FullRow, RowHeight = double.NaN, VerticalAlignment = VerticalAlignment.Top };
            g.Columns.Add(Col("Field", "Field", 1.3, true));
            g.Columns.Add(Col("Before", "Before", 1.5, true));
            g.Columns.Add(Col("After", "After", 1.5, true));
            g.Columns.Add(Col("What it means", "Meaning", 1.5, false));
            var rows = (c.Kind == ChangeKind.Update ? p.Fields : p.Fields.Where(f => !string.IsNullOrEmpty(f.After ?? f.Before) && (f.After ?? f.Before) != "0" && (f.After ?? f.Before) != "0x0")).ToList();
            g.ItemsSource = rows.Select(f => new DiffRow { Field = f.Name, Before = f.Before == null ? "(no row)" : f.Before.Length == 0 ? "(empty)" : f.Before, After = f.After == null ? "(removed)" : f.After.Length == 0 ? "(empty)" : f.After, Meaning = DbcLayout.HintOf(p.File, f.Name) }).ToList();
            sp.Children.Add(g);
            detail.Content = U.Card(sp, new Thickness(20, 16, 20, 16), new Thickness(0));
        }

        sealed class DiffRow { public string Field { get; set; } public string Before { get; set; } public string After { get; set; } public string Meaning { get; set; } }

        static DataGridTextColumn Col(string header, string path, double stars, bool mono)
        {
            var c = new DataGridTextColumn { Header = new TextBlock { Text = header }, Binding = new System.Windows.Data.Binding(path), Width = new DataGridLength(stars, DataGridLengthUnitType.Star) };
            var st = new Style(typeof(TextBlock));
            st.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
            st.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 6, 0, 6)));
            if (mono) st.Setters.Add(new Setter(TextBlock.FontFamilyProperty, U.Mono));
            if (path == "After") st.Setters.Add(new Setter(TextBlock.ForegroundProperty, U.R("Amber")));
            if (path == "Meaning") st.Setters.Add(new Setter(TextBlock.ForegroundProperty, U.R("Muted")));
            c.ElementStyle = st;
            return c;
        }

        // ================================================================== apply panel
        List<Change> Ticked() => S.Changes.Pending.Changes.Where(c => c.Selected && !c.Structure && c.Dbc == null).ToList();
        List<Change> TickedDbc() => S.Changes.Pending.Changes.Where(c => c.Selected && c.Dbc != null).ToList();
        bool HasDbChanges => S.Changes.Pending.Changes.Any(c => c.Dbc == null);

        string TargetDb => w.ApplyTarget == "Live" ? S.Profile.WorldDb : S.Profile.SandboxDb;
        string ConfirmWord => w.ApplyTarget == "Live" ? "APPLY LIVE" : "APPLY";

        void BuildApply()
        {
            var p = S.Profile;
            var sp = new StackPanel();
            sp.Children.Add(U.Eyebrow("Step 4"));
            sp.Children.Add(U.H2("Apply"));
            sp.Children.Add(U.T("Pick where the ticked changes go.", 12.5, "Muted", null, true, new Thickness(0, 0, 0, 12)));

            RadioButton Target(string key, string title, string db, string note, bool enabled, string tip)
            {
                var r = new RadioButton { GroupName = "target", IsChecked = w.ApplyTarget == key, IsEnabled = enabled, Margin = new Thickness(0, 0, 0, 8), ToolTip = tip,
                    Content = U.Stack(U.Row(U.T(title, 14, "Text", FontWeights.SemiBold, false), new Border { Width = 8 }, U.Pill(db, "Panel", "Muted")), U.T(note, 12, key == "Live" ? "Bad" : "Muted", null, true, new Thickness(0, 3, 0, 0))) };
                r.Checked += (s, e) => { if (w.ApplyTarget != key) { w.ApplyTarget = key; confirmText = ""; w.RefreshChrome(); BuildApply(); } };
                return r;
            }
            bool sandbox = p.SandboxExists;
            sp.Children.Add(Target("Sandbox", "Sandbox", p.SandboxDb, sandbox ? "Practice copy. Players never see it." : "Not created yet: Settings › Run setup again, step 5.", sandbox,
                "Apply to the practice copy of the world database. Safe to experiment."));
            sp.Children.Add(Target("Live", "Live", p.WorldDb, S.Profiles.Settings.AllowLive ? "Your real world database. Players see this after a reload/restart." : "Locked. Turn on in Settings › Safety.", S.Profiles.Settings.AllowLive,
                S.Profiles.Settings.AllowLive ? "Apply to your real world database." : "Applying to Live is switched off. Settings › Safety › Allow applying to Live."));

            var backup = new CheckBox { IsChecked = true, IsEnabled = false, Margin = new Thickness(0, 8, 0, 4),
                Content = U.T("Back up affected tables first", 13.5, "Text", FontWeights.SemiBold, false),
                ToolTip = "Always on. Before anything runs, Studio saves the affected tables with mysqldump. If the backup fails, Apply stops." };
            sp.Children.Add(backup);
            sp.Children.Add(U.T("Always on · saved to " + Path.Combine(S.Profiles.Settings.BackupRoot, "<date_time>"), 12, "Muted", null, true, new Thickness(27, 0, 0, 14)));

            var ticked = Ticked();
            var tables = Backup.AffectedTables(ticked);
            sp.Children.Add(U.CheckRow(ticked.Count > 0 ? U.Check.Ok : U.Check.Info, "Ticked", $"{ticked.Count} change(s) in {tables.Count} table(s)", tables.Count > 0 ? string.Join(", ", tables) : null, 70));
            var problems = ApplyEngine.Validate(ticked, TargetDb, S.AllDatabases);
            bool anyDbcStaged = S.Changes.Pending.Changes.Any(c => c.Dbc != null);
            if (ticked.Count == 0)
            {
                problems.Remove("No changes are selected.");
                problems.Insert(0, !HasDbChanges && anyDbcStaged
                    ? "Nothing here is for the database: your staged changes are client data (DBC) edits. They don't use Apply; send them out with the Send out DBC edits card" + (HasDbChanges ? " below." : " above.")
                    : "No database change is ticked. Tick at least one in the list on the left.");
            }
            if (!S.HasWriteAccount) problems.Insert(0, "The apply account (studio_rw) isn't set up. Finish setup step 3.");
            if (w.ApplyTarget == "Sandbox" && !sandbox) problems.Insert(0, "The sandbox doesn't exist yet. Create it in setup step 5, or turn on Live in Settings › Safety.");
            if (!File.Exists(S.Profiles.Settings.MysqldumpPath)) problems.Add("mysqldump.exe wasn't found, so no backup can be taken. Set its location in Settings.");
            if (problems.Count > 0) { var cb = U.Callout(string.Join("\n", problems.Select(x => "• " + x)), "warn", "Can't apply yet"); cb.Margin = new Thickness(0, 12, 0, 0); sp.Children.Add(cb); }

            var confirm = U.Box(confirmText, true);
            U.AutomationName(confirm, "Confirmation word");
            var applyBtn = U.Btn(w.ApplyTarget == "Live" ? "Apply to Live" : "Apply to Sandbox", DoApply, "BtnPrimary",
                "Back up, then run the ticked changes in one transaction with the apply account. If anything fails, nothing changes.");
            applyBtn.HorizontalAlignment = HorizontalAlignment.Stretch; applyBtn.HorizontalContentAlignment = HorizontalAlignment.Center; applyBtn.Margin = new Thickness(0);
            void Upd() => applyBtn.IsEnabled = !busy && problems.Count == 0 && confirm.Text.Trim() == ConfirmWord;
            confirm.TextChanged += (s, e) => { confirmText = confirm.Text; Upd(); };
            confirm.PreviewKeyDown += (s, e) => { if (e.Key == Key.Enter && applyBtn.IsEnabled) { DoApply(); e.Handled = true; } };
            var f = U.Field($"Type {ConfirmWord} to confirm", "A typed word makes sure Apply is never one stray click away.", confirm);
            f.Margin = new Thickness(0, 14, 0, 12);
            sp.Children.Add(f);
            Upd();
            sp.Children.Add(applyBtn);
            if (busy) sp.Children.Add(U.Callout(busyText ?? "Working…", "info"));
            else if (lastError != null) { var e2 = U.Callout(lastError, "bad", "Apply stopped: nothing was changed"); e2.Margin = new Thickness(0, 0, 0, 14); AddConflictChoices((StackPanel)e2.Child); sp.Children.Insert(3, e2); }
            else sp.Children.Add(U.T("One transaction: either every ticked change lands, or none does.", 12, "Muted", null, true, new Thickness(0, 10, 0, 0)));
            // The database card is always there (so Apply is never missing); when only DBC edits are staged, their card comes first.
            var outer = new StackPanel();
            var dbCard = U.Card(sp, new Thickness(18, 16, 18, 16), new Thickness(0, 0, 0, 14));
            if (anyDbcStaged && !HasDbChanges) outer.Children.Add(U.Card(BuildDbcCard(), new Thickness(18, 16, 18, 16), new Thickness(0, 0, 0, 14)));
            outer.Children.Add(dbCard);
            if (anyDbcStaged && HasDbChanges) outer.Children.Add(U.Card(BuildDbcCard(), new Thickness(18, 16, 18, 16), new Thickness(0)));
            applyHost.Content = U.Scroll(outer);
        }

        /// <summary>When Apply stopped at one change, plain choices for it: refresh its starting point, skip it, or remove it.</summary>
        void AddConflictChoices(StackPanel into)
        {
            var c = conflictId == null ? null : S.Changes.Pending.Changes.FirstOrDefault(x => x.Id == conflictId);
            if (c == null) return;
            var what = conflictKind switch
            {
                ConflictKind.Changed => "Your choices: refresh the starting point (Studio re-reads the row and keeps your new values, then you apply again), skip this change for now, or show it.",
                ConflictKind.Missing => "The row is gone, so this change can't land. Skip it (untick) or discard it.",
                ConflictKind.Exists => "A row with that key is already there (often because it was applied before). Discard this change, or skip it.",
                ConflictKind.Several => "Skip this change, or discard it and make the edit in the SQL browser.",
                _ => "Skip this change and apply the rest, or show it.",
            };
            into.Children.Add(U.T(what, 12.5, "Text", null, true, new Thickness(0, 8, 0, 6)));
            var row = new WrapPanel();
            Button B(string text, Action a, string style, string tip) { var b = U.Btn(text, a, style, tip); b.Margin = new Thickness(0, 0, 8, 6); return b; }
            if (conflictKind == ConflictKind.Changed)
            {
                row.Children.Add(B("Refresh starting point", () => RefreshBaseline(c, false), "BtnPrimary", "Re-read the row from the database and use it as the new \"before\". Your new values stay. Then apply again."));
                row.Children.Add(B("Refresh and apply now", () => RefreshBaseline(c, true), null, "Refresh the starting point, then run Apply straight away (the confirmation word you typed still counts)."));
            }
            row.Children.Add(B("Skip it (untick)", () => { c.Selected = false; ClearConflict(); S.SaveChanges(); w.Toast("Unticked. Apply again to send the rest.", "info"); }, null, "Leave this change staged but out of the next Apply."));
            if (conflictKind is ConflictKind.Missing or ConflictKind.Exists or ConflictKind.Several)
                row.Children.Add(B("Discard it", () => { S.Changes.Pending.Changes.Remove(c); ClearConflict(); S.SaveChanges(); w.Toast("Discarded: " + c.Summary, "info"); }, null, "Remove this change from the staged list. The database isn't touched."));
            row.Children.Add(B("Show it", () => { selectedId = c.Id; Refresh(); }, "BtnGhost", "Select this change in the list."));
            into.Children.Add(row);
        }

        void ClearConflict() { conflictId = null; conflictKind = ConflictKind.None; lastError = null; }

        void RefreshBaseline(Change c, bool applyAfter)
        {
            try
            {
                ResultTable cur;
                using (var db = S.OpenRead(TargetDb))
                    cur = db.Query(SqlGen.SelectRow(c.Table, c.Key, c.Fields.Select(f => f.Name), false), 2);
                if (!ApplyEngine.RefreshBaseline(c, cur)) { w.Toast("That row can't be read back the same way any more. Skip or discard the change instead.", "warn"); return; }
                ClearConflict();
                if (c.Kind == ChangeKind.Update && c.Fields.Count == 0)
                {
                    S.Changes.Pending.Changes.Remove(c);
                    S.SaveChanges();
                    w.Toast("The database already has your values for that row, so there's nothing left to apply for it. Removed it from the list.", "ok");
                }
                else
                {
                    S.SaveChanges();
                    w.Toast("Starting point refreshed from " + TargetDb + ". Your new values are kept.", "ok");
                }
                if (applyAfter && Ticked().Count > 0) DoApply();
            }
            catch (Exception ex) { w.Toast("Couldn't read the row: " + Friendly.Text(ex), "bad"); }
        }

        // ================================================================== client data (DBC) outputs
        string dbcWorkName = "dbc_edits";
        string dbcConfirm = "";
        DbcOutputResult dbcLast; string dbcLastWhat;

        string StudioRoot => w.IsScreens ? Path.Combine(Path.GetTempPath(), "AndersCraftStudio_screens_patches") : S.Profiles.Settings.Workspace();
        string ServerDbcDir { get { try { var d = ConfParser.ParseFile(S.Profile.ConfPath).DataDir; return string.IsNullOrEmpty(d) ? null : Path.Combine(d, "dbc"); } catch { return null; } } }
        string ClientData => string.IsNullOrEmpty(S.Profile?.ClientDir) ? null : Path.Combine(S.Profile.ClientDir, "Data");

        StackPanel BuildDbcCard()
        {
            var sp = new StackPanel();
            sp.Children.Add(U.Eyebrow("Client data"));
            sp.Children.Add(U.H2("Send out DBC edits"));
            var ticked = TickedDbc();
            var files = ticked.Select(c => c.Table).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            sp.Children.Add(U.T("DBC edits never go to the database and never overwrite an original. Choose one or both ways out; each checks every row first and writes nothing if anything doesn't match.", 12.5, "Muted", null, true, new Thickness(0, 0, 0, 10)));
            sp.Children.Add(U.CheckRow(ticked.Count > 0 ? U.Check.Ok : U.Check.Info, "Ticked", $"{ticked.Count} row change(s) in {files.Count} file(s)", files.Count > 0 ? string.Join(", ", files) : null, 70));
            // where each ticked edit has been sent, and the way out of the list
            int sentServer = ticked.Count(c => c.Dbc.SentToServerUtc != null), sentPatch = ticked.Count(c => c.Dbc.SentToPatchUtc != null);
            if (ticked.Count > 0)
            {
                sp.Children.Add(U.T("Status", 14, "Text", FontWeights.SemiBold, false, new Thickness(0, 12, 0, 4)));
                sp.Children.Add(U.CheckRow(sentServer == ticked.Count ? U.Check.Ok : U.Check.Pending, "Server copy", sentServer == ticked.Count ? "sent" : sentServer == 0 ? "not yet" : $"{sentServer} of {ticked.Count} sent", null, 110));
                sp.Children.Add(U.CheckRow(sentPatch == ticked.Count ? U.Check.Ok : U.Check.Pending, "Patch folder", sentPatch == ticked.Count ? "written" : sentPatch == 0 ? "not yet" : $"{sentPatch} of {ticked.Count} written", null, 110));
                var sent = ticked.Where(c => c.Dbc.SentToServerUtc != null || c.Dbc.SentToPatchUtc != null).ToList();
                if (sent.Count > 0)
                {
                    var next = new List<string>();
                    if (sentPatch > 0) next.Add("Build the patch and install it into your client on the Client patches screen, then restart WoW.");
                    if (sentServer > 0) next.Add("Restart worldserver so it loads the new server DBCs.");
                    var cb2 = U.Callout(string.Join("\n", next.Select(x => "• " + x)), "info", "Next");
                    cb2.Margin = new Thickness(0, 8, 0, 8);
                    sp.Children.Add(cb2);
                    var row = new WrapPanel();
                    var done = U.Btn($"Done: move {sent.Count} sent edit(s) to History", () => DbcDone(sent), "BtnPrimary", "Take the edits you've sent out of the staged list. They're kept in History. Edits that went to both places move there by themselves.");
                    done.Margin = new Thickness(0, 0, 8, 6);
                    row.Children.Add(done);
                    if (sentPatch > 0) { var bp = U.Btn("Open Client patches", () => w.Navigate("patches"), null, "Build and install the client patch."); bp.Margin = new Thickness(0, 0, 8, 6); row.Children.Add(bp); }
                    sp.Children.Add(row);
                }
            }


            // (a) patch work folder
            sp.Children.Add(U.T("1 · Files for a client patch", 14, "Text", FontWeights.SemiBold, false, new Thickness(0, 14, 0, 4)));
            bool client = ClientData != null && Directory.Exists(ClientData);
            sp.Children.Add(U.T($"Writes the patched DBCs into a Client patches work folder (DBFilesClient\\…), adding to what you sent there before (the first time it starts from {(client ? "the copy the client sees today" : "the server's copy, as no client folder is set")}). Then use Send to game on the Spells screen, or build and install the patch on Client patches.", 12, "Muted", null, true, new Thickness(0, 0, 0, 6)));
            var nameBox = U.Box(dbcWorkName, true);
            nameBox.TextChanged += (s, e) => dbcWorkName = nameBox.Text.Trim();
            var nf = U.Field("Work folder name", "Under " + Path.Combine(StudioRoot, "patch_work"), nameBox);
            nf.Margin = new Thickness(0, 0, 0, 8);
            sp.Children.Add(nf);
            var toPatch = U.Btn("Write to patch work folder", DbcToPatch, "BtnPrimary", "Patched copies of the ticked files go into the work folder. Nothing in the game or server folders is touched.");
            toPatch.IsEnabled = ticked.Count > 0;
            sp.Children.Add(toPatch);

            // (b) server copy
            sp.Children.Add(U.T("2 · Update the server's copy", 14, "Text", FontWeights.SemiBold, false, new Thickness(0, 16, 0, 4)));
            var dir = ServerDbcDir;
            sp.Children.Add(U.T($"Backs up the affected files to {Path.Combine(S.Profiles.Settings.BackupRoot, "dbc_<date_time>")}, then replaces them in {dir ?? "(DataDir not found)"} with patched copies. worldserver reads DBCs at start-up: restart it afterwards. Server and client should get the same edits.", 12, "Muted", null, true, new Thickness(0, 0, 0, 6)));
            var confirm = U.Box(dbcConfirm, true);
            var go = U.Btn("Update server DBC copy", DbcToServer, "BtnPrimary", "Back up, then write the patched files into the server's dbc folder.");
            void Upd() => go.IsEnabled = ticked.Count > 0 && dir != null && dbcConfirm.Trim() == "UPDATE DBC";
            confirm.TextChanged += (s, e) => { dbcConfirm = confirm.Text; Upd(); };
            var cf = U.Field("Type UPDATE DBC to confirm", null, confirm); cf.Margin = new Thickness(0, 0, 0, 8);
            sp.Children.Add(cf);
            Upd();
            sp.Children.Add(go);

            if (dbcLast != null)
            {
                var text = string.Join("\n", (dbcLast.Problems.Count > 0 ? dbcLast.Problems.Take(8) : dbcLast.Log.Take(8)));
                var cb = U.Callout(text, dbcLast.Success ? "ok" : "bad", dbcLastWhat + (dbcLast.Success ? " done" : " didn't go through"));
                cb.Margin = new Thickness(0, 12, 0, 0);
                sp.Children.Add(cb);
            }

            // restore
            var root = S.Profiles.Settings.BackupRoot;
            var backups = Directory.Exists(root) ? Directory.GetDirectories(root, "dbc_*").OrderByDescending(d => d).Take(5).ToList() : new List<string>();
            if (backups.Count > 0)
            {
                sp.Children.Add(U.T("Undo an update", 14, "Text", FontWeights.SemiBold, false, new Thickness(0, 16, 0, 4)));
                sp.Children.Add(U.T("Put a server DBC backup back (newest first).", 12, "Muted", null, true, new Thickness(0, 0, 0, 6)));
                foreach (var b in backups)
                {
                    var n = Directory.GetFiles(b, "*.dbc").Length;
                    var bb = U.Btn($"Restore {Path.GetFileName(b)} ({n} file(s))", () => DbcRestore(b), null, b);
                    bb.Margin = new Thickness(0, 0, 0, 6); bb.HorizontalAlignment = HorizontalAlignment.Left;
                    sp.Children.Add(bb);
                }
            }
            return sp;
        }

        void DbcToPatch()
        {
            var ticked = TickedDbc();
            if (ticked.Count == 0) return;
            var name = string.IsNullOrWhiteSpace(dbcWorkName) ? "dbc_edits" : dbcWorkName;
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { w.Toast("The folder name has characters Windows doesn't allow.", "warn"); return; }
            var work = Path.Combine(StudioRoot, "patch_work", name);
            ClientDbcSource client = null;
            try { if (ClientData != null && Directory.Exists(ClientData)) client = new ClientDbcSource(ClientData, Path.Combine(ProfileStore.DefaultRoot, "name_cache")); } catch { }
            var server = ServerDbcDir;
            // start from what this work folder already holds (earlier sends), so each send adds to it instead of starting over
            var inWork = Path.Combine(work, "DBFilesClient");
            Func<string, byte[]> read = f => (File.Exists(Path.Combine(inWork, f)) ? File.ReadAllBytes(Path.Combine(inWork, f)) : null)
                ?? client?.Read(f) ?? (server != null && File.Exists(Path.Combine(server, f)) ? File.ReadAllBytes(Path.Combine(server, f)) : null);
            dbcLastWhat = "Write to patch work folder";
            try { dbcLast = DbcOutputs.ToPatchFolder(ticked, read, work); }
            catch (Exception ex) { dbcLast = new DbcOutputResult(); dbcLast.Problems.Add(Friendly.Text(ex)); }
            Record("DBC → patch folder", work);
            if (dbcLast.Success)
            {
                var moved = S.Changes.MarkDbcSent(ticked, false, work);
                w.Toast($"Wrote {dbcLast.Written.Count} DBC file(s) to {work}. Build the patch on Client patches." + (moved != null ? $" {moved.Changes.Count} edit(s) are now in both places and moved to History." : ""), "ok");
                if (moved != null) { S.RaiseChanges(); Refresh(); return; }
            }
            BuildApply();
        }

        void DbcToServer()
        {
            var ticked = TickedDbc();
            var dir = ServerDbcDir;
            if (ticked.Count == 0 || dir == null || dbcConfirm.Trim() != "UPDATE DBC") return;
            dbcLastWhat = "Update server DBC copy";
            try { dbcLast = DbcOutputs.ToServerCopy(ticked, dir, S.Profiles.Settings.BackupRoot); }
            catch (Exception ex) { dbcLast = new DbcOutputResult(); dbcLast.Problems.Add(Friendly.Text(ex) + " (a file may be in use)"); }
            Record("DBC → server copy", dbcLast.BackupDir);
            dbcConfirm = "";
            if (dbcLast.Success)
            {
                var moved = S.Changes.MarkDbcSent(ticked, true, dbcLast.BackupDir);
                w.Toast($"Updated {dbcLast.Written.Count} server DBC file(s). Backup: {dbcLast.BackupDir}. Restart worldserver to load them." + (moved != null ? $" {moved.Changes.Count} edit(s) are now in both places and moved to History." : ""), "ok");
                if (moved != null) { S.RaiseChanges(); Refresh(); return; }
            }
            BuildApply();
        }

        void DbcDone(List<Change> sent)
        {
            var moved = S.Changes.MoveDbcToHistory(sent);
            if (moved == null) return;
            dbcLast = null;
            S.RaiseChanges();
            Refresh();
            w.Toast($"Moved {moved.Changes.Count} sent DBC edit(s) to History.", "ok");
        }

        void DbcRestore(string backup)
        {
            var typed = TextAsk.Ask(w, "Restore server DBCs", $"Puts the {Directory.GetFiles(backup, "*.dbc").Length} file(s) from {backup} back into the server's dbc folder. Type RESTORE to confirm.", "");
            if (typed?.Trim() != "RESTORE") return;
            var target = File.Exists(Path.Combine(backup, "restore_to.txt")) ? File.ReadAllText(Path.Combine(backup, "restore_to.txt")).Trim() : ServerDbcDir;
            dbcLastWhat = "Restore";
            try { dbcLast = DbcOutputs.RestoreServerCopy(backup, target); }
            catch (Exception ex) { dbcLast = new DbcOutputResult(); dbcLast.Problems.Add(Friendly.Text(ex)); }
            Record("DBC restore", backup);
            BuildApply();
        }

        void Record(string kind, string where)
        {
            S.Changes.Pending.Log.Add(new ApplyRecord { Utc = DateTime.UtcNow, Target = kind, Database = "dbc", BackupDir = where, Success = dbcLast.Success, Message = string.Join(" ", dbcLast.Success ? dbcLast.Log : dbcLast.Problems), Kind = kind });
            S.Changes.SavePending();
        }

        public void ScreensScrollApplyToEnd() { if (applyHost.Content is ScrollViewer sv) { sv.UpdateLayout(); sv.ScrollToEnd(); } }
        public string ScreensConfirm { set { confirmText = value; BuildApply(); } }

        async void DoApply()
        {
            var ticked = Ticked();
            var target = w.ApplyTarget; var dbName = TargetDb;
            if (ticked.Count == 0 || busy) return;
            if (confirmText.Trim() != ConfirmWord) return;
            var problems = ApplyEngine.Validate(ticked, dbName, S.AllDatabases);
            if (problems.Count > 0) { BuildApply(); return; }
            busy = true; ClearConflict(); busyText = "Backing up " + string.Join(", ", Backup.AffectedTables(ticked)) + "…"; BuildApply();
            var p = S.Profile; var st = S.Profiles.Settings;
            var rwPw = S.Secrets.Unprotect(p.RwPassEnc);
            var folder = Backup.BackupFolder(st.BackupRoot, DateTime.Now);
            var rec = new ApplyRecord { Utc = DateTime.UtcNow, Target = target, Database = dbName, BackupDir = folder, Kind = "Apply" };
            try
            {
                await Task.Run(() => Backup.Run(st.MysqldumpPath, p.Host, p.Port, p.RwUser, rwPw, dbName, Backup.AffectedTables(ticked), folder));
                busyText = $"Applying {ticked.Count} change(s) to {dbName}…"; BuildApply();
                var outcome = await Task.Run(() => { using var db = S.OpenWrite(dbName); return ApplyEngine.Apply(ticked, db); });
                rec.Success = outcome.Success; rec.Message = outcome.Message;
                File.WriteAllLines(Path.Combine(folder, "apply_log.sql"), outcome.Log);
                if (outcome.Success)
                {
                    S.Changes.RecordApplied(ticked, rec);
                    S.DatabaseWritten();
                    confirmText = "";
                    w.Toast($"{outcome.Message} Backup: {folder}. They're in History now.", "ok");
                }
                else
                {
                    lastError = outcome.Message;
                    conflictId = outcome.ConflictChangeId; conflictKind = outcome.Conflict;
                    S.Changes.Pending.Log.Add(rec); S.Changes.SavePending();
                }
            }
            catch (Exception ex)
            {
                lastError = (ex.InnerException ?? ex).Message + " Nothing was changed.";
                rec.Success = false; rec.Message = lastError;
                S.Changes.Pending.Log.Add(rec); S.Changes.SavePending();
            }
            busy = false; busyText = null;
            S.RaiseChanges();
            Refresh();
        }

        void DiscardSelected()
        {
            var c = S.Changes.Pending.Changes.FirstOrDefault(x => x.Id == selectedId);
            if (c == null) return;
            S.Changes.Pending.Changes.Remove(c);
            S.SaveChanges();
            w.Toast("Discarded: " + c.Summary + ". Your database was never touched by it.", "info");
        }

        void ExportSql()
        {
            var ticked = Ticked();
            if (ticked.Count == 0) { w.Toast("Tick at least one change.", "warn"); return; }
            var d = new SaveFileDialog { FileName = "studio_changes_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".sql", Filter = "SQL script|*.sql" };
            if (d.ShowDialog(w) != true) return;
            File.WriteAllText(d.FileName, S.Changes.ExportSql(ticked, false));
            File.WriteAllText(Path.ChangeExtension(d.FileName, null) + "_undo.sql", S.Changes.ExportSql(ticked, true));
            w.Toast("Saved the changes and a matching _undo.sql next to it.", "ok");
        }

        // ================================================================== history
        void BuildHistory()
        {
            var hist = S.Changes.History();
            var sp = new StackPanel { MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Left };
            if (hist.Count == 0)
            {
                sp.Children.Add(U.H2("No applies yet"));
                sp.Children.Add(U.T("Each time you Apply, Studio records what ran, where its backup is, and the SQL to undo it. Those records show up here with a Roll back button.", 13.5, "Muted"));
                historyHost.Content = U.Card(sp, new Thickness(28), new Thickness(0));
                return;
            }
            sp.Children.Add(U.T("Newest first. Roll back runs the undo SQL in one transaction after a fresh backup, and stops if a row was changed again since.", 13, "Muted", null, true, new Thickness(0, 0, 0, 12)));
            foreach (var cs in hist)
            {
                var rec = cs.Log.FirstOrDefault(l => l.Kind == "Apply" || l.Kind == "DBC");
                bool dbcSent = rec?.Kind == "DBC";
                bool rolled = cs.State == ChangesetState.RolledBack;
                var head = new DockPanel();
                var state = U.Pill(dbcSent ? "sent out" : rolled ? "rolled back" : "applied", rolled ? "Panel2" : "OkBg", rolled ? "Muted" : "Ok");
                DockPanel.SetDock(state, Dock.Right); head.Children.Add(state);
                head.Children.Add(U.Stack(U.T(cs.Name, 15, "Text", FontWeights.SemiBold, false),
                    U.T(dbcSent ? $"{cs.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm} · {rec.Message}" : $"{cs.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm} · {cs.AppliedTarget} ({cs.AppliedDatabase}) · {cs.Changes.Count} change(s)", 12.5, "Muted", null, true, new Thickness(0, 2, 0, 0))));
                var body = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
                foreach (var c in cs.Changes.Take(8))
                    body.Children.Add(U.Row(U.Pill(c.Category, CategoryBg(c.Category), "Text"), U.T(c.Summary, 12.5, "Text", null, false)));
                foreach (UIElement e in body.Children) e.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 5));
                if (cs.Changes.Count > 8) body.Children.Add(U.T($"…and {cs.Changes.Count - 8} more", 12, "Muted"));
                if (rec?.BackupDir != null) body.Children.Add(U.T((dbcSent ? "Server DBC backup: " : "Backup: ") + rec.BackupDir, 12, "Muted", null, true, new Thickness(0, 6, 0, 0)));
                if (dbcSent)
                {
                    body.Children.Add(U.T("DBC edits have no SQL to roll back. To undo the server copy, restore its backup; for the client, rebuild the patch without the edit.", 12, "Muted", null, true, new Thickness(0, 6, 0, 0)));
                    var dRow = U.Row(U.Btn("Open backup folder", () => { if (rec?.BackupDir != null && Directory.Exists(rec.BackupDir)) Process.Start(new ProcessStartInfo("explorer.exe", "\"" + rec.BackupDir + "\"") { UseShellExecute = true }); else w.Toast("No server backup for this one (it only went to a patch folder), or the folder isn't there any more.", "warn"); }, "BtnGhost", "Show the server DBC files saved before the update."));
                    if (rec?.BackupDir != null && Directory.Exists(rec.BackupDir)) dRow.Children.Add(U.Btn("Restore server DBC backup…", () => { DbcRestore(rec.BackupDir); BuildHistory(); }, null, "Put the server's DBC files back as they were before this update. You'll be asked to type RESTORE."));
                    dRow.Margin = new Thickness(0, 12, 0, 0);
                    body.Children.Add(dRow);
                    sp.Children.Add(U.Card(U.Stack(head, body), new Thickness(20, 16, 20, 16), new Thickness(0, 0, 0, 12)));
                    continue;
                }
                var btns = U.Row(
                    U.Btn("Open backup folder", () => { if (rec?.BackupDir != null && Directory.Exists(rec.BackupDir)) Process.Start(new ProcessStartInfo("explorer.exe", "\"" + rec.BackupDir + "\"") { UseShellExecute = true }); else w.Toast("That backup folder isn't there any more.", "warn"); }, "BtnGhost", "Show the mysqldump files taken before this apply."),
                    U.Btn("Copy undo SQL", () => { try { Clipboard.SetText(S.Changes.ExportSql(cs.Changes, true)); w.Toast("Undo SQL copied.", "ok"); } catch { } }, "BtnGhost", "Copy the undo SQL for this apply."));
                if (!rolled) btns.Children.Add(U.Btn("Roll back…", () => { rollbackFor = cs.Id; rollbackConfirm = ""; BuildHistory(); }, null, "Undo this apply. You'll be asked to confirm."));
                btns.Margin = new Thickness(0, 12, 0, 0);
                body.Children.Add(btns);
                if (rollbackFor == cs.Id && !rolled) body.Children.Add(RollbackPanel(cs));
                foreach (var l in cs.Log.Where(l => l.Kind == "Rollback"))
                    body.Children.Add(U.T($"{l.Utc.ToLocalTime():yyyy-MM-dd HH:mm} roll back: {(l.Success ? "done" : "failed")} · {l.Message}", 12, l.Success ? "Ok" : "Bad", null, true, new Thickness(0, 6, 0, 0)));
                sp.Children.Add(U.Card(U.Stack(head, body), new Thickness(20, 16, 20, 16), new Thickness(0, 0, 0, 12)));
            }
            historyHost.Content = U.Scroll(sp);
        }

        UIElement RollbackPanel(Changeset cs)
        {
            var live = cs.AppliedTarget == "Live";
            var blocked = live && !S.Profiles.Settings.AllowLive;
            var missing = cs.Changes.Count(c => !c.UndoAvailable);
            var sp = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            sp.Children.Add(U.Callout($"Studio backs up {string.Join(", ", Backup.AffectedTables(cs.Changes))} in {cs.AppliedDatabase} again, then runs the undo SQL newest-first in one transaction. If any row was changed after this apply, it stops and nothing changes.", "warn", "Roll back " + cs.Changes.Count + " change(s)"));
            if (blocked) sp.Children.Add(U.Callout("This apply went to Live. Turn on Settings › Safety › Allow applying to Live to roll it back from Studio.", "bad"));
            if (missing > 0) sp.Children.Add(U.Callout($"{missing} change(s) have no automatic undo. Restore the backup folder instead.", "bad"));
            if (!S.HasWriteAccount) sp.Children.Add(U.Callout("The apply account (studio_rw) isn't set up, so Studio can't roll back yet. Finish setup step 3.", "bad"));
            var box = U.Box(rollbackConfirm, true);
            var go = U.Btn("Roll back now", () => DoRollback(cs), "BtnPrimary", "Back up, then run the undo SQL in one transaction.");
            void Upd() => go.IsEnabled = !busy && !blocked && missing == 0 && S.HasWriteAccount && box.Text.Trim() == "ROLL BACK";
            box.TextChanged += (s, e) => { rollbackConfirm = box.Text; Upd(); };
            Upd();
            var f = U.Field("Type ROLL BACK to confirm", null, box, "A typed confirmation, so rollback is never one click away.", 300);
            f.Margin = new Thickness(0);
            go.VerticalAlignment = VerticalAlignment.Bottom; go.Margin = new Thickness(12, 0, 10, 0);
            var cancel = U.Btn("Cancel", () => { rollbackFor = null; BuildHistory(); }, "BtnGhost");
            cancel.VerticalAlignment = VerticalAlignment.Bottom;
            sp.Children.Add(U.Row(f, go, cancel));
            if (busy && busyText != null) sp.Children.Add(U.Callout(busyText, "info"));
            return sp;
        }

        async void DoRollback(Changeset cs)
        {
            if (busy) return;
            busy = true;
            var p = S.Profile; var st = S.Profiles.Settings;
            var rwPw = S.Secrets.Unprotect(p.RwPassEnc);
            var folder = Backup.BackupFolder(st.BackupRoot, DateTime.Now) + "_rollback";
            var rec = new ApplyRecord { Utc = DateTime.UtcNow, Target = cs.AppliedTarget, Database = cs.AppliedDatabase, BackupDir = folder, Kind = "Rollback" };
            busyText = "Backing up before roll back…"; BuildHistory();
            try
            {
                await Task.Run(() => Backup.Run(st.MysqldumpPath, p.Host, p.Port, p.RwUser, rwPw, cs.AppliedDatabase, Backup.AffectedTables(cs.Changes), folder));
                busyText = "Rolling back…"; BuildHistory();
                var outcome = await Task.Run(() => { using var db = S.OpenWrite(cs.AppliedDatabase); return ApplyEngine.Rollback(cs.Changes, db); });
                rec.Success = outcome.Success; rec.Message = outcome.Message;
                File.WriteAllLines(Path.Combine(folder, "rollback_log.sql"), outcome.Log);
                if (outcome.Success) { cs.State = ChangesetState.RolledBack; S.DatabaseWritten(); }
                w.Toast(outcome.Message, outcome.Success ? "ok" : "bad");
            }
            catch (Exception ex) { rec.Success = false; rec.Message = (ex.InnerException ?? ex).Message + " Nothing was changed."; w.Toast(rec.Message, "bad"); }
            cs.Log.Add(rec);
            S.Changes.Save(cs);
            busy = false; busyText = null; rollbackFor = null;
            BuildHistory();
        }

        public void ScreensConflict(string id, ConflictKind kind, string message) { conflictId = id; conflictKind = kind; lastError = message; Refresh(); }
        public void ScreensShowHistory(string rollbackId) { tabHistory.IsChecked = true; rollbackFor = rollbackId; BuildHistory(); }
        public void ScreensDetailTab(int i) { detailTab = i; ShowDetail(); }
    }
}
