using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AndersCraftStudio.Core;

namespace AndersCraftStudio
{
    /// <summary>Loot workspace: every *_loot_template table as a grouped, explained editor with a drop simulator.</summary>
    public sealed class LootPage : IPage
    {
        readonly MainWindow w;
        Studio S => w.S;
        readonly Refs refs;
        readonly Grid view = new Grid();
        public UIElement View => view;

        sealed class LootTable { public string Table, Label, What, OwnerSql, NameSearchSql; }
        static readonly LootTable[] Tables =
        {
            new LootTable { Table = "creature_loot_template", Label = "Creature drops", What = "What creatures drop when killed. The entry is creature_template.lootid.",
                OwnerSql = "SELECT entry, name FROM creature_template WHERE lootid = {0} LIMIT 20", NameSearchSql = "SELECT lootid, CONCAT(name, ' (creature ', entry, ')') FROM creature_template WHERE lootid <> 0 AND {0} LIMIT 100" },
            new LootTable { Table = "gameobject_loot_template", Label = "Chests & nodes", What = "What chests, herbs, ore veins and other objects contain. The entry is the object's loot ID (Data1 for chests).",
                OwnerSql = "SELECT entry, name FROM gameobject_template WHERE type IN (3, 25) AND Data1 = {0} LIMIT 20", NameSearchSql = "SELECT Data1, CONCAT(name, ' (object ', entry, ')') FROM gameobject_template WHERE type IN (3, 25) AND Data1 <> 0 AND {0} LIMIT 100" },
            new LootTable { Table = "item_loot_template", Label = "Openable items", What = "What you get from opening an item (bags of loot, clams, lockboxes). The entry is the item's own ID.",
                OwnerSql = "SELECT entry, name FROM item_template WHERE entry = {0}", NameSearchSql = "SELECT entry, name FROM item_template WHERE (Flags & 4) <> 0 AND {0} LIMIT 100" },
            new LootTable { Table = "reference_loot_template", Label = "Shared lists (references)", What = "Shared loot lists other tables point to with their Reference column, e.g. a zone's world drops.",
                OwnerSql = "SELECT Entry, 'creature drop list' FROM creature_loot_template WHERE Reference = {0} UNION ALL SELECT Entry, 'reference list' FROM reference_loot_template WHERE Reference = {0} LIMIT 20" },
            new LootTable { Table = "pickpocketing_loot_template", Label = "Pickpocketing", What = "What rogues can steal. The entry is creature_template.pickpocketloot.",
                OwnerSql = "SELECT entry, name FROM creature_template WHERE pickpocketloot = {0} LIMIT 20", NameSearchSql = "SELECT pickpocketloot, CONCAT(name, ' (creature ', entry, ')') FROM creature_template WHERE pickpocketloot <> 0 AND {0} LIMIT 100" },
            new LootTable { Table = "skinning_loot_template", Label = "Skinning", What = "What skinning (or mining/herbing a creature) gives. The entry is creature_template.skinloot.",
                OwnerSql = "SELECT entry, name FROM creature_template WHERE skinloot = {0} LIMIT 20", NameSearchSql = "SELECT skinloot, CONCAT(name, ' (creature ', entry, ')') FROM creature_template WHERE skinloot <> 0 AND {0} LIMIT 100" },
            new LootTable { Table = "disenchant_loot_template", Label = "Disenchanting", What = "What disenchanting gives. The entry is item_template.DisenchantID.",
                OwnerSql = "SELECT entry, name FROM item_template WHERE DisenchantID = {0} LIMIT 20", NameSearchSql = "SELECT DisenchantID, CONCAT(name, ' (item ', entry, ')') FROM item_template WHERE DisenchantID <> 0 AND {0} LIMIT 100" },
            new LootTable { Table = "prospecting_loot_template", Label = "Prospecting", What = "What prospecting an ore gives. The entry is the ore's item ID.", OwnerSql = "SELECT entry, name FROM item_template WHERE entry = {0}", NameSearchSql = "SELECT entry, name FROM item_template WHERE {0} LIMIT 100" },
            new LootTable { Table = "milling_loot_template", Label = "Milling", What = "What milling a herb gives. The entry is the herb's item ID.", OwnerSql = "SELECT entry, name FROM item_template WHERE entry = {0}", NameSearchSql = "SELECT entry, name FROM item_template WHERE {0} LIMIT 100" },
            new LootTable { Table = "fishing_loot_template", Label = "Fishing", What = "What can be fished up. The entry is the zone or area ID." },
            new LootTable { Table = "mail_loot_template", Label = "Mail attachments", What = "Items attached to mails the game sends (mail templates)." },
            new LootTable { Table = "spell_loot_template", Label = "Spell loot", What = "Items created by spells that roll loot. The entry is the spell ID." },
        };

