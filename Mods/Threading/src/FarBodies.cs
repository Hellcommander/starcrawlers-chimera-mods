// Free to use, copy and change for any purpose, like the example plugin.

using System.Collections.Generic;
using Breachwork;
using FIMSpace.FLook;
using HarmonyLib;
using LiquidVolumeFX;
using UnityEngine;

namespace Threading
{
    /// <summary>
    /// Every body in a crowd runs its head-turning, idle chatter and flask sloshing each frame, wherever it stands. Twice a second
    /// where everyone stands is copied, a worker sorts near from far, and the main thread applies the answer a few bodies a frame:
    /// far bodies stop those and let their animator rest whenever no camera sees them. They still act, walk and fight as usual.
    /// </summary>
    public static class FarBodies
    {
        private const float FarTiles = 20f, Every = 0.5f;
        private const int ApplyPerFrame = 64;

        private sealed class Body
        {
            public AI ai;
            public Animator anim;
            public FLookAnimator look;
            public AltIdleAnim idle;
            public LiquidVolume[] liquids;
            public AnimatorCullingMode cull;
            public bool parked;
        }

        private sealed class Answer
        {
            public List<Body> bodies;
            public bool[] far;
            public int next;
        }

        private static readonly AccessTools.FieldRef<AI_Manager, List<AI>> Ais = AccessTools.FieldRefAccess<AI_Manager, List<AI>>("ais");
        private static readonly Dictionary<AI, Body> known = new Dictionary<AI, Body>();
        private static volatile Answer answered;
        private static Answer applying;
        private static float nextSort;

        private static Body Of(AI ai)
        {
            if (known.TryGetValue(ai, out Body b)) return b;
            Character c = ai.GetCharacter();
            b = new Body { ai = ai, anim = c != null ? c.anim : null, look = ai.GetComponentInChildren<FLookAnimator>(true), idle = ai.GetComponentInChildren<AltIdleAnim>(true), liquids = ai.GetComponentsInChildren<LiquidVolume>(true) };
            if (b.anim != null) b.cull = b.anim.cullingMode;
            known[ai] = b;
            return b;
        }

        private static void Set(Body b, bool far)
        {
            if (b.parked == far || b.ai == null) return;
            b.parked = far;
            if (b.look != null) b.look.enabled = !far;
            if (b.idle != null) b.idle.enabled = !far;
            foreach (LiquidVolume l in b.liquids)
                if (l != null) l.enabled = !far;
            if (b.anim != null) b.anim.cullingMode = far ? AnimatorCullingMode.CullCompletely : b.cull;
        }

        internal static void Tick()
        {
            if (!Api.EventBusOn)
            {
                if (known.Count == 0) return;
                foreach (Body b in known.Values) Set(b, false);
                known.Clear();
                answered = applying = null;
                return;
            }
            if (applying == null && answered != null)
            {
                applying = answered;
                answered = null;
            }
            if (applying != null)
            {
                int end = Mathf.Min(applying.next + ApplyPerFrame, applying.bodies.Count);
                for (; applying.next < end; applying.next++) Set(applying.bodies[applying.next], applying.far[applying.next]);
                if (applying.next >= applying.bodies.Count) applying = null;
            }
            if (Time.unscaledTime < nextSort || AI_Manager.instance == null || Player.instance == null || !Api.WorkersHealthy) return;
            nextSort = Time.unscaledTime + Every;
            Sort();
        }

        private static void Sort()
        {
            var bodies = new List<Body>();
            foreach (AI ai in Ais(AI_Manager.instance))
                if (ai != null) bodies.Add(Of(ai));
            float[] x = new float[bodies.Count], z = new float[bodies.Count];
            for (int i = 0; i < bodies.Count; i++)
            {
                Vector3 at = bodies[i].ai.transform.position;
                x[i] = at.x;
                z[i] = at.z;
            }
            Vector3 you = Player.instance.transform.position;
            float youX = you.x, youZ = you.z, reach = FarTiles * FarTiles;
            Api.RunOnWorker(() =>
            {
                var far = new bool[x.Length];
                for (int i = 0; i < far.Length; i++)
                {
                    float dx = x[i] - youX, dz = z[i] - youZ;
                    far[i] = dx * dx + dz * dz > reach;
                }
                answered = new Answer { bodies = bodies, far = far };
            });
        }
    }
}
