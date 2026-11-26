using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;
using AndersCraftStudio.Core;
using AndersCraftStudio.Core.Live;

namespace AndersCraftStudio
{
    /// <summary>
    /// Everything the Server control tab shares: its settings, the live status (refreshed every few seconds), the server's
    /// command line (SOAP), starting and stopping, backups, the scheduled tasks and the crash watchdog. Unlike the editor
    /// side, everything here acts on the running server straight away; every action is written to the audit log.
    /// </summary>
    public sealed class ServerHub
    {
        readonly MainWindow w;
        Studio S => w.S;
        public ServerSettings Cfg { get; private set; }
        public string SettingsPath => Path.Combine(S.Profiles.Root, "server_control.json");
        public string AuditDir => Path.Combine(S.Profiles.Settings.Workspace(), "audit");
        public bool Screens => w.IsScreens;

        public sealed class Snapshot
        {
            public CompStatus Auth = new CompStatus { Key = "auth", Name = "Login server" }, Signup = new CompStatus { Key = "signup", Name = "Sign-up website" }, Tunnel = new CompStatus { Key = "tunnel", Name = "Sign-up tunnel" }, BackupLoop = new CompStatus { Key = "bak", Name = "Daily backup" };
            public Dictionary<int, CompStatus> Worlds = new Dictionary<int, CompStatus>();
            public Dictionary<int, bool> Soap = new Dictionary<int, bool>();
            public Dictionary<int, (int real, int bots)> Online = new Dictionary<int, (int, int)>();
            public bool DbOk; public string DbError;
            public DateTime At = DateTime.Now;
        }
        public Snapshot Now { get; private set; } = new Snapshot();
        public event Action StatusChanged;
        public readonly List<string> Activity = new List<string>();
        public event Action<string> ActivityAdded;

        readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        readonly CpuSampler cpu = new CpuSampler();
        public readonly Watchdog Dog = new Watchdog();
        int tick; bool refreshing;
        public bool Busy { get; private set; }

        public ServerHub(MainWindow w)
        {
            this.w = w;
            Cfg = ServerSettingsStore.Load(SettingsPath);
            if (!File.Exists(SettingsPath) && !Screens) ImportControlPanel(quiet: true);
            timer.Tick += async (s, e) => await Refresh();
        }

        public bool Started => timer.IsEnabled;

        /// <summary>Screenshots: neutral sample settings in a throw-away folder (the live status probes stay read-only).</summary>
        public void ScreensSample(string dir)
        {
            Directory.CreateDirectory(Path.Combine(dir, "logs")); Directory.CreateDirectory(Path.Combine(dir, "logs_hardcore")); Directory.CreateDirectory(Path.Combine(dir, "backups"));
            File.WriteAllLines(Path.Combine(dir, "logs", "Server.log"), new[]
            {
                "2026-11-25_18:00:01 Using configuration file worldserver.conf",
                "2026-11-25_18:00:04 Loading Spell Dbc Data...",
                "2026-11-25_18:00:09 >> Loaded 49858 spells in 1203 ms",
                "2026-11-25_18:00:12 WARN Table `creature_addon` has invalid path 0 for creature guid 12345",
                "2026-11-25_18:00:15 >> Loaded 7631 quests definitions",
                "2026-11-25_18:00:21 World initialized in 0 minutes 20 seconds",
                "2026-11-25_18:00:21 AzerothCore rev. sample ready...",
                "2026-11-25_18:20:44 ERROR Spell 900001 has invalid effect 0 target, skipped",
                "2026-11-25_18:31:02 Player Sample1 logged in",
            });
            File.WriteAllLines(Path.Combine(dir, "logs", "Errors.log"), new[] { "2026-11-25_18:20:44 ERROR Spell 900001 has invalid effect 0 target, skipped" });
            foreach (var (n, f) in new[] { ("2026-11-24_050000_server_scheduled", "acore_auth_.sql"), ("2026-11-25_120312_server_manual", "acore_characters_.sql"), ("2026-11-25_163001_server_restart_1", "acore_world_.sql") })
            { var d = Path.Combine(dir, "backups", n); Directory.CreateDirectory(d); File.WriteAllText(Path.Combine(d, f), new string('-', 300000)); }
            Cfg = new ServerSettings
            {
                BinDir = dir, BackupDir = Path.Combine(dir, "backups"), SoapUser = "",
                Realms = new List<Realm>
                {
                    new Realm { Id = 1, Name = "Main realm", WorldPort = 8085, SoapPort = 7878, CharDb = "acore_characters", LogDir = "logs", HasBots = true, AutoRestart = true },
                    new Realm { Id = 2, Name = "Hardcore realm", WorldPort = 8086, SoapPort = 7879, CharDb = "achc_characters", LogDir = "logs_hardcore", LaunchScript = "ServerLauncher_Hardcore.ps1" },
                },
                Tasks = new List<ScheduledTask>
                {
                    new ScheduledTask { Kind = "announce", Message = "Double XP weekend is on!", EveryMinutes = 60 },
                    new ScheduledTask { Kind = "restart", DailyAt = "05:00", WarnMinutes = 15 },
                    new ScheduledTask { Kind = "backup", DailyAt = "04:30" },
                },
            };
        }
        public void Start() { if (!timer.IsEnabled) { timer.Start(); _ = Refresh(true); } }
        /// <summary>Stops the refresh / scheduled-task timer (the window that owned it is closing).</summary>
        public void Stop() => timer.Stop();
        public void Save() { if (!Screens) ServerSettingsStore.Save(SettingsPath, Cfg); }

