using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AndersCraftStudio.Core;

namespace AndersCraftStudio
{
    public static class AppInfo
    {
        public static string Version => (Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 6, 0)).ToString(3);
        public const string License = @"MIT License

Copyright (c) 2026 AndersCraft Studio contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the ""Software""), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.";
    }

    /// <summary>A Guide card in the shared style: eyebrow, title, text, optional tip.</summary>
    public static class GuideCard
    {
        public static Border Make(string title, string text, string tip = null)
        {
            var sp = U.Stack(U.Eyebrow("Guide"), U.T(title, 15, "Text", FontWeights.SemiBold, true, new Thickness(0, 0, 0, 6)), U.T(text, 13, "Muted", null, true));
            if (tip != null) sp.Children.Add(new Border { Margin = new Thickness(0, 10, 0, 0), Child = U.Callout(tip, "info") });
            return U.Card(sp, new Thickness(16, 14, 16, 14), new Thickness(0));
        }
    }

    // =====================================================================================================
    /// <summary>Landing screen: server summary, staged/applied changes, quick links and a start-here card.</summary>
    public sealed class HomePage : IPage
    {
        readonly MainWindow w;
        Studio S => w.S;
        readonly ContentControl host = new ContentControl { Focusable = false };
        public UIElement View => host;
        public HomePage(MainWindow w) { this.w = w; }

        public void OnShow() => Build();

        void Build()
        {
            var p = S.Profile;
            var root = new StackPanel { Margin = new Thickness(28, 22, 28, 20) };
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
            var crest = U.Crest(64); crest.Margin = new Thickness(0, 0, 18, 0); DockPanel.SetDock(crest, Dock.Left); head.Children.Add(crest);
            head.Children.Add(U.Stack(U.H1("Welcome to AndersCraft Studio"), U.T(p == null ? "Set up a server to get started." : $"Server profile \"{p.Name}\" · nothing reaches your database until you review and apply it.", 14, "Muted")));
            root.Children.Add(head);

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(g);
            var left = new StackPanel(); var right = new StackPanel();
            Grid.SetColumn(right, 2); g.Children.Add(left); g.Children.Add(right);

            // start here
            var start = new StackPanel();
            start.Children.Add(U.Eyebrow("New to this? Start here"));
            start.Children.Add(U.H2("Four steps, always the same"));
            start.Children.Add(U.T("1  Edit something (a creature, a loot table, a spell…).\n2  Stage it: it goes into your changeset, not the database.\n3  Review it on Changes: before, after, and how to undo it.\n4  Apply it to the practice sandbox (and later Live), with a backup first.", 13.5, "Text", null, true, new Thickness(0, 0, 0, 12)));
            start.Children.Add(U.Row(U.Btn("Getting started guide", () => w.Navigate("help"), "BtnPrimary"), U.Btn("Try a template", () => w.Navigate("templates"))));
            left.Children.Add(U.Card(start, new Thickness(20, 16, 20, 16)));

            // server summary
            var srv = new StackPanel();
            srv.Children.Add(U.H2("Your server"));
            if (p == null || !p.SetupComplete)
            {
                srv.Children.Add(U.Callout("No server is set up yet. The setup wizard finds worldserver.conf, helps you make two safe MySQL accounts and a practice sandbox. It takes about five minutes.", "warn", "Not set up yet"));
                srv.Children.Add(U.Btn("Run setup", () => w.ShowWizard(1, true), "BtnPrimary"));
            }
            else
            {
                string who = null, ver = null;
                if (S.HasReadAccount)
                    try { using var db = S.OpenRead(p.WorldDb); var t = db.Query("SELECT CURRENT_USER(), VERSION()", 1); who = Convert.ToString(t.Rows[0][0]); ver = Convert.ToString(t.Rows[0][1]); } catch (Exception ex) { who = "can't connect: " + Friendly.Text(ex); }
                srv.Children.Add(U.CheckRow(U.Check.Info, "Profile", p.Name, null, 150));
                srv.Children.Add(U.CheckRow(File.Exists(p.ConfPath) ? U.Check.Ok : U.Check.Warn, "worldserver.conf", p.ConfPath ?? "(not set)", null, 150));
                srv.Children.Add(U.CheckRow(U.Check.Info, "MySQL", $"{p.Host}:{p.Port}" + (ver != null ? $" · version {ver}" : ""), null, 150));
                srv.Children.Add(U.CheckRow(S.HasReadAccount && ver != null ? U.Check.Ok : U.Check.Warn, "Browsing as", who ?? "read-only account not set up", "Studio reads with its read-only account in READ ONLY sessions.", 150));
                srv.Children.Add(U.CheckRow(U.Check.Info, "World database", p.WorldDb, null, 150));
                srv.Children.Add(U.CheckRow(p.SandboxExists ? U.Check.Ok : U.Check.Warn, "Sandbox", p.SandboxExists ? p.SandboxDb : "not created yet (setup step 5)", null, 150));
                srv.Children.Add(U.CheckRow(S.Profiles.Settings.AllowLive ? U.Check.Warn : U.Check.Ok, "Live database", S.Profiles.Settings.AllowLive ? "unlocked in Settings" : "locked (Apply goes to the sandbox)", null, 150));
                string dbc = null; try { var d = ConfParser.ParseFile(p.ConfPath).DataDir; if (!string.IsNullOrEmpty(d)) dbc = Path.Combine(d, "dbc"); } catch { }
                srv.Children.Add(U.CheckRow(dbc != null && Directory.Exists(dbc) ? U.Check.Ok : U.Check.Info, "Server DBC folder", dbc ?? "(DataDir not set in worldserver.conf)", null, 150));
                srv.Children.Add(U.CheckRow(!string.IsNullOrEmpty(p.ClientDir) && Directory.Exists(p.ClientDir) ? U.Check.Ok : U.Check.Info, "Game client", string.IsNullOrEmpty(p.ClientDir) ? "not set (optional; needed for icons and client patches)" : p.ClientDir, null, 150));
                srv.Children.Add(U.CheckRow(U.Check.Info, "Studio folder", S.Profiles.Settings.Workspace(), "Changesets, patch work folders and built patches live here.", 150));
                var b = U.Row(U.Btn("Settings", () => w.Navigate("settings")), U.Btn("Run setup again", () => w.ShowWizard(1, true), "BtnGhost")); b.Margin = new Thickness(0, 12, 0, 0);
                srv.Children.Add(b);
            }
            left.Children.Add(U.Card(srv, new Thickness(20, 16, 20, 16)));

            // changes
            var ch = new StackPanel();
            ch.Children.Add(U.H2("Changes"));
            var pending = S.Changes.Pending.Changes;
            if (pending.Count == 0) ch.Children.Add(U.T("Nothing staged. Edits you stage anywhere in Studio collect here first.", 13, "Muted"));
            else
            {
                foreach (var grp in pending.GroupBy(c => c.Category ?? "Database").OrderByDescending(x => x.Count()))
                    ch.Children.Add(U.CheckRow(U.Check.Pending, grp.Key, $"{grp.Count()} staged", null, 170));
                var rv = U.Btn($"Review {pending.Count} staged change(s)", () => w.Navigate("changes"), "BtnAmber"); rv.Margin = new Thickness(0, 10, 0, 0); rv.HorizontalAlignment = HorizontalAlignment.Left;
                ch.Children.Add(rv);
            }
            var hist = S.Changes.History().Take(5).ToList();
            ch.Children.Add(U.T("Recent applies", 13.5, "Text", FontWeights.SemiBold, false, new Thickness(0, 16, 0, 6)));
            if (hist.Count == 0) ch.Children.Add(U.T("None yet. Each Apply is recorded with its backup and can be rolled back.", 12.5, "Muted"));
            foreach (var h in hist) ch.Children.Add(U.CheckRow(h.State == ChangesetState.RolledBack ? U.Check.Info : U.Check.Ok, h.CreatedUtc.ToLocalTime().ToString("MMM d, HH:mm"), $"{h.Changes.Count} change(s) → {h.AppliedTarget}" + (h.State == ChangesetState.RolledBack ? " (rolled back)" : ""), null, 130));
            right.Children.Add(U.Card(ch, new Thickness(20, 16, 20, 16)));

            // quick links
            var links = new WrapPanel();
            foreach (var (key, text, tip) in new[]
            {
                ("creatures", "Creatures", "NPCs and monsters"), ("items", "Items", "Gear and everything in bags"), ("quests", "Quests", "Text, objectives, rewards"),
                ("gameobjects", "Game objects", "Chests, doors, herbs, signs"), ("npctext", "NPC text", "Gossip greetings"), ("pagetext", "Page text", "Books and letters"),
                ("admin", "Admin console", "Direct SQL, users, import/export, server setup"),
                ("loot", "Loot", "Drops and the simulator"), ("smartai", "SmartAI", "Behaviour as sentences"), ("world", "World tables", "Conditions, waypoints, gossip, events, area triggers"),
                ("templates", "Templates", "Ready-made actions"), ("spells", "Spells", "Spell.dbc with a live tooltip"), ("dbc", "DBC tables", "All 246 client tables"),
                ("assets", "Asset import", "Icons, models, sounds"), ("patches", "Client patches", "Build and install MPQs"), ("sql", "SQL browser", "Every table, read-only queries"),
            })
            {
                var k = key;
                var b = new Button { Width = 176, Height = 74, Margin = new Thickness(0, 0, 10, 10), HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(14, 8, 10, 8), ToolTip = tip,
                    Content = U.Stack(U.T(text, 14, "Text", FontWeights.SemiBold, false), U.T(tip, 11.5, "Muted", null, true)) };
                b.Click += (s, e) => w.Navigate(k);
                links.Children.Add(b);
            }
            right.Children.Add(U.Card(U.Stack(U.H2("Jump to"), links), new Thickness(20, 16, 10, 6)));
            host.Content = U.Scroll(root);
        }
    }

    // =====================================================================================================
    /// <summary>Friendly placeholder for a workspace that needs setup first.</summary>
    public sealed class NotSetUpPage : IPage
    {
        public UIElement View { get; }
        public void OnShow() { }
        public NotSetUpPage(MainWindow w, string title, string why)
        {
            var sp = new StackPanel { MaxWidth = 680, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
            sp.Children.Add(new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 34, Foreground = U.R("Amber"), Margin = new Thickness(0, 0, 0, 16) });
            sp.Children.Add(U.Row(U.H1(title), new Border { Width = 12 }, U.Pill("not set up yet", "AmberDim", "Amber")));
            sp.Children.Add(U.T(why, 15, "Muted", null, true, new Thickness(0, 8, 0, 20)));
            sp.Children.Add(U.Callout("Studio only reads your database with its own read-only account, and only changes it through Changes › Apply with a backup first. Setting those accounts up takes one script you run yourself as MySQL root.", "info", "Why setup comes first"));
            sp.Children.Add(U.Row(U.Btn("Open setup", () => w.ShowWizard(1, true), "BtnPrimary"), U.Btn("Accounts (step 3)", () => w.ShowWizard(3, true)), U.Btn("Getting started guide", () => w.Navigate("help"), "BtnGhost")));
            View = new Border { Padding = new Thickness(40, 30, 40, 30), Child = U.Scroll(sp) };
        }
    }

    // =====================================================================================================
    /// <summary>Templates: ready-made actions that stage a reviewable changeset.</summary>
    public sealed class TemplatesPage : IPage
    {
        readonly MainWindow w;
        Studio S => w.S;
        readonly Grid view = new Grid();
        public UIElement View => view;
        readonly ListBox list = new ListBox();
        readonly ContentControl form = new ContentControl { Focusable = false };
        readonly ContentControl guide = new ContentControl { Focusable = false };
        readonly ContentControl result = new ContentControl { Focusable = false };
        readonly Dictionary<string, TextBox> boxes = new Dictionary<string, TextBox>();
        readonly Dictionary<string, ComboBox> combos = new Dictionary<string, ComboBox>();
        readonly Refs refs;
        StudioTemplate cur;

        public TemplatesPage(MainWindow w)
        {
            this.w = w; refs = new Refs(w.S);
            view.Margin = new Thickness(28, 22, 28, 20);
            view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            view.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
            var strip = U.StepStrip(1); DockPanel.SetDock(strip, Dock.Right); strip.VerticalAlignment = VerticalAlignment.Center; head.Children.Add(strip);
            head.Children.Add(U.Stack(U.H1("Templates"), U.T("Ready-made actions. Fill in a few boxes, preview exactly what will be staged, then stage it and review it on Changes like any other edit.", 13.5, "Muted")));
            view.Children.Add(head);
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
            Grid.SetRow(g, 1); view.Children.Add(g);
            foreach (var t in Templates.All)
                list.Items.Add(new ListBoxItem { Tag = t, Content = U.Stack(U.T(t.Title, 13.5, "Text", FontWeights.SemiBold, true), U.T(t.What, 12, "Muted", null, true)), ToolTip = t.Explain });
            list.SelectionChanged += (s, e) => { if (list.SelectedItem is ListBoxItem li && li.Tag is StudioTemplate t) Open(t); };
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            g.Children.Add(U.Card(U.Stack(U.Eyebrow("Templates"), list), new Thickness(10, 12, 6, 8), new Thickness(0)));
            var mid = U.Stack(form, result);
            var ms = U.Scroll(mid); Grid.SetColumn(ms, 2); g.Children.Add(ms);
            Grid.SetColumn(guide, 4); g.Children.Add(guide);
            list.SelectedIndex = 0;
        }

        public void OnShow() { }

        void Open(StudioTemplate t)
        {
            cur = t; boxes.Clear(); combos.Clear(); result.Content = null;
            var sp = new StackPanel();
            sp.Children.Add(U.H2(t.Title));
            sp.Children.Add(U.T(t.What, 13.5, "Muted", null, true, new Thickness(0, 0, 0, 14)));
            var wrap = new WrapPanel();
            foreach (var inp in t.Inputs)
            {
                FrameworkElement ctl;
                if (inp.Kind == "choice")
                {
                    var cb = new ComboBox { Width = 220 };
                    foreach (var c in inp.Choices) cb.Items.Add(c);
                    cb.SelectedItem = inp.Default ?? inp.Choices[0];
                    combos[inp.Key] = cb; ctl = cb;
                }
                else
                {
                    var box = U.Box(inp.Default ?? "", inp.Kind != "text" && inp.Kind != "longtext");
                    if (inp.Kind == "longtext" || inp.Kind == "itemlist") { box.AcceptsReturn = true; box.TextWrapping = TextWrapping.Wrap; box.Height = 70; box.VerticalContentAlignment = VerticalAlignment.Top; }
                    boxes[inp.Key] = box; ctl = box;
                    var refKind = inp.Kind switch { "creature" => "creature", "item" => "item", "quest" => "quest", "spell" => "spell", _ => null };
                    if (refKind != null)
                    {
                        var nameT = U.T("", 12, "Amber", null, true, new Thickness(2, 4, 0, 0));
                        void Upd() { nameT.Text = long.TryParse(box.Text.Trim().TrimStart('#'), out var id) && id != 0 ? (refs.Name(refKind, id) ?? "") : ""; }
                        box.LostFocus += (s, e) => Upd(); Upd();
                        var pick = U.Btn("…", () =>
                        {
                            var dlg = new PickerWindow(w, "Pick a " + refKind, inp.Hint ?? inp.Label, q => refs.Search(refKind, q), "");
                            if (dlg.ShowDialog() == true && dlg.Picked != null) { box.Text = dlg.Picked.Value.ToString(CultureInfo.InvariantCulture); Upd(); }
                        }, null, "Pick by name"); pick.Padding = new Thickness(10, 5, 10, 5); pick.Margin = new Thickness(6, 0, 0, 0);
                        box.Width = 150;
                        ctl = U.Stack(U.Row(box, pick), nameT);
                    }
                }
                var wide = inp.Kind == "longtext" || inp.Kind == "itemlist" || inp.Kind == "text";
                var f = U.Field(inp.Label + (inp.Optional ? " (optional)" : ""), inp.Hint, ctl, null, wide ? 460 : 240);
                f.Margin = new Thickness(0, 0, 16, 14);
                wrap.Children.Add(f);
            }
            sp.Children.Add(wrap);
            var buttons = U.Row(U.Btn("Preview", () => Run(false), null, "Shows exactly what would be staged. Nothing is staged."), U.Btn("Stage", () => Run(true), "BtnPrimary", "Stages the changes into your changeset. Review them on Changes."));
            sp.Children.Add(buttons);
            form.Content = U.Card(sp, new Thickness(20, 16, 20, 16), new Thickness(0, 0, 0, 12));
            guide.Content = GuideCard.Make(t.Title, t.Explain, "Templates only stage changes. Nothing reaches the database until you Apply on the Changes screen, with a backup first.");
        }

        Dictionary<string, string> Values()
        {
            var v = boxes.ToDictionary(kv => kv.Key, kv => kv.Value.Text);
            foreach (var kv in combos) v[kv.Key] = kv.Value.SelectedItem as string ?? "";
            return v;
        }

        public void Run(bool stage)
        {
            if (cur == null) return;
            if (!S.HasReadAccount) { result.Content = U.Callout("Templates read your world database to copy rows. Finish setup step 3 first.", "warn"); return; }
            var target = stage ? S.Changes.Pending : new Changeset { Changes = S.Changes.Pending.Changes.ToList() };
            int before = target.Changes.Count;
            TemplateResult res;
            try
            {
                using var db = S.OpenRead(S.Profile.WorldDb);
                res = cur.Build(Values(), db, target, S.Profiles.Settings.CustomIdStart);
            }
            catch (TemplateException ex) { result.Content = U.Callout(Friendly.Text(ex), "warn", "Check the form"); return; }
            catch (Exception ex) { result.Content = U.Callout(Friendly.Text(ex), "bad", "Couldn't build it"); return; }
            if (stage) { S.SaveChanges(); w.Toast($"Staged {res.Changes.Count} change(s). Review them on Changes.", "ok"); }
            var sp = new StackPanel();
            sp.Children.Add(U.Eyebrow(stage ? "Staged" : "Preview — nothing staged yet"));
            sp.Children.Add(U.T(res.Headline, 15, "Text", FontWeights.SemiBold, true, new Thickness(0, 0, 0, 8)));
            foreach (var n in res.Notes) sp.Children.Add(U.T("• " + n, 12.5, "Muted", null, true, new Thickness(0, 0, 0, 4)));
            sp.Children.Add(U.T($"{res.Changes.Count} change(s):", 13, "Text", FontWeights.SemiBold, false, new Thickness(0, 10, 0, 6)));
            foreach (var c in res.Changes) sp.Children.Add(U.CheckRow(U.Check.Pending, c.Kind == ChangeKind.Insert ? "new row" : c.Kind == ChangeKind.Update ? "edit" : c.Kind.ToString().ToLowerInvariant(), c.Summary, null, 90));
            var sql = U.Code(string.Join(Environment.NewLine, res.Changes.Select(c => c.ForwardSql)), 200);
            sql.Margin = new Thickness(0, 10, 0, 10);
            sp.Children.Add(sql);
            if (stage) sp.Children.Add(U.Btn("Review on Changes", () => w.Navigate("changes"), "BtnAmber"));
            result.Content = U.Card(sp, new Thickness(20, 16, 20, 16), new Thickness(0));
            result.Dispatcher.BeginInvoke(new Action(() => result.BringIntoView()), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        public void ScreensOpen(string key, Dictionary<string, string> values)
        {
            var i = Templates.All.ToList().FindIndex(t => t.Key == key);
            list.SelectedIndex = i;
            foreach (var kv in values) if (boxes.TryGetValue(kv.Key, out var b)) b.Text = kv.Value;
        }
    }

    // =====================================================================================================
    /// <summary>World tables Keira3 users expect: conditions, waypoints, gossip menus, game events, area triggers.</summary>
    public sealed class WorldTablesPage : IPage
    {
        readonly MainWindow w;
        Studio S => w.S;
        readonly Grid view = new Grid();
        public UIElement View => view;
        readonly ListBox kinds = new ListBox();
        readonly ContentControl body = new ContentControl { Focusable = false };
        readonly ContentControl side = new ContentControl { Focusable = false };
        readonly Refs refs;
        string kind = "conditions";

        sealed class Kind { public string Key, Title, What, Explain; }
        static readonly Kind[] Kinds =
        {
            new Kind { Key = "conditions", Title = "Conditions", What = "When loot drops, gossip options show, spells hit, SmartAI runs…",
                Explain = "The conditions table adds requirements to other things. Pick what the condition applies to (the source type), its group and entry, and list the conditions: rows with the same ElseGroup must ALL be true; different ElseGroups are alternatives (OR). NegativeCondition = 1 flips a condition. Each row reads as a sentence below the grid." },
            new Kind { Key = "waypoints", Title = "Waypoints (paths)", What = "The points a creature walks along.",
                Explain = "waypoint_data holds paths: each row is one point (id = path, point = order, position, delay in ms at the point, move_type 0 walk / 1 run / 2 fly). A creature walks a path when its creature_addon (spawn) or creature_template_addon has path_id set and MovementType = 2. Tip: in game, .wp add while standing on each point, or use the spawn's coordinates from the Spawns tab." },
            new Kind { Key = "gossip", Title = "Gossip menus", What = "What NPCs say and the options you can click.",
                Explain = "A gossip menu (gossip_menu MenuID) shows one or more texts (npc_text) and options (gossip_menu_option). Options can open another menu (ActionMenuID), open vendor/trainer windows (OptionType + OptionNpcFlag), or ask to confirm (BoxText/BoxMoney). Creatures use a menu through creature_template.gossip_menu_id; conditions (source type 14/15) hide texts or options." },
            new Kind { Key = "events", Title = "Game events", What = "Holidays and timed events, and what spawns during them.",
                Explain = "game_event defines when an event runs (start/end time, how often, how long). game_event_creature and game_event_gameobject list spawns (by guid) that only exist while the event runs; a negative eventEntry means the spawn is REMOVED during the event. Use .event start <id> on a test realm to try it." },
            new Kind { Key = "areatriggers", Title = "Area triggers", What = "Invisible spots that teleport, script, or count for quests.",
                Explain = "Area triggers come from the client's AreaTrigger.dbc (position and size). The server attaches behaviour by ID: areatrigger_teleport (instance portals), areatrigger_scripts (C++ or SmartAI scripts, source_type 2 in smart_scripts), areatrigger_involvedrelation (exploration quest objectives) and areatrigger_tavern (rest areas)." },
            new Kind { Key = "pools", Title = "Pools", What = "Spawn groups where only some members are up at a time.",
                Explain = "pool_template defines a pool: max_limit = how many members can be spawned at once. Members are creature spawns (pool_creature), object spawns (pool_gameobject: herbs, ore, chests) or other pools (pool_pool), each by guid with a chance in percent (0 = equal share). Classic use: 5 ore veins in a cave with max_limit 2." },
            new Kind { Key = "spellarea", Title = "Spell areas", What = "Auras and spells given in a zone (phasing, buffs).",
                Explain = "spell_area casts (autocast 1) or allows a spell while a player is in an area, optionally only between two quest states, with an aura, race mask or gender. It's how 3.3.5a does phasing: a phase aura given in a zone after a quest. quest_start_status / quest_end_status are bit masks: 1 none, 2 complete, 8 incomplete, 32 rewarded, 64 failed." },
            new Kind { Key = "dungeon", Title = "Dungeon requirements", What = "Level, item level and keys/quests needed to enter.",
                Explain = "dungeon_access_template sets per map and difficulty the minimum/maximum level and average item level. dungeon_access_requirements adds items, quests or achievements (requirement_type 0 achievement, 1 quest, 2 item) a player (or only the leader) needs, with the message shown when it's missing." },
            new Kind { Key = "scripts", Title = "Script tables", What = "event_scripts, spell_scripts and waypoint_scripts.",
                Explain = "Old-style command scripts: each row is a command (command = what, delay = seconds after the start, datalong/dataint/x y z o = its values). event_scripts runs on game events (from objects, spells and transports), spell_scripts on spell effects, waypoint_scripts at waypoints. event_scripts and spell_scripts have no primary key, so Studio finds each row by all of its values." },
        };

        // inputs
        readonly ComboBox condSource = new ComboBox { Width = 300 };
        readonly TextBox k1 = U.Box("", true), k2 = U.Box("", true);
        readonly List<RowListEditor> editors = new List<RowListEditor>();

        public WorldTablesPage(MainWindow w)
        {
            this.w = w; refs = new Refs(w.S);
            view.Margin = new Thickness(28, 22, 28, 20);
            view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            view.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
            var strip = U.StepStrip(1); DockPanel.SetDock(strip, Dock.Right); strip.VerticalAlignment = VerticalAlignment.Center; head.Children.Add(strip);
            head.Children.Add(U.Stack(U.H1("World tables"), U.T("Conditions, waypoints, gossip menus, game events, area triggers, pools, spell areas, dungeon requirements and script tables, as explained, editable lists. Edits are staged with undo.", 13.5, "Muted")));
            view.Children.Add(head);
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
            Grid.SetRow(g, 1); view.Children.Add(g);
            foreach (var k in Kinds) kinds.Items.Add(new ListBoxItem { Tag = k, Content = U.Stack(U.T(k.Title, 13.5, "Text", FontWeights.SemiBold, false), U.T(k.What, 12, "Muted", null, true)) });
            ScrollViewer.SetHorizontalScrollBarVisibility(kinds, ScrollBarVisibility.Disabled);
            kinds.SelectionChanged += (s, e) => { if (kinds.SelectedItem is ListBoxItem li && li.Tag is Kind k) { kind = k.Key; BuildForm(); } };
            g.Children.Add(U.Card(U.Stack(U.Eyebrow("Table"), kinds), new Thickness(10, 12, 6, 8), new Thickness(0)));
            var bs = U.Scroll(body); Grid.SetColumn(bs, 2); g.Children.Add(bs);
            Grid.SetColumn(side, 4); g.Children.Add(side);
            foreach (var e in SpellCatalog.Of("condsource")) condSource.Items.Add(new ComboBoxItem { Content = $"{e.Value} · {e.Label}", Tag = e.Value });
            condSource.SelectedIndex = Math.Min(1, condSource.Items.Count - 1);
            k1.Width = 140; k2.Width = 140;
            k1.KeyDown += (s, e) => { if (e.Key == Key.Enter) Load(); };
            k2.KeyDown += (s, e) => { if (e.Key == Key.Enter) Load(); };
        }

        public void OnShow() { if (kinds.SelectedIndex < 0) kinds.SelectedIndex = 0; }

        static void Detach(FrameworkElement e) { if (e.Parent is Panel p) p.Children.Remove(e); }

        void BuildForm()
        {
            var k = Kinds.First(x => x.Key == kind);
            side.Content = GuideCard.Make(k.Title, k.Explain);
            foreach (var e in new FrameworkElement[] { condSource, k1, k2 }) Detach(e);
            editors.Clear();
            var form = new WrapPanel();
            FrameworkElement F(string label, string hint, FrameworkElement c, double wd = 200) { var f = U.Field(label, hint, c, null, wd); f.Margin = new Thickness(0, 0, 14, 8); return f; }
            switch (kind)
            {
                case "conditions":
                    form.Children.Add(F("Applies to (source type)", "What the conditions are attached to.", condSource, 320));
                    form.Children.Add(F("SourceGroup", "Depends on the type: loot entry, gossip MenuID, spell effect mask…", k1, 160));
                    form.Children.Add(F("SourceEntry", "Depends on the type: item, gossip TextID/OptionID, spell ID… (empty = all)", k2, 160));
                    break;
                case "waypoints": form.Children.Add(F("Path ID", "waypoint_data.id (often the spawn guid × 10).", k1)); break;
                case "gossip": form.Children.Add(F("Menu ID", "gossip_menu.MenuID (creature_template.gossip_menu_id).", k1)); break;
                case "events": form.Children.Add(F("Event ID", "game_event.eventEntry (e.g. 2 = Winter Veil, 12 = Hallow's End).", k1)); break;
                case "areatriggers": form.Children.Add(F("Area trigger ID", "The ID from AreaTrigger.dbc.", k1)); break;
                case "pools": form.Children.Add(F("Pool ID", "pool_template.entry.", k1)); break;
                case "spellarea": form.Children.Add(F("Area ID", "AreaTable ID (zone or sub-area). Empty if you search by spell.", k1)); form.Children.Add(F("or Spell ID", "Show every area that gives this spell.", k2)); break;
                case "dungeon": form.Children.Add(F("Map ID", "Map of the dungeon or raid (e.g. 36 Deadmines, 533 Naxxramas).", k1)); break;
                case "scripts": form.Children.Add(F("Script ID", "The id in event_scripts / spell_scripts / waypoint_scripts.", k1)); break;
            }
            var load = U.Btn("Load", Load, "BtnPrimary"); load.VerticalAlignment = VerticalAlignment.Center; load.Margin = new Thickness(0, 8, 0, 0);
            form.Children.Add(load);
            body.Content = U.Stack(U.Card(form, new Thickness(18, 14, 18, 6), new Thickness(0, 0, 0, 12)), U.Callout("Type an ID and press Load (Enter).", "info"));
        }

        RowListEditor List(string title, string help, string table, string where, Dictionary<string, string> fixedV, string order, params RowListEditor.NameColumn[] names)
        {
            var ed = new RowListEditor(w, title, help, table, where, fixedV, order, names, (sp, id) => refs.ByKind(sp, id));
            ed.Load();
            editors.Add(ed);
            return ed;
        }

        static string L(long v) => v.ToString(CultureInfo.InvariantCulture);

        RowListEditor ListKeyless(string title, string help, string table, string where, Dictionary<string, string> fixedV, string order)
        {
            var ed = new RowListEditor(w, title, help, table, where, fixedV, order, null, (sp, id) => refs.ByKind(sp, id)) { MatchAllColumns = true };
            ed.Load();
            editors.Add(ed);
            return ed;
        }

        public void Load()
        {
            if (!S.HasReadAccount) { body.Content = U.Callout("Finish setup step 3 (the read-only account) first.", "warn"); return; }
            editors.Clear();
            var stack = new StackPanel();
            var form = ((StackPanel)body.Content).Children[0];
            ((StackPanel)body.Content).Children.Remove(form);
            stack.Children.Add(form);
            bool Num(TextBox b, out long v) => long.TryParse(b.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
            try
            {
                switch (kind)
                {
                    case "conditions":
                    {
                        long st = condSource.SelectedItem is ComboBoxItem ci ? (long)ci.Tag : 0;
                        Num(k1, out var grp);
                        bool hasEntry = Num(k2, out var ent);
                        var where = $"SourceTypeOrReferenceId = {L(st)} AND SourceGroup = {L(grp)}" + (hasEntry ? $" AND SourceEntry = {L(ent)}" : "");
                        var fx = new Dictionary<string, string> { ["SourceTypeOrReferenceId"] = L(st), ["SourceGroup"] = L(grp) };
                        if (hasEntry) fx["SourceEntry"] = L(ent);
                        var ed = List("Conditions", "One row per requirement. Same ElseGroup = AND, different ElseGroup = OR.", "conditions", where, fx, "SourceEntry, ElseGroup, ConditionTypeOrReference");
                        stack.Children.Add(U.Card(ed.View, new Thickness(18, 14, 18, 12), new Thickness(0, 0, 0, 12)));
                        stack.Children.Add(U.Card(ConditionSentences(ed), new Thickness(18, 14, 18, 12), new Thickness(0, 0, 0, 12)));
                        ed.Changed = e2 => { stack.Children.RemoveAt(stack.Children.Count - 1); stack.Children.Add(U.Card(ConditionSentences(e2), new Thickness(18, 14, 18, 12), new Thickness(0, 0, 0, 12))); };
                        break;
                    }
                    case "waypoints":
                    {
                        if (!Num(k1, out var path)) throw new FormatException("Path ID: enter a number.");
                        var ed = List($"Path {path}", "Points in order. delay = ms to wait at the point; move_type 0 walk, 1 run, 2 fly; action = waypoint_scripts id.", "waypoint_data", $"id = {L(path)}", new Dictionary<string, string> { ["id"] = L(path) }, "point");
                        ed.NewRow = r => { long next = 1; foreach (System.Data.DataRow dr in ed.Data.Rows) if (dr.RowState != System.Data.DataRowState.Deleted && long.TryParse(Convert.ToString(dr["point"]), out var pt)) next = Math.Max(next, pt + 1); r.Set("point", L(next)); return r; };
                        stack.Children.Add(U.Card(ed.View, new Thickness(18, 14, 18, 12), new Thickness(0, 0, 0, 12)));
                        using var db = S.OpenRead(S.Profile.WorldDb);
                        var users = new List<string>();
                        try { foreach (var r in db.Query($"SELECT guid FROM creature_addon WHERE path_id = {L(path)} LIMIT 20", 20).Rows) users.Add("spawn guid " + r[0]); } catch { }
                        try { foreach (var r in db.Query($"SELECT entry FROM creature_template_addon WHERE path_id = {L(path)} LIMIT 20", 20).Rows) users.Add($"creature {r[0]} ({refs.Name("creature", Convert.ToInt64(r[0]))})"); } catch { }
                        stack.Children.Add(U.Callout(users.Count == 0 ? "Nothing uses this path yet: set path_id on a spawn's creature_addon (and MovementType 2 on the spawn)." : "Used by: " + string.Join(", ", users), users.Count == 0 ? "warn" : "ok", "Who walks this path"));
                        break;
                    }
                    case "gossip":
                    {
                        if (!Num(k1, out var menu)) throw new FormatException("Menu ID: enter a number.");
                        var texts = List($"Texts of menu {menu}", "gossip_menu: which npc_text the menu shows (conditions can choose between several).", "gossip_menu", $"MenuID = {L(menu)}", new Dictionary<string, string> { ["MenuID"] = L(menu) }, "TextID");
                        stack.Children.Add(U.Card(texts.View, new Thickness(18, 14, 18, 12), new Thickness(0, 0, 0, 12)));
                        var opts = List($"Options of menu {menu}", "gossip_menu_option: the clickable lines. OptionIcon 0 chat, 1 vendor, 2 taxi, 3 trainer; ActionMenuID opens another menu.", "gossip_menu_option", $"MenuID = {L(menu)}", new Dictionary<string, string> { ["MenuID"] = L(menu) }, "OptionID");
                        stack.Children.Add(U.Card(opts.View, new Thickness(18, 14, 18, 12), new Thickness(0, 0, 0, 12)));
                        using var db = S.OpenRead(S.Profile.WorldDb);
                        var preview = new StackPanel();
                        foreach (System.Data.DataRow r in texts.Data.Rows)
                        {
                            if (!long.TryParse(Convert.ToString(r["TextID"]), out var tid)) continue;
                            try
                            {
                                var t = db.Query($"SELECT text0_0, text0_1 FROM npc_text WHERE ID = {L(tid)}", 1);
                                if (t.Rows.Count > 0)
                                {
                                    var textId = tid;
                                    var edit = U.Btn("Edit text ›", () => w.Navigate("npctext", pg => ((EntityEditorPage)pg).Open(textId)), "BtnGhost", $"Open npc_text {tid} in the NPC text editor (all 8 variants, chances, emotes).");
                                    edit.Padding = new Thickness(8, 3, 8, 3); edit.Margin = new Thickness(0, 0, 10, 0); edit.VerticalAlignment = VerticalAlignment.Top;
                                    var line = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                                    DockPanel.SetDock(edit, Dock.Left); line.Children.Add(edit);
                                    line.Children.Add(U.T($"npc_text {tid}: " + (Convert.ToString(t.Rows[0][0]) is string a && a.Length > 0 ? a : Convert.ToString(t.Rows[0][1])), 13, "Text", null, true));
                                    preview.Children.Add(line);
                                }
                            }
                            catch { }
                        }
                        try { foreach (var r in db.Query($"SELECT entry, name FROM creature_template WHERE gossip_menu_id = {L(menu)} LIMIT 10", 10).Rows) preview.Children.Add(U.T($"Used by creature {r[0]} \"{r[1]}\"", 12.5, "Muted")); } catch { }
                        stack.Children.Add(U.Card(U.Stack(U.H2("What players read"), preview), new Thickness(18, 14, 18, 12), new Thickness(0, 0, 0, 12)));
                        break;
                    }
                    case "events":
                    {
                        if (!Num(k1, out var ev)) throw new FormatException("Event ID: enter a number.");
                        var e1 = List($"Event {ev}", "When it runs: start_time / end_time, occurence (minutes between starts) and length (minutes it lasts).", "game_event", $"eventEntry = {L(ev)}", new Dictionary<string, string> { ["eventEntry"] = L(ev) }, null);
                        stack.Children.Add(U.Card(e1.View, new Thickness(18, 14, 18, 12), new Thickness(0, 0, 0, 12)));
                        var cr = List("Creature spawns during the event", "Spawn guids that only exist while it runs. Use a negative eventEntry (on another row) to remove a spawn during the event.", "game_event_creature", $"eventEntry = {L(ev)}", new Dictionary<string, string> { ["eventEntry"] = L(ev) }, "guid");
                        stack.Children.Add(U.Card(cr.View, new Thickness(18, 14, 18, 12), new Thickness(0, 0, 0, 12)));
                        var go = List("Object spawns during the event", "Game object spawn guids that only exist while it runs.", "game_event_gameobject", $"eventEntry = {L(ev)}", new Dictionary<string, string> { ["eventEntry"] = L(ev) }, "guid");
                        stack.Children.Add(U.Card(go.View, new Thickness(18, 14, 18, 12), new Thickness(0, 0, 0, 12)));
                        break;
                    }
                    case "pools":
                    {
                        if (!Num(k1, out var pool)) throw new FormatException("Pool ID: enter a number.");
                        foreach (var (table, title, help, where, fix, order) in new[]
                        {
                            ("pool_template", $"Pool {pool}", "max_limit: how many members may be spawned at the same time. description is just for you.", $"`entry` = {L(pool)}", "entry", "entry"),
                            ("pool_creature", "Creature spawns in it", "guid = a creature spawn's guid; chance in percent (0 = equal share of what's left).", $"`pool_entry` = {L(pool)}", "pool_entry", "guid"),
                            ("pool_gameobject", "Object spawns in it", "guid = a game object spawn's guid (herbs, ore, chests).", $"`pool_entry` = {L(pool)}", "pool_entry", "guid"),
                            ("pool_pool", "Pools inside it", "pool_id = a child pool; mother_pool = this pool.", $"`mother_pool` = {L(pool)}", "mother_pool", "pool_id"),
                        })
                        {
                            var ed = List(title, help, table, where, new Dictionary<string, string> { [fix] = L(pool) }, order);
                            stack.Children.Add(U.Card(ed.View, new Thickness(18, 14, 18, 12), new Thickness(0, 0, 0, 12)));
                        }
                        try
                        {
                            using var db = S.OpenRead(S.Profile.WorldDb);
                            var parent = db.Scalar($"SELECT mother_pool FROM pool_pool WHERE pool_id = {L(pool)} LIMIT 1");
                            if (!string.IsNullOrEmpty(parent)) stack.Children.Add(U.Callout($"This pool is itself a member of pool {parent}.", "info", "Parent pool"));
                        }
                        catch { }
                        break;
                    }
                    case "spellarea":
                    {
                        bool byArea = Num(k1, out var area), bySpell = Num(k2, out var spell);
                        if (!byArea && !bySpell) throw new FormatException("Enter an area ID or a spell ID.");
                        var where = byArea ? $"`area` = {L(area)}" : $"`spell` = {L(spell)}";
                        var fix = byArea ? new Dictionary<string, string> { ["area"] = L(area) } : new Dictionary<string, string> { ["spell"] = L(spell) };
                        var ed = List(byArea ? $"Spells in area {area} ({S.Dbc.Name("area", area) ?? "?"})" : $"Areas that give spell {spell}", "autocast 1 = cast automatically on entering; racemask 0 = all races; gender 2 = both.",
                            "spell_area", where, fix, "spell, area", new RowListEditor.NameColumn { IdColumn = "spell", Kind = "spell", Header = "spell name" }, new RowListEditor.NameColumn { IdColumn = "area", Kind = "area", Header = "area name" });
                        stack.Children.Add(U.Card(ed.View, new Thickness(18, 14, 18, 12), new Thickness(0, 0, 0, 12)));
                        break;
                    }
                    case "dungeon":
                    {
                        if (!Num(k1, out var map)) throw new FormatException("Map ID: enter a number.");
                        var t = List($"Entry rules for map {map} ({S.Dbc.Name("map", map) ?? "?"})", "One row per difficulty (0 normal / 10-player, 1 heroic / 25-player, 2-3 raid heroic): min_level, max_level (0 = none), min_avg_item_level.",
                            "dungeon_access_template", $"`map_id` = {L(map)}", new Dictionary<string, string> { ["map_id"] = L(map) }, "difficulty");
                        stack.Children.Add(U.Card(t.View, new Thickness(18, 14, 18, 12), new Thickness(0, 0, 0, 12)));
                        var ids = t.Data == null ? new List<string>() : t.Data.Rows.Cast<System.Data.DataRow>().Select(r => Convert.ToString(r["id"])).Where(x => x.Length > 0).ToList();
                        if (ids.Count == 0) stack.Children.Add(U.Callout("No entry rules for this map yet. Add a row above (with a new id), stage it, then come back to add requirements.", "info"));
                        else
                        {
                            var req = List("Requirements", "requirement_type 0 achievement, 1 quest, 2 item; requirement_id = its ID; requirement_note = the message when it's missing; faction 0 Alliance, 1 Horde, 2 both; leader_only 1 = only the group leader needs it.",
                                "dungeon_access_requirements", $"`dungeon_access_id` IN ({string.Join(", ", ids)})", ids.Count == 1 ? new Dictionary<string, string> { ["dungeon_access_id"] = ids[0] } : null, "dungeon_access_id, requirement_type");
                            stack.Children.Add(U.Card(req.View, new Thickness(18, 14, 18, 12), new Thickness(0, 0, 0, 12)));
                        }
                        break;
                    }
                    case "scripts":
                    {
                        if (!Num(k1, out var sid)) throw new FormatException("Script ID: enter a number.");
                        foreach (var (table, help) in new[]
                        {
                            ("event_scripts", "Commands run for event " + sid + "."),
                            ("spell_scripts", "Commands run for spell " + sid + " (effIndex = which spell effect)."),
                            ("waypoint_scripts", "Commands run at waypoints with action " + sid + " (guid is the key)."),
                        })
                        {
                            var ed = ListKeyless(table, help, table, $"`id` = {L(sid)}", new Dictionary<string, string> { ["id"] = L(sid) }, "delay");
                            stack.Children.Add(U.Card(ed.View, new Thickness(18, 14, 18, 12), new Thickness(0, 0, 0, 12)));
                        }
                        break;
                    }
                    case "areatriggers":
                    {
                        if (!Num(k1, out var at)) throw new FormatException("Area trigger ID: enter a number.");
                        string pos = "not in the server's AreaTrigger.dbc";
                        try
                        {
                            var d = ConfParser.ParseFile(S.Profile.ConfPath).DataDir;
                            var t = DbcTable.Load(Path.Combine(d, "dbc", "AreaTrigger.dbc"));
                            var r = t.Find((uint)at);
                            if (r != null) pos = $"map {t.FieldText(r, 1)} ({S.Dbc.Name("map", r.V[1])}), x {t.FieldText(r, 2)}, y {t.FieldText(r, 3)}, z {t.FieldText(r, 4)}, radius {t.FieldText(r, 5)}";
                        }
                        catch { }
                        stack.Children.Add(U.Callout(pos, "info", $"Area trigger {at} in AreaTrigger.dbc"));
                        foreach (var (table, key, help) in new[]
                        {
                            ("areatrigger_teleport", "ID", "Where the trigger sends players (instance entrances)."),
                            ("areatrigger_scripts", "entry", "A script name (C++), or SmartAI when ScriptName = 'SmartTrigger'."),
                            ("areatrigger_involvedrelation", "id", "Quest exploration objective: entering completes the quest's \"explore\" goal."),
                            ("areatrigger_tavern", "id", "Marks an inn: resting area."),
                        })
                        {
                            try
                            {
                                var ed = List(table, help, table, $"{SqlGen.Ident(key)} = {L(at)}", new Dictionary<string, string> { [key] = L(at) }, null);
                                stack.Children.Add(U.Card(ed.View, new Thickness(18, 14, 18, 12), new Thickness(0, 0, 0, 12)));
                            }
                            catch { }
                        }
                        break;
                    }
                }
            }
            catch (Exception ex) { stack.Children.Add(U.Callout(Friendly.Text(ex), "warn")); }
            body.Content = stack;
        }

        UIElement ConditionSentences(RowListEditor ed)
        {
            var sp = new StackPanel();
            sp.Children.Add(U.H2("Reads as"));
            if (ed.Data == null || ed.Data.Rows.Count == 0) { sp.Children.Add(U.T("No conditions: always allowed.", 13, "Muted")); return sp; }
            foreach (var grp in ed.Data.Rows.Cast<System.Data.DataRow>().Where(r => r.RowState != System.Data.DataRowState.Deleted).GroupBy(r => Convert.ToString(r["ElseGroup"])))
            {
                sp.Children.Add(U.T($"Else-group {grp.Key} (all of these):", 13, "Amber", FontWeights.SemiBold, false, new Thickness(0, 6, 0, 4)));
                foreach (var r in grp)
                {
                    long.TryParse(Convert.ToString(r["ConditionTypeOrReference"]), out var ct);
                    var e = SpellCatalog.Get("condition", ct);
                    var neg = Convert.ToString(r["NegativeCondition"]) == "1";
                    string vals = $"value1 {r["ConditionValue1"]}, value2 {r["ConditionValue2"]}, value3 {r["ConditionValue3"]}";
                    var text = (neg ? "NOT " : "") + (e?.Label ?? "condition " + ct) + $" ({vals})" + (e != null && e.Studio && !string.IsNullOrEmpty(e.Desc) ? " — " + e.Desc : "");
                    sp.Children.Add(U.T("• " + text, 12.5, "Text", null, true, new Thickness(0, 0, 0, 3)));
                }
            }
            sp.Children.Add(U.T("Different else-groups are alternatives: one whole group being true is enough.", 12, "Muted", null, true, new Thickness(0, 6, 0, 0)));
            return sp;
        }

        public void ScreensLoad(string k, string a, string b = "", int source = -1)
        {
            kinds.SelectedIndex = Array.FindIndex(Kinds, x => x.Key == k);
            if (source >= 0) foreach (ComboBoxItem it in condSource.Items) if ((long)it.Tag == source) condSource.SelectedItem = it;
            k1.Text = a; k2.Text = b; Load();
        }
    }

    // =====================================================================================================
    /// <summary>Help / Getting started: what Studio is, the safety model, setup, a glossary and the keys.</summary>
    public sealed class HelpPage : IPage
    {
        readonly ContentControl host = new ContentControl { Focusable = false };
        public UIElement View => host;
        readonly MainWindow w;
        public HelpPage(MainWindow w) { this.w = w; Build(); }
        public void OnShow() { }

        static Border Section(string title, params UIElement[] kids) => U.Card(U.Stack(new[] { (UIElement)U.H2(title) }.Concat(kids).ToArray()), new Thickness(22, 16, 22, 16));
        static TextBlock P(string t) => U.T(t, 13.5, "Text", null, true, new Thickness(0, 0, 0, 10));

        void Build()
        {
            var sp = new StackPanel { Margin = new Thickness(28, 22, 28, 20), MaxWidth = 1000, HorizontalAlignment = HorizontalAlignment.Left };
            sp.Children.Add(U.H1("Getting started"));
            sp.Children.Add(U.Sub("AndersCraft Studio is a safe, friendly editor for AzerothCore 3.3.5a (Wrath of the Lich King) servers: world database content, the client's DBC tables and spells, and MPQ patches for your players."));
            sp.Children.Add(Section("What you need",
                P("• An AzerothCore 3.3.5a server you run yourself (its worldserver.conf on this PC or a reachable path) and its MySQL / MariaDB database.\n• MySQL's mysqldump.exe (comes with MySQL Server) for the backup Studio takes before every Apply.\n• Optional: your 3.3.5a game client folder, for icons, client DBCs and building patches.\n• Windows 10/11 (64-bit). The self-contained download needs nothing else; the small download needs the .NET 10 Desktop Runtime.")));
            sp.Children.Add(Section("Set up in five minutes",
                P("1. Start Studio. The setup wizard opens.\n2. Step 2 finds worldserver.conf (Auto-detect or Browse). Studio reads which databases the server uses; it never stores the server's own MySQL password.\n3. Step 3 makes two MySQL accounts just for Studio: one that can only read (used for all browsing), one that can only change rows in the world database (used only by Apply). Press \"Run it now as admin…\" and type your MySQL root password once (used for that run, never saved), or copy the script into any MySQL tool. Then press \"Test the accounts\".\n4. Step 4 (optional): your game client folder.\n5. Step 5 (recommended): create a sandbox, a full copy of the world database to practise on (\"Run it now as admin…\" again). Apply goes there until you unlock Live in Settings.\n\nNo server yet? Install MySQL, open Admin console › Set up AzerothCore: it creates the three databases and the server's account and loads AzerothCore's base tables. Start authserver and worldserver once, then run this wizard."),
                U.Btn("Open the setup wizard", () => w.ShowWizard(1, true), "BtnPrimary")));
            sp.Children.Add(Section("The safety model (why nothing breaks by accident)",
                P("Every screen shows the same four steps: Edit → Stage → Review → Apply.\n• Browsing and queries use the read-only account in READ ONLY sessions, and every statement is checked again by Studio.\n• Edits are never written directly: they are staged into a changeset file with the old value, the new value and the SQL to undo it.\n• Changes shows every staged change side by side. Apply backs up the affected tables with mysqldump first, asks you to type APPLY (or APPLY LIVE), and runs everything in one transaction that is re-checked row by row. Any mismatch rolls it all back.\n• History keeps every Apply with its backup and a Roll back button.\n• Client data (DBC files, patches) never overwrites originals: edits leave as files in Studio's own folders, and installing a patch backs up anything it replaces.")));
            sp.Children.Add(Section("Admin console (instead of HeidiSQL)",
                P("For MySQL administration the editors deliberately leave out: run any SQL directly, databases, tables, columns, indexes and views, users and permissions, connections, server status, importing .sql files or whole folders, exporting databases, and setting up AzerothCore's databases from scratch.\n• It needs an admin login (usually root) that you type each time; Studio keeps it in memory only and forgets it when you disconnect or close Studio.\n• Statements there run straight away. Anything that deletes asks you to type the object's name; account, permission and connection changes are marked as permanent; a copy of a table is offered before risky changes.\n• For content, keep using the editors: they stage, preview and back up every change."),
                U.Btn("Open the Admin console", () => w.Navigate("admin"))));
            sp.Children.Add(Section("3D viewer",
                P("Creatures, game objects, items and spell effects show in 3D in their editors (Creatures and Game objects › Basics, Items › Display, Spells › Visuals) and in Asset import. Drag to turn, right-drag to move, wheel to zoom, double-click to reset; pick an animation and press Play. It reads your client's archives (setup step 4), never changes them, and shows imported files from your patch work folders before you install them. Particle effects are an approximation of the game's.")));
            var gloss = new[]
            {
                ("Entry", "The ID of a template: creature_template.entry, item_template.entry, quest_template.ID."),
                ("Guid", "The ID of one placed copy in the world (a spawn): creature.guid, gameobject.guid."),
                ("Changeset", "Your list of staged changes (a JSON file). Nothing in it has touched the database until Apply."),
                ("Stage", "Write an edit into the changeset instead of the database."),
                ("Apply", "Run the ticked staged changes, after a backup, in one transaction, on the sandbox or Live."),
                ("Roll back", "Run the undo SQL of an earlier Apply (newest first), after a fresh backup."),
                ("Sandbox", "A practice copy of the world database. Players never see it."),
                ("Live", "Your real world database. Locked until you allow it in Settings."),
                ("DBC", "The client's data tables (Spell.dbc, AreaTable.dbc…). The server reads its own copies from Data\\dbc."),
                ("MPQ / patch", "The client's archive format. A patch-X.MPQ in the client's Data folder overrides files in earlier archives."),
                ("spell_dbc", "A world-database table AzerothCore loads as extra Spell.dbc rows: how the server learns about custom spells."),
                ("SmartAI", "AzerothCore's database scripting: smart_scripts rows of event → action → target."),
                ("Reference loot", "A shared loot list (reference_loot_template) other loot tables point to."),
                ("Condition", "A requirement attached to loot, gossip, spells… in the conditions table."),
                ("Template (Studio)", "A ready-made action that stages several related changes at once."),
            };
            var gs = new StackPanel();
            foreach (var (term, def) in gloss) gs.Children.Add(U.CheckRow(U.Check.Info, term, def, null, 170));
            sp.Children.Add(Section("Glossary", gs));
            sp.Children.Add(Section("Keys",
                P("Ctrl+K search everything · Alt+1…9 sidebar · Ctrl+S stage · Ctrl+D duplicate as new · Ctrl+PgUp/PgDn tabs · F5 / Ctrl+Enter run a query · F2 edit a cell · Ctrl+F filter · Ctrl+G find an ID · Ctrl+Shift+R review changes · Ctrl+, settings · F1 this page")));
            sp.Children.Add(Section("Where your files are",
                P("Settings and saved passwords (encrypted for your Windows user): %APPDATA%\\AndersCraftStudio\nChangesets, backups, patch work folders and built patches: Documents\\AndersCraft Studio (change it in Settings)."),
                U.Row(U.Btn("About AndersCraft Studio", () => AboutWindow.ShowAbout(w)), U.Btn("Settings", () => w.Navigate("settings"), "BtnGhost"))));
            sp.Children.Add(Section("Something went wrong?",
                P("Studio keeps a log of errors and important actions (passwords are removed before anything is written), one file per day for the last 14 days. To report a problem, open the logs, attach today's file, and describe what you did at github.com/damananders/WoWPrivateServerStudio/issues. Studio sends nothing anywhere by itself: it only talks to your own database."),
                U.Row(U.Btn("Open logs", () => { try { System.IO.Directory.CreateDirectory(Log.Folder); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + Log.Folder + "\"") { UseShellExecute = true }); } catch (Exception ex) { w.Toast(Friendly.Text(ex), "bad"); } }),
                      U.Btn("Report a problem", () => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/damananders/WoWPrivateServerStudio/issues") { UseShellExecute = true }); } catch { } }, "BtnGhost"))));
            host.Content = U.Scroll(sp);
        }
    }

    /// <summary>About: version, licence, credits.</summary>
    public sealed class AboutWindow : Window
    {
        public static void ShowAbout(Window owner) { var a = new AboutWindow(owner); if (MainWindow.Instance?.IsScreens == true) { a.WindowStartupLocation = WindowStartupLocation.Manual; a.Left = -20000; a.Top = -20000; a.ShowActivated = false; Last = a; a.Show(); } else a.ShowDialog(); }
        public static AboutWindow Last;

        public AboutWindow(Window owner)
        {
            Owner = owner; Width = 640; SizeToContent = SizeToContent.Height; WindowStartupLocation = WindowStartupLocation.CenterOwner; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Background = U.R("Panel"); FontFamily = U.Font; Foreground = U.R("Text");
            System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome { CaptionHeight = 30, GlassFrameThickness = new Thickness(0), ResizeBorderThickness = new Thickness(0) });
            var head = new DockPanel();
            var crest = U.Crest(72); crest.Margin = new Thickness(0, 0, 18, 0); DockPanel.SetDock(crest, Dock.Left); head.Children.Add(crest);
            head.Children.Add(U.Stack(U.T("AndersCraft Studio", 24, "Text", FontWeights.SemiBold, false), U.T("Version " + AppInfo.Version, 13.5, "Amber", FontWeights.SemiBold, false), U.T("Safe, staged editing for AzerothCore 3.3.5a servers.", 13, "Muted", null, true)));
            var credits = U.T("Uses MySqlConnector (MIT licence, © Bradley Grainger and contributors).\nThe PKWARE DCL, Storm Huffman and ADPCM tables are format constants as documented by StormLib (MIT licence, © Ladislav Zezula).\nDBC column layouts and spell/condition enum names are generated from AzerothCore's source (GPL-2, the AzerothCore project).\nEverything else is Studio's own code.\n\nNot affiliated with or endorsed by Blizzard Entertainment. World of Warcraft is a trademark of Blizzard Entertainment, Inc. Studio ships no game data: it reads the files of a client and server you already have.", 12.5, "Muted", null, true, new Thickness(0, 14, 0, 12));
            var lic = U.Code(AppInfo.License, 220);
            var close = U.Btn("Close", Close, "BtnPrimary"); close.IsDefault = true; close.IsCancel = true; close.HorizontalAlignment = HorizontalAlignment.Left; close.Margin = new Thickness(0, 14, 0, 0);
            Content = new Border { BorderBrush = U.R("LineHi"), BorderThickness = new Thickness(1), Padding = new Thickness(24, 20, 24, 20), Child = U.Stack(head, credits, U.Eyebrow("Licence"), lic, close) };
        }
    }
}
