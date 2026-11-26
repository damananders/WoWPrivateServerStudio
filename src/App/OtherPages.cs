using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AndersCraftStudio.Core;
using Microsoft.Win32;

namespace AndersCraftStudio
{
    /// <summary>Placeholder for workspaces planned for later milestones.</summary>
    public sealed class ComingSoonPage : IPage
    {
        readonly MainWindow w;
        readonly string key;
        public UIElement View { get; }

        public ComingSoonPage(MainWindow w, string key)
        {
            this.w = w; this.key = key;
            var (title, glyph, what, table) = key switch
            {
                "creatures" => ("Creatures", "", "Search NPCs by name, see their stats, spawns and AI in one place, and edit them with plain-English fields instead of raw columns.", "creature_template"),
                "items" => ("Items", "", "Find any item, preview its tooltip, and tweak stats, prices and requirements with a live tooltip preview.", "item_template"),
                "quests" => ("Quests", "", "Follow quest chains, edit objectives, text and rewards, and see who starts and ends each quest.", "quest_template"),
                "loot" => ("Loot", "", "See what drops from what, at which chance, including shared reference tables, and adjust drop rates safely.", "creature_loot_template"),
                "smartai" => ("SmartAI", "", "Read and build smart_scripts as readable event → action → target sentences instead of numbers.", "smart_scripts"),
                "spells" => ("Spells", "", "Browse Spell.dbc together with the server's spell tables, and stage spell changes for both the client and the server.", "spell_dbc"),
                "patches" => ("Client patches", "", "Package your client-side changes (DBC tables, icons, models, interface files) into a proper MPQ patch (patch-X.MPQ) for the 3.3.5a client: build it, check what's inside, compare with the previous patch, and install it into your client's Data folder in one step. Uses the client folder from setup step 4.", null),
                _ => ("DBC tables", "", "Open the game client's DBC data tables, edit them, and produce a patch for the client. Uses the client folder from setup step 4.", null),
            };
            var sp = new StackPanel { MaxWidth = 680, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
            sp.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 34, Foreground = U.R("Amber"), Margin = new Thickness(0, 0, 0, 16) });
            sp.Children.Add(U.Row(U.H1(title), new Border { Width = 12 }, U.Pill(key == "patches" ? "coming in Milestone 4" : "coming in a later milestone", "AmberDim", "Amber")));
            sp.Children.Add(U.T(what, 15, "Muted", null, true, new Thickness(0, 8, 0, 20)));
            sp.Children.Add(U.Callout("Milestone 1 builds the safety foundation every editor will share: the read-only browser, the changeset, review, backup-first Apply and roll back. This workspace will sit on top of it, so its edits will be staged and reviewable the same way.", "info", "Why it isn't here yet"));
            var row = U.Row();
            if (table != null) row.Children.Add(U.Btn($"Meanwhile, open {table} in the SQL browser", () => w.Navigate("sql", pg => ((SqlBrowserPage)pg).OpenTable(null, table)), "BtnPrimary", "Browse and edit the raw table today. Edits are staged, never written directly."));
            else row.Children.Add(U.Btn("Open the SQL browser", () => w.Navigate("sql"), "BtnPrimary"));
            sp.Children.Add(row);
            View = new Border { Padding = new Thickness(40, 30, 40, 30), Child = U.Scroll(sp) };
        }

