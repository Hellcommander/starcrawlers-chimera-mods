using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// Makes combat readable without the music. Vanilla enters combat the moment any enemy targets you (seen or not),
    /// leaves it after a hidden 8-turn countdown, lets every enemy slide at once while you are still stepping, and
    /// hands control back before they've stopped. [Combat] in the config file turns each part off.
    /// <list type="bullet">
    /// <item>A banner when combat starts (naming who spotted you, or saying it's out of sight), when a boss fight
    /// starts and when combat ends.</item>
    /// <item>A badge at the top of the screen during combat: round number, whose turn it is, and the countdown once
    /// nothing can see you.</item>
    /// <item>Being spotted stops a held movement key, so you don't keep walking into the fight.</item>
    /// <item>In combat, enemies wait for your step to land, enemies you can see move one after another, and your
    /// turn starts once everything has stopped.</item>
    /// </list>
    /// </summary>
    internal static class CombatClarity
    {
        internal static ConfigEntry<bool> Banner, Badge, HaltOnSpotted, WaitForMoves, OneAtATime;

        internal static void Bind(ConfigFile cfg)
        {
            Banner = cfg.Bind("Combat", "Banner", true,
                "Show a banner when combat starts (and who spotted you), when a boss fight starts and when combat ends.");
            Badge = cfg.Bind("Combat", "TurnBadge", true,
                "During combat, show the round, whose turn it is and how many turns until combat ends once nothing can see you.");
            HaltOnSpotted = cfg.Bind("Combat", "HaltOnSpotted", true,
                "Being spotted stops a held movement or wait key; press it again to keep going.");
            WaitForMoves = cfg.Bind("Combat", "WaitForMoves", true,
                "In combat, enemies act after your step lands and your turn starts once everyone has stopped moving.");
            OneAtATime = cfg.Bind("Combat", "OneAtATime", true,
                "In combat, enemies you can see move one after another instead of all at once (your own minions and allies don't wait).");
        }

        /// <summary>Enemy phases since combat started (1 during the first).</summary>
        internal static int Round { get; private set; }

        /// <summary>Turns until combat ends, or -1 while something can see you.</summary>
        internal static int Countdown { get; private set; } = -1;

        internal static bool EnemyPhase { get; private set; }

        private static bool haltMovement;
        private static bool bossStarting;
        private static Panel panel;

        private static readonly AccessTools.FieldRef<AI_Manager, List<AI>> AisRef = AccessTools.FieldRefAccess<AI_Manager, List<AI>>("ais");

        internal static bool InCombat => LevelManager.instance != null && LevelManager.instance.inCombat;

        /// <summary>On screen for the player: its room is drawn and part of it is rendered this frame.</summary>
        internal static bool Seen(Character c)
        {
            if (c == null || c.GetIsDead() || !c.gameObject.activeInHierarchy) return false;
            NavNode n = c.GetMover()?.GetCurrentNavNode();
            if (n != null && n.GetRoom() != null && !n.GetRoom().IsShowing()) return false;
            foreach (Renderer r in c.GetComponentsInChildren<Renderer>())
                if (r != null && r.enabled && r.isVisible) return true;
            return false;
        }

        private static string Name(Character c)
        {
            string n = c != null ? c.GetName() : null;
            return string.IsNullOrEmpty(n) ? "An enemy" : System.Text.RegularExpressions.Regex.Replace(n, "<.*?>", "");
        }

        // ---- entering and leaving --------------------------------------------------------------------------------

        [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.EnterBossCombat))]
        private static class Patch_Boss
        {
            private static void Prefix() => bossStarting = true;
            private static void Finalizer() => bossStarting = false;
        }

        [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.EnterCombat))]
        private static class Patch_Enter
        {
            private static void Prefix(LevelManager __instance, out bool __state) => __state = __instance.inCombat;

            private static void Postfix(LevelManager __instance, bool __state)
            {
                if (__state || !__instance.inCombat) return;
                Round = 1;
                Countdown = -1;
                if (Game.Game_isLoading || Player.instance == null) return; // a save made mid-fight: badge only
                haltMovement = HaltOnSpotted.Value;

                // Called from inside the enemy turn, the enemy taking its turn is the one that saw you.
                AI_Manager am = AI_Manager.instance;
                Character spotter = am != null && am.activeCharacter != null && am.activeAI != null && am.activeAI.sawPlayer ? am.activeCharacter : null;
                string title, sub, log;
                if (bossStarting)
                {
                    title = "BOSS FIGHT";
                    sub = "";
                    log = "<color=#FF3F40>Boss fight!</color>";
                }
                else if (spotter == null)
                {
                    title = "COMBAT";
                    sub = "";
                    log = "<color=#FF3F40>Combat!</color>";
                }
                else if (Seen(spotter))
                {
                    title = "COMBAT";
                    sub = $"{Name(spotter)} spotted you";
                    log = $"<color=#FF3F40>Combat!</color> {Name(spotter)} spotted you.";
                }
                else
                {
                    title = "COMBAT";
                    sub = "Something out of sight has spotted you";
                    log = "<color=#FF3F40>Combat!</color> Something you can't see has spotted you.";
                }
                Game.LOG(log);
                if (Banner.Value) panel?.Flash(title, sub, new Color(1f, 0.25f, 0.25f));
            }
        }

        [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.ExitCombat))]
        private static class Patch_Exit
        {
            private static void Prefix(LevelManager __instance, out bool __state) => __state = __instance.inCombat;

            private static void Postfix(LevelManager __instance, bool __state)
            {
                if (!__state || __instance.inCombat) return;
                Countdown = -1;
                EnemyPhase = false;
                if (Game.Game_isLoading || Player.instance == null || Player.instance.GetIsDead()) return;
                Game.LOG("<color=#7FE0FF>Combat over.</color> Nothing is hunting you.");
                if (Banner.Value) panel?.Flash("COMBAT OVER", "", new Color(0.5f, 0.88f, 1f));
            }
        }

        // ---- pacing ----------------------------------------------------------------------------------------------

        private static readonly AccessTools.FieldRef<Game, float> SafetyTimerRef = AccessTools.FieldRefAccess<Game, float>("resolveSafetyTimer");

        /// <summary>
        /// Waits (at most <paramref name="max"/> seconds) while something is busy. The turn's softlock guard aborts a
        /// turn that runs past resolveTimeMax; these bounded waits aren't a softlock, so the guard's clock is held still.
        /// </summary>
        private static IEnumerator WaitWhile(Func<bool> busy, float max)
        {
            float until = Time.time + max;
            float held = Game.instance != null ? SafetyTimerRef(Game.instance) : 0f;
            while (Time.time < until && busy())
            {
                yield return null;
                if (Game.instance != null) SafetyTimerRef(Game.instance) = held;
            }
        }

        private static bool AnySeenMoving()
        {
            Player p = Player.instance;
            if (p != null && p.GetMover() != null && p.GetMover().isMoving) return true;
            AI_Manager am = AI_Manager.instance;
            List<AI> ais = am != null ? AisRef(am) : null;
            if (ais == null) return false;
            foreach (AI ai in ais)
            {
                Character c = ai != null ? ai.GetCharacter() : null;
                if (c != null && c.GetMover() != null && c.GetMover().isMoving && Seen(c)) return true;
            }
            return false;
        }

        /// <summary>
        /// Wraps both enemy phases of a turn. Before the normal phase: let the player's step land. After the normal
        /// phase: count the round and read the countdown. After the fast phase (always the last one): wait until
        /// nothing on screen is moving, so the player's turn starts on a still board.
        /// </summary>
        [HarmonyPatch(typeof(AI_Manager), nameof(AI_Manager.DoAiTurn))]
        private static class Patch_AiTurn
        {
            private static void Postfix(AI_Manager __instance, bool isFast, ref IEnumerator __result) => __result = Wrap(__instance, isFast, __result);

            private static IEnumerator Wrap(AI_Manager am, bool isFast, IEnumerator inner)
            {
                bool fighting = InCombat;
                if (!isFast)
                {
                    EnemyPhase = true;
                    if (fighting && WaitForMoves.Value && !Breachwork.Api.SimultaneousTurns)
                    {
                        Character_Mover pm = Player.instance != null ? Player.instance.GetMover() : null;
                        yield return WaitWhile(() => pm != null && pm.isMoving, 1.5f);
                    }
                }
                while (true)
                {
                    object cur;
                    try
                    {
                        if (!inner.MoveNext()) break;
                        cur = inner.Current;
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogError("Enemy turn: " + e);
                        break;
                    }
                    yield return cur;
                }
                if (!isFast)
                {
                    if (InCombat)
                    {
                        if (fighting) Round++;
                        // Scripted fights (FINALFIGHT) never count down.
                        bool counting = !am.sawPlayerThisTurn && am.outOfCombatTimer > 0 && Player.instance != null && !Player.instance.HasTag("FINALFIGHT");
                        Countdown = counting ? am.outOfCombatTimer : -1;
                    }
                }
                else
                {
                    if (InCombat && WaitForMoves.Value && !Breachwork.Api.SimultaneousTurns) yield return WaitWhile(AnySeenMoving, 1.5f);
                    EnemyPhase = false;
                }
            }
        }

        /// <summary>A visible enemy's step finishes before the next enemy acts.</summary>
        [HarmonyPatch(typeof(AI), nameof(AI.TakeTurn))]
        private static class Patch_TakeTurn
        {
            private static void Postfix(AI __instance, ref IEnumerator __result) => __result = Wrap(__instance, __result);

            private static IEnumerator Wrap(AI ai, IEnumerator inner)
            {
                while (true)
                {
                    object cur;
                    try
                    {
                        if (!inner.MoveNext()) break;
                        cur = inner.Current;
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogError("Enemy step: " + e);
                        break;
                    }
                    yield return cur;
                }
                if (!OneAtATime.Value || !InCombat || Breachwork.Api.SimultaneousTurns) yield break;
                // only enemies: your own minions and allies don't hold up the queue (in the final fight a full
                // Engineer crew waiting on each other was a big part of the wait between your turns)
                if (ai == null || ai.GetTeam() == AI.AI_TEAM.Player) yield break;
                Character c = ai.GetCharacter();
                Character_Mover m = c != null ? c.GetMover() : null;
                if (m == null || !m.isMoving || !Seen(c)) yield break;
                yield return WaitWhile(() => m != null && m.isMoving, 1f);
            }
        }

        // ---- held keys ---------------------------------------------------------------------------------------------

        /// <summary>True while a key that keeps you moving is still held from before you were spotted.</summary>
        private static bool Halted(Player_Control pc)
        {
            if (!haltMovement) return false;
            bool held = false;
            foreach (string k in new[] { pc.MoveForward, pc.MoveBackward, pc.StrafeLeft, pc.StrafeRight, pc.TurnLeft, pc.TurnRight, pc.Wait })
                if (!string.IsNullOrEmpty(k) && SafeGetKey(k)) { held = true; break; }
            Keybindings kb = Keybindings.GetInstance();
            if (!held && kb != null && pc.CONTROLLERMODE)
            {
                held = kb.GetButton("Wait") || kb.GetButton("TurnLeft") || kb.GetButton("TurnRight");
                if (!held) held = Mathf.Abs(Axis("Vertical")) >= 0.5f || Mathf.Abs(Axis("Horizontal")) >= 0.5f;
            }
            if (!held) haltMovement = false;
            return held;
        }

        /// <summary>Keybindings.input is a Rewired player; read it by reflection so the plugin needs no Rewired reference.</summary>
        private static float Axis(string name)
        {
            try
            {
                Traverse input = Traverse.Create(typeof(Keybindings)).Field("input");
                return input.GetValue() == null ? 0f : input.Method("GetAxis", new[] { typeof(string) }).GetValue<float>(name);
            }
            catch (Exception) { return 0f; }
        }

        private static bool SafeGetKey(string k)
        {
            try { return Input.GetKey(k); }
            catch (ArgumentException) { return false; }
        }

        // First, so later prefixes (Diagonal) see __runOriginal == false while a key is still held.
        [HarmonyPatch(typeof(Player_Control), "DoKeyboardMovement")]
        private static class Patch_Keyboard
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(Player_Control __instance) => !Halted(__instance);
        }

        [HarmonyPatch(typeof(Player_Control), "DoControllerMovement")]
        private static class Patch_Controller
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(Player_Control __instance) => !Halted(__instance);
        }

        [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.ClearAllLevelData))]
        private static class Patch_NewRun
        {
            private static void Postfix()
            {
                Round = 0;
                Countdown = -1;
                EnemyPhase = false;
                haltMovement = false;
            }
        }

        // ---- on screen -----------------------------------------------------------------------------------------------

        internal class Panel : MonoBehaviour
        {
            public TMP_FontAsset font;
            private RectTransform badge, banner;
            private TextMeshProUGUI badgeText, bannerTitle, bannerSub;
            private CanvasGroup bannerGroup;
            private float bannerStart = -100f;
            private const float BannerHold = 1.6f, BannerFade = 0.6f;
            private string shown;

            internal void Build()
            {
                var rt = (RectTransform)transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.anchoredPosition = new Vector2(0f, -12f);
                rt.sizeDelta = new Vector2(700f, 200f);

                badge = UiKit.Panel(transform, "Badge", new Vector2(380f, 54f), new Color(0.03f, 0.06f, 0.09f, 0.78f));
                badge.anchorMin = badge.anchorMax = badge.pivot = new Vector2(0.5f, 1f);
                badge.anchoredPosition = Vector2.zero;
                badge.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
                badgeText = UiKit.Label(badge, "", font, 20f);
                badgeText.enableWordWrapping = false;
                badgeText.overflowMode = TextOverflowModes.Overflow;

                banner = UiKit.Rect(transform, "Banner", new Vector2(700f, 110f));
                banner.anchorMin = banner.anchorMax = banner.pivot = new Vector2(0.5f, 1f);
                banner.anchoredPosition = new Vector2(0f, -70f);
                bannerGroup = banner.gameObject.AddComponent<CanvasGroup>();
                bannerGroup.alpha = 0f;
                bannerGroup.blocksRaycasts = false;
                bannerGroup.interactable = false;
                // A dark strip behind the text so it reads over bright signage.
                var strip = banner.gameObject.AddComponent<UnityEngine.UI.Image>();
                strip.color = new Color(0.02f, 0.03f, 0.05f, 0.85f);
                strip.raycastTarget = false;
                bannerTitle = UiKit.Label(banner, "", font, 54f);
                ((RectTransform)bannerTitle.transform).offsetMin = new Vector2(8f, 36f);
                bannerTitle.fontStyle = FontStyles.Bold;
                bannerTitle.outlineWidth = 0.2f;
                bannerTitle.outlineColor = new Color32(0, 0, 0, 220);
                bannerSub = UiKit.Label(banner, "", font, 28f);
                ((RectTransform)bannerSub.transform).offsetMax = new Vector2(-8f, -74f);
                bannerSub.color = Color.white;
                badge.gameObject.SetActive(false);
            }

            internal void Flash(string title, string sub, Color color)
            {
                bannerTitle.text = title;
                bannerTitle.color = color;
                bannerSub.text = sub;
                bannerStart = Time.unscaledTime;
                bannerGroup.alpha = 1f;
            }

            private void Update()
            {
                float t = Time.unscaledTime - bannerStart;
                bannerGroup.alpha = t < BannerHold ? 1f : Mathf.Clamp01(1f - (t - BannerHold) / BannerFade);

                Player p = Player.instance;
                bool show = Badge.Value && InCombat && p != null && !p.GetIsDead() && !Game.Game_isLoading && !Game.STASIS && !Game.CREATING;
                if (badge.gameObject.activeSelf != show) badge.gameObject.SetActive(show);
                if (!show) return;

                string turn = EnemyPhase || Game.RESOLVING
                    ? "<color=#FFB060>Enemies acting...</color>"
                    : "<color=#9FE870>Your turn</color>";
                string line2 = Countdown >= 0
                    ? $"{turn}  <color=#9FB7C9>· out of sight, ends in {Countdown} turn{(Countdown == 1 ? "" : "s")}</color>"
                    : turn;
                string text = $"<color=#FF5A5A><b>COMBAT</b></color>  <color=#C9D6E0>Round {Math.Max(1, Round)}</color>\n<size=85%>{line2}</size>";
                if (text != shown) { badgeText.text = text; shown = text; }
            }
        }

        [HarmonyPatch(typeof(Menu_HUD), nameof(Menu_HUD.Awake))]
        private static class Patch_HudAwake
        {
            private static void Postfix(Menu_HUD __instance)
            {
                try
                {
                    Canvas canvas = __instance.GetComponentInParent<Canvas>();
                    Transform root = canvas != null ? canvas.rootCanvas.transform : __instance.transform;
                    RectTransform rt = UiKit.Rect(root, "SCCL_Combat", Vector2.zero);
                    rt.SetAsLastSibling();
                    panel = rt.gameObject.AddComponent<Panel>();
                    panel.font = UiKit.FontFrom(__instance);
                    panel.Build();
                    UiLook.Attach(rt, "CombatBadge");
                }
                catch (Exception e) { Plugin.Log.LogError("Combat badge: " + e); }
            }
        }
    }
}
