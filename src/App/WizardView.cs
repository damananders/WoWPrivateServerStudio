using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AndersCraftStudio.Core;
using Microsoft.Win32;

namespace AndersCraftStudio
{
    /// <summary>First-run setup: five steps that end with a saved server profile.</summary>
    public sealed class WizardView : Grid
    {
        readonly MainWindow w;
        readonly Profile p;
        readonly bool rerun;
        int step;
        readonly StackPanel rail = new StackPanel();
        readonly ContentControl body = new ContentControl { Focusable = false };
        readonly Button back, next;
        readonly TextBlock footNote = U.T("", 12.5, "Muted", null, true);

        // step 2 state
        ServerConf conf;
        string confError;
        bool? reachable;
        List<string> detected;
        // step 3 state
        string roPw, rwPw;
        bool reveal;
        List<Border> accountChecks;
        // step 5 state
        bool? sandboxFound;
        string sandboxScript;

        static readonly string[] Titles = { "Welcome", "Find your server", "Safe database accounts", "Game client folder", "Practice sandbox" };
        static readonly string[] Subs = { "What Studio does", "Read worldserver.conf", "Read-only + apply accounts", "Optional: icons, DBCs, patches", "Practise applying safely" };

        public WizardView(MainWindow w, int startStep, bool rerun)
        {
            this.w = w; this.rerun = rerun;
            var existing = w.S.Profile;
            p = existing != null ? Clone(existing) : new Profile();
            roPw = w.S.Secrets.Unprotect(p.RoPassEnc); if (string.IsNullOrEmpty(roPw)) roPw = Passwords.Generate();
            rwPw = w.S.Secrets.Unprotect(p.RwPassEnc); if (string.IsNullOrEmpty(rwPw)) rwPw = Passwords.Generate();
            if (!string.IsNullOrEmpty(p.ConfPath) && File.Exists(p.ConfPath)) LoadConf(p.ConfPath, false);

            Background = U.R("Bg");
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = new Border { Background = U.R("Panel"), BorderBrush = U.R("Line"), BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(26, 30, 22, 20) };
            var ls = new DockPanel();
            var head = U.Stack(U.Eyebrow(rerun ? "Setup · run again" : "First-run setup"), U.T("Let's connect Studio to your server", 19, "Text", FontWeights.SemiBold, true, new Thickness(0, 0, 0, 22)));
            DockPanel.SetDock(head, Dock.Top); ls.Children.Add(head);
            var help = U.T("You can change any of this later in Settings › Run setup again. Nothing here writes to your database.", 12.5, "Muted");
            DockPanel.SetDock(help, Dock.Bottom); ls.Children.Add(help);
            ls.Children.Add(rail);
            left.Child = ls;
            Children.Add(left);

            var right = new DockPanel { Margin = new Thickness(56, 36, 56, 24) };
            Grid.SetColumn(right, 1);
            var foot = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
            DockPanel.SetDock(foot, Dock.Bottom);
            back = U.Btn("Back", () => Go(step - 1), null, "Previous step (Alt+Left)", 110);
            next = U.Btn("Next", NextClicked, "BtnPrimary", "Next step (Alt+Right). Your progress is kept.", 150);
            next.Margin = new Thickness(0);
            var btns = U.Row(back, next);
            if (rerun) btns.Children.Insert(0, U.Btn("Cancel", () => w.Navigate("settings"), "BtnGhost", "Leave setup without saving changes"));
            DockPanel.SetDock(btns, Dock.Right); foot.Children.Add(btns);
            footNote.VerticalAlignment = VerticalAlignment.Center; footNote.Margin = new Thickness(0, 0, 20, 0);
            foot.Children.Add(footNote);
            right.Children.Add(foot);
            var sv = U.Scroll(body);
            right.Children.Add(sv);
            Children.Add(right);

            U.OnKey(this, System.Windows.Input.Key.Right, System.Windows.Input.ModifierKeys.Alt, NextClicked);
            U.OnKey(this, System.Windows.Input.Key.Left, System.Windows.Input.ModifierKeys.Alt, () => { if (step > 1) Go(step - 1); });
            Go(Math.Max(1, Math.Min(5, startStep)));
        }

        static Profile Clone(Profile x) => System.Text.Json.JsonSerializer.Deserialize<Profile>(System.Text.Json.JsonSerializer.Serialize(x));

        public int Step => step;