        public void OnShow() { }
    }

    /// <summary>Profile, folders, Safety (the Live switch) and re-running setup.</summary>
    public sealed class SettingsPage : IPage
    {
        readonly MainWindow w;
        Studio S => w.S;
        readonly ContentControl host = new ContentControl { Focusable = false };
        public UIElement View => host;

        public SettingsPage(MainWindow w) { this.w = w; }

        public void OnShow() => Build();

        void Build()
        {
            var st = S.Profiles.Settings;
            var p = S.Profile;
            var sp = new StackPanel { MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left };
            sp.Children.Add(U.Hero(U.Stack(U.H1("Settings"), U.T("Everything is saved in %APPDATA%\\AndersCraftStudio. Passwords are encrypted for your Windows user.", 13.5, "Muted"))));


            // ---- profile
            var prof = new StackPanel();
            prof.Children.Add(U.H2("Server profile"));
            if (p == null) prof.Children.Add(U.T("No profile yet.", 13, "Muted"));
            else
            {
                prof.Children.Add(U.CheckRow(U.Check.Info, "Profile", p.Name));
                prof.Children.Add(U.CheckRow(U.Check.Info, "worldserver.conf", p.ConfPath ?? "(not set)"));
                prof.Children.Add(U.CheckRow(U.Check.Info, "Core", p.Core ?? "unknown"));
                prof.Children.Add(U.CheckRow(U.Check.Info, "Database server", $"{p.Host}:{p.Port}"));
                prof.Children.Add(U.CheckRow(U.Check.Info, "Databases", $"world {p.WorldDb} · characters {p.CharactersDb} · auth {p.AuthDb}"));
                prof.Children.Add(U.CheckRow(p.SandboxExists ? U.Check.Ok : U.Check.Warn, "Sandbox", p.SandboxDb + (p.SandboxExists ? " (exists)" : " (not created yet)")));
                prof.Children.Add(U.CheckRow(S.HasReadAccount ? U.Check.Ok : U.Check.Warn, "Browse account", S.HasReadAccount ? p.RoUser + " (password saved)" : "not set up"));
                prof.Children.Add(U.CheckRow(S.HasWriteAccount ? U.Check.Ok : U.Check.Warn, "Apply account", S.HasWriteAccount ? p.RwUser + " (password saved)" : "not set up"));
                prof.Children.Add(U.CheckRow(U.Check.Info, "Game client", string.IsNullOrEmpty(p.ClientDir) ? "(not set, optional)" : p.ClientDir));
            }
            var names = S.Profiles.Names();
            if (names.Count > 1)
            {
                var pick = new ComboBox { Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
                foreach (var n in names) pick.Items.Add(n);
                pick.SelectedItem = p?.Name;
                pick.SelectionChanged += (s, e) =>
                {
                    if (!(pick.SelectedItem is string n) || n == p?.Name) return;
                    S.Profiles.Settings.ActiveProfile = n; S.Profiles.SaveSettings();
                    S.Profile = S.Profiles.Active();
                    w.ApplyTarget = "Sandbox";
                    w.Toast($"Switched to the server profile \"{n}\".", "ok");
                    w.WizardDone();
                };
                var pf = U.Field("Switch server", "Each server you set up is a saved profile with its own accounts, databases and client folder.", pick);
                pf.Margin = new Thickness(0, 12, 0, 0);
                prof.Children.Add(pf);
            }
            var rr = U.Row(U.Btn("Run setup again", () => w.ShowWizard(1, true), "BtnPrimary", "Walk through the five setup steps again. Your current answers are pre-filled. A new profile name adds another server."),
                           U.Btn("Accounts (step 3)", () => w.ShowWizard(3, true), null, "Jump to Safe database accounts."),
                           U.Btn("Sandbox (step 5)", () => w.ShowWizard(5, true), null, "Jump to Practice sandbox."));
            rr.Margin = new Thickness(0, 14, 0, 0);
            prof.Children.Add(rr);
            sp.Children.Add(U.Card(prof));

            // ---- safety
            var safety = new StackPanel();
            safety.Children.Add(U.Row(U.H2("Safety"), new Border { Width = 10 }, U.Pill(st.AllowLive ? "Live unlocked" : "Live locked", st.AllowLive ? "BadBg" : "OkBg", st.AllowLive ? "Bad" : "Ok")));
            var live = new CheckBox { IsChecked = st.AllowLive, Margin = new Thickness(0, 6, 0, 6),
                Content = U.T("Allow applying to Live (" + (p?.WorldDb ?? "world database") + ")", 14, "Text", FontWeights.SemiBold, false),
                ToolTip = "When off, Apply can only target the practice sandbox. When on, the Changes screen lets you pick Live. Backups and the typed confirmation still apply either way." };
            U.AutomationName(live, "Allow applying to Live");
            live.Checked += (s, e) => { st.AllowLive = true; S.Profiles.SaveSettings(); w.RefreshChrome(); Build(); w.Toast("Live is unlocked. Apply can now target your real world database (after a backup and typing APPLY LIVE).", "warn"); };
            live.Unchecked += (s, e) => { st.AllowLive = false; S.Profiles.SaveSettings(); w.ApplyTarget = "Sandbox"; w.RefreshChrome(); Build(); w.Toast("Live is locked again. Apply targets the sandbox only.", "ok"); };
            safety.Children.Add(live);
            safety.Children.Add(U.T("Off by default. Practise in the sandbox first; switch this on when you're ready to change the real world. Players see live changes after the server reloads the tables (e.g. .reload commands) or restarts.", 12.5, "Muted", null, true, new Thickness(27, 0, 0, 12)));
            var always = new CheckBox { IsChecked = true, IsEnabled = false, Content = U.T("Back up affected tables before every Apply and Roll back", 13.5, "Text", null, false), ToolTip = "Always on, can't be turned off." };
            safety.Children.Add(always);
            safety.Children.Add(U.T("Always on. If the backup fails, nothing is applied.", 12.5, "Muted", null, true, new Thickness(27, 4, 0, 0)));
            sp.Children.Add(U.Card(safety));

            // ---- names
            var namesCard = new StackPanel();
            namesCard.Children.Add(U.H2("Names"));
            var tech = new CheckBox { IsChecked = st.ShowTechnicalNames, Margin = new Thickness(0, 6, 0, 4), Content = U.T("Show technical names", 14, "Text", FontWeights.SemiBold, false),
                ToolTip = "Also show the database column, table, DBC field and command behind each plain name." };
            U.AutomationName(tech, "Show technical names");
            tech.Checked += (s, e) => { st.ShowTechnicalNames = U.Technical = true; S.Profiles.SaveSettings(); w.Toast("Technical names are shown. Screens you open next show them.", "info"); };
            tech.Unchecked += (s, e) => { st.ShowTechnicalNames = U.Technical = false; S.Profiles.SaveSettings(); w.Toast("Only plain names are shown.", "info"); };
            namesCard.Children.Add(tech);
            namesCard.Children.Add(U.T("Off by default: every field has a plain name and a short description. Turn this on to also see the column (e.g. creature_template.minlevel), the DBC field, or the server command behind it.", 12.5, "Muted", null, true, new Thickness(27, 0, 0, 0)));
            sp.Children.Add(U.Card(namesCard));

            // ---- folders
            var fold = new StackPanel();
            fold.Children.Add(U.H2("Folders and tools"));
            fold.Children.Add(PathField("Changesets folder", "Where staged changes (pending.json) and the applied history are kept. Changing it starts from that folder's contents.", st.ChangesetsDir, true, v =>
            {
                st.ChangesetsDir = v; S.Profiles.SaveSettings(); S.ReloadChangesFolder(v);
            }));
            fold.Children.Add(PathField("Backup folder", "Each Apply and Roll back writes a timestamped subfolder of mysqldump files here.", st.BackupRoot, true, v => { st.BackupRoot = v; S.Profiles.SaveSettings(); }));
            fold.Children.Add(PathField("mysqldump.exe", "Used for the backup before Apply. It ships with MySQL Server, in its bin folder.", st.MysqldumpPath, false, v => { st.MysqldumpPath = v; S.Profiles.SaveSettings(); }));
            fold.Children.Add(U.CheckRow(File.Exists(st.MysqldumpPath) ? U.Check.Ok : U.Check.Bad, "mysqldump", File.Exists(st.MysqldumpPath) ? "found" : "not found: Apply stays disabled until this points to mysqldump.exe"));
            var size = U.Box(st.PageSize.ToString(), true);
            size.LostFocus += (s, e) => { if (int.TryParse(size.Text, out var n) && n >= 20 && n <= 5000) { st.PageSize = n; S.Profiles.SaveSettings(); } else size.Text = st.PageSize.ToString(); };
            var sf = U.Field("Rows per page", "How many rows the SQL browser shows at once (20 to 5000).", size, null, 200);
            sf.Margin = new Thickness(0, 14, 16, 0);
            var cid = U.Box(st.CustomIdStart.ToString(), true);
            cid.LostFocus += (s, e) => { if (long.TryParse(cid.Text, out var n) && n > 0 && n < 4000000000) { st.CustomIdStart = n; S.Profiles.SaveSettings(); } else cid.Text = st.CustomIdStart.ToString(); };
            var cf = U.Field("Custom ID range starts at", "\"Duplicate as new\" gives copies the first free ID at or above this number, well clear of the game's own IDs. Default 900000.", cid, null, 360);
            cf.Margin = new Thickness(0, 14, 0, 0);
            fold.Children.Add(U.Wrap(sf, cf));
            sp.Children.Add(U.Card(fold));

            // ---- keyboard
            var keys = new StackPanel();
            keys.Children.Add(U.H2("Keyboard"));
            foreach (var (k, d) in new[] { ("Ctrl+K", "Search creatures, items, quests, tables, screens and staged changes"), ("Alt+1 … Alt+9", "Jump to a sidebar item"),
                ("Ctrl+S", "Stage the edits in a Creatures / Items / Quests form"), ("Ctrl+D", "Duplicate the open creature / item / quest as new"), ("Ctrl+PgUp / PgDn", "Previous / next tab in an editor"), ("F5 / Ctrl+Enter", "Run the query editor"),
                ("Ctrl+Shift+R", "Review changes"), ("Ctrl+,", "Settings"), ("F2 / double-click", "Edit a cell (staged)"), ("Delete", "Discard the highlighted staged change"), ("Alt+← / Alt+→", "Back / next in setup") })
                keys.Children.Add(U.CheckRow(U.Check.Info, k, d));
            sp.Children.Add(U.Card(keys));

            host.Content = new Border { Padding = new Thickness(28, 22, 28, 20), Child = U.Scroll(sp) };
        }

        UIElement PathField(string label, string hint, string value, bool folder, Action<string> save)
        {
            var box = U.Box(value, true);
            box.LostFocus += (s, e) => { if (box.Text.Trim() != value && box.Text.Trim().Length > 0) save(box.Text.Trim()); };
            var b = U.Btn("Browse…", () =>
            {
                if (folder)
                {
                    var d = new OpenFolderDialog { Title = label };
                    if (Directory.Exists(box.Text)) d.InitialDirectory = box.Text;
                    if (d.ShowDialog(w) == true) { box.Text = d.FolderName; save(d.FolderName); Build(); }
                }
                else
                {
                    var d = new OpenFileDialog { Title = label, Filter = "mysqldump.exe|mysqldump.exe|Programs|*.exe" };
                    if (d.ShowDialog(w) == true) { box.Text = d.FileName; save(d.FileName); Build(); }
                }
            }, null, "Pick it with the file browser.");
            var row = new DockPanel(); b.Margin = new Thickness(10, 0, 0, 0); DockPanel.SetDock(b, Dock.Right); row.Children.Add(b); row.Children.Add(box);
            return U.Field(label, hint, row);
        }
    }
}