        // ------------------------------------------------------------------ the old Control Panel's settings
        public static string ControlPanelSettings => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AndersCraftControlPanel", "settings.json");

        /// <summary>Reads the old Control Panel's settings (its file is only read, never changed) and re-encrypts its GM password for Studio.</summary>
        public string ImportControlPanel(bool quiet = false)
        {
            var root = GuessRoot();
            try
            {
                ServerSettings s;
                if (File.Exists(ControlPanelSettings))
                {
                    var entropy = Encoding.UTF8.GetBytes("AndersCraft.ControlPanel.v2");
                    s = ServerSettingsStore.FromControlPanel(File.ReadAllText(ControlPanelSettings), enc =>
                    {
                        try { var plain = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(enc), entropy, DataProtectionScope.CurrentUser)); return S.Secrets.Protect(plain); }
                        catch { return ""; }
                    });
                    var d = ServerSettingsStore.ControlPanelDefaults(root ?? "");
                    if (s.Realms.Count == 0) s.Realms = d.Realms;
                    s.LegacyAuditDir = d.LegacyAuditDir;
                }
                else if (root != null) s = ServerSettingsStore.ControlPanelDefaults(root);
                else return quiet ? null : "The old Control Panel's settings weren't found.";
                if (string.IsNullOrEmpty(s.BinDir) && !string.IsNullOrEmpty(S.Profile?.ConfPath)) s.BinDir = Path.GetDirectoryName(Path.GetDirectoryName(S.Profile.ConfPath));
                s.Tasks = Cfg?.Tasks ?? new List<ScheduledTask>();
                Cfg = s; Save();
                return $"Imported: {s.Realms.Count} realm(s), folders, ports" + (string.IsNullOrEmpty(s.SoapPassEnc) ? "." : " and the GM login.");
            }
            catch (Exception ex) { return quiet ? null : "Couldn't import: " + Friendly.Text(ex); }
        }

        /// <summary>The folder above the server checkout (the folder holding azerothcore-wotlk), when Studio's profile points into one.</summary>
        string GuessRoot()
        {
            var conf = S.Profile?.ConfPath;
            if (string.IsNullOrEmpty(conf)) return null;
            var d = new DirectoryInfo(Path.GetDirectoryName(conf));
            while (d != null && !d.Name.Equals("azerothcore-wotlk", StringComparison.OrdinalIgnoreCase)) d = d.Parent;
            return d?.Parent?.FullName;
        }

        // ------------------------------------------------------------------ GM login (SOAP)
        public bool HasGmLogin => !string.IsNullOrWhiteSpace(Cfg.SoapUser) && !string.IsNullOrEmpty(Cfg.SoapPassEnc);
        public void SetGmLogin(string user, string pass) { Cfg.SoapUser = user.Trim(); if (!string.IsNullOrEmpty(pass)) Cfg.SoapPassEnc = S.Secrets.Protect(pass); Save(); }
        public void ForgetGmLogin() { Cfg.SoapPassEnc = ""; Save(); }

        /// <summary>Why commands can't go to a realm right now (null = they can).</summary>
        public string Blocked(Realm r)
        {
            if (!HasGmLogin) return "Save a GM login first (Server control › Settings).";
            if (r == null) return Cfg.Realms.Any(x => Now.Soap.TryGetValue(x.Id, out var up) && up) ? null : "No realm's command port answers. Is a realm running with SOAP on?";
            if (!(Now.Soap.TryGetValue(r.Id, out var u) && u)) return $"{r.Name} isn't answering on its command port ({r.SoapPort}). Is it running with SOAP on?";
            return null;
        }

        /// <summary>Runs a server command on a realm right now, and writes it to the audit log.</summary>
        public async Task<(bool ok, string text)> Run(Realm r, string command, string action = "command")
        {
            if (Screens) return (true, "(screenshot mode: nothing sent)");
            var res = await Soap.Exec(Cfg.SoapUser, S.Secrets.Unprotect(Cfg.SoapPassEnc), r.SoapPort, command);
            Audit.Write(AuditDir, r.Name, action, command, res.ok ? "ok: " + res.text : "FAILED: " + res.text);
            return res;
        }

        public void Log(string s)
        {
            var line = $"{DateTime.Now:HH:mm:ss}  {s}";
            Activity.Add(line); if (Activity.Count > 500) Activity.RemoveAt(0);
            ActivityAdded?.Invoke(line);
        }

        // ------------------------------------------------------------------ reading the databases (read-only)
        /// <summary>
        /// A read-only connection for the login and character databases: Studio's own read-only account, or (if that account
        /// can't see them) the server's login from worldserver.conf in a READ ONLY session, read at run time and never stored.
        /// </summary>
        public T Read<T>(Func<ISqlReader, T> q, Func<T, bool> complete = null)
        {
            Exception first = null;
            if (S.HasReadAccount)
            {
                try
                {
                    using var db = S.OpenRead(Cfg.AuthDb);
                    var r = q(db);
                    // Studio's read-only account may not see every character database (e.g. a second realm's): then try the server's login
                    if (complete == null || complete(r) || Screens || ConfLogin() == null) return r;
                }
                catch (Exception ex) { first = ex; }
            }
            if (Screens) throw first ?? new InvalidOperationException("No read-only account.");
            var login = ConfLogin();
            if (login == null) throw first ?? new InvalidOperationException("Neither Studio's read-only account nor the server's login (worldserver.conf) could be used.");
            using var db2 = new ReadOnlyDb(login.Host, login.Port, login.User, login.Password, Cfg.AuthDb);
            return q(db2);
        }

        DbInfo ConfLogin() { try { var p = File.Exists(Cfg.ConfPath) ? Cfg.ConfPath : S.Profile?.ConfPath; return p != null && File.Exists(p) ? ConfParser.ParseFile(p).Login : null; } catch { return null; } }

        // ------------------------------------------------------------------ status
        public async Task Refresh(bool full = false)
        {
            if (refreshing) return;
            refreshing = true;
            try
            {
                bool doDb = full || tick % 4 == 0; tick++;
                var realms = Cfg.Realms.ToList(); var c = Cfg;
                var snap = await Task.Run(() =>
                {
                    var s = new Snapshot();
                    var lis = Procs.ListenerPids();
                    void Fill(CompStatus st, Process p, int port)
                    {
                        st.Running = p != null; st.Pid = p?.Id ?? 0; st.Started = p != null ? Procs.StartTime(p.Id) : null;
                        st.Ready = port > 0 ? Procs.Probe(port) : p != null;
                        if (p != null) { var (cu, me) = cpu.Sample(p.Id); st.CpuPercent = cu; st.MemoryBytes = me; }
                    }
                    Fill(s.Auth, Process.GetProcessesByName("authserver").FirstOrDefault(), c.AuthPort);
                    var wp = Procs.WorldPids(realms, lis);
                    foreach (var r in realms)
                    {
                        var cs = new CompStatus { Key = "w" + r.Id, Name = r.Name };
                        if (wp.TryGetValue(r.Id, out int pid)) { cs.Running = true; cs.Pid = pid; cs.Started = Procs.StartTime(pid); var (cu, me) = cpu.Sample(pid); cs.CpuPercent = cu; cs.MemoryBytes = me; }
                        cs.Ready = Procs.Probe(r.WorldPort);
                        cs.Detail = Logs.Tail(c.LogPath(r, "Server.log"), 1).FirstOrDefault() ?? "";
                        s.Worlds[r.Id] = cs;
                        s.Soap[r.Id] = Procs.Probe(r.SoapPort);
                    }
                    Fill(s.Signup, Process.GetProcessesByName("php").FirstOrDefault(), c.SignupPort);
                    Fill(s.Tunnel, Process.GetProcessesByName("ngrok").FirstOrDefault(), 0);
                    var bl = Procs.FindBackupLoop();
                    s.BackupLoop.Running = s.BackupLoop.Ready = bl > 0; s.BackupLoop.Pid = bl; s.BackupLoop.Started = bl > 0 ? Procs.StartTime(bl) : null;
                    return s;
                });
                if (doDb)
                {
                    try { snap.Online = await Task.Run(() => Read(db => LiveData.Online(db, realms, c.AuthDb), d => realms.All(r => d.ContainsKey(r.Id)))); snap.DbOk = true; }
                    catch (Exception ex) { snap.DbOk = false; snap.DbError = Friendly.Text(ex); }
                }
                else { snap.Online = Now.Online; snap.DbOk = Now.DbOk; snap.DbError = Now.DbError; }
                Now = snap;
                await Watch();
                await RunDueTasks();
                StatusChanged?.Invoke();
            }
            catch (Exception ex) { Log("Status refresh failed: " + Friendly.Text(ex)); }
            finally { refreshing = false; }
        }

        async Task Watch()
        {
            foreach (var r in Cfg.Realms)
            {
                bool running = Now.Worlds.TryGetValue(r.Id, out var st) && st.Running;
                if (Dog.Tick(r, running, DateTime.UtcNow, Cfg.AutoRestartMaxPerHour))
                {
                    Log(Dog.LastNote); Audit.Write(AuditDir, r.Name, "watchdog-restart", "stopped on its own; started again");
                    if (!Screens) StartRealmProcess(r);
                }
                else if (Dog.LastNote != null && !running) { Log(Dog.LastNote); Dog.LastNote = null; }
            }
            await Task.CompletedTask;
        }

        // ------------------------------------------------------------------ starting and stopping (the same programs and scripts as the old Control Panel)
        static void RunHidden(string file, string args, string wd) => Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = wd });

        public int RealmPid(Realm r) => Procs.WorldPids(Cfg.Realms, Procs.ListenerPids()).TryGetValue(r.Id, out var p) ? p : 0;

        public void StartAuthProcess() { RunHidden(Path.Combine(Cfg.BinDir, "authserver.exe"), "", Cfg.BinDir); Audit.Write(AuditDir, "-", "start", "login server"); Log("Login server: starting"); }

        public void StartRealmProcess(Realm r)
        {
            Dog.Clear(r.Id);
            if (string.IsNullOrEmpty(r.LaunchScript)) RunHidden(Path.Combine(Cfg.BinDir, "worldserver.exe"), "", Cfg.BinDir);
            else RunHidden("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{Path.Combine(Cfg.BinDir, r.LaunchScript)}\"", Cfg.BinDir);
            Audit.Write(AuditDir, r.Name, "start", "worldserver");
            Log($"{r.Name}: starting");
        }

        public void StartSignupProcess()
        {
            RunHidden(Cfg.PhpExe, $"-S 127.0.0.1:{Cfg.SignupPort} -t public", Cfg.SignupDir);
            string dom = "";
            try { var f = Path.Combine(Cfg.SignupDir, "ngrok_domain.txt"); if (File.Exists(f)) dom = File.ReadLines(f).FirstOrDefault()?.Trim() ?? ""; } catch { }
            if (dom != "" && File.Exists(Cfg.NgrokExe)) RunHidden(Cfg.NgrokExe, $"http --domain={dom} 127.0.0.1:{Cfg.SignupPort}", Cfg.SignupDir);
            Audit.Write(AuditDir, "-", "start", "sign-up website"); Log("Sign-up website: starting");
        }
        public void StopSignupProcess()
        {
            foreach (var n in new[] { "ngrok", "php" }) foreach (var p in Process.GetProcessesByName(n)) { try { p.Kill(); } catch { } }
            Audit.Write(AuditDir, "-", "stop", "sign-up website"); Log("Sign-up website: stopped");
        }
        public void StartBackupLoopProcess() { RunHidden("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{Path.Combine(Cfg.BinDir, "AndersCraft_BackupDaily.ps1")}\"", Cfg.BinDir); Log("Daily backup loop: started"); }

        static void PoliteKill(int pid) => RunHidden("taskkill.exe", "/PID " + pid, Environment.SystemDirectory);
        public static void ForceKill(int pid) { try { Process.GetProcessById(pid).Kill(); } catch { } }
        public static async Task<bool> WaitExit(int pid, int seconds) { for (int i = 0; i < seconds * 2; i++) { if (!Procs.Alive(pid)) return true; await Task.Delay(500); } return !Procs.Alive(pid); }

        public async Task StopAuthProcess()
        {
            var p = Process.GetProcessesByName("authserver").FirstOrDefault();
            if (p == null) { Log("Login server: not running"); return; }
            Audit.Write(AuditDir, "-", "stop", "login server pid " + p.Id);
            PoliteKill(p.Id);
            if (!await WaitExit(p.Id, 15)) { ForceKill(p.Id); await WaitExit(p.Id, 5); }
            Log("Login server: stopped");
        }

        /// <summary>Graceful stop: save everyone, shut down in 10 s over SOAP; otherwise ask the console window to close. Returns the pid if it's still running.</summary>
        public async Task<int> StopRealmGracefully(Realm r, int countdown = 10)
        {
            int pid = RealmPid(r);
            if (pid == 0) { Log($"{r.Name}: not running"); return 0; }
            Dog.Expect(r.Id);
            Audit.Write(AuditDir, r.Name, "stop", "worldserver pid " + pid);
            if (HasGmLogin && Procs.Probe(r.SoapPort))
            {
                var a = await Run(r, "saveall", "stop"); Log($"{r.Name}: save everyone → {a.text}");
                var b = await Run(r, Gm.Shutdown(countdown), "stop"); Log($"{r.Name}: shut down in {countdown}s → {b.text}");
                if (!b.ok) { Log($"{r.Name}: the command line didn't take it; asking the window to close"); PoliteKill(pid); }
                if (await WaitExit(pid, 120 + countdown)) { Log($"{r.Name}: stopped"); return 0; }
            }
            else
            {
                Log($"{r.Name}: no command line; asking the window to close");
                PoliteKill(pid);
                if (await WaitExit(pid, 25)) { Log($"{r.Name}: stopped"); return 0; }
            }
            return pid;
        }

        // ------------------------------------------------------------------ backups (mysqldump with the server's login, as the old Control Panel did)
        public string MysqlDumpExe
        {
            get
            {
                var a = Path.Combine(Cfg.MysqlBin ?? "", "mysqldump.exe");
                if (File.Exists(a)) return a;
                var b = Path.Combine(Cfg.MysqlBin ?? "", "mariadb-dump.exe");
                return File.Exists(b) ? b : S.Profiles.Settings.MysqldumpPath;
            }
        }

        public IEnumerable<string> AllDatabases => new[] { Cfg.AuthDb }.Concat(Cfg.Realms.Select(r => r.CharDb)).Append(Cfg.WorldDb).Where(d => !string.IsNullOrEmpty(d)).Distinct();

        public async Task<(bool ok, string msg)> Backup(IEnumerable<string> dbs, string label)
        {
            if (Screens) return (true, "(screenshot mode)");
            var login = ConfLogin();
            if (login == null) return (false, "worldserver.conf couldn't be read, so there's no login for the backup.");
            var dir = Path.Combine(string.IsNullOrEmpty(Cfg.BackupDir) ? S.Profiles.Settings.BackupRoot : Cfg.BackupDir, DateTime.Now.ToString("yyyy-MM-dd_HHmmss") + "_server_" + label);
            var exe = MysqlDumpExe;
            long total = 0;
            try
            {
                foreach (var db in dbs.Distinct())
                {
                    Log("Backup: " + db + "…");
                    var f = await Task.Run(() => Core.Backup.Run(exe, login.Host, login.Port, login.User, login.Password, db, new List<string>(), dir));
                    total += new FileInfo(f).Length;
                }
                var msg = $"{dir} ({total / 1048576.0:0.0} MB)";
                Audit.Write(AuditDir, "-", "backup", string.Join(",", dbs), msg); Log("Backup written: " + msg);
                return (true, msg);
            }
            catch (Exception ex) { var m = Friendly.Text(ex); Audit.Write(AuditDir, "-", "backup", string.Join(",", dbs), "FAILED: " + m); Log("Backup failed: " + m); return (false, m); }
        }

        /// <summary>Puts a .sql backup back into its database with mysql.exe (after a fresh backup of that database).</summary>
        public async Task<(bool ok, string msg)> Restore(string sqlFile, string db)
        {
            if (Screens) return (true, "(screenshot mode)");
            var login = ConfLogin();
            var mysql = Path.Combine(Cfg.MysqlBin ?? "", "mysql.exe");
            if (login == null || !File.Exists(mysql)) return (false, "mysql.exe or the server's login (worldserver.conf) wasn't found.");
            var pre = await Backup(new[] { db }, "before_restore");
            if (!pre.ok) return (false, "The safety backup before restoring failed, so nothing was restored: " + pre.msg);
            var opt = Path.Combine(Path.GetTempPath(), "acstudio_" + Guid.NewGuid().ToString("N") + ".cnf");
            File.WriteAllText(opt, Core.Backup.OptionFileText(login.User, login.Password), new UTF8Encoding(false));
            try
            {
                var psi = new ProcessStartInfo(mysql) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardInput = true, RedirectStandardOutput = true };
                foreach (var a in new[] { "--defaults-extra-file=" + opt, "--host=" + login.Host, "--port=" + login.Port, "--default-character-set=utf8mb4", db }) psi.ArgumentList.Add(a);
                using var p = Process.Start(psi);
                var err = p.StandardError.ReadToEndAsync();
                using (var src = File.OpenRead(sqlFile)) await src.CopyToAsync(p.StandardInput.BaseStream);
                p.StandardInput.Close();
                await p.WaitForExitAsync();
                var e = (await err).Trim();
                var ok = p.ExitCode == 0;
                Audit.Write(AuditDir, "-", "restore", $"{db} ← {sqlFile}", ok ? "ok" : "FAILED: " + e);
                return ok ? (true, $"Restored {db} from {Path.GetFileName(sqlFile)}. The backup taken just before is {pre.msg}.") : (false, e);
            }
            finally { try { File.Delete(opt); } catch { } }
        }

        // ------------------------------------------------------------------ scheduled tasks (run while Studio is open)
        readonly Dictionary<string, HashSet<int>> warned = new Dictionary<string, HashSet<int>>();

        async Task RunDueTasks()
        {
            var now = DateTime.Now;
            foreach (var t in Cfg.Tasks.Where(t => t.Enabled).ToList())
            {
                if (!Schedule.Due(t, now)) continue;
                var realms = t.RealmId == 0 ? Cfg.Realms.ToList() : Cfg.Realms.Where(r => r.Id == t.RealmId).ToList();
                if (t.Kind == "announce")
                {
                    t.LastRunUtc = DateTime.UtcNow; Save();
                    foreach (var r in realms.Where(r => Blocked(r) == null)) await Run(r, Gm.Announce(t.Message), "scheduled announce");
                    Log("Scheduled message sent: " + t.Message);
                }
                else if (t.Kind == "backup")
                {
                    t.LastRunUtc = DateTime.UtcNow; Save();
                    Log("Scheduled backup…");
                    await Backup(AllDatabases, "scheduled");
                }
                else if (t.Kind == "restart")
                {
                    var at = Schedule.Next(t, now) ?? now;
                    var left = (int)Math.Ceiling((at - now).TotalMinutes);
                    if (!warned.TryGetValue(t.Id, out var done)) warned[t.Id] = done = new HashSet<int>();
                    var mark = Schedule.WarnMarks(t.WarnMinutes).Where(m => m >= left && !done.Contains(m)).OrderBy(m => m).FirstOrDefault();
                    if (left > 0)
                    {
                        if (mark > 0)
                        {
                            done.Add(mark);
                            foreach (var r in realms.Where(r => Blocked(r) == null)) await Run(r, Gm.Announce($"The server restarts in {left} minute{(left == 1 ? "" : "s")}."), "scheduled restart warning");
                        }
                        continue;
                    }
                    t.LastRunUtc = DateTime.UtcNow; Save(); warned.Remove(t.Id);
                    Log("Scheduled restart: " + string.Join(", ", realms.Select(r => r.Name)));
                    foreach (var r in realms)
                    {
                        if (Screens) continue;
                        var still = await StopRealmGracefully(r);
                        if (still != 0) { Log($"{r.Name}: didn't stop; scheduled restart skipped it."); continue; }
                        await Task.Delay(2000); StartRealmProcess(r);
                    }
                }
            }
        }
    }
}
