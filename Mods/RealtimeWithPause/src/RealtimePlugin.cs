// An example to start your own plugin from: use, copy and change it for any purpose, with no conditions and no credit needed.

using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using Breachwork;
using HarmonyLib;
using UnityEngine;

namespace RealtimeWithPause
{
    /// <summary>
    /// Realtime with pause. The whole framework counts turns (cooldowns, effects, procs, fields, traps), so turns stay, but
    /// nobody shares one: every AI runs its turns on a timer of its own (<see cref="Together"/>), and the game's turn is
    /// left as yours. Game.PlayerAction, the one door every player action goes through, takes the action as an
    /// <see cref="Order"/> (what was aimed at included). The oldest order goes through as soon as you are ready, one per tick,
    /// and a tick with no order passes as a Wait, so your cooldowns run while you stand still. Outside a fight nothing is held back.
    /// </summary>
    public class RealtimePlugin : BreachworkPlugin
    {
        private const float MinTick = 0.1f, MaxTick = 10f;

        internal ConfigEntry<bool> Enabled, PauseWhileAiming, PauseInMenus;
        internal ConfigEntry<float> TickSeconds;
        internal ConfigEntry<KeyCode> PauseKey;

        private readonly List<Order> orders = new List<Order>();
        private float sinceTick, sinceAction;
        private int lastOrderFrame = -1;
        private bool clockActing;
        private Player seenPlayer;
        private Map seenMap;
        private Overlay overlay;

        // ---- for scripts, other plugins and the self-test

        public static RealtimePlugin Current { get; private set; }
        public static bool Paused { get; private set; }
        /// <summary>Ticks the clock has played this session.</summary>
        public static int Ticks { get; private set; }
        /// <summary>Orders that couldn't be carried out by the time their tick came, this session.</summary>
        public static int Dropped { get; private set; }
        public static int OrdersWaiting => Current == null ? 0 : Current.orders.Count;
        public static void SetPaused(bool on) => Current?.Pause(on);
        public static void ClearOrders() => Current?.orders.Clear();
        /// <summary>Your action or any AI's turn is still playing out.</summary>
        public static bool Busy => Game.RESOLVING || Together.Busy;
        /// <summary>Any AI is in the middle of its turn.</summary>
        public static bool EnemiesActing => Together.Busy;
        /// <summary>Whether the clock is running the game right now (it may still be paused).</summary>
        public static bool ClockOn => Current != null && Current.Enabled.Value && Current.InFight;

        private float Tick => Mathf.Clamp(TickSeconds.Value, MinTick, MaxTick);
        private bool InFight => Player.instance != null && !Game.STASIS && LevelManager.instance != null && (LevelManager.instance.IsInCombat() || orders.Count > 0);

        public override void OnLoad()
        {
            Current = this;
            Enabled = Context.Bind("Enabled", true, "Fights run on the clock. Off, they're turn-based again, at once.");
            TickSeconds = Context.Bind("TickSeconds", 1f, "How long a turn is in a fight, in seconds: you, and every enemy and minion on its own timer, act about this often.");
            PauseKey = Context.Bind("PauseKey", KeyCode.P, "Pauses and resumes a fight. What's already swinging finishes, and nothing new starts.");
            PauseWhileAiming = Context.Bind("PauseWhileAiming", true, "The clock waits while you pick a target for a skill. Off, time runs while you aim.");
            PauseInMenus = Context.Bind("PauseInMenus", true, "The clock waits while the inventory, skills or another menu is open. Off, time runs behind them.");
            Context.Harmony.PatchAll(GetType().Assembly);
        }