        // state
        LootTable cur = Tables[0];
        long entry;
        readonly Stack<(string table, long entry)> back = new Stack<(string, long)>();
        RowListEditor editor;
        readonly ListBox tableList = new ListBox();
        readonly TextBox search = U.Box();
        readonly ListBox results = new ListBox();
        readonly ContentControl main = new ContentControl { Focusable = false };
        readonly StackPanel groupsPanel = new StackPanel();
        readonly ContentControl simHost = new ContentControl { Focusable = false };
        int kills = 10000; bool hasQuests = true;
        List<LootSim.ItemStat> lastSim;

        public LootPage(MainWindow w)
        {
            this.w = w; refs = new Refs(w.S);
            view.Margin = new Thickness(28, 22, 28, 20);
            view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            view.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
            var strip = U.StepStrip(1); DockPanel.SetDock(strip, Dock.Right); strip.VerticalAlignment = VerticalAlignment.Center; head.Children.Add(strip);
            head.Children.Add(U.Stack(U.H1("Loot"), U.T("Who drops what, how often, in which groups and shared lists, and what a typical kill really gives. Edits are staged with undo.", 13.5, "Muted")));
            view.Children.Add(U.Hero(head));

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(g, 1); view.Children.Add(g);

            var left = new DockPanel();
            foreach (var t in Tables) tableList.Items.Add(new ListBoxItem { Content = U.Stack(U.T(t.Label, 13, "Text", FontWeights.SemiBold, false), U.T(t.Table, 11, "Faint", null, false)), Tag = t, ToolTip = t.What });
            tableList.SelectedIndex = 0;
            tableList.SelectionChanged += (s, e) => { if (tableList.SelectedItem is ListBoxItem li && li.Tag is LootTable t && t != cur) { cur = t; back.Clear(); RunSearch(); } };
            var top = U.Stack(U.Eyebrow("Loot table"), tableList);
            top.Margin = new Thickness(0, 0, 0, 10);
            DockPanel.SetDock(top, Dock.Top); left.Children.Add(top);
            search.ToolTip = "A loot entry number, or part of the owner's name (creature, object or item).";
            search.KeyDown += (s, e) => { if (e.Key == Key.Enter) RunSearch(); };
            var sb = U.WithPlaceholder(search, "Entry or owner name (Enter)"); sb.Margin = new Thickness(0, 0, 0, 8);
            DockPanel.SetDock(sb, Dock.Top); left.Children.Add(sb);
            results.SelectionChanged += (s, e) => { if (results.SelectedItem is ListBoxItem li && li.Tag is long en && en != entry) { back.Clear(); Open(cur.Table, en); } };
            left.Children.Add(results);
            g.Children.Add(U.Card(left, new Thickness(10, 12, 6, 8), new Thickness(0)));

            Grid.SetColumn(main, 2); g.Children.Add(main);
            main.Content = U.Card(U.Stack(U.H2("Pick a loot table and an entry"), U.T("Search on the left, or open loot from a creature's Loot tab.", 13.5, "Muted")), new Thickness(28), new Thickness(0));
        }

