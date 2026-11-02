using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// What a skill costs before and while it lands, with [Performance] EventBus on. Working out a skill's tuning is asked for by
    /// every hotbar icon, every can-I-use-this and every proc, and each ask walked all you know and everything on you to find
    /// the few that rewrite skills; a cast asked about 170 times. The carriers are kept here, in the same order, so an ask reads
    /// those few. Their conditions are still checked every time: only the search for them goes. The list is dropped when your
    /// kit or effects change and each turn, as EventBus's listeners are, and a count that doesn't match rebuilds it, which
    /// covers a change no hook saw.
    /// </summary>
    internal static class SkillLane
    {
        internal static bool Bypass; // devtests: the full walk, bus on
        internal static int Built, Reused;

        internal static IReadOnlyList<ModAbility> Abilities { get; private set; } = new List<ModAbility>();
        internal static IReadOnlyList<ModEffect> Effects { get; private set; } = new List<ModEffect>();

        private static bool fresh;
        private static Player owner;
        private static int abilitiesSeen, effectsSeen;

        internal static void KitChanged(Character who)
        {
            if (who == Player.instance) fresh = false;
        }

        internal static void NewTurn() => fresh = false;

        /// <summary>Everything that rewrites skills right now, in the order the game holds them: a later rewrite wins.</summary>
        internal static void Look(Player_AbilityControl pac, Player you)
        {
            bool keep = EventBus.On && !Bypass;
            if (keep && fresh && owner == you && abilitiesSeen == pac.abilities.Count && effectsSeen == you.effects.effects.Count) { Reused++; return; }
            var abilities = new List<ModAbility>();
            foreach (Ability a in pac.abilities)
                if (a is ModAbility m && m != null && m.Spec?.modifies?.Count > 0) abilities.Add(m);
            var effects = new List<ModEffect>();
            foreach (Effect e in you.effects.effects)
                if (e is ModEffect me && me != null && me.Spec?.modifies?.Count > 0) effects.Add(me);
            Abilities = abilities;
            Effects = effects;
            owner = you;
            abilitiesSeen = pac.abilities.Count;
            effectsSeen = you.effects.effects.Count;
            fresh = keep;
            Built++;
        }

        /// <summary>
        /// The beat between a skill's victims. Off, 0.05 s each, which at a crowd's frame rate is a frame each: 60 victims made a
        /// cast of many seconds. On, the same beat up to the point the whole sweep would pass <see cref="SweepSeconds"/>, then
        /// quicker, and behind schedule victims land together until the frame's budget is spent. Same victims, same order.
        /// </summary>
        internal struct Ripple
        {
            private const float Beat = 0.05f, SweepSeconds = 0.75f;
            private readonly float began, step;
            private int landed, frame;
            private long slice;

            internal Ripple(int victims)
            {
                began = Time.time;
                step = Mathf.Min(Beat, SweepSeconds / Mathf.Max(1, victims));
                landed = 0;
                frame = -1;
                slice = 0;
            }

            /// <summary>After each victim. True: yield <paramref name="wait"/> (null is the next frame).</summary>
            internal bool Hold(out object wait)
            {
                if (!EventBus.On) { wait = new WaitForSeconds(Beat); return true; }
                landed++;
                float ahead = began + landed * step - Time.time;
                if (ahead > 0f) { wait = new WaitForSeconds(ahead); return true; }
                wait = null;
                long now = Stopwatch.GetTimestamp();
                if (frame != Time.frameCount) { frame = Time.frameCount; slice = now; }
                return (now - slice) * 1000.0 / Stopwatch.Frequency >= BusProfile.TurnMs;
            }
        }
    }
}
