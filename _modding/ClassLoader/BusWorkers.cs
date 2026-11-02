using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// The event bus's worker lane. Work is copied into plain data on the main thread, runs on the workers, and any answer
    /// that has to touch the game comes back to the main thread inside the bus's own budget. Nothing on a worker may touch
    /// a game object or a Unity call: only what it was handed.
    /// </summary>
    internal static class BusWorkers
    {
        internal static ConfigEntry<int> Threads;

        private const int MostQueued = 256;
        private const double StuckSeconds = 2.0;

        private static readonly ConcurrentQueue<Action> jobs = new ConcurrentQueue<Action>();
        private static readonly ConcurrentQueue<Action> finished = new ConcurrentQueue<Action>();
        private static readonly SemaphoreSlim wake = new SemaphoreSlim(0);
        private static int started, queued, busy;
        private static long lastMoved;
        private static bool wasStuck;

        internal static int Count { get; private set; }
        internal static int Refused, MostBusy;

        internal static void Init(ConfigFile cfg)
        {
            Threads = cfg.Bind("Performance", "WorkerThreads", 0,
                "Needs EventBus on. Threads the event bus may use for work that doesn't need the game itself, like enemy pathfinding. 0 = what HardwareTier gives: on most machines all of your CPU's threads but two, kept for the game and its drawing. Windows places them on whichever cores are free.");
        }

        /// <summary>Main thread. How many pieces to cut work into so that at least half the workers get some.</summary>
        internal static int Pieces(int work)
        {
            Start();
            return Math.Max(1, Math.Min(work, (Count + 1) / 2));
        }

        private static void Start()
        {
            if (Interlocked.Exchange(ref started, 1) != 0) return;
            int most = Math.Max(1, Environment.ProcessorCount - 2); // never pinned: Windows moves threads off busy or hot cores itself
            Count = Threads.Value > 0 ? Math.Min(Threads.Value, most) : BusProfile.Workers;
            for (int i = 0; i < Count; i++)
                new Thread(Loop) { IsBackground = true, Name = "Breachwork worker " + i, Priority = System.Threading.ThreadPriority.BelowNormal }.Start();
        }

        private static void Loop()
        {
            while (true)
            {
                wake.Wait();
                if (!jobs.TryDequeue(out Action job)) continue;
                int now = Interlocked.Increment(ref busy);
                for (int was = Volatile.Read(ref MostBusy); now > was; was = Volatile.Read(ref MostBusy))
                    if (Interlocked.CompareExchange(ref MostBusy, now, was) == was) break;
                try { job(); }
                catch (Exception e) { finished.Enqueue(() => Plugin.Log.LogError("Event bus worker failed: " + e)); }
                Interlocked.Decrement(ref busy);
                Interlocked.Decrement(ref queued);
                Interlocked.Exchange(ref lastMoved, Stopwatch.GetTimestamp());
            }
        }

        /// <summary>Main thread. False while handed work has sat unfinished too long; the lane steps aside until it moves again.</summary>
        internal static bool Healthy
        {
            get
            {
                long still = Stopwatch.GetTimestamp() - Interlocked.Read(ref lastMoved);
                bool stuck = Volatile.Read(ref queued) > 0 && still > StuckSeconds * Stopwatch.Frequency;
                if (stuck != wasStuck)
                {
                    wasStuck = stuck;
                    if (stuck) Plugin.Log.LogWarning("Event bus workers have stopped answering; the game does this work itself until they recover.");
                    else Plugin.Log.LogInfo("Event bus workers are answering again.");
                }
                return !stuck;
            }
        }

        /// <summary>Main thread. Runs on a worker, with no way back. False when the workers are full or stuck, and the caller keeps the work.</summary>
        internal static bool Run(Action work)
        {
            Start();
            if (Volatile.Read(ref queued) >= MostQueued || !Healthy) { Refused++; return false; }
            if (Interlocked.Increment(ref queued) == 1) Interlocked.Exchange(ref lastMoved, Stopwatch.GetTimestamp());
            jobs.Enqueue(work);
            wake.Release();
            return true;
        }

        /// <summary>Main thread. <paramref name="work"/> on a worker, then <paramref name="apply"/> on the main thread from the bus's budget.</summary>
        internal static bool Run<T>(Func<T> work, Action<T> apply)
        {
            return Run(() =>
            {
                T result = work();
                finished.Enqueue(() => apply(result));
            });
        }

        internal static void Drain(Stopwatch clock, double budgetMs)
        {
            while (clock.Elapsed.TotalMilliseconds < budgetMs && finished.TryDequeue(out Action apply))
            {
                try { apply(); }
                catch (Exception e) { Plugin.Log.LogError("Event bus result failed: " + e); }
            }
        }

        /// <summary>A load has destroyed what these answers were about.</summary>
        internal static void Forget()
        {
            while (finished.TryDequeue(out _)) { }
        }
    }
}
