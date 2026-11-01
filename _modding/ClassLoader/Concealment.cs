using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// "concealment": 65 on an effect: while its bearer wears it, an enemy's line-of-sight check on the bearer fails that
    /// often, so the enemy loses track of it mid-fight (it goes searching, or turns on someone it can see: a summon). Up
    /// close (within Adjacent tiles) the enemy always finds the bearer; the dark hides, it doesn't make you a ghost. The
    /// roll is made once per enemy, per bearer, per game turn, so an enemy that lost you stays lost for that turn instead of
    /// flickering, and the strongest concealment on the bearer counts. Allies' own sight is never dimmed.
    /// Shadow Magus: Unlit (Shadowflame, the Shade's dark) and Nightwalker (Lose Them).
    /// </summary>
    internal static class Concealment
    {
        private const float Adjacent = 1.6f;
        private static readonly Dictionary<long, bool> rolls = new Dictionary<long, bool>();
        private static int rollsTurn = -1;
        internal static int Hidden; // devtests: sight checks the dark took away

        /// <summary>The strongest concealment on a character, 0-100.</summary>
        internal static int Of(Character c)
        {
            if (c == null || c.effects == null) return 0;
            int best = 0;
            foreach (Effect e in c.effects.GetEffects())
                if (e is ModEffect me && ModEffect.Specs.TryGetValue(me.internalName, out var rt) && rt.spec.concealment > best) best = rt.spec.concealment;
            return Mathf.Clamp(best, 0, 100);
        }

        /// <summary>Whether this observer loses sight of the bearer this turn.</summary>
        internal static bool Hides(Character observer, Character bearer)
        {
            int pct = Of(bearer);
            if (pct <= 0 || observer == null || observer == bearer) return false;
            if (TileStacks.IsAlly(observer) == TileStacks.IsAlly(bearer)) return false; // friends see each other
            if (Vector3.Distance(observer.transform.position, bearer.transform.position) <= Adjacent) return false;
            if (rollsTurn != ProcHub.TurnCount) { rolls.Clear(); rollsTurn = ProcHub.TurnCount; }
            long key = ((long)observer.GetInstanceID() << 32) ^ (uint)bearer.GetInstanceID();
            if (!rolls.TryGetValue(key, out bool hidden))
            {
                hidden = Random.Range(0, 100) < pct;
                rolls[key] = hidden;
            }
            return hidden;
        }

        /// <summary>A Horror is a thing of the dark: while you wear any concealment it cannot find you at all.</summary>
        internal static bool HorrorsLoseYou(Character you) => Of(you) > 0;

        /// <summary>Whoever else is nearest for a Horror that lost its quarry: any living character that is not on your side
        /// (enemies, outsiders, bystanders), within reach of the spot.</summary>
        internal static Character OtherPrey(Vector3 at, Character notThis)
        {
            if (AI_Manager.instance == null) return null;
            return AI_Manager.instance.GetCharactersAt(at, 30f, omitPlayer: true)
                .Where(c => c != null && c != notThis && !c.GetIsDead() && !TileStacks.IsAlly(c))
                .OrderBy(c => Vector3.Distance(c.transform.position, at)).FirstOrDefault();
        }

        // ---- the Cursed class's own Horror: it walks at you and bites you; in the dark it takes someone else -----------

        private static readonly Dictionary<Hazard_Cursed_Horror, Character> horrorPrey = new Dictionary<Hazard_Cursed_Horror, Character>();
        internal static int CursedTurned; // devtests: the Cursed Horror's steps that went for someone else

        [HarmonyPatch(typeof(Hazard_Cursed_Horror), nameof(Hazard_Cursed_Horror.DoUpdate_Instant))]
        private static class Patch_CursedHunt
        {
            private static bool Prefix(Hazard_Cursed_Horror __instance)
            {
                try
                {
                    if (Player.instance == null || !HorrorsLoseYou(Player.instance)) { horrorPrey.Remove(__instance); return true; }
                    Character prey = OtherPrey(__instance.transform.position, Player.instance);
                    horrorPrey[__instance] = prey;
                    if (prey == null) return false; // nobody to turn on: it drifts where it is
                    var t = Traverse.Create(__instance);
                    if (!t.Field("isMoving").GetValue<bool>())
                    {
                        t.Field("isMoving").SetValue(true);
                        t.Field("moveTimer").SetValue(0f);
                        t.Field("startPos").SetValue(__instance.transform.position);
                    }
                    t.Field("targetPos").SetValue(Vector3.MoveTowards(__instance.transform.position, prey.transform.position, 1f));
                    CursedTurned++;
                    return false;
                }
                catch (System.Exception e) { Plugin.Log.LogWarning("Concealment (Horror hunt): " + e.Message); return true; }
            }
        }

        [HarmonyPatch(typeof(Hazard_Cursed_Horror), "Attack")]
        private static class Patch_CursedBite
        {
            private static bool Prefix(Hazard_Cursed_Horror __instance)
            {
                try
                {
                    if (Player.instance == null || !HorrorsLoseYou(Player.instance)) return true;
                    if (!horrorPrey.TryGetValue(__instance, out Character prey) || prey == null || prey.GetIsDead()) return false;
                    if (Vector3.Distance(__instance.transform.position, prey.transform.position) > 0.5f) return false;
                    Ability bite = __instance.attackAbility;
                    if (bite == null) return false;
                    int num = Mathf.Max(1, (int)(prey.GetHealthMax() * 0.06f));
                    for (int i = 0; i < bite.damageList.Count && i < 3; i++) bite.damageList[i].damage = num;
                    bite.DealDamage(null, prey, bite.GetDamageList(), string.Concat(__instance.hazardName, " ", bite.useString[0]), bite, null);
                    ScreenShake.GetInstance().Shake(0.1f, 3f);
                    AudioManager.PlayEvent("event:/Mobs/Mimic/Mimic_Attack", __instance.transform.position);
                    Game.LOG($"<color=#7CFFD0>The Horror</color> cannot find you in the dark and takes {prey.GetName()} instead.");
                    return false;
                }
                catch (System.Exception e) { Plugin.Log.LogWarning("Concealment (Horror bite): " + e.Message); return true; }
            }
        }

        internal static string Describe(int pct) =>
            pct <= 0 ? "" : $"Enemies more than a tile away lose sight of you {pct}% of the time (rolled once a turn each)";

        [HarmonyPatch(typeof(AI_Detection), nameof(AI_Detection.CanSee), typeof(Character))]
        private static class Patch_CanSee
        {
            private static void Postfix(AI_Detection __instance, Character c, ref bool __result)
            {
                if (!__result || c == null) return;
                try
                {
                    AI ai = __instance != null ? __instance.GetComponentInParent<AI>() : null;
                    Character observer = ai != null ? ai.GetCharacter() : null;
                    if (Hides(observer, c)) { __result = false; Hidden++; }
                }
                catch (System.Exception e) { Plugin.Log.LogWarning("Concealment: " + e.Message); }
            }
        }
    }
}
