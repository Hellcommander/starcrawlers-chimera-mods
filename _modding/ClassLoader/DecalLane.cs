using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// A grenade's scorch is a new object with its own copy of a mesh, cut to fit the floor on the next Update, so a crowd
    /// throwing them pays for every one inside its explosion. With the bus on a scorch nobody can see isn't made, and the rest are
    /// made one a frame. They're only marks on the floor: the thrower never looks at the result.
    /// </summary>
    internal static class DecalLane
    {
        private const int MostWaiting = 8;

        private struct Mark { public GameObject prefab, receiver; public Vector3 at, normal; }

        private static readonly Queue<Mark> waiting = new Queue<Mark>();
        private static bool landing;

        internal static int Spread, Unseen, Dropped;

        private static readonly MethodBase Project =
            AccessTools.Method(AccessTools.TypeByName("ch.sycoforge.Decal.EasyDecal"), "ProjectAt", new[] { typeof(GameObject), typeof(GameObject), typeof(Vector3), typeof(Vector3) });

        internal static void Drain()
        {
            if (waiting.Count == 0) return;
            Mark m = waiting.Dequeue();
            if (m.prefab == null || m.receiver == null) return;
            landing = true;
            try { Project.Invoke(null, new object[] { m.prefab, m.receiver, m.at, m.normal }); }
            catch (System.Exception e) { Plugin.Log.LogWarning("Event bus decal failed: " + e.Message); }
            finally { landing = false; }
        }

        /// <summary>A load has destroyed what these would land on.</summary>
        internal static void Forget() => waiting.Clear();

        [HarmonyPatch]
        private static class Patch_Project
        {
            private static bool Prepare() => Project != null;

            private static MethodBase TargetMethod() => Project;

            // skipping the game's method answers null, which its three callers ignore
            private static bool Prefix(GameObject decalPrefab, GameObject receiver, Vector3 position, Vector3 normal)
            {
                if (landing || !EventBus.On) return true;
                if (EventBus.Unseen(position)) { Unseen++; return false; }
                if (waiting.Count >= MostWaiting) { Dropped++; return false; }
                waiting.Enqueue(new Mark { prefab = decalPrefab, receiver = receiver, at = position, normal = normal });
                Spread++;
                return false;
            }
        }
    }
}
