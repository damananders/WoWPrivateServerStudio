using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AndersCraftStudio.Core.Live;

namespace AndersCraftStudio.Tests
{
    /// <summary>Server control (the merged Control Panel): pure logic only. Nothing here talks to a server.</summary>
    static class ServerControlTests
    {
        [Test] static void Soap_envelope_escapes_and_answers_read_in_plain_words()
        {
            var env = Soap.Envelope("announce <b>Hi</b> & bye");
            A.Contains(env, "announce &lt;b&gt;Hi&lt;/b&gt; &amp; bye");
            A.Contains(env, "urn:AC");
            var ok = Soap.Parse(200, "<SOAP-ENV:Envelope><result>Online players: 3&#xD;\nUptime: 1h</result></SOAP-ENV:Envelope>");
            A.True(ok.ok, "ok"); A.Contains(ok.text, "Online players: 3");
            var fault = Soap.Parse(500, "<faultstring>There is no such command</faultstring>");
            A.False(fault.ok, "fault"); A.Eq("There is no such command", fault.text);
            var denied = Soap.Parse(401, "");
            A.False(denied.ok, "401"); A.Contains(denied.text, "Administrator");
        }

        [Test] static void Gm_commands_are_cleaned_and_validated()
        {
            A.Eq("kick Aldren spamming chat", Gm.Kick("Aldren", "  spamming\r\n chat "));
            A.Eq("send mail Aldren \"Hi 'there'\" \"line1 line2\"", Gm.Mail("Aldren", "Hi \"there\"", "line1\nline2"));
            A.Eq("6948 2589:20", Gm.ItemList("6948, 2589:20"));
            A.Eq(null, Gm.ItemList("6948 abc"), "bad item");
            A.Eq(null, Gm.ItemList(string.Join(" ", Enumerable.Range(1, 13))), "more than 12");
            A.Eq(120304L, Gm.Copper(12, 3, 4));
            A.True(Gm.IsDuration("1d12h") && Gm.IsDuration("-1") && Gm.IsDuration("30m"), "durations");
            A.False(Gm.IsDuration("1 day") || Gm.IsDuration("") || Gm.IsDuration("7"), "bad durations");
            A.True(Gm.IsName("Aldren2") && !Gm.IsName("Al dren") && !Gm.IsName("x;y"), "names");
            A.Eq("account set gmlevel BOB 3 -1", Gm.AccountRank("BOB", 9), "rank clamped");
            A.Eq("account set addon BOB 2", Gm.AccountExpansion("BOB", 2));
            A.Eq("server set closed on", Gm.Closed(true));
            A.Eq("server shutdown 300", Gm.Shutdown(300));
            // passwords never reach the audit log
            A.Eq("account create BOB ***", Gm.ForLog(Gm.AccountCreate("BOB", "Secret1")));
            A.Eq("account set password BOB *** ***", Gm.ForLog(Gm.AccountPassword("BOB", "Secret1")));
            A.Eq("kick BOB", Gm.ForLog("kick BOB"));
            A.True(CommandPalette.All.All(e => !string.IsNullOrEmpty(e.Name) && !string.IsNullOrEmpty(e.What) && !e.Command.StartsWith(".")), "palette entries have plain names");
        }