        public void Go(int s)
        {
            if (s < 1 || s > 5) return;
            step = s;
            BuildRail();
            back.IsEnabled = s > 1;
            next.Content = s == 5 ? "Finish setup" : "Next";
            body.Content = s switch { 1 => Step1(), 2 => Step2(), 3 => Step3(), 4 => Step4(), _ => Step5() };
            footNote.Text = s switch
            {
                1 => "Step 1 of 5",
                2 => conf?.World != null ? "Step 2 of 5 · server found" : "Step 2 of 5 · pick your worldserver.conf to continue",
                3 => "Step 3 of 5 · you can finish this later; browsing waits until it's done",
                4 => "Step 4 of 5 · optional",
                _ => "Step 5 of 5 · optional",
            };
        }

        void BuildRail()
        {
            rail.Children.Clear();
            for (int i = 1; i <= 5; i++)
            {
                bool on = i == step, done = i < step;
                var num = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = on ? U.R("Amber") : done ? U.R("OkBg") : U.R("Panel2"), BorderBrush = U.R("Line"), BorderThickness = new Thickness(on || done ? 0 : 1),
                    Child = new TextBlock { Text = done ? "✓" : i.ToString(), FontWeight = FontWeights.Bold, Foreground = on ? U.R("AmberInk") : done ? U.R("Ok") : U.R("Muted"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                var txt = U.Stack(U.T(Titles[i - 1], 14, on ? "Text" : "Muted", on ? FontWeights.SemiBold : FontWeights.Normal, false), U.T(Subs[i - 1], 12, "Faint", null, false));
                txt.Margin = new Thickness(12, 0, 0, 0);
                var row = U.Row(num, txt);
                var b = new Border { Child = row, Padding = new Thickness(10, 9, 10, 9), CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 0, 4), Background = on ? U.R("MaroonDim") : Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand };
                int k = i;
                b.MouseLeftButtonUp += (s, e) => { if (k < step || CanLeave(step, false)) Go(k); };
                b.ToolTip = $"Go to step {i}: {Titles[i - 1]}";
                rail.Children.Add(b);
            }
        }

        bool CanLeave(int s, bool showWhy)
        {
            if (s >= 2 && step >= 2 && (conf == null || conf.World == null))
            {
                if (showWhy) w.Toast("Pick your worldserver.conf first, so Studio knows which database to use.", "warn");
                return false;
            }
            return true;
        }

        void NextClicked()
        {
            if (step == 1 && string.IsNullOrWhiteSpace(p.Name)) { w.Toast("Give this server profile a name.", "warn"); return; }
            if (step == 2 && !CanLeave(2, true)) return;
            if (step == 3) SaveAccountFields();
            if (step < 5) { Go(step + 1); return; }
            Finish();
        }

        void Finish()
        {
            SaveAccountFields();
            p.SetupComplete = true;
            w.S.Profiles.Save(p);
            w.S.Profile = p;
            w.S.RaiseProfile();
            w.Toast(w.S.HasReadAccount ? "Setup saved. Happy editing!" : "Setup saved. The SQL browser will wait until the read-only account works (step 3).", "ok");
            w.WizardDone();
        }

        void SaveAccountFields()
        {
            p.RoPassEnc = w.S.Secrets.Protect(roPw);
            p.RwPassEnc = w.S.Secrets.Protect(rwPw);
        }

        // ------------------------------------------------------------------ step 1
        UIElement Step1()
        {
            var sp = new StackPanel { MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left };
            sp.Children.Add(U.Row(U.Crest(64), U.Stack(U.Eyebrow("Welcome"), U.H1("AndersCraft Studio"))));
            ((StackPanel)sp.Children[0]).Children[1].SetValue(MarginProperty, new Thickness(16, 6, 0, 0));
            sp.Children.Add(U.T("A friendly editor for AzerothCore servers. Browse the world database, make changes, and apply them with a safety net: every change is staged first, reviewed side by side, backed up, and can be rolled back.",
                15, "Muted", null, true, new Thickness(0, 18, 0, 24)));

            var cards = new UniformGrid3();
            cards.Add(Feature("", "Look freely", "Browsing uses a read-only MySQL account. Clicking around can never change your server."));
            cards.Add(Feature("", "Stage, don't write", "Edits and typed SQL go into a changeset file. Your database stays untouched until you say so."));
            cards.Add(Feature("", "Apply with a net", "Apply backs up the affected tables first, runs in one transaction, and records how to undo it."));
            sp.Children.Add(cards);

            var name = U.Box(p.Name);
            name.TextChanged += (s, e) => p.Name = name.Text.Trim();
            sp.Children.Add(U.Field("Profile name", "A name for this server, e.g. \"AndersCraft\" or \"Test realm\". Studio can remember several servers.", name, null, 420));
            sp.Children.Add(U.Callout("Setup takes about five minutes. You'll need to run one SQL script yourself as a MySQL administrator in step 3; Studio never creates accounts on its own.", "info", "Before you start"));
            return sp;
        }

        UIElement Feature(string glyph, string title, string text)
        {
            var g = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 20, Foreground = U.R("Amber"), Margin = new Thickness(0, 0, 0, 12) };
            return U.Card(U.Stack(g, U.T(title, 15, "Text", FontWeights.SemiBold, true, new Thickness(0, 0, 0, 6)), U.T(text, 13, "Muted")), new Thickness(18), new Thickness(0, 0, 14, 22));
        }

