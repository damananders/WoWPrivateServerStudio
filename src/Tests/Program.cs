using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AndersCraftStudio.Core;

namespace AndersCraftStudio.Tests
{
    [AttributeUsage(AttributeTargets.Method)] sealed class TestAttribute : Attribute { }

    static class A
    {
        public static void Eq<T>(T expected, T actual, string what = "")
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception($"{what} expected <{expected}> but got <{actual}>");
        }
        public static void True(bool c, string what) { if (!c) throw new Exception("expected true: " + what); }
        public static void False(bool c, string what) { if (c) throw new Exception("expected false: " + what); }
        public static void Contains(string hay, string needle) { if (hay == null || !hay.Contains(needle)) throw new Exception($"expected to contain <{needle}> in <{hay}>"); }
        public static void Throws(Action a, string what) { try { a(); } catch { return; } throw new Exception("expected exception: " + what); }
    }

    /// <summary>Tiny dependency-free test runner: every [Test] static method in this assembly runs; exit code = failures.</summary>
    static class Program
    {
        static int Main(string[] args)
        {
            if (args.Length >= 1 && args[0] == "--recover-names")
                return ClientScan.Recover(DevPaths.ClientData, DevPaths.ServerDbc, args.Length > 1 ? args[1] : null);
            if (args.Length >= 1 && args[0] == "--scan-client") return ClientScan.Run(args.Length > 1 ? args[1] : DevPaths.ClientData, args.Contains("--extract"), args.Contains("--all"));
            if (args.Length >= 1 && args[0] == "--probe-fx") return FxProbe.Run(args[1]);
            if (args.Length >= 1 && args[0] == "--dbc-roundtrip") return DbcRoundTrip.Run(args.Length > 1 ? args[1] : DbcRoundTrip.DefaultDir);
            if (args.Length >= 1 && args[0] == "--icons") return IconProbe.Run(args.Length > 1 ? args[1] : DevPaths.ClientData);
            if (args.Length >= 1 && args[0] == "--spell-tip") return SpellProbe.Run(args.Skip(1).ToArray());
            int pass = 0, fail = 0;
            var tests = typeof(Program).Assembly.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                .Where(m => m.GetCustomAttribute<TestAttribute>() != null)
                .OrderBy(m => m.DeclaringType.Name).ThenBy(m => m.Name);
            foreach (var m in tests)
            {
                var name = m.DeclaringType.Name + "." + m.Name;
                try { m.Invoke(null, null); pass++; Console.WriteLine("  PASS  " + name); }
                catch (TargetInvocationException e) { fail++; Console.WriteLine("  FAIL  " + name + "\n        " + e.InnerException?.Message); }
            }
            Console.WriteLine($"\n{pass} passed, {fail} failed, {pass + fail} total");
            return fail;
        }
    }

    /// <summary>In-memory stand-in for a database: answers queries from canned results and records every write.</summary>
    sealed class FakeDb : ISqlExecutor
    {
        public readonly List<string> Log = new List<string>();
        public Func<string, ResultTable> OnQuery = _ => new ResultTable();
        public Func<string, int> OnExecute = _ => 1;
        public Dictionary<string, IList<string>> Pks = new Dictionary<string, IList<string>>(StringComparer.OrdinalIgnoreCase);
        public ResultTable Query(string sql, int maxRows) { Log.Add("Q " + sql); return OnQuery(sql); }
        public IList<string> PrimaryKey(string table) => Pks.TryGetValue(table, out var k) ? k : new List<string>();
        public void Begin() => Log.Add("BEGIN");
        public void Commit() => Log.Add("COMMIT");
        public void Rollback() => Log.Add("ROLLBACK");
        public int Execute(string sql) { Log.Add("X " + sql); return OnExecute(sql); }

        public static ResultTable Table(string[] cols, params object[][] rows)
        {
            var t = new ResultTable();
            t.Columns.AddRange(cols);
            foreach (var r in rows) t.Rows.Add(r);
            return t;
        }
    }
}
