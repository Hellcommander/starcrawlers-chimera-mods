using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BepInEx.Configuration;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.LowLevel;

namespace SCClassLoader
{
    /// <summary>
    /// The player's own test of how many job workers their computer runs best with, since the best number depends on the CPU and
    /// on what else it is doing. It runs inside their real fights: only frames where a turn is resolving count, and a window can
    /// wait out the player's turn. The saved setting doesn't change until the end, so a crash leaves it as it was. Only job
    /// workers are tried, because they change live and change nothing on screen.
    /// </summary>
    internal static class BusTune
    {
        private const float WindowSeconds = 2f;
        private const int SettleFrames = 10;

        private sealed class WaitMark { }

        internal static ConfigEntry<bool> Wanted;
        private static BusTrial trial;
        private static Process me;
        private static bool counting;
        private static int settle;
        private static long frameFrom, busyTicks, wallTicks, wallFrom;
        private static int frames;
        private static TimeSpan cpu, cpuFrom;

        internal static void Init(ConfigFile cfg)
        {
            Wanted = cfg.Bind("Performance", "TestJobWorkers", false,
                "Needs EventBus on. Turn on, then fight: over about a minute of big fights this tries different JobWorkers numbers, saves the one that costs the main thread least to JobWorkers, and turns itself off. Numbers too close to tell apart go to whichever spreads the load over the most threads. A fight can stutter while a bad number is tried. Best with nothing else working your CPU or graphics card; a small fight shows no difference and leaves JobWorkers alone. Set JobWorkers back to 0 to undo it. GraphicsJobs and IncrementalGC need a restart, so it can't try those.");
        }

        internal static void Tick()
        {
            if (trial == null)
            {
                if (Wanted.Value && EventBus.On && !Game.Game_isLoading && When.InCombat()) Begin();
                return;
            }
            if (!Wanted.Value || !EventBus.On)
            {
                End();
                return;
            }
            if (Game.Game_isLoading || !When.InCombat())
            {
                Count(false);
                frames = 0;
                busyTicks = wallTicks = 0;
                cpu = TimeSpan.Zero;
                trial.Forget();
                Use(trial.Now);
                return;
            }
            bool run = Game.RESOLVING && Application.isFocused;
            if (run && settle > 0)
            {
                settle--;
                run = false;
            }
            Count(run);
            if (Seconds(wallTicks + (counting ? Stopwatch.GetTimestamp() - wallFrom : 0)) < WindowSeconds) return;
            Count(false);
            if (frames > 0) trial.Observe(Seconds(busyTicks) * 1000.0 / frames, cpu.TotalSeconds / Seconds(wallTicks));
            frames = 0;
            busyTicks = wallTicks = 0;
            cpu = TimeSpan.Zero;
            if (trial.Done) Finish();
            else Use(trial.Now);
        }

        private static double Seconds(long ticks) => ticks / (double)Stopwatch.Frequency;

        private static void Use(int workers)
        {
            settle = SettleFrames;
            if (EventBus.Trial == workers) return;
            EventBus.Trial = workers;
            EventBus.ApplyWorkers();
        }

        private static void Count(bool on)
        {
            if (on == counting) return;
            counting = on;
            frameFrom = 0;
            long now = Stopwatch.GetTimestamp();
            if (on)
            {
                wallFrom = now;
                cpuFrom = me.TotalProcessorTime;
                return;
            }
            wallTicks += now - wallFrom;
            cpu += me.TotalProcessorTime - cpuFrom;
        }

        // the wait for the last frame to reach the screen isn't work: a busy graphics card or vsync would read as main-thread cost
        private static void FrameBegins() => frameFrom = Stopwatch.GetTimestamp();

        private static void FrameEnds()
        {
            if (!counting || frameFrom == 0) return;
            busyTicks += Stopwatch.GetTimestamp() - frameFrom;
            frames++;
        }

        private static void Hook(bool on)
        {
            PlayerLoopSystem root = PlayerLoop.GetCurrentPlayerLoop();
            PlayerLoopSystem[] top = root.subSystemList;
            for (int t = 0; t < top.Length; t++)
            {
                if (top[t].subSystemList == null) continue;
                var steps = new List<PlayerLoopSystem>();
                foreach (PlayerLoopSystem step in top[t].subSystemList)
                {
                    if (step.type == typeof(WaitMark)) continue;
                    bool wait = on && step.type == typeof(UnityEngine.PlayerLoop.TimeUpdate.WaitForLastPresentationAndUpdateTime);
                    if (wait) steps.Add(new PlayerLoopSystem { type = typeof(WaitMark), updateDelegate = FrameEnds });
                    steps.Add(step);
                    if (wait) steps.Add(new PlayerLoopSystem { type = typeof(WaitMark), updateDelegate = FrameBegins });
                }
                top[t].subSystemList = steps.ToArray();
            }
            root.subSystemList = top;
            PlayerLoop.SetPlayerLoop(root);
        }

        private static void Begin()
        {
            int home = EventBus.SettledWorkers;
            trial = new BusTrial(home, BusTrial.Ladder(Mathf.Min(EventBus.GameWorkers, JobsUtility.JobWorkerMaximumCount)));
            me = Process.GetCurrentProcess();
            frames = 0;
            busyTicks = wallTicks = 0;
            cpu = TimeSpan.Zero;
            Hook(true);
            Use(trial.Now);
            Plugin.Log.LogInfo($"Worker test: starting at {home} job workers, trying {string.Join(", ", trial.Counts.Skip(1))}");
        }

        private static void Finish()
        {
            int home = trial.Home, best = trial.Best;
            foreach (int n in trial.Counts.OrderBy(n => n))
                Plugin.Log.LogInfo($"Worker test: {n} job workers, {trial.Score(n) * 100:0}% of the main-thread time at {home}, {trial.Threads(n):0.0} threads busy");
            Plugin.Log.LogInfo($"Worker test: the fight wandered {(trial.Wander - 1) * 100:0.0}% by itself");
            bool uneven = trial.Uneven;
            double score = trial.Score(best);
            End();
            if (uneven)
            {
                Game.LOG($"<color=#7FE0C8>Worker test:</color> this fight was too uneven to tell the numbers apart, so job workers stay at {home}.");
                return;
            }
            if (best == home)
            {
                Game.LOG($"<color=#7FE0C8>Worker test:</color> {home} job workers was already the best number here.");
                return;
            }
            EventBus.JobWorkers.Value = best;
            Game.LOG($"<color=#7FE0C8>Worker test:</color> job workers set to {best}, {(1 - score) * 100:0}% less main-thread time than {home}.");
        }

        /// <summary>Puts the worker number back, whether the test finished or was stopped.</summary>
        private static void End()
        {
            Count(false);
            Hook(false);
            trial = null;
            me = null;
            EventBus.Trial = 0;
            EventBus.ApplyWorkers();
            Wanted.Value = false;
        }
    }
}