        sealed class UniformGrid3 : System.Windows.Controls.Primitives.UniformGrid
        {
            public UniformGrid3() { Columns = 3; }
            public void Add(UIElement e) => Children.Add(e);
        }

        // ------------------------------------------------------------------ step 2
        void LoadConf(string path, bool rebuild = true)
        {
            try { conf = ConfParser.ParseFile(path); confError = null; p.ConfPath = path; }
            catch (Exception ex) { conf = null; confError = ex.Message; }
            if (conf?.World != null)
            {
                p.Core = conf.Core; p.Host = conf.World.Host; p.Port = conf.World.Port; p.WorldDb = conf.World.Database;
                if (conf.Characters != null) p.CharactersDb = conf.Characters.Database;
                if (conf.Login != null) p.AuthDb = conf.Login.Database;
                if (string.IsNullOrEmpty(p.SandboxDb) || p.SandboxDb == "acore_world_studio_sandbox") p.SandboxDb = p.WorldDb + "_studio_sandbox";
                p.AccountHost = p.Host == "127.0.0.1" || p.Host == "localhost" || p.Host == "::1" ? "localhost" : "%";
                reachable = null;
                var host = p.Host; var port = p.Port;
                Task.Run(() =>
                {
                    try { using var c = new TcpClient(); return c.ConnectAsync(host, port).Wait(1500) && c.Connected; } catch { return false; }
                }).ContinueWith(t => Dispatcher.Invoke(() => { reachable = t.Result; if (step == 2) Go(2); }));
            }
            if (rebuild && step == 2) Go(2);
        }

        UIElement Step2()
        {
            var sp = new StackPanel { MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left };
            sp.Children.Add(U.Eyebrow("Step 2 of 5"));
            sp.Children.Add(U.H1("Find your server"));
            sp.Children.Add(U.Sub("Studio reads your worldserver.conf to learn where the databases are. It only reads the file; it never changes it, and it doesn't keep the server's own MySQL password."));

            var path = U.Box(p.ConfPath ?? "", true);
            path.LostFocus += (s, e) => { if (path.Text != (p.ConfPath ?? "") && File.Exists(path.Text.Trim())) LoadConf(path.Text.Trim()); };
            var browse = U.Btn("Browse…", () =>
            {
                var d = new OpenFileDialog { Title = "Pick worldserver.conf", Filter = "worldserver.conf|worldserver.conf|Config files (*.conf)|*.conf|All files|*.*" };
                if (!string.IsNullOrEmpty(p.ConfPath)) try { d.InitialDirectory = Path.GetDirectoryName(p.ConfPath); } catch { }
                if (d.ShowDialog(w) == true) LoadConf(d.FileName);
            }, null, "Pick the file yourself. It's usually in your server's bin folder under configs\\.");
            var auto = U.Btn("Auto-detect", AutoDetect, "BtnPrimary", "Look in the usual places on every local drive (folders with names like azerothcore, server, wow...). Takes a few seconds.");
            var g = new DockPanel();
            var bb = U.Row(browse, auto); bb.Margin = new Thickness(10, 0, 0, 0);
            DockPanel.SetDock(bb, Dock.Right); g.Children.Add(bb); g.Children.Add(path);
            sp.Children.Add(U.Field("worldserver.conf location", "Typically ...\\Build\\bin\\RelWithDebInfo\\configs\\worldserver.conf, or env\\dist\\etc on Linux-style layouts.", g));

            if (detected != null)
            {
                if (detected.Count == 0) sp.Children.Add(U.Callout("Auto-detect didn't find a worldserver.conf. Use Browse… to pick it.", "warn"));
                else
                {
                    var lst = new StackPanel();
                    foreach (var f in detected)
                    {
                        var b = U.Btn(f, () => LoadConf(f), f == p.ConfPath ? "BtnAmber" : null, "Use this file");
                        b.HorizontalContentAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 0, 0, 6); b.FontFamily = U.Mono; b.FontWeight = FontWeights.Normal; b.FontSize = 12.5;
                        lst.Children.Add(b);
                    }
                    sp.Children.Add(U.Field($"Found {detected.Count} file(s): click one to use it", null, lst));
                }
            }

