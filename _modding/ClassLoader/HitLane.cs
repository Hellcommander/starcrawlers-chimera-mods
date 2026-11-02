using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;

namespace SCClassLoader
{
    /// <summary>
    /// An area hit is a whole run of actions per victim, all in one frame. With the bus on, a burst lands its first victim
    /// (and you, always) at once and the rest in the same order from the bus's budget, so a wave rolls over a crowd instead of
    /// stopping the frame. The same context carries through, as the loop it replaces did, and whatever is still waiting lands
    /// before the turn ends, unless that alone would stall the game: then the last of it lands just after. Only when it lands changes, never what it does: a hit keeps how deep in
    /// procs and bursts it was queued, so a chain of death blasts stops at the same link and a proc's hit still fires no procs.
    /// </summary>
    internal static class HitLane
    {
        // A queue that outgrows its budget isn't saving work, only owing it: past this the hits land where they're cast, so
        // what's left to flush at the end of a turn is always a moment's work.
        private const int MinCrowd = 4, MostWaiting = 64, MostInFlush = 512;
        private const double HitsPerMs = 8.0, FlushMs = 250.0;
        private static bool warned;

        private struct Hit { public IEnumerable<ActionSpec> actions; public ActionContext ctx; public Character who; public int procDepth, burstDepth; }

        private static readonly Queue<Hit> waiting = new Queue<Hit>();

        internal static int Spread, Deepest;

        /// <summary>Runs <paramref name="actions"/> on each victim, as a foreach over them would.</summary>
        internal static void Each(List<Character> victims, IEnumerable<ActionSpec> actions, ActionContext ctx)
        {
            bool spread = EventBus.On && victims.Count >= MinCrowd;
            bool first = true;
            foreach (Character v in victims)
            {
                if (v == null || v.GetIsDead()) continue;
                var hit = new Hit { actions = actions, ctx = ctx, who = v, procDepth = Actions.ProcDepth, burstDepth = ModEffect.burstDepth };
                if (!spread || first || v == Player.instance || waiting.Count >= MostWaiting) Land(hit);
                else
                {
                    waiting.Enqueue(hit);
                    Spread++;
                    if (waiting.Count > Deepest) Deepest = waiting.Count;
                }
                first = false;
            }
        }

        private static void Land(Hit h)
        {
            if (h.who == null || h.who.GetIsDead()) return;
            if ((object)h.ctx.caster != null && h.ctx.caster == null) return; // went with its map
            int procDepth = Actions.ProcDepth, burstDepth = ModEffect.burstDepth;
            Actions.ProcDepth = h.procDepth;
            ModEffect.burstDepth = h.burstDepth;
            h.ctx.target = h.who;
            try { Actions.Run(h.actions, h.ctx, h.who); }
            catch (System.Exception e) { Plugin.Log.LogError("Event bus hit failed: " + e); }
            finally { Actions.ProcDepth = procDepth; ModEffect.burstDepth = burstDepth; }
        }

        /// <summary>At least one a frame, so a slow hit can't stall the queue behind the budget; a longer queue gets longer.</summary>
        internal static void Drain(Stopwatch clock, double budgetMs)
        {
            double budget = System.Math.Max(budgetMs, waiting.Count / HitsPerMs);
            while (waiting.Count > 0)
            {
                Land(waiting.Dequeue());
                if (clock.Elapsed.TotalMilliseconds >= budget) return;
            }
            DecalLane.Drain();
        }

        internal static void Flush()
        {
            if (!EventBus.On) DecalLane.Forget();
            if (waiting.Count == 0) return;
            long began = Stopwatch.GetTimestamp();
            for (int i = 0; i < MostInFlush && waiting.Count > 0; i++) // hits can queue more hits
            {
                Hit next = waiting.Peek();
                Land(waiting.Dequeue());
                double ms = (Stopwatch.GetTimestamp() - began) * 1000.0 / Stopwatch.Frequency;
                if (ms < FlushMs || waiting.Count == 0) continue;
                // better a hit landing a moment into the next turn than a game that looks hung
                if (!warned) Plugin.Log.LogWarning($"Event bus: {waiting.Count} hits held over after {ms:0} ms, last from {next.ctx.sourceName}; they land over the next frames.");
                warned = true;
                return;
            }
        }

        /// <summary>A load has destroyed who these hits were for.</summary>
        internal static void Forget()
        {
            waiting.Clear();
            DecalLane.Forget();
            Swaps.Forget();
        }

        [HarmonyPatch(typeof(Game), nameof(Game.EndTurn))]
        private static class Patch_EndTurn
        {
            private static void Prefix() => Flush();
        }
    }
}
