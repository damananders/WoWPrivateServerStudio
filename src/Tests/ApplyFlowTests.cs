using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AndersCraftStudio.Core;

namespace AndersCraftStudio.Tests
{
    /// <summary>
    /// Regression: stage → Apply must work again and again, across restarts (a fresh ChangesetStore on the same folder), and
    /// sent DBC edits must leave the staged list. Uses an in-memory table; nothing touches a real database.
    /// </summary>
    static class ApplyFlowTests
    {
        /// <summary>One table (quest_template), rows by ID, string columns. Answers SELECTs and runs UPDATE / INSERT / DELETE.</summary>
        sealed class TinyDb : ISqlExecutor
        {
            public readonly Dictionary<long, Dictionary<string, string>> Rows = new Dictionary<long, Dictionary<string, string>>();
            Dictionary<long, Dictionary<string, string>> snapshot;
            public int Commits;
            static long Id(string sql) => long.Parse(Regex.Match(sql, @"`ID` = (\d+)").Groups[1].Value);
            public ResultTable Query(string sql, int maxRows)
            {
                var t = new ResultTable();
                var cols = Regex.Match(sql, @"^SELECT (.*?) FROM").Groups[1].Value.Split(',').Select(c => c.Trim().Trim('`')).ToList();
                t.Columns.AddRange(cols);
                if (Rows.TryGetValue(Id(sql), out var r)) t.Rows.Add(cols.Select(c => Unquote(r[c])).ToArray());
                return t;
            }
            static object Unquote(string lit) => lit.StartsWith("'") ? lit.Substring(1, lit.Length - 2).Replace("''", "'") : (object)long.Parse(lit);
            public IList<string> PrimaryKey(string table) => new List<string> { "ID" };
            public void Begin() => snapshot = Rows.ToDictionary(kv => kv.Key, kv => new Dictionary<string, string>(kv.Value));
            public void Commit() { snapshot = null; Commits++; }
            public void Rollback() { Rows.Clear(); foreach (var kv in snapshot) Rows[kv.Key] = kv.Value; snapshot = null; }
            public int Execute(string sql)
            {
                var id = Id(sql);
                if (sql.StartsWith("UPDATE"))
                {
                    if (!Rows.TryGetValue(id, out var r)) return 0;
                    foreach (Match m in Regex.Matches(Regex.Match(sql, @" SET (.*) WHERE ").Groups[1].Value, @"`(\w+)` = ('(?:[^']|'')*'|[^,]+)"))
                        r[m.Groups[1].Value] = m.Groups[2].Value.Trim();
                    return 1;
                }
                if (sql.StartsWith("DELETE")) return Rows.Remove(id) ? 1 : 0;
                if (sql.StartsWith("INSERT"))
                {
                    var cols = Regex.Match(sql, @"\((.*?)\) VALUES").Groups[1].Value.Split(',').Select(c => c.Trim().Trim('`')).ToList();
                    var vals = Regex.Match(sql, @"VALUES \((.*)\);").Groups[1].Value.Split(',').Select(v => v.Trim()).ToList();
                    Rows[id] = cols.Zip(vals, (c, v) => (c, v)).ToDictionary(x => x.c, x => x.v);
                    return 1;
                }
                return 0;
            }
        }