        public void OnShow()
        {
            if (!S.HasReadAccount) { main.Content = U.Callout("Finish setup step 3 (the read-only account) to use the Loot editor.", "warn"); return; }
            if (results.Items.Count == 0) RunSearch();
            // after an Apply the list shown may be out of date: reload it unless it has unstaged edits
            if (seenVersion != S.DataVersion) { seenVersion = S.DataVersion; if (editor != null && cur != null && !editor.Dirty) Open(cur.Table, entry); }
        }
        int seenVersion;

        void RunSearch()
        {
            results.Items.Clear();
            var q = search.Text.Trim();
            try
            {
                using var db = S.OpenRead(S.Profile.WorldDb);
                if (q.Length > 0 && !long.TryParse(q, out _) && cur.NameSearchSql != null)
                {
                    var t = db.Query(string.Format(cur.NameSearchSql, "name LIKE " + Refs.LikeLiteral(q)), 100);
                    foreach (var r in t.Rows) AddResult(Convert.ToInt64(r[0]), Convert.ToString(r[1]));
                }
                else
                {
                    var where = long.TryParse(q, out var n) ? $"WHERE Entry = {n}" : "";
                    var t = db.Query($"SELECT Entry, COUNT(*) FROM {SqlGen.Ident(cur.Table)} {where} GROUP BY Entry ORDER BY Entry LIMIT 300", 300);
                    foreach (var r in t.Rows) AddResult(Convert.ToInt64(r[0]), $"{r[1]} row(s)");
                }
            }
            catch (Exception ex) { results.Items.Add(new ListBoxItem { Content = U.T(Friendly.Text(ex), 12, "Bad"), IsEnabled = false }); }
        }

        void AddResult(long en, string what) => results.Items.Add(new ListBoxItem { Content = U.Stack(U.T("#" + en, 13, "Text", FontWeights.SemiBold, false), U.T(what, 11.5, "Muted", null, false)), Tag = en });

        public void Open(string table, long en)
        {
            if (editor != null && editor.Dirty && !w.IsScreens &&
                ChoiceWindow.Ask(w, "Unstaged loot edits", "This loot list has edits you haven't staged. Stage them first?", "Stage them", "Discard them", "Stay") is int r && r != 1) { if (r == 0) editor.Stage(); else return; }
            cur = Tables.First(t => t.Table == table);
            tableList.SelectedItem = tableList.Items.Cast<ListBoxItem>().First(li => li.Tag == cur);
            entry = en; lastSim = null;
            editor = new RowListEditor(w, "Rows", "Edit cells directly. Item = what drops; Reference = a shared list (then Item is just a label); Chance in %; GroupId 0 = rolled alone; MinCount/MaxCount = how many; QuestRequired 1 = only for players on a quest that needs it; LootMode 1 = normal mode.",
                table, $"`Entry` = {en}", new Dictionary<string, string> { ["Entry"] = en.ToString(CultureInfo.InvariantCulture) }, "GroupId, Chance DESC, Item",
                new[] { new RowListEditor.NameColumn { IdColumn = "Item", Kind = "item", Header = "item name" } }, refs.ByKind);
            editor.NewRow = r => r.Set("Chance", "100").Set("LootMode", "1").Set("MinCount", "1").Set("MaxCount", "1");
            var openRef = U.Btn("Open selected reference", () =>
            {
                var sel = editor.Grid.CurrentCell.Item as DataRowView;
                if (sel == null || !long.TryParse(Convert.ToString(sel.Row["Reference"]), out var rf) || rf == 0) { w.Toast("Click a row whose Reference isn't 0.", "warn"); return; }
                back.Push((cur.Table, entry)); Open("reference_loot_template", Math.Abs(rf));
            }, "BtnGhost", "Drill into the shared list this row points to.");
            editor.ExtraButtons.Add(openRef);
            editor.Changed = _ => { RefreshGroups(); };
            editor.Load();
            Build();
        }