        [Test] static void Schedule_works_out_the_next_run()
        {
            var now = new DateTime(2026, 11, 25, 10, 0, 0, DateTimeKind.Local);
            var every = new ScheduledTask { Kind = "announce", Message = "x", EveryMinutes = 30 };
            A.Eq(now, Schedule.Next(every, now), "never run: now");
            every.LastRunUtc = now.AddMinutes(-10).ToUniversalTime();
            A.Eq(now.AddMinutes(20), Schedule.Next(every, now));
            A.False(Schedule.Due(every, now), "not yet");
            var daily = new ScheduledTask { Kind = "backup", DailyAt = "04:30" };
            A.Eq(new DateTime(2026, 11, 26, 4, 30, 0), Schedule.Next(daily, now), "missed long ago: tomorrow");
            daily.DailyAt = "09:45";
            A.Eq(new DateTime(2026, 11, 25, 9, 45, 0), Schedule.Next(daily, now), "missed by 15 min: still today");
            A.True(Schedule.Due(daily, now), "so it's due");
            daily.LastRunUtc = new DateTime(2026, 11, 25, 9, 46, 0).ToUniversalTime();
            A.Eq(new DateTime(2026, 11, 26, 9, 45, 0), Schedule.Next(daily, now), "ran today: tomorrow");
            var restart = new ScheduledTask { Kind = "restart", DailyAt = "10:10", WarnMinutes = 15 };
            A.True(Schedule.Due(restart, now), "inside the warning window");
            restart.WarnMinutes = 5;
            A.False(Schedule.Due(restart, now), "before the warning window");
            A.Eq("15,10,5,1", string.Join(",", Schedule.WarnMarks(15)));
            A.Eq(null, Schedule.Next(new ScheduledTask { DailyAt = "25:99" }, now), "bad time");
            A.Eq(null, Schedule.Next(new ScheduledTask { DailyAt = "05:00", Enabled = false }, now), "off");
            A.Contains(restart.Describe(new List<Realm> { new Realm { Id = 1, Name = "Main" } }), "Restart every realm daily at 10:10");
        }

        [Test] static void Watchdog_restarts_only_unexpected_stops_and_gives_up()
        {
            var dog = new Watchdog();
            var r = new Realm { Id = 1, Name = "Main", AutoRestart = true };
            var t = DateTime.UtcNow;
            A.False(dog.Tick(r, false, t, 3), "was never up");
            A.False(dog.Tick(r, true, t, 3), "up");
            A.True(dog.Tick(r, false, t, 3), "crashed: restart");
            A.Contains(dog.LastNote, "crash");
            // asked to stop: no restart
            dog.Tick(r, true, t, 3); dog.Expect(1);
            A.False(dog.Tick(r, false, t, 3), "expected stop");
            // too many crashes in an hour
            dog.Tick(r, true, t, 3); A.True(dog.Tick(r, false, t.AddMinutes(1), 3), "2nd");
            dog.Tick(r, true, t, 3); A.True(dog.Tick(r, false, t.AddMinutes(2), 3), "3rd");
            dog.Tick(r, true, t, 3); A.False(dog.Tick(r, false, t.AddMinutes(3), 3), "gives up");
            A.Contains(dog.LastNote, "stops trying");
            dog.Tick(r, true, t, 3); A.True(dog.Tick(r, false, t.AddHours(2), 3), "an hour later it tries again");
            var off = new Realm { Id = 2, Name = "HC", AutoRestart = false };
            dog.Tick(off, true, t, 3);
            A.False(dog.Tick(off, false, t, 3), "auto restart off");
        }

