using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>"castRules" on an effect: while it is on you, the rules of casting change for every skill you use, any
    /// class's or an item's. A skill rewrite changes what one skill does; these change what using a skill means.</summary>
    public class CastRuleSpec
    {
        /// <summary>Using a skill doesn't take your turn.</summary>
        public bool freeTurn;
        /// <summary>Skills don't go on cooldown.</summary>
        public bool noCooldown;
        /// <summary>Each use costs this share of your max health. It never takes your last point.</summary>
        public float healthCost;
        /// <summary>An attack on an enemy lands a second time, at this share of its damage.</summary>
        public float repeat;
        /// <summary>One stack of the effect is spent by each use that changed.</summary>
        public bool consume;
        /// <summary>Only for the casts this holds on. { "cast": "vanilla" } keeps a rule off mod skills.</summary>
        public WhenSpec when;
    }

    /// <summary>Applies "castRules" around the player's casts. Rules are read before the cast, so a skill that puts one
    /// on you starts with the next cast.</summary>
    internal static class CastRules
    {
        private static Ability lent;
        /// <summary>Repeats landed so far, for the tests.</summary>
        internal static int Repeats;
        /// <summary>The share of its damage a repeat lands, 0 outside one.</summary>
        private static float echo;

        /// <summary>A cast rule is holding this skill's turn: a mod skill works out its own speed while it casts.</summary>
        internal static bool Free(Ability a) => a != null && a == lent;

        private static List<ModEffect> Carrying()
        {
            Player p = Player.instance;
            if (p == null || Player_AbilityControl.instance == null) return new List<ModEffect>();
            return p.effects.effects.OfType<ModEffect>().Where(e => e != null && e.Spec?.castRules != null).ToList();
        }

        internal static string Describe(CastRuleSpec r)
        {
            var bits = new List<string>();
            if (r.freeTurn) bits.Add("Using a skill doesn't take your turn");
            if (r.noCooldown) bits.Add("Skills don't go on cooldown");
            if (r.healthCost > 0f) bits.Add($"Each use costs {Mathf.RoundToInt(r.healthCost * 100)}% of your health");
            if (r.repeat > 0f) bits.Add($"Attacks strike again for {Mathf.RoundToInt(r.repeat * 100)}% of their damage");
            if (r.consume) bits.Add("spent by the cast");
            string when = When.Describe(r.when, null);
            return "<color=#E59CFF>" + string.Join(". ", bits) + (when.Length > 0 ? $" ({when})" : "") + ".</color>";
        }

        private static void Pay(float share)
        {
            Player p = Player.instance;
            int cost = Mathf.Min(Mathf.Max(1, Mathf.RoundToInt(p.GetHealthMax() * share)), p.GetHealth() - 1);
            if (cost <= 0) return;
            p.SetHealth(p.GetHealth() - cost);
            if (HUD.instance != null) HUD.instance.MakeFct("<color=#B58CFF>-" + cost + "</color>", p.GetHitFx().position, false);
        }

        /// <summary>A skill of yours is going off right now. Game.PLAYER_ABILITY stays set between casts, so it can't say.</summary>
        internal static bool InCast { get; private set; }
        private static bool readied;

        /// <summary>"ready": the skill going off now is ready again when it ends, and doesn't end your turn.</summary>
        internal static void Ready() { if (InCast) readied = true; }

        private static IEnumerator Marked(IEnumerator cast)
        {
            InCast = true;
            readied = false;
            try { yield return cast; }
            finally { InCast = false; }
        }

        private static IEnumerator Around(IEnumerator cast)
        {
            Ability a = Game.PLAYER_ABILITY;
            if (a == null || a.isPassive || Game.PLAYER_ITEM != null || Game.PLAYER_ITEM_ABILITY != null) { yield return cast; yield break; }
            List<ModEffect> carried = Carrying();
            Character aim = Game.TARGET_C;
            NavNode tile = Game.TARGET_N;
            bool attack = aim != null && aim != Player.instance && !TileStacks.IsAlly(aim);
            var cx = new ActionContext { caster = Player.instance, target = aim, source = a };
            var used = carried.Where(e => (attack || e.Spec.castRules.repeat <= 0f) && When.Check(e.Spec.castRules.when, cx, aim)).ToList();
            if (used.Any(e => e.Spec.castRules.freeTurn) && !a.isInstant)
            {
                a.isInstant = true;
                lent = a;
            }
            yield return cast;
            if (readied || used.Any(e => e.Spec.castRules.noCooldown))
            {
                a.SetCooldownAmount(0);
                Menu_HUD.instance.Refresh_Cooldown();
            }
            if (readied)
            {
                readied = false;
                if (!a.isInstant) { a.isInstant = true; lent = a; }
                Game.LOG($"<color=#FFD15C>{a.GetDisplayName()} is ready again!</color>");
            }
            foreach (ModEffect e in used)
            {
                CastRuleSpec r = e.Spec.castRules;
                if (r.healthCost > 0f) Pay(r.healthCost);
                if (r.repeat > 0f && aim != null && !aim.GetIsDead())
                {
                    Game.LOG($"<color=#9FB7C9>{e.GetName()} strikes as well.</color>");
                    Game.TARGET_C = aim;
                    Game.TARGET_N = tile;
                    Game.PLAYER_ABILITY = a;
                    echo = r.repeat;
                    Repeats++;
                    int cooldown = a.GetCooldownAmount();
                    yield return a.StartCoroutine("AbilityUse");
                    a.SetCooldownAmount(cooldown);
                    echo = 0f;
                }
                if (r.consume)
                {
                    string nm = e.GetName();
                    e.RemoveStacks(1);
                    Game.LOG($"<color=#9FB7C9>{nm} is spent.</color>");
                }
            }
        }

        private static IEnumerator Restoring(IEnumerator action)
        {
            yield return action;
            if (lent != null) lent.isInstant = false;
            lent = null;
        }

        [HarmonyPatch(typeof(Game), nameof(Game.Resolve_UseAbility))]
        private static class Patch_Cast
        {
            private static void Postfix(ref IEnumerator __result) => __result = Marked(Around(__result));
        }

        // the game reads isInstant again after the cast, to decide the turn
        [HarmonyPatch(typeof(Game), nameof(Game.iPlayerAction))]
        private static class Patch_Action
        {
            private static void Postfix(ref IEnumerator __result) => __result = Restoring(__result);
        }

        [HarmonyPatch(typeof(Character), nameof(Character.TakeDamage), new[] { typeof(Character), typeof(List<DmgObj>), typeof(string), typeof(Ability), typeof(float) })]
        private static class Patch_Echo
        {
            private static void Prefix(ref List<DmgObj> dmgList)
            {
                if (echo <= 0f || dmgList == null) return;
                float share = echo;
                dmgList = dmgList.Where(d => d != null).Select(d => new DmgObj(d.dmgType, Mathf.Max(1, Mathf.RoundToInt(d.damage * share))) { damageMax = Mathf.Max(1, Mathf.RoundToInt(d.damageMax * share)) }).ToList();
            }
        }
    }
}
