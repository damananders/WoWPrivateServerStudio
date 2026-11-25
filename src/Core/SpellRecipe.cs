using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AndersCraftStudio.Core
{
    /// <summary>
    /// The guided "Create a spell": a few plain choices turned into a complete Spell.dbc row. It starts from a real spell of
    /// the same kind (so visuals, targeting and flags are ones the game already handles), then sets the numbers, text, icon,
    /// cost, cast time, cooldown, range and duration, clears what the template had that you didn't ask for, and makes sure
    /// it will show in the spellbook.
    /// </summary>
    public static class SpellRecipe
    {
        public enum Kind { Damage, Heal, Buff, Passive }

        public sealed class Boost { public string Label; public uint Aura; public int Misc; public string Words; public bool Percent; }

        /// <summary>What a buff or passive raises (aura and misc value), in words.</summary>
        public static readonly Boost[] Boosts =
        {
            new Boost { Label = "Stamina", Aura = 29, Misc = 2, Words = "Stamina" },
            new Boost { Label = "Strength", Aura = 29, Misc = 0, Words = "Strength" },
            new Boost { Label = "Agility", Aura = 29, Misc = 1, Words = "Agility" },
            new Boost { Label = "Intellect", Aura = 29, Misc = 3, Words = "Intellect" },
            new Boost { Label = "Spirit", Aura = 29, Misc = 4, Words = "Spirit" },
            new Boost { Label = "All stats", Aura = 29, Misc = -1, Words = "all stats" },
            new Boost { Label = "Armor", Aura = 22, Misc = 1, Words = "Armor" },
            new Boost { Label = "Attack power", Aura = 99, Misc = 0, Words = "attack power" },
            new Boost { Label = "Spell power", Aura = 13, Misc = 126, Words = "spell damage" },
            new Boost { Label = "Run speed (%)", Aura = 31, Misc = 0, Words = "run speed", Percent = true },
            new Boost { Label = "Experience gained (%)", Aura = 200, Misc = 0, Words = "experience gained", Percent = true },
        };

        public static readonly (uint mask, string name)[] Schools =
        {
            (1, "Physical"), (2, "Holy"), (4, "Fire"), (8, "Nature"), (16, "Frost"), (32, "Shadow"), (64, "Arcane"),
        };

        public sealed class Recipe
        {
            public Kind Kind = Kind.Damage;
            public string Name = "New spell", Description;
            public uint IconId;
            public int Amount = 100;
            public uint School = 4;
            public Boost Boost = Boosts[0];
            public int CastMs = 1500, CooldownSec, ManaPct = 5, DurationSec = 1800;
            /// <summary>0 = on yourself, otherwise the range in yards.</summary>
            public int RangeYards = 30;
        }

        /// <summary>The spell each kind starts from (3.3.5a IDs): Fireball, Lesser Heal, Power Word: Fortitude.</summary>
        public static uint TemplateOf(Kind k) => k switch { Kind.Damage => 133, Kind.Heal => 2050, _ => 1243 };

        /// <summary>Fills <paramref name="sp"/> (a copy of the template under its new ID). Returns notes on what was approximated.</summary>
        public static List<string> Apply(SpellData sp, Recipe r, Func<string, DbcTable> table)
        {
            var notes = new List<string>();
            void Set(string f, string v) { if (!sp.Set(f, v, out var err) && sp.Field(f) >= 0) notes.Add(f + ": " + err); }
            void SetU(string f, long v) => Set(f, v.ToString(CultureInfo.InvariantCulture));

            // only effect 1 is ours; the template's other effects (Fireball's burn, etc.) go
            foreach (var i in new[] { 2, 3 })
                foreach (var f in new[] { "Effect_", "EffectAura_", "EffectBasePoints_", "EffectDieSides_", "EffectMiscValue_", "EffectMiscValueB_", "EffectTriggerSpell_", "ImplicitTargetA_", "ImplicitTargetB_", "EffectAuraPeriod_", "EffectRadiusIndex_", "EffectChainTargets_", "EffectItemType_", "EffectMechanic_" })
                    SetU(f + i, 0);
            SetU("EffectTriggerSpell_1", 0);
            // no class family: class talents and glyphs won't change it
            SetU("SpellClassSet", 0); foreach (var i in new[] { 1, 2, 3 }) SetU("SpellClassMask_" + i, 0);
            foreach (var i in new[] { 1, 2, 3 }) foreach (var c in new[] { "A", "B", "C" }) SetU($"EffectSpellClassMask{c}_{i}", 0);
            SetU("SpellDescriptionVariableID", 0); SetU("SpellDifficultyID", 0); SetU("ModalNextSpell", 0);
            SetU("MaxLevel", 0); SetU("BaseLevel", 1); SetU("SpellLevel", 1);
            foreach (var i in Enumerable.Range(1, 8)) { SetU("Reagent_" + i, 0); SetU("ReagentCount_" + i, 0); }

            Set("Name_enUS", r.Name ?? "");
            Set("NameSubtext_enUS", "");
            if (r.IconId != 0) SetU("SpellIconID", r.IconId);   // 0 = keep the template's icon
            SetU("EffectBasePoints_1", Math.Max(0, r.Amount) - 1);
            SetU("EffectDieSides_1", 1);
            SetU("EffectRealPointsPerLevel_1", 0);
            SetU("ManaCost", 0); SetU("ManaCostPerLevel", 0); SetU("ManaCostPct", Math.Max(0, Math.Min(100, r.ManaPct))); SetU("PowerType", 0);
            SetU("RecoveryTime", Math.Max(0, r.CooldownSec) * 1000L); SetU("CategoryRecoveryTime", 0); SetU("Category", 0);
            SetU("StartRecoveryTime", 1500); SetU("StartRecoveryCategory", 133);

            uint attr = sp.U("Attributes") & ~SpellBook.AttrDoNotDisplay & ~SpellBook.AttrPassive;
            string desc, aura = "";
            switch (r.Kind)
            {
                case Kind.Damage:
                    SetU("SchoolMask", r.School);
                    var school = Schools.FirstOrDefault(s => s.mask == r.School).name ?? "magic";
                    desc = $"Deals $s1 {school} damage to the target.";
                    break;
                case Kind.Heal:
                    desc = "Heals the target for $s1.";
                    break;
                default:
                    SetU("EffectAura_1", r.Boost.Aura); SetU("EffectMiscValue_1", r.Boost.Misc);
                    var amount = r.Boost.Percent ? "$s1%" : "$s1";
                    if (r.Kind == Kind.Passive)
                    {
                        attr |= SpellBook.AttrPassive;
                        desc = $"Increases your {r.Boost.Words} by {amount}.";
                        r.RangeYards = 0; r.CastMs = 0; r.CooldownSec = 0; SetU("RecoveryTime", 0); SetU("ManaCostPct", 0); SetU("StartRecoveryTime", 0); SetU("StartRecoveryCategory", 0);
                    }
                    else
                    {
                        desc = $"Increases {(r.RangeYards == 0 ? "your" : "the target's")} {r.Boost.Words} by {amount} for $d.";
                        aura = $"{Cap(r.Boost.Words)} increased by {amount}.";
                    }
                    break;
            }
            SetU("Attributes", attr);
            Set("Description_enUS", string.IsNullOrWhiteSpace(r.Description) ? desc : r.Description);
            Set("AuraDescription_enUS", aura);

            // targeting: yourself, or a target in range (the template's targeting otherwise)
            if (r.RangeYards == 0) { SetU("ImplicitTargetA_1", 1); SetU("ImplicitTargetB_1", 0); SetU("Targets", 0); }
            SetU("CastingTimeIndex", Pick(table("SpellCastTimes.dbc"), "Base", r.CastMs, 1, notes, "cast time", v => v + " ms"));
            SetU("RangeIndex", r.RangeYards == 0 ? 1 : PickF(table("SpellRange.dbc"), "RangeMax_1", r.RangeYards, notes));
            if (r.Kind == Kind.Buff) SetU("DurationIndex", Pick(table("SpellDuration.dbc"), "Duration", r.DurationSec * 1000, 0, notes, "duration", v => v / 1000 + " s"));
            if (r.Kind == Kind.Passive) SetU("DurationIndex", Pick(table("SpellDuration.dbc"), "Duration", -1, 0, notes, "duration", v => "until cancelled"));
            if (r.Kind is Kind.Damage or Kind.Heal) SetU("DurationIndex", 0);
            return notes;
        }

        static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        /// <summary>The row of a lookup table whose value matches (or is closest to) what was asked.</summary>
        static uint Pick(DbcTable t, string field, int want, uint fallback, List<string> notes, string what, Func<int, string> show)
        {
            if (t == null) return fallback;
            int f = t.Layout.FieldByName(field);
            if (f < 0) return fallback;
            var best = t.Rows.OrderBy(r => Math.Abs((long)unchecked((int)r.V[f]) - want)).ThenBy(r => r.V[0]).FirstOrDefault();
            if (best == null) return fallback;
            int got = unchecked((int)best.V[f]);
            if (got != want) notes.Add($"No {what} of exactly {show(want)} exists; using {show(got)}.");
            return best.V[0];
        }

        static uint PickF(DbcTable t, string field, int yards, List<string> notes)
        {
            if (t == null) return 1;
            int f = t.Layout.FieldByName(field), mn = t.Layout.FieldByName("RangeMin_1");
            var best = t.Rows.Where(r => mn < 0 || BitConverter.Int32BitsToSingle(unchecked((int)r.V[mn])) == 0)
                .OrderBy(r => Math.Abs(BitConverter.Int32BitsToSingle(unchecked((int)r.V[f])) - yards)).ThenBy(r => r.V[0]).FirstOrDefault();
            if (best == null) return 1;
            var got = BitConverter.Int32BitsToSingle(unchecked((int)best.V[f]));
            if (Math.Abs(got - yards) > 0.01) notes.Add($"No range of exactly {yards} yd exists; using {got:0} yd.");
            return best.V[0];
        }
    }
}
