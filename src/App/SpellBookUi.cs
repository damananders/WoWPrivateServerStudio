using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using AndersCraftStudio.Core;
using DS = global::Studio.DesignSystem;

namespace AndersCraftStudio
{
    // ============================================================================ Spell editor: spellbook check + guided create
    public sealed partial class SpellEditorPage
    {
        readonly StackPanel bookCheck = new StackPanel();
        ClientDbcSource checkClient; bool checkClientTried;
        readonly Dictionary<string, DbcTable> clientTables = new Dictionary<string, DbcTable>(StringComparer.OrdinalIgnoreCase);
        uint testClass;   // the class the director tests with (0 = any)

        /// <summary>A table as the game client loads it today (null when no client folder is set).</summary>
        DbcTable ClientTable(string file)
        {
            if (source == "client") return Table(file).baseT;
            if (clientTables.TryGetValue(file, out var t)) return t;
            if (!checkClientTried)
            {
                checkClientTried = true;
                var dd = ClientDataDir;
                if (dd != null && Directory.Exists(dd)) using (new Wait()) checkClient = new ClientDbcSource(dd, Path.Combine(ProfileStore.DefaultRoot, "name_cache"));
            }
            try { var b = checkClient?.Read(file); t = b == null ? null : DbcTable.Read(file, b); } catch { t = null; }
            clientTables[file] = t;
            return t;
        }
        bool ClientKnown => source == "client" || (ClientTable("Spell.dbc") != null);

        /// <summary>Forget the client's tables (after Send to game installed a new patch).</summary>
        public void ClientChanged() { clientTables.Clear(); checkClient = null; checkClientTried = false; RefreshBook(); }