        public override void OnUpdate()
        {
            Api.SimultaneousTurns = Enabled.Value && Player.instance != null && LevelManager.instance != null && LevelManager.instance.IsInCombat() && !Game.STASIS;
            if (!Enabled.Value)
            {
                Reset();
                overlay?.Hide();
                return;
            }
            if (Player.instance != seenPlayer || Map.instance != seenMap)
            {
                // a new floor or a loaded save: what was aimed at is gone
                Reset();
                seenPlayer = Player.instance;
                seenMap = Map.instance;
            }
            if (!InFight)
            {
                sinceTick = 0f;
                Paused = false;
                overlay?.Hide();
                return;
            }
            if (Input.GetKeyDown(PauseKey.Value) && !Game.FEEDBACK && !Game.EVENT && !Game.VENDOR) Pause(!Paused);
            bool held = Held();
            Player p = Player.instance;
            bool acting = Game.RESOLVING || p.GetIsDead() || (p.GetMover() != null && p.GetMover().isMoving);
            (overlay ?? (overlay = Overlay.Make())).Show(Paused, Paused && Busy, held, Mathf.Clamp01(sinceAction / Tick), orders.Select(o => o.Name).ToList(), PauseKey.Value);
            if (Paused || held) return;

            Together.Run(Time.deltaTime, Tick);
            sinceTick += Time.deltaTime;
            sinceAction += Time.deltaTime;
            if (acting) return;

            // Instant skills don't take a turn, so they don't wait for one.
            if (orders.Count > 0 && orders[0].Instant && !Game.TARGETING)
            {
                Order instant = Next();
                if (instant != null) Release(instant);
                return;
            }
            // A stunned or meditating player has no say, and an aim in progress keeps the game's ability slot.
            Order o = sinceAction >= Tick && !p.FLAG_skipTurn && !Game.TARGETING ? Next() : null;
            if (o == null && sinceTick < Tick) return;
            if (o != null) sinceAction = 0f;
            sinceTick = 0f;
            Ticks++;
            Release(o ?? new Order { action = PLAYER_ACTION.Wait });
        }

        private void Pause(bool on)
        {
            if (Paused == on) return;
            Paused = on;
            AudioManager.PlayEvent(on ? "event:/UI/Menu_Show" : "event:/UI/Menu_Hide");
        }

        private void Reset()
        {
            orders.Clear();
            sinceTick = sinceAction = 0f;
            Paused = false;
            Together.Reset();
        }

        /// <summary>Anything the whole fight must wait for: a dialog, a load, the player's own choice to look at a menu.</summary>
        private bool Held()
        {
            Player p = Player.instance;
            if (Game.LOCK || Game.EVENT || Game.VENDOR || Game.FEEDBACK || Game.CREATING) return true;
            if (Game.Game_isLoading || Game.Game_isTransitioning || Game.Game_isQuitting) return true;
            if (p.GetIsDead()) return true;
            if (Time.timeScale <= 0f) return true;
            if (Game.TARGETING && PauseWhileAiming.Value) return true;
            if (Menu_Options.instance != null && Menu_Options.instance.GetIsShowing()) return true;
            if (Menu_MiniMap.instance != null && Menu_MiniMap.instance.mapMode) return true;
            HUD hud = HUD.instance;
            if (hud != null && hud.menu_system != null && hud.menu_system.GetIsShowing()) return true;
            return hud != null && PauseInMenus.Value && hud.openMenus.Count > 0;
        }

        // ---- orders

        /// <summary>The Game.PlayerAction prefix: true when the action became an order and must not run yet.</summary>
        internal bool TakeOrder(PLAYER_ACTION action)
        {
            if (clockActing || !Enabled.Value || !InFight || Game.RESOLVING) return false;
            Player p = Player.instance;
            // Player.TurnBegin asking for its skipped turn: the clock does that on its own beat.
            if (p.FLAG_skipTurn) return true;
            if (action == PLAYER_ACTION.UseAbility && Game.PLAYER_ABILITY != null && Game.PLAYER_ABILITY.isInstant && !Paused && orders.Count == 0) return false;
            // What used to be blocked by the action starting: one click reaching two handlers in one frame.
            if (Time.frameCount == lastOrderFrame) return true;
            lastOrderFrame = Time.frameCount;

            Order o = Order.Take(action);
            int same = orders.FindLastIndex(x => x.SameAs(o));
            // Keys held down ask every frame, and a step planned from here replaces the last one planned from here.
            if (same >= 0) orders[same] = o;
            else if (orders.Count < 6) orders.Add(o);
            return true;
        }

        private Order Next()
        {
            while (orders.Count > 0)
            {
                Order o = orders[0];
                orders.RemoveAt(0);
                string why = o.Restore();
                if (why == null) return o;
                Dropped++;
                Order.Forget();
                Game.LOG($"<color=#FFC24A>Order dropped: {o.Name} ({why}).</color>");
            }
            return null;
        }

        private void Release(Order o)
        {
            clockActing = true;
            try { Game.instance.PlayerAction(o.action); }
            finally { clockActing = false; }
        }

        [HarmonyPatch(typeof(Game), nameof(Game.PlayerAction))]
        private static class Patch_PlayerAction
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(PLAYER_ACTION action) => Current == null || !Current.TakeOrder(action);
        }
    }
}