        static string NewFolder() { var d = Path.Combine(Path.GetTempPath(), "acs_flow_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); return d; }
        static List<KeyPart> K(long id) => new List<KeyPart> { new KeyPart("ID", id.ToString()) };

        /// <summary>What an editor does: stage an edit whose "before" is what it loaded.</summary>
        static Change Stage(ChangesetStore st, long id, string col, string before, string after) =>
            ChangeFactory.StageCellEdit(st.Pending, "quest_template", K(id), col, before, before, after, after);

        /// <summary>What the Changes screen does on a successful Apply.</summary>
        static ApplyOutcome ApplyTicked(ChangesetStore st, TinyDb db, DateTime now)
        {
            var ticked = st.Pending.Changes.Where(c => c.Selected && !c.Structure && c.Dbc == null).ToList();
            A.Eq(0, ApplyEngine.Validate(ticked, "acore_world_studio_sandbox", new string[0]).Count, "validate");
            var o = ApplyEngine.Apply(ticked, db);
            if (o.Success) st.RecordApplied(ticked, new ApplyRecord { Utc = now, Target = "Sandbox", Database = "acore_world_studio_sandbox", Success = true, Message = o.Message }, now);
            return o;
        }

        [Test] static void Three_applies_then_restart_then_a_fourth()
        {
            var dir = NewFolder();
            try
            {
                var db = new TinyDb();
                db.Rows[1] = new Dictionary<string, string> { ["ID"] = "1", ["LogTitle"] = "'A'", ["MinLevel"] = "1" };
                var now = new DateTime(2026, 11, 25, 12, 0, 0);   // all in the same second: history files must not overwrite each other
                var st = new ChangesetStore(dir);
                // the same row, edited and applied three times in a row; each edit starts from what the last Apply wrote
                Stage(st, 1, "LogTitle", "'A'", "'B'");
                A.True(ApplyTicked(st, db, now).Success, "apply 1");
                Stage(st, 1, "LogTitle", "'B'", "'C'");
                A.True(ApplyTicked(st, db, now).Success, "apply 2");
                Stage(st, 1, "MinLevel", "1", "10");
                A.True(ApplyTicked(st, db, now).Success, "apply 3");
                A.Eq(0, st.Pending.Changes.Count, "staged list empty after each apply");
                A.Eq(3, st.History().Count, "three History entries (none overwritten)");
                A.Eq("'C'", db.Rows[1]["LogTitle"]); A.Eq("10", db.Rows[1]["MinLevel"]);

                // restart: a new store on the same folder
                var st2 = new ChangesetStore(dir);
                A.Eq(0, st2.Pending.Changes.Count, "nothing staged after restart");
                A.Eq(3, st2.History().Count, "history kept");
                Stage(st2, 1, "LogTitle", "'C'", "'D'");
                A.True(ApplyTicked(st2, db, now).Success, "apply 4 after restart");
                A.Eq(4, st2.History().Count, "four History entries");
                A.Eq(4, new ChangesetStore(dir).History().Count, "and still four after another restart");
                A.Eq("'D'", db.Rows[1]["LogTitle"]);
                A.Eq(4, db.Commits, "four transactions");

                // roll back the newest: the row goes back to C
                var newest = st2.History().First(h => h.Changes.Any(c => c.Fields.Any(f => f.After == "'D'")));
                A.True(ApplyEngine.Rollback(newest.Changes, db).Success, "roll back");
                A.Eq("'C'", db.Rows[1]["LogTitle"]);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Test] static void Stale_edit_stops_with_a_plain_reason_and_refresh_fixes_it()
        {
            var dir = NewFolder();
            try
            {
                var db = new TinyDb();
                db.Rows[7] = new Dictionary<string, string> { ["ID"] = "7", ["LogTitle"] = "'Old'", ["MinLevel"] = "5" };
                var st = new ChangesetStore(dir);
                // an editor that loaded the row before an earlier Apply changed it: its "before" is out of date
                db.Rows[7]["LogTitle"] = "'Newer'";
                var c = Stage(st, 7, "LogTitle", "'Old'", "'Mine'");
                var o = ApplyEngine.Apply(st.Pending.Changes.ToList(), db);
                A.False(o.Success, "stops");
                A.Eq(c.Id, o.ConflictChangeId, "names the change");
                A.Eq(ConflictKind.Changed, o.Conflict);
                A.Contains(o.Message, "is now 'Newer'");
                A.Contains(o.Message, "Nothing was changed");
                A.Eq("'Newer'", db.Rows[7]["LogTitle"], "rolled back");

                // refresh the starting point from the row as it is now, then apply
                var cur = db.Query(SqlGen.SelectRow(c.Table, c.Key, c.Fields.Select(f => f.Name), false), 2);
                A.True(ApplyEngine.RefreshBaseline(c, cur), "refreshed");
                A.Eq("'Newer'", c.Fields[0].Before); A.Eq("'Mine'", c.Fields[0].After, "your value kept");
                A.Contains(c.UndoSql, "'Newer'");
                A.True(ApplyEngine.Apply(st.Pending.Changes.ToList(), db).Success, "applies now");
                A.Eq("'Mine'", db.Rows[7]["LogTitle"]);

                // refreshing when the database already has your value leaves nothing to do
                var d = new Change { Kind = ChangeKind.Update, Table = "quest_template", Key = K(7) };
                d.Fields.Add(new FieldChange { Name = "LogTitle", Before = "'x'", After = "'Mine'" });
                A.True(ApplyEngine.RefreshBaseline(d, db.Query(SqlGen.SelectRow("quest_template", K(7), new[] { "LogTitle" }, false), 2)), "refresh");
                A.Eq(0, d.Fields.Count, "nothing left");
                // a row that's gone can't be refreshed
                A.False(ApplyEngine.RefreshBaseline(d, db.Query(SqlGen.SelectRow("quest_template", K(99), new[] { "LogTitle" }, false), 2)), "missing row");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Test] static void New_row_already_there_and_missing_row_are_explained()
        {
            var db = new TinyDb();
            db.Rows[5] = new Dictionary<string, string> { ["ID"] = "5", ["LogTitle"] = "'X'" };
            var cs = new Changeset();
            var ins = ChangeFactory.StageRowInsert(cs, "quest_template", K(5), new[] { "ID", "LogTitle" }, new[] { "5", "'X'" });
            var o = ApplyEngine.Apply(new[] { ins }, db);
            A.Eq(ConflictKind.Exists, o.Conflict); A.Eq(ins.Id, o.ConflictChangeId); A.Contains(o.Message, "already has a row");
            var upd = ChangeFactory.StageCellEdit(new Changeset(), "quest_template", K(42), "LogTitle", "'a'", "a", "'b'", "b");
            var o2 = ApplyEngine.Apply(new[] { upd }, db);
            A.Eq(ConflictKind.Missing, o2.Conflict); A.Contains(o2.Message, "isn't in the database any more");
        }

        static Change DbcEdit(uint key, DateTime created) => new Change
        {
            Kind = ChangeKind.Update, Schema = DbcStager.Schema, Table = "Spell.dbc", Summary = "Edit Spell.dbc " + key, CreatedUtc = created,
            Dbc = new DbcPatch { File = "Spell.dbc", Key = key, Fields = { new DbcFieldEdit { Field = 1, Name = "Category", Before = "0", After = "1" } } },
        };

        [Test] static void Sent_dbc_edits_leave_the_staged_list()
        {
            var dir = NewFolder();
            try
            {
                var st = new ChangesetStore(dir);
                var a = DbcEdit(365004, DateTime.UtcNow); var b = DbcEdit(365005, DateTime.UtcNow);
                st.Pending.Changes.Add(a); st.Pending.Changes.Add(b); st.SavePending();
                // server copy only: still staged, marked as sent
                A.Eq(null, st.MarkDbcSent(new[] { a, b }, true, @"C:\backups\dbc_x"), "nothing moved yet");
                A.Eq(2, st.Pending.Changes.Count);
                A.True(new ChangesetStore(dir).Pending.Changes.All(c => c.Dbc.SentToServerUtc != null), "sent flag survives a restart");
                // then the patch folder: both places → History by itself
                var moved = st.MarkDbcSent(new[] { a }, false, @"C:\work\dbc_edits");
                A.True(moved != null && moved.Changes.Count == 1, "moved once in both places");
                A.Eq(1, st.Pending.Changes.Count);
                // "Done: move to History" for the one only sent to the server
                var moved2 = st.MoveDbcToHistory(st.Pending.Changes.ToList());
                A.Eq(1, moved2.Changes.Count);
                var st2 = new ChangesetStore(dir);
                A.Eq(0, st2.Pending.Changes.Count, "nothing stuck after restart");
                var hist = st2.History();
                A.Eq(2, hist.Count, "both in History");
                A.True(hist.All(h => h.Log.Any(l => l.Kind == "DBC" && l.Success)), "recorded as sent out");
                A.Contains(hist.First(h => h.Changes[0].Dbc.Key == 365005).Log[0].BackupDir, "dbc_x");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Test] static void Older_staged_file_learns_where_dbc_edits_went()
        {
            var dir = NewFolder();
            try
            {
                // like a 0.8 pending.json: a DBC edit that was sent both ways, but the file only has the log of it
                var created = new DateTime(2026, 11, 22, 23, 13, 20, DateTimeKind.Utc);
                var cs = new Changeset { Name = "Staged changes" };
                cs.Changes.Add(DbcEdit(365004, created));
                cs.Log.Add(new ApplyRecord { Utc = created.AddDays(-1), Kind = "DBC → server copy", Database = "dbc", Success = true, BackupDir = "old" });
                cs.Log.Add(new ApplyRecord { Utc = created.AddSeconds(10), Kind = "DBC → patch folder", Database = "dbc", Success = true, BackupDir = @"C:\work" });
                cs.Log.Add(new ApplyRecord { Utc = created.AddSeconds(22), Kind = "DBC → server copy", Database = "dbc", Success = true, BackupDir = @"C:\b\dbc_new" });
                File.WriteAllText(Path.Combine(dir, "pending.json"), System.Text.Json.JsonSerializer.Serialize(cs));
                var st = new ChangesetStore(dir);
                var p = st.Pending.Changes[0].Dbc;
                A.True(p.SentToServerUtc != null && p.SentToPatchUtc != null, "both inferred");
                A.Eq(@"C:\b\dbc_new", p.ServerBackupDir, "the send after staging, not the older one");
                A.Eq(1, st.MoveDbcToHistory(st.Pending.Changes.ToList()).Changes.Count);
                A.Eq(0, new ChangesetStore(dir).Pending.Changes.Count);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
    }
}