        void RefreshBook()
        {
            bookCheck.Children.Clear();
            bookCheck.Children.Add(U.Eyebrow("Will it show in the spellbook?"));
            if (sp == null) { bookCheck.Children.Add(U.T("Open a spell to check it.", 12.5, "Muted")); return; }
            List<SpellBook.Issue> issues;
            try
            {
                var cs = ClientTable("Spell.dbc");
                issues = SpellBook.Check(new SpellBook.Input
                {
                    Spell = sp, Links = Table("SkillLineAbility.dbc").work, SkillLine = Table("SkillLine.dbc").work,
                    ClientKnown = cs != null, ClientSpell = cs?.Find(id) is DbcRow cr ? new SpellData(cs, cr) : null, ClientLinks = ClientTable("SkillLineAbility.dbc"),
                    IconExists = i => Table("SpellIcon.dbc").work?.Find(i) != null,
                    VisualExists = v => (Table("SpellVisual.dbc").work ?? ClientTable("SpellVisual.dbc"))?.Find(v) != null,
                    ClassMask = testClass,
                });
            }
            catch (Exception ex) { bookCheck.Children.Add(U.T("Couldn't check: " + Friendly.Text(ex), 12.5, "Bad")); return; }
            foreach (var i in issues)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
                var mark = new TextBlock { Text = i.Level == "ok" ? "" : i.Level == "bad" ? "" : "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 13, Margin = new Thickness(0, 2, 10, 0), VerticalAlignment = VerticalAlignment.Top,
                    Foreground = U.R(i.Level == "ok" ? "Ok" : i.Level == "bad" ? "Bad" : "Amber") };
                DockPanel.SetDock(mark, Dock.Left); row.Children.Add(mark);
                var body = U.Stack(U.T(i.Text, 12.5, i.Level == "ok" ? "Ok" : "Text", i.Level == "ok" ? FontWeights.SemiBold : (FontWeight?)null, true));
                if (i.Fix != null)
                {
                    var fix = i.Fix;
                    var b = U.Btn(i.FixLabel ?? "Fix it", () => Fix(fix), i.Level == "bad" ? "BtnAmber" : null);
                    b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 6, 0, 0); b.Padding = new Thickness(10, 4, 10, 4);
                    body.Children.Add(b);
                }
                row.Children.Add(body);
                bookCheck.Children.Add(row);
            }
            var cls = new ComboBox { Width = 170, Margin = new Thickness(0, 0, 0, 0), ToolTip = "The class you'll test with. Leave on Any class to check the link for every class." };
            cls.Items.Add(new ComboBoxItem { Content = "Any class", Tag = 0u });
            foreach (var c in SpellBook.Classes) cls.Items.Add(new ComboBoxItem { Content = c.name, Tag = c.mask });
            cls.SelectedItem = cls.Items.Cast<ComboBoxItem>().First(x => (uint)x.Tag == testClass);
            cls.SelectionChanged += (s, e) => { testClass = (uint)((ComboBoxItem)cls.SelectedItem).Tag; RefreshBook(); };
            var ship = U.Btn("Send to game…", () => Fix("ship"), issues.Any(x => x.Fix == "ship") ? "BtnPrimary" : null, "Build one client patch with every spell and DBC edit you've made, so it wins over your other patches, and install it (with a backup).");
            var tools = U.Row(U.T("Testing as", 12, "Muted", null, false, new Thickness(0, 0, 8, 0)), cls);
            tools.Margin = new Thickness(0, 4, 0, 8); ((FrameworkElement)tools.Children[0]).VerticalAlignment = VerticalAlignment.Center;
            bookCheck.Children.Add(tools);
            ship.HorizontalAlignment = HorizontalAlignment.Left;
            bookCheck.Children.Add(ship);
        }

        void Fix(string what)
        {
            if (sp == null) return;
            switch (what)
            {
                case "unhide":
                case "unpassive":
                    var a = sp.U("Attributes") & ~(what == "unhide" ? SpellBook.AttrDoNotDisplay : SpellBook.AttrPassive);
                    sp.Set("Attributes", "0x" + a.ToString("X", CultureInfo.InvariantCulture), out _);
                    Changed(); BuildTab(); RefreshPreview();
                    w.Toast(what == "unhide" ? "Unticked \"Do not display\". Stage to keep it." : "Unticked Passive. Stage to keep it.", "ok");
                    break;
                case "selftrigger":
                    for (int i = 1; i <= 3; i++) if (sp.Trigger(i) == id) sp.Set("EffectTriggerSpell_" + i, "0", out _);
                    Changed(); BuildTab(); RefreshPreview();
                    break;
                case "link": PickTab(); break;
                case "autolearn":
                    {
                        var sla = Table("SkillLineAbility.dbc").work;
                        int n = sla == null ? 0 : SpellBook.SetAutoLearn(sla, id, true);
                        if (n > 0) Changed("SkillLineAbility.dbc");
                        RefreshBook();
                        w.Toast("Every character of the linked classes will learn it by themselves at their next login. Stage it, Send to game, and restart the server.", "ok");
                    }
                    break;
                case "ship":
                    if (dirtyFiles.Count > 0)
                    {
                        var r = ChoiceWindow.Ask(w, "Unstaged spell edits", "This spell has edits you haven't staged. Stage them first, so they go to the game too?", "Stage them", "Send without them", "Cancel");
                        if (r == 0) Stage(); else if (r != 1) return;
                    }
                    var win = new GameShipWindow(w);
                    win.ShowDialog();
                    if (win.Installed) ClientChanged();
                    break;
            }
        }

        /// <summary>Choose the spellbook tab (class skill line) for this spell; adds the SkillLineAbility link.</summary>
        void PickTab()
        {
            var tabs = SpellBook.ClassTabs(Table("SkillLine.dbc").work, Table("SkillRaceClassInfo.dbc").work);
            var sla = Table("SkillLineAbility.dbc").work;
            if (sla == null || tabs.Count == 0) { w.Toast("SkillLine.dbc / SkillLineAbility.dbc couldn't be read from the server's dbc folder.", "bad"); return; }
            var dlg = new TabPickWindow(w, tabs, testClass);
            if (dlg.ShowDialog() != true) return;
            var chosen = dlg.Every ? SpellBook.Classes.Select(c => tabs.First(t => (t.ClassMask & c.mask) != 0)).Where(t => t != null).ToList() : new List<SpellBook.Tab> { dlg.Picked };
            foreach (var t in chosen) SpellBook.AddLink(sla, id, t.SkillLine, t.ClassMask, autoLearn: dlg.AutoLearn);
            Changed("SkillLineAbility.dbc");
            RefreshBook();
            w.Toast(dlg.Every ? $"Linked to a spellbook tab for every class ({chosen.Count} SkillLineAbility rows). Stage to keep it." : $"Linked to {dlg.Picked}. Stage to keep it.", "ok");
        }

        /// <summary>The guided "Create a spell". Makes the new spell (unstaged), links it to a spellbook tab and opens it.</summary>
        public void CreateGuided()
        {
            var t = Table("Spell.dbc").work;
            if (t == null) { w.Toast("The server's Spell.dbc couldn't be read.", "bad"); return; }
            var tabs = SpellBook.ClassTabs(Table("SkillLine.dbc").work, Table("SkillRaceClassInfo.dbc").work);
            var dlg = new CreateSpellWindow(w, this, tabs);
            if (dlg.ShowDialog() != true) return;
            var r = dlg.Result;
            var tmpl = t.Find(SpellRecipe.TemplateOf(r.Kind));
            if (tmpl == null) { w.Toast($"The template spell {SpellRecipe.TemplateOf(r.Kind)} isn't in this Spell.dbc.", "bad"); return; }
            var used = new HashSet<uint>();
            try
            {
                if (S.HasReadAccount)
                {
                    using var db = S.OpenRead(S.Profile.WorldDb);
                    foreach (var q in db.Query($"SELECT ID FROM spell_dbc WHERE ID >= {S.Profiles.Settings.CustomIdStart} ORDER BY ID LIMIT 5000", 5000).Rows) used.Add(Convert.ToUInt32(q[0]));
                }
            }
            catch { }
            var nid = SpellIds.FreeId(t, S.Changes.Pending, (uint)Math.Min(uint.MaxValue, Math.Max(1, S.Profiles.Settings.CustomIdStart)), used);
            var row = tmpl.Clone(); row.V[0] = nid;
            t.Rows.Add(row); t.InvalidateIndex();
            var data = new SpellData(t, row);
            var notes = SpellRecipe.Apply(data, r, f => Table(f).work);
            dirtyFiles.Add("Spell.dbc");
            copiedFrom[nid] = tmpl.V[0];
            if (dlg.Every || dlg.Tab != null)
            {
                var sla = Table("SkillLineAbility.dbc").work;
                var chosen = dlg.Every ? SpellBook.Classes.Select(c => tabs.FirstOrDefault(x => (x.ClassMask & c.mask) != 0)).Where(x => x != null).ToList() : new List<SpellBook.Tab> { dlg.Tab };
                if (sla != null) { foreach (var tb in chosen) SpellBook.AddLink(sla, nid, tb.SkillLine, tb.ClassMask, autoLearn: dlg.AutoLearn); dirtyFiles.Add("SkillLineAbility.dbc"); }
            }
            id = nid; sp = data; refs = MakeRefs();
            BuildEditor(); RefreshPreview(); RunSearch();
            w.Toast($"Made spell {nid}, \"{r.Name}\". Check the tooltip and the spellbook card, then Stage (Ctrl+S) and Send to game." + (notes.Count > 0 ? " " + notes[0] : ""), "ok");
        }

        /// <summary>The tooltip lines for a draft spell (the guided window's preview).</summary>
        public List<string> TooltipFor(SpellData draft)
        {
            try { refs ??= MakeRefs(); refs.SpellTable = Table("Spell.dbc").work; return SpellTooltip.Build(draft, refs).Lines(); }
            catch (Exception ex) { return new List<string> { "Preview failed: " + Friendly.Text(ex) }; }
        }
        public DbcTable WorkTable(string file) => Table(file).work;
        public System.Windows.Media.Imaging.BitmapSource IconFor(uint iconId) => Icon(iconId);
        public void ScreensCreate(SpellRecipe.Recipe r, SpellBook.Tab tab)
        {
            var t = Table("Spell.dbc").work;
            var nid = SpellIds.FreeId(t, S.Changes.Pending, (uint)Math.Max(1, S.Profiles.Settings.CustomIdStart));
            var row = t.Find(SpellRecipe.TemplateOf(r.Kind)).Clone(); row.V[0] = nid; t.Rows.Add(row); t.InvalidateIndex();
            var data = new SpellData(t, row);
            SpellRecipe.Apply(data, r, f => Table(f).work);
            dirtyFiles.Add("Spell.dbc");
            if (tab != null) { SpellBook.AddLink(Table("SkillLineAbility.dbc").work, nid, tab.SkillLine, tab.ClassMask, autoLearn: true); dirtyFiles.Add("SkillLineAbility.dbc"); }
            id = nid; sp = data; refs = MakeRefs(); BuildEditor(); RefreshPreview(); RunSearch();
        }
        public void ScreensUnlink() { var t = Table("SkillLineAbility.dbc").work; t.Rows.RemoveAll(r => r.V[t.Layout.FieldByName("Spell")] == id); t.InvalidateIndex(); RefreshBook(); }
        public void ScreensRelink(SpellBook.Tab tab) { if (tab != null) SpellBook.AddLink(Table("SkillLineAbility.dbc").work, id, tab.SkillLine, tab.ClassMask); dirtyFiles.Add("SkillLineAbility.dbc"); RefreshBook(); }
        public string ScreensCheckText => string.Join("\n", bookCheck.Children.OfType<DockPanel>().Select(d => d.Children.OfType<StackPanel>().FirstOrDefault()?.Children.OfType<TextBlock>().FirstOrDefault()?.Text));
    }

    // ============================================================================ pick a spellbook tab
    public sealed class TabPickWindow : Window
    {
        public SpellBook.Tab Picked { get; private set; }
        public bool Every { get; private set; }
        public bool AutoLearn { get; private set; } = true;

        public TabPickWindow(Window owner, List<SpellBook.Tab> tabs, uint classMask)
        {
            Owner = owner; Title = "Spellbook tab"; Width = 560; SizeToContent = SizeToContent.Height; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Background = U.R("Panel"); FontFamily = U.Font; Foreground = U.R("Text");
            WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 30, GlassFrameThickness = new Thickness(0), ResizeBorderThickness = new Thickness(0) });
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) DialogResult = false; };
            var list = new ComboBox { MaxDropDownHeight = 420 };
            foreach (var t in tabs) list.Items.Add(new ComboBoxItem { Content = t.ToString(), Tag = t });
            list.SelectedIndex = Math.Max(0, tabs.FindIndex(t => classMask != 0 && (t.ClassMask & classMask) != 0));
            var every = new CheckBox { Content = U.T("Every class (one tab each: the first one of each class)", 13, "Text", null, false), Margin = new Thickness(0, 12, 0, 0) };
            every.Checked += (s, e) => list.IsEnabled = false; every.Unchecked += (s, e) => list.IsEnabled = true;
            var auto = new CheckBox { IsChecked = true, Content = U.T("Every character of the class learns it by themselves (at their next login)", 13, "Text", null, false), Margin = new Thickness(0, 8, 0, 0),
                ToolTip = "Off: only a trainer, a quest or the GM command .learn teaches it." };
            var ok = U.Btn("Add to the spellbook", () => { Picked = (SpellBook.Tab)((ComboBoxItem)list.SelectedItem).Tag; Every = every.IsChecked == true; AutoLearn = auto.IsChecked == true; DialogResult = true; }, "BtnPrimary");
            var cancel = U.Btn("Cancel", () => DialogResult = false, "BtnGhost");
            Content = U.Frame(new Border { Padding = new Thickness(22, 18, 22, 18),
                Child = U.Stack(U.H2("Which spellbook tab?"),
                    U.T("The spellbook shows a spell under the tab of the skill line it's linked to (SkillLineAbility.dbc). Every character of a class has that class's tabs, so pick the class and tab it belongs under. Tick \"learns it by themselves\" and every character of the class gets it at their next login; otherwise it has to be learned (trainer, quest, item or .learn).", 13, "Muted", null, true, new Thickness(0, 0, 0, 12)),
                    U.Field("Class › tab", null, list), every, auto, U.Row(ok, cancel)),
            });
            ((StackPanel)((Border)Content).Child).Children[^1].SetValue(MarginProperty, new Thickness(0, 16, 0, 0));
        }
    }

    // ============================================================================ guided "Create a spell"
    public sealed class CreateSpellWindow : Window
    {
        public SpellRecipe.Recipe Result { get; } = new SpellRecipe.Recipe();
        public SpellBook.Tab Tab { get; private set; }
        public bool Every { get; private set; }
        public bool AutoLearn { get; private set; } = true;
        readonly SpellEditorPage pg;
        readonly StackPanel preview = new StackPanel();
        readonly ContentControl kindBody = new ContentControl { Focusable = false };
        readonly Image iconImg = new Image { Width = 44, Height = 44 };
        readonly List<SpellBook.Tab> tabs;
        TextBox nameBox;

        public CreateSpellWindow(Window owner, SpellEditorPage pg, List<SpellBook.Tab> tabs)
        {
            this.pg = pg; this.tabs = tabs;
            Owner = owner; Title = "Create a spell"; Width = 980; Height = 760; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResizeWithGrip; ShowInTaskbar = false; Background = U.R("Bg"); FontFamily = U.Font; Foreground = U.R("Text");
            WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 30, GlassFrameThickness = new Thickness(0), ResizeBorderThickness = new Thickness(6) });
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) DialogResult = false; };
            Result.IconId = pg.WorkTable("Spell.dbc")?.Find(133) is DbcRow fr ? fr.V[pg.WorkTable("Spell.dbc").Layout.FieldByName("SpellIconID")] : 0;

            var grid = new Grid { Margin = new Thickness(24, 20, 24, 18) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });

            var form = new StackPanel();
            form.Children.Add(U.Eyebrow("Create a spell"));
            form.Children.Add(U.H2("What should it do?"));
            form.Children.Add(U.T("Pick a kind and fill in the plain fields. Studio builds the whole Spell.dbc row from a real spell of that kind, links it to a spellbook tab, and opens it in the editor for anything else.", 13, "Muted", null, true, new Thickness(0, 0, 0, 12)));
            var kinds = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            foreach (var (k, label, tip) in new[] {
                (SpellRecipe.Kind.Damage, "Damage", "Hurts a target (like Fireball)."), (SpellRecipe.Kind.Heal, "Heal", "Heals a target (like Lesser Heal)."),
                (SpellRecipe.Kind.Buff, "Buff", "Raises something for a while (like Power Word: Fortitude)."), (SpellRecipe.Kind.Passive, "Passive", "Always on once learned; shows in the book as Passive.") })
            {
                var rb = new RadioButton { Style = U.S("SegBtn"), Content = label, GroupName = "kind", IsChecked = k == Result.Kind, ToolTip = tip };
                var kk = k;
                rb.Checked += (s, e) => { Result.Kind = kk; if (kk == SpellRecipe.Kind.Buff) Result.RangeYards = 30; BuildKind(); Update(); };
                kinds.Children.Add(rb);
            }
            form.Children.Add(new Border { Background = Brushes.Transparent, Padding = new Thickness(3), HorizontalAlignment = HorizontalAlignment.Left, Child = kinds, Margin = new Thickness(0, 0, 0, 12) });

            var name = nameBox = U.Box(Result.Name); name.TextChanged += (s, e) => { Result.Name = name.Text; Update(); };
            var desc = U.Box(""); desc.TextChanged += (s, e) => { Result.Description = desc.Text; Update(); };
            var iconBtn = U.Btn("Choose icon…", () => { var d = new IconPickerWindow(this, pg, Result.IconId); if (d.ShowDialog() == true && d.Picked is uint ic) { Result.IconId = ic; Update(); } });
            form.Children.Add(Pair(U.Field("Name", "As it shows in the spellbook and tooltip.", name), U.Row(iconImg, new Border { Width = 10 }, iconBtn)));
            form.Children.Add(U.Field("Tooltip text (optional)", "Leave empty and Studio writes it for you ($s1 = the amount, $d = the duration).", desc));
            form.Children.Add(kindBody);
            BuildKind();

            // where it shows up
            var tabBox = new ComboBox { MaxDropDownHeight = 380 };
            tabBox.Items.Add(new ComboBoxItem { Content = "Every class (first tab of each)", Tag = null });
            foreach (var t in tabs) tabBox.Items.Add(new ComboBoxItem { Content = t.ToString(), Tag = t });
            tabBox.SelectedIndex = 0;
            var show = new CheckBox { IsChecked = true, Content = U.T("Show it in the spellbook (adds the SkillLineAbility link)", 13, "Text", null, false), Margin = new Thickness(0, 4, 0, 0) };
            var auto = new CheckBox { IsChecked = true, Content = U.T("Every character of the class learns it by themselves (at their next login)", 13, "Text", null, false), Margin = new Thickness(0, 6, 0, 0),
                ToolTip = "Off: only a trainer, a quest or the GM command .learn teaches it." };
            show.Checked += (s, e) => { tabBox.IsEnabled = true; auto.IsEnabled = true; }; show.Unchecked += (s, e) => { tabBox.IsEnabled = false; auto.IsEnabled = false; };
            form.Children.Add(U.T("Where it shows up", 14, "Text", FontWeights.SemiBold, false, new Thickness(0, 10, 0, 4)));
            form.Children.Add(U.Field("Spellbook tab", "The class and tab it's listed under once learned.", tabBox));
            form.Children.Add(show);
            form.Children.Add(auto);

            var ok = U.Btn("Create the spell", () =>
            {
                if (string.IsNullOrWhiteSpace(Result.Name)) { name.Focus(); return; }
                if (show.IsChecked == true) { Tab = ((ComboBoxItem)tabBox.SelectedItem).Tag as SpellBook.Tab; Every = Tab == null; AutoLearn = auto.IsChecked == true; }
                DialogResult = true;
            }, "BtnPrimary", "Makes the spell under the next free custom ID. Nothing is staged until you press Stage in the editor.");
            var cancel = U.Btn("Cancel", () => DialogResult = false, "BtnGhost");
            var btns = U.Row(ok, cancel); btns.Margin = new Thickness(0, 16, 0, 0);
            form.Children.Add(btns);
            grid.Children.Add(U.Scroll(form));

            var right = U.Stack(U.Card(U.Stack(U.Eyebrow("In-game tooltip (preview)"), DS.U.GameTooltip(new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 10, 12, 10), Child = preview })), new Thickness(16, 14, 16, 14), new Thickness(0, 0, 0, 12)),
                U.Callout("After Create: check the tooltip and the spellbook card in the editor, press Stage (Ctrl+S), then Send to game and restart worldserver. With \"learns it by themselves\" ticked, every character of the class has it at their next login; otherwise teach it with a trainer, a quest or .learn.", "info", "Then"));
            Grid.SetColumn(right, 2); grid.Children.Add(right);
            Content = U.Frame(new Border { Child = grid });
            Update();
        }

        static Grid Pair(UIElement a, UIElement b)
        {
            var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ((FrameworkElement)b).VerticalAlignment = VerticalAlignment.Center; ((FrameworkElement)b).Margin = new Thickness(14, 10, 0, 0);
            g.Children.Add(a); Grid.SetColumn(b, 1); g.Children.Add(b);
            return g;
        }

        void BuildKind()
        {
            var sp = new StackPanel();
            var r = Result;
            TextBox Num(int v, Action<int> set) { var b = U.Box(v.ToString(CultureInfo.InvariantCulture)); b.Width = 120; b.TextChanged += (s, e) => { if (int.TryParse(b.Text, out var n)) { set(n); Update(); } }; return b; }
            ComboBox Pick<T>(IEnumerable<(T v, string label)> items, T cur, Action<T> set)
            {
                var c = new ComboBox { Width = 220 };
                foreach (var it in items) c.Items.Add(new ComboBoxItem { Content = it.label, Tag = it.v });
                c.SelectedItem = c.Items.Cast<ComboBoxItem>().FirstOrDefault(x => Equals((T)x.Tag, cur)) ?? c.Items[0];
                c.SelectionChanged += (s, e) => { set((T)((ComboBoxItem)c.SelectedItem).Tag); Update(); };
                return c;
            }
            var w1 = new WrapPanel();
            void Add(FrameworkElement f) { f.Margin = new Thickness(0, 0, 18, 8); w1.Children.Add(f); }
            Add(U.Field(r.Kind switch { SpellRecipe.Kind.Damage => "Damage", SpellRecipe.Kind.Heal => "Healing", _ => r.Boost.Percent ? "Amount (%)" : "Amount" }, null, Num(r.Amount, v => r.Amount = v)));
            if (r.Kind == SpellRecipe.Kind.Damage) Add(U.Field("School", null, Pick(SpellRecipe.Schools.Select(s => (s.mask, s.name)), r.School, v => r.School = v)));
            if (r.Kind is SpellRecipe.Kind.Buff or SpellRecipe.Kind.Passive) Add(U.Field("Raises", null, Pick(SpellRecipe.Boosts.Select(b => (b, b.Label)), r.Boost, v => { r.Boost = v; BuildKind(); })));
            if (r.Kind != SpellRecipe.Kind.Passive)
            {
                Add(U.Field("Cast time", null, Pick(new[] { (0, "Instant"), (1500, "1.5 sec"), (2000, "2 sec"), (2500, "2.5 sec"), (3000, "3 sec") }, r.CastMs, v => r.CastMs = v)));
                Add(U.Field("Cooldown (sec)", null, Num(r.CooldownSec, v => r.CooldownSec = v)));
                Add(U.Field("Mana cost (% of base)", null, Num(r.ManaPct, v => r.ManaPct = v)));
                Add(U.Field("Range", null, Pick(new[] { (0, "On yourself"), (5, "Melee (5 yd)"), (20, "20 yd"), (30, "30 yd"), (40, "40 yd") }, r.RangeYards, v => r.RangeYards = v)));
            }
            if (r.Kind == SpellRecipe.Kind.Buff) Add(U.Field("Lasts (minutes)", null, Num(r.DurationSec / 60, v => r.DurationSec = v * 60)));
            sp.Children.Add(w1);
            kindBody.Content = sp;
        }

        void Update()
        {
            iconImg.Source = pg.IconFor(Result.IconId);
            var t = pg.WorkTable("Spell.dbc");
            var tmpl = t?.Find(SpellRecipe.TemplateOf(Result.Kind));
            preview.Children.Clear();
            if (tmpl == null) { preview.Children.Add(U.T("The template spell isn't in Spell.dbc.", 12, "Bad")); return; }
            var draft = new SpellData(t, tmpl.Clone());
            var copy = new SpellRecipe.Recipe { Kind = Result.Kind, Name = Result.Name, Description = Result.Description, IconId = Result.IconId, Amount = Result.Amount, School = Result.School, Boost = Result.Boost, CastMs = Result.CastMs, CooldownSec = Result.CooldownSec, ManaPct = Result.ManaPct, DurationSec = Result.DurationSec, RangeYards = Result.RangeYards };
            SpellRecipe.Apply(draft, copy, f => pg.WorkTable(f));
            var lines = pg.TooltipFor(draft);
            for (int i = 0; i < lines.Count; i++)
                preview.Children.Add(new TextBlock { Text = lines[i], FontFamily = U.Font, FontSize = i == 0 ? 15 : 13, FontWeight = i == 0 ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap,
                    Foreground = i == lines.Count - 1 && lines.Count > 1 ? U.R("Game.Gold") : U.R("Game.White"), Margin = new Thickness(0, 1, 0, 1) });
        }

        public void ScreensSet(SpellRecipe.Kind k, string name, int amount) { Result.Kind = k; Result.Amount = amount; nameBox.Text = name; BuildKind(); Update(); }
    }

    // ============================================================================ Send to game
    /// <summary>
    /// Builds one client patch with every DBC edit you've sent out or staged (and the files in the patch work folder), named so
    /// it loads after any patch holding the same tables, sends staged edits to the server's copy too, and installs the patch
    /// with a backup. Staged DBC edits then move to History.
    /// </summary>
    public sealed class GameShipWindow : Window
    {
        /// <summary>What the player does next to have the spell(s) just sent: automatic at login, or .learn.</summary>
        string LearnHint()
        {
            try
            {
                var p = System.IO.Path.Combine(ConfParser.ParseFile(w.S.Profile.ConfPath).DataDir, "dbc", "SkillLineAbility.dbc");
                var sla = System.IO.File.Exists(p) ? DbcTable.Read("SkillLineAbility.dbc", System.IO.File.ReadAllBytes(p)) : null;
                var spells = w.S.Changes.History().Concat(new[] { w.S.Changes.Pending }).SelectMany(c => c.Changes).Where(c => c.Dbc != null && c.Dbc.File == "Spell.dbc").Select(c => (uint)c.Dbc.Key).Distinct().ToList();
                var manual = spells.Where(id => SpellBook.Links(sla, id).Count > 0 && !SpellBook.AutoLearned(sla, id)).Take(3).ToList();
                if (manual.Count == 0) return "Characters of the linked classes have the spell after they log in (log out and back in if you're already in game).";
                return "Nobody learns " + string.Join(", ", manual) + " by themselves yet: teach it in game with .learn " + manual[0] + " (as a GM, with your character targeted), a trainer or a quest, or tick \"Everyone learns it automatically\" on its spellbook check and send again.";
            }
            catch { return "Then log in: characters learn it at login if it's set to be learned automatically, otherwise use .learn, a trainer or a quest."; }
        }

        readonly MainWindow w;
        Studio S => w.S;
        public bool Installed { get; private set; }
        GameShip.Plan plan;
        List<Change> staged;
        readonly ContentControl body = new ContentControl { Focusable = false };
        string confirm = "", busy, result;

        string Workspace => w.IsScreens ? Path.Combine(Path.GetTempPath(), "AndersCraftStudio_screens_patches") : S.Profiles.Settings.Workspace();
        string WorkFolder => Path.Combine(Workspace, "patch_work", "dbc_edits");
        string ClientDir => S.Profile?.ClientDir;
        string ServerDbcDir { get { try { var d = ConfParser.ParseFile(S.Profile.ConfPath).DataDir; return string.IsNullOrEmpty(d) ? null : Path.Combine(d, "dbc"); } catch { return null; } } }
        PatchInstaller Installer => new PatchInstaller(Path.Combine(S.Profiles.Root, "installed_patches.json"), S.Profiles.Settings.BackupRoot);

        public GameShipWindow(MainWindow owner)
        {
            w = owner; Owner = owner; Title = "Send to game"; Width = 820; Height = 720; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResizeWithGrip; ShowInTaskbar = false; Background = U.R("Panel"); FontFamily = U.Font; Foreground = U.R("Text");
            WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 30, GlassFrameThickness = new Thickness(0), ResizeBorderThickness = new Thickness(6) });
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape && busy == null) Close(); };
            Content = U.Frame(new Border { Padding = new Thickness(24, 20, 24, 18), Child = body });
            MakePlan();
            Build();
        }

        void MakePlan()
        {
            staged = S.Changes.Pending.Changes.Where(c => c.Selected && DbcStager.IsDbc(c)).ToList();
            ClientDbcSource client = null;
            var data = string.IsNullOrEmpty(ClientDir) ? null : Path.Combine(ClientDir, "Data");
            try { if (data != null && Directory.Exists(data)) using (new SpellEditorPage.Wait()) client = new ClientDbcSource(data, Path.Combine(ProfileStore.DefaultRoot, "name_cache")); } catch { }
            var mine = data == null ? new List<string>() : Installer.Records().Where(r => !r.InPlace && r.DataDir != null && r.DataDir.StartsWith(data, StringComparison.OrdinalIgnoreCase)).Select(r => r.Name).ToList();   // patches Studio only added files to stay "someone else's"
            var server = ServerDbcDir;
            var cache = new Dictionary<string, DbcTable>(StringComparer.OrdinalIgnoreCase);
            Func<string, DbcTable> read = f =>
            {
                if (server == null) return null;
                if (cache.TryGetValue(f, out var t)) return t;
                var p = Path.Combine(server, f);
                return cache[f] = File.Exists(p) ? DbcTable.Read(f, File.ReadAllBytes(p)) : null;
            };
            try { plan = GameShip.Make(client, S.Changes.History(), staged, Directory.Exists(WorkFolder) ? WorkFolder : null, mine, read); }
            catch (Exception ex) { plan = new GameShip.Plan(); plan.Problems.Add(Friendly.Text(ex)); }
            if (client == null) plan.Problems.Insert(0, "No game client folder is set (Settings, or setup step 4), so Studio can't build a patch for it.");
        }

        void Build()
        {
            var sp = new StackPanel();
            sp.Children.Add(U.Eyebrow("Send to game"));
            sp.Children.Add(U.H2("Put your spells and DBC edits in the game"));
            sp.Children.Add(U.T("One client patch with everything you've changed in DBC files (what's in History and what's staged now), built so it wins over the patches already in your client, then installed with a backup. Staged edits also go to the server's copy, so both sides match.", 13, "Muted", null, true, new Thickness(0, 0, 0, 12)));
            if (plan.PatchName != null)
            {
                sp.Children.Add(U.CheckRow(plan.InPlace ? U.Check.Warn : U.Check.Ok, plan.InPlace ? "Goes into" : "Patch", plan.PatchName + (plan.TargetDir != null ? $"  (in Data\\{plan.TargetDir})" : "  (in Data)") + (plan.InPlace ? ", the patch the game reads these tables from (backed up first)" : ""), null, 120));
                sp.Children.Add(U.T(plan.Why, 12.5, "Muted", null, true, new Thickness(0, 2, 0, 8)));
            }
            sp.Children.Add(U.CheckRow(plan.Files.Count > 0 ? U.Check.Ok : U.Check.Warn, "Files", plan.Files.Count == 0 ? "nothing to send" : string.Join(", ", plan.Files.Keys.Take(8)) + (plan.Files.Count > 8 ? $" +{plan.Files.Count - 8}" : ""), null, 120));
            sp.Children.Add(U.CheckRow(staged.Count > 0 ? U.Check.Info : U.Check.Ok, "Server copy", staged.Count > 0 ? $"{staged.Count} staged DBC edit(s) go to the server's dbc folder too (backed up first)" : "already has everything you've sent", null, 120));
            foreach (var n in plan.Notes) sp.Children.Add(U.T("• " + n, 12.5, "Text", null, true, new Thickness(0, 6, 0, 0)));
            if (plan.Problems.Count > 0) { var c = U.Callout(string.Join("\n", plan.Problems.Take(8).Select(x => "• " + x)), "bad", "Can't send yet"); c.Margin = new Thickness(0, 12, 0, 0); sp.Children.Add(c); }

            var box = U.Box(confirm, true);
            var go = U.Btn("Build and install", Go, "BtnPrimary", "Builds the patch, checks it, sends staged edits to the server's copy, and installs the patch (backing up any file it replaces). Close WoW first.");
            void Upd() => go.IsEnabled = busy == null && plan.Ok && !w.IsScreens && box.Text.Trim() == "SEND";
            box.TextChanged += (s, e) => { confirm = box.Text; Upd(); };
            var f = U.Field("Type SEND to confirm", "Close WoW first: the game keeps its patches open while it runs.", box);
            f.Margin = new Thickness(0, 14, 0, 10);
            sp.Children.Add(f);
            Upd();
            var cancel = U.Btn(result != null ? "Close" : "Cancel", Close, "BtnGhost");
            sp.Children.Add(U.Row(go, cancel));
            if (busy != null) sp.Children.Add(U.Callout(busy, "info"));
            if (result != null) { var c = U.Callout(result, Installed ? "ok" : "bad", Installed ? "Sent" : "Didn't go through"); c.Margin = new Thickness(0, 12, 0, 0); sp.Children.Add(c); }

            // undo: remove the patch Studio installed
            var data = string.IsNullOrEmpty(ClientDir) ? null : Path.Combine(ClientDir, "Data");
            var rec = plan.PatchName == null || data == null ? null : Installer.Records().FirstOrDefault(r => string.Equals(r.Name, plan.PatchName, StringComparison.OrdinalIgnoreCase) && r.DataDir != null && r.DataDir.StartsWith(data, StringComparison.OrdinalIgnoreCase));
            if (rec != null && busy == null)
            {
                var un = U.Btn($"Remove {rec.Name} from the game", () =>
                {
                    try { var m = Installer.Uninstall(ClientDir, rec.Name); result = m; Installed = true; w.Toast(m, "ok"); MakePlan(); }
                    catch (Exception ex) { result = "Not removed: " + Friendly.Text(ex); }
                    Build();
                }, "BtnGhost", $"Takes out the patch Studio installed on {rec.InstalledUtc.ToLocalTime():MMM d, HH:mm} and puts back what was there before.");
                un.HorizontalAlignment = HorizontalAlignment.Left; un.Margin = new Thickness(0, 14, 0, 0);
                sp.Children.Add(un);
            }
            body.Content = U.Scroll(sp);
        }

        async void Go()
        {
            if (!plan.Ok || busy != null) return;
            var name = plan.PatchName; var dir = plan.TargetDir; var files = plan.Files.Select(kv => (kv.Key, kv.Value)).ToList();
            var outDir = Path.Combine(Workspace, "patches"); var built = Path.Combine(outDir, name);
            var work = WorkFolder; var client = ClientDir; var server = ServerDbcDir; var backupRoot = S.Profiles.Settings.BackupRoot;
            var running = PatchInstaller.RunningClients(client);
            if (running.Count > 0) { result = "Close the game first: " + string.Join(", ", running) + " is running."; Build(); return; }
            try
            {
                DbcOutputResult serverRes = null;
                if (staged.Count > 0)
                {
                    busy = "Updating the server's DBC copy (backed up first)…"; Build();
                    if (server == null) throw new InvalidOperationException("The server's dbc folder wasn't found (worldserver.conf DataDir).");
                    serverRes = await Task.Run(() => DbcOutputs.ToServerCopy(staged, server, backupRoot));
                    S.Changes.Pending.Log.Add(new ApplyRecord { Utc = DateTime.UtcNow, Target = "DBC → server copy", Database = "dbc", BackupDir = serverRes.BackupDir, Success = serverRes.Success, Message = string.Join(" ", serverRes.Success ? serverRes.Log : serverRes.Problems), Kind = "DBC → server copy" });
                    S.Changes.SavePending();
                    if (!serverRes.Success) throw new InvalidOperationException("The server copy wasn't updated: " + string.Join(" ", serverRes.Problems.Take(3)));
                }
                PatchInstaller.Record rec;
                if (plan.InPlace)
                {
                    busy = $"Putting your files into {name} (backed up first)…"; Build();
                    rec = await Task.Run(() =>
                    {
                        foreach (var (path, data) in files.Where(x => x.Key.StartsWith("DBFilesClient\\", StringComparison.OrdinalIgnoreCase)))
                        {
                            var p = Path.Combine(work, path); Directory.CreateDirectory(Path.GetDirectoryName(p));
                            File.WriteAllBytes(p, data);
                        }
                        return Installer.UpdateInPlace(client, dir == null ? name : Path.Combine(dir, name), files);
                    });
                }
                else
                {
                busy = $"Building {name}…"; Build();
                var problems = await Task.Run(() =>
                {
                    // the work folder keeps the full current state, so later sends start from it
                    foreach (var (path, data) in files.Where(x => x.Key.StartsWith("DBFilesClient\\", StringComparison.OrdinalIgnoreCase)))
                    {
                        var p = Path.Combine(work, path); Directory.CreateDirectory(Path.GetDirectoryName(p));
                        File.WriteAllBytes(p, data);
                    }
                    Directory.CreateDirectory(outDir);
                    return MpqWriter.BuildVerified(built, files);
                });
                if (problems.Count > 0) throw new InvalidOperationException("The patch didn't pass its check: " + string.Join(" ", problems.Take(3)));
                busy = $"Installing {name}…"; Build();
                rec = await Task.Run(() => Installer.Install(built, client, name, dir));
                }
                if (staged.Count > 0)
                {
                    S.Changes.MarkDbcSent(staged, true, serverRes?.BackupDir);
                    S.Changes.MarkDbcSent(staged.Where(c => S.Changes.Pending.Changes.Contains(c)).ToList(), false, work);
                }
                S.Changes.Pending.Log.Add(new ApplyRecord { Utc = DateTime.UtcNow, Target = "Send to game", Database = "dbc", BackupDir = rec.BackupOfPrevious, Success = true, Message = $"Installed {rec.Name} ({files.Count} file(s)).", Kind = "Send to game" });
                S.Changes.SavePending();
                S.RaiseChanges();
                Installed = true;
                result = $"Installed {rec.Name} in {rec.DataDir}." + (rec.BackupOfPrevious != null ? " The previous file is backed up at " + rec.BackupOfPrevious + "." : "") +
                         (staged.Count > 0 ? " Restart worldserver so it loads the new server DBCs." : "") + " Delete WoW's Cache folder and start the game. " + LearnHint();
                w.Toast("Sent to the game: " + rec.Name, "ok");
            }
            catch (Exception ex)
            {
                var target = Path.Combine(client ?? "", "Data", dir ?? "", name ?? "");
                result = (ex is UnauthorizedAccessException || ex is IOException ? FileLocks.Explain(target, ex) : Friendly.Text(ex)) + " Nothing in the game folder was changed by this step.";
            }
            busy = null; confirm = "";
            MakePlan();
            Build();
        }
    }
}
