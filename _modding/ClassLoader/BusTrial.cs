using System;
using System.Collections.Generic;
using System.Linq;

namespace SCClassLoader
{
    /// <summary>
    /// Which number of job workers suits this machine. A fight's load moves all the time, so two windows can't be compared as
    /// they stand: every number is timed between two windows at the home number and scored against those. The home windows also
    /// show how far the fight wanders by itself, and a gap smaller than that is no gap. Among the numbers nothing separates, the
    /// one that kept the most threads busy wins, so the load stays as spread as it can without costing the main thread.
    ///
    /// No clock and no Unity here, only decisions, so this can be tried against a model.
    /// </summary>
    internal sealed class BusTrial
    {
        internal const int Rounds = 4;

        /// <summary>A gap has to be this big to count, however steady the fight.</summary>
        private const double Noticeable = 1.03;

        /// <summary>A fight wandering more than this can't tell one number from another.</summary>
        private const double TooUneven = 1.10;

        private readonly int home;
        private readonly List<int> others;
        private readonly Dictionary<int, List<double>> against = new Dictionary<int, List<double>>();
        private readonly Dictionary<int, List<double>> busy = new Dictionary<int, List<double>>();
        private readonly List<double> wander = new List<double>();
        private int next, round;
        private double homeMs, triedMs, triedBusy;

        internal BusTrial(int home, IEnumerable<int> others)
        {
            this.home = home;
            this.others = others.Where(n => n != home).Distinct().ToList();
            Now = home;
            busy[home] = new List<double>();
            foreach (int n in this.others)
            {
                against[n] = new List<double>();
                busy[n] = new List<double>();
            }
        }

        /// <summary>Doubling up from two, then the game's own number.</summary>
        internal static List<int> Ladder(int most)
        {
            var counts = new List<int>();
            for (int n = 2; n < most; n *= 2) counts.Add(n);
            counts.Add(most);
            return counts;
        }

        /// <summary>The number to run for the next window.</summary>
        internal int Now { get; private set; }

        internal bool Done => others.Count == 0 || round >= Rounds;

        internal int Home => home;

        internal IEnumerable<int> Counts => new[] { home }.Concat(others);

        /// <summary>One finished window at <see cref="Now"/>: main-thread ms a frame, and threads busy.</summary>
        internal void Observe(double ms, double threads)
        {
            if (Done || ms <= 0) return;
            if (Now != home)
            {
                triedMs = ms;
                triedBusy = threads;
                Now = home;
                return;
            }
            if (homeMs > 0 && triedMs > 0)
            {
                int tried = others[next];
                against[tried].Add(triedMs / ((homeMs + ms) / 2));
                busy[tried].Add(triedBusy);
                wander.Add(Math.Max(ms, homeMs) / Math.Min(ms, homeMs));
                triedMs = 0;
                if (++next == others.Count)
                {
                    next = 0;
                    round++;
                }
            }
            busy[home].Add(threads);
            homeMs = ms;
            if (!Done) Now = others[next];
        }

        /// <summary>The fight ended or a load came: what was measured before it says nothing about the windows after.</summary>
        internal void Forget()
        {
            homeMs = triedMs = 0;
            Now = home;
        }

        /// <summary>How far apart two home windows sit: 1.05 is 5%.</summary>
        internal double Wander => wander.Count == 0 ? 1 : Median(wander);

        internal bool Uneven => Wander > TooUneven;

        /// <summary>Main-thread time at this number against the home number: 0.9 is 10% less.</summary>
        internal double Score(int count) => count == home ? 1 : Median(against[count]);

        internal double Threads(int count) => busy[count].Count == 0 ? 0 : busy[count].Average();

        internal int Best
        {
            get
            {
                if (Uneven) return home;
                double within = Counts.Min(Score) * Math.Max(Noticeable, Wander);
                return Counts.Where(n => Score(n) <= within).OrderByDescending(Threads).ThenByDescending(n => n).First();
            }
        }

        private static double Median(List<double> values)
        {
            if (values.Count == 0) return 1;
            List<double> sorted = values.OrderBy(v => v).ToList();
            int mid = sorted.Count / 2;
            return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
        }
    }
}