            if (confError != null) sp.Children.Add(U.Callout(confError, "bad", "Couldn't read that file"));
            if (conf != null)
            {
                var checks = new StackPanel();
                checks.Children.Add(U.CheckRow(U.Check.Ok, "Settings file", Path.GetFileName(p.ConfPath) + " (readable)", p.ConfPath));
                checks.Children.Add(U.CheckRow(conf.Core == "AzerothCore" ? U.Check.Ok : U.Check.Warn, "Server core", conf.Core,
                    conf.Core == "AzerothCore" ? "Studio is built and tested for AzerothCore 3.3.5a." : "Studio is built for AzerothCore. Other cores may work for browsing, but table descriptions may not match."));
                if (conf.World != null)
                {
                    var rs = reachable == null ? U.Check.Pending : reachable == true ? U.Check.Ok : U.Check.Bad;
                    var rt = reachable == null ? " (checking…)" : reachable == true ? " (answering)" : " (not answering; is MySQL running?)";
                    checks.Children.Add(U.CheckRow(rs, "Database server", $"{conf.World.Host}:{conf.World.Port}{rt}", "Studio only opened a network connection to see if MySQL answers. It didn't log in."));
                    checks.Children.Add(U.CheckRow(U.Check.Ok, "World database", conf.World.Database, "Creatures, items, quests, loot, scripts. This is what Studio edits."));
                }
                else checks.Children.Add(U.CheckRow(U.Check.Bad, "World database", "WorldDatabaseInfo not found", "The line should look like: WorldDatabaseInfo = \"127.0.0.1;3306;user;password;acore_world\""));
                checks.Children.Add(U.CheckRow(conf.Characters != null ? U.Check.Ok : U.Check.Warn, "Characters database", conf.Characters?.Database ?? "not found (view-only browsing will be skipped)"));
                checks.Children.Add(U.CheckRow(conf.Login != null ? U.Check.Ok : U.Check.Warn, "Login (auth) database", conf.Login?.Database ?? "not found"));
                if (!string.IsNullOrEmpty(conf.DataDir))
                    checks.Children.Add(U.CheckRow(Directory.Exists(conf.DataDir) ? U.Check.Ok : U.Check.Info, "Server data folder", conf.DataDir, "Maps, vmaps and DBC files extracted for the server. Studio reads the server's DBC files from here (dbc subfolder)."));
                foreach (var wv in conf.Warnings.Where(x => !x.StartsWith("World"))) { }
                sp.Children.Add(U.Card(U.Stack(U.H2("What Studio found"), checks), new Thickness(20, 16, 20, 12)));
                sp.Children.Add(U.Callout("Studio saw the server's MySQL user in this file but won't use or store it. In the next step you'll create two limited accounts just for Studio.", "ok", "Your server password stays put"));
            }
            return sp;
        }

        void AutoDetect()
        {
            footNote.Text = "Searching your drives for worldserver.conf…";
            next.IsEnabled = false;
            Task.Run(() => ConfLocator.Find(ConfLocator.DefaultRoots(), 3, 10)).ContinueWith(t => Dispatcher.Invoke(() =>
            {
                next.IsEnabled = true;
                detected = t.Exception == null ? t.Result : new List<string>();
                if (detected.Count >= 1 && (conf == null || conf.World == null)) LoadConf(detected[0], false);
                Go(2);
            }));
        }