        void Build()
        {
            if (groupsPanel.Parent is Panel p1) p1.Children.Remove(groupsPanel);
            if (simHost.Parent is Panel p2) p2.Children.Remove(simHost);
            var sp = new StackPanel();
            // header
            var owners = new List<string>();
            if (cur.OwnerSql != null)
                try { using var db = S.OpenRead(S.Profile.WorldDb); foreach (var r in db.Query(string.Format(cur.OwnerSql, entry), 20).Rows) owners.Add($"{r[1]} ({r[0]})"); } catch { }
            if (cur.Table == "fishing_loot_template") owners.Add(S.Dbc.Name("area", entry) ?? "area " + entry);
            if (cur.Table == "spell_loot_template") owners.Add(S.Dbc.Name("spell", entry) ?? "spell " + entry);
            var crumbs = U.Row();
            foreach (var (t, e) in back.Reverse())
            {
                var tt = t; var ee = e;
                var b = U.Btn($"‹ {Tables.First(x => x.Table == tt).Label} #{ee}", () => { while (back.Count > 0 && back.Peek() != (tt, ee)) back.Pop(); if (back.Count > 0) back.Pop(); Open(tt, ee); }, "BtnGhost", "Back");
                crumbs.Children.Add(b);
            }
            var head = U.Stack(crumbs, U.Row(U.T(cur.Label, 22, "Text", FontWeights.SemiBold, false), new Border { Width = 12 }, U.Pill("#" + entry, "AmberDim", "Amber"), U.Pill(cur.Table, "Panel2", "Muted")),
                U.T(cur.What, 13, "Muted", null, true, new Thickness(0, 4, 0, 0)),
                U.T(owners.Count == 0 ? "Not used by anything Studio can find." : "Used by: " + string.Join(", ", owners), 13, owners.Count == 0 ? "Faint" : "Text", null, true, new Thickness(0, 6, 0, 0)));
            sp.Children.Add(U.Card(head, new Thickness(20, 14, 20, 14), new Thickness(0, 0, 0, 12)));

            // groups + explanation side by side
            var two = new Grid();
            two.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            two.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            two.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
            two.Children.Add(groupsPanel);
            var explain = U.Card(U.Stack(U.Eyebrow("How loot works"),
                U.T("Chance", 13.5, "Text", FontWeights.SemiBold), U.T("Percent chance (0-100). In a group, 0 means \"share whatever chance is left, evenly\".", 12.5, "Muted", null, true, new Thickness(0, 0, 0, 8)),
                U.T("GroupId", 13.5, "Text", FontWeights.SemiBold), U.T("0 = rolled on its own (several can drop). Same number above 0 = a group: at most one row of it drops per kill.", 12.5, "Muted", null, true, new Thickness(0, 0, 0, 8)),
                U.T("MinCount / MaxCount", 13.5, "Text", FontWeights.SemiBold), U.T("How many copies drop. For a Reference row, MaxCount is how many times the shared list is rolled.", 12.5, "Muted", null, true, new Thickness(0, 0, 0, 8)),
                U.T("Reference", 13.5, "Text", FontWeights.SemiBold), U.T("Points to reference_loot_template. If its chance hits, that whole list is rolled.", 12.5, "Muted", null, true, new Thickness(0, 0, 0, 8)),
                U.T("QuestRequired", 13.5, "Text", FontWeights.SemiBold), U.T("1 = only drops for players on a quest that needs the item.", 12.5, "Muted", null, true, new Thickness(0, 0, 0, 0))), new Thickness(18, 14, 18, 14), new Thickness(0, 0, 0, 12));
            Grid.SetColumn(explain, 2); two.Children.Add(explain);
            sp.Children.Add(two);
            RefreshGroups();

            if (editor.View is FrameworkElement fe && fe.Parent is Border old) old.Child = null;
            sp.Children.Add(U.Card(editor.View, new Thickness(18, 14, 18, 14), new Thickness(0, 0, 0, 12)));
            sp.Children.Add(simHost);
            BuildSim();
            main.Content = U.Scroll(sp);
        }

