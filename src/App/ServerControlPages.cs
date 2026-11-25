using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Ellipse = System.Windows.Shapes.Ellipse;
using Path = System.IO.Path;
using AndersCraftStudio.Core;
using AndersCraftStudio.Core.Live;

namespace AndersCraftStudio
{
    /// <summary>Shared look for the Server control pages: a "LIVE" strip, so it's never mistaken for the staged editor side.</summary>
    static class Live
    {
        public static UIElement Strip(string title, string sub)
        {
            var pill = new Border { Background = U.R("BadBg"), BorderBrush = U.R("Bad"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 3, 10, 3), VerticalAlignment = VerticalAlignment.Center,
                Child = U.Row(new Ellipse { Width = 8, Height = 8, Fill = U.R("Bad"), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center }, U.T("LIVE SERVER", 11.5, "Bad", FontWeights.Bold, false)),
                ToolTip = "Everything on this tab acts on your running server right away. Nothing here is staged, and Apply isn't involved." };
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
            var note = U.T("Acts on your running server right away · not staged · every action is in the audit log", 12, "Muted", null, false);
            note.VerticalAlignment = VerticalAlignment.Center; note.Margin = new Thickness(12, 0, 0, 0);
            var right = U.Row(pill, note); right.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(right, Dock.Right); head.Children.Add(right);
            head.Children.Add(U.Stack(U.H1(title), U.T(sub, 13.5, "Muted")));
            return head;
        }

        /// <summary>A labelled field whose box keeps its own width at the left (not centred).</summary>
        public static StackPanel F(string label, string hint, FrameworkElement control, string tip = null)
        {
            control.HorizontalAlignment = HorizontalAlignment.Left;
            var f = U.Field(label, hint, control, tip);
            f.HorizontalAlignment = HorizontalAlignment.Left;
            return f;
        }

        public static Ellipse Pip(CompStatus s) => new Ellipse { Width = 11, Height = 11, Fill = U.R(s.Ready ? "Ok" : s.Running ? "Amber" : "Faint"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 9, 0) };

        public static TextBox Out(double h = 160) { var t = U.Code("", h); t.Foreground = U.R("Text"); return t; }
        public static void Append(TextBox t, string s) { t.AppendText((t.Text.Length > 0 ? Environment.NewLine : "") + s); t.ScrollToEnd(); }