        // ------------------------------------------------------------------ step 3
        UIElement Step3()
        {
            var sp = new StackPanel { MaxWidth = 980, HorizontalAlignment = HorizontalAlignment.Left };
            sp.Children.Add(U.Eyebrow("Step 3 of 5"));
            sp.Children.Add(U.H1("Safe database accounts"));
            sp.Children.Add(U.Sub("Studio uses two MySQL accounts of its own, so it never needs your admin password. Copy the script below and run it once yourself, logged in as a MySQL administrator (root), in HeidiSQL, MySQL Workbench or the mysql command line."));

            var two = new Grid();
            two.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            two.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            two.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var script = U.Code("", 300);
            // passwords stay masked on screen (and in screenshots) unless you press Show; Copy and Save always use the real ones
            string RealScript() => AccountScript.Build(p, roPw, rwPw);
            void Refresh() => script.Text = reveal ? RealScript() : AccountScript.Build(p, "********", "********");

            UIElement AccountCard(string title, string badge, string badgeBg, string badgeFg, string what, Func<string> getUser, Action<string> setUser, Func<string> getPw, Action<string> setPw, string userTip, string pwTip)
            {
                var user = U.Box(getUser(), true);
                user.TextChanged += (s, e) => { setUser(user.Text.Trim()); Refresh(); };
                FrameworkElement pwCtl;
                Action<string> setBox;
                if (reveal)
                {
                    var pw = U.Box(getPw(), true);
                    pw.TextChanged += (s, e) => { setPw(pw.Text); Refresh(); };
                    pwCtl = pw; setBox = v => pw.Text = v;
                }
                else
                {
                    var pw = new PasswordBox { Password = getPw() };
                    pw.PasswordChanged += (s, e) => { setPw(pw.Password); Refresh(); };
                    pwCtl = pw; setBox = v => pw.Password = v;
                }
                var gen = U.Btn("Generate", () => setBox(Passwords.Generate()), null, "Make a new random 24-character password (letters and digits).");
                var show = U.Btn(reveal ? "Hide" : "Show", () => { reveal = !reveal; Go(3); }, "BtnGhost", "Show or hide both passwords (and in the script below).");
                show.Margin = new Thickness(4, 0, 0, 0);
                var pwRow = new DockPanel(); gen.Margin = new Thickness(8, 0, 0, 0); var btns = U.Row(gen, show); DockPanel.SetDock(btns, Dock.Right); pwRow.Children.Add(btns); pwRow.Children.Add(pwCtl);
                return U.Card(U.Stack(
                    U.Row(U.T(title, 15, "Text", FontWeights.SemiBold, false), new Border { Width = 10 }, U.Pill(badge, badgeBg, badgeFg)),
                    U.T(what, 12.5, "Muted", null, true, new Thickness(0, 6, 0, 14)),
                    U.Field("User name", null, user, userTip),
                    U.Field("Password", "Saved encrypted for your Windows user only (DPAPI). Studio never shows it to anyone else.", pwRow, pwTip)), new Thickness(18, 16, 18, 4), new Thickness(0, 0, 0, 16));
            }

            var ro = AccountCard("Browse account", "read-only", "OkBg", "Ok", "Used for everything you look at: the table list, data grid, filters, exports and SELECT queries. It can't change anything.",
                () => p.RoUser, v => p.RoUser = v, () => roPw, v => roPw = v,
                "Name of the read-only MySQL account. studio_ro is the default.", "Password for the read-only account. It goes into the script below.");
            var rw = AccountCard("Apply account", "apply only", "AmberDim", "Amber", "Used only when you press Apply on the Changes screen, after a backup and a typed confirmation. It can change rows in the world database and the sandbox, nothing else.",
                () => p.RwUser, v => p.RwUser = v, () => rwPw, v => rwPw = v,
                "Name of the apply account. studio_rw is the default.", "Password for the apply account. It goes into the script below.");
            two.Children.Add(ro);
            Grid.SetColumn(rw, 2); two.Children.Add(rw);
            sp.Children.Add(two);

            var hostBox = U.Box(p.AccountHost, true);
            hostBox.TextChanged += (s, e) => { p.AccountHost = hostBox.Text.Trim(); Refresh(); };
            var auth = new CheckBox { Content = "Let the browse account view the login database (" + p.AuthDb + ", view-only)", IsChecked = p.AllowAuthView, Margin = new Thickness(0, 0, 0, 16),
                ToolTip = "Lets you look at accounts, bans and the realm list in the SQL browser. Passwords there are stored hashed, and the browse account can't change anything. Untick to keep Studio out of it entirely." };
            auth.Checked += (s, e) => { p.AllowAuthView = true; Refresh(); };
            auth.Unchecked += (s, e) => { p.AllowAuthView = false; Refresh(); };
            var opts = U.Wrap(U.Field("Connect from (account host)", "\"localhost\" when Studio runs on the same PC as MySQL (the script then also adds 127.0.0.1). Use the PC's address or % for remote.", hostBox, null, 440));
            sp.Children.Add(opts);
            sp.Children.Add(auth);

            Refresh();
            var copy = U.Btn("Copy script", () => { try { Clipboard.SetText(RealScript()); w.Toast("Script copied. Paste it into your SQL tool and run it as root.", "ok"); } catch { w.Toast("Couldn't reach the clipboard. Use Save as .sql instead.", "warn"); } }, "BtnPrimary", "Copy the whole script to the clipboard.");
            var save = U.Btn("Save as .sql…", () =>
            {
                var d = new SaveFileDialog { FileName = "studio_accounts.sql", Filter = "SQL script|*.sql" };
                if (d.ShowDialog(w) == true) { File.WriteAllText(d.FileName, RealScript()); w.Toast("Saved " + d.FileName, "ok"); }
            }, null, "Save the script to a file. It contains the passwords, so delete it once you've run it.");
            var test = U.Btn("I ran it · Test the accounts", TestAccounts, "BtnAmber", "Log in with both accounts and read their permissions (SHOW GRANTS). Nothing is changed.");
            sp.Children.Add(U.Card(U.Stack(
                U.Row(U.H2("Script to run as administrator"), new Border { Width = 12 }, U.Pill("Studio never runs this", "Panel2", "Muted")),
                U.T("Running it again is safe: it only resets the two passwords. Nothing else on your server changes.", 12.5, "Muted", null, true, new Thickness(0, 0, 0, 10)),
                script,
                U.Row(copy, save, test)), new Thickness(20, 16, 20, 18)));
            ((StackPanel)((Border)sp.Children[sp.Children.Count - 1]).Child).Children[3].SetValue(MarginProperty, new Thickness(0, 14, 0, 0));

            if (accountChecks != null) sp.Children.Add(U.Card(U.Stack(new UIElement[] { U.H2("Account check") }.Concat(accountChecks).ToArray()), new Thickness(20, 16, 20, 12)));
            return sp;
        }