        [Test] static void Control_panel_settings_import_reads_realms_and_reencrypts()
        {
            var json = @"{ ""BinDir"": ""D:\\srv\\bin"", ""MysqlBin"": ""C:\\mysql\\bin"", ""AuthPort"": 3724, ""SoapUser"": ""GMBOSS"", ""SoapPassEnc"": ""OLDENC"",
              ""Realms"": [ { ""Id"": 1, ""Name"": ""Main"", ""WorldPort"": 8085, ""SoapPort"": 7878, ""CharDb"": ""acore_characters"", ""LogDir"": ""logs"", ""LaunchScript"": """", ""HasBots"": true },
                            { ""Id"": 2, ""Name"": ""Hardcore"", ""WorldPort"": 8086, ""SoapPort"": 7879, ""CharDb"": ""achc_characters"", ""LogDir"": ""logs_hardcore"", ""LaunchScript"": ""ServerLauncher_Hardcore.ps1"", ""HasBots"": false } ] }";
            var s = ServerSettingsStore.FromControlPanel(json, enc => enc == "OLDENC" ? "NEWENC" : null);
            A.Eq(@"D:\srv\bin", s.BinDir); A.Eq("GMBOSS", s.SoapUser); A.Eq("NEWENC", s.SoapPassEnc, "re-encrypted");
            A.Eq(2, s.Realms.Count); A.True(s.Realms[0].HasBots && !s.Realms[1].HasBots, "bots");
            A.Eq("ServerLauncher_Hardcore.ps1", s.Realms[1].LaunchScript); A.Eq(7879, s.Realms[1].SoapPort);
            var none = ServerSettingsStore.FromControlPanel(json, null);
            A.Eq("", none.SoapPassEnc, "no password without a re-encrypter");
            var d = ServerSettingsStore.ControlPanelDefaults(@"D:\Root");
            A.Eq(@"D:\Root\azerothcore-wotlk\Build\bin\RelWithDebInfo", d.BinDir);
            A.True(d.Realms.Count == 2 && d.Realms[1].CharDb == "achc_characters", "defaults");
            // round trip through Studio's own file
            var f = Path.Combine(Path.GetTempPath(), "acs_sc_" + Guid.NewGuid().ToString("N") + ".json");
            try { s.Tasks.Add(new ScheduledTask { Kind = "backup", DailyAt = "04:30" }); ServerSettingsStore.Save(f, s); var back = ServerSettingsStore.Load(f); A.Eq(2, back.Realms.Count); A.Eq("04:30", back.Tasks[0].DailyAt); }
            finally { File.Delete(f); }
        }

        [Test] static void Queries_only_use_safe_database_names()
        {
            var r = new Realm { CharDb = "achc_characters" };
            A.Contains(LiveData.CharactersSql(r, "acore_auth"), "`achc_characters`.characters");
            A.Eq(null, LiveData.CharactersSql(new Realm { CharDb = "x`; DROP" }, "acore_auth"), "bad name refused");
            A.Eq(null, LiveData.OnlineSql(r, "auth db"), "bad auth name refused");
            var acc = LiveData.AccountsSql("acore_auth", new[] { new Realm { CharDb = "acore_characters" }, r });
            A.Contains(acc, "`acore_characters`.characters"); A.Contains(acc, "`achc_characters`.characters"); A.Contains(acc, "account_access");
            A.True(!acc.Contains("INSERT") && !acc.Contains("UPDATE") && !acc.Contains("DELETE"), "read only");
        }

        [Test] static void Logs_audit_and_backups()
        {
            A.Eq("error", Logs.Level("2026 ERROR Spell 1 broken")); A.Eq("warn", Logs.Level("WARN Table x")); A.Eq("info", Logs.Level("World initialized"));
            var line = Audit.Line(new DateTime(2026, 11, 25, 12, 0, 0), "me", "Main", "kick", "kick BOB", "ok: done");
            A.Eq("2026-11-25 12:00:00 | me | Main | kick | kick BOB | ok: done", line);
            var dir = Path.Combine(Path.GetTempPath(), "acs_bk_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(dir, "a")); File.WriteAllText(Path.Combine(dir, "a", "acore_auth_.sql"), new string('x', 2048));
                Directory.CreateDirectory(Path.Combine(dir, "empty"));
                var list = BackupList.Scan(new[] { dir, dir, null });
                A.Eq(1, list.Count, "empty folders and duplicates skipped"); A.Eq(2048L, list[0].Bytes); A.Contains(list[0].FilesText, "acore_auth_.sql");
                Audit.Write(dir, "Main", "account create", Gm.AccountCreate("BOB", "Secret1"), "ok");
                var text = File.ReadAllText(Directory.GetFiles(dir, "audit_*.log").Single());
                A.False(text.Contains("Secret1"), "password not in the audit log");
                var log = Path.Combine(dir, "Server.log"); File.WriteAllLines(log, Enumerable.Range(1, 50).Select(i => "line " + i));
                A.Eq("line 50", Logs.Tail(log, 3).Last()); A.Eq(3, Logs.Tail(log, 3).Count);
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
