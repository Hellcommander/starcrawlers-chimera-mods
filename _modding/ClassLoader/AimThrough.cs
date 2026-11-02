using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// While you aim a skill your own companions don't stop the mouse: you reach the enemy, tile or object behind them.
    /// Unity sends the mouse only to the nearest collider, so their colliders are off for as long as the aim lasts, back
    /// on before the skill resolves. Buffs still land on them, and holding the game's Force key (the one that attacks
    /// neutrals) makes them solid again, to aim at one on purpose.
    /// </summary>
    internal static class AimThrough
    {
        internal static ConfigEntry<bool> Enabled;

        /// <summary>Devtest: the Force key held, in place of the keyboard.</summary>
        internal static bool TestForce;
        /// <summary>Devtest: the skill being aimed counts as a buff.</summary>
        internal static bool TestBuff;

        private static readonly List<Collider> off = new List<Collider>();
        private static Ability judged;
        private static bool buff;

        internal static void Bind(ConfigFile cfg)
        {
            Enabled = cfg.Bind("Orders", "AimThroughMinions", true,
                "While you aim a skill, your companions don't get in the way of the mouse: you target what's behind them. Buffs still land on them, and holding the Force key lets any skill aim at one.");
        }

        /// <summary>Companions whose colliders are off right now (for the tests).</summary>
        internal static int Hidden => off.Count;

        /// <summary>
        /// A skill meant for your own side. A mod skill says so (ModAbility.ForAllies). A vanilla one has no such flag, but
        /// its buffs all name who they take: one that takes a companion and turns down every enemy around is one.
        /// </summary>
        internal static bool IsBuff(Ability a)
        {
            if (a is ModAbility mod) return mod.ForAllies;
            if (a == null || a.GetTargetStyle() == TARGET_STYLE.Floor || a.damageList.Count > 0) return false;
            List<Character> around = AI_Manager.instance.GetActiveCharacters().Where(c => c != null && !c.GetIsDead()).ToList();
            return around.Any(c => MinionMenu.Mine(c) && a.CanUseOn(c)) && !around.Any(c => !MinionMenu.Mine(c) && c != Player.instance && a.CanUseOn(c));
        }

        private static bool ForceHeld()
        {
            if (TestForce || Keybindings.GetInstance().GetButton("Force")) return true;
            try
            {
                string key = Keybindings.ParseConvention(PlayerPrefs.GetString("Mod"));
                return !string.IsNullOrEmpty(key) && Input.GetKey(key);
            }
            catch (System.ArgumentException) { return false; } // a key name Unity doesn't know
        }

        private static void Restore()
        {
            foreach (Collider c in off)
                if (c != null) c.enabled = true;
            off.Clear();
        }

        /// <summary>Once a frame, and at the start of an aim, before the game asks what's under the mouse.</summary>
        internal static void Tick()
        {
            if (Enabled == null || !Enabled.Value || !Game.TARGETING || Game.RESOLVING || Game.PLAYER_ABILITY == null || Game.TARGETSTYLE == TARGET_STYLE.Item)
            {
                judged = null;
                if (off.Count > 0) Restore();
                return;
            }
            if (judged != Game.PLAYER_ABILITY)
            {
                judged = Game.PLAYER_ABILITY;
                buff = IsBuff(judged);
            }
            if (buff || TestBuff || ForceHeld())
            {
                if (off.Count > 0) Restore();
                return;
            }
            foreach (Character c in AI_Manager.instance.GetActiveCharacters())
            {
                if (c == null || !MinionMenu.Mine(c)) continue;
                foreach (Collider col in c.GetComponentsInChildren<Collider>())
                {
                    if (!col.enabled || col.isTrigger) continue;
                    col.enabled = false;
                    off.Add(col);
                }
            }
        }

        [HarmonyPatch(typeof(Player_AbilityControl), nameof(Player_AbilityControl.DoTargetOverlay))]
        private static class Patch_AimStart
        {
            private static void Prefix() => Tick();
        }

        /// <summary>The aim ending, by a cast or a cancel: back before the skill looks for who it hits.</summary>
        [HarmonyPatch(typeof(Game), nameof(Game.SetTargetMode), typeof(Action_Icon), typeof(bool))]
        private static class Patch_AimEnd
        {
            private static void Postfix(bool flag)
            {
                if (flag) return;
                judged = null;
                Restore();
            }
        }
    }
}