        List<LootRow> RowsFromEditor()
        {
            var res = new List<LootRow>();
            try
            {
                foreach (var r in editor.CurrentRows()) res.Add(ToLoot(n => r.Disp(n)));
            }
            catch { }
            return res;
        }

        static LootRow ToLoot(Func<string, string> get)
        {
            int I(string c) => int.TryParse(get(c), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
            float.TryParse(get("Chance"), NumberStyles.Float, CultureInfo.InvariantCulture, out var ch);
            return new LootRow { Item = I("Item"), Reference = I("Reference"), Chance = ch, QuestRequired = I("QuestRequired") != 0, LootMode = get("LootMode") == null ? 1 : I("LootMode"), GroupId = I("GroupId"), MinCount = I("MinCount"), MaxCount = I("MaxCount") };
        }

        void RefreshGroups()
        {
            groupsPanel.Children.Clear();
            var rows = RowsFromEditor();
            if (rows.Count == 0) { groupsPanel.Children.Add(U.Card(U.T("No rows yet. Add one below.", 13, "Muted"), new Thickness(18, 14, 18, 14), new Thickness(0, 0, 0, 12))); return; }
            foreach (var g in rows.GroupBy(r => r.GroupId).OrderBy(g => g.Key))
            {
                var list = g.ToList();
                var sp = new StackPanel();
                sp.Children.Add(U.Row(U.T(g.Key == 0 ? "Rolled on their own" : $"Group {g.Key}", 15, "Text", FontWeights.SemiBold, false), new Border { Width = 10 }, U.Pill($"{list.Count} row(s)", "Panel2", "Muted")));
                sp.Children.Add(U.T(LootSim.DescribeGroup(list), 12.5, "Muted", null, true, new Thickness(0, 4, 0, 8)));
                foreach (var r in list.OrderByDescending(r => r.Chance))
                {
                    string name = r.Reference != 0 ? $"Shared list #{Math.Abs(r.Reference)} (rolled {Math.Max(1, r.MaxCount)}x)" : refs.ByKind(SP.Item, r.Item) ?? "#" + r.Item;
                    string chance = r.Chance == 0 && g.Key != 0 ? "equal share" : $"{r.Chance:0.###}%";
                    string count = r.Reference == 0 && (r.MinCount != 1 || r.MaxCount != 1) ? $" · {r.MinCount}-{r.MaxCount}" : "";
                    var line = U.Row(new Border { Width = 70, Child = U.T(chance, 12.5, "Amber", FontWeights.SemiBold, false) }, U.T(name + count + (r.QuestRequired ? " · quest only" : ""), 12.5, "Text", null, false));
                    if (r.Reference != 0)
                    {
                        var rf = Math.Abs(r.Reference);
                        var go = U.Btn("Open ›", () => { back.Push((cur.Table, entry)); Open("reference_loot_template", rf); }, "BtnGhost", "Drill into this shared list.");
                        go.Padding = new Thickness(8, 2, 8, 2); line.Children.Add(go);
                    }
                    line.Margin = new Thickness(0, 0, 0, 3);
                    sp.Children.Add(line);
                }
                groupsPanel.Children.Add(U.Card(sp, new Thickness(18, 12, 18, 12), new Thickness(0, 0, 0, 10)));
            }
        }

        void BuildSim()
        {
            var sp = new StackPanel();
            sp.Children.Add(U.Row(U.H2("Drop simulator"), new Border { Width = 10 }, U.Pill("uses your unstaged edits", "AmberDim", "Amber")));
            sp.Children.Add(U.T("Rolls this loot many times with the server's rules (groups, shared lists, counts) at normal drop rates, and shows what a kill gives on average.", 12.5, "Muted", null, true, new Thickness(0, 0, 0, 10)));
            var n = U.Box(kills.ToString(CultureInfo.InvariantCulture), true); n.Width = 120;
            n.LostFocus += (s, e) => { if (int.TryParse(n.Text, out var v) && v > 0 && v <= 1_000_000) kills = v; else n.Text = kills.ToString(); };
            var q = new CheckBox { IsChecked = hasQuests, Content = U.T("Player is on the quests (quest-only items can drop)", 13, "Text", null, false), Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            q.Checked += (s, e) => hasQuests = true; q.Unchecked += (s, e) => hasQuests = false;
            var run = U.Btn("Roll " + "kills", RunSim, "BtnPrimary", "Run the simulation.");
            run.Content = "Roll";
            var f = U.Field("Kills", null, n, "How many kills to simulate (up to 1,000,000).", 140); f.Margin = new Thickness(0, 0, 12, 0);
            var row = U.Row(f, run, q); run.VerticalAlignment = VerticalAlignment.Bottom;
            sp.Children.Add(row);
            if (lastSim != null)
            {
                var dt = new DataTable();
                dt.Columns.Add("Item"); dt.Columns.Add("Name"); dt.Columns.Add("Per kill"); dt.Columns.Add("% of kills"); dt.Columns.Add("Avg when dropped");
                foreach (var s in lastSim) dt.Rows.Add(s.Item, refs.ByKind(SP.Item, s.Item) ?? "?", s.PerKill.ToString("0.000", CultureInfo.InvariantCulture), s.DropPct.ToString("0.0", CultureInfo.InvariantCulture) + "%", s.AvgWhenDropped.ToString("0.0", CultureInfo.InvariantCulture));
                var g = new DataGrid { IsReadOnly = true, ItemsSource = dt.DefaultView, MaxHeight = 360, Margin = new Thickness(0, 12, 0, 0) };
                g.AutoGeneratingColumn += (s, e) => e.Column.Header = new TextBlock { Text = e.PropertyName };
                sp.Children.Add(g);
                sp.Children.Add(U.T(lastSim.Count == 0 ? "Nothing dropped in any kill." : $"{lastSim.Sum(x => x.PerKill):0.00} items per kill on average, over {kills:N0} kills.", 12.5, "Muted", null, true, new Thickness(0, 8, 0, 0)));
            }
            simHost.Content = U.Card(sp, new Thickness(18, 14, 18, 14), new Thickness(0));
        }

        public void RunSim()
        {
            var root = RowsFromEditor();
            var dbCache = new Dictionary<(string, long), List<LootRow>>();
            List<LootRow> Load(string table, int en)
            {
                if (table == cur.Table && en == entry) return root;
                try
                {
                    using var db = S.OpenRead(S.Profile.WorldDb);
                    var t = db.Query($"SELECT * FROM {SqlGen.Ident(table)} WHERE Entry = {en}", 5000);
                    return t.Rows.Select(r => ToLoot(c => { int i = t.ColumnIndex(c); return i < 0 ? null : SqlGen.Display(r[i]); })).ToList();
                }
                catch { return new List<LootRow>(); }
            }
            var sim = new LootSim(Load, Environment.TickCount) { HasQuests = hasQuests };
            lastSim = sim.Run(cur.Table, (int)entry, kills);
            if (sim.Warnings.Count > 0) w.Toast(string.Join(" ", sim.Warnings), "warn");
            BuildSim();
        }

        public RowListEditor Editor => editor;
        public void ScreensScrollToEnd() { if (main.Content is ScrollViewer sv) { sv.UpdateLayout(); sv.ScrollToEnd(); } }
        public void ScreensBackTo(string table, long en) { back.Push((table, en)); }
    }
}
