using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AndersCraftStudio.Core.Live
{
    // ============================================================================ settings
    /// <summary>One game world (realm) the server runs.</summary>
    public sealed class Realm
    {
        public int Id { get; set; } = 1;
        public string Name { get; set; } = "Realm";
        public int WorldPort { get; set; } = 8085;
        public int SoapPort { get; set; } = 7878;
        public string CharDb { get; set; } = "acore_characters";
        /// <summary>Log folder, relative to the server folder.</summary>
        public string LogDir { get; set; } = "logs";
        /// <summary>Empty = start worldserver.exe directly; otherwise a .ps1 in the server folder that starts it with its own settings.</summary>
        public string LaunchScript { get; set; } = "";
        /// <summary>Has playerbots (accounts named RNDBOT…).</summary>
        public bool HasBots { get; set; }
        /// <summary>Start it again by itself if it stops without being asked to (crash watchdog).</summary>
        public bool AutoRestart { get; set; }
        public override string ToString() => Name;
    }

    /// <summary>Something Server control does on a timer while Studio is open.</summary>
    public sealed class ScheduledTask
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 10);
        /// <summary>"announce", "restart" or "backup".</summary>
        public string Kind { get; set; } = "announce";
        /// <summary>0 = every realm.</summary>
        public int RealmId { get; set; }
        public string Message { get; set; } = "";
        /// <summary>Repeat every N minutes (announcements). 0 = use <see cref="DailyAt"/>.</summary>
        public int EveryMinutes { get; set; }
        /// <summary>"HH:mm" once a day (restarts, backups, or announcements).</summary>
        public string DailyAt { get; set; } = "";
        /// <summary>Restarts: minutes of warning players get (announced at 15 / 5 / 1 minute marks within this).</summary>
        public int WarnMinutes { get; set; } = 5;
        public bool Enabled { get; set; } = true;
        public DateTime? LastRunUtc { get; set; }

        public string Describe(IList<Realm> realms)
        {
            var where = RealmId == 0 ? "every realm" : realms.FirstOrDefault(r => r.Id == RealmId)?.Name ?? "realm " + RealmId;
            var when = EveryMinutes > 0 ? $"every {EveryMinutes} min" : string.IsNullOrEmpty(DailyAt) ? "(no time set)" : "daily at " + DailyAt;
            return Kind switch
            {
                "restart" => $"Restart {where} {when}, warning players {WarnMinutes} min ahead",
                "backup" => $"Back up the databases {when}",
                _ => $"Announce to {where} {when}: \"{Message}\"",
            };
        }
    }

    public sealed class ServerSettings
    {
        public string BinDir { get; set; } = "";
        public string MysqlBin { get; set; } = "";
        public string SignupDir { get; set; } = "";
        public string PhpExe { get; set; } = "";
        public string NgrokExe { get; set; } = "";
        public int SignupPort { get; set; } = 8787;
        public int AuthPort { get; set; } = 3724;
        public string BackupDir { get; set; } = "";
        public string LegacyBackupDir { get; set; } = "";
        public string AuthDb { get; set; } = "acore_auth";
        public string WorldDb { get; set; } = "acore_world";
        /// <summary>GM account for SOAP (the server's remote command line). Password encrypted per Windows user.</summary>
        public string SoapUser { get; set; } = "";
        public string SoapPassEnc { get; set; } = "";
        public List<Realm> Realms { get; set; } = new List<Realm>();
        public List<ScheduledTask> Tasks { get; set; } = new List<ScheduledTask>();
        /// <summary>The crash watchdog gives up after this many restarts of one realm within an hour.</summary>
        public int AutoRestartMaxPerHour { get; set; } = 3;
        /// <summary>Folder of the old Control Panel's audit log (still shown on Logs).</summary>
        public string LegacyAuditDir { get; set; } = "";
        public bool Imported { get; set; }

        public string LogPath(Realm r, string file) => Path.Combine(BinDir ?? "", r.LogDir ?? "logs", file);
        public string ConfPath => Path.Combine(BinDir ?? "", "configs", "worldserver.conf");
    }

    public static class ServerSettingsStore
    {
        static readonly JsonSerializerOptions JO = new JsonSerializerOptions { WriteIndented = true };

        public static ServerSettings Load(string path)
        {
            try { if (File.Exists(path)) return JsonSerializer.Deserialize<ServerSettings>(File.ReadAllText(path), JO) ?? new ServerSettings(); } catch { }
            return new ServerSettings();
        }

        public static void Save(string path, ServerSettings s)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(s, JO));
            File.Move(tmp, path, true);
        }

        /// <summary>
        /// Reads the old Control Panel's settings.json (read-only) into Studio's own. <paramref name="reencrypt"/> turns the
        /// old file's encrypted GM password into Studio's (null leaves it out). Returns null when the file isn't there.
        /// </summary>
        public static ServerSettings FromControlPanel(string json, Func<string, string> reencrypt)
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            string S(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            int I(string n, int d) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : d;
            var s = new ServerSettings
            {
                BinDir = S("BinDir") ?? "", MysqlBin = S("MysqlBin") ?? "", SignupDir = S("SignupDir") ?? "", PhpExe = S("PhpExe") ?? "", NgrokExe = S("NgrokExe") ?? "",
                SignupPort = I("SignupPort", 8787), AuthPort = I("AuthPort", 3724), BackupDir = S("BackupDir") ?? "", LegacyBackupDir = S("LegacyBackupDir") ?? "",
                AuthDb = S("AuthDb") ?? "acore_auth", WorldDb = S("WorldDb") ?? "acore_world", SoapUser = S("SoapUser") ?? "", Imported = true,
            };
            var enc = S("SoapPassEnc");
            if (!string.IsNullOrEmpty(enc) && reencrypt != null) s.SoapPassEnc = reencrypt(enc) ?? "";
            if (r.TryGetProperty("Realms", out var rs) && rs.ValueKind == JsonValueKind.Array)
                foreach (var x in rs.EnumerateArray())
                {
                    string XS(string n) => x.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";
                    int XI(string n) => x.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
                    bool XB(string n) => x.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
                    s.Realms.Add(new Realm { Id = XI("Id"), Name = XS("Name"), WorldPort = XI("WorldPort"), SoapPort = XI("SoapPort"), CharDb = XS("CharDb"), LogDir = XS("LogDir"), LaunchScript = XS("LaunchScript"), HasBots = XB("HasBots") });
                }
            return s;
        }

        /// <summary>The old Control Panel's defaults (its realms, ports and folders), used when it never saved settings.</summary>
        public static ServerSettings ControlPanelDefaults(string root) => new ServerSettings
        {
            BinDir = Path.Combine(root, @"azerothcore-wotlk\Build\bin\RelWithDebInfo"), MysqlBin = @"C:\Program Files\MySQL\MySQL Server 8.4\bin",
            SignupDir = Path.Combine(root, "signup"), PhpExe = @"D:\XAMPP\php\php.exe",
            NgrokExe = Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Microsoft\WinGet\Packages\Ngrok.Ngrok_Microsoft.Winget.Source_8wekyb3d8bbwe\ngrok.exe"),
            BackupDir = Path.Combine(root, "_REA_backups"), LegacyBackupDir = Path.Combine(root, "_DB_backups"), LegacyAuditDir = Path.Combine(root, @"ControlPanel\audit"),
            Realms = new List<Realm>
            {
                new Realm { Id = 1, Name = "Main realm", WorldPort = 8085, SoapPort = 7878, CharDb = "acore_characters", LogDir = "logs", HasBots = true },
                new Realm { Id = 2, Name = "Hardcore realm", WorldPort = 8086, SoapPort = 7879, CharDb = "achc_characters", LogDir = "logs_hardcore", LaunchScript = "ServerLauncher_Hardcore.ps1" },
            },
        };
    }

    // ============================================================================ SOAP (the server's remote command line)
    public static class Soap
    {
        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };

        public static string Envelope(string command) =>
            "<?xml version=\"1.0\" encoding=\"utf-8\"?><SOAP-ENV:Envelope xmlns:SOAP-ENV=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:ns1=\"urn:AC\"><SOAP-ENV:Body><ns1:executeCommand><command>"
            + SecurityElement.Escape(command) + "</command></ns1:executeCommand></SOAP-ENV:Body></SOAP-ENV:Envelope>";

        /// <summary>The server's answer in plain words.</summary>
        public static (bool ok, string text) Parse(int status, string body)
        {
            if (status == 401) return (false, "The GM login was refused. Use an account with staff rank Administrator (3) or higher.");
            var m = Regex.Match(body ?? "", "(?s)<result>(.*?)</result>");
            if (m.Success) return (status >= 200 && status < 300, System.Net.WebUtility.HtmlDecode(m.Groups[1].Value).TrimEnd());
            var f = Regex.Match(body ?? "", "(?s)<faultstring>(.*?)</faultstring>");
            if (f.Success) return (false, System.Net.WebUtility.HtmlDecode(f.Groups[1].Value).TrimEnd());
            var t = body ?? "";
            return (status >= 200 && status < 300, t.Length > 500 ? t.Substring(0, 500) : t);
        }

        public static async Task<(bool ok, string text)> Exec(string user, string pass, int port, string command)
        {
            if (string.IsNullOrWhiteSpace(user) || string.IsNullOrEmpty(pass)) return (false, "No GM login saved yet (Server control › Settings).");
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/");
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + pass)));
                req.Content = new StringContent(Envelope(command), Encoding.UTF8, "application/xml");
                using var resp = await Http.SendAsync(req);
                return Parse((int)resp.StatusCode, await resp.Content.ReadAsStringAsync());
            }
            catch (Exception ex) { return (false, $"The realm's command port ({port}) didn't answer. Is the realm running with SOAP on? ({ex.Message})"); }
        }
    }

    // ============================================================================ GM commands, built safely
    /// <summary>Builds the server commands Server control sends. Text from the user is cleaned so it can't add a second command.</summary>
    public static class Gm
    {
        /// <summary>One line, no quotes or control characters (they would end or split the command).</summary>
        public static string Clean(string s) => string.Join(" ", (s ?? "").Replace("\"", "'").Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries));
        /// <summary>A character or account name: letters, digits, and nothing else.</summary>
        public static bool IsName(string s) => !string.IsNullOrEmpty(s) && s.Length <= 32 && s.All(char.IsLetterOrDigit);
        public static int Int(string s, int def) => int.TryParse((s ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def;

        /// <summary>"6948 2589:20" (item IDs, optional :count) → command form, max 12. Null if anything is wrong.</summary>
        public static string ItemList(string s)
        {
            var parts = (s ?? "").Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            var ok = new List<string>();
            foreach (var p in parts)
            {
                var m = Regex.Match(p, @"^(\d+)(?::(\d+))?$");
                if (!m.Success) return null;
                ok.Add(m.Groups[2].Success ? $"{m.Groups[1].Value}:{m.Groups[2].Value}" : m.Groups[1].Value);
            }
            return ok.Count == 0 || ok.Count > 12 ? null : string.Join(" ", ok);
        }

        public static long Copper(int gold, int silver, int copper) => gold * 10000L + silver * 100L + copper;

        /// <summary>"30m", "12h", "7d", "1d12h" or "-1" (forever).</summary>
        public static bool IsDuration(string s) => s == "-1" || Regex.IsMatch(s ?? "", @"^(\d+[smhdw])+$");

        public static string Kick(string ch, string reason) => $"kick {ch} {Clean(reason)}".TrimEnd();
        public static string Mute(string ch, int minutes, string reason) => $"mute {ch} {Math.Max(1, minutes)} {Clean(reason)}".TrimEnd();
        public static string Unmute(string ch) => $"unmute {ch}";
        public static string Revive(string ch) => $"revive {ch}";
        public static string Info(string ch) => $"pinfo {ch}";
        public static string BanCharacter(string ch, string dur, string reason) => $"ban character {ch} {Clean(dur)} {Clean(reason)}";
        public static string BanAccountOf(string ch, string dur, string reason) => $"ban playeraccount {ch} {Clean(dur)} {Clean(reason)}";
        public static string BanAccount(string acc, string dur, string reason) => $"ban account {acc} {Clean(dur)} {Clean(reason)}";
        public static string UnbanCharacter(string ch) => $"unban character {ch}";
        public static string UnbanAccount(string acc) => $"unban account {acc}";
        public static string Teleport(string ch, string where) => $"teleport name {ch} {Clean(where)}";
        public static string Home(string ch) => $"teleport name {ch} $home";
        public static string Level(string ch, int lvl) => $"character level {ch} {Math.Max(1, Math.Min(255, lvl))}";
        public static string Mail(string ch, string subj, string body) => $"send mail {ch} \"{Clean(subj)}\" \"{Clean(body)}\"";
        public static string Money(string ch, string subj, string body, long copper) => $"send money {ch} \"{Clean(subj)}\" \"{Clean(body)}\" {copper}";
        public static string Items(string ch, string subj, string body, string items) => $"send items {ch} \"{Clean(subj)}\" \"{Clean(body)}\" {items}";
        public static string Announce(string text) => "announce " + Clean(text);
        public static string Notify(string text) => "notify " + Clean(text);
        public static string Shutdown(int seconds) => "server shutdown " + Math.Max(0, seconds);
        public static string ShutdownCancel => "server shutdown cancel";
        /// <summary>Closed = only staff can log in (maintenance).</summary>
        public static string Closed(bool closed) => "server set closed " + (closed ? "on" : "off");
        // accounts
        public static string AccountCreate(string acc, string pass) => $"account create {acc} {pass}";
        public static string AccountPassword(string acc, string pass) => $"account set password {acc} {pass} {pass}";
        /// <summary>Staff rank on every realm (-1).</summary>
        public static string AccountRank(string acc, int rank) => $"account set gmlevel {acc} {Math.Max(0, Math.Min(3, rank))} -1";
        /// <summary>0 = classic, 1 = The Burning Crusade, 2 = Wrath of the Lich King.</summary>
        public static string AccountExpansion(string acc, int exp) => $"account set addon {acc} {Math.Max(0, Math.Min(2, exp))}";

        /// <summary>The command as it goes in the audit log: passwords replaced by ***.</summary>
        public static string ForLog(string cmd)
        {
            var m = Regex.Match(cmd ?? "", @"^(account create \S+) (.+)$", RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value + " ***";
            m = Regex.Match(cmd ?? "", @"^(account set password \S+) .+$", RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value + " *** ***";
            return cmd;
        }

        public static readonly string[] Ranks = { "Player", "Moderator", "Game master", "Administrator" };
        public static readonly string[] Expansions = { "Classic only", "Up to The Burning Crusade", "Up to Wrath of the Lich King" };
    }

    /// <summary>The quick-command palette: common server commands with plain names (the raw command shows only when asked).</summary>
    public static class CommandPalette
    {
        public sealed class Entry { public string Name, What, Command; public override string ToString() => Name; }
        public static readonly Entry[] All =
        {
            new Entry { Name = "Server status", What = "Uptime, players online and the build.", Command = "server info" },
            new Entry { Name = "Who's online", What = "Every online account and character.", Command = "account onlinelist" },
            new Entry { Name = "Save everyone now", What = "Writes every online character to the database.", Command = "saveall" },
            new Entry { Name = "Message everyone (chat)", What = "A system message in everyone's chat. Replace the text.", Command = "announce Your message here" },
            new Entry { Name = "Message everyone (screen)", What = "A message in the middle of everyone's screen.", Command = "notify Your message here" },
            new Entry { Name = "Close to players (maintenance)", What = "Only staff can log in until you open it again.", Command = "server set closed on" },
            new Entry { Name = "Open to players", What = "Ends maintenance mode.", Command = "server set closed off" },
            new Entry { Name = "Shut down in 5 minutes", What = "Players see a countdown. Start it again from the dashboard.", Command = "server shutdown 300" },
            new Entry { Name = "Cancel a shutdown", What = "Stops a countdown that's running.", Command = "server shutdown cancel" },
            new Entry { Name = "Reload server settings", What = "Re-reads worldserver.conf without a restart (most settings).", Command = "reload config" },
            new Entry { Name = "Reload all reloadable tables", What = "Re-reads quest, loot, creature text and similar tables.", Command = "reload all" },
            new Entry { Name = "Message of the day", What = "Shows the current message players get at login.", Command = "server motd" },
            new Entry { Name = "Set the message of the day", What = "Replace the text.", Command = "server set motd Welcome to the server!" },
            new Entry { Name = "Banned accounts", What = "Lists active account bans.", Command = "banlist account" },
            new Entry { Name = "Banned characters", What = "Lists active character bans.", Command = "banlist character" },
        };
    }

    // ============================================================================ audit log
    public static class Audit
    {
        static readonly object L = new object();
        public static string Line(DateTime when, string user, string realm, string action, string detail, string result)
        {
            string r = result == null ? "" : " | " + result.Replace("\r", " ").Replace("\n", " ");
            if (r.Length > 400) r = r.Substring(0, 400) + "...";
            return $"{when:yyyy-MM-dd HH:mm:ss} | {user} | {realm} | {action} | {detail}{r}";
        }
        public static void Write(string dir, string realm, string action, string detail, string result = null)
        {
            if (string.IsNullOrEmpty(dir)) return;
            try
            {
                lock (L)
                {
                    Directory.CreateDirectory(dir);
                    File.AppendAllText(Path.Combine(dir, $"audit_{DateTime.Now:yyyy-MM}.log"), Line(DateTime.Now, Environment.UserName, realm, action, Gm.ForLog(detail), result) + Environment.NewLine);
                }
            }
            catch { }
        }
    }

    // ============================================================================ logs
    public static class Logs
    {
        /// <summary>"error", "warn" or "info" for a server log line.</summary>
        public static string Level(string line)
        {
            if (string.IsNullOrEmpty(line)) return "info";
            if (Regex.IsMatch(line, @"\b(ERROR|FATAL|Error|Exception|crash|Crash|ASSERT|failed|FAILED)\b")) return "error";
            if (Regex.IsMatch(line, @"\b(WARN|Warning|WARNING|warn)\b")) return "warn";
            return "info";
        }

        /// <summary>The last <paramref name="n"/> lines of a file the server may still be writing.</summary>
        public static List<string> Tail(string path, int n)
        {
            var res = new List<string>();
            try
            {
                if (!File.Exists(path)) return res;
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                long want = Math.Min(fs.Length, Math.Max(4096, n * 400L));
                fs.Seek(-want, SeekOrigin.End);
                var buf = new byte[want]; int read = 0;
                while (read < want) { int r = fs.Read(buf, read, (int)want - read); if (r <= 0) break; read += r; }
                var all = Encoding.UTF8.GetString(buf, 0, read).Replace("\r", "").Split('\n').Where(l => l.Length > 0).ToList();
                if (want < fs.Length && all.Count > 0) all.RemoveAt(0);
                return all.Skip(Math.Max(0, all.Count - n)).ToList();
            }
            catch { return res; }
        }
    }

    // ============================================================================ schedule + watchdog (pure logic, tested)
    public static class Schedule
    {
        static bool TryTime(string hhmm, out TimeSpan t) => TimeSpan.TryParseExact(hhmm ?? "", new[] { @"hh\:mm", @"h\:mm" }, CultureInfo.InvariantCulture, out t);

        /// <summary>When the task next runs (local time), or null when it has no valid time.</summary>
        public static DateTime? Next(ScheduledTask t, DateTime nowLocal)
        {
            if (!t.Enabled) return null;
            if (t.EveryMinutes > 0)
            {
                var last = t.LastRunUtc?.ToLocalTime();
                return last == null ? nowLocal : last.Value.AddMinutes(t.EveryMinutes);
            }
            if (!TryTime(t.DailyAt, out var at)) return null;
            var today = nowLocal.Date + at;
            var lastLocal = t.LastRunUtc?.ToLocalTime();
            if (lastLocal != null && lastLocal.Value >= today) return today.AddDays(1);
            return today < nowLocal.AddMinutes(-30) ? today.AddDays(1) : today;   // missed by more than half an hour: tomorrow
        }

        /// <summary>Due now (within the tick). Restarts start their warnings <see cref="ScheduledTask.WarnMinutes"/> early.</summary>
        public static bool Due(ScheduledTask t, DateTime nowLocal)
        {
            var n = Next(t, nowLocal);
            if (n == null) return false;
            var start = t.Kind == "restart" ? n.Value.AddMinutes(-Math.Max(0, t.WarnMinutes)) : n.Value;
            return nowLocal >= start;
        }

        /// <summary>The minute marks a restart warns at (15, 10, 5, 1 that fit inside the warning time).</summary>
        public static int[] WarnMarks(int warnMinutes) => new[] { 30, 15, 10, 5, 1 }.Where(m => m <= Math.Max(1, warnMinutes)).ToArray();
    }

    /// <summary>The crash watchdog: decides when a realm that stopped on its own should be started again.</summary>
    public sealed class Watchdog
    {
        readonly Dictionary<int, bool> wasUp = new Dictionary<int, bool>();
        readonly Dictionary<int, List<DateTime>> restarts = new Dictionary<int, List<DateTime>>();
        readonly HashSet<int> expectedDown = new HashSet<int>();
        public string LastNote;

        /// <summary>Tell the watchdog a stop is on purpose (Stop, Restart, a scheduled restart), so it doesn't restart it.</summary>
        public void Expect(int realmId) => expectedDown.Add(realmId);
        public void Clear(int realmId) => expectedDown.Remove(realmId);

        /// <summary>Called each status tick. True = start this realm again now.</summary>
        public bool Tick(Realm r, bool running, DateTime nowUtc, int maxPerHour)
        {
            wasUp.TryGetValue(r.Id, out var before);
            wasUp[r.Id] = running;
            if (running) { expectedDown.Remove(r.Id); return false; }
            if (!before) return false;                         // it wasn't up a moment ago: nothing changed
            if (expectedDown.Contains(r.Id)) { LastNote = $"{r.Name} stopped (asked to)."; return false; }
            if (!r.AutoRestart) { LastNote = $"{r.Name} stopped on its own (a crash?). Automatic restart is off for it."; return false; }
            if (!restarts.TryGetValue(r.Id, out var list)) restarts[r.Id] = list = new List<DateTime>();
            list.RemoveAll(t => t < nowUtc.AddHours(-1));
            if (list.Count >= Math.Max(1, maxPerHour)) { LastNote = $"{r.Name} stopped on its own again; it has been restarted {list.Count} time(s) this hour, so the watchdog stops trying. Check its Errors log."; return false; }
            list.Add(nowUtc);
            LastNote = $"{r.Name} stopped on its own (a crash?). Starting it again (restart {list.Count} this hour).";
            return true;
        }
    }

    // ============================================================================ data from the database (read-only)
    public sealed class CharRow
    {
        public uint Guid { get; set; }
        public string Name { get; set; }
        public Realm Realm { get; set; }
        public string RealmName => Realm?.Name;
        public uint AccountId { get; set; }
        public string Account { get; set; }
        public int Level { get; set; }
        public int RaceId { get; set; }
        public int ClassId { get; set; }
        public string Race { get; set; }
        public string Class { get; set; }
        public bool Online { get; set; }
        public string OnlineText => Online ? "● online" : "offline";
        public int ZoneId { get; set; }
        public string Zone { get; set; }
        public long Money { get; set; }
        public double Gold => Math.Round(Money / 10000.0, 2);
        public long TotalTime { get; set; }
        public string Played => Span(TotalTime);
        public long LogoutUnix { get; set; }
        public string LastLogoutText => LogoutUnix > 0 ? DateTimeOffset.FromUnixTimeSeconds(LogoutUnix).LocalDateTime.ToString("yyyy-MM-dd HH:mm") : "never";
        public bool IsBot { get; set; }
        public string Kind => IsBot ? "bot" : "player";
        public static string Span(long secs)
        {
            var t = TimeSpan.FromSeconds(Math.Max(0, secs));
            return t.TotalDays >= 1 ? $"{(int)t.TotalDays}d {t.Hours}h" : $"{t.Hours}h {t.Minutes}m";
        }
    }

    public sealed class AccountRow
    {
        public uint Id { get; set; }
        public string Name { get; set; }
        public int Rank { get; set; }
        public string RankText => Rank >= 0 && Rank < Gm.Ranks.Length ? Gm.Ranks[Rank] : "Rank " + Rank;
        public int Expansion { get; set; }
        public string ExpansionText => Expansion >= 0 && Expansion < Gm.Expansions.Length ? Gm.Expansions[Expansion] : "?";
        public bool Online { get; set; }
        public string OnlineText => Online ? "● online" : "";
        public bool Banned { get; set; }
        public bool Muted { get; set; }
        public string Status => Banned ? "banned" : Muted ? "muted" : "";
        public string LastLogin { get; set; }
        public int Characters { get; set; }
        public bool IsBot => Name?.StartsWith("RNDBOT", StringComparison.OrdinalIgnoreCase) == true;
    }

    public static class LiveData
    {
        static bool Ident(string s) => !string.IsNullOrEmpty(s) && Regex.IsMatch(s, "^[A-Za-z0-9_]+$");

        public static string CharactersSql(Realm r, string authDb) => !Ident(r.CharDb) || !Ident(authDb) ? null :
            $"SELECT ch.guid, ch.name, ch.account, IFNULL(a.username,'?'), ch.level, ch.race, ch.class, ch.online, ch.zone, ch.money, ch.totaltime, ch.logout_time " +
            $"FROM `{r.CharDb}`.characters ch LEFT JOIN `{authDb}`.account a ON a.id = ch.account WHERE ch.deleteInfos_Account IS NULL";

        public static string OnlineSql(Realm r, string authDb) => !Ident(r.CharDb) || !Ident(authDb) ? null :
            $"SELECT SUM(IFNULL(a.username,'') NOT LIKE 'RNDBOT%'), SUM(IFNULL(a.username,'') LIKE 'RNDBOT%') FROM `{r.CharDb}`.characters ch LEFT JOIN `{authDb}`.account a ON a.id=ch.account WHERE ch.online=1";

        public static string AccountsSql(string authDb, IEnumerable<Realm> realms)
        {
            if (!Ident(authDb)) return null;
            var counts = string.Join(" + ", realms.Where(r => Ident(r.CharDb)).Select(r => $"(SELECT COUNT(*) FROM `{r.CharDb}`.characters c WHERE c.account = a.id)"));
            if (counts.Length == 0) counts = "0";
            return $"SELECT a.id, a.username, IFNULL((SELECT MAX(x.gmlevel) FROM `{authDb}`.account_access x WHERE x.id = a.id), 0), a.expansion, a.online, " +
                   $"(SELECT COUNT(*) FROM `{authDb}`.account_banned b WHERE b.id = a.id AND b.active = 1), a.mutetime, a.last_login, {counts} " +
                   $"FROM `{authDb}`.account a ORDER BY a.id";
        }

        static long L(object o) => o == null || o is DBNull ? 0 : Convert.ToInt64(o, CultureInfo.InvariantCulture);

        public static List<CharRow> Characters(ISqlReader db, IList<Realm> realms, string authDb, Func<int, string> race, Func<int, string> cls, Func<int, string> zone, List<string> errors)
        {
            var list = new List<CharRow>();
            foreach (var r in realms)
            {
                var sql = CharactersSql(r, authDb);
                if (sql == null) continue;
                try
                {
                    foreach (var x in db.Query(sql, 200000).Rows)
                    {
                        var acc = Convert.ToString(x[3]);
                        var row = new CharRow
                        {
                            Guid = (uint)L(x[0]), Name = Convert.ToString(x[1]), AccountId = (uint)L(x[2]), Account = acc, Level = (int)L(x[4]), RaceId = (int)L(x[5]), ClassId = (int)L(x[6]),
                            Online = L(x[7]) != 0, ZoneId = (int)L(x[8]), Money = L(x[9]), TotalTime = L(x[10]), LogoutUnix = L(x[11]), Realm = r,
                            IsBot = r.HasBots && acc.StartsWith("RNDBOT", StringComparison.OrdinalIgnoreCase),
                        };
                        row.Race = race(row.RaceId); row.Class = cls(row.ClassId); row.Zone = zone(row.ZoneId);
                        list.Add(row);
                    }
                }
                catch (Exception ex) { errors?.Add($"{r.Name} ({r.CharDb}): {Friendly.Text(ex)}"); }
            }
            return list;
        }

        public static Dictionary<int, (int real, int bots)> Online(ISqlReader db, IList<Realm> realms, string authDb)
        {
            var d = new Dictionary<int, (int, int)>();
            foreach (var r in realms)
            {
                var sql = OnlineSql(r, authDb);
                if (sql == null) continue;
                try { var t = db.Query(sql, 1); if (t.Rows.Count > 0) d[r.Id] = ((int)L(t.Rows[0][0]), (int)L(t.Rows[0][1])); } catch { }
            }
            return d;
        }

        public static List<AccountRow> Accounts(ISqlReader db, IList<Realm> realms, string authDb)
        {
            var sql = AccountsSql(authDb, realms);
            var res = new List<AccountRow>();
            if (sql == null) return res;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach (var x in db.Query(sql, 200000).Rows)
                res.Add(new AccountRow
                {
                    Id = (uint)L(x[0]), Name = Convert.ToString(x[1]), Rank = (int)L(x[2]), Expansion = (int)L(x[3]), Online = L(x[4]) != 0, Banned = L(x[5]) > 0,
                    Muted = L(x[6]) > now, LastLogin = x[7] is DateTime dt ? dt.ToString("yyyy-MM-dd HH:mm") : Convert.ToString(x[7]), Characters = (int)L(x[8]),
                });
            return res;
        }

        public static List<string> BanStatus(ISqlReader db, CharRow ch, string authDb)
        {
            var res = new List<string>();
            if (!Ident(ch.Realm?.CharDb) || !Ident(authDb)) return res;
            string Until(object un, object bd) { long u = L(un), b = L(bd); return u <= b ? " (permanent)" : " (until " + DateTimeOffset.FromUnixTimeSeconds(u).LocalDateTime.ToString("yyyy-MM-dd HH:mm") + ")"; }
            try
            {
                foreach (var r in db.Query($"SELECT unbandate, bandate, bannedby, banreason FROM `{ch.Realm.CharDb}`.character_banned WHERE guid={ch.Guid} AND active=1", 20).Rows)
                    res.Add($"Character banned by {r[2]}: {r[3]}{Until(r[0], r[1])}");
                foreach (var r in db.Query($"SELECT unbandate, bandate, bannedby, banreason FROM `{authDb}`.account_banned WHERE id={ch.AccountId} AND active=1", 20).Rows)
                    res.Add($"Account banned by {r[2]}: {r[3]}{Until(r[0], r[1])}");
                var mt = db.Query($"SELECT mutetime FROM `{authDb}`.account WHERE id={ch.AccountId}", 1);
                if (mt.Rows.Count > 0 && L(mt.Rows[0][0]) > DateTimeOffset.UtcNow.ToUnixTimeSeconds()) res.Add("Muted until " + DateTimeOffset.FromUnixTimeSeconds(L(mt.Rows[0][0])).LocalDateTime.ToString("yyyy-MM-dd HH:mm"));
            }
            catch (Exception ex) { res.Add("(ban status unavailable: " + Friendly.Text(ex) + ")"); }
            return res;
        }
    }

    // ============================================================================ processes (read-only probes; starting/stopping is done by the app)
    public sealed class CompStatus
    {
        public string Key, Name;
        public bool Running, Ready;
        public int Pid;
        public DateTime? Started;
        public double CpuPercent; public long MemoryBytes;
        public string Detail = "";
        public string Uptime => Started == null ? "" : CharRow.Span((long)(DateTime.Now - Started.Value).TotalSeconds);
        public string Memory => MemoryBytes <= 0 ? "" : $"{MemoryBytes / 1048576.0:0} MB";
    }

    public static class Procs
    {
        public static bool Probe(int port, int ms = 300)
        {
            try { using var c = new TcpClient(); var t = c.ConnectAsync("127.0.0.1", port); return t.Wait(ms) && c.Connected; }
            catch { return false; }
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int dwOutBufLen, bool sort, int ipVersion, int tblClass, uint reserved);

        /// <summary>Listening TCP port → process ID.</summary>
        public static Dictionary<int, int> ListenerPids()
        {
            var map = new Dictionary<int, int>();
            foreach (int af in new[] { 2, 23 })
            {
                int len = 0;
                GetExtendedTcpTable(IntPtr.Zero, ref len, false, af, 3, 0);
                if (len <= 0) continue;
                IntPtr buf = Marshal.AllocHGlobal(len);
                try
                {
                    if (GetExtendedTcpTable(buf, ref len, false, af, 3, 0) != 0) continue;
                    int n = Marshal.ReadInt32(buf);
                    int rowSize = af == 2 ? 24 : 56;
                    for (int i = 0; i < n; i++)
                    {
                        IntPtr row = buf + 4 + i * rowSize;
                        int portRaw, pid;
                        if (af == 2) { portRaw = Marshal.ReadInt32(row, 8); pid = Marshal.ReadInt32(row, 20); }
                        else { portRaw = Marshal.ReadInt32(row, 20); pid = Marshal.ReadInt32(row, 52); }
                        map.TryAdd(((portRaw & 0xFF) << 8) | ((portRaw >> 8) & 0xFF), pid);
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            return map;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PBI { public IntPtr R1; public IntPtr Peb; public IntPtr R2a; public IntPtr R2b; public IntPtr UniqueId; public IntPtr Parent; }
        [DllImport("ntdll.dll")] static extern int NtQueryInformationProcess(IntPtr h, int cls, ref PBI pbi, int len, out int ret);
        [DllImport("ntdll.dll")] static extern int NtQueryInformationProcess(IntPtr h, int cls, IntPtr buf, int len, out int ret);

        public static int ParentPid(Process p)
        {
            try { var pbi = new PBI(); if (NtQueryInformationProcess(p.Handle, 0, ref pbi, Marshal.SizeOf(pbi), out _) == 0) return pbi.Parent.ToInt32(); } catch { }
            return -1;
        }
        static string ProcName(int pid) { try { return Process.GetProcessById(pid).ProcessName; } catch { return ""; } }

        /// <summary>Realm → worldserver process: the owner of its world or command port, else by how it was started (a script = the scripted realm).</summary>
        public static Dictionary<int, int> WorldPids(IList<Realm> realms, Dictionary<int, int> listeners)
        {
            var res = new Dictionary<int, int>();
            var ws = Process.GetProcessesByName("worldserver");
            var known = new HashSet<int>();
            foreach (var r in realms)
            {
                if (listeners.TryGetValue(r.WorldPort, out int pid) && ws.Any(p => p.Id == pid)) { res[r.Id] = pid; known.Add(pid); }
                else if (listeners.TryGetValue(r.SoapPort, out pid) && ws.Any(p => p.Id == pid)) { res[r.Id] = pid; known.Add(pid); }
            }
            foreach (var p in ws.Where(p => !known.Contains(p.Id)))
            {
                bool scripted = ProcName(ParentPid(p)).StartsWith("powershell", StringComparison.OrdinalIgnoreCase);
                var realm = realms.FirstOrDefault(r => !res.ContainsKey(r.Id) && string.IsNullOrEmpty(r.LaunchScript) != scripted) ?? realms.FirstOrDefault(r => !res.ContainsKey(r.Id));
                if (realm != null) { res[realm.Id] = p.Id; known.Add(p.Id); }
            }
            return res;
        }

        public static DateTime? StartTime(int pid) { try { return Process.GetProcessById(pid).StartTime; } catch { return null; } }
        public static bool Alive(int pid) { try { return !Process.GetProcessById(pid).HasExited; } catch { return false; } }

        public static string CommandLine(Process p)
        {
            try
            {
                int len = 4096;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    IntPtr buf = Marshal.AllocHGlobal(len);
                    try
                    {
                        int st = NtQueryInformationProcess(p.Handle, 60, buf, len, out int ret);
                        if (st == 0) { int l = Marshal.ReadInt16(buf) & 0xFFFF; IntPtr s = Marshal.ReadIntPtr(buf, IntPtr.Size); return Marshal.PtrToStringUni(s, l / 2); }
                        if (ret > len) { len = ret; continue; }
                        return "";
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
            }
            catch { }
            return "";
        }

        /// <summary>The hidden PowerShell loop that runs the daily backup script, or 0.</summary>
        public static int FindBackupLoop()
        {
            foreach (var p in Process.GetProcessesByName("powershell"))
                if (CommandLine(p).IndexOf("AndersCraft_BackupDaily.ps1", StringComparison.OrdinalIgnoreCase) >= 0) return p.Id;
            return 0;
        }
    }

    /// <summary>CPU use between two samples, per process.</summary>
    public sealed class CpuSampler
    {
        readonly Dictionary<int, (TimeSpan cpu, DateTime at)> last = new Dictionary<int, (TimeSpan, DateTime)>();
        public (double cpu, long mem) Sample(int pid)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                var cpu = p.TotalProcessorTime; var now = DateTime.UtcNow; long mem = p.WorkingSet64;
                double pct = 0;
                if (last.TryGetValue(pid, out var prev) && now > prev.at)
                    pct = (cpu - prev.cpu).TotalMilliseconds / (now - prev.at).TotalMilliseconds / Environment.ProcessorCount * 100.0;
                last[pid] = (cpu, now);
                return (Math.Max(0, Math.Min(100, pct)), mem);
            }
            catch { return (0, 0); }
        }
    }

    /// <summary>Backup folders on disk (Server control's and Studio's), newest first.</summary>
    public static class BackupList
    {
        public sealed class Entry { public string Path, Name; public DateTime When; public long Bytes; public List<string> Files = new List<string>(); public string Size => Bytes >= 1048576 ? $"{Bytes / 1048576.0:0.0} MB" : $"{Bytes / 1024.0:0} KB"; public string FilesText => string.Join(", ", Files.Take(6)) + (Files.Count > 6 ? $" +{Files.Count - 6}" : ""); public string WhenText => When.ToString("yyyy-MM-dd HH:mm"); }
        public static List<Entry> Scan(IEnumerable<string> roots)
        {
            var res = new List<Entry>();
            foreach (var root in roots.Where(r => !string.IsNullOrEmpty(r) && Directory.Exists(r)).Distinct(StringComparer.OrdinalIgnoreCase))
                foreach (var d in Directory.GetDirectories(root))
                {
                    try
                    {
                        var files = Directory.GetFiles(d, "*", SearchOption.AllDirectories);
                        if (files.Length == 0) continue;
                        res.Add(new Entry { Path = d, Name = System.IO.Path.GetFileName(d), When = Directory.GetCreationTime(d), Bytes = files.Sum(f => new FileInfo(f).Length), Files = files.Select(System.IO.Path.GetFileName).ToList() });
                    }
                    catch { }
                }
            return res.OrderByDescending(e => e.When).ToList();
        }
    }
}
