using System;
using System.Collections.Generic;
using System.Linq;

namespace AndersCraftStudio.Core
{
    public enum StatementKind
    {
        /// <summary>Only reads data (SELECT, SHOW, DESCRIBE, EXPLAIN ...). Runs immediately on the read-only account.</summary>
        Read,
        /// <summary>Changes rows (INSERT / UPDATE / DELETE / REPLACE). Never runs; it is staged in the changeset.</summary>
        RowWrite,
        /// <summary>Changes table structure (CREATE / ALTER / DROP / TRUNCATE / RENAME). Staged, but Studio will not apply it.</summary>
        StructureWrite,
        /// <summary>Anything else (GRANT, SET, CALL, LOAD DATA, transactions ...). Studio refuses it.</summary>
        Blocked,
    }

    public sealed class StatementInfo
    {
        public string Text;
        public List<Tok> Tokens;
        public StatementKind Kind;
        public string Verb;
        public string Reason;
        public bool IsWrite => Kind == StatementKind.RowWrite || Kind == StatementKind.StructureWrite;
        public override string ToString() => $"{Kind} {Verb}: {Text}";
    }

    /// <summary>Decides whether SQL text only reads, or would change something. Unknown always counts as "not safe to run".</summary>
    public static class SqlClassifier
    {
        static readonly HashSet<string> ReadVerbs = new HashSet<string> { "SELECT", "SHOW", "DESCRIBE", "DESC", "EXPLAIN", "TABLE", "VALUES", "HELP" };
        static readonly HashSet<string> RowVerbs = new HashSet<string> { "INSERT", "UPDATE", "DELETE", "REPLACE" };
        static readonly HashSet<string> StructureVerbs = new HashSet<string> { "CREATE", "ALTER", "DROP", "TRUNCATE", "RENAME" };

        public static List<StatementInfo> Classify(string sql)
        {
            var list = new List<StatementInfo>();
            foreach (var (text, toks) in SqlLexer.Split(sql)) list.Add(ClassifyOne(text, toks));
            return list;
        }

        public static StatementInfo ClassifyOne(string sql)
        {
            var parts = SqlLexer.Split(sql);
            if (parts.Count == 0) return new StatementInfo { Text = "", Tokens = new List<Tok>(), Kind = StatementKind.Blocked, Verb = "", Reason = "Empty statement." };
            if (parts.Count > 1) return new StatementInfo { Text = sql, Tokens = SqlLexer.Lex(sql), Kind = StatementKind.Blocked, Verb = "", Reason = "More than one statement." };
            return ClassifyOne(parts[0].text, parts[0].toks);
        }

        /// <summary>True only when every statement in the text is a plain read.</summary>
        public static bool IsReadOnly(string sql)
        {
            var all = Classify(sql);
            return all.Count > 0 && all.All(s => s.Kind == StatementKind.Read);
        }

        public static StatementInfo ClassifyOne(string text, List<Tok> toks)
        {
            var info = new StatementInfo { Text = text, Tokens = toks };
            if (toks.Any(t => t.IsSym("/*!")))
                return Block(info, "/*!", "Uses a MySQL executable comment (/*! ... */). Studio can't check those safely, so it won't run or stage them.");

            // skip leading '(' (e.g. "(SELECT ...) UNION (SELECT ...)")
            int i = 0;
            while (i < toks.Count && toks[i].IsSym("(")) i++;
            if (i >= toks.Count) return Block(info, "", "Empty statement.");
            var first = toks[i];
            if (first.Kind != TokKind.Word) return Block(info, first.Text, "Doesn't start with a SQL keyword.");
            info.Verb = first.Value;

            if (info.Verb == "WITH")
            {
                // the real verb is the first top-level SELECT/INSERT/UPDATE/DELETE/REPLACE after the CTE list
                var main = toks.Skip(i + 1).FirstOrDefault(t => t.Depth == 0 && t.Kind == TokKind.Word &&
                    (t.Value == "SELECT" || RowVerbs.Contains(t.Value) || t.Value == "TABLE" || t.Value == "VALUES"));
                if (main == null) return Block(info, "WITH", "Couldn't find the main statement after WITH.");
                info.Verb = main.Value;
            }

            if (ReadVerbs.Contains(info.Verb))
            {
                // SELECT ... INTO OUTFILE / DUMPFILE / @var writes files or session state
                if (toks.Any(t => t.Is("INTO") && (info.Verb == "SELECT" || info.Verb == "TABLE" || info.Verb == "VALUES")))
                    return Block(info, info.Verb, "SELECT ... INTO writes to a file or variable, which Studio doesn't allow.");
                if (info.Verb == "EXPLAIN" || info.Verb == "DESCRIBE" || info.Verb == "DESC")
                {
                    // EXPLAIN ANALYZE <write> would actually execute the write
                    if (toks.Any(t => t.Is("ANALYZE")) && toks.Any(t => t.Kind == TokKind.Word && RowVerbs.Contains(t.Value)))
                        return Block(info, info.Verb, "EXPLAIN ANALYZE runs the statement for real; use plain EXPLAIN.");
                }
                info.Kind = StatementKind.Read;
                info.Reason = "Reads data only.";
                return info;
            }
            if (RowVerbs.Contains(info.Verb))
            {
                info.Kind = StatementKind.RowWrite;
                info.Reason = "Changes rows. It will be staged in your changeset, not run.";
                return info;
            }
            if (StructureVerbs.Contains(info.Verb))
            {
                info.Kind = StatementKind.StructureWrite;
                info.Reason = "Changes table structure. It is recorded in your changeset, but Studio only applies row changes; run structure changes yourself with an admin account.";
                return info;
            }
            switch (info.Verb)
            {
                case "USE": return Block(info, "USE", "Pick the database in the tree on the left instead of USE.");
                case "SET": return Block(info, "SET", "SET changes server or session settings, which Studio doesn't do.");
                case "GRANT":
                case "REVOKE": return Block(info, info.Verb, "Account permissions are managed outside Studio (see the setup script).");
                case "CALL": return Block(info, "CALL", "Stored procedures can do anything, so Studio won't run or stage them.");
                case "LOAD": return Block(info, "LOAD", "LOAD DATA/XML imports files; Studio doesn't do that.");
                case "START":
                case "BEGIN":
                case "COMMIT":
                case "ROLLBACK":
                case "SAVEPOINT":
                case "RELEASE":
                case "XA": return Block(info, info.Verb, "Studio manages transactions itself when you Apply.");
                default: return Block(info, info.Verb, $"Studio doesn't recognise {info.Verb} as a safe read, so it won't run it.");
            }
        }

        static StatementInfo Block(StatementInfo i, string verb, string reason)
        {
            i.Kind = StatementKind.Blocked; i.Verb = verb; i.Reason = reason; return i;
        }
    }
}