        void TestAccounts()
        {
            SaveAccountFields();
            var prof = Clone(p); var ro = roPw; var rw = rwPw;
            accountChecks = new List<Border> { U.CheckRow(U.Check.Pending, "Testing", "logging in…") };
            Go(3);
            Task.Run(() =>
            {
                var res = new List<(U.Check, string, string, string)>();
                try
                {
                    using var db = new ReadOnlyDb(prof.Host, prof.Port, prof.RoUser, ro, prof.WorldDb);
                    res.Add((U.Check.Ok, prof.RoUser + " logs in", "yes", null));
                    var writes = GrantCheck.WritePrivileges(db.Grants());
                    res.Add(writes.Count == 0 ? (U.Check.Ok, prof.RoUser + " is read-only", "only SELECT / SHOW VIEW", null)
                                              : (U.Check.Bad, prof.RoUser + " is read-only", "NO: it also has " + string.Join(", ", writes), "Remove those privileges, or re-create the account with the script. Studio refuses to browse with an account that can write."));
                    var n = db.Scalar("SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = DATABASE()");
                    res.Add((U.Check.Ok, "Can see " + prof.WorldDb, n + " tables", null));
                    var dbs = db.Databases();
                    prof.SandboxExists = dbs.Contains(prof.SandboxDb, StringComparer.OrdinalIgnoreCase);
                    p.SandboxExists = prof.SandboxExists;
                    if (writes.Count == 0) p.AccountsVerifiedUtc = DateTime.UtcNow;
                }
                catch (Exception ex) { res.Add((U.Check.Bad, prof.RoUser + " logs in", "no: " + ex.Message, "Did the script run without errors? Check the user name, password and host.")); }
                try
                {
                    using var db = new ReadOnlyDb(prof.Host, prof.Port, prof.RwUser, rw, prof.WorldDb);   // logging in only; read-only session
                    res.Add((U.Check.Ok, prof.RwUser + " logs in", "yes (only tested, nothing written)", null));
                    var writes = GrantCheck.WritePrivileges(db.Grants());
                    var bad = writes.Where(x => x != "INSERT" && x != "UPDATE" && x != "DELETE").ToList();
                    res.Add(bad.Count == 0 ? (U.Check.Ok, prof.RwUser + " is limited", "row changes only", null)
                                           : (U.Check.Warn, prof.RwUser + " is limited", "has extra rights: " + string.Join(", ", bad), "It works, but has more power than Studio needs. Re-run the script on a fresh account to limit it."));
                }
                catch (Exception ex) { res.Add((U.Check.Bad, prof.RwUser + " logs in", "no: " + ex.Message, "You can still browse. Apply needs this account.")); }
                return res;
            }).ContinueWith(t => Dispatcher.Invoke(() =>
            {
                accountChecks = t.Result.Select(r => U.CheckRow(r.Item1, r.Item2, r.Item3, r.Item4)).ToList();
                if (step == 3) Go(3);
            }));
        }

