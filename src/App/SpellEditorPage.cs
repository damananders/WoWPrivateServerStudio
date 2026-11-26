using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AndersCraftStudio.Core;
using DS = global::Studio.DesignSystem;

namespace AndersCraftStudio
{
    /// <summary>
    /// Spell editor: Spell.dbc as a tabbed form with a live tooltip preview, related-DBC pickers (range, cast time,
    /// duration, radius, icon, visual) with "new row", and the server-side spell_dbc row. Everything is staged.
    /// </summary>
    public sealed partial class SpellEditorPage : IPage
    {
        readonly MainWindow w;
        Studio S => w.S;
        readonly Grid view = new Grid();
        public UIElement View => view;

        // left
        RadioButton srcServer, srcClient;
        readonly TextBox search = U.Box();
        readonly ListBox results = new ListBox();
        // centre
        readonly ContentControl editor = new ContentControl { Focusable = false };
        readonly ContentControl tabBody = new ContentControl { Focusable = false };
        readonly WrapPanel tabBar = new WrapPanel();
        // right
        readonly StackPanel tooltip = new StackPanel();
        readonly StackPanel effectsList = new StackPanel();
        readonly StackPanel guide = new StackPanel();
        readonly DispatcherTimer refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };

        static readonly string[] Tabs = { "Overview", "Effects", "Targeting & range", "Cast time & cooldown", "Cost", "Duration", "Interrupts & procs", "Visuals", "Attributes", "Server side" };
        string tab = "Overview";

        // state
        string source = "server";
        IDbcSource serverSrc; ClientDbcSource clientSrc; bool clientTried;
        ClientFiles clientFiles; IconCache icons;
        readonly Dictionary<string, BitmapSource> bitmapCache = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Per file: the source copy and the working copy (staged changes applied, plus unstaged edits).</summary>
        readonly Dictionary<string, (DbcTable baseT, DbcTable work)> tables = new Dictionary<string, (DbcTable, DbcTable)>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> dirtyFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        DbcSpellRefs refs;
        uint id;
        SpellData sp;
        readonly List<(int field, Control ctl)> marks = new List<(int, Control)>();
        readonly Refs names;
        string loadError;
        TextBlock headerName; Image headerIcon;
        /// <summary>New spells made with Duplicate: new ID → the spell it was copied from (for Compare).</summary>
        readonly Dictionary<uint, uint> copiedFrom = new Dictionary<uint, uint>();

