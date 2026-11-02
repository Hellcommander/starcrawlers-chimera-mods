// Free to use, copy and change for any purpose, like the example plugin.

using System.Linq;
using Breachwork;
using FIMSpace.FLook;
using HarmonyLib;
using UnityEngine;

namespace Threading
{
    /// <summary>
    /// The switch for spreading the game's work out. With this mod on the event bus runs: enemy paths go to worker threads,
    /// area hits and particle bursts spread over frames, and models off screen stop their head-turning bone work. Off, none
    /// of it loads and the game runs as it always did.
    /// </summary>
    public class ThreadingPlugin : BreachworkPlugin
    {
        public override void OnLoad()
        {
            Api.RunEventBus();
            Context.Harmony.PatchAll(GetType().Assembly);
        }

        public override void OnUpdate()
        {
            BatchPaths.Tick();
            FarBodies.Tick();
        }

        /// <summary>The head-look's own off-screen check, which the game never gives a mesh to watch.</summary>
        [HarmonyPatch(typeof(AI), nameof(AI.Init))]
        private static class Patch_LookOnlyWhenSeen
        {
            private static void Postfix(AI __instance)
            {
                Transform model = __instance.GetModel();
                FLookAnimator look = model != null ? model.GetComponent<FLookAnimator>() : null;
                if (look != null && look.OptimizeWithMesh == null) look.OptimizeWithMesh = model.GetComponentsInChildren<Renderer>().FirstOrDefault(r => r is SkinnedMeshRenderer);
            }
        }
    }
}