        // ------------------------------------------------------------------ step 4
        UIElement Step4()
        {
            var sp = new StackPanel { MaxWidth = 880, HorizontalAlignment = HorizontalAlignment.Left };
            sp.Children.Add(U.Eyebrow("Step 4 of 5 · optional"));
            sp.Children.Add(U.H1("Game client folder"));
            sp.Children.Add(U.Sub("Studio reads the client's data from here: icons for the Spell editor, the client's DBC tables, and installing the patches you build. Everything else works without it, so you can skip this and set it later in Settings."));
            var path = U.Box(p.ClientDir ?? "", true);
            path.TextChanged += (s, e) => p.ClientDir = path.Text.Trim();
            var browse = U.Btn("Browse…", () =>
            {
                var d = new OpenFolderDialog { Title = "Pick your World of Warcraft 3.3.5a folder (the one with Wow.exe)" };
                if (d.ShowDialog(w) == true) { p.ClientDir = d.FolderName; Go(4); }
            }, null, "Pick the folder that contains Wow.exe.");
            var row = new DockPanel(); browse.Margin = new Thickness(10, 0, 0, 0); DockPanel.SetDock(browse, Dock.Right); row.Children.Add(browse); row.Children.Add(path);
            sp.Children.Add(U.Field("Client folder", "The folder that contains Wow.exe and a Data folder. Studio will only read from it.", row));
            if (!string.IsNullOrWhiteSpace(p.ClientDir))
            {
                bool exe = File.Exists(Path.Combine(p.ClientDir, "Wow.exe")) || File.Exists(Path.Combine(p.ClientDir, "WoW.exe"));
                bool data = Directory.Exists(Path.Combine(p.ClientDir, "Data"));
                string locale = data ? Directory.GetDirectories(Path.Combine(p.ClientDir, "Data")).Select(Path.GetFileName).FirstOrDefault(n => n.Length == 4 && char.IsLower(n[0]) && char.IsUpper(n[2])) : null;
                sp.Children.Add(U.Card(U.Stack(U.H2("What Studio found"),
                    U.CheckRow(exe ? U.Check.Ok : U.Check.Warn, "Game program", exe ? "Wow.exe" : "Wow.exe not found here"),
                    U.CheckRow(data ? U.Check.Ok : U.Check.Warn, "Data folder", data ? "Data\\" : "missing"),
                    U.CheckRow(locale != null ? U.Check.Ok : U.Check.Info, "Language", locale ?? "unknown")), new Thickness(20, 16, 20, 12)));
            }
            sp.Children.Add(U.Callout("Studio never launches the game or changes client files in this milestone.", "info"));
            return sp;
        }

        // ------------------------------------------------------------------ step 5
        UIElement Step5()
        {
            var sp = new StackPanel { MaxWidth = 960, HorizontalAlignment = HorizontalAlignment.Left };
            sp.Children.Add(U.Eyebrow("Step 5 of 5 · optional, recommended"));
            sp.Children.Add(U.H1("Practice sandbox"));
            sp.Children.Add(U.Sub("The sandbox is a full copy of your world database that the game server never uses. Apply goes to the sandbox by default, so you can practise the whole Edit → Stage → Review → Apply loop, and look at the result, without any risk to your live world."));

            var how = new UniformGrid3();
            how.Add(Feature("", "A copy, not a link", $"{p.SandboxDb} starts as an exact copy of {p.WorldDb}. Changes there never reach players."));
            how.Add(Feature("", "Refresh anytime", "Drop it and run the script again to start over from your live data."));
            how.Add(Feature("", "Live stays locked", "Applying to Live stays off until you switch it on in Settings › Safety."));
            sp.Children.Add(how);

            var name = U.Box(p.SandboxDb, true);
            name.TextChanged += (s, e) => { p.SandboxDb = name.Text.Trim(); };
            sp.Children.Add(U.Field("Sandbox database name", "Must differ from your live databases. The default adds _studio_sandbox to the world database name.", name, null, 460));

            string status = sandboxFound == null ? (w.S.HasReadAccount || w.S.DemoConf ? "not checked yet" : "can't check until step 3 works") : sandboxFound == true ? "exists" : "not created yet";
            sp.Children.Add(U.Card(U.Stack(U.H2("Sandbox status"),
                U.CheckRow(sandboxFound == true ? U.Check.Ok : sandboxFound == false ? U.Check.Warn : U.Check.Pending, p.SandboxDb, status),
                U.Row(U.Btn("Create sandbox…", BuildSandboxScript, "BtnPrimary", "Builds the SQL that creates the sandbox. Studio shows it to you; you run it yourself."),
                      U.Btn("Check again", CheckSandbox, null, "Look for the sandbox database with the read-only account."))), new Thickness(20, 16, 20, 18)));
            var lastRow = (StackPanel)((StackPanel)((Border)sp.Children[sp.Children.Count - 1]).Child).Children[2];
            lastRow.Margin = new Thickness(0, 14, 0, 0);

            if (sandboxScript != null)
            {
                var code = U.Code(sandboxScript, 260);
                sp.Children.Add(U.Card(U.Stack(
                    U.Row(U.H2("Sandbox plan"), new Border { Width = 12 }, U.Pill("you run this, not Studio", "Panel2", "Muted")),
                    U.T("Run it as a MySQL administrator. It only reads " + p.WorldDb + " and writes the new " + p.SandboxDb + ".", 12.5, "Muted", null, true, new Thickness(0, 0, 0, 10)),
                    code,
                    U.Row(U.Btn("Copy plan", () => { try { Clipboard.SetText(sandboxScript); w.Toast("Sandbox plan copied.", "ok"); } catch { } }, "BtnPrimary", "Copy the plan to the clipboard."),
                          U.Btn("Save as .sql…", () =>
                          {
                              var d = new SaveFileDialog { FileName = "studio_sandbox.sql", Filter = "SQL script|*.sql" };
                              if (d.ShowDialog(w) == true) File.WriteAllText(d.FileName, sandboxScript);
                          }, null, "Save the plan to a file."))), new Thickness(20, 16, 20, 18)));
                ((StackPanel)((Border)sp.Children[sp.Children.Count - 1]).Child).Children[3].SetValue(MarginProperty, new Thickness(0, 14, 0, 0));
            }
            return sp;
        }