        public static DataGrid Grid()
        {
            // soft table: zebra rows and a hover tint, no grid lines or outline
            var g = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, HeadersVisibility = DataGridHeadersVisibility.Column, CanUserAddRows = false, EnableRowVirtualization = true, GridLinesVisibility = DataGridGridLinesVisibility.None, RowHeight = 32,
                BorderThickness = new Thickness(0), Background = Brushes.Transparent, RowBackground = U.R("Panel"), AlternatingRowBackground = U.R("Panel2") };
            var rs = new Style(typeof(DataGridRow));
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(Control.BackgroundProperty, U.R("Hover"))); rs.Triggers.Add(hover);
            g.RowStyle = rs;
            return g;
        }
        public static void Col(DataGrid g, string h, string path, double w = double.NaN, string fmt = null, string sort = null)
        {
            var b = new Binding(path); if (fmt != null) b.StringFormat = fmt;
            g.Columns.Add(new DataGridTextColumn { Header = h, Binding = b, Width = double.IsNaN(w) ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(w), SortMemberPath = sort ?? path });
        }

        /// <summary>Confirm a live action; for risky ones also offer a backup first. Returns (go, backupFirst).</summary>
        public static (bool go, bool backup) Confirm(Window owner, string title, string text, bool offerBackup)
        {
            if (!offerBackup) return (ChoiceWindow.Ask(owner, title, text, "Do it now", "Cancel", "Cancel") == 0, false);
            var r = ChoiceWindow.Ask(owner, title, text + "\n\nA backup of the databases it touches is recommended first.", "Back up first, then do it", "Do it without a backup", "Cancel");
            return (r == 0 || r == 1, r == 0);
        }

        public static Button Gated(ServerHub hub, string text, Action click, string style, Func<Realm> realm, string tip = null)
        {
            var b = U.Btn(text, click, style, tip);
            void Upd() { var why = hub.Blocked(realm()); b.IsEnabled = why == null; b.ToolTip = why ?? tip; }
            hub.StatusChanged += () => b.Dispatcher.Invoke(Upd);
            Upd();
            return b;
        }

        public static string RealmLabel(Realm r) => $"{r.Name} (world {r.WorldPort}, command port {r.SoapPort}{(r.HasBots ? ", bots" : "")})";
    }

    // ============================================================================ Dashboard
    public sealed class ScDashboardPage : IPage
    {
        readonly MainWindow w; ServerHub H => w.Hub;
        readonly ContentControl host = new ContentControl { Focusable = false };
        readonly ContentControl cards = new ContentControl { Focusable = false };
        readonly TextBox activity = Live.Out(170);
        readonly TextBox info = Live.Out(170);
        public UIElement View => host;

        public ScDashboardPage(MainWindow w)
        {
            this.w = w;
            var sp = new StackPanel { Margin = new Thickness(28, 22, 28, 20) };
            sp.Children.Add(Live.Strip("Server dashboard", "What's running, who's online, and starting, stopping and restarting the server."));
            var all = new WrapPanel();
            Button B(string t, Action a, string st, string tip) { var b = U.Btn(t, a, st, tip); b.Margin = new Thickness(0, 0, 8, 8); return b; }
            all.Children.Add(B("Start everything", () => _ = StartAll(), "BtnPrimary", "Starts whatever isn't running: login server, each realm, the sign-up website and the daily backup."));
            all.Children.Add(B("Stop everything", () => _ = StopAll(), null, "Saves everyone, shuts each realm down gracefully, then the login server and the sign-up website."));
            all.Children.Add(B("Restart the realms", () => _ = RestartRealms(), null, "Gracefully stops and starts every realm. Players are dropped and can log back in once it's ready."));
            all.Children.Add(B("Back up now", () => _ = BackupNow(), null, "A full backup (mysqldump) of the login, character and world databases."));
            var infoBtn = Live.Gated(H, "Server status", async () => { info.Clear(); foreach (var r in H.Cfg.Realms.Where(r => H.Blocked(r) == null)) { var res = await H.Run(r, "server info", "status"); Live.Append(info, r.Name.ToUpperInvariant() + Environment.NewLine + res.text + Environment.NewLine); } }, null, () => null, "Uptime, players and build of each realm (the server's own report).");
            infoBtn.Margin = new Thickness(0, 0, 8, 8); all.Children.Add(infoBtn);
            sp.Children.Add(U.Soft(U.Stack(U.Eyebrow("Whole server"), all), new Thickness(18, 14, 18, 8), new Thickness(0, 0, 0, 14)));
            sp.Children.Add(cards);
            var g2 = new UniformGrid { Columns = 2, Margin = new Thickness(0, 14, 0, 0) };
            g2.Children.Add(U.Soft(U.Stack(U.Eyebrow("Server status (from the realms)"), info), new Thickness(16, 12, 16, 12), new Thickness(0, 0, 7, 0)));
            g2.Children.Add(U.Soft(U.Stack(U.Eyebrow("Activity"), activity), new Thickness(16, 12, 16, 12), new Thickness(7, 0, 0, 0)));
            sp.Children.Add(g2);
            host.Content = U.Scroll(sp);
            foreach (var l in H.Activity) Live.Append(activity, l);
            H.ActivityAdded += l => activity.Dispatcher.Invoke(() => Live.Append(activity, l));
            H.StatusChanged += () => host.Dispatcher.Invoke(() => { if (w.CurrentPage == "sc_dash") BuildCards(); });
            BuildCards();
        }

        public void OnShow() { H.Start(); BuildCards(); }

        void BuildCards()
        {
            var s = H.Now;
            var grid = new UniformGrid { Columns = 3 };
            grid.Children.Add(Card(s.Auth, "Login server", s.Auth.Ready ? $"Accepting logins on port {H.Cfg.AuthPort}" : s.Auth.Running ? "Starting…" : "Stopped", null, new[]
            {
                ("Start", (Action)(() => _ = Guard(async () => { if (Process.GetProcessesByName("authserver").Any()) { w.Toast("The login server is already running.", "info"); return; } H.StartAuthProcess(); await Task.CompletedTask; }))),
                ("Stop", () => _ = Guard(async () => { if (Live.Confirm(w, "Stop the login server", "Players can't log in until it's started again. Players already in the game stay.", false).go) await H.StopAuthProcess(); })),
            }));
            foreach (var r in H.Cfg.Realms)
            {
                var rr = r;
                s.Worlds.TryGetValue(r.Id, out var st); st ??= new CompStatus();
                s.Online.TryGetValue(r.Id, out var oc);
                var online = !s.DbOk ? "Online: (database not readable)" : st.Ready ? $"Online: {oc.real} player{(oc.real == 1 ? "" : "s")}" + (r.HasBots ? $" + {oc.bots} bots" : "") : "Online: —";
                var soapUp = s.Soap.TryGetValue(r.Id, out var su) && su;
                var extra = new StackPanel();
                var auto = new CheckBox { IsChecked = r.AutoRestart, Content = U.T("Restart it if it crashes", 12.5, "Text", null, false), ToolTip = $"The crash watchdog starts it again when it stops without being asked to (at most {H.Cfg.AutoRestartMaxPerHour} times an hour). Works while Studio is open.", Margin = new Thickness(0, 6, 0, 0) };
                auto.Checked += (o, e) => { rr.AutoRestart = true; H.Save(); }; auto.Unchecked += (o, e) => { rr.AutoRestart = false; H.Save(); };
                extra.Children.Add(auto);
                var closed = U.Btn("Maintenance on", () => _ = Toggle(rr, true), "BtnGhost", "Close the realm to players: only staff can log in. Players already in stay.");
                var open = U.Btn("Maintenance off", () => _ = Toggle(rr, false), "BtnGhost", "Open the realm to players again.");
                closed.IsEnabled = open.IsEnabled = H.Blocked(rr) == null;
                extra.Children.Add(U.Row(closed, open));
                grid.Children.Add(Card(st, "Realm: " + r.Name, (st.Ready ? "Ready" : st.Running ? "Loading…" : "Stopped") + $" · world port {r.WorldPort} · command port {(soapUp ? "answers" : "not answering")}",
                    online + (string.IsNullOrEmpty(st.Detail) ? "" : "\nLast log line: " + st.Detail), new[]
                {
                    ("Start", (Action)(() => _ = Guard(async () => { if (H.RealmPid(rr) > 0) { w.Toast(rr.Name + " is already running.", "info"); return; } H.StartRealmProcess(rr); await Task.CompletedTask; }))),
                    ("Stop", () => _ = Guard(() => StopRealm(rr, false))),
                    ("Restart", () => _ = Guard(() => StopRealm(rr, true))),
                }, extra));
            }
            grid.Children.Add(Card(s.Signup, "Sign-up website", s.Signup.Ready ? $"Listening on 127.0.0.1:{H.Cfg.SignupPort}" : s.Signup.Running ? "Starting…" : "Stopped", null, new[]
            {
                ("Start", (Action)(() => { if (Process.GetProcessesByName("php").Any()) { w.Toast("The sign-up website is already running.", "info"); return; } H.StartSignupProcess(); })),
                ("Stop", () => { if (Live.Confirm(w, "Stop the sign-up website", "Stops the sign-up page and its internet tunnel.", false).go) H.StopSignupProcess(); }),
            }));
            grid.Children.Add(Card(s.Tunnel, "Sign-up tunnel (ngrok)", s.Tunnel.Running ? "Tunnel running" : "Stopped", "Starts and stops with the sign-up website.", Array.Empty<(string, Action)>()));
            grid.Children.Add(Card(s.BackupLoop, "Daily backup", s.BackupLoop.Running ? "Waiting for the 05:00 backup" : "Not running", "The old daily backup script (AndersCraft_BackupDaily.ps1). Scheduled tasks can also back up.", new[]
            {
                ("Start", (Action)(() => { if (Procs.FindBackupLoop() > 0) { w.Toast("The daily backup is already running.", "info"); return; } H.StartBackupLoopProcess(); })),
            }));
            var top = new StackPanel();
            if (!s.DbOk && s.DbError != null) top.Children.Add(U.Callout("The login and character databases couldn't be read, so player counts are missing: " + s.DbError, "warn", "Database"));
            if (!H.HasGmLogin) top.Children.Add(U.Callout("No GM login is saved, so commands (graceful stop, messages, player actions) are off. Add one in Server control › Settings.", "warn", "GM login"));
            if (H.Cfg.Realms.Count == 0) top.Children.Add(U.Callout("No realms are set up yet. Server control › Settings › Import from the old Control Panel, or add them there.", "info", "Set up"));
            foreach (UIElement e in top.Children) ((FrameworkElement)e).Margin = new Thickness(0, 0, 0, 10);
            top.Children.Add(grid);
            cards.Content = top;
        }

        Border Card(CompStatus st, string title, string state, string extra, (string label, Action act)[] buttons, UIElement more = null)
        {
            var sp = new StackPanel();
            sp.Children.Add(U.Row(Live.Pip(st), U.T(title, 15, "Text", FontWeights.SemiBold, false)));
            sp.Children.Add(U.T(state, 13, "Text", null, true, new Thickness(0, 6, 0, 0)));
            if (st.Running) sp.Children.Add(U.T($"Up {st.Uptime}" + (st.MemoryBytes > 0 ? $" · CPU {st.CpuPercent:0}% · memory {st.Memory}" : "") + (U.Technical ? $" · PID {st.Pid}" : ""), 12, "Muted", null, true, new Thickness(0, 3, 0, 0)));
            if (!string.IsNullOrEmpty(extra)) { var x = U.T(extra, 12, "Muted", null, true, new Thickness(0, 4, 0, 0)); x.MaxHeight = 54; sp.Children.Add(x); }
            if (more != null) sp.Children.Add(more);
            var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            foreach (var (label, act) in buttons) { var b = U.Btn(label, act, label == "Start" ? "BtnPrimary" : null); b.Margin = new Thickness(0, 0, 6, 0); b.IsEnabled = !w.IsScreens; row.Children.Add(b); }
            sp.Children.Add(row);
            var c = U.Soft(sp, new Thickness(16, 14, 16, 12), new Thickness(0, 0, 12, 12)); c.MinHeight = 170;
            return c;
        }

        bool busy;
        async Task Guard(Func<Task> a)
        {
            if (busy) { w.Toast("Another server action is still running.", "info"); return; }
            busy = true; Mouse.OverrideCursor = Cursors.AppStarting;
            try { await a(); } catch (Exception ex) { H.Log("Action failed: " + Friendly.Text(ex)); w.Toast(Friendly.Text(ex), "bad"); }
            finally { busy = false; Mouse.OverrideCursor = null; await H.Refresh(); }
        }

        async Task Toggle(Realm r, bool closed)
        {
            var res = await H.Run(r, Gm.Closed(closed), closed ? "maintenance on" : "maintenance off");
            H.Log($"{r.Name}: maintenance {(closed ? "on (staff only)" : "off")} → {res.text}");
        }

        async Task StopRealm(Realm r, bool restart)
        {
            var cf = Live.Confirm(w, (restart ? "Restart " : "Stop ") + r.Name,
                $"Saves everyone and shuts {r.Name} down in 10 seconds (or asks its window to close if commands are off). Online players are disconnected." + (restart ? " It starts again right after." : ""), true);
            if (!cf.go) return;
            if (cf.backup && !(await BackupOk(new[] { H.Cfg.AuthDb, r.CharDb }, (restart ? "restart_" : "stop_") + r.Id))) return;
            var left = await H.StopRealmGracefully(r);
            if (left != 0)
            {
                if (ChoiceWindow.Ask(w, "Force it to stop?", $"{r.Name} hasn't closed. Force it? Anything not saved since the last save can be lost.", "Force it to stop", "Leave it running", "Leave it running") != 0) { H.Log($"{r.Name}: still running (not forced)"); return; }
                ServerHub.ForceKill(left); Audit.Write(H.AuditDir, r.Name, "force-stop", "worldserver pid " + left); await ServerHub.WaitExit(left, 10); H.Log($"{r.Name}: forced to stop");
            }
            if (restart) { await Task.Delay(1500); H.StartRealmProcess(r); }
        }

        async Task<bool> BackupOk(IEnumerable<string> dbs, string label)
        {
            var res = await H.Backup(dbs, label);
            if (res.ok) return true;
            return ChoiceWindow.Ask(w, "Backup failed", "The backup failed:\n" + res.msg + "\n\nGo on WITHOUT a backup?", "Go on anyway", "Stop here", "Stop here") == 0;
        }

        async Task StartAll() => await Guard(async () =>
        {
            Audit.Write(H.AuditDir, "-", "start-all", "");
            if (!Process.GetProcessesByName("authserver").Any()) { H.StartAuthProcess(); await Task.Delay(5000); }
            foreach (var r in H.Cfg.Realms) { if (H.RealmPid(r) > 0) { H.Log($"{r.Name}: already running"); continue; } H.StartRealmProcess(r); await Task.Delay(5000); }
            if (!string.IsNullOrEmpty(H.Cfg.PhpExe) && File.Exists(H.Cfg.PhpExe) && !Process.GetProcessesByName("php").Any()) H.StartSignupProcess();
            if (File.Exists(Path.Combine(H.Cfg.BinDir, "AndersCraft_BackupDaily.ps1")) && Procs.FindBackupLoop() == 0) H.StartBackupLoopProcess();
        });

        async Task StopAll() => await Guard(async () =>
        {
            var cf = Live.Confirm(w, "Stop everything", "Shuts every realm down gracefully, then the login server and the sign-up website. Everyone online is disconnected.", true);
            if (!cf.go) return;
            if (cf.backup && !(await BackupOk(H.AllDatabases, "stop_all"))) return;
            Audit.Write(H.AuditDir, "-", "stop-all", "");
            var left = await Task.WhenAll(H.Cfg.Realms.Select(r => H.StopRealmGracefully(r)));
            foreach (var pid in left.Where(p => p != 0)) { ServerHub.ForceKill(pid); H.Log("Forced a realm that didn't close (pid " + pid + ")"); }
            await H.StopAuthProcess();
            if (Process.GetProcessesByName("php").Any()) H.StopSignupProcess();
        });

        async Task RestartRealms() => await Guard(async () =>
        {
            var cf = Live.Confirm(w, "Restart the realms", "Gracefully restarts every realm. Players are dropped and log back in once it's ready.", true);
            if (!cf.go) return;
            if (cf.backup && !(await BackupOk(H.AllDatabases, "restart_realms"))) return;
            Audit.Write(H.AuditDir, "-", "restart-realms", "");
            foreach (var r in H.Cfg.Realms) if (await H.StopRealmGracefully(r) != 0) { H.Log($"{r.Name} didn't stop; restart stopped here."); return; }
            await Task.Delay(2000);
            foreach (var r in H.Cfg.Realms) { H.StartRealmProcess(r); await Task.Delay(5000); }
        });

        async Task BackupNow() => await Guard(async () =>
        {
            var res = await H.Backup(H.AllDatabases, "manual");
            w.Toast(res.ok ? "Backup written: " + res.msg : "Backup failed: " + res.msg, res.ok ? "ok" : "bad");
        });
    }

    // ============================================================================ Players
    public sealed class ScPlayersPage : IPage
    {
        readonly MainWindow w; ServerHub H => w.Hub;
        readonly Grid host = new Grid();
        readonly DataGrid grid = Live.Grid();
        readonly TextBox search = U.Box();
        readonly ComboBox realmBox = new ComboBox { Width = 200 }, onlineBox = new ComboBox { Width = 130 }, kindBox = new ComboBox { Width = 150 };
        readonly TextBlock count = U.T("", 12.5, "Muted", null, false);
        readonly TextBox bulkOut = Live.Out(90);
        List<CharRow> all = new List<CharRow>();
        public UIElement View => host;
        bool loaded;

        public ScPlayersPage(MainWindow w)
        {
            this.w = w;
            host.Margin = new Thickness(28, 22, 28, 20);
            host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            host.Children.Add(Live.Strip("Players", "Every character on every realm. Double-click one for its actions; tick several for actions on all of them."));
            search.TextChanged += (s, e) => Apply();
            realmBox.Items.Add("Every realm"); foreach (var r in H.Cfg.Realms) realmBox.Items.Add(r.Name); realmBox.SelectedIndex = 0; realmBox.SelectionChanged += (s, e) => Apply();
            foreach (var x in new[] { "Online and offline", "Online", "Offline" }) onlineBox.Items.Add(x); onlineBox.SelectedIndex = 0; onlineBox.SelectionChanged += (s, e) => Apply();
            foreach (var x in new[] { "Real players", "Bots", "Players and bots" }) kindBox.Items.Add(x); kindBox.SelectedIndex = 0; kindBox.SelectionChanged += (s, e) => Apply();
            var filters = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            var sb = U.WithPlaceholder(search, "Character or account name"); sb.Width = 260; sb.Margin = new Thickness(0, 0, 10, 0);
            filters.Children.Add(sb);
            foreach (var c in new[] { realmBox, onlineBox, kindBox }) { c.Margin = new Thickness(0, 0, 10, 0); filters.Children.Add(c); }
            filters.Children.Add(U.Btn("Refresh", () => _ = Load(), null, "Read the characters again."));
            filters.Children.Add(U.Btn("Open", Open, "BtnPrimary", "Open the selected character's actions (or double-click it)."));
            count.VerticalAlignment = VerticalAlignment.Center; count.Margin = new Thickness(10, 0, 0, 0); filters.Children.Add(count);
            Grid.SetRow(filters, 1); host.Children.Add(filters);

            grid.SelectionMode = DataGridSelectionMode.Extended;
            Live.Col(grid, "Name", "Name"); Live.Col(grid, "Realm", "RealmName", 150); Live.Col(grid, "Account", "Account", 120); Live.Col(grid, "Level", "Level", 60);
            Live.Col(grid, "Race", "Race", 100); Live.Col(grid, "Class", "Class", 110); Live.Col(grid, "Status", "OnlineText", 90, null, "Online"); Live.Col(grid, "Where", "Zone");
            Live.Col(grid, "Gold", "Gold", 90, "N2"); Live.Col(grid, "Played", "Played", 90, null, "TotalTime"); Live.Col(grid, "Last seen", "LastLogoutText", 135, null, "LogoutUnix");
            grid.MouseDoubleClick += (s, e) => Open();
            grid.PreviewKeyDown += (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; Open(); } };
            Grid.SetRow(grid, 2); host.Children.Add(grid);

            // bulk actions on the ticked characters
            var reason = U.Box("Server moderation"); reason.Width = 200;
            var subject = U.Box("From the staff"); subject.Width = 180;
            var items = U.Box(""); items.Width = 160; items.ToolTip = "Item IDs, optionally with a count: 6948 2589:20 (up to 12)";
            var gold = U.Box("0"); gold.Width = 60;
            Button Bulk(string t, Func<CharRow, string> cmd, bool risky, string tip)
            {
                var b = Live.Gated(H, t, () => _ = RunBulk(t, cmd, risky), risky ? null : null, () => null, tip);
                b.Margin = new Thickness(0, 0, 6, 6); return b;
            }
            var bulk = new WrapPanel();
            bulk.Children.Add(U.T("Selected characters:", 12.5, "Muted", null, false, new Thickness(0, 6, 8, 0)));
            bulk.Children.Add(Bulk("Kick", c => Gm.Kick(c.Name, reason.Text), true, "Disconnects them (they can log back in)."));
            bulk.Children.Add(Bulk("Revive", c => Gm.Revive(c.Name), false, "Brings dead characters back to life."));
            bulk.Children.Add(Bulk("Send home", c => Gm.Home(c.Name), true, "Teleports them to their hearthstone location (offline ones move at their next login)."));
            bulk.Children.Add(U.T("Reason", 12.5, "Muted", null, false, new Thickness(6, 6, 6, 0))); bulk.Children.Add(reason);
            bulk.Children.Add(U.T("Mail subject", 12.5, "Muted", null, false, new Thickness(12, 6, 6, 0))); bulk.Children.Add(subject);
            bulk.Children.Add(U.T("Items", 12.5, "Muted", null, false, new Thickness(12, 6, 6, 0))); bulk.Children.Add(items);
            bulk.Children.Add(Bulk("Mail items", c => Gm.ItemList(items.Text) is string it ? Gm.Items(c.Name, subject.Text, "Sent by the staff.", it) : null, true, "Mails the items to each one."));
            bulk.Children.Add(U.T("Gold", 12.5, "Muted", null, false, new Thickness(12, 6, 6, 0))); bulk.Children.Add(gold);
            bulk.Children.Add(Bulk("Mail gold", c => Gm.Int(gold.Text, 0) > 0 ? Gm.Money(c.Name, subject.Text, "Sent by the staff.", Gm.Copper(Gm.Int(gold.Text, 0), 0, 0)) : null, true, "Mails the gold to each one."));
            var bulkCard = U.Soft(U.Stack(bulk, bulkOut), new Thickness(14, 10, 14, 10), new Thickness(0, 10, 0, 0));
            Grid.SetRow(bulkCard, 3); host.Children.Add(bulkCard);
        }

        public void OnShow() { H.Start(); if (!loaded && !w.IsScreens) { loaded = true; _ = Load(); } }

        public async Task Load()
        {
            count.Text = "loading…";
            try
            {
                var names = new Refs(w.S);
                var errors = new List<string>();
                var realms = H.Cfg.Realms.ToList(); var auth = H.Cfg.AuthDb;
                all = await Task.Run(() => H.Read(db => { errors.Clear(); return LiveData.Characters(db, realms, auth, GameNames.Race, GameNames.Class, z => GameNames.Zone(w.S, z), errors); }, _ => errors.Count == 0));
                if (errors.Count > 0) w.Toast("Some characters couldn't be read: " + errors[0], "warn");
                Apply();
            }
            catch (Exception ex) { count.Text = "couldn't read the characters"; w.Toast("Couldn't read the characters: " + Friendly.Text(ex), "bad"); }
        }

        public void ScreensData(List<CharRow> rows) { loaded = true; all = rows; Apply(); }

        void Apply()
        {
            IEnumerable<CharRow> q = all;
            var t = search.Text.Trim();
            if (t.Length > 0) q = q.Where(c => c.Name.Contains(t, StringComparison.OrdinalIgnoreCase) || (c.Account ?? "").Contains(t, StringComparison.OrdinalIgnoreCase));
            if (realmBox.SelectedIndex > 0) { var rn = (string)realmBox.SelectedItem; q = q.Where(c => c.RealmName == rn); }
            if (onlineBox.SelectedIndex == 1) q = q.Where(c => c.Online); else if (onlineBox.SelectedIndex == 2) q = q.Where(c => !c.Online);
            if (kindBox.SelectedIndex == 0) q = q.Where(c => !c.IsBot); else if (kindBox.SelectedIndex == 1) q = q.Where(c => c.IsBot);
            var list = q.OrderByDescending(c => c.Online).ThenBy(c => c.RealmName).ThenBy(c => c.Name).ToList();
            grid.ItemsSource = list;
            count.Text = $"{list.Count:N0} shown of {all.Count:N0} · {all.Count(c => c.Online && !c.IsBot)} players online";
        }

        void Open() { if (grid.SelectedItem is CharRow c) new PlayerWindow(w, c).Show(); }

        async Task RunBulk(string what, Func<CharRow, string> cmd, bool risky)
        {
            var sel = grid.SelectedItems.Cast<CharRow>().ToList();
            if (sel.Count == 0) { w.Toast("Select one or more characters first (Ctrl+click or Shift+click).", "info"); return; }
            var cmds = sel.Select(c => (c, cmd(c))).ToList();
            if (cmds.Any(x => x.Item2 == null)) { w.Toast("Check the fields next to the button (items, gold).", "warn"); return; }
            if (!Live.Confirm(w, what, $"{what} for {sel.Count} character(s): {string.Join(", ", sel.Take(8).Select(c => c.Name))}{(sel.Count > 8 ? " …" : "")}?" + (U.Technical ? "\n\nFirst command: ." + cmds[0].Item2 : ""), false).go) return;
            foreach (var (c, command) in cmds)
            {
                if (H.Blocked(c.Realm) != null) { Live.Append(bulkOut, $"{c.Name}: skipped ({H.Blocked(c.Realm)})"); continue; }
                var res = await H.Run(c.Realm, command, "bulk " + what.ToLowerInvariant());
                Live.Append(bulkOut, $"{c.Name}: {(res.ok ? "" : "failed: ")}{res.text}");
            }
        }
    }

    /// <summary>Race, class and zone names (from the server's DBCs, with built-in fallbacks).</summary>
    static class GameNames
    {
        static readonly Dictionary<int, string> RaceN = new() { [1] = "Human", [2] = "Orc", [3] = "Dwarf", [4] = "Night Elf", [5] = "Undead", [6] = "Tauren", [7] = "Gnome", [8] = "Troll", [9] = "Goblin", [10] = "Blood Elf", [11] = "Draenei" };
        static readonly Dictionary<int, string> ClassN = new() { [1] = "Warrior", [2] = "Paladin", [3] = "Hunter", [4] = "Rogue", [5] = "Priest", [6] = "Death Knight", [7] = "Shaman", [8] = "Mage", [9] = "Warlock", [11] = "Druid" };
        static Refs refs;
        public static string Race(int id) => RaceN.TryGetValue(id, out var s) ? s : "Race " + id;
        public static string Class(int id) => ClassN.TryGetValue(id, out var s) ? s : "Class " + id;
        public static string Zone(Studio S, int id)
        {
            if (id == 0) return "—";
            try { refs ??= new Refs(S); var n = refs.Name("area", id); return string.IsNullOrEmpty(n) || n == "(not found)" ? "Zone " + id : n; } catch { return "Zone " + id; }
        }
    }

    // ============================================================================ one character
    public sealed class PlayerWindow : Window
    {
        readonly MainWindow w; ServerHub H => w.Hub;
        readonly CharRow c;
        readonly TextBox outBox = Live.Out(130);
        readonly TextBlock banLbl = U.T("Checking bans and mutes…", 12.5, "Muted");

        public PlayerWindow(MainWindow owner, CharRow ch)
        {
            w = owner; c = ch; Owner = owner; Title = ch.Name; Width = 1000; Height = 840; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResizeWithGrip; ShowInTaskbar = false; Background = U.R("Bg"); FontFamily = U.Font; Foreground = U.R("Text");
            System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome { CaptionHeight = 34, GlassFrameThickness = new Thickness(0), ResizeBorderThickness = new Thickness(6) });
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
            var sp = new StackPanel { Margin = new Thickness(24, 18, 24, 18) };
            var close = U.Btn("Close", Close, "BtnGhost"); close.HorizontalAlignment = HorizontalAlignment.Right; System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(close, true);
            var head = new DockPanel(); DockPanel.SetDock(close, Dock.Right); head.Children.Add(close);
            var title = U.Row(U.T(c.Name, 26, "Text", FontWeights.SemiBold, false), new Border { Width = 12 }, U.Pill(c.Online ? "online" : "offline", c.Online ? "OkBg" : "Panel2", c.Online ? "Ok" : "Muted"));
            if (c.IsBot) title.Children.Add(U.Pill("bot", "Panel2", "Muted"));
            title.Children.Add(new Border { Width = 8 }); title.Children.Add(U.Pill("LIVE: actions run right away", "BadBg", "Bad"));
            head.Children.Add(U.Stack(title, U.T($"Level {c.Level} {c.Race} {c.Class} · {c.RealmName}", 14, "Muted", null, false, new Thickness(0, 4, 0, 0))));
            sp.Children.Add(head);
            var facts = new UniformGrid { Columns = 4, Margin = new Thickness(0, 12, 0, 6) };
            void Fact(string k, string v) => facts.Children.Add(U.Stack(U.T(k, 11.5, "Muted", null, false), U.T(v, 14, "Text", null, false, new Thickness(0, 2, 0, 10))));
            Fact("Account", c.Account); Fact("Where", c.Zone); Fact("Gold", c.Gold.ToString("N2") + " g"); Fact("Played", c.Played);
            Fact("Last seen", c.LastLogoutText); if (U.Technical) { Fact("Character ID (guid)", c.Guid.ToString()); Fact("Characters database", c.Realm.CharDb); Fact("Command port", c.Realm.SoapPort.ToString()); }
            sp.Children.Add(facts);
            sp.Children.Add(banLbl);
            var gate = U.T(H.Blocked(c.Realm) ?? "", 12.5, "Amber", null, true, new Thickness(0, 4, 0, 8)); sp.Children.Add(gate);

            var reason = U.Box("Server moderation"); reason.Width = 260;
            var minutes = U.Box("60"); minutes.Width = 70;
            var dur = U.Box("1d"); dur.Width = 90; dur.ToolTip = "How long: 30m, 12h, 7d, 1d12h. -1 means forever.";
            var cols = new UniformGrid { Columns = 2 };
            cols.Children.Add(Section("Session", Live.F("Reason", "Shown to the player where the game shows one.", reason),
                Btns(A("Kick", Gm.Kick(c.Name, reason.Text), true, "Disconnects them; they can log back in."), A("Revive", Gm.Revive(c.Name)), A("Details", Gm.Info(c.Name), false, "The server's report on this character and account.")),
                U.Row(Live.F("Minutes", null, minutes), new Border { Width = 10 }, Btns(A("Mute", () => Gm.Mute(c.Name, Gm.Int(minutes.Text, 60), reason.Text), true, "They can't chat for that long."), A("Unmute", Gm.Unmute(c.Name))))));
            cols.Children.Add(Section("Bans", Live.F("How long", "30m, 12h, 7d, 1d12h; -1 = forever. The reason is the one under Session.", dur),
                Btns(A("Ban character", () => Gm.IsDuration(dur.Text.Trim()) ? Gm.BanCharacter(c.Name, dur.Text, reason.Text) : null, true, "Only this character."), A("Ban account", () => Gm.IsDuration(dur.Text.Trim()) ? Gm.BanAccountOf(c.Name, dur.Text, reason.Text) : null, true, $"The whole account {c.Account}: every character on it.")),
                Btns(A("Lift character ban", Gm.UnbanCharacter(c.Name)), A("Lift account ban", Gm.UnbanAccount(c.Account)))));
            var where = U.Box(""); where.Width = 220; where.ToolTip = "A teleport location name, e.g. Stormwind, Orgrimmar, Dalaran (Teleport locations on the editor side lists them).";
            var lvl = U.Box(c.Level.ToString()); lvl.Width = 70;
            cols.Children.Add(Section("Move", Live.F("Teleport location", "Works for offline characters too (moved at next login).", where),
                Btns(A("Teleport", () => string.IsNullOrWhiteSpace(where.Text) ? null : Gm.Teleport(c.Name, where.Text), true), A("Send home", Gm.Home(c.Name), true, "To their hearthstone location.")),
                U.T("Summon to me isn't available: the command line has no position in the world to summon to.", 12, "Muted", null, true, new Thickness(0, 6, 0, 0))));
            cols.Children.Add(Section("Character", U.Row(Live.F("Level", null, lvl), new Border { Width = 10 }, Btns(A("Set level", () => Gm.Level(c.Name, Gm.Int(lvl.Text, c.Level)), true))),
                U.T("At next login:", 12, "Muted", null, false, new Thickness(0, 6, 0, 2)),
                Btns(A("Pick a new name", $"character rename {c.Name}", true), A("Change looks", $"character customize {c.Name}", true), A("Change race", $"character changerace {c.Name}", true), A("Change faction", $"character changefaction {c.Name}", true)),
                Btns(A("Reset talents", $"reset talents {c.Name}", true), A("Reset spells", $"reset spells {c.Name}", true), A("Delete character", () => $"character delete {c.Name}", true, null, typed: true))));
            sp.Children.Add(cols);
            var subj = U.Box("From the staff"); subj.Width = 300;
            var body = U.Box(""); body.Width = 420;
            var g = U.Box("0"); g.Width = 60; var s2 = U.Box("0"); s2.Width = 50; var c2 = U.Box("0"); c2.Width = 50;
            var items = U.Box(""); items.Width = 260; items.ToolTip = "Item IDs, optionally with a count: 6948 2589:20 (up to 12 per mail)";
            sp.Children.Add(Section("Mail, gold and items", U.Row(Live.F("Subject", null, subj), new Border { Width = 10 }, Live.F("Text", null, body)),
                Btns(A("Send mail", () => Gm.Mail(c.Name, subj.Text, body.Text), true)),
                U.Row(Live.F("Gold", null, g), new Border { Width = 6 }, Live.F("Silver", null, s2), new Border { Width = 6 }, Live.F("Copper", null, c2), new Border { Width = 10 },
                    Btns(A("Send money", () => { var cp = Gm.Copper(Gm.Int(g.Text, 0), Gm.Int(s2.Text, 0), Gm.Int(c2.Text, 0)); return cp <= 0 ? null : Gm.Money(c.Name, subj.Text, body.Text, cp); }, true))),
                U.Row(Live.F("Items", "6948 2589:20", items), new Border { Width = 10 }, Btns(A("Send items", () => Gm.ItemList(items.Text) is string it ? Gm.Items(c.Name, subj.Text, body.Text, it) : null, true)))));
            sp.Children.Add(Section("What the server said", outBox));
            Content = new Border { BorderBrush = U.R("LineHi"), BorderThickness = new Thickness(1), Child = U.Scroll(sp) };
            Loaded += async (s, e) =>
            {
                try { var b = await Task.Run(() => H.Read(db => LiveData.BanStatus(db, c, H.Cfg.AuthDb))); banLbl.Text = b.Count == 0 ? "No bans or mutes." : string.Join("\n", b); banLbl.Foreground = U.R(b.Count == 0 ? "Muted" : "Bad"); }
                catch (Exception ex) { banLbl.Text = "Bans and mutes couldn't be read: " + Friendly.Text(ex); }
            };
        }

        static Border Section(string title, params UIElement[] kids) { var sp = U.Stack(U.Eyebrow(title)); foreach (var k in kids) { ((FrameworkElement)k).Margin = new Thickness(0, 0, 0, 6); sp.Children.Add(k); } return U.Soft(sp, new Thickness(16, 12, 16, 10), new Thickness(0, 0, 10, 10)); }
        static WrapPanel Btns(params Button[] b) { var p = new WrapPanel(); foreach (var x in b) { x.Margin = new Thickness(0, 0, 6, 6); p.Children.Add(x); } return p; }
        Button A(string label, string cmd, bool confirm = false, string tip = null) => A(label, () => cmd, confirm, tip);
        Button A(string label, Func<string> cmd, bool confirm = false, string tip = null, bool typed = false)
        {
            var b = Live.Gated(H, label, async () =>
            {
                var command = cmd();
                if (string.IsNullOrWhiteSpace(command)) { Live.Append(outBox, $"[{label}] check the fields next to it."); return; }
                if (typed)
                {
                    var t = TextAsk.Ask(this, label, $"This can't be undone from here. Type the character's name ({c.Name}) to confirm.", "");
                    if (t?.Trim() != c.Name) return;
                }
                else if (confirm && !Live.Confirm(this, label, $"{label}: {c.Name} on {c.RealmName}?" + (U.Technical ? "\n\nCommand: ." + command : ""), false).go) return;
                Live.Append(outBox, U.Technical ? "> ." + command : "> " + label);
                var res = await H.Run(c.Realm, command, label.ToLowerInvariant());
                Live.Append(outBox, (res.ok ? "" : "Didn't work: ") + res.text);
                if (label.Contains("ban", StringComparison.OrdinalIgnoreCase) || label.Contains("ute"))
                    try { var bs = await Task.Run(() => H.Read(db => LiveData.BanStatus(db, c, H.Cfg.AuthDb))); banLbl.Text = bs.Count == 0 ? "No bans or mutes." : string.Join("\n", bs); } catch { }
            }, null, () => c.Realm, tip);
            return b;
        }
    }

    // ============================================================================ Accounts
    public sealed class ScAccountsPage : IPage
    {
        readonly MainWindow w; ServerHub H => w.Hub;
        readonly Grid host = new Grid();
        readonly DataGrid grid = Live.Grid();
        readonly TextBox search = U.Box();
        readonly CheckBox bots = new CheckBox { Content = U.T("Show bot accounts", 12.5, "Text", null, false), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) };
        readonly TextBox outBox = Live.Out(90);
        List<AccountRow> all = new List<AccountRow>();
        bool loaded;
        public UIElement View => host;

        public ScAccountsPage(MainWindow w)
        {
            this.w = w;
            host.Margin = new Thickness(28, 22, 28, 20);
            host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            host.Children.Add(Live.Strip("Accounts", "Logins: make new ones, change passwords, staff rank and expansion, and ban or lift bans."));
            search.TextChanged += (s, e) => Apply(); bots.Checked += (s, e) => Apply(); bots.Unchecked += (s, e) => Apply();
            var sb = U.WithPlaceholder(search, "Account name"); sb.Width = 260;
            var top = U.Row(sb, bots, U.Btn("Refresh", () => _ = Load()), Live.Gated(H, "New account…", () => _ = Create(), "BtnPrimary", () => null, "Makes a login players (or staff) can use."));
            top.Margin = new Thickness(0, 0, 0, 10);
            Grid.SetRow(top, 1); host.Children.Add(top);
            Live.Col(grid, "Account", "Name"); Live.Col(grid, "Staff rank", "RankText", 140, null, "Rank"); Live.Col(grid, "Expansion", "ExpansionText", 220, null, "Expansion");
            Live.Col(grid, "Characters", "Characters", 90); Live.Col(grid, "Online", "OnlineText", 80, null, "Online"); Live.Col(grid, "Ban / mute", "Status", 100); Live.Col(grid, "Last login", "LastLogin", 150);
            Grid.SetRow(grid, 2); host.Children.Add(grid);
            var rank = new ComboBox { Width = 150 }; foreach (var r in Gm.Ranks) rank.Items.Add(r); rank.SelectedIndex = 0;
            var exp = new ComboBox { Width = 230 }; foreach (var e in Gm.Expansions) exp.Items.Add(e); exp.SelectedIndex = 2;
            var dur = U.Box("7d"); dur.Width = 70; dur.ToolTip = "30m, 12h, 7d; -1 = forever";
            var reason = U.Box("Server moderation"); reason.Width = 180;
            Button G(string t, Func<AccountRow, string> cmd, string tip) { var b = Live.Gated(H, t, () => _ = Act(t, cmd), null, () => null, tip); b.Margin = new Thickness(0, 0, 6, 6); return b; }
            var acts = new WrapPanel();
            acts.Children.Add(U.T("Selected account:", 12.5, "Muted", null, false, new Thickness(0, 6, 8, 0)));
            acts.Children.Add(G("New password…", a => { var p = TextAsk.Ask(w, "New password", $"New password for {a.Name} (letters and digits, up to 16; it isn't stored or logged).", ""); return string.IsNullOrWhiteSpace(p) || p.Length > 16 || !p.All(char.IsLetterOrDigit) ? null : Gm.AccountPassword(a.Name, p); }, "Sets a new password."));
            acts.Children.Add(rank); acts.Children.Add(G("Set staff rank", a => Gm.AccountRank(a.Name, rank.SelectedIndex), "Player, moderator, game master or administrator, on every realm."));
            acts.Children.Add(exp); acts.Children.Add(G("Set expansion", a => Gm.AccountExpansion(a.Name, exp.SelectedIndex), "Which expansions the account can play."));
            acts.Children.Add(U.T("Ban for", 12.5, "Muted", null, false, new Thickness(8, 6, 6, 0))); acts.Children.Add(dur); acts.Children.Add(reason);
            acts.Children.Add(G("Ban", a => Gm.IsDuration(dur.Text.Trim()) ? Gm.BanAccount(a.Name, dur.Text, reason.Text) : null, "Every character on the account is banned."));
            acts.Children.Add(G("Lift ban", a => Gm.UnbanAccount(a.Name), "Ends an account ban."));
            var card = U.Soft(U.Stack(acts, outBox), new Thickness(14, 10, 14, 10), new Thickness(0, 10, 0, 0));
            Grid.SetRow(card, 3); host.Children.Add(card);
        }

        public void OnShow() { H.Start(); if (!loaded && !w.IsScreens) { loaded = true; _ = Load(); } }
        public void ScreensData(List<AccountRow> rows) { loaded = true; all = rows; Apply(); }

        async Task Load()
        {
            try { var realms = H.Cfg.Realms.ToList(); var auth = H.Cfg.AuthDb; all = await Task.Run(() => H.Read(db => LiveData.Accounts(db, realms, auth))); Apply(); }
            catch (Exception ex) { w.Toast("Couldn't read the accounts: " + Friendly.Text(ex), "bad"); }
        }

        void Apply()
        {
            IEnumerable<AccountRow> q = all;
            if (bots.IsChecked != true) q = q.Where(a => !a.IsBot);
            var t = search.Text.Trim(); if (t.Length > 0) q = q.Where(a => a.Name.Contains(t, StringComparison.OrdinalIgnoreCase));
            grid.ItemsSource = q.ToList();
        }

        Realm AnyRealm => H.Cfg.Realms.FirstOrDefault(r => H.Blocked(r) == null);

        async Task Act(string what, Func<AccountRow, string> cmd)
        {
            if (grid.SelectedItem is not AccountRow a) { w.Toast("Select an account first.", "info"); return; }
            var c = cmd(a);
            if (c == null) { w.Toast("Check the fields next to the button.", "warn"); return; }
            if (!Live.Confirm(w, what, $"{what}: account {a.Name}?", false).go) return;
            var r = AnyRealm; if (r == null) return;
            var res = await H.Run(r, c, "account " + what.ToLowerInvariant());
            Live.Append(outBox, $"{a.Name}: {what} → {(res.ok ? "" : "didn't work: ")}{res.text}");
            await Load();
        }

        async Task Create()
        {
            var name = TextAsk.Ask(w, "New account", "Account name (letters and digits, up to 16):", "");
            if (string.IsNullOrWhiteSpace(name)) return;
            if (!Gm.IsName(name) || name.Length > 16) { w.Toast("Use letters and digits only, up to 16.", "warn"); return; }
            var pass = TextAsk.Ask(w, "New account", $"Password for {name} (letters and digits, up to 16; not stored or logged):", "");
            if (string.IsNullOrWhiteSpace(pass) || pass.Length > 16 || !pass.All(char.IsLetterOrDigit)) { w.Toast("Use letters and digits only, up to 16.", "warn"); return; }
            var r = AnyRealm; if (r == null) return;
            var res = await H.Run(r, Gm.AccountCreate(name, pass), "account create");
            Live.Append(outBox, $"{name}: {(res.ok ? "" : "didn't work: ")}{res.text}");
            await Load();
        }
    }

    // ============================================================================ Console + quick commands
    public sealed class ScConsolePage : IPage
    {
        readonly MainWindow w; ServerHub H => w.Hub;
        readonly ContentControl host = new ContentControl { Focusable = false };
        public UIElement View => host;
        readonly ComboBox realmBox = new ComboBox { Width = 260 };
        readonly TextBox input = U.Box("", true);
        readonly TextBox output = Live.Out(double.NaN);

        public ScConsolePage(MainWindow w)
        {
            this.w = w;
            var g = new Grid { Margin = new Thickness(28, 22, 28, 20) };
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            g.Children.Add(Live.Strip("Console", "Type any server command, or pick one from the list. It runs on the realm right away, as your GM login."));
            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
            Grid.SetRow(body, 1); g.Children.Add(body);
            foreach (var r in H.Cfg.Realms) realmBox.Items.Add(new ComboBoxItem { Content = r.Name, Tag = r });
            if (realmBox.Items.Count > 0) realmBox.SelectedIndex = 0;
            Realm Sel() => (realmBox.SelectedItem as ComboBoxItem)?.Tag as Realm;
            var send = Live.Gated(H, "Run", () => _ = Run(Sel()), "BtnPrimary", Sel, "Runs the command on the chosen realm (Enter).");
            realmBox.SelectionChanged += (s, e) => _ = H.Refresh();
            input.KeyDown += (s, e) => { if (e.Key == Key.Enter && send.IsEnabled) { e.Handled = true; _ = Run(Sel()); } };
            var line = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            DockPanel.SetDock(realmBox, Dock.Left); realmBox.Margin = new Thickness(0, 0, 10, 0); line.Children.Add(realmBox);
            DockPanel.SetDock(send, Dock.Right); send.Margin = new Thickness(10, 0, 0, 0); line.Children.Add(send);
            line.Children.Add(U.WithPlaceholder(input, "A server command, e.g. server info (no dot needed)"));
            var left = new DockPanel(); DockPanel.SetDock(line, Dock.Bottom); left.Children.Add(line); left.Children.Add(output);
            body.Children.Add(U.Soft(left, new Thickness(14), new Thickness(0)));
            var pal = new StackPanel();
            pal.Children.Add(U.Eyebrow("Quick commands"));
            pal.Children.Add(U.T("Click one to put it in the box, check or change it, then Run.", 12.5, "Muted", null, true, new Thickness(0, 0, 0, 8)));
            foreach (var e in CommandPalette.All)
            {
                var ee = e;
                var b = new Button { Style = U.S("BtnGhost"), HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 4), ToolTip = U.Technical ? "." + e.Command : e.What,
                    Content = U.Stack(U.T(e.Name, 13, "Text", FontWeights.SemiBold, false), U.T(e.What, 11.5, "Muted", null, true)) };
                b.Click += (s, x) => { input.Text = ee.Command; input.Focus(); input.CaretIndex = input.Text.Length; };
                pal.Children.Add(b);
            }
            var palCard = U.Soft(U.Scroll(pal), new Thickness(14), new Thickness(0)); Grid.SetColumn(palCard, 2); body.Children.Add(palCard);
            host.Content = g;
        }

        public void OnShow() => H.Start();

        async Task Run(Realm r)
        {
            var c = input.Text.Trim().TrimStart('.');
            if (c.Length == 0 || r == null) return;
            Live.Append(output, $"{r.Name}> {c}");
            var res = await H.Run(r, c, "console");
            Live.Append(output, res.text + Environment.NewLine);
            input.Clear();
        }
    }

    // ============================================================================ Logs (live)
    public sealed class ScLogsPage : IPage
    {
        readonly MainWindow w; ServerHub H => w.Hub;
        readonly Grid host = new Grid();
        public UIElement View => host;
        readonly ComboBox source = new ComboBox { Width = 240 }, file = new ComboBox { Width = 220 };
        readonly TextBox filter = U.Box();
        readonly CheckBox errorsOnly = new CheckBox { Content = U.T("Problems only", 12.5, "Text", null, false), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        readonly CheckBox follow = new CheckBox { IsChecked = true, Content = U.T("Follow live", 12.5, "Text", null, false), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        readonly ListBox lines = new ListBox();
        readonly TextBlock path = U.T("", 11.5, "Faint", null, false);
        List<(string name, string dir)> sources = new List<(string, string)>();

        public ScLogsPage(MainWindow w)
        {
            this.w = w;
            host.Margin = new Thickness(28, 22, 28, 20);
            host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            host.Children.Add(Live.Strip("Logs", "The server's logs as they're written, with problems in colour, and the audit log of every action taken from here."));
            sources = H.Cfg.Realms.Select(r => (r.Name, Path.Combine(H.Cfg.BinDir ?? "", r.LogDir ?? "logs"))).ToList();
            sources.Add(("Audit log (Studio)", H.AuditDir));
            if (!string.IsNullOrEmpty(H.Cfg.LegacyAuditDir)) sources.Add(("Audit log (old Control Panel)", H.Cfg.LegacyAuditDir));
            foreach (var s in sources) source.Items.Add(s.name);
            source.SelectionChanged += (s, e) => FillFiles();
            file.SelectionChanged += (s, e) => Load();
            filter.TextChanged += (s, e) => Load();
            errorsOnly.Checked += (s, e) => Load(); errorsOnly.Unchecked += (s, e) => Load();
            var fb = U.WithPlaceholder(filter, "Only lines containing…"); fb.Width = 220; fb.Margin = new Thickness(10, 0, 0, 0);
            var bar = U.Row(source, new Border { Width = 10 }, file, fb, errorsOnly, follow, new Border { Width = 10 },
                U.Btn("Open folder", () => { var d = Dir; if (d != null && Directory.Exists(d)) Process.Start(new ProcessStartInfo("explorer.exe", "\"" + d + "\"") { UseShellExecute = true }); }, "BtnGhost"));
            var top = U.Stack(bar, path); top.Margin = new Thickness(0, 0, 0, 10);
            Grid.SetRow(top, 1); host.Children.Add(top);
            lines.FontFamily = U.Mono; lines.FontSize = 12;
            VirtualizingPanel.SetIsVirtualizing(lines, true);
            Grid.SetRow(lines, 2); host.Children.Add(U.Soft(lines, new Thickness(6), new Thickness(0)));
            Grid.SetRow((UIElement)host.Children[host.Children.Count - 1], 2);
            if (source.Items.Count > 0) source.SelectedIndex = 0;
            H.StatusChanged += () => host.Dispatcher.Invoke(() => { if (w.CurrentPage == "sc_logs" && follow.IsChecked == true) Load(); });
        }

        string Dir => source.SelectedIndex >= 0 && source.SelectedIndex < sources.Count ? sources[source.SelectedIndex].dir : null;
        public void OnShow() { H.Start(); Load(); }

        void FillFiles()
        {
            file.Items.Clear();
            var d = Dir;
            var files = d != null && Directory.Exists(d) ? Directory.GetFiles(d, "*.log").Select(Path.GetFileName).OrderBy(f => f == "Server.log" ? 0 : f == "Errors.log" ? 1 : 2).ThenByDescending(f => f).ToList() : new List<string>();
            foreach (var f in files) file.Items.Add(f);
            if (files.Count > 0) file.SelectedIndex = 0; else { lines.Items.Clear(); path.Text = "No .log files in " + d; }
        }

        void Load()
        {
            if (Dir == null || file.SelectedItem == null) return;
            var p = Path.Combine(Dir, (string)file.SelectedItem);
            path.Text = p;
            var all = Logs.Tail(p, 800);
            var f = filter.Text.Trim();
            if (f.Length > 0) all = all.Where(l => l.Contains(f, StringComparison.OrdinalIgnoreCase)).ToList();
            if (errorsOnly.IsChecked == true) all = all.Where(l => Logs.Level(l) != "info").ToList();
            bool atEnd = lines.Items.Count == 0 || follow.IsChecked == true;
            lines.Items.Clear();
            foreach (var l in all)
            {
                var lv = Logs.Level(l);
                lines.Items.Add(new TextBlock { Text = l, TextWrapping = TextWrapping.NoWrap, Foreground = U.R(lv == "error" ? "Bad" : lv == "warn" ? "Amber" : "Text") });
            }
            if (atEnd && lines.Items.Count > 0) lines.ScrollIntoView(lines.Items[lines.Items.Count - 1]);
        }
    }

    // ============================================================================ Messages & schedule
    public sealed class ScSchedulePage : IPage
    {
        readonly MainWindow w; ServerHub H => w.Hub;
        readonly ContentControl host = new ContentControl { Focusable = false };
        readonly ContentControl taskList = new ContentControl { Focusable = false };
        readonly TextBox outBox = Live.Out(120);
        public UIElement View => host;

        public ScSchedulePage(MainWindow w)
        {
            this.w = w;
            var sp = new StackPanel { Margin = new Thickness(28, 22, 28, 20) };
            sp.Children.Add(Live.Strip("Messages & schedule", "Message everyone now, count down to a shutdown, and set up repeating messages, restarts and backups."));
            // now
            var msg = U.Box("");
            var screen = new CheckBox { Content = U.T("In the middle of the screen too", 12.5, "Text", null, false), Margin = new Thickness(0, 6, 0, 0) };
            var to = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            foreach (var r in H.Cfg.Realms) { var rr = r; var b = Live.Gated(H, "To " + r.Name, () => _ = Say(new[] { rr }, msg.Text, screen.IsChecked == true), null, () => rr); b.Margin = new Thickness(0, 0, 8, 0); to.Children.Add(b); }
            var bAll = Live.Gated(H, "To every realm", () => _ = Say(H.Cfg.Realms.Where(r => H.Blocked(r) == null).ToArray(), msg.Text, screen.IsChecked == true), "BtnPrimary", () => null); to.Children.Add(bAll);
            sp.Children.Add(U.Soft(U.Stack(U.Eyebrow("Message everyone now"), MsgBox(msg), screen, to), new Thickness(18, 14, 18, 14), new Thickness(0, 0, 0, 12)));
            // countdown
            var realm = new ComboBox { Width = 240 }; foreach (var r in H.Cfg.Realms) realm.Items.Add(new ComboBoxItem { Content = r.Name, Tag = r }); if (realm.Items.Count > 0) realm.SelectedIndex = 0;
            Realm Sel() => (realm.SelectedItem as ComboBoxItem)?.Tag as Realm;
            var mins = U.Box("5"); mins.Width = 70;
            var shut = Live.Gated(H, "Start the countdown", async () =>
            {
                var r = Sel(); var n = Gm.Int(mins.Text, -1);
                if (r == null || n < 0) { w.Toast("Enter the minutes as a number.", "warn"); return; }
                if (!Live.Confirm(w, "Shut down " + r.Name, $"{r.Name} shuts down in {n} minute(s); players see the countdown. Start it again from the dashboard afterwards.", false).go) return;
                H.Dog.Expect(r.Id);
                var res = await H.Run(r, Gm.Shutdown(n * 60), "shutdown countdown"); Live.Append(outBox, $"{r.Name}: shut down in {n} min → {res.text}");
            }, null, Sel);
            var cancel = Live.Gated(H, "Cancel it", async () => { var r = Sel(); if (r == null) return; H.Dog.Clear(r.Id); var res = await H.Run(r, Gm.ShutdownCancel, "shutdown cancel"); Live.Append(outBox, $"{r.Name}: countdown cancelled → {res.text}"); }, null, Sel);
            realm.SelectionChanged += (s, e) => _ = H.Refresh();
            sp.Children.Add(U.Soft(U.Stack(U.Eyebrow("Shutdown countdown"), U.Row(realm, new Border { Width = 10 }, U.T("in", 12.5, "Muted", null, false, new Thickness(0, 8, 6, 0)), mins, U.T("minutes", 12.5, "Muted", null, false, new Thickness(6, 8, 10, 0)), shut, new Border { Width = 8 }, cancel)), new Thickness(18, 14, 18, 14), new Thickness(0, 0, 0, 12)));
            // scheduled tasks
            var add = new WrapPanel();
            Button Add(string t, Func<ScheduledTask> make) { var b = U.Btn(t, () => { var task = make(); if (task == null) return; H.Cfg.Tasks.Add(task); H.Save(); BuildTasks(); }, null); b.Margin = new Thickness(0, 0, 8, 0); return b; }
            add.Children.Add(Add("Add a repeating message…", () =>
            {
                var m = TextAsk.Ask(w, "Repeating message", "What should players read?", "Join our Discord!"); if (string.IsNullOrWhiteSpace(m)) return null;
                var every = Gm.Int(TextAsk.Ask(w, "Repeating message", "Every how many minutes?", "30"), 0); if (every <= 0) return null;
                return new ScheduledTask { Kind = "announce", Message = m.Trim(), EveryMinutes = every };
            }));
            add.Children.Add(Add("Add a daily restart…", () =>
            {
                var at = TextAsk.Ask(w, "Daily restart", "At what time (24-hour, HH:mm)?", "05:00"); if (string.IsNullOrWhiteSpace(at)) return null;
                var warn = Gm.Int(TextAsk.Ask(w, "Daily restart", "How many minutes of warning should players get?", "15"), 15);
                return new ScheduledTask { Kind = "restart", DailyAt = at.Trim(), WarnMinutes = Math.Max(1, warn) };
            }));
            add.Children.Add(Add("Add a daily backup…", () =>
            {
                var at = TextAsk.Ask(w, "Daily backup", "At what time (24-hour, HH:mm)?", "04:30"); if (string.IsNullOrWhiteSpace(at)) return null;
                return new ScheduledTask { Kind = "backup", DailyAt = at.Trim() };
            }));
            sp.Children.Add(U.Soft(U.Stack(U.Eyebrow("Scheduled"), U.T("These run while Studio is open (the old daily backup script on the dashboard runs on its own).", 12.5, "Muted", null, true, new Thickness(0, 0, 0, 10)), taskList, add), new Thickness(18, 14, 18, 14), new Thickness(0, 0, 0, 12)));
            sp.Children.Add(U.Soft(U.Stack(U.Eyebrow("What the server said"), outBox), new Thickness(18, 14, 18, 14), new Thickness(0)));
            host.Content = U.Scroll(sp);
            BuildTasks();
        }

        public void OnShow() { H.Start(); BuildTasks(); }
        static Grid MsgBox(TextBox msg) { var g = U.WithPlaceholder(msg, "What players should read"); g.Width = 640; g.HorizontalAlignment = HorizontalAlignment.Left; return g; }

        void BuildTasks()
        {
            var sp = new StackPanel();
            if (H.Cfg.Tasks.Count == 0) sp.Children.Add(U.T("Nothing scheduled yet.", 12.5, "Muted", null, false, new Thickness(0, 0, 0, 10)));
            foreach (var t in H.Cfg.Tasks.ToList())
            {
                var tt = t;
                var on = new CheckBox { IsChecked = t.Enabled, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), ToolTip = "On / off" };
                on.Checked += (s, e) => { tt.Enabled = true; H.Save(); BuildTasks(); }; on.Unchecked += (s, e) => { tt.Enabled = false; H.Save(); BuildTasks(); };
                var next = Schedule.Next(t, DateTime.Now);
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
                var del = U.Btn("Remove", () => { H.Cfg.Tasks.Remove(tt); H.Save(); BuildTasks(); }, "BtnGhost"); DockPanel.SetDock(del, Dock.Right); row.Children.Add(del);
                DockPanel.SetDock(on, Dock.Left); row.Children.Add(on);
                row.Children.Add(U.Stack(U.T(t.Describe(H.Cfg.Realms), 13, "Text", FontWeights.SemiBold, true), U.T(t.Enabled ? (next == null ? "No valid time." : "Next: " + next.Value.ToString("ddd HH:mm")) : "Off", 12, "Muted", null, false)));
                sp.Children.Add(row);
            }
            taskList.Content = sp;
        }

        async Task Say(Realm[] realms, string text, bool screen)
        {
            if (string.IsNullOrWhiteSpace(text)) { w.Toast("Type the message first.", "info"); return; }
            foreach (var r in realms)
            {
                var res = await H.Run(r, Gm.Announce(text), "message everyone"); Live.Append(outBox, $"{r.Name}: {res.text}");
                if (screen) { var r2 = await H.Run(r, Gm.Notify(text), "message on screen"); Live.Append(outBox, $"{r.Name} (screen): {r2.text}"); }
            }
        }
    }

    // ============================================================================ Backups
    public sealed class ScBackupsPage : IPage
    {
        readonly MainWindow w; ServerHub H => w.Hub;
        readonly Grid host = new Grid();
        readonly DataGrid grid = Live.Grid();
        public UIElement View => host;

        public ScBackupsPage(MainWindow w)
        {
            this.w = w;
            host.Margin = new Thickness(28, 22, 28, 20);
            host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            host.Children.Add(Live.Strip("Backups", "Every backup taken by Server control, Studio's Apply and the old Control Panel: back up now, look inside, or put one back."));
            var dbs = new WrapPanel();
            var boxes = new List<(CheckBox cb, string db)>();
            foreach (var d in H.AllDatabases) { var cb = new CheckBox { IsChecked = true, Content = U.T(DbLabel(d), 12.5, "Text", null, false), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center }; boxes.Add((cb, d)); dbs.Children.Add(cb); }
            var go = U.Btn("Back up now", async () =>
            {
                var pick = boxes.Where(b => b.cb.IsChecked == true).Select(b => b.db).ToList(); if (pick.Count == 0) return;
                Mouse.OverrideCursor = Cursors.Wait;
                try { var res = await H.Backup(pick, "manual"); w.Toast(res.ok ? "Backup written: " + res.msg : "Backup failed: " + res.msg, res.ok ? "ok" : "bad"); } finally { Mouse.OverrideCursor = null; }
                Load();
            }, "BtnPrimary", "mysqldump of the ticked databases, into " + (H.Cfg.BackupDir ?? ""));
            go.IsEnabled = !w.IsScreens;
            var top = U.Soft(U.Stack(U.Eyebrow("Back up now"), U.Row(dbs, go)), new Thickness(16, 12, 16, 12), new Thickness(0, 0, 0, 10));
            Grid.SetRow(top, 1); host.Children.Add(top);
            Live.Col(grid, "When", "WhenText", 150, null, "When"); Live.Col(grid, "Name", "Name", 340); Live.Col(grid, "Size", "Size", 100, null, "Bytes"); Live.Col(grid, "Files", "FilesText");
            var acts = U.Row(U.Btn("Open folder", () => { if (grid.SelectedItem is BackupList.Entry e) Process.Start(new ProcessStartInfo("explorer.exe", "\"" + e.Path + "\"") { UseShellExecute = true }); }, null),
                U.Btn("Put a database back…", () => _ = Restore(), null, "Restores one .sql file from the selected backup into its database, after a fresh safety backup."), U.Btn("Refresh", Load, "BtnGhost"));
            acts.Margin = new Thickness(0, 10, 0, 0);
            var dock = new DockPanel(); DockPanel.SetDock(acts, Dock.Bottom); dock.Children.Add(acts); dock.Children.Add(grid);
            Grid.SetRow(dock, 2); host.Children.Add(dock);
        }

        string DbLabel(string d) => d == H.Cfg.AuthDb ? "Logins (" + d + ")" : d == H.Cfg.WorldDb ? "World (" + d + ")" : (H.Cfg.Realms.FirstOrDefault(r => r.CharDb == d)?.Name ?? "Characters") + " characters (" + d + ")";

        public void OnShow() => Load();
        void Load() => grid.ItemsSource = BackupList.Scan(new[] { H.Cfg.BackupDir, w.S.Profiles.Settings.BackupRoot });

        async Task Restore()
        {
            if (grid.SelectedItem is not BackupList.Entry e) { w.Toast("Select a backup first.", "info"); return; }
            var sqls = Directory.GetFiles(e.Path, "*.sql", SearchOption.AllDirectories);
            if (sqls.Length == 0) { w.Toast("That backup has no .sql files (it may be a DBC or zip backup).", "info"); return; }
            var dlg = new PickerWindow(w, "Put a database back", "Which file? Its database is overwritten with it.", q => sqls.Select((f, i) => ((long)i, Path.GetFileName(f), $"{new FileInfo(f).Length / 1048576.0:0.0} MB")).Where(x => x.Item2.Contains(q ?? "", StringComparison.OrdinalIgnoreCase)).ToList(), "");
            if (dlg.ShowDialog() != true || dlg.Picked == null) return;
            var file = sqls[(int)dlg.Picked.Value];
            var db = H.AllDatabases.FirstOrDefault(d => Path.GetFileName(file).StartsWith(d, StringComparison.OrdinalIgnoreCase)) ?? TextAsk.Ask(w, "Which database?", "Couldn't tell which database this file belongs to. Type its name:", "");
            if (string.IsNullOrWhiteSpace(db)) return;
            var typed = TextAsk.Ask(w, "Put a database back", $"This replaces everything in {db} with {Path.GetFileName(file)}. Stop the realms first. Studio backs {db} up again before it starts.\n\nType {db} to confirm.", "");
            if (typed?.Trim() != db) return;
            Mouse.OverrideCursor = Cursors.Wait;
            try { var res = await H.Restore(file, db); w.Toast(res.ok ? res.msg : "Not restored: " + res.msg, res.ok ? "ok" : "bad"); }
            finally { Mouse.OverrideCursor = null; }
            Load();
        }
    }

    // ============================================================================ Settings
    public sealed class ScSettingsPage : IPage
    {
        readonly MainWindow w; ServerHub H => w.Hub;
        readonly ContentControl host = new ContentControl { Focusable = false };
        public UIElement View => host;
        public ScSettingsPage(MainWindow w) { this.w = w; Build(); }
        public void OnShow() => Build();

        void Build()
        {
            var c = H.Cfg;
            var sp = new StackPanel { Margin = new Thickness(28, 22, 28, 20), MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Left };
            sp.Children.Add(Live.Strip("Server control settings", "The GM login, the server's folders and ports, and its realms."));
            var imp = U.Btn("Import from the old Control Panel", () => { var m = H.ImportControlPanel(); w.Toast(m ?? "Nothing to import.", "info"); w.ReloadServerControl(); }, "BtnPrimary", "Reads its settings (its file isn't changed): realms, ports, folders and the GM login.");
            sp.Children.Add(U.Soft(U.Stack(U.Eyebrow("Old Control Panel"), U.T(File.Exists(ServerHub.ControlPanelSettings) ? "Its settings are on this PC. Import them once; the old program keeps working on its own." : "Its settings file wasn't found on this PC.", 12.5, "Muted", null, true, new Thickness(0, 0, 0, 8)), imp), new Thickness(18, 14, 18, 14), new Thickness(0, 0, 0, 12)));

            var user = U.Box(c.SoapUser); user.Width = 220;
            var pass = new PasswordBox { Width = 220, Height = 34 };
            var gm = U.Stack(U.Eyebrow("GM login"), U.T("An account with staff rank Administrator (3) or higher. Commands go to the realm's command port (SOAP) as this account. The password is encrypted for your Windows user and never shown.", 12.5, "Muted", null, true, new Thickness(0, 0, 0, 8)),
                U.Row(Live.F("Account", null, user), new Border { Width = 10 }, Live.F("Password", H.HasGmLogin ? "Saved. Type a new one to change it." : "Not saved yet.", pass)),
                U.Row(U.Btn("Save login", () => { H.SetGmLogin(user.Text, pass.Password); pass.Clear(); w.Toast("GM login saved (encrypted).", "ok"); _ = H.Refresh(); Build(); }, "BtnPrimary"),
                      U.Btn("Forget the password", () => { H.ForgetGmLogin(); Build(); }, "BtnGhost")));
            sp.Children.Add(U.Soft(gm, new Thickness(18, 14, 18, 14), new Thickness(0, 0, 0, 12)));

            var fields = new List<(string label, string hint, Func<string> get, Action<string> set)>
            {
                ("Server folder", "Where worldserver.exe and authserver.exe are.", () => c.BinDir, v => c.BinDir = v),
                ("MySQL programs folder", "Where mysqldump.exe and mysql.exe are (backups).", () => c.MysqlBin, v => c.MysqlBin = v),
                ("Backups folder", "Where Server control's backups go.", () => c.BackupDir, v => c.BackupDir = v),
                ("Sign-up website folder", "The PHP sign-up site.", () => c.SignupDir, v => c.SignupDir = v),
                ("php.exe", null, () => c.PhpExe, v => c.PhpExe = v), ("ngrok.exe", "The internet tunnel for the sign-up site.", () => c.NgrokExe, v => c.NgrokExe = v),
                ("Login database", "Usually acore_auth.", () => c.AuthDb, v => c.AuthDb = v), ("World database", "Usually acore_world.", () => c.WorldDb, v => c.WorldDb = v),
            };
            var fp = new StackPanel();
            var boxes = new List<TextBox>();
            foreach (var f in fields) { var b = U.Box(f.get() ?? ""); b.Width = 620; boxes.Add(b); var fe = Live.F(f.label, f.hint, b); fe.Margin = new Thickness(0, 0, 0, 8); fp.Children.Add(fe); }
            var authPort = U.Box(c.AuthPort.ToString()); authPort.Width = 90; var signPort = U.Box(c.SignupPort.ToString()); signPort.Width = 90; var maxR = U.Box(c.AutoRestartMaxPerHour.ToString()); maxR.Width = 70;
            fp.Children.Add(U.Row(Live.F("Login server port", null, authPort), new Border { Width = 10 }, Live.F("Sign-up website port", null, signPort), new Border { Width = 10 }, Live.F("Crash restarts per hour (max)", null, maxR)));
            sp.Children.Add(U.Soft(U.Stack(U.Eyebrow("Folders and ports"), fp), new Thickness(18, 14, 18, 14), new Thickness(0, 0, 0, 12)));

            var rp = new StackPanel();
            rp.Children.Add(U.Eyebrow("Realms"));
            rp.Children.Add(U.T("Each game world the server runs. Start script: leave empty to start worldserver.exe directly, or name a .ps1 in the server folder that starts it with its own settings.", 12.5, "Muted", null, true, new Thickness(0, 0, 0, 8)));
            var rows = new List<(Realm r, TextBox[] b, CheckBox bots)>();
            foreach (var r in c.Realms)
            {
                var b = new[] { U.Box(r.Name), U.Box(r.WorldPort.ToString()), U.Box(r.SoapPort.ToString()), U.Box(r.CharDb), U.Box(r.LogDir), U.Box(r.LaunchScript) };
                b[0].Width = 170; b[1].Width = 70; b[2].Width = 70; b[3].Width = 150; b[4].Width = 120; b[5].Width = 210;
                var bots = new CheckBox { IsChecked = r.HasBots, Content = U.T("has bots", 12, "Text", null, false), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8, 0, 0, 8) };
                rows.Add((r, b, bots));
                var line = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
                foreach (var (lbl, box) in new[] { ("Name", b[0]), ("Game port", b[1]), ("Command port", b[2]), ("Characters database", b[3]), ("Log folder", b[4]), ("Start script", b[5]) }) { var fe = Live.F(lbl, null, box); fe.Margin = new Thickness(0, 0, 8, 0); line.Children.Add(fe); }
                line.Children.Add(bots);
                var rr = r;
                var rm = U.Btn("Remove", () => { c.Realms.Remove(rr); H.Save(); Build(); }, "BtnGhost"); rm.VerticalAlignment = VerticalAlignment.Bottom; line.Children.Add(rm);
                rp.Children.Add(line);
            }
            rp.Children.Add(U.Btn("Add a realm", () => { c.Realms.Add(new Realm { Id = c.Realms.Count == 0 ? 1 : c.Realms.Max(x => x.Id) + 1, Name = "New realm" }); H.Save(); Build(); }, "BtnGhost"));
            sp.Children.Add(U.Soft(rp, new Thickness(18, 14, 18, 14), new Thickness(0, 0, 0, 12)));

            var save = U.Btn("Save settings", () =>
            {
                for (int i = 0; i < fields.Count; i++) fields[i].set(boxes[i].Text.Trim());
                c.AuthPort = Gm.Int(authPort.Text, c.AuthPort); c.SignupPort = Gm.Int(signPort.Text, c.SignupPort); c.AutoRestartMaxPerHour = Math.Max(1, Gm.Int(maxR.Text, 3));
                foreach (var (r, b, bots) in rows) { r.Name = b[0].Text.Trim(); r.WorldPort = Gm.Int(b[1].Text, r.WorldPort); r.SoapPort = Gm.Int(b[2].Text, r.SoapPort); r.CharDb = b[3].Text.Trim(); r.LogDir = b[4].Text.Trim(); r.LaunchScript = b[5].Text.Trim(); r.HasBots = bots.IsChecked == true; }
                H.Save(); w.Toast("Saved.", "ok"); w.ReloadServerControl();
            }, "BtnPrimary");
            sp.Children.Add(save);
            host.Content = U.Scroll(sp);
        }
    }
}
