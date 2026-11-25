using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using AndersCraftStudio.Core;

namespace AndersCraftStudio
{
    public interface IPage
    {
        UIElement View { get; }
        void OnShow();
    }

    public sealed class MainWindow : Window
    {
        public static MainWindow Instance;
        public static bool OnlyM7, OnlyM8, OnlyM9, OnlyM10, OnlyM11, OnlyM12, OnlyM13;
        public static bool Fresh;
        public readonly Studio S;
        readonly string screensDir;

        readonly Grid root = new Grid();
        readonly Grid shell = new Grid();
        readonly ContentControl pageHost = new ContentControl { Focusable = false };
        Button guideBtn;
        readonly ContentControl wizardHost = new ContentControl { Focusable = false, Visibility = Visibility.Collapsed };
        readonly Dictionary<string, IPage> pages = new Dictionary<string, IPage>();
        readonly Dictionary<string, RadioButton> nav = new Dictionary<string, RadioButton>();
        readonly Border toast = new Border();
        readonly TextBlock toastText = new TextBlock();
        readonly DispatcherTimer toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        readonly DispatcherTimer searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };

        TextBox search;
        Popup searchPopup;
        ListBox searchList;
        Border connBadge;
        TextBlock connText, targetText, changesBadgeText;
        Button reviewBtn;
        Border changesNavBadge;
        public string CurrentPage { get; private set; }
        /// <summary>The Server control tab's shared state (live status, GM login, schedule).</summary>
        public ServerHub Hub => hub ??= new ServerHub(this);
        ServerHub hub;
        /// <summary>True while the Server control tab (live operations) is showing; false on the Studio (staged editing) side.</summary>
        public bool ServerMode { get; private set; }
        StackPanel studioNav, serverNav;
        RadioButton modeStudio, modeServer;
        string lastStudioPage = "home", lastServerPage = "sc_dash";
        /// <summary>"Sandbox" or "Live". Chosen on the Changes screen; Live needs Settings › Safety.</summary>
        public string ApplyTarget { get; set; } = "Sandbox";

