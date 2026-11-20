using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace AndersCraftStudio.Core
{
    /// <summary>
    /// A spell as a shareable file (.studio-spell.json): every Spell.dbc field by name, plus the related rows it uses
    /// (range, cast time, duration, radius, icon, visual), so another server owner can import it.
    /// </summary>
    public static class SpellPackage
    {
        public const string Format = "AndersCraftStudio.Spell";

        sealed class Pkg
        {
            public string format { get; set; } = Format;
            public int version { get; set; } = 1;
            public uint id { get; set; }
            public string name { get; set; }
            public Dictionary<string, string> spell { get; set; } = new Dictionary<string, string>();
            public List<Rel> related { get; set; } = new List<Rel>();
        }
        sealed class Rel { public string file { get; set; } public string column { get; set; } public uint id { get; set; } public Dictionary<string, string> fields { get; set; } = new Dictionary<string, string>(); }

        /// <summary>Which spell columns point to which related DBC (the rows that travel with the spell).</summary>
        public static readonly (string column, string file)[] RelatedColumns =
        {
            ("RangeIndex", "SpellRange.dbc"), ("CastingTimeIndex", "SpellCastTimes.dbc"), ("DurationIndex", "SpellDuration.dbc"),
            ("EffectRadiusIndex_1", "SpellRadius.dbc"), ("EffectRadiusIndex_2", "SpellRadius.dbc"), ("EffectRadiusIndex_3", "SpellRadius.dbc"),
            ("SpellIconID", "SpellIcon.dbc"), ("ActiveIconID", "SpellIcon.dbc"), ("SpellVisualID_1", "SpellVisual.dbc"), ("SpellVisualID_2", "SpellVisual.dbc"),
        };

        static Dictionary<string, string> Fields(DbcTable t, DbcRow r) => Enumerable.Range(0, t.Layout.FieldCount).ToDictionary(f => t.Layout.FieldName(f), f => t.FieldText(r, f));

        public static string Export(SpellData s, Func<string, DbcTable> related)
        {
            var p = new Pkg { id = s.Id, name = s.Name, spell = Fields(s.Table, s.Row) };
            foreach (var (col, file) in RelatedColumns)
            {
                var id = s.U(col);
                if (id == 0 || p.related.Any(x => x.file == file && x.id == id)) continue;
                var t = related(file); var r = t?.Find(id);
                if (r != null) p.related.Add(new Rel { file = file, column = col, id = id, fields = Fields(t, r) });
            }
            return JsonSerializer.Serialize(p, new JsonSerializerOptions { WriteIndented = true });
        }

        public sealed class ImportResult { public uint NewId; public string Name; public List<string> Notes = new List<string>(); public HashSet<string> TouchedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase); }

        /// <summary>
        /// Adds the packaged spell to <paramref name="spells"/> (working copy) under the first free ID at or above
        /// <paramref name="start"/>. Related rows are reused when an identical row exists, otherwise added under a new ID
        /// and the spell is pointed at it. Only working copies change; the caller stages them.
        /// </summary>
        public static ImportResult Import(string json, DbcTable spells, Func<string, DbcTable> relatedWork, uint start, Changeset pending)
        {
            Pkg p;
            try { p = JsonSerializer.Deserialize<Pkg>(json); } catch (Exception ex) { throw new FormatException("Not a Studio spell file: " + ex.Message); }
            if (p == null || p.format != Format || p.spell == null || p.spell.Count == 0) throw new FormatException("Not a Studio spell file.");
            var res = new ImportResult { Name = p.name };
            var remap = new Dictionary<(string, uint), uint>();
            foreach (var rel in p.related)
            {
                var t = relatedWork(rel.file);
                if (t == null) { res.Notes.Add($"{rel.file} isn't available here; {rel.column} keeps {rel.id}."); continue; }
                var existing = t.Find(rel.id);
                if (existing != null && rel.fields.All(kv => { int f = t.Layout.FieldByName(kv.Key); return f < 0 || t.FieldText(existing, f) == kv.Value; }))
                { remap[(rel.file, rel.id)] = rel.id; continue; }
                var same = t.Rows.FirstOrDefault(r => rel.fields.All(kv => { int f = t.Layout.FieldByName(kv.Key); return f <= 0 || t.FieldText(r, f) == kv.Value; }));
                if (same != null) { remap[(rel.file, rel.id)] = same.V[0]; res.Notes.Add($"{rel.file}: using existing row {same.V[0]} (same values as {rel.id})."); continue; }
                var nr = t.NewRow();
                foreach (var kv in rel.fields) { int f = t.Layout.FieldByName(kv.Key); if (f > 0) t.TrySetField(nr, f, kv.Value, out _); }
                nr.V[0] = t.NextFreeId(start);
                t.Rows.Add(nr); t.InvalidateIndex();
                remap[(rel.file, rel.id)] = nr.V[0];
                res.TouchedFiles.Add(rel.file);
                res.Notes.Add($"{rel.file}: added row {nr.V[0]} for {rel.column}.");
            }
            var row = spells.NewRow();
            foreach (var kv in p.spell)
            {
                int f = spells.Layout.FieldByName(kv.Key);
                if (f < 0) { res.Notes.Add($"Field {kv.Key} isn't in this Spell.dbc layout; skipped."); continue; }
                if (!spells.TrySetField(row, f, kv.Value, out var err)) res.Notes.Add($"{kv.Key}: {err}");
            }
            foreach (var (col, file) in RelatedColumns)
            {
                int f = spells.Layout.FieldByName(col);
                if (f >= 0 && remap.TryGetValue((file, row.V[f]), out var to)) row.V[f] = to;
            }
            res.NewId = SpellIds.FreeId(spells, pending, start);
            row.V[0] = res.NewId;
            spells.Rows.Add(row); spells.InvalidateIndex();
            res.TouchedFiles.Add("Spell.dbc");
            return res;
        }
    }
}
