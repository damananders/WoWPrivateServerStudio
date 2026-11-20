using System;
using System.Collections.Generic;
using System.Linq;

namespace AndersCraftStudio.Core
{
    public sealed class LootRow
    {
        public int Item, Reference, GroupId, MinCount = 1, MaxCount = 1, LootMode = 1;
        public float Chance;
        public bool QuestRequired;
    }

    /// <summary>
    /// Rolls a loot table the way AzerothCore does (default drop rates):
    ///  • rows with GroupId 0 are rolled one by one with their own Chance (%);
    ///  • each group (GroupId > 0) drops at most one row: rows with a Chance are tried in order against one roll,
    ///    and if none hits, one of the rows with Chance 0 is picked at random (so such a group always drops);
    ///  • a row with a Reference rolls its Chance and then runs the referenced reference_loot_template MaxCount times;
    ///  • normal rows drop MinCount..MaxCount copies; quest-only rows count only if "has the quests" is on.
    /// </summary>
    public sealed class LootSim
    {
        public sealed class ItemStat { public int Item; public long Total; public long KillsWithDrop; public double PerKill, DropPct, AvgWhenDropped; }

        readonly Func<string, int, List<LootRow>> load;
        readonly Dictionary<(string, int), List<LootRow>> cache = new Dictionary<(string, int), List<LootRow>>();
        readonly Random rnd;
        public bool HasQuests = true;
        public int LootMode = 1;
        public List<string> Warnings { get; } = new List<string>();

        /// <param name="load">(table, entry) → rows. References are loaded from "reference_loot_template".</param>
        public LootSim(Func<string, int, List<LootRow>> load, int seed = 12345) { this.load = load; rnd = new Random(seed); }

        List<LootRow> Rows(string table, int entry)
        {
            if (!cache.TryGetValue((table, entry), out var r)) cache[(table, entry)] = r = load(table, entry) ?? new List<LootRow>();
            return r;
        }

        double Roll100() => rnd.NextDouble() * 100.0;

        public List<ItemStat> Run(string table, int entry, int kills)
        {
            var total = new Dictionary<int, long>();
            var hit = new Dictionary<int, long>();
            var thisKill = new Dictionary<int, int>();
            for (int k = 0; k < kills; k++)
            {
                thisKill.Clear();
                Process(table, entry, thisKill, 0, 0);
                foreach (var kv in thisKill)
                {
                    total[kv.Key] = total.GetValueOrDefault(kv.Key) + kv.Value;
                    hit[kv.Key] = hit.GetValueOrDefault(kv.Key) + 1;
                }
            }
            return total.Select(kv => new ItemStat
            {
                Item = kv.Key, Total = kv.Value, KillsWithDrop = hit[kv.Key],
                PerKill = (double)kv.Value / Math.Max(1, kills), DropPct = 100.0 * hit[kv.Key] / Math.Max(1, kills),
                AvgWhenDropped = (double)kv.Value / Math.Max(1, hit[kv.Key]),
            }).OrderByDescending(s => s.PerKill).ToList();
        }

        bool Eligible(LootRow r) => (r.LootMode & LootMode) != 0 && (HasQuests || !r.QuestRequired);

        void Process(string table, int entry, Dictionary<int, int> got, int onlyGroup, int depth)
        {
            if (depth > 8) { if (!Warnings.Contains("Reference loop")) Warnings.Add("Reference loop: references nest more than 8 deep, stopped there."); return; }
            var rows = Rows(table, entry);
            if (onlyGroup == 0)
                foreach (var r in rows.Where(r => r.GroupId == 0))
                {
                    if ((r.LootMode & LootMode) == 0) continue;
                    if (r.Chance < 100f && Roll100() >= r.Chance) continue;
                    if (r.Reference != 0) { for (int i = 0; i < Math.Max(0, r.MaxCount); i++) Process("reference_loot_template", Math.Abs(r.Reference), got, r.GroupId, depth + 1); }
                    else Add(r, got);
                }
            foreach (var g in rows.Where(r => r.GroupId > 0 && (onlyGroup == 0 || r.GroupId == onlyGroup)).GroupBy(r => r.GroupId))
            {
                var pick = RollGroup(g.ToList(), got);
                if (pick == null) continue;
                if (pick.Reference != 0) { for (int i = 0; i < Math.Max(0, pick.MaxCount); i++) Process("reference_loot_template", Math.Abs(pick.Reference), got, 0, depth + 1); }
                else Add(pick, got);
            }
        }

        LootRow RollGroup(List<LootRow> rows, Dictionary<int, int> got)
        {
            // like the server, a group won't hand out an item that is already in this loot
            var possible = rows.Where(r => Eligible(r) && (r.Reference != 0 || !got.ContainsKey(r.Item))).ToList();
            var explicitly = possible.Where(r => r.Chance > 0).ToList();
            if (explicitly.Count > 0)
            {
                double roll = Roll100();
                foreach (var r in explicitly)
                {
                    if (r.Chance >= 100f) return r;
                    roll -= r.Chance;
                    if (roll < 0) return r;
                }
            }
            var equal = possible.Where(r => r.Chance == 0).ToList();
            return equal.Count > 0 ? equal[rnd.Next(equal.Count)] : null;
        }

        void Add(LootRow r, Dictionary<int, int> got)
        {
            if (r.QuestRequired && !HasQuests) return;
            int n = r.MaxCount > r.MinCount ? rnd.Next(r.MinCount, r.MaxCount + 1) : Math.Max(1, r.MinCount);
            got[r.Item] = got.GetValueOrDefault(r.Item) + n;
        }

        /// <summary>Plain-English description of what a group does, for the editor.</summary>
        public static string DescribeGroup(IList<LootRow> rows)
        {
            if (rows.Count == 0) return "";
            if (rows[0].GroupId == 0) return "Each row is rolled on its own, so several of them can drop together.";
            double sum = rows.Where(r => r.Chance > 0).Sum(r => r.Chance);
            int equal = rows.Count(r => r.Chance == 0);
            var s = $"At most one of these {rows.Count} drops per kill.";
            if (equal > 0) s += sum >= 100 ? " The chances already add up to 100% or more, so the rows with chance 0 never drop." : $" {sum:0.##}% goes to the rows with a chance; the remaining {Math.Max(0, 100 - sum):0.##}% is shared evenly by the {equal} row(s) with chance 0, so something always drops.";
            else s += sum >= 100 ? " The chances add up to 100% or more, so something always drops." : $" The chances add up to {sum:0.##}%, so {100 - sum:0.##}% of kills get nothing from this group.";
            return s;
        }
    }
}
