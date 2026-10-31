using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;

namespace SCClassLoader
{
    /// <summary>
    /// Chemistry's passives reach every chem they describe. [Fixes] in the config file turns each part off.
    /// <list type="bullet">
    /// <item>Special Treat (Xeno's Craft Ooze Jar perk) feeds the chem to your pet slime. Weirdest Science already
    /// doubled the good half there, but every chem checked "is the target the player?" before Try Anything Once
    /// (dodge the crash, less addiction) or Weirdest Science (crash lasts half as long, ArmrAll blocks 8 hits) applied,
    /// so the slime always crashed in full. Your pet slime now counts as you for those checks.</item>
    /// <item>Try Anything Once never lowered the addiction chance for Zerk, Promethean, YEETI, Weep, Mindscrew or
    /// Soulsuck: their check read "target is not the player" inside a block that only runs for the player.</item>
    /// <item>Weirdest Science halved the duration of YEETI, Weep, Mindscrew and Soulsuck themselves, not only their
    /// crash, while doubling their charges. The good effect now keeps its full duration.</item>
    /// <item>Weirdest Science's doubling of Promethean, YEETI, Weep, Mindscrew and Soulsuck doubled the whole charge
    /// total, not the dose just taken, so a second dose on top of a running one grew it exponentially. It now
    /// doubles the charges that dose added.</item>
    /// </list>
    /// </summary>
    internal static class ChemistryFixes
    {
        internal static ConfigEntry<bool> PetSlime, TryAddiction, KeepPositives, DoubleDose;

        internal static void Bind(ConfigFile cfg)
        {
            PetSlime = cfg.Bind("Fixes", "ChemistryOnPetSlime", true,
                "A chem fed to your pet slime with Special Treat gets Try Anything Once and Weirdest Science like a chem you take yourself.");
            TryAddiction = cfg.Bind("Fixes", "TryAnythingOnceAddiction", true,
                "Try Anything Once lowers the addiction chance of Zerk, Promethean, YEETI, Weep, Mindscrew and Soulsuck too (vanilla's check could never pass).");
            KeepPositives = cfg.Bind("Fixes", "WeirdestScienceKeepsPositives", true,
                "Weirdest Science halves only the crash of YEETI, Weep, Mindscrew and Soulsuck, not the good effect itself.");
            DoubleDose = cfg.Bind("Fixes", "WeirdestScienceDoublesDose", true,
                "Weirdest Science doubles the charges of the Promethean/YEETI/Weep/Mindscrew/Soulsuck dose you just took, not everything already running (vanilla re-dosing grew exponentially).");
        }

        private static bool IsPetSlime(UnityEngine.Object o) =>
            o is Character c && c != null && !c.GetIsDead() && c.HasTag("XENO_SLIME");

        /// <summary>Replaces <c>target == Player.instance</c>: the player, or the pet slime Special Treat feeds.</summary>
        public static bool IsChemUser(UnityEngine.Object target, UnityEngine.Object player) =>
            target == player || (PetSlime.Value && player != null && player == Player.instance && IsPetSlime(target));

        /// <summary>
        /// Replaces <c>target != Player.instance</c> in the addiction checks, which only run for the player and meant
        /// "is the player": with the fix on it answers that, so Try Anything Once applies.
        /// </summary>
        public static bool AddictionCheck(UnityEngine.Object target, UnityEngine.Object player) =>
            TryAddiction.Value ? IsChemUser(target, player) : target != player;

        private static readonly FieldInfo PlayerInstance = AccessTools.Field(typeof(Player), nameof(Player.instance));
        private static readonly MethodInfo ObjEq = AccessTools.Method(typeof(UnityEngine.Object), "op_Equality");
        private static readonly MethodInfo ObjNe = AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality");

        /// <summary>Every chem-effect method that compares its target with the player.</summary>
        private static readonly (System.Type type, string[] methods)[] Targets =
        {
            (typeof(Effect_ArmrAll), new[] { "Set" }),
            (typeof(Effect_Zerk), new[] { "Set", "Expired_Duration", "Clear" }),
            (typeof(Effect_ChooChoo), new[] { "Set", "Expired_Duration", "Clear" }),
            (typeof(Effect_MentX), new[] { "Set", "Expired_Duration", "Clear" }),
            (typeof(Effect_Spice), new[] { "Set", "Expired_Duration", "Clear" }),
            (typeof(Effect_Promethean), new[] { "ModCharge", "Set", "Clear" }),
            (typeof(Effect_DamageShield), new[] { "ModCharge", "Init", "Clear" }),
        };