        void CheckSandbox()
        {
            var prof = Clone(p);
            Task.Run(() =>
            {
                try { using var db = OpenForWizard(prof); return (bool?)db.Databases().Contains(prof.SandboxDb, StringComparer.OrdinalIgnoreCase); }
                catch { return null; }
            }).ContinueWith(t => Dispatcher.Invoke(() =>
            {
                sandboxFound = t.Result; if (t.Result != null) p.SandboxExists = t.Result.Value;
                if (t.Result == null) w.Toast("Couldn't check: the read-only account isn't working yet (step 3).", "warn");
                if (step == 5) Go(5);
            }));
        }

        ReadOnlyDb OpenForWizard(Profile prof)
        {
            if (w.S.DemoConf) { var c = ConfParser.ParseFile(prof.ConfPath).World; return new ReadOnlyDb(c.Host, c.Port, c.User, c.Password, prof.WorldDb); }
            return new ReadOnlyDb(prof.Host, prof.Port, prof.RoUser, roPw, prof.WorldDb);
        }

        public void BuildSandboxScript()
        {
            if (string.Equals(p.SandboxDb, p.WorldDb, StringComparison.OrdinalIgnoreCase) || string.Equals(p.SandboxDb, p.CharactersDb, StringComparison.OrdinalIgnoreCase) || string.Equals(p.SandboxDb, p.AuthDb, StringComparison.OrdinalIgnoreCase))
            { w.Toast("The sandbox needs its own name, different from your live databases.", "bad"); return; }
            var prof = Clone(p);
            Task.Run(() =>
            {
                try
                {
                    using var db = OpenForWizard(prof);
                    var tables = db.Tables().Select(t => t.name).ToList();
                    var cs = db.Query("SELECT DEFAULT_CHARACTER_SET_NAME, DEFAULT_COLLATION_NAME FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = DATABASE()", 1);
                    var charset = cs.Rows.Count > 0 ? Convert.ToString(cs.Rows[0][0]) : "utf8mb4";
                    var coll = cs.Rows.Count > 0 ? Convert.ToString(cs.Rows[0][1]) : null;
                    return SandboxPlan.Build(prof, tables, charset, coll);
                }
                catch { return SandboxPlan.Build(prof, null); }
            }).ContinueWith(t => Dispatcher.Invoke(() => { sandboxScript = t.Result; if (step == 5) Go(5); }));
        }

        /// <summary>For screenshots: fill in the step 3 check results without contacting the server.</summary>
        public void ShowAccountCheckPreview(bool exists)
        {
            accountChecks = new List<Border>
            {
                U.CheckRow(exists ? U.Check.Ok : U.Check.Bad, p.RoUser + " logs in", exists ? "yes" : "not yet: run the script above as root, then test again"),
                U.CheckRow(exists ? U.Check.Ok : U.Check.Pending, p.RoUser + " is read-only", exists ? "only SELECT / SHOW VIEW" : "waiting"),
                U.CheckRow(exists ? U.Check.Ok : U.Check.Pending, p.RwUser + " logs in", exists ? "yes (only tested, nothing written)" : "waiting"),
            };
            Go(3);
        }
        public void TestAccountsNow() => TestAccounts();
        public void UseConf(string path) { if (path != null) LoadConf(path); }
        public void FinishNow() => Finish();
        public void AutoDetectNow() => AutoDetect();
        public void CheckSandboxNow() => CheckSandbox();
    }
}
