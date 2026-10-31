using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// The final fight (Cerberus) piled things up with no limit, which players felt as the fight getting slower:
    /// <list type="bullet">
    /// <item><b>Warp hazards:</b> every warp leaves hazards on all tiles around where it left and where it lands. The
    /// check for "already one here" only looked for the normal warp hazard, so the phase-3 inferno warp stacked a new
    /// inferno hazard (particles and a light each) on the same tiles every time. Now a tile takes one warp hazard of
    /// either kind, and past <c>MaxWarpHazards</c> the oldest burns out to make room.</item>
    /// <item><b>Portal adds:</b> the phase-3 portal spawns an add every 5 turns until phase 4, with no limit. Now it
    /// waits while <c>MaxPortalAdds</c> of them are alive.</item>
    /// <item><b>Annihilation beam:</b> its particles were emitted at 20 per second per metre of beam, rewritten every
    /// frame, so a beam across the arena threw thousands. Now at most <c>BeamParticlesPerSecond</c> per stream.</item>
    /// </list>
    /// [Fixes] CerberusLimits turns all three off.
    /// </summary>
    internal static class CerberusFight
    {
        internal static ConfigEntry<bool> Fix;
        internal static ConfigEntry<int> MaxWarpHazards, MaxPortalAdds, BeamParticlesPerSecond;
        internal const string AddTag = "SCCL_PORTAL_ADD";

        internal static void Bind(ConfigFile cfg)
        {
            Fix = cfg.Bind("Fixes", "CerberusLimits", true,
                "The final fight stops piling things up: one warp hazard per tile and a cap on them, a cap on live portal adds, and a lighter annihilation beam.");
            MaxWarpHazards = cfg.Bind("Fixes", "CerberusMaxWarpHazards", 30, new ConfigDescription(
                "Most warp hazards (normal and inferno) on the floor at once in the final fight; the oldest burns out for a new one.", new AcceptableValueRange<int>(6, 200)));
            MaxPortalAdds = cfg.Bind("Fixes", "CerberusMaxPortalAdds", 4, new ConfigDescription(
                "Most adds from the phase-3 portal alive at once; it waits while there are this many.", new AcceptableValueRange<int>(1, 20)));
            BeamParticlesPerSecond = cfg.Bind("Fixes", "CerberusBeamParticles", 300, new ConfigDescription(
                "Most particles per second per stream of the annihilation beam (vanilla: 20 per metre of beam).", new AcceptableValueRange<int>(50, 2000)));
        }

        private static MA_BOSS_Cerberus Boss => MA_BOSS_Cerberus.instance;

        /// <summary>The two warp hazard ids, from the boss's own warp ability.</summary>
        internal static HashSet<string> WarpIds()
        {
            var ids = new HashSet<string>();
            MA_BOSS_Cerberus_Warp w = Boss?.Ability_Warp;
            if (w?.hazardPrefab != null) ids.Add(w.hazardPrefab.internalName);
            if (w?.infernoHazard != null) ids.Add(w.infernoHazard.internalName);
            // the inferno warp may be a second warp ability on the boss
            foreach (var other in Boss != null ? Boss.GetComponents<MA_BOSS_Cerberus_Warp>() : new MA_BOSS_Cerberus_Warp[0])
            {
                if (other.hazardPrefab != null) ids.Add(other.hazardPrefab.internalName);
                if (other.infernoHazard != null) ids.Add(other.infernoHazard.internalName);
            }
            return ids;
        }

        internal static List<Hazard> WarpHazards(HashSet<string> ids) =>
            HazardManager.instance.hazards.Where(h => h != null && ids.Contains(h.internalName)).ToList();

        internal static int LiveAdds() =>
            AI_Manager.instance.FindCharactersByTag("CERBERUS_SUMMON").Concat(AI_Manager.instance.FindCharactersByTag(AddTag))
                .Where(c => c != null && !c.GetIsDead()).Distinct().Count();

        // ---- warp hazards -------------------------------------------------------------------------------------

        [HarmonyPatch(typeof(HazardManager), nameof(HazardManager.CreateHazard), typeof(string), typeof(Vector3))]
        private static class Patch_CreateHazard
        {
            private static bool Prefix(string id, Vector3 pos)
            {
                if (!Fix.Value || Boss == null || HazardManager.instance == null) return true;
                var ids = WarpIds();
                if (!ids.Contains(id)) return true;
                var existing = WarpHazards(ids);
                // a tile already burning with either kind takes no second one
                if (existing.Any(h => (h.transform.position - pos).sqrMagnitude < 0.25f)) return false;
                int over = existing.Count - MaxWarpHazards.Value + 1;
                foreach (Hazard old in existing.Take(Mathf.Max(0, over))) old.RemoveHazard();
                return true;
            }
        }

        // ---- portal adds --------------------------------------------------------------------------------------

        private static bool summoning;

        [HarmonyPatch(typeof(Hazard_Summoning), nameof(Hazard_Summoning.DoUpdate_Instant))]
        private static class Patch_Portal
        {
            private static void Prefix(Hazard_Summoning __instance)
            {
                summoning = true;
                if (!Fix.Value || Boss == null) return;
                // hold the countdown one short of a spawn while the cap is full
                if (__instance.hazardFlag_A + 1 >= __instance.summonOnTurn && LiveAdds() >= MaxPortalAdds.Value)
                    __instance.hazardFlag_A = __instance.summonOnTurn - 2;
            }

            private static void Postfix() => summoning = false;
        }

        [HarmonyPatch(typeof(AI_Manager), nameof(AI_Manager.Spawn_Character), typeof(Character), typeof(Vector3), typeof(AI.AI_TEAM))]
        private static class Patch_Spawn
        {
            private static void Postfix(Character __result) { if (summoning && __result != null) __result.AddTag(AddTag); }
        }

        // ---- annihilation beam --------------------------------------------------------------------------------

        /// <summary>From Plugin.LateUpdate: the game rewrites the beam's emission every frame; cap it after.</summary>
        internal static void Tick()
        {
            if (Boss == null || !Fix.Value) return;
            var beam = Boss.Ability_Annihilation;
            if (beam?.particles == null) return;
            float cap = BeamParticlesPerSecond.Value;
            foreach (ParticleSystem ps in beam.particles)
            {
                if (ps == null) continue;
                var em = ps.emission;
                if (em.rateOverTimeMultiplier > cap) em.rateOverTimeMultiplier = cap;
            }
        }
    }
}