        public MainWindow(Studio s, string screens)
        {
            Instance = this; S = s; screensDir = screens;
            U.Technical = s.Profiles.Settings.ShowTechnicalNames;
            Title = "AndersCraft Studio";
            Background = U.R("Bg");
            FontFamily = U.Font;
            Foreground = U.R("Text");
            Width = 1440; Height = 900; MinWidth = 1100; MinHeight = 680;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
            if (App.Icon != null) Icon = App.Icon;
            WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 56, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });

            if (screens != null)
            {
                // render off-screen so screenshots never cover what you're doing
                WindowStartupLocation = WindowStartupLocation.Manual; Left = -20000; Top = -20000; ShowActivated = false; ShowInTaskbar = false;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                WindowState = WindowState.Maximized; // borderless, windowed full-screen
            }
            StateChanged += (a, b) => root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            if (WindowState == WindowState.Maximized) root.Margin = new Thickness(7);

            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(BuildTopBar());
            Grid.SetRow(shell, 1); root.Children.Add(shell);
            Grid.SetRow(wizardHost, 1); root.Children.Add(wizardHost);
            BuildShell();
            BuildToast();
            Content = root;

            S.ChangesChanged += RefreshChrome;
            S.ProfileChanged += RefreshChrome;
            RegisterKeys();
            Closed += (s, e) => { if (pages.TryGetValue("admin", out var ap) && ap is AdminPage adm) adm.Disconnect(); };   // the admin login never outlives the window
            Loaded += async (a, b) =>
            {
                if (S.Changes.RecoveryNote != null) { Log.Write("warn", S.Changes.RecoveryNote); Toast(S.Changes.RecoveryNote, "warn"); }
                RefreshChrome();
                if (S.Profile == null || !S.Profile.SetupComplete) ShowWizard(1); else Navigate("home");
                // scheduled server tasks and the crash watchdog run whenever Studio is open, not only on the Server control tab
                if (screensDir == null && File.Exists(Path.Combine(S.Profiles.Root, "server_control.json")) && (Hub.Cfg.Tasks.Any(t => t.Enabled) || Hub.Cfg.Realms.Any(r => r.AutoRestart))) Hub.Start();
                if (screensDir != null) await ScreensMode();
            };
        }

        // ================================================================== top bar
        UIElement BuildTopBar()
        {
            var bar = new Grid { Background = U.R("Panel") };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(232) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var line = new Border { BorderBrush = U.R("Line"), BorderThickness = new Thickness(0, 0, 0, 1) };
            Grid.SetColumnSpan(line, 5); bar.Children.Add(line);

            var brandName = U.T("AndersCraft", 15, "Text", FontWeights.Bold, false, new Thickness(10, 0, 0, 0)); brandName.Tag = "brand";
            var brand = U.Row(U.Crest(30), brandName, U.T(" Studio", 15, "Amber", FontWeights.Bold, false));
            brand.VerticalAlignment = VerticalAlignment.Center; brand.Margin = new Thickness(18, 0, 0, 0);
            bar.Children.Add(brand);

            // global search
            search = new TextBox { Width = 400, Height = 36, Padding = new Thickness(34, 0, 60, 0) };
            U.AutomationName(search, "Search");
            search.ToolTip = "Search screens, database tables and staged changes. Press Ctrl+K from anywhere, arrow keys to pick, Enter to open.";
            var sg = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            sg.Children.Add(U.WithPlaceholder(search, "Search tables, screens, changes…"));
            sg.Children.Add(new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 13, Foreground = U.R("Muted"), Margin = new Thickness(13, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false });
            sg.Children.Add(new Border { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Background = U.R("Panel2"), BorderBrush = U.R("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 1, 6, 2), IsHitTestVisible = false,
                Child = U.T("Ctrl K", 11.5, "Muted", null, false) });
            WindowChrome.SetIsHitTestVisibleInChrome(sg, true);
            // Studio (staged, safe editing) | Server control (live operations): two clearly separate tabs
            modeStudio = new RadioButton { Style = U.S("SegBtn"), Content = "Studio", GroupName = "mode", IsChecked = true, ToolTip = "Editing: everything is staged and reviewed before it reaches your server." };
            modeServer = new RadioButton { Style = U.S("SegBtn"), GroupName = "mode", ToolTip = "Server control: start, stop, players, accounts, messages. Acts on your running server right away.",
                Content = U.Row(new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = U.R("Bad"), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center }, new TextBlock { Text = "Server control" }) };
            modeStudio.Checked += (a, b) => SetMode(false); modeServer.Checked += (a, b) => SetMode(true);
            var modes = new Border { Background = U.R("Bg"), CornerRadius = new CornerRadius(10), Padding = new Thickness(2), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), Child = U.Row(modeStudio, modeServer) };
            WindowChrome.SetIsHitTestVisibleInChrome(modes, true);
            var midRow = U.Row(modes, sg); midRow.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(midRow, 1); bar.Children.Add(midRow);
            BuildSearchPopup(sg);

            // status cluster
            connText = U.T("", 12.5, "Ok", FontWeights.SemiBold, false);
            connBadge = new Border { CornerRadius = new CornerRadius(20), Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = U.Row(new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = U.R("Ok"), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center }, connText) };
            WindowChrome.SetIsHitTestVisibleInChrome(connBadge, true);
            targetText = U.T("", 12.5, "Muted", null, false, new Thickness(0, 0, 14, 0));
            targetText.VerticalAlignment = VerticalAlignment.Center;
            WindowChrome.SetIsHitTestVisibleInChrome(targetText, true);
            changesBadgeText = new TextBlock { FontWeight = FontWeights.Bold };
            reviewBtn = U.Btn("", () => Navigate("changes"), "BtnAmber", "Open the Changes screen to review, apply or discard what you've staged. (Ctrl+Shift+R)");
            reviewBtn.Padding = new Thickness(14, 7, 14, 7);
            reviewBtn.VerticalAlignment = VerticalAlignment.Center;
            reviewBtn.Content = changesBadgeText;
            WindowChrome.SetIsHitTestVisibleInChrome(reviewBtn, true);
            // "How to use this": the visual guide for the screen you're on (Shift+F1)
            guideBtn = U.Btn("", () => GuideWindow.ShowFor(this, CurrentPage), "BtnGhost", "How to use this screen: a picture with numbered steps. (Shift+F1)");
            guideBtn.Content = U.Row(new TextBlock { Text = "\uE897", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14, Foreground = U.R("Amber"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) }, U.T("How to use this", 12.5, "Text", FontWeights.SemiBold, false));
            guideBtn.VerticalAlignment = VerticalAlignment.Center; guideBtn.Margin = new Thickness(0, 0, 12, 0);
            U.AutomationName(guideBtn, "How to use this screen");
            WindowChrome.SetIsHitTestVisibleInChrome(guideBtn, true);
            var status = U.Row(guideBtn, connBadge, targetText, reviewBtn);
            status.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(status, 3); bar.Children.Add(status);

            // window buttons
            Button WinBtn(string glyph, string tip, Action a)
            {
                var b = U.Btn(glyph, a, "BtnGhost", tip);
                b.Width = 44; b.Height = 36; b.Padding = new Thickness(0); b.HorizontalContentAlignment = HorizontalAlignment.Center; b.Margin = new Thickness(0);
                b.FontFamily = new FontFamily("Segoe MDL2 Assets"); b.FontSize = 11;
                WindowChrome.SetIsHitTestVisibleInChrome(b, true);
                return b;
            }
            var wb = U.Row(
                WinBtn("", "Minimise", () => WindowState = WindowState.Minimized),
                WinBtn("", "Maximise / restore", () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized),
                WinBtn("", "Close (Alt+F4)", Close));
            wb.Margin = new Thickness(4, 0, 8, 0); wb.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(wb, 4); bar.Children.Add(wb);
            return bar;
        }

        public void RefreshChrome()
        {
            var p = S.Profile;
            var dot = (System.Windows.Shapes.Ellipse)((StackPanel)connBadge.Child).Children[0];
            if (p == null || !S.HasReadAccount)
            {
                connBadge.Background = U.R("AmberDim"); connText.Foreground = U.R("Amber"); dot.Fill = U.R("Amber");
                connText.Text = "Setup needed";
                connBadge.ToolTip = "Studio has no verified read-only database account yet. Settings › Run setup again, step 3.";
            }
            else
            {
                connBadge.Background = U.R("OkBg"); connText.Foreground = U.R("Ok"); dot.Fill = U.R("Ok");
                connText.Text = U.Technical ? $"{p.WorldDb} · read-only" : "World database · read-only";
                connBadge.ToolTip = $"Browsing uses the {p.RoUser} account on {p.Host}:{p.Port}. It can only read, so nothing you click here can change the database.";
            }
            if (ApplyTarget == "Live" && !S.Profiles.Settings.AllowLive) ApplyTarget = "Sandbox";
            var db = p == null ? "" : ApplyTarget == "Live" ? p.WorldDb : p.SandboxDb;
            targetText.Text = $"Apply target: {ApplyTarget}";
            targetText.ToolTip = $"Apply writes to {ApplyTarget}" + (db.Length > 0 ? $" ({db})" : "") + ". The practice sandbox is the default; Live (your real world database) must be switched on in Settings › Safety first.";
            targetText.Foreground = ApplyTarget == "Live" ? U.R("Bad") : U.R("Muted");
            int n = S.StagedCount;
            changesBadgeText.Text = n == 1 ? "1 staged change · Review" : $"{n} staged changes · Review";
            reviewBtn.Opacity = n == 0 ? 0.55 : 1;
            if (changesNavBadge != null)
            {
                ((TextBlock)changesNavBadge.Child).Text = n.ToString();
                changesNavBadge.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        // ================================================================== sidebar + pages
        void BuildShell()
        {
            shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(232) });
            shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var side = new DockPanel { Background = U.R("Panel"), LastChildFill = false };
            var sideBorder = new Border { BorderBrush = U.R("Line"), BorderThickness = new Thickness(0, 0, 1, 0), Child = side };
            shell.Children.Add(sideBorder);
            var list = studioNav = new StackPanel { Margin = new Thickness(12, 14, 12, 0) };
            serverNav = new StackPanel { Margin = new Thickness(12, 14, 12, 0), Visibility = Visibility.Collapsed };
            var lists = new Grid(); lists.Children.Add(list); lists.Children.Add(serverNav);
            var listScroll = new ScrollViewer { Content = lists, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
            DockPanel.SetDock(listScroll, Dock.Top);
            side.Children.Add(listScroll);

            int shortcut = 1;
            void Group(string title) => list.Children.Add(U.T(title.ToUpperInvariant(), 11, "Faint", FontWeights.Bold, false, new Thickness(12, list.Children.Count == 0 ? 0 : 14, 0, 4)));
            void Item(string key, string glyph, string text, string tip, UIElement extra = null)
            {
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
                var t = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(t, 1); g.Children.Add(t);
                if (extra != null) { Grid.SetColumn(extra, 2); g.Children.Add(extra); }
                int sc = shortcut++;
                var rb = new RadioButton { Style = U.S("NavBtn"), Content = g, GroupName = "nav", ToolTip = tip + (sc <= 9 ? $"  (Alt+{sc})" : "") };
                U.AutomationName(rb, text);
                rb.Checked += (s, e) => { if (CurrentPage != key) Navigate(key); };
                nav[key] = rb;
                list.Children.Add(rb);
            }
            Item("home", "\uE80F", "Home", "Your server at a glance, recent changes and quick links.");
            Group("World");
            Item("creatures", "", "Creatures", "NPCs and monsters: stats, spawns, equipment, vendor, loot and AI.");
            Item("items", "", "Items", "Weapons, armour and everything else in bags.");
            Item("quests", "", "Quests", "Quest text, objectives, rewards and chains.");
            Item("gameobjects", "\uE7B8", "Game objects", "Chests, doors, herbs, signs, portals: type-aware settings, spawns and loot.");
            Item("npctext", "\uE8BD", "NPC text", "What NPCs say when their gossip window opens: up to 8 variants with chances and emotes.");
            Item("pagetext", "\uE736", "Page text", "Books, letters, plaques and signs: pages and how they chain.");
            Item("broadcast", "\uE789", "Broadcast text", "Shared text lines (with sound and emotes) that creature_text, gossip and quests point at.");
            Item("tele", "\uE707", "Teleport locations", "game_tele: the places .tele takes GMs and players."); 
            Item("loot", "", "Loot", "Who drops what, how often, in which groups and shared lists, with a drop simulator.");
            Item("world", "\uE909", "World tables", "Conditions, waypoints, gossip menus, game events and area triggers.");
            Item("templates", "\uE8A5", "Templates", "Ready-made actions: clone a boss, make a vendor, new quest, scale a creature, add a combat spell.");
            Group("Scripting");
            Item("smartai", "", "SmartAI", "Build NPC and object behaviour as readable Event → Action → Target sentences (smart_scripts).");
            Group("Client data");
            Item("spells", "", "Spells", "Spell.dbc as a form with a live tooltip: effects, range, cast time, cost, duration, flags, icon, visuals and the server-side spell_dbc row.");
            Item("dbc", "", "DBC tables", "The game client's 246 data tables: read, edit, find/replace, compare, export, and stage row patches.");
            Item("patches", "", "Client patches", "Build MPQ patches for the game client from DBCs and other files, see which patch wins for each file, install with a backup, uninstall, and export a zip for players.");
            Item("assets", "\uE8B9", "Asset import", "Bring in your own icons, models, sounds and other files: converted, placed in a patch work folder and registered in the DBCs.");
            Group("Database");
            Item("admin", "\uE7EF", "Admin console", "Run any SQL directly, manage databases, tables, users and connections, import/export, and set up AzerothCore's databases. Needs an admin login each time.");
            Item("sql", "", "SQL browser", "Browse every table, filter, export, and run read-only queries. Edits are staged, never written directly.");
            Group("Safety");
            changesNavBadge = new Border { Background = U.R("Amber"), CornerRadius = new CornerRadius(10), Padding = new Thickness(7, 1, 7, 1), Visibility = Visibility.Collapsed,
                Child = new TextBlock { Foreground = U.R("AmberInk"), FontSize = 11.5, FontWeight = FontWeights.Bold } };
            Item("changes", "", "Changes", "Everything you've staged: review it, apply it to the sandbox or live, and roll back past applies.", changesNavBadge);


            // ---- Server control (live)
            list = serverNav;
            var live = new Border { Background = U.R("BadBg"), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 10),
                Child = U.Stack(U.T("LIVE SERVER CONTROL", 11, "Bad", FontWeights.Bold, false), U.T("Acts on your running server right away. Nothing here is staged.", 11.5, "Muted", null, true, new Thickness(0, 3, 0, 0))) };
            list.Children.Add(live);
            Item("sc_dash", "\uE7F4", "Dashboard", "What's running, who's online; start, stop and restart.");
            Item("sc_players", "\uE716", "Players", "Every character: kick, mute, ban, teleport, level, mail gold and items, one or many at once.");
            Item("sc_accounts", "\uE77B", "Accounts", "Make logins, change passwords, staff rank and expansion, bans.");
            Item("sc_console", "\uE756", "Console", "Type any server command, or pick a quick command.");
            Item("sc_logs", "\uE81C", "Logs", "Live server logs with problems in colour, and the audit log.");
            Item("sc_schedule", "\uE823", "Messages & schedule", "Message everyone, shutdown countdowns, repeating messages, daily restarts and backups.");
            Item("sc_backups", "\uE8F1", "Backups", "Back up now, see every backup, put a database back.");
            Item("sc_settings", "\uE713", "Settings", "GM login, folders, ports and realms; import from the old Control Panel.");
            list = studioNav;

            var bottom = new StackPanel { Margin = new Thickness(12, 0, 12, 14) };
            DockPanel.SetDock(bottom, Dock.Bottom);
            side.Children.Insert(0, bottom);
            var setG = new Grid();
            setG.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            setG.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            setG.Children.Add(new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            var st = new TextBlock { Text = "Settings", VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(st, 1); setG.Children.Add(st);
            var setRb = new RadioButton { Style = U.S("NavBtn"), Content = setG, GroupName = "nav", ToolTip = "Server profile, folders, the Live safety switch, and the setup wizard.  (Ctrl+,)" };
            setRb.Checked += (s, e) => { if (CurrentPage != "settings") Navigate("settings"); };
            nav["settings"] = setRb;
            var helpG = new Grid();
            helpG.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            helpG.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            helpG.Children.Add(new TextBlock { Text = "\uE897", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            var ht = new TextBlock { Text = "Help", VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(ht, 1); helpG.Children.Add(ht);
            var helpRb = new RadioButton { Style = U.S("NavBtn"), Content = helpG, GroupName = "nav", ToolTip = "Getting started, the safety model, a glossary and the keys.  (F1)" };
            helpRb.Checked += (s, e) => { if (CurrentPage != "help") Navigate("help"); };
            nav["help"] = helpRb;
            bottom.Children.Add(helpRb);
            bottom.Children.Add(setRb);
            var about = new TextBlock { Text = "AndersCraft Studio " + AppInfo.Version + " · About", FontSize = 11, Foreground = U.R("Faint"), Margin = new Thickness(12, 8, 0, 0), Cursor = Cursors.Hand, ToolTip = "Version, licence and credits" };
            about.MouseLeftButtonUp += (s, e) => AboutWindow.ShowAbout(this);
            bottom.Children.Add(about);

            Grid.SetColumn(pageHost, 1);
            shell.Children.Add(pageHost);
        }

        public void Navigate(string key, Action<IPage> then = null)
        {
            wizardHost.Visibility = Visibility.Collapsed; shell.Visibility = Visibility.Visible;
            if (!pages.TryGetValue(key, out var page))
            {
                bool needsDb = key is "creatures" or "items" or "quests" or "gameobjects" or "npctext" or "pagetext" or "broadcast" or "tele" or "loot" or "smartai" or "templates" or "world";
                if (needsDb && !S.HasReadAccount)
                {
                    var titles = new Dictionary<string, string> { ["creatures"] = "Creatures", ["items"] = "Items", ["quests"] = "Quests", ["gameobjects"] = "Game objects", ["npctext"] = "NPC text", ["pagetext"] = "Page text", ["broadcast"] = "Broadcast text", ["tele"] = "Teleport locations", ["loot"] = "Loot", ["smartai"] = "SmartAI", ["templates"] = "Templates", ["world"] = "World tables" };
                    var np = new NotSetUpPage(this, titles[key], $"{titles[key]} reads your world database, so it needs the read-only account from setup step 3.");
                    CurrentPage = key;
                    if (nav.TryGetValue(key, out var nrb) && nrb.IsChecked != true) nrb.IsChecked = true;
                    pageHost.Content = np.View;
                    return;
                }
                page = key switch
                {
                    "home" => new HomePage(this),
                    "templates" => new TemplatesPage(this),
                    "world" => new WorldTablesPage(this),
                    "help" => new HelpPage(this),
                    "assets" => new AssetsPage(this),
                    "sql" => new SqlBrowserPage(this),
                    "admin" => new AdminPage(this),
                    "creatures" => new EntityEditorPage(this, EntityDefs.Creature),
                    "items" => new EntityEditorPage(this, EntityDefs.Item),
                    "quests" => new EntityEditorPage(this, EntityDefs.Quest),
                    "gameobjects" => new EntityEditorPage(this, EntityDefs.GameObject),
                    "npctext" => new EntityEditorPage(this, EntityDefs.NpcText),
                    "pagetext" => new EntityEditorPage(this, EntityDefs.PageText),
                    "broadcast" => new EntityEditorPage(this, EntityDefs.BroadcastText),
                    "tele" => new EntityEditorPage(this, EntityDefs.GameTele),
                    "patches" => new ClientPatchesPage(this),
                    "loot" => new LootPage(this),
                    "smartai" => new SmartAIPage(this),
                    "dbc" => new DbcPage(this),
                    "spells" => new SpellEditorPage(this),
                    "changes" => new ChangesPage(this),
                    "settings" => new SettingsPage(this),
                    "sc_dash" => new ScDashboardPage(this),
                    "sc_players" => new ScPlayersPage(this),
                    "sc_accounts" => new ScAccountsPage(this),
                    "sc_console" => new ScConsolePage(this),
                    "sc_logs" => new ScLogsPage(this),
                    "sc_schedule" => new ScSchedulePage(this),
                    "sc_backups" => new ScBackupsPage(this),
                    "sc_settings" => new ScSettingsPage(this),
                    _ => new ComingSoonPage(this, key),
                };
                pages[key] = page;
            }
            CurrentPage = key;
            bool sc = key.StartsWith("sc_");
            if (sc != ServerMode) { ServerMode = sc; (sc ? modeServer : modeStudio).IsChecked = true; studioNav.Visibility = sc ? Visibility.Collapsed : Visibility.Visible; serverNav.Visibility = sc ? Visibility.Visible : Visibility.Collapsed; }
            if (sc) lastServerPage = key; else if (key != "wizard") lastStudioPage = key;
            if (guideBtn != null) guideBtn.Visibility = Guides.For(key) != null ? Visibility.Visible : Visibility.Collapsed;
            if (nav.TryGetValue(key, out var rb) && rb.IsChecked != true) rb.IsChecked = true;
            pageHost.Content = page.View;
            page.OnShow();
            then?.Invoke(page);
        }

        void SetMode(bool server)
        {
            if (server == ServerMode && CurrentPage != null) return;
            Navigate(server ? lastServerPage : lastStudioPage);
        }

        /// <summary>Rebuilds the Server control pages (after its settings changed).</summary>
        public void ReloadServerControl()
        {
            foreach (var k in pages.Keys.Where(k => k.StartsWith("sc_")).ToList()) pages.Remove(k);
            if (ServerMode) Navigate(CurrentPage);
        }

        public void ShowWizard(int step, bool rerun = false)
        {
            shell.Visibility = Visibility.Collapsed;
            wizardHost.Content = new WizardView(this, step, rerun);
            wizardHost.Visibility = Visibility.Visible;
            CurrentPage = "wizard";
        }

        public void WizardDone()
        {
            pages.Clear();
            RefreshChrome();
            Navigate("home");
        }

        // ================================================================== search
        sealed class Hit { public string Title, Detail, Kind; public Action Go; }

        void BuildSearchPopup(FrameworkElement anchor)
        {
            searchList = new ListBox { MaxHeight = 420, Width = 520 };
            searchPopup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, VerticalOffset = 6,
                Child = new Border { Background = U.R("Panel2"), BorderBrush = U.R("LineHi"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(6), Child = searchList,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.5 } } };
            searchDebounce.Tick += (s, e) => { searchDebounce.Stop(); UpdateSearch(); };
            search.TextChanged += (s, e) => { searchDebounce.Stop(); searchDebounce.Start(); };
            search.GotKeyboardFocus += (s, e) => UpdateSearch();
            search.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Down && searchList.Items.Count > 0) { searchList.SelectedIndex = Math.Min(searchList.Items.Count - 1, searchList.SelectedIndex + 1); e.Handled = true; }
                else if (e.Key == Key.Up && searchList.Items.Count > 0) { searchList.SelectedIndex = Math.Max(0, searchList.SelectedIndex - 1); e.Handled = true; }
                else if (e.Key == Key.Enter) { OpenHit(searchList.SelectedItem as ListBoxItem ?? searchList.Items.OfType<ListBoxItem>().FirstOrDefault()); e.Handled = true; }
                else if (e.Key == Key.Escape) { searchPopup.IsOpen = false; search.Text = ""; Keyboard.ClearFocus(); FocusManager.SetFocusedElement(this, this); e.Handled = true; }
            };
            searchList.MouseLeftButtonUp += (s, e) => OpenHit(searchList.SelectedItem as ListBoxItem);
        }

        void OpenHit(ListBoxItem item)
        {
            if (item?.Tag is Hit h) { searchPopup.IsOpen = false; search.Text = ""; h.Go(); }
        }

        IEnumerable<Hit> SearchHits(string q)
        {
            q = q.Trim();
            var screens = new (string key, string name, string what)[]
            {
                ("sql", "SQL browser", "Browse tables and run read-only queries"), ("admin", "Admin console", "Direct SQL, databases, users, import/export, server setup"), ("changes", "Changes", "Review, apply and roll back"), ("settings", "Settings", "Profile, folders, Live switch"),
                ("creatures", "Creatures", "Creature editor"), ("items", "Items", "Item editor"), ("quests", "Quests", "Quest editor"), ("gameobjects", "Game objects", "Chests, doors, herbs, signs"), ("npctext", "NPC text", "Gossip greetings"), ("pagetext", "Page text", "Books and letters"), ("broadcast", "Broadcast text", "Shared text lines"), ("tele", "Teleport locations", ".tele places"), ("loot", "Loot", "Loot editor and drop simulator"),
                ("smartai", "SmartAI", "SmartAI builder"), ("spells", "Spells", "Spell editor with live tooltip"), ("dbc", "DBC tables", "All 246 client tables"), ("home", "Home", "Dashboard"), ("templates", "Templates", "Ready-made actions"), ("world", "World tables", "Conditions, waypoints, gossip, events, area triggers"), ("assets", "Asset import", "Icons, models, sounds"), ("help", "Help", "Getting started and glossary"), ("patches", "Client patches", "Build, install and inspect MPQ patches"),
            };
            foreach (var sc in screens)
                if (q.Length == 0 || sc.name.Contains(q, StringComparison.OrdinalIgnoreCase))
                    yield return new Hit { Kind = "Screen", Title = sc.name, Detail = sc.what, Go = () => Navigate(sc.key) };
            if (q.Length == 0) yield break;
            if ("run setup again wizard".Contains(q, StringComparison.OrdinalIgnoreCase))
                yield return new Hit { Kind = "Action", Title = "Run setup again", Detail = "Open the first-run wizard", Go = () => ShowWizard(1, true) };
            var tables = new HashSet<string>(TableCatalog.KnownTables, StringComparer.OrdinalIgnoreCase);
            if (pages.TryGetValue("sql", out var p) && p is SqlBrowserPage sp) foreach (var t in sp.LoadedTables) tables.Add(t);
            foreach (var t in tables.Where(t => t.Contains(q, StringComparison.OrdinalIgnoreCase) || (TableCatalog.Describe(t) ?? "").Contains(q, StringComparison.OrdinalIgnoreCase))
                                    .OrderBy(t => t.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(t => t.Length).Take(12))
            {
                var name = t;
                yield return new Hit { Kind = "Table", Title = name, Detail = TableCatalog.Describe(name) ?? "Database table", Go = () => Navigate("sql", pg => ((SqlBrowserPage)pg).OpenTable(null, name)) };
            }
            // spells by name or ID (server Spell.dbc)
            if (q.Length >= 3 || long.TryParse(q.TrimStart('#'), out _))
                foreach (var (sid, sname) in S.Dbc.Search("spell", q.TrimStart('#'), 6))
                {
                    var spellId = (uint)sid;
                    yield return new Hit { Kind = "Spell", Title = $"{sname}  #{sid}", Detail = "Spell editor", Go = () => Navigate("spells", pg => ((SpellEditorPage)pg).Open(spellId)) };
                }
            // client data tables
            foreach (var f in DbcLayout.DescribedFiles.Where(f => f.Contains(q, StringComparison.OrdinalIgnoreCase) || DbcLayout.Describe(f).Contains(q, StringComparison.OrdinalIgnoreCase))
                                    .OrderBy(f => f.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(f => f.Length).Take(8))
            {
                var name = f;
                yield return new Hit { Kind = "DBC", Title = name, Detail = DbcLayout.Describe(name), Go = () => Navigate("dbc", pg => ((DbcPage)pg).OpenTable(name)) };
            }
            // creatures, items and quests by name or ID (read-only account)
            if (S.HasReadAccount && (q.Length >= 2 || long.TryParse(q.TrimStart('#'), out _)))
            {
                var found = new List<Hit>();
                try
                {
                    using var db = S.OpenRead(S.Profile.WorldDb);
                    bool num = long.TryParse(q.TrimStart('#'), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n);
                    foreach (var def in EntityDefs.All)
                    {
                        var k = SqlGen.Ident(def.KeyCol); var nm = SqlGen.Ident(def.NameCol);
                        var where = num ? $"{k} = {n}" : $"{nm} LIKE {Refs.LikeLiteral(q)}";
                        var t = db.Query($"SELECT {k}, {nm}, {def.ListExtra} FROM {SqlGen.Ident(def.Table)} WHERE {where} ORDER BY LENGTH({nm}), {k} LIMIT 5", 5);
                        foreach (var r in t.Rows)
                        {
                            var eid = Convert.ToInt64(r[0]); var ws = def.Key;
                            var kindName = def.Key switch { "gameobjects" => "Object", "npctext" => "NPC text", "pagetext" => "Page", "broadcast" => "Text", "tele" => "Tele", _ => def.Singular.Substring(0, 1).ToUpperInvariant() + def.Singular.Substring(1) };
                            found.Add(new Hit { Kind = kindName, Title = $"{Refs.OneLine(Convert.ToString(r[1]), 60)}  #{eid}", Detail = Convert.ToString(r[2]), Go = () => Navigate(ws, pg => ((EntityEditorPage)pg).Open(eid)) });
                        }
                    }
                }
                catch { }
                foreach (var h in found) yield return h;
            }
            foreach (var c in S.Changes.Pending.Changes.Where(c => (c.Summary ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)).Take(6))
            {
                var id = c.Id;
                yield return new Hit { Kind = "Staged", Title = c.Summary, Detail = c.Category, Go = () => Navigate("changes", pg => ((ChangesPage)pg).Select(id)) };
            }
        }

        void UpdateSearch()
        {
            searchList.Items.Clear();
            foreach (var h in SearchHits(search.Text).Take(20))
            {
                var row = new DockPanel();
                var tag = U.Pill(h.Kind, h.Kind == "Table" ? "MaroonDim" : h.Kind == "Staged" ? "AmberDim" : "Panel", h.Kind == "Staged" ? "Amber" : "Muted");
                tag.Width = 64; tag.Margin = new Thickness(0, 0, 10, 0);
                DockPanel.SetDock(tag, Dock.Left); row.Children.Add(tag);
                row.Children.Add(U.Stack(U.T(h.Title, 13.5, "Text", FontWeights.SemiBold, false), U.T(h.Detail, 12, "Muted", null, false)));
                searchList.Items.Add(new ListBoxItem { Content = row, Tag = h });
            }
            if (searchList.Items.Count > 0) searchList.SelectedIndex = 0;
            searchPopup.IsOpen = search.IsKeyboardFocused && searchList.Items.Count > 0;
        }

        // ================================================================== keys
        void RegisterKeys()
        {
            PreviewKeyDown += (s, e) =>
            {
                var k = e.Key == Key.System ? e.SystemKey : e.Key;
                var m = Keyboard.Modifiers;
                if (m == ModifierKeys.Control && k == Key.K) { if (CurrentPage != "wizard") { search.Focus(); search.SelectAll(); } e.Handled = true; }
                else if (m == (ModifierKeys.Control | ModifierKeys.Shift) && k == Key.R && CurrentPage != "wizard") { Navigate("changes"); e.Handled = true; }
                else if (m == ModifierKeys.Control && k == Key.OemComma && CurrentPage != "wizard") { Navigate("settings"); e.Handled = true; }
                else if (m == ModifierKeys.None && k == Key.F1 && CurrentPage != "wizard") { Navigate("help"); e.Handled = true; }
                else if (m == ModifierKeys.Shift && k == Key.F1) { GuideWindow.ShowFor(this, CurrentPage); e.Handled = true; }
                else if (m == ModifierKeys.Alt && k >= Key.D1 && k <= Key.D9 && CurrentPage != "wizard")
                {
                    var keys = new[] { "home", "creatures", "items", "quests", "loot", "smartai", "spells", "dbc", "sql" };
                    Navigate(keys[k - Key.D1]); e.Handled = true;
                }
            };
        }

        // ================================================================== toast
        void BuildToast()
        {
            toastText.TextWrapping = TextWrapping.Wrap; toastText.FontSize = 13.5; toastText.MaxWidth = 460;
            toast.Child = toastText;
            toast.CornerRadius = new CornerRadius(12); toast.Padding = new Thickness(16, 12, 16, 12);
            toast.HorizontalAlignment = HorizontalAlignment.Right; toast.VerticalAlignment = VerticalAlignment.Bottom; toast.Margin = new Thickness(0, 0, 24, 24);
            toast.BorderThickness = new Thickness(1);
            toast.Visibility = Visibility.Collapsed;
            toast.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.5 };
            toast.MouseLeftButtonUp += (s, e) => toast.Visibility = Visibility.Collapsed;
            toast.ToolTip = "Click to dismiss";
            Grid.SetRowSpan(toast, 2);
            root.Children.Add(toast);
            toastTimer.Tick += (s, e) => { toast.Visibility = Visibility.Collapsed; toastTimer.Stop(); };
        }

        static void LogToast(string text, string kind) { if (kind == "bad" || kind == "warn") Log.Write(kind == "bad" ? "error" : "warn", text); }

        public void HideToast() { toast.Visibility = Visibility.Collapsed; toastTimer.Stop(); }

        public void Toast(string text, string kind = "info")
        {
            LogToast(text, kind);
            toastText.Text = text;
            toast.Background = U.R(kind == "bad" ? "BadBg" : kind == "ok" ? "OkBg" : kind == "warn" ? "AmberDim" : "Panel2");
            toast.BorderBrush = U.R(kind == "bad" ? "Bad" : kind == "ok" ? "Ok" : kind == "warn" ? "Amber" : "LineHi");
            toastText.Foreground = U.R("Text");
            toast.Visibility = Visibility.Visible;
            toastTimer.Stop(); toastTimer.Interval = TimeSpan.FromSeconds(kind == "bad" ? 10 : 6); toastTimer.Start();
        }

        // ================================================================== screenshot mode
        public async Task Snap(string file)
        {
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(250);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            UpdateLayout();
            Render(file, null, default);
        }

        /// <summary>--public screenshots: text replaced on screen before rendering (private paths, account names…).</summary>
        public static List<(string from, string to)> Redactions;

        public static void Redact(DependencyObject root)
        {
            if (Redactions == null || root == null) return;
            string Fix(string t)
            {
                if (string.IsNullOrEmpty(t)) return t;
                foreach (var (a, b) in Redactions)
                {
                    if (a.StartsWith("=")) { if (t == a.Substring(1)) t = b; continue; }
                    t = t.Replace(a, b, StringComparison.OrdinalIgnoreCase);
                }
                return t;
            }
            void Walk(DependencyObject d)
            {
                if (d is TextBlock tb && !"brand".Equals(tb.Tag)) { var t = tb.Text; var f = Fix(t); if (f != t) tb.Text = f; }
                // TextBoxes are left alone: changing their text would fire edit handlers. Public shots avoid private text in boxes.
                int n = 0;
                try { n = VisualTreeHelper.GetChildrenCount(d); } catch { }
                for (int i = 0; i < n; i++) Walk(VisualTreeHelper.GetChild(d, i));
            }
            Walk(root);
        }

        void Render(string file, FrameworkElement overlay, Point overlayAt)
        {
            Redact(this); if (overlay != null) Redact(overlay);
            UpdateLayout();
            var el = (FrameworkElement)Content;
            int w = (int)el.ActualWidth, h = (int)el.ActualHeight;
            var rtb = new RenderTargetBitmap(w * 2, h * 2, 192, 192, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(U.R("Bg"), null, new Rect(0, 0, w, h));
                dc.DrawRectangle(new VisualBrush(el), null, new Rect(0, 0, w, h));
                if (overlay != null) dc.DrawRectangle(new VisualBrush(overlay), null, new Rect(overlayAt, overlay.RenderSize));
            }
            rtb.Render(dv);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(Path.Combine(screensDir, file));
            enc.Save(fs);
        }

        /// <summary>Screenshot of the search dropdown. The popup is drawn in place instead of opened, so nothing appears on the real screen.</summary>
        public async Task SnapSearch(string file, string text)
        {
            search.Text = text;
            UpdateSearch();
            searchPopup.IsOpen = false;
            var child = (FrameworkElement)searchPopup.Child;
            searchPopup.Child = null;
            child.Measure(new Size(560, 600)); child.Arrange(new Rect(child.DesiredSize)); child.UpdateLayout();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var anchor = (FrameworkElement)searchPopup.PlacementTarget;
            var pos = anchor.TranslatePoint(new Point(0, anchor.ActualHeight + 6), (FrameworkElement)Content);
            UpdateLayout();
            Render(file, child, pos);
            searchPopup.Child = child;
            search.Text = "";
        }

        /// <summary>Screenshot of a dialog window (shown off-screen next to this one).</summary>
        public async Task SnapWindow(Window win, string file)
        {
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(400);
            var el = (FrameworkElement)win.Content;
            Redact(el);
            el.UpdateLayout();
            int w = (int)el.ActualWidth, h = (int)el.ActualHeight;
            var rtb = new RenderTargetBitmap(w * 2, h * 2, 192, 192, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen()) { dc.DrawRectangle(U.R("Panel"), null, new Rect(0, 0, w, h)); dc.DrawRectangle(new VisualBrush(el), null, new Rect(0, 0, w, h)); }
            rtb.Render(dv);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(Path.Combine(screensDir, file));
            enc.Save(fs);
        }

        async Task ScreensMode()
        {
            try { if (Fresh) await Screens.RunFresh(this, screensDir); else if (OnlyM7) await Screens.RunM7(this, screensDir); else if (OnlyM8) await Screens.RunM8(this, screensDir); else if (OnlyM9) await Screens.RunM9(this, screensDir); else if (OnlyM10) await Screens.RunM10(this, screensDir); else if (OnlyM11) await Screens.RunM11(this, screensDir); else if (OnlyM12) await Screens.RunM12(this, screensDir); else if (OnlyM13) await Screens.RunM13(this, screensDir); else await Screens.Run(this, screensDir); }
            catch (Exception ex) { File.WriteAllText(Path.Combine(screensDir, "screens_error.txt"), ex.ToString()); }
            Close();
        }

        public IPage GetPage(string key) => pages.TryGetValue(key, out var p) ? p : null;
        public WizardView Wizard => wizardHost.Content as WizardView;
        public bool IsScreens => screensDir != null;
    }
}
