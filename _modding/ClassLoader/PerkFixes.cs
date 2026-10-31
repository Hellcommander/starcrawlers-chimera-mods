using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// Skills whose numbers didn't do what they say. [Fixes] PerkNumbers turns all of them off.
    /// <list type="bullet">
    /// <item><b>Iron Harvest</b> (Bullet Farmer): the perk is meant to make it burst at 10 stacks instead of 20, and the
    /// tooltip says 10, but taking the perk set 20 and removing it set 10. So with the perk it burst at 20 until you
    /// happened to open its tooltip (which set the right number as a side effect), and again after every load.</item>
    /// <item><b>Avast Ye!</b> (Space Pirate): the tooltip's stun chance was 1% + 1% per Tempt Fate stack, up to 15%;
    /// the real chance is 3% + 4% per stack, up to 50%. The tooltip now shows the real one.</item>
    /// <item><b>Pyramid Scheme</b> (Thought Leader): the per-minion percentage was multiplied in twice, so 3 minions at
    /// 3% gave +0.27% damage (in practice +1 per hit) instead of +9%, and the damage cut was N × 3% × 30% instead
    /// of N × 3%. The perk's minion soaked 3% instead of the 30% the perk says. All three now match the text.</item>
    /// <item><b>Nanite Replication</b> (Nanomancy perk): at 3 stacks it spent them and said a jar was filled even
    /// with no Empty Nanite Jar in the bag. Now the stacks wait at 3 until you have one.</item>
    /// <item><b>Mech Part</b> (Engineer): the heal looked up the Engineer rank under the wrong name, so it was always
    /// the 10% minimum instead of 5% per Engineer rank (10-30%).</item>
    /// <item><b>Old Faithful</b> (Big Guns): if a stack ever took the bonus past its cap, the stacks were reset at 1%
    /// each while the bonus is 0.5% each, dropping a 20% cap back to 15%. Stacks land exactly on the cap in play, so
    /// this is a guard: going over now holds at the cap (10% per Big Guns rank, at most 30%, the game's own limit).</item>
    /// <item><b>Unobtanium Chrome Job</b> (Cybertechnician): each hit's Toughness bonus and the 3-hit cap were worked
    /// out from Toughness that already held the bonus, so it went past 3 stacks and the cap kept rising. Both now
    /// come from your Toughness without it, and the bonus (and the perk's armor) stops at 3 hits.</item>
    /// <item><b>Tempt Fate</b> (Daring Maneuver perk): the stacks only learned about the perk when they were first
    /// made, so taking the perk mid-stack left them decaying at the old rate. They now follow the perk.</item>
    /// <item><b>Bastion</b> (Force Psyker perk): Fortress was set up twice per proc, spawning its effects twice.</item>
    /// <item><b>Lucky Find</b> (Loot Hunter perk): the tooltip said a triggered Lucky Find had half the upgrade chance
    /// (5-ish %) to also find something from a lower floor, but the code's roll can't fail: it always does, once per
    /// turn. The tooltip now says 100%.</item>
    /// </list>
    /// </summary>
    internal static class PerkFixes
    {
        internal static ConfigEntry<bool> Fix;

        internal static void Bind(ConfigFile cfg)
        {
            Fix = cfg.Bind("Fixes", "PerkNumbers", true,
                "Skills do what their tooltips say: Iron Harvest's perk bursts at 10 stacks, Avast Ye! shows its real stun chance, Pyramid Scheme gives its full per-minion bonus, Nanite Replication waits for an empty jar, Mech Part heals by Engineer rank, Old Faithful holds its cap, Chrome stops at 3 hits, Tempt Fate follows its perk, Fortress is set up once, Lucky Find's perk shows its real chance.");
        }

        // ---- Iron Harvest -------------------------------------------------------------------------------------

        internal static int IronHarvestThreshold(Ability a) => a.perkPoints > 0 ? 10 : 20;

        [HarmonyPatch(typeof(PA_PASSIVE_BulletFarmer_IronHarvest), "DoIronHarvest")]
        private static class Patch_IronHarvest
        {
            private static void Prefix(PA_PASSIVE_BulletFarmer_IronHarvest __instance) { if (Fix.Value) __instance.hitChance = IronHarvestThreshold(__instance); }
        }

        // ---- Avast Ye! ----------------------------------------------------------------------------------------

        /// <summary>Effect_Pirate_Avast's own chance: 3 + 4 per Tempt Fate stack, at most 50.</summary>
        internal static int AvastChance(Character c)
        {
            Effect tf = c?.effects?.GetEffect("EFFECT_Pirate_TemptFate");
            return Mathf.Min(50, 3 + (tf != null ? tf.chargeCount * 4 : 0));
        }

        [HarmonyPatch(typeof(PA_Pirate_Avast), "SetDamage")]
        private static class Patch_Avast
        {
            private static void Postfix(PA_Pirate_Avast __instance)
            {
                if (!Fix.Value) return;
                var tr = Traverse.Create(__instance);
                tr.Field("stunChance").SetValue(AvastChance(tr.Field("caster").GetValue<Character>() ?? Player.instance));
            }
        }

        // ---- Pyramid Scheme -----------------------------------------------------------------------------------

        private static readonly AccessTools.FieldRef<Effect_ThoughtLeader_PyramidScheme, float> PyramidBoost =
            AccessTools.FieldRefAccess<Effect_ThoughtLeader_PyramidScheme, float>("damageBoost");
        private static readonly AccessTools.FieldRef<Effect_ThoughtLeader_PyramidScheme, float> PyramidReduction =
            AccessTools.FieldRefAccess<Effect_ThoughtLeader_PyramidScheme, float>("damageReduction");
        private static readonly AccessTools.FieldRef<Effect_ThoughtLeader_PyramidScheme, float> PyramidAbsorb =
            AccessTools.FieldRefAccess<Effect_ThoughtLeader_PyramidScheme, float>("damageAbsorb");

        // Each replaces one field read in the vanilla method, so the fix is three swapped loads.

        /// <summary>Replaces <c>damageBoost</c> in OnDealDamage's <c>damage * (damageBoost * amountC)</c>: amountC already holds it.</summary>
        public static float PyramidBoostFactor(Effect_ThoughtLeader_PyramidScheme e) => Fix.Value ? 1f : PyramidBoost(e);

        /// <summary>Replaces <c>damageAbsorb</c> in OnTakeDamage's <c>damage * (damageAbsorb * amountC)</c>: the cut is amountC.</summary>
        public static float PyramidCutFactor(Effect_ThoughtLeader_PyramidScheme e) => Fix.Value ? 1f : PyramidAbsorb(e);

        /// <summary>Replaces <c>damageReduction</c> in the perk's minion soak: the minion takes the perk's 30%.</summary>
        public static float PyramidSoak(Effect_ThoughtLeader_PyramidScheme e) => Fix.Value ? PyramidAbsorb(e) : PyramidReduction(e);

        /// <summary>Points the <paramref name="skip"/>-th and later reads of <paramref name="field"/> at <paramref name="helper"/>.</summary>
        private static List<CodeInstruction> SwapFieldReads(IEnumerable<CodeInstruction> code, string field, string helper, int skip, string what)
        {
            var list = code.ToList();
            FieldInfo f = AccessTools.Field(typeof(Effect_ThoughtLeader_PyramidScheme), field);
            int seen = 0, n = 0;
            foreach (CodeInstruction ci in list)
            {
                if (!ci.LoadsField(f) || seen++ < skip) continue;
                ci.opcode = OpCodes.Call;
                ci.operand = AccessTools.Method(typeof(PerkFixes), helper);
                n++;
            }
            if (n != 1) Plugin.Log.LogWarning($"Pyramid Scheme fix ({what}): found {n} places to patch (expected 1)");
            return list;
        }

        [HarmonyPatch(typeof(Effect_ThoughtLeader_PyramidScheme), nameof(Effect.OnDealDamage))]
        private static class Patch_PyramidDeal
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code) =>
                SwapFieldReads(code, "damageBoost", nameof(PyramidBoostFactor), 1, "bonus"); // the first read sets amountC
        }

        [HarmonyPatch(typeof(Effect_ThoughtLeader_PyramidScheme), nameof(Effect.OnTakeDamage))]
        private static class Patch_PyramidTake
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
            {
                var list = SwapFieldReads(code, "damageReduction", nameof(PyramidSoak), 1, "soak"); // the first read sets amountC
                return SwapFieldReads(list, "damageAbsorb", nameof(PyramidCutFactor), 0, "cut");
            }
        }

        // ---- Nanite Replication -------------------------------------------------------------------------------

        private static bool HaveEmptyJar() => Menu_Inventory.instance?.GetItemNamed("CONSUME_NaniteJar_Empty") is Item_Consumable;

        /// <summary>The player's Replication counter (amountA 0) stops at 3 while there's no jar to fill.</summary>
        [HarmonyPatch(typeof(Effect_Nanomancy_Replication), nameof(Effect.ModCharge))]
        private static class Patch_ReplicationCharge
        {
            private static bool Prefix(Effect_Nanomancy_Replication __instance, int c, ref int __result)
            {
                if (!Fix.Value || __instance.amountA != 0 || HaveEmptyJar()) return true;
                __instance.chargeCount = Mathf.Clamp(__instance.chargeCount + c, 0, 3);
                __result = __instance.chargeCount;
                return false;
            }
        }

        [HarmonyPatch(typeof(Effect_Nanomancy_Replication), nameof(Effect.TurnOver_Instant))]
        private static class Patch_ReplicationTurn
        {
            private static void Prefix(Effect_Nanomancy_Replication __instance, out int __state)
            {
                __state = -1;
                if (!Fix.Value || __instance.amountA != 0 || __instance.chargeCount < 3 || HaveEmptyJar()) return;
                __state = __instance.chargeCount;
                __instance.chargeCount = 2; // below the fill, for this turn's check only
            }

            private static void Postfix(Effect_Nanomancy_Replication __instance, int __state) { if (__state >= 0) __instance.chargeCount = __state; }
        }

        // ---- Mech Part ----------------------------------------------------------------------------------------

        [HarmonyPatch(typeof(PA_Engineer_MechPart), "CalculateHeal")]
        private static class Patch_MechPart
        {
            private static void Postfix(PA_Engineer_MechPart __instance)
            {
                if (!Fix.Value || Player_SkillControl.instance == null) return;
                Traverse.Create(__instance).Field("healMod").SetValue(Mathf.Clamp(Player_SkillControl.instance.GetRank("SKILLTREE_Engineer") * 0.05f, 0.1f, 0.3f));
            }
        }

        // ---- Old Faithful -------------------------------------------------------------------------------------

        /// <summary>The bonus is 5% + 0.5% per stack; amountC is its cap, which the game also holds to 30%.</summary>
        internal static int OldFaithfulCapStacks(float cap) => Mathf.RoundToInt((cap - 0.05f) / 0.005f);

        [HarmonyPatch(typeof(Effect_OldFaithful), nameof(Effect_OldFaithful.UpdateMod))]
        private static class Patch_OldFaithful
        {
            private static void Prefix(Effect_OldFaithful __instance, out int __state)
            {
                __state = __instance.chargeCount;
                if (Fix.Value && __instance.amountC > 0.3f) __instance.amountC = 0.3f;
            }

            private static void Postfix(Effect_OldFaithful __instance, int __state)
            {
                if (!Fix.Value || __instance.amountC < 0.05f) return;
                int capStacks = OldFaithfulCapStacks(__instance.amountC);
                if (__state < capStacks) return;
                __instance.chargeCount = capStacks; // vanilla set amountC / 1%, half of what the cap is worth
                __instance.damageMod = __instance.amountC;
            }
        }

        // ---- Unobtanium Chrome Job ----------------------------------------------------------------------------

        /// <summary>Toughness without Chrome's own bonus, taken when a stack run starts (the stat cache can lag a hit).</summary>
        private static readonly Dictionary<int, int> chromeBase = new Dictionary<int, int>();

        [HarmonyPatch(typeof(Effect_Cybertechnician_Chrome), "Set")]
        private static class Patch_ChromeSet
        {
            private static void Postfix(Effect_Cybertechnician_Chrome __instance)
            {
                if (!Fix.Value) return;
                int id = __instance.GetInstanceID();
                if (__instance.amountA <= 0 || !chromeBase.TryGetValue(id, out int tough))
                    chromeBase[id] = tough = Player.instance.GetStat(STAT_TYPE.Toughness) - Mathf.Max(0, __instance.amountA);
                __instance.toughnessBoost = Mathf.Max(1, Mathf.RoundToInt(tough * 0.1f));
                __instance.toughnessMax = __instance.toughnessBoost * 3;
                __instance.perkArmor = Mathf.Max(1, Mathf.RoundToInt(tough * 0.03f));
            }
        }

        [HarmonyPatch(typeof(Effect_Cybertechnician_Chrome), nameof(Effect.OnTakeDamage))]
        private static class Patch_ChromeHit
        {
            private static void Postfix(Effect_Cybertechnician_Chrome __instance)
            {
                if (!Fix.Value || __instance.attributeMods.Count < 2) return;
                if (__instance.amountA > __instance.toughnessMax) __instance.attributeMods[0].amount = __instance.amountA = __instance.toughnessMax;
                if (__instance.amountB > __instance.perkArmor * 3) __instance.attributeMods[1].amount = __instance.amountB = __instance.perkArmor * 3;
            }
        }

        // ---- Tempt Fate ---------------------------------------------------------------------------------------

        [HarmonyPatch(typeof(Effect_TemptFate), nameof(Effect.UpdateDuration))]
        private static class Patch_TemptFatePerk
        {
            private static void Prefix(Effect_TemptFate __instance)
            {
                if (!Fix.Value || __instance.internalName != "EFFECT_Pirate_TemptFate" || __instance.target == null || __instance.target != Player.instance) return;
                Ability dm = Player_AbilityControl.instance?.GetAbility("PA_Pirate_DaringManeuver");
                if (dm != null) __instance.perkPoints = Mathf.Max(0, dm.perkPoints);
            }
        }

        // ---- Bastion's Fortress -------------------------------------------------------------------------------

        /// <summary>Bastion calls Init again on the Fortress ApplyEffect has just set up; the second one only zeroes it.</summary>
        [HarmonyPatch(typeof(Effect_Force_Psyker_Fortress), nameof(Effect.Init))]
        private static class Patch_FortressInit
        {
            private static bool Prefix(Effect_Force_Psyker_Fortress __instance, Character c)
            {
                if (!Fix.Value || c == null || __instance.target != c) return true;
                if (__instance.attributeMods.Count > 0) __instance.attributeMods[0].amount = 0;
                return false;
            }
        }

        // ---- Lucky Find ---------------------------------------------------------------------------------------

        [HarmonyPatch(typeof(PA_PASSIVE_Loot_Hunter_Lucky), nameof(Ability.GetPerkTooltipDescription))]
        private static class Patch_LuckyFindTip
        {
            private static void Postfix(PA_PASSIVE_Loot_Hunter_Lucky __instance, ref string __result)
            {
                if (Fix.Value) __result = ((string)__instance.perkDescription).Replace("[X]", "100"); // vanilla rolls 1-100 against "> 100"
            }
        }
    }
}
