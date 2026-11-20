using System;
using System.Collections.Generic;
using System.Linq;

namespace AndersCraftStudio.Core
{
    /// <summary>
    /// The tables open side by side in a tab strip (DBC tables), each keeping its own state (unstaged edits, filter,
    /// page) while you look at another. Oldest unchanged tabs close first when there are too many.
    /// </summary>
    public sealed class OpenTabs<T>
    {
        readonly List<string> order = new List<string>();
        readonly Dictionary<string, T> state = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        readonly Func<T, bool> isDirty;
        public int Max { get; }
        public string Active { get; private set; }

        public OpenTabs(int max, Func<T, bool> isDirty) { Max = Math.Max(1, max); this.isDirty = isDirty ?? (_ => false); }

        public IReadOnlyList<string> Names => order;
        public bool Contains(string name) => state.ContainsKey(name);
        public bool TryGet(string name, out T value) => state.TryGetValue(name, out value);

        /// <summary>Saves a tab's state and makes it the active one. Returns the tabs closed to stay within <see cref="Max"/>.</summary>
        public List<string> Put(string name, T value)
        {
            if (!state.ContainsKey(name)) order.Add(name);
            state[name] = value;
            Active = name;
            var closed = new List<string>();
            while (order.Count > Max)
            {
                var victim = order.FirstOrDefault(n => !string.Equals(n, Active, StringComparison.OrdinalIgnoreCase) && !isDirty(state[n]));
                if (victim == null) break;   // every other tab has unstaged edits: keep them
                order.Remove(victim); state.Remove(victim); closed.Add(victim);
            }
            return closed;
        }

        /// <summary>Closes a tab. Returns the tab to show next (the neighbour), or null.</summary>
        public string Close(string name)
        {
            int i = order.FindIndex(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            if (i < 0) return Active;
            order.RemoveAt(i); state.Remove(name);
            if (!string.Equals(Active, name, StringComparison.OrdinalIgnoreCase)) return Active;
            Active = order.Count == 0 ? null : order[Math.Min(i, order.Count - 1)];
            return Active;
        }

        /// <summary>Forgets saved state of tabs without unstaged edits (e.g. after the changeset changed), keeping them open.</summary>
        public void DropClean(Func<string, bool> keep = null)
        {
            foreach (var n in order.ToList())
                if (state.TryGetValue(n, out var v) && !isDirty(v) && (keep == null || !keep(n))) state[n] = default;
        }

        public bool HasState(string name) => state.TryGetValue(name, out var v) && v != null;
        public int DirtyCount => state.Values.Count(v => v != null && isDirty(v));
    }
}
