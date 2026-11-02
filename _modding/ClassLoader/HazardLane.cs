using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// What fields, mines and traps cost with [Performance] EventBus on. Their turn is left alone: 400 fields under 120 enemies
    /// is 8-26 ms in one go, so spreading it would only make the turn longer. The cost was in culling, which runs on every step
    /// you take and every turn: for a hazard in a hidden room it switched the hazard off, then its distance check switched it
    /// straight back on, so nothing ended up hidden and every particle, light and sound on it started over, undoing what
    /// ParticleLoad had parked. Here each hazard gets the same final state from the distance check alone.
    ///
    /// "bus:hazards" goes out at most once a frame when a hazard lands, leaves or moves tiles.
    /// </summary>
    internal static class HazardLane
    {
        private static readonly AccessTools.FieldRef<CullManager, List<Hazard>> Culled = AccessTools.FieldRefAccess<CullManager, List<Hazard>>("hazards");
        private static readonly List<Hazard> Nothing = new List<Hazard>();

        private static void Sweep(List<Hazard> hazards, float reach)
        {
            if (Player.instance == null) return;
            Vector3 you = Player.instance.transform.position;
            float far = reach * reach;
            foreach (Hazard h in hazards)
            {
                if (h == null) continue;
                bool wanted = (h.transform.position - you).sqrMagnitude <= far;
                if (h.gameObject.activeInHierarchy != wanted) h.gameObject.SetActive(wanted);
            }
        }

        /// <summary>The game's walk gets an empty list, so its other kinds of object cull as they always did.</summary>
        [HarmonyPatch(typeof(CullManager), nameof(CullManager.UpdateCulling), new Type[0])]
        private static class Patch_Cull
        {
            private static void Prefix(CullManager __instance, out List<Hazard> __state)
            {
                __state = null;
                if (!EventBus.On) return;
                __state = Culled(__instance);
                Culled(__instance) = Nothing;
            }

            private static void Finalizer(CullManager __instance, List<Hazard> __state)
            {
                if (__state == null) return;
                Culled(__instance) = __state;
                Sweep(__state, __instance.hazardCullDistance);
            }
        }

        [HarmonyPatch]
        private static class Patch_Changed
        {
            private static IEnumerable<System.Reflection.MethodBase> TargetMethods() => new System.Reflection.MethodBase[]
            {
                AccessTools.Method(typeof(HazardManager), nameof(HazardManager.AddHazard)),
                AccessTools.Method(typeof(HazardManager), nameof(HazardManager.RemoveHazard), new[] { typeof(Hazard), typeof(bool) }),
                AccessTools.Method(typeof(HazardManager), nameof(HazardManager.Clear)),
                AccessTools.Method(typeof(NavNode), nameof(NavNode.AddHazard)),
                AccessTools.Method(typeof(NavNode), nameof(NavNode.RemoveHazard)),
            };

            private static void Postfix() => EventBus.Mark(EventBus.Hazards);
        }
    }
}