        [HarmonyPatch]
        private static class Patch_ChemTargets
        {
            private static IEnumerable<MethodBase> TargetMethods() =>
                Targets.SelectMany(t => t.methods.Select(m => (MethodBase)AccessTools.DeclaredMethod(t.type, m))).Where(m => m != null);

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code, MethodBase original)
            {
                var list = code.ToList();
                int eq = 0, ne = 0;
                for (int i = 1; i < list.Count; i++)
                {
                    if (!list[i - 1].LoadsField(PlayerInstance)) continue;
                    if (list[i].Calls(ObjEq)) { list[i].operand = AccessTools.Method(typeof(ChemistryFixes), nameof(IsChemUser)); eq++; }
                    else if (list[i].Calls(ObjNe)) { list[i].operand = AccessTools.Method(typeof(ChemistryFixes), nameof(AddictionCheck)); ne++; }
                }
                if (eq + ne == 0) Plugin.Log.LogWarning($"Chemistry fix: no player check found in {original.DeclaringType?.Name}.{original.Name}");
                return list;
            }
        }

        /// <summary>The perk's halving in Init meant the crash; the good YEETI-family effect gets its duration back.</summary>
        [HarmonyPatch(typeof(Effect_DamageShield), nameof(Effect_DamageShield.Init))]
        private static class Patch_ShieldDuration
        {
            private static void Prefix(Effect_DamageShield __instance, out int __state) => __state = __instance.duration;

            private static void Postfix(Effect_DamageShield __instance, int __state)
            {
                if (KeepPositives.Value && !__instance.isWithdrawal) __instance.duration = __state;
            }
        }

        // ---- Weirdest Science's doubled dose -------------------------------------------------------------------

        private static readonly HashSet<string> Doubled = new HashSet<string>
            { "EFFECT_CHEM_Promethean", "EFFECT_CHEM_YEETI", "EFFECT_CHEM_Mindscrew", "EFFECT_CHEM_Soulsuck", "EFFECT_CHEM_Weep" };

        /// <summary>The doubled chem already running on a target before this dose, and its charges then.</summary>
        private static Effect doseBefore;
        private static int doseBeforeCharge;

        private static void NoteDose(string internalName, Character target)
        {
            if (target == null || target.effects == null || !Doubled.Contains(internalName)) return;
            doseBefore = target.effects.GetEffect(internalName);
            doseBeforeCharge = doseBefore != null ? doseBefore.GetCharge() : 0;
        }

        [HarmonyPatch(typeof(EffectManager), nameof(EffectManager.ApplyEffect), typeof(string), typeof(Character), typeof(int))]
        private static class Patch_NoteDoseCharged
        {
            private static void Prefix(string internalName, Character target) => NoteDose(internalName, target);
        }

        [HarmonyPatch(typeof(EffectManager), nameof(EffectManager.ApplyEffect), typeof(string), typeof(Character))]
        private static class Patch_NoteDose
        {
            private static void Prefix(string internalName, Character target) => NoteDose(internalName, target);
        }

        /// <summary>Replaces <c>effect.GetCharge()</c> in Weirdest Science's <c>effect.ModCharge(effect.GetCharge())</c>.</summary>
        public static int DoseCharge(Effect e)
        {
            int all = e.GetCharge();
            if (!DoubleDose.Value) return all;
            int dose = e == doseBefore ? Math.Max(0, all - doseBeforeCharge) : all;
            doseBefore = null;
            return dose;
        }

        [HarmonyPatch]
        private static class Patch_WeirdestDose
        {
            private static MethodBase TargetMethod() => AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(Item_OnUse_Trigger), nameof(Item_OnUse_Trigger.Use)));

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
            {
                var list = code.ToList();
                MethodInfo getCharge = AccessTools.Method(typeof(Effect), nameof(Effect.GetCharge));
                MethodInfo modCharge = AccessTools.Method(typeof(Effect), nameof(Effect.ModCharge));
                int n = 0;
                for (int i = 0; i + 1 < list.Count; i++)
                    if (list[i].Calls(getCharge) && list[i + 1].Calls(modCharge))
                    {
                        list[i].opcode = OpCodes.Call;
                        list[i].operand = AccessTools.Method(typeof(ChemistryFixes), nameof(DoseCharge));
                        n++;
                    }
                if (n != 1) Plugin.Log.LogWarning($"Weirdest Science dose fix: found {n} places to patch (expected 1)");
                return list;
            }
        }
    }
}