        public SpellEditorPage(MainWindow w)
        {
            this.w = w; names = new Refs(w.S);
            view.Margin = new Thickness(28, 22, 28, 20);
            view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            view.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
            var strip = U.StepStrip(1); DockPanel.SetDock(strip, Dock.Right); strip.VerticalAlignment = VerticalAlignment.Center; head.Children.Add(strip);
            head.Children.Add(U.Stack(U.H1("Spells"), U.T("Spell.dbc as a form: effects, range, cast time, cost, duration, flags, icon and visuals, with the in-game tooltip updating as you type. Edits are staged as DBC row patches with undo.", 13.5, "Muted")));
            view.Children.Add(U.Hero(head));

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(370) });
            Grid.SetRow(g, 1); view.Children.Add(g);

            // left: source + search
            var left = new DockPanel();
            srcServer = new RadioButton { Style = U.S("SegBtn"), Content = "Server", GroupName = "spsrc", IsChecked = true, ToolTip = "Edit against the server's Spell.dbc (Data\\dbc)." };
            srcClient = new RadioButton { Style = U.S("SegBtn"), Content = "Client", GroupName = "spsrc", ToolTip = "Edit against the Spell.dbc the game client really loads (the winning MPQ)." };
            srcServer.Checked += (s, e) => SwitchSource("server");
            srcClient.Checked += (s, e) => SwitchSource("client");
            var seg = new Border { Background = Brushes.Transparent, Padding = new Thickness(3), HorizontalAlignment = HorizontalAlignment.Left, Child = U.Row(srcServer, srcClient), Margin = new Thickness(0, 0, 0, 10) };
            var top = U.Stack(U.Eyebrow("Source"), seg);
            DockPanel.SetDock(top, Dock.Top); left.Children.Add(top);
            search.ToolTip = "Spell name (part of it) or ID. Enter searches.";
            search.KeyDown += (s, e) => { if (e.Key == Key.Enter) RunSearch(); };
            var sb = U.WithPlaceholder(search, "Name or #ID (Enter)"); sb.Margin = new Thickness(0, 0, 0, 8);
            DockPanel.SetDock(sb, Dock.Top); left.Children.Add(sb);
            results.SelectionChanged += (s, e) => { if (results.SelectedItem is ListBoxItem li && li.Tag is uint sid && sid != id) Open(sid); };
            ScrollViewer.SetHorizontalScrollBarVisibility(results, ScrollBarVisibility.Disabled);
            left.Children.Add(results);
            g.Children.Add(U.Card(left, new Thickness(10, 12, 6, 8), new Thickness(0)));

            Grid.SetColumn(editor, 2); g.Children.Add(editor);
            var createBtn = U.Btn("Create a spell…", CreateGuided, "BtnPrimary", "Guided: pick damage, heal, buff or passive, fill in plain fields, and Studio builds the spell and puts it in the spellbook.");
            createBtn.HorizontalAlignment = HorizontalAlignment.Left; createBtn.Margin = new Thickness(0, 14, 0, 0);
            editor.Content = U.Card(U.Stack(U.H2("Pick a spell, or make a new one"), U.T("Search by name or ID on the left to edit a spell. To make a new one step by step, use Create a spell. Ctrl+K also finds DBC tables; Spell.dbc itself is on DBC tables as a raw grid.", 13.5, "Muted"), createBtn), new Thickness(28), new Thickness(0));

            var right = new StackPanel();
            right.Children.Add(U.Card(bookCheck, new Thickness(16, 14, 16, 14), new Thickness(0, 0, 0, 12)));
            right.Children.Add(U.Card(U.Stack(U.Eyebrow("In-game tooltip (preview)"), DS.U.GameTooltip(new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 10, 12, 10), Child = tooltip })), new Thickness(16, 14, 16, 14), new Thickness(0, 0, 0, 12)));
            right.Children.Add(U.Card(U.Stack(U.Eyebrow("What it does"), effectsList), new Thickness(16, 14, 16, 14), new Thickness(0, 0, 0, 12)));
            right.Children.Add(U.Card(guide, new Thickness(16, 14, 16, 14), new Thickness(0)));
            var rs = U.Scroll(right); Grid.SetColumn(rs, 4); g.Children.Add(rs);
            ShowGuide("Spells", "Click into any field and this card explains it in game terms. Changed fields get an amber outline and show what they were before.", null);

            refreshTimer.Tick += (s, e) => { refreshTimer.Stop(); RefreshPreview(); RefreshMarks(); };
            U.OnKey(view, Key.S, ModifierKeys.Control, Stage);
            U.OnKey(view, Key.D, ModifierKeys.Control, Duplicate);
            U.OnKey(view, Key.PageDown, ModifierKeys.Control, () => SwitchTab(+1));
            U.OnKey(view, Key.PageUp, ModifierKeys.Control, () => SwitchTab(-1));
        }

        void SwitchTab(int d) { int i = Array.IndexOf(Tabs, tab); tab = Tabs[(i + d + Tabs.Length) % Tabs.Length]; BuildTabBar(); BuildTab(); }

        // ================================================================ sources & tables
        string ServerDbcDir { get { try { var d = ConfParser.ParseFile(S.Profile.ConfPath).DataDir; return string.IsNullOrEmpty(d) ? null : Path.Combine(d, "dbc"); } catch { return null; } } }
        string ClientDataDir => string.IsNullOrEmpty(S.Profile?.ClientDir) ? null : Path.Combine(S.Profile.ClientDir, "Data");

        IDbcSource Source
        {
            get
            {
                if (source == "client")
                {
                    if (!clientTried) { clientTried = true; var dd = ClientDataDir; if (dd != null && Directory.Exists(dd)) using (new Wait()) clientSrc = new ClientDbcSource(dd, Path.Combine(ProfileStore.DefaultRoot, "name_cache")); }
                    return clientSrc;
                }
                if (serverSrc == null && ServerDbcDir != null) serverSrc = new FolderDbcSource(ServerDbcDir, "Server copy");
                return serverSrc;
            }
        }

        IconCache Icons
        {
            get
            {
                if (icons != null) return icons;
                var dd = ClientDataDir;
                if (dd == null || !Directory.Exists(dd)) { icons = new IconCache(WorkFile); return icons; }
                clientFiles = new ClientFiles(dd);
                // one disk cache per client folder state (archive names, sizes and dates)
                var stamp = string.Join("|", Directory.GetFiles(dd, "*.mpq", SearchOption.AllDirectories).OrderBy(x => x).Select(f => { var fi = new FileInfo(f); return fi.Name + fi.Length + fi.LastWriteTimeUtc.Ticks; }));
                var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(dd + stamp))).Substring(0, 16);
                var disk = w.IsScreens ? null : Path.Combine(ProfileStore.DefaultRoot, "icon_cache", hash);
                icons = new IconCache(p => WorkFile(p) ?? clientFiles.Read(p, out _), disk);
                return icons;
            }
        }

        /// <summary>A file imported into one of Studio's patch work folders (newest first), so new icons show before the patch is built.</summary>
        byte[] WorkFile(string archivePath)
        {
            try
            {
                var root = Path.Combine(w.IsScreens ? Path.Combine(Path.GetTempPath(), "AndersCraftStudio_screens_patches") : S.Profiles.Settings.Workspace(), "patch_work");
                if (!Directory.Exists(root)) return null;
                foreach (var d in Directory.GetDirectories(root).OrderByDescending(Directory.GetLastWriteTimeUtc))
                {
                    var f = Path.Combine(d, archivePath);
                    if (File.Exists(f)) return File.ReadAllBytes(f);
                }
            }
            catch { }
            return null;
        }

        BitmapSource Icon(uint iconId, int size = 0)
        {
            var t = Table("SpellIcon.dbc").work;
            var r = t?.Find(iconId);
            if (r == null) return null;
            var tex = r.S[1];
            if (string.IsNullOrEmpty(tex)) return null;
            if (bitmapCache.TryGetValue(tex, out var bmp)) return bmp;
            var img = Icons?.Get(tex);
            if (img != null) { bmp = BitmapSource.Create(img.Width, img.Height, 96, 96, PixelFormats.Bgra32, null, img.Pixels, img.Width * 4); bmp.Freeze(); }
            bitmapCache[tex] = bmp;
            return bmp;
        }

        (DbcTable baseT, DbcTable work) Table(string file)
        {
            if (tables.TryGetValue(file, out var t)) return t;
            DbcTable b = null, wk = null;
            try
            {
                var bytes = Source?.Read(file) ?? (source == "client" ? (serverSrc ??= ServerDbcDir != null ? new FolderDbcSource(ServerDbcDir, "Server copy") : null)?.Read(file) : null);
                if (bytes != null && bytes.Length > 0)
                {
                    b = DbcTable.Read(file, bytes);
                    var probs = new List<string>();
                    wk = DbcPatcher.Apply(b, S.Changes.Pending.Changes, false, probs) ?? b.Clone();
                }
            }
            catch { b = wk = null; }
            tables[file] = (b, wk);
            return (b, wk);
        }

        DbcSpellRefs MakeRefs()
        {
            var r = new DbcSpellRefs(f => Table(f).work) { SpellTable = Table("Spell.dbc").work };
            r.Items = i => { var n = names.Name("item", i); return n == "(not found)" ? null : n; };
            r.Creatures = i => { var n = names.Name("creature", i); return n == "(not found)" ? null : n; };
            return r;
        }

        void SwitchSource(string s)
        {
            if (source == s) return;
            if (dirtyFiles.Count > 0 && DS.StudioDialog.Show(w, "You have spell edits that aren't staged yet. Switch source and drop them?", "Unstaged edits", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            { (s == "client" ? srcServer : srcClient).IsChecked = true; return; }
            source = s; tables.Clear(); dirtyFiles.Clear(); refs = null;
            if (sp != null) Open(id, force: true); else RunSearch();
        }

        public void OnShow()
        {
            if (Table("Spell.dbc").work == null) { editor.Content = U.Callout(source == "client" ? "The client's Spell.dbc couldn't be read. Check the client folder in Settings." : "The server's Spell.dbc couldn't be found (worldserver.conf DataDir\\dbc).", "warn"); return; }
            if (dirtyFiles.Count == 0 && sp != null) { tables.Clear(); refs = null; Open(id, force: true); }
            if (results.Items.Count == 0) RunSearch();
        }

        // ================================================================ search / open
        void RunSearch()
        {
            results.Items.Clear();
            var t = Table("Spell.dbc").work;
            if (t == null) return;
            var q = search.Text.Trim().TrimStart('#');
            int nf = t.Layout.FieldByName("Name_enUS"), rf = t.Layout.FieldByName("NameSubtext_enUS"), icf = t.Layout.FieldByName("SpellIconID");
            IEnumerable<DbcRow> rows;
            if (q.Length == 0) rows = t.Rows.Where(r => r.V[0] >= S.Profiles.Settings.CustomIdStart).Concat(t.Rows.Take(150));
            else if (uint.TryParse(q, out var n)) rows = t.Rows.Where(r => r.V[0] == n || r.V[0].ToString(CultureInfo.InvariantCulture).StartsWith(q));
            else rows = t.Rows.Where(r => (r.S[nf] ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(r => (r.S[nf] ?? "").Length);
            foreach (var r in rows.Take(200))
            {
                var row = new DockPanel();
                var ic = Icon(r.V[icf]);
                var img = new Image { Width = 28, Height = 28, Source = ic, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Top };
                DockPanel.SetDock(img, Dock.Left); row.Children.Add(img);
                var idT = U.T("#" + r.V[0], 11.5, "Faint", null, false); DockPanel.SetDock(idT, Dock.Right); row.Children.Add(idT);
                row.Children.Add(U.Stack(U.T(r.S[nf] ?? "", 13, "Text", FontWeights.SemiBold, false), string.IsNullOrEmpty(r.S[rf]) ? null : U.T(r.S[rf], 11.5, "Muted", null, false)));
                var li = new ListBoxItem { Content = row, Tag = r.V[0] };
                results.Items.Add(li);
                if (r.V[0] == id) li.IsSelected = true;
            }
            if (results.Items.Count == 0) results.Items.Add(new ListBoxItem { Content = U.T("No spells found.", 12.5, "Muted"), IsEnabled = false });
        }

        /// <summary>Opens a spell by ID (also used by links from other screens).</summary>
        public void Open(uint sid, bool force = false)
        {
            if (!force && dirtyFiles.Count > 0 && sid != id && ChoiceWindow.Ask(w, "Unstaged spell edits", "This spell has edits you haven't staged. Stage them first?", "Stage them", "Discard them", "Stay") is int r && r != 1)
            { if (r == 0) Stage(); else return; }
            if (force || sid != id) { foreach (var f in dirtyFiles.ToList()) tables.Remove(f); dirtyFiles.Clear(); }
            var t = Table("Spell.dbc").work;
            var row = t?.Find(sid);
            if (row == null) { w.Toast($"Spell {sid} isn't in this Spell.dbc.", "warn"); return; }
            id = sid; sp = new SpellData(t, row); refs = MakeRefs();
            BuildEditor();
            RefreshPreview();
        }

        // ================================================================ editor shell
        void BuildEditor()
        {
            marks.Clear();
            var head = new DockPanel();
            var icon = new Image { Width = 52, Height = 52, Source = Icon(sp.U("SpellIconID")), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Top };
            DockPanel.SetDock(icon, Dock.Left); head.Children.Add(icon);
            headerName = U.T(sp.Name, 22, "Text", FontWeights.SemiBold, false);
            headerIcon = icon;
            var titleRow = U.Row(headerName, new Border { Width = 10 }, U.Pill("#" + id, "AmberDim", "Amber"));
            if (!string.IsNullOrEmpty(sp.Rank)) titleRow.Children.Add(U.Pill(sp.Rank));
            titleRow.Children.Add(U.Pill(source == "client" ? "client" : "server", "Panel2", "Muted", source == "client" ? "Edited against the client's Spell.dbc." : "Edited against the server's Spell.dbc."));
            bool isNew = Table("Spell.dbc").baseT?.Find(id) == null;
            if (isNew) titleRow.Children.Add(U.Pill("new spell", "OkBg", "Ok", "This spell isn't in the source Spell.dbc yet: it's a staged or unstaged new row."));
            int staged = S.Changes.Pending.Changes.Count(c => DbcStager.IsDbc(c) && c.Table == "Spell.dbc" && c.Dbc.Key == id);
            if (staged > 0) titleRow.Children.Add(U.Pill("staged", "Amber", "AmberInk", "This spell has staged changes (shown here)."));
            var desc = U.T(SpellCatalog.Schools(sp.U("SchoolMask")) is string sc && sc.Length > 0 ? sc + " spell" : "Spell", 12.5, "Muted", null, false, new Thickness(0, 4, 0, 0));
            var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            buttons.Children.Add(U.Btn("Create a spell…", CreateGuided, null, "Guided: a new damage, heal, buff or passive spell from plain choices, already in the spellbook."));
            buttons.Children.Add(U.Btn("Duplicate as new", Duplicate, null, $"Copy this spell to the first free ID at or above {S.Profiles.Settings.CustomIdStart} (Settings > Custom ID range). Ctrl+D"));
            buttons.Children.Add(U.Btn("Stage", Stage, "BtnPrimary", "Write your edits (and any new related rows) into the changeset. Ctrl+S"));
            buttons.Children.Add(U.Btn("Discard", () => Open(id, force: true), null, "Drop edits that aren't staged yet."));
            buttons.Children.Add(U.Btn("Compare with original", Compare, null, "Every field that differs from the source Spell.dbc, with the old and new value."));
            buttons.Children.Add(U.Btn("Open in DBC tables", () => w.Navigate("dbc", pg => ((DbcPage)pg).OpenTable("Spell.dbc", id)), "BtnGhost", "The raw row, every column, other languages."));
            buttons.Children.Add(U.Btn("Batch edit…", () => w.Navigate("dbc", pg => ((DbcPage)pg).OpenTable("Spell.dbc")), "BtnGhost", "Change many spells at once: DBC tables › Spell.dbc, filter (e.g. SchoolMask=4), then Bulk edit or Find & replace. Staged like any edit."));
            buttons.Children.Add(U.Btn("Export spell…", ExportSpell, "BtnGhost", "Save this spell (and the range, cast time, duration, radius, icon and visual rows it uses) as a .studio-spell.json file to share."));
            buttons.Children.Add(U.Btn("Import spell…", ImportSpell, "BtnGhost", "Add a spell from a .studio-spell.json file under a new ID in your custom range (related rows reused or added)."));
            foreach (Button b in buttons.Children) b.Margin = new Thickness(0, 0, 8, 6);
            head.Children.Add(U.Stack(titleRow, desc, buttons));
            var headStack = U.Stack(head);
            if (isNew || id >= S.Profiles.Settings.CustomIdStart)
            {
                var stagedServer = S.Changes.Pending.Changes.Any(c => c.Table == SpellServerRow.Table && c.Kind == ChangeKind.Insert && c.Key.Count > 0 && c.Key[0].Value == id.ToString(CultureInfo.InvariantCulture));
                var call = U.Callout(stagedServer ? "A matching spell_dbc row is staged, so the server will know this spell from the database too." : "The server knows a custom spell from its own Spell.dbc (Send to game, or Changes › Update server DBC copy, puts it there; restart worldserver after) or from the world database's spell_dbc table. One of the two is enough; spell_dbc is optional.",
                    stagedServer ? "ok" : "info", "Server side");
                if (!stagedServer) { var bt = U.Btn("Stage spell_dbc row (optional)", StageServerRow, null); bt.HorizontalAlignment = HorizontalAlignment.Left; bt.Margin = new Thickness(0, 8, 0, 0); ((StackPanel)call.Child).Children.Add(bt); }
                call.Margin = new Thickness(0, 10, 0, 0);
                headStack.Children.Add(call);
            }
            BuildTabBar();
            var dock = new DockPanel();
            if (tabBar.Parent is Decorator d) d.Child = null;
            var top = U.Stack(U.Card(headStack, new Thickness(20, 16, 20, 14), new Thickness(0, 0, 0, 12)), new Border { Child = tabBar, Margin = new Thickness(0, 0, 0, 10) });
            DockPanel.SetDock(top, Dock.Top); dock.Children.Add(top);
            if (tabBody.Parent is Panel p) p.Children.Remove(tabBody);
            dock.Children.Add(tabBody);
            editor.Content = dock;
            BuildTab();
        }

        void BuildTabBar()
        {
            tabBar.Children.Clear();
            foreach (var t in Tabs)
            {
                var rb = new RadioButton { Style = U.S("SegBtn"), Content = t, GroupName = "sptab", IsChecked = t == tab, Margin = new Thickness(0, 0, 4, 4) };
                var name = t;
                rb.Checked += (s, e) => { if (tab != name) { tab = name; BuildTab(); } };
                tabBar.Children.Add(rb);
            }
        }

        void BuildTab()
        {
            marks.Clear();
            var sp2 = new StackPanel();
            switch (tab)
            {
                case "Overview": OverviewTab(sp2); break;
                case "Effects": EffectsTab(sp2); break;
                case "Targeting & range": RangeTab(sp2); break;
                case "Cast time & cooldown": CastTab(sp2); break;
                case "Cost": CostTab(sp2); break;
                case "Duration": DurationTab(sp2); break;
                case "Interrupts & procs": FlagsTab(sp2); break;
                case "Visuals": VisualsTab(sp2); break;
                case "Attributes": AttributesTab(sp2); break;
                case "Server side": ServerTab(sp2); break;
            }
            tabBody.Content = U.Scroll(sp2);
            RefreshMarks();
        }

        // ================================================================ field builders
        void Changed(string file = "Spell.dbc") { dirtyFiles.Add(file); refreshTimer.Stop(); refreshTimer.Start(); }

        void Focused(string label, string hint, string column) => ShowGuide(label, hint, column);

        void ShowGuide(string title, string text, string column)
        {
            guide.Children.Clear();
            guide.Children.Add(U.Eyebrow("Guide"));
            guide.Children.Add(U.T(title, 15, "Text", FontWeights.SemiBold, true, new Thickness(0, 0, 0, 6)));
            guide.Children.Add(U.T(text ?? "", 13, "Muted", null, true));
            if (column != null && U.Technical) guide.Children.Add(U.T("Column: " + column, 12, "Faint", null, true, new Thickness(0, 8, 0, 0)));
        }

        /// <summary>A text box bound to one Spell.dbc field (by layout field name).</summary>
        FrameworkElement Num(string col, string label, string hint, double width = 200)
        {
            int f = sp.Field(col);
            if (f < 0) return U.T($"({col} isn't in this Spell.dbc layout)", 12, "Faint");
            var box = U.Box(sp.Table.FieldText(sp.Row, f), true);
            box.TextChanged += (s, e) =>
            {
                if (sp.Table.TrySetField(sp.Row, f, box.Text, out var err)) { box.ClearValue(Control.BorderBrushProperty); box.ToolTip = null; Changed(); }
                else { box.BorderBrush = U.R("Bad"); box.ToolTip = err; }
            };
            box.GotKeyboardFocus += (s, e) => Focused(label, hint, col);
            marks.Add((f, box));
            var fe = U.Field(label, null, box, hint, width);   // the hint shows in the Guide card and as a tooltip
            fe.Margin = new Thickness(0, 0, 14, 12);
            return fe;
        }

        FrameworkElement TextArea(string col, string label, string hint, double height = 80)
        {
            int f = sp.Field(col);
            var box = U.Box(sp.Table.FieldText(sp.Row, f));
            box.AcceptsReturn = true; box.TextWrapping = TextWrapping.Wrap; box.Height = height; box.VerticalContentAlignment = VerticalAlignment.Top;
            box.TextChanged += (s, e) => { sp.Table.TrySetField(sp.Row, f, box.Text, out _); Changed(); };
            box.GotKeyboardFocus += (s, e) => Focused(label, hint, col);
            marks.Add((f, box));
            var fe = U.Field(label, hint, box);
            fe.Margin = new Thickness(0, 0, 0, 12);
            return fe;
        }

        /// <summary>A dropdown over a spell enum (effect, aura, target…), keeping unknown values.</summary>
        FrameworkElement Enum(string col, string kind, string label, string hint, double width = 330, Action after = null)
        {
            int f = sp.Field(col);
            if (f < 0) return U.T($"({col} missing)", 12, "Faint");
            long cur = sp.Table.Layout.ColumnOf(f).Kind == DbcKind.Int ? unchecked((int)sp.Row.V[f]) : sp.Row.V[f];
            var cb = new ComboBox { IsTextSearchEnabled = true };
            TextSearch.SetTextPath(cb, "Content");
            var list = SpellCatalog.Of(kind).ToList();
            if (!list.Any(x => x.Value == cur)) list.Insert(0, new SpellEnum { Value = cur, Label = cur == 0 ? "None" : "Unknown (" + cur + ")" });
            if (cur != 0 && !list.Any(x => x.Value == 0)) list.Insert(0, new SpellEnum { Value = 0, Label = "None" });
            foreach (var e in list)
            {
                var item = new ComboBoxItem { Content = U.Technical ? $"{e.Value} · {e.Label}" : e.Label, Tag = e, ToolTip = string.IsNullOrEmpty(e.Desc) ? (U.Technical ? e.Name : null) : e.Desc };
                cb.Items.Add(item);
                if (e.Value == cur) cb.SelectedItem = item;
            }
            var descT = U.T(Desc(list.FirstOrDefault(x => x.Value == cur)), 12, "Muted", null, true, new Thickness(2, 6, 0, 0));
            cb.SelectionChanged += (s, e) =>
            {
                if (!(cb.SelectedItem is ComboBoxItem it) || !(it.Tag is SpellEnum en)) return;
                sp.Table.TrySetField(sp.Row, f, en.Value.ToString(CultureInfo.InvariantCulture), out _);
                descT.Text = Desc(en);
                Changed();
                after?.Invoke();
            };
            cb.GotKeyboardFocus += (s, e) => Focused(label, hint + (cb.SelectedItem is ComboBoxItem it && it.Tag is SpellEnum en && !string.IsNullOrEmpty(en.Desc) ? "\n\n" + en.Label + ": " + en.Desc : ""), col);
            marks.Add((f, cb));
            var sp2 = new StackPanel { Width = width, Margin = new Thickness(0, 0, 14, 12) };
            sp2.Children.Add(U.T(label, 13, "Text", FontWeights.SemiBold, false, new Thickness(0, 0, 0, 6)));
            sp2.Children.Add(cb);
            sp2.Children.Add(descT);
            cb.ToolTip = hint;
            return sp2;
        }

        static string Desc(SpellEnum e) => e == null ? "" : (string.IsNullOrEmpty(e.Desc) ? (e.Studio ? "" : e.Name) : e.Desc);

        /// <summary>Tick boxes for a bitmask field; unknown bits are kept.</summary>
        FrameworkElement Flags(string col, string kind, string title, string hint)
        {
            int f = sp.Field(col);
            var wrap = new WrapPanel();
            var bits = SpellCatalog.Of(kind);
            foreach (var b in bits)
            {
                if (b.Value == 0 || b.Value > uint.MaxValue) continue;
                uint bit = (uint)b.Value;
                bool unknown = b.Label.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase) || b.Name.StartsWith("UNK", StringComparison.OrdinalIgnoreCase);
                var cb = new CheckBox { IsChecked = (sp.Row.V[f] & bit) != 0, Width = 300, Margin = new Thickness(0, 0, 12, 6),
                    Content = U.T(b.Label, 12.5, unknown ? "Muted" : "Text", null, false),
                    ToolTip = $"0x{bit:X8}  {b.Name}" + (string.IsNullOrEmpty(b.Desc) ? "" : "\n" + b.Desc) };
                cb.Checked += (s, e) => { sp.Row.V[f] |= bit; Changed(); };
                cb.Unchecked += (s, e) => { sp.Row.V[f] &= ~bit; Changed(); };
                cb.GotKeyboardFocus += (s, e) => Focused(b.Label, (string.IsNullOrEmpty(b.Desc) ? "" : b.Desc + "\n\n") + (U.Technical ? $"Bit 0x{bit:X} of {col}." : ""), col);
                wrap.Children.Add(cb);
            }
            var valueT = U.T("", 12, "Faint", null, false);
            void Upd() => valueT.Text = $"{col} = 0x{sp.Row.V[f]:X8}";
            Upd();
            foreach (CheckBox c in wrap.Children) { c.Checked += (s, e) => Upd(); c.Unchecked += (s, e) => Upd(); }
            var card = U.Stack(U.Row(U.T(title, 14, "Text", FontWeights.SemiBold, false), new Border { Width = 10 }, valueT), U.T(hint, 12.5, "Muted", null, true, new Thickness(0, 2, 0, 8)), wrap);
            return U.Card(card, new Thickness(16, 12, 16, 8), new Thickness(0, 0, 0, 12));
        }

        /// <summary>A field that points into another DBC: shows the row by name, Pick…, New row…, Open.</summary>
        FrameworkElement Link(string col, string file, string label, string hint, double width = 520)
        {
            int f = sp.Field(col);
            var nameT = U.T("", 13, "Amber", FontWeights.SemiBold, true);
            var box = U.Box(sp.Table.FieldText(sp.Row, f), true); box.Width = 90;
            void Upd() => nameT.Text = RowLabel(file, sp.Row.V[f]);
            box.TextChanged += (s, e) =>
            {
                if (sp.Table.TrySetField(sp.Row, f, box.Text, out var err)) { box.ClearValue(Control.BorderBrushProperty); Upd(); Changed(); }
                else box.BorderBrush = U.R("Bad");
            };
            box.GotKeyboardFocus += (s, e) => Focused(label, hint, col + " → " + file);
            marks.Add((f, box));
            Upd();
            var pick = U.Btn("Pick…", () =>
            {
                var dlg = new PickerWindow(w, "Pick from " + file, hint, q => SearchRows(file, q), "");
                if (dlg.ShowDialog() == true && dlg.Picked != null) box.Text = dlg.Picked.Value.ToString(CultureInfo.InvariantCulture);
            }, null, "Choose an existing row of " + file + ".");
            var add = U.Btn("New row…", () => { var nid = NewRelatedRow(file, sp.Row.V[f]); if (nid != null) box.Text = nid.Value.ToString(CultureInfo.InvariantCulture); }, null, $"Create a new {file} row (a copy of the current one, with your values). It is staged together with the spell.");
            var open = U.Btn("Open", () => w.Navigate("dbc", pg => ((DbcPage)pg).OpenTable(file, sp.Row.V[f])), "BtnGhost", "Show this row in DBC tables.");
            foreach (var b in new[] { pick, add, open }) { b.Margin = new Thickness(8, 0, 0, 0); b.Padding = new Thickness(10, 5, 10, 5); }
            var row = U.Row(box, pick, add, open);
            var sp2 = new StackPanel { Width = width, Margin = new Thickness(0, 0, 14, 12), HorizontalAlignment = HorizontalAlignment.Left };
            sp2.Children.Add(U.T(label, 13, "Text", FontWeights.SemiBold, false, new Thickness(0, 0, 0, 6)));
            sp2.Children.Add(row);
            nameT.Margin = new Thickness(2, 6, 0, 0);
            sp2.Children.Add(nameT);
            if (!string.IsNullOrEmpty(hint)) sp2.Children.Add(U.T(hint, 12, "Muted", null, true, new Thickness(2, 4, 0, 0)));
            return sp2;
        }

        /// <summary>Readable label for a row of a related DBC.</summary>
        string RowLabel(string file, uint rid)
        {
            if (rid == 0) return "0 · none";
            var t = Table(file).work;
            var r = t?.Find(rid);
            if (r == null) return $"{rid} · not in {file}";
            float Fl(string c) { var x = t.Layout.FieldByName(c); return x < 0 ? 0 : BitConverter.Int32BitsToSingle(unchecked((int)r.V[x])); }
            int In(string c) { var x = t.Layout.FieldByName(c); return x < 0 ? 0 : unchecked((int)r.V[x]); }
            string Tx(string c) { var x = t.Layout.FieldByName(c); return x < 0 ? "" : r.S[x] ?? ""; }
            string lbl = file switch
            {
                "SpellRange.dbc" => $"{Tx("DisplayName_enUS")} ({Math.Max(Fl("RangeMin_1"), Fl("RangeMin_2")):0.#}–{Math.Max(Fl("RangeMax_1"), Fl("RangeMax_2")):0.#} yd)",
                "SpellCastTimes.dbc" => In("Base") <= 0 ? "instant" : SpellTooltip.Seconds(In("Base")),
                "SpellDuration.dbc" => SpellTooltip.Seconds(In("Duration")) + (In("MaxDuration") != In("Duration") ? $" (max {SpellTooltip.Seconds(In("MaxDuration"))})" : ""),
                "SpellRadius.dbc" => $"{Fl("Radius"):0.##} yd" + (Fl("RadiusMax") > Fl("Radius") ? $" (max {Fl("RadiusMax"):0.##})" : ""),
                "SpellIcon.dbc" => Path.GetFileName((r.S[1] ?? "").Replace('\\', '/')),
                "SpellVisual.dbc" => $"precast kit {In("PrecastKit")}, cast kit {In("CastKit")}, impact kit {In("ImpactKit")}" + (In("HasMissile") != 0 ? ", missile" : ""),
                _ => t.Layout.NameColumn is DbcColumn nc && !string.IsNullOrEmpty(r.S[nc.Field]) ? r.S[nc.Field] : string.Join(", ", t.Layout.Columns.Skip(1).Take(3).Select(c => c.Name + " " + t.FieldText(r, c.Field))),
            };
            return $"{rid} · {lbl}";
        }

        List<(long id, string name, string extra)> SearchRows(string file, string q)
        {
            var t = Table(file).work;
            if (t == null) return new List<(long, string, string)>();
            q = (q ?? "").Trim();
            var res = new List<(long, string, string)>();
            foreach (var r in t.Rows)
            {
                var lbl = RowLabel(file, r.V[0]);
                if (q.Length > 0 && !lbl.Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
                int dot = lbl.IndexOf(" · ", StringComparison.Ordinal);
                res.Add((r.V[0], dot > 0 ? lbl.Substring(dot + 3) : lbl, Uses(file, r.V[0])));
                if (res.Count >= 200) break;
            }
            return res;
        }

        string Uses(string file, uint rid)
        {
            string col = file switch { "SpellRange.dbc" => "RangeIndex", "SpellCastTimes.dbc" => "CastingTimeIndex", "SpellDuration.dbc" => "DurationIndex", "SpellIcon.dbc" => "SpellIconID", _ => null };
            if (col == null) return null;
            var t = Table("Spell.dbc").work; int f = t.Layout.FieldByName(col);
            int n = 0; foreach (var r in t.Rows) if (r.V[f] == rid) n++;
            return $"used by {n:N0} spell(s)";
        }

        /// <summary>Copies a related row to a new ID (custom range), lets the user edit it, keeps it for staging.</summary>
        uint? NewRelatedRow(string file, uint fromId)
        {
            var (bt, wk) = Table(file);
            if (wk == null) { w.Toast($"{file} isn't available.", "warn"); return null; }
            var src = wk.Find(fromId) ?? wk.Rows.FirstOrDefault();
            var nr = src != null ? src.Clone() : wk.NewRow();
            var nid = wk.NextFreeId((uint)Math.Min(uint.MaxValue, Math.Max(1, S.Profiles.Settings.CustomIdStart)));
            nr.V[0] = nid;
            var dlg = new RowFormWindow(w, $"New {file} row #{nid}", $"A copy of row {fromId}. Change what you need; the new row is staged together with the spell when you press Stage.", wk, nr);
            if (dlg.ShowDialog() != true) return null;
            wk.Rows.Add(nr); wk.InvalidateIndex();
            Changed(file);
            w.Toast($"New {file} row {nid} created. Stage the spell to keep it.", "ok");
            return nid;
        }

        // ================================================================ tabs
        static WrapPanel W(params FrameworkElement[] kids) { var wp = new WrapPanel(); foreach (var k in kids) if (k != null) wp.Children.Add(k); return wp; }
        static Border Section(string title, UIElement body, string sub = null) => U.Card(U.Stack(U.T(title, 15, "Text", FontWeights.SemiBold, false), sub == null ? null : U.T(sub, 12.5, "Muted", null, true, new Thickness(0, 2, 0, 0)), new Border { Height = 10 }, body), new Thickness(18, 14, 18, 8), new Thickness(0, 0, 0, 12));

        void OverviewTab(StackPanel p)
        {
            p.Children.Add(Section("Name and text", U.Stack(
                W(Num("Name_enUS", "Name", "The spell's name in the spellbook and tooltip (English).", 330), Num("NameSubtext_enUS", "Rank / subtext", "Shown next to the name, e.g. \"Rank 3\" or \"Racial\".", 200)),
                TextArea("Description_enUS", "Tooltip description", "The tooltip text. Tokens are filled in by the client: $s1 = amount of effect 1, $d = duration, $o1 = total over time, $t1 = tick interval, $a1 = radius. The preview on the right fills them the same way."),
                TextArea("AuraDescription_enUS", "Buff/debuff text", "Text shown when hovering the buff or debuff icon.", 56)),
                "English text. Other languages: Open in DBC tables › All locales."));
            p.Children.Add(Section("School and type", U.Stack(
                FlagsInline("SchoolMask", "school", "Damage school", "Which school(s) the spell belongs to: resistances, immunities and lockouts use it."),
                W(Enum("DispelType", "dispel", "Dispel type", "Which dispels can remove its aura (Magic, Curse, Disease, Poison…).", 250),
                  Enum("Mechanic", "mechanic", "Mechanic", "The crowd-control mechanic (stun, root…) for diminishing returns and immunities.", 250),
                  Enum("DefenseType", "dmgclass", "Damage class", "How it hits: magic (resisted, crits as a spell), melee or ranged (dodge/parry/miss).", 250),
                  Enum("PreventionType", "prevention", "Prevented by", "Silence stops spells; pacify stops abilities.", 250)))));
            p.Children.Add(Section("Levels and limits", W(
                Num("SpellLevel", "Spell level", "Level the spell is learned at; affects scaling and resist chance.", 150),
                Num("BaseLevel", "Base level", "Level the per-level values start counting from.", 150),
                Num("MaxLevel", "Max level", "Level where per-level scaling stops (0 = none).", 150),
                Num("MaxTargets", "Max targets", "Most units an area spell can hit (0 = no limit).", 150),
                Num("MaxTargetLevel", "Max target level", "Highest target level it works on (0 = any).", 150),
                Num("CumulativeAura", "Stacks", "How many times the aura can stack on one target.", 150),
                Num("Speed", "Missile speed", "Projectile speed in yards per second (0 = instant hit).", 150))));
        }

        FrameworkElement FlagsInline(string col, string kind, string title, string hint)
        {
            int f = sp.Field(col);
            var wrap = new WrapPanel();
            foreach (var b in SpellCatalog.Of(kind))
            {
                uint bit = (uint)b.Value;
                var cb = new CheckBox { IsChecked = (sp.Row.V[f] & bit) != 0, Margin = new Thickness(0, 0, 16, 6), Content = U.T(b.Label, 13, "Text", null, false) };
                cb.Checked += (s, e) => { sp.Row.V[f] |= bit; Changed(); };
                cb.Unchecked += (s, e) => { sp.Row.V[f] &= ~bit; Changed(); };
                cb.GotKeyboardFocus += (s, e) => Focused(title, hint, col);
                wrap.Children.Add(cb);
            }
            return U.Stack(U.T(title, 13, "Text", FontWeights.SemiBold, false, new Thickness(0, 0, 0, 6)), wrap, U.T(hint, 12, "Muted", null, true, new Thickness(2, 0, 0, 12)));
        }

        static readonly int[] AuraEffects = { 6, 27, 35, 65, 119, 128, 129, 143 };

        void EffectsTab(StackPanel p)
        {
            p.Children.Add(U.T("A spell has up to three effects. Each one reads as a sentence; change the type and its settings below.", 13, "Muted", null, true, new Thickness(0, 0, 0, 10)));
            for (int i = 1; i <= 3; i++)
            {
                int n = i;
                var summary = U.T(SpellTooltip.EffectSummary(sp, n, refs) ?? "Empty: no effect in this slot.", 13.5, "Amber", FontWeights.SemiBold, true, new Thickness(0, 0, 0, 10));
                var body = new StackPanel();
                body.Children.Add(summary);
                int eff = sp.Effect(n);
                body.Children.Add(W(Enum("Effect_" + n, "effect", "Effect type", "What this effect does.", 360, () => BuildTab()),
                    AuraEffects.Contains(eff) ? Enum("EffectAura_" + n, "aura", "Aura type", "The kind of buff or debuff this effect applies.", 360, () => BuildTab()) : null));
                if (eff != 0)
                {
                    var (mn, mx) = sp.Points(n);
                    body.Children.Add(W(
                        Num("EffectBasePoints_" + n, "Base points", "The amount, minus one when Die sides is 1 (stored as amount - 1). Rolled value = base points + 1 … base points + die sides.", 160),
                        Num("EffectDieSides_" + n, "Die sides", "Random spread: the roll adds 1 … this many. 1 = fixed amount.", 140),
                        Num("EffectRealPointsPerLevel_" + n, "Per level", "Added per caster level above Base level (up to Max level).", 140),
                        Num("EffectBonusMultiplier_" + n, "Bonus coefficient", "How much spell power / attack power scaling the server applies for this effect (EffectBonusMultiplier).", 160),
                        Num("EffectPointsPerCombo_" + n, "Per combo point", "Extra amount per combo point (rogue/druid finishers).", 150)));
                    body.Children.Add(U.T(mn == mx ? $"Rolls {mn}" : $"Rolls {mn} to {mx}", 12, "Faint", null, false, new Thickness(2, -4, 0, 10)));
                    body.Children.Add(W(
                        Enum("ImplicitTargetA_" + n, "target", "Target A", "Who or where the effect goes (the first target rule).", 300),
                        Enum("ImplicitTargetB_" + n, "target", "Target B", "Second target rule; for areas this is the set of units around Target A.", 300)));
                    body.Children.Add(W(
                        Link("EffectRadiusIndex_" + n, "SpellRadius.dbc", "Radius", "Area size for area targets (SpellRadius.dbc).", 520)));
                    body.Children.Add(W(
                        Num("EffectChainTargets_" + n, "Chain targets", "How many targets it jumps to (Chain Lightning).", 140),
                        Num("EffectChainAmplitude_" + n, "Chain multiplier", "Each jump's amount is multiplied by this.", 140),
                        Num("EffectMultipleValue_" + n, "Multiple value", "Extra factor some effects use (e.g. mana drained → damage).", 140),
                        Num("EffectAuraPeriod_" + n, "Tick every (ms)", "For periodic auras: time between ticks in milliseconds.", 150),
                        Num("EffectMechanic_" + n, "Effect mechanic", "Mechanic for this effect only (Mechanic list, 0 = none).", 150)));
                    body.Children.Add(W(
                        TriggerField(n),
                        Num("EffectItemType_" + n, "Item type", "Item for Create item and similar effects (item_template.entry).", 150),
                        Num("EffectMiscValue_" + n, "Misc value A", "Meaning depends on the effect/aura: stat, school mask, creature entry, power type, etc. (see the effect's description).", 150),
                        Num("EffectMiscValueB_" + n, "Misc value B", "Second extra value (e.g. SummonProperties for Summon).", 150)));
                    body.Children.Add(W(
                        Num("EffectSpellClassMaskA_" + n, "Affects spells mask A", "For spell-modifier auras: which of the class's spells are changed (SpellClassMask bits).", 160),
                        Num("EffectSpellClassMaskB_" + n, "mask B", "", 140), Num("EffectSpellClassMaskC_" + n, "mask C", "", 140)));
                }
                p.Children.Add(Section($"Effect {n}", body));
            }
        }

        FrameworkElement TriggerField(int n)
        {
            var col = "EffectTriggerSpell_" + n;
            int f = sp.Field(col);
            var nameT = U.T("", 12.5, "Amber", null, true, new Thickness(2, 6, 0, 0));
            var box = U.Box(sp.Table.FieldText(sp.Row, f), true); box.Width = 110;
            void Upd() => nameT.Text = sp.Row.V[f] == 0 ? "none" : (refs.SpellName(sp.Row.V[f]) is string nm ? nm : "not in Spell.dbc");
            box.TextChanged += (s, e) => { if (sp.Table.TrySetField(sp.Row, f, box.Text, out _)) { box.ClearValue(Control.BorderBrushProperty); Upd(); Changed(); } else box.BorderBrush = U.R("Bad"); };
            box.GotKeyboardFocus += (s, e) => Focused("Trigger spell", "The spell this effect casts or learns (Trigger spell, Learn spell, proc and periodic trigger auras).", col);
            marks.Add((f, box)); Upd();
            var pick = U.Btn("…", () =>
            {
                var dlg = new PickerWindow(w, "Pick a spell", "The spell this effect triggers.", q => SearchSpells(q), "");
                if (dlg.ShowDialog() == true && dlg.Picked != null) box.Text = dlg.Picked.Value.ToString(CultureInfo.InvariantCulture);
            }, null, "Pick by name"); pick.Padding = new Thickness(10, 5, 10, 5); pick.Margin = new Thickness(6, 0, 0, 0);
            var go = U.Btn("Open", () => { if (sp.Row.V[f] != 0) Open(sp.Row.V[f]); }, "BtnGhost"); go.Padding = new Thickness(8, 5, 8, 5);
            return new StackPanel { Width = 330, Margin = new Thickness(0, 0, 14, 12), Children = { U.T("Trigger spell", 13, "Text", FontWeights.SemiBold, false, new Thickness(0, 0, 0, 6)), U.Row(box, pick, go), nameT } };
        }

        List<(long id, string name, string extra)> SearchSpells(string q)
        {
            var t = Table("Spell.dbc").work; int nf = t.Layout.FieldByName("Name_enUS"), rf = t.Layout.FieldByName("NameSubtext_enUS");
            q = (q ?? "").Trim();
            return t.Rows.Where(r => q.Length == 0 || r.V[0].ToString(CultureInfo.InvariantCulture) == q || (r.S[nf] ?? "").Contains(q, StringComparison.OrdinalIgnoreCase))
                .Take(200).Select(r => ((long)r.V[0], r.S[nf] ?? "", r.S[rf] ?? "")).ToList();
        }

        void RangeTab(StackPanel p)
        {
            p.Children.Add(Section("Range", Link("RangeIndex", "SpellRange.dbc", "Range", "Minimum and maximum distance (SpellRange.dbc). Pick an existing range or make a new row.")));
            p.Children.Add(Section("Who can be targeted", U.Stack(
                W(Num("Targets", "Target flags", "Old-style target flags (hex), mostly for the client's targeting cursor.", 160),
                  Num("TargetCreatureType", "Creature type mask", "Only creatures of these types (bitmask of CreatureType; 0 = any).", 180),
                  Link("RequiresSpellFocus", "SpellFocusObject.dbc", "Needs nearby object", "An object (anvil, forge…) that must be near the caster.", 520)),
                W(Num("FacingCasterFlags", "Facing flags", "1 = the caster must face the target.", 150)))));
        }

        void CastTab(StackPanel p)
        {
            p.Children.Add(Section("Cast time", Link("CastingTimeIndex", "SpellCastTimes.dbc", "Cast time", "How long the cast bar takes (SpellCastTimes.dbc). Pick one or make a new row.")));
            p.Children.Add(Section("Cooldown and global cooldown", W(
                Num("RecoveryTime", "Cooldown (ms)", "Time before the spell can be cast again, in milliseconds.", 170),
                Num("CategoryRecoveryTime", "Category cooldown (ms)", "Cooldown shared with every spell in the same Category.", 190),
                Link("Category", "SpellCategory.dbc", "Category", "Spells in one category share the category cooldown.", 520),
                Num("StartRecoveryTime", "Global cooldown (ms)", "GCD this spell triggers (usually 1500; 0 = off the GCD).", 180),
                Num("StartRecoveryCategory", "GCD category", "133 = the normal global cooldown.", 150))));
        }

        void CostTab(StackPanel p)
        {
            p.Children.Add(Section("Cost", U.Stack(
                W(Enum("PowerType", "power", "Power type", "What the spell costs. Rage and runic power are stored ×10.", 260)),
                W(Num("ManaCost", "Cost", "Flat cost (rage/runic power ×10).", 150),
                  Num("ManaCostPct", "Cost % of base", "Cost as a percentage of the caster's base mana (most class spells in 3.3.5a).", 160),
                  Num("ManaCostPerLevel", "Cost per level", "Added per level above Base level.", 150),
                  Num("ManaPerSecond", "Per second", "Extra cost every second while channeling.", 150),
                  Num("ManaPerSecondPerLevel", "Per second per level", "", 170)),
                W(Link("RuneCostID", "SpellRuneCost.dbc", "Rune cost", "Death knight runes (SpellRuneCost.dbc).", 520)))));
        }

        void DurationTab(StackPanel p)
        {
            p.Children.Add(Section("Duration", Link("DurationIndex", "SpellDuration.dbc", "Duration", "How long auras last (SpellDuration.dbc). Pick an existing duration or make a new row.")));
            p.Children.Add(Section("Charges and stacks", W(
                Num("ProcCharges", "Charges", "How many times the aura can proc before it fades (0 = unlimited).", 150),
                Num("CumulativeAura", "Stacks", "Maximum stack count.", 150))));
        }

        void FlagsTab(StackPanel p)
        {
            p.Children.Add(Flags("InterruptFlags", "interrupt", "Cast interrupted by", "What stops the cast bar."));
            p.Children.Add(Flags("AuraInterruptFlags", "aurainterrupt", "Aura removed by", "What makes the buff/debuff fall off (moving, taking damage, attacking…)."));
            p.Children.Add(Flags("ChannelInterruptFlags", "aurainterrupt", "Channel interrupted by", "What stops a channeled spell (same flags as aura removal)."));
            p.Children.Add(Flags("ProcTypeMask", "proc", "Procs on", "For proc auras: which events can trigger it. The server's spell_proc table can override these."));
            p.Children.Add(Section("Proc chance", W(Num("ProcChance", "Proc chance %", "Chance to proc on each matching event.", 150), Num("ProcCharges", "Charges", "Procs before the aura fades (0 = unlimited).", 150))));
        }

        void VisualsTab(StackPanel p)
        {
            var iconF = sp.Field("SpellIconID");
            var big = new Image { Width = 64, Height = 64, Source = Icon(sp.Row.V[iconF]), Margin = new Thickness(0, 0, 16, 0) };
            var pickIcon = U.Btn("Choose icon…", () =>
            {
                if (Icons == null) { w.Toast("Set the game client folder in Settings to see icons.", "warn"); return; }
                var dlg = new IconPickerWindow(w, this, sp.Row.V[iconF]);
                if (dlg.ShowDialog() == true && dlg.Picked != null) { sp.Row.V[iconF] = dlg.Picked.Value; Changed(); BuildEditor(); }
            }, "BtnPrimary", "Pick from every icon in SpellIcon.dbc, drawn from the client's own textures.");
            p.Children.Add(Section("Icon", U.Stack(U.Row(big, U.Stack(U.T(RowLabel("SpellIcon.dbc", sp.Row.V[iconF]), 13, "Amber", FontWeights.SemiBold, true), new Border { Height = 8 }, pickIcon)), new Border { Height = 12 },
                W(Link("SpellIconID", "SpellIcon.dbc", "Icon (SpellIcon.dbc)", "The spellbook/action bar icon. \"New row\" adds an icon entry pointing at a texture path you type (put the .blp in your patch).", 520),
                  Link("ActiveIconID", "SpellIcon.dbc", "Active icon", "Icon shown while the spell is active (stances, auto-attacks; 0 = same).", 520))),
                Icons == null ? "Icons need the game client folder (Settings)." : "Icons are read from the client's archives and cached."));
            p.Children.Add(Section("Visual effects", W(
                Link("SpellVisualID_1", "SpellVisual.dbc", "Visual", "What the spell looks and sounds like: precast, cast, missile and impact kits (SpellVisual.dbc).", 520),
                Link("SpellVisualID_2", "SpellVisual.dbc", "Second visual", "A second visual some spells play at the same time.", 520),
                Num("SpellPriority", "Priority", "Client-side ordering hint.", 140))));
            p.Children.Add(EffectsPreview(sp.Row.V[sp.Field("SpellVisualID_1")]));
        }

        /// <summary>The visual's effect models (cast, missile, impact…) in the 3D viewer, particles and ribbons included.</summary>
        UIElement EffectsPreview(uint visualId)
        {
            var models = new List<(string what, string path)>();
            try
            {
                var vis = S.Models.Raw("SpellVisual.dbc"); var kits = S.Models.Raw("SpellVisualKit.dbc"); var names = S.Models.Raw("SpellVisualEffectName.dbc");
                string Effect(uint id) { if (names == null || id == 0) return null; for (int i = 0; i < names.Records; i++) if (names.UInt(i, 0) == id) return names.Str(i, 2); return null; }
                int VisRow() { if (vis == null) return -1; for (int i = 0; i < vis.Records; i++) if (vis.UInt(i, 0) == visualId) return i; return -1; }
                int r = VisRow();
                if (r >= 0)
                {
                    var kitNames = new[] { "Precast", "Cast", "Impact", "State", "State done", "Channel" };
                    string[] parts = { "head", "chest", "base", "left hand", "right hand", "breath", "left weapon", "right weapon", "special 1", "special 2", "special 3", "world" };
                    for (int k = 0; k < 6; k++)
                    {
                        uint kit = vis.UInt(r, 1 + k); if (kit == 0 || kits == null) continue;
                        for (int i = 0; i < kits.Records; i++)
                        {
                            if (kits.UInt(i, 0) != kit) continue;
                            for (int e = 0; e < 12; e++) { var f = Effect(kits.UInt(i, 3 + e)); if (f != null) models.Add(($"{kitNames[k]} · {parts[e]}", M2Model.M2Path(f))); }
                            break;
                        }
                    }
                    var missile = Effect(vis.UInt(r, 8)); if (missile != null) models.Add(("Missile", M2Model.M2Path(missile)));
                }
            }
            catch { }
            models = models.GroupBy(m => m.path, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
            var viewer = new ModelViewer(S.Models, 340);
            var pick = new ComboBox { Width = 380 };
            foreach (var (what, path) in models) pick.Items.Add(new ComboBoxItem { Content = $"{what} — {Path.GetFileNameWithoutExtension(path)}", Tag = path });
            pick.SelectionChanged += (s, e) => { if (pick.SelectedItem is ComboBoxItem ci) viewer.Load(new ModelLibrary.Request { Path = (string)ci.Tag, Title = Convert.ToString(ci.Content) }); };
            if (models.Count == 0) viewer.ShowMessage(visualId == 0 ? "This spell has no visual." : "No effect models found for this visual (or the client folder isn't set).");
            else pick.SelectedIndex = 0;
            lastEffects = viewer;
            return Section("3D effect preview", U.Stack(models.Count > 1 ? pick : null, models.Count > 1 ? new Border { Height = 8 } : null, viewer.View),
                "The visual's effect models with their particles and ribbons. The effects are an approximation of what the game draws (colours and fades are simplified).");
        }

        public ModelViewer lastEffects;

        void AttributesTab(StackPanel p)
        {
            p.Children.Add(U.T("Attributes switch special rules on and off. Hover a box for what it does; grey ones aren't understood well.", 13, "Muted", null, true, new Thickness(0, 0, 0, 10)));
            string[] cols = { "Attributes", "AttributesEx", "AttributesEx2", "AttributesEx3", "AttributesEx4", "AttributesEx5", "AttributesEx6", "AttributesEx7" };
            for (int i = 0; i < 8; i++) p.Children.Add(Flags(cols[i], "attr" + i, cols[i], $"Attribute group {i} (SPELL_ATTR{i})."));
        }

        void ServerTab(StackPanel p)
        {
            var info = new StackPanel();
            int rows = -1; List<string> cols = null; string err = null;
            var counts = new List<(string table, int n)>();
            try
            {
                if (!S.HasReadAccount) err = "Finish setup step 3 (the read-only account) to check the world database.";
                else
                {
                    using var db = S.OpenRead(S.Profile.WorldDb);
                    var c = db.Query("SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'spell_dbc' ORDER BY ORDINAL_POSITION", 400);
                    cols = c.Rows.Select(r => Convert.ToString(r[0])).ToList();
                    if (cols.Count > 0) rows = int.Parse(db.Scalar($"SELECT COUNT(*) FROM spell_dbc WHERE ID = {id}") ?? "0", CultureInfo.InvariantCulture);
                    foreach (var (tbl, key) in new[] { ("spell_custom_attr", "spell_id"), ("spell_proc", "SpellId"), ("spell_script_names", "spell_id"), ("spell_bonus_data", "entry"), ("spell_ranks", "spell_id"), ("spell_linked_spell", "spell_trigger") })
                        try { counts.Add((tbl, int.Parse(db.Scalar($"SELECT COUNT(*) FROM {tbl} WHERE {key} = {id}") ?? "0", CultureInfo.InvariantCulture))); } catch { }
                }
            }
            catch (Exception ex) { err = Friendly.Text(ex); }
            bool inDbc = Table("Spell.dbc").baseT?.Find(id) != null;
            var staged = S.Changes.Pending.Changes.FirstOrDefault(c => c.Table == SpellServerRow.Table && c.Kind == ChangeKind.Insert && c.Key.Count > 0 && c.Key[0].Value == id.ToString(CultureInfo.InvariantCulture));
            info.Children.Add(U.CheckRow(inDbc ? U.Check.Ok : U.Check.Warn, $"In the {(source == "client" ? "client's" : "server's")} Spell.dbc", inDbc ? "yes" : "no — new spell", null, 260));
            if (err != null) info.Children.Add(U.CheckRow(U.Check.Info, "spell_dbc (world database)", err, null, 260));
            else if (cols == null || cols.Count == 0) info.Children.Add(U.CheckRow(U.Check.Warn, "spell_dbc (world database)", "this database has no spell_dbc table", null, 260));
            else info.Children.Add(U.CheckRow(rows > 0 ? U.Check.Ok : staged != null ? U.Check.Ok : U.Check.Info, "spell_dbc row", rows > 0 ? "exists" : staged != null ? "staged (INSERT, undo = DELETE)" : "none", null, 260));
            foreach (var (tbl, n) in counts) info.Children.Add(U.CheckRow(n > 0 ? U.Check.Ok : U.Check.Info, tbl, n > 0 ? $"{n} row(s) for this spell" : "none", TableCatalog.Describe(tbl), 260));
            var btn = U.Btn(staged != null ? "Restage spell_dbc row (refresh values)" : "Stage spell_dbc row", StageServerRow, "BtnPrimary", "INSERT INTO spell_dbc with every value of this spell; undo deletes it.");
            btn.IsEnabled = cols != null && cols.Count > 0 && rows <= 0;
            btn.HorizontalAlignment = HorizontalAlignment.Left; btn.Margin = new Thickness(0, 12, 0, 0);
            info.Children.Add(btn);
            if (rows > 0) info.Children.Add(U.T("This spell already has a spell_dbc row; change it in the SQL browser (spell_dbc) so the two stay in step.", 12.5, "Muted", null, true, new Thickness(0, 8, 0, 0)));
            p.Children.Add(Section("The server's copy of this spell", info));
            p.Children.Add(Section("Guide: why the server needs to know", U.T(
                "The game client reads spells from its Spell.dbc (inside your patch). The server reads its own Spell.dbc from Data\\dbc AND the world database's spell_dbc table, which AzerothCore loads on start-up as extra Spell.dbc rows.\n\n" +
                "A brand-new spell must exist on both sides: in the client patch (so the player sees it) and on the server (so it can be cast and its effects run). You can do the server half two ways: stage a spell_dbc row here (a database change, applied with the normal Apply to the sandbox or live), or use \"Update server DBC copy\" on Changes. Use one of them, not both.\n\n" +
                "Edits to existing Blizzard spells don't need spell_dbc: send them out with both DBC outputs. The tables listed above (spell_proc, spell_bonus_data …) are server-only tweaks for the same spell ID.", 13, "Muted")));
        }

        // ================================================================ preview
        void RefreshPreview()
        {
            if (sp == null) return;
            refs.SpellTable = Table("Spell.dbc").work;
            SpellTooltip.Tip tip;
            try { tip = SpellTooltip.Build(sp, refs); } catch (Exception ex) { tooltip.Children.Clear(); tooltip.Children.Add(U.T("Preview failed: " + Friendly.Text(ex), 12, "Bad")); return; }
            if (headerName != null) headerName.Text = sp.Name;
            if (headerIcon != null) headerIcon.Source = Icon(sp.U("SpellIconID"));
            tooltip.Children.Clear();
            var gold = U.R("Game.Gold");
            var white = U.R("Game.White");
            var grey = U.R("Game.Grey");
            TextBlock L(string text, Brush b, double size = 13, bool bold = false) => new TextBlock { Text = text, Foreground = b, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, FontFamily = U.Font };
            DockPanel Pair(string a, string b)
            {
                var d = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                var r = L(b ?? "", white); DockPanel.SetDock(r, Dock.Right); d.Children.Add(r);
                d.Children.Add(L(a ?? "", white));
                return d;
            }
            var nameRow = new DockPanel();
            var rank = L(tip.Rank ?? "", grey); DockPanel.SetDock(rank, Dock.Right); nameRow.Children.Add(rank);
            nameRow.Children.Add(L(tip.Name, white, 15, true));
            tooltip.Children.Add(nameRow);
            if (!string.IsNullOrEmpty(tip.Cost) || !string.IsNullOrEmpty(tip.Range)) tooltip.Children.Add(Pair(tip.Cost, tip.Range));
            if (!string.IsNullOrEmpty(tip.Cast) || !string.IsNullOrEmpty(tip.Cooldown)) tooltip.Children.Add(Pair(tip.Cast, tip.Cooldown));
            if (!string.IsNullOrEmpty(tip.Description)) { var d = L(tip.Description, gold); d.Margin = new Thickness(0, 4, 0, 0); tooltip.Children.Add(d); }
            effectsList.Children.Clear();
            if (tip.Effects.Count == 0) effectsList.Children.Add(U.T("No effects.", 12.5, "Muted"));
            foreach (var e in tip.Effects) effectsList.Children.Add(U.T("• " + e, 13, "Text", null, true, new Thickness(0, 0, 0, 4)));
            if (!string.IsNullOrEmpty(tip.AuraText)) effectsList.Children.Add(U.T("Buff text: " + tip.AuraText, 12, "Muted", null, true, new Thickness(0, 6, 0, 0)));
            RefreshBook();
        }

        void RefreshMarks()
        {
            if (sp == null) return;
            var orig = Table("Spell.dbc").baseT?.Find(id);
            foreach (var (f, ctl) in marks)
            {
                if (orig == null) { ctl.BorderBrush = U.R("Ok"); continue; }
                var was = Table("Spell.dbc").baseT.FieldText(orig, f);
                var now = sp.Table.FieldText(sp.Row, f);
                if (was != now) { ctl.BorderBrush = U.R("Amber"); ctl.ToolTip = "Was " + (was.Length == 0 ? "(empty)" : was); }
                else if (ctl.BorderBrush != U.R("Bad")) { ctl.ClearValue(Control.BorderBrushProperty); ctl.ClearValue(Control.BorderThicknessProperty); if (ctl.ToolTip is string tt && tt.StartsWith("Was ")) ctl.ToolTip = null; }
            }
        }

        // ================================================================ actions
        public void Stage()
        {
            if (sp == null) return;
            int n = 0;
            foreach (var file in dirtyFiles.Concat(new[] { "Spell.dbc" }).Distinct(StringComparer.OrdinalIgnoreCase).ToList())
            {
                var (bt, wk) = Table(file);
                if (bt == null || wk == null) continue;
                n += DbcStager.Restage(S.Changes.Pending, bt, wk, source);
            }
            // keep a staged spell_dbc row in step with the spell
            if (S.Changes.Pending.Changes.Any(c => c.Table == SpellServerRow.Table && c.Kind == ChangeKind.Insert && c.Key.Count > 0 && c.Key[0].Value == id.ToString(CultureInfo.InvariantCulture)))
                StageServerRowQuiet();
            S.SaveChanges();
            dirtyFiles.Clear();
            w.Toast(n == 0 ? "Nothing differs from the source copy; nothing staged." : $"Staged {n} DBC row change(s). Send them out from Changes (patch work folder and/or server copy).", n == 0 ? "info" : "ok");
            BuildEditor(); RefreshPreview();
        }

        void Duplicate()
        {
            if (sp == null) return;
            var t = Table("Spell.dbc").work;
            var used = new HashSet<uint>();
            try
            {
                if (S.HasReadAccount)
                {
                    using var db = S.OpenRead(S.Profile.WorldDb);
                    var q = db.Query($"SELECT ID FROM spell_dbc WHERE ID >= {S.Profiles.Settings.CustomIdStart} ORDER BY ID LIMIT 5000", 5000);
                    foreach (var r in q.Rows) used.Add(Convert.ToUInt32(r[0]));
                }
            }
            catch { }
            var nid = SpellIds.FreeId(t, S.Changes.Pending, (uint)Math.Min(uint.MaxValue, Math.Max(1, S.Profiles.Settings.CustomIdStart)), used);
            var copy = sp.Row.Clone();
            copiedFrom[nid] = copiedFrom.TryGetValue(id, out var root) ? root : id;
            copy.V[0] = nid;
            t.Rows.Add(copy); t.InvalidateIndex();
            dirtyFiles.Add("Spell.dbc");
            id = nid; sp = new SpellData(t, copy);
            BuildEditor(); RefreshPreview(); RunSearch();
            w.Toast($"Copied as spell {nid}. Change it, then Stage. The Server side tab adds the spell_dbc row the server needs.", "ok");
        }

        void ExportSpell()
        {
            if (sp == null) return;
            var d = new Microsoft.Win32.SaveFileDialog { FileName = AssetImport.SafeName(sp.Name) + ".studio-spell.json", Filter = "Studio spell (*.studio-spell.json)|*.studio-spell.json" };
            if (d.ShowDialog(w) != true) return;
            File.WriteAllText(d.FileName, SpellPackage.Export(sp, f => Table(f).work));
            w.Toast($"Saved spell {id} to {d.FileName}.", "ok");
        }

        void ImportSpell()
        {
            var d = new Microsoft.Win32.OpenFileDialog { Filter = "Studio spell (*.studio-spell.json)|*.studio-spell.json|JSON|*.json" };
            if (d.ShowDialog(w) != true) return;
            ImportSpellText(File.ReadAllText(d.FileName));
        }

        public void ImportSpellText(string json)
        {
            var t = Table("Spell.dbc").work;
            if (t == null) return;
            try
            {
                var res = SpellPackage.Import(json, t, f => Table(f).work, (uint)Math.Min(uint.MaxValue, Math.Max(1, S.Profiles.Settings.CustomIdStart)), S.Changes.Pending);
                foreach (var f in res.TouchedFiles) dirtyFiles.Add(f);
                id = res.NewId; sp = new SpellData(t, t.Find(id)); refs = MakeRefs();
                BuildEditor(); RefreshPreview(); RunSearch();
                w.Toast($"Imported \"{res.Name}\" as spell {id}. " + string.Join(" ", res.Notes.Take(3)) + " Stage it to keep it.", "ok");
            }
            catch (Exception ex) { w.Toast("Not imported: " + Friendly.Text(ex), "bad"); }
        }

        void StageServerRow()
        {
            StageServerRowQuiet();
            S.SaveChanges();
            w.Toast($"Staged INSERT INTO spell_dbc for spell {id} (undo deletes it). It's applied with the normal Apply.", "ok");
            BuildEditor();
        }

        void StageServerRowQuiet()
        {
            List<string> cols = null;
            try
            {
                if (S.HasReadAccount)
                {
                    using var db = S.OpenRead(S.Profile.WorldDb);
                    cols = db.Query("SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'spell_dbc'", 400).Rows.Select(r => Convert.ToString(r[0])).ToList();
                    if (cols.Count == 0) cols = null;
                }
            }
            catch { cols = null; }
            SpellServerRow.Stage(S.Changes.Pending, sp, cols);
        }

        void Compare()
        {
            if (sp == null) return;
            var bt = Table("Spell.dbc").baseT;
            var orig = bt?.Find(id);
            uint from = 0;
            if (orig == null && copiedFrom.TryGetValue(id, out from)) orig = bt?.Find(from);
            var rows = new List<(string field, string was, string now)>();
            var l = sp.Table.Layout;
            for (int f = 0; f < l.FieldCount; f++)
            {
                var now = sp.Table.FieldText(sp.Row, f);
                var was = orig == null ? null : bt.FieldText(orig, f);
                if (was != now && !(orig == null && (now == "" || now == "0" || now == "0x0"))) rows.Add((l.FieldName(f), was ?? "(new spell)", now));
            }
            var g = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, HeadersVisibility = DataGridHeadersVisibility.Column, MaxHeight = 460 };
            g.Columns.Add(new DataGridTextColumn { Header = "Field", Binding = new System.Windows.Data.Binding("Item1"), Width = new DataGridLength(1.2, DataGridLengthUnitType.Star) });
            g.Columns.Add(new DataGridTextColumn { Header = "Original", Binding = new System.Windows.Data.Binding("Item2"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            g.Columns.Add(new DataGridTextColumn { Header = "Now", Binding = new System.Windows.Data.Binding("Item3"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            g.ItemsSource = rows.Select(r => Tuple.Create(r.field, r.was, r.now)).ToList();
            var body = U.Stack(U.T(from != 0 && orig != null ? $"A new spell copied from {from} ({bt.FieldText(orig, sp.Field("Name_enUS"))}): {rows.Count} field(s) differ from that spell." : orig == null ? "This is a new spell: every non-empty value is listed." : rows.Count == 0 ? "Nothing differs from the source Spell.dbc." : $"{rows.Count} field(s) differ from the source Spell.dbc (staged and unstaged edits together).", 13, "Muted", null, true, new Thickness(0, 0, 0, 10)), g);
            Dialog.Show(w, $"Spell {id}: compared with the original", body);
        }

        internal sealed class Wait : IDisposable
        {
            readonly Cursor prev;
            public Wait() { prev = Mouse.OverrideCursor; Mouse.OverrideCursor = Cursors.Wait; }
            public void Dispose() => Mouse.OverrideCursor = prev;
        }

        // ================================================================ for the icon picker and screenshots
        public IEnumerable<(uint id, string texture)> AllIcons() { var t = Table("SpellIcon.dbc").work; return t == null ? Enumerable.Empty<(uint, string)>() : t.Rows.Select(r => (r.V[0], r.S[1] ?? "")); }
        public BitmapSource IconBitmap(uint iconId) => Icon(iconId);
        public void ScreensSearch(string q) { search.Text = q; RunSearch(); }
        public void ScreensTab(string t) { tab = t; BuildTabBar(); BuildTab(); }
        public void ScreensSet(string col, string text) { sp.Set(col, text, out _); Changed(); BuildTab(); RefreshPreview(); }
        public void ScreensDuplicate() => Duplicate();
        public void ScreensStage() => Stage();
        public void ScreensStageServer() => StageServerRow();
        public void ScreensCompare() => Compare();
        public uint CurrentId => id;
        public IconPickerWindow ScreensIconPicker()
        {
            var p = new IconPickerWindow(w, this, sp.U("SpellIconID")) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, ShowActivated = false };
            return p;
        }
    }

    /// <summary>Small modal with a title and any content (Close button).</summary>
    public static class Dialog
    {
        public static void Show(Window owner, string title, UIElement body, double width = 760)
        {
            var win = new Window { Owner = owner, Width = width, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Background = U.R("Panel"), FontFamily = U.Font, Foreground = U.R("Text") };
            System.Windows.Shell.WindowChrome.SetWindowChrome(win, new System.Windows.Shell.WindowChrome { CaptionHeight = 30, GlassFrameThickness = new Thickness(0), ResizeBorderThickness = new Thickness(0) });
            var close = U.Btn("Close", () => win.Close(), "BtnPrimary"); close.IsCancel = true; close.IsDefault = true; close.HorizontalAlignment = HorizontalAlignment.Left; close.Margin = new Thickness(0, 14, 0, 0);
            win.Content = U.Frame(new Border { Padding = new Thickness(22, 18, 22, 18), Child = U.Stack(U.H2(title), body, close) });
            if (MainWindow.Instance?.IsScreens == true) { win.WindowStartupLocation = WindowStartupLocation.Manual; win.Left = -20000; win.Top = -20000; win.ShowActivated = false; Last = win; win.Show(); return; }
            win.ShowDialog();
        }
        /// <summary>Screenshot mode: the dialog that was opened last (shown modeless).</summary>
        public static Window Last;
    }

    /// <summary>Edits every column of one new DBC row (used for "New row…" on related DBCs).</summary>
    public sealed class RowFormWindow : Window
    {
        public RowFormWindow(Window owner, string title, string help, DbcTable t, DbcRow row)
        {
            Owner = owner; Width = 620; Height = 640; WindowStartupLocation = WindowStartupLocation.CenterOwner; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Background = U.R("Panel"); FontFamily = U.Font; Foreground = U.R("Text");
            System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome { CaptionHeight = 30, GlassFrameThickness = new Thickness(0), ResizeBorderThickness = new Thickness(0) });
            var fields = new StackPanel();
            var boxes = new List<(int f, TextBox box)>();
            var l = t.Layout;
            foreach (var c in l.Columns.Skip(1))
            {
                int f = c.Field;
                var box = U.Box(t.FieldText(row, f), c.Kind != DbcKind.String && c.Kind != DbcKind.Loc);
                boxes.Add((f, box));
                var fe = U.Field(c.Name + (c.Kind == DbcKind.Loc ? " (English)" : ""), c.Hint, box, null);
                fe.Margin = new Thickness(0, 0, 0, 10);
                fields.Children.Add(fe);
            }
            var err = U.T("", 12.5, "Bad", null, true);
            var ok = U.Btn("Create row", () =>
            {
                var copy = row.Clone();
                foreach (var (f, box) in boxes)
                    if (!t.TrySetField(copy, f, box.Text, out var e)) { err.Text = l.FieldName(f) + ": " + e; return; }
                row.V = copy.V; row.S = copy.S;
                DialogResult = true;
            }, "BtnPrimary"); ok.IsDefault = true;
            var cancel = U.Btn("Cancel", () => DialogResult = false, "BtnGhost"); cancel.IsCancel = true;
            var dock = new DockPanel();
            var top = U.Stack(U.H2(title), U.T(help, 13, "Muted", null, true, new Thickness(0, 0, 0, 12)));
            DockPanel.SetDock(top, Dock.Top); dock.Children.Add(top);
            var bottom = U.Stack(err, U.Row(ok, cancel)); bottom.Margin = new Thickness(0, 12, 0, 0);
            DockPanel.SetDock(bottom, Dock.Bottom); dock.Children.Add(bottom);
            dock.Children.Add(U.Scroll(fields));
            Content = U.Frame(new Border { Padding = new Thickness(22, 18, 22, 18), Child = dock });
        }
    }

    /// <summary>Icon picker: every SpellIcon.dbc entry drawn from the client's textures, searchable, paged.</summary>
    public sealed class IconPickerWindow : Window
    {
        public uint? Picked { get; private set; }
        readonly SpellEditorPage pg;
        readonly List<(uint id, string tex)> all;
        readonly WrapPanel grid = new WrapPanel();
        readonly TextBlock status = U.T("", 12.5, "Muted", null, false);
        List<(uint id, string tex)> shown = new List<(uint, string)>();
        int page;
        const int PerPage = 96;

        public IconPickerWindow(Window owner, SpellEditorPage pg, uint current)
        {
            this.pg = pg;
            all = pg.AllIcons().Where(x => x.texture.Length > 0).ToList();
            Owner = owner; Width = 820; Height = 720; WindowStartupLocation = WindowStartupLocation.CenterOwner; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Background = U.R("Panel"); FontFamily = U.Font; Foreground = U.R("Text");
            System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome { CaptionHeight = 30, GlassFrameThickness = new Thickness(0), ResizeBorderThickness = new Thickness(0) });
            var box = U.Box();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += (s, e) => { timer.Stop(); Filter(box.Text); };
            box.TextChanged += (s, e) => { timer.Stop(); timer.Start(); };
            var prev = U.Btn("◀", () => Go(page - 1)); var next = U.Btn("▶", () => Go(page + 1));
            var cancel = U.Btn("Cancel", () => DialogResult = false, "BtnGhost"); cancel.IsCancel = true;
            var dock = new DockPanel();
            var top = U.Stack(U.H2("Choose an icon"), U.T("Every icon listed in SpellIcon.dbc, drawn from the client's own textures. Type part of the texture name (e.g. fire, frost, holy).", 13, "Muted", null, true, new Thickness(0, 0, 0, 10)), U.WithPlaceholder(box, "Search icon names"), U.Row(prev, status, next));
            ((StackPanel)top.Children[3]).Margin = new Thickness(0, 10, 0, 10); status.VerticalAlignment = VerticalAlignment.Center; status.Margin = new Thickness(0, 0, 10, 0);
            DockPanel.SetDock(top, Dock.Top); dock.Children.Add(top);
            DockPanel.SetDock(cancel, Dock.Bottom); cancel.HorizontalAlignment = HorizontalAlignment.Left; cancel.Margin = new Thickness(0, 10, 0, 0); dock.Children.Add(cancel);
            dock.Children.Add(U.Scroll(grid));
            Content = U.Frame(new Border { Padding = new Thickness(22, 18, 22, 18), Child = dock });
            Filter("");
            int idx = shown.FindIndex(x => x.id == current);
            if (idx >= 0) Go(idx / PerPage);
            Loaded += (s, e) => box.Focus();
        }

        public void Filter(string q)
        {
            q = (q ?? "").Trim();
            shown = all.Where(x => q.Length == 0 || x.tex.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || x.id.ToString(CultureInfo.InvariantCulture) == q).ToList();
            Go(0);
        }

        void Go(int p)
        {
            int pages = Math.Max(1, (shown.Count + PerPage - 1) / PerPage);
            page = Math.Max(0, Math.Min(pages - 1, p));
            grid.Children.Clear();
            foreach (var (iid, tex) in shown.Skip(page * PerPage).Take(PerPage))
            {
                var img = new Image { Width = 48, Height = 48, Source = pg.IconBitmap(iid) };
                var b = new Button { Content = img, Width = 60, Height = 60, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(4), ToolTip = $"#{iid}  {Path.GetFileName(tex.Replace('\\', '/'))}" };
                var pick = iid;
                b.Click += (s, e) => { Picked = pick; DialogResult = true; };
                grid.Children.Add(b);
            }
            status.Text = $"{shown.Count:N0} icons · page {page + 1} of {pages}";
        }
    }
}
