using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// Plays a model's own clips (from a skinned .glb) for what its creature is doing: idle, move while it walks,
    /// attack when it uses an ability, hit when it takes damage, death when it dies (and it stays down). The game's
    /// own Animator keeps driving the hidden original body, so nothing vanilla notices the difference.
    /// </summary>
    public class MobAnimDriver : MonoBehaviour
    {
        public Animation anim;
        public float speed = 1f;
        public string idle, move, attack, hit, death, special;
        /// <summary>Every clip that counts as an attack (attack, attack2, ...) and as an idle; one is picked each time.</summary>
        public string[] attacks = new string[0], idles = new string[0];
        private float nextIdleSwap;
        private Character c;
        private bool hadCharacter;
        private float fallFrom, fallStart;
        private string oneShot;
        private float oneShotUntil;
        private bool dead;

        internal static readonly string[] States = { "idle", "move", "attack", "hit", "death" };

        internal void Bind(Animation a, Dictionary<string, string> overrides, float playSpeed)
        {
            anim = a;
            speed = playSpeed <= 0f ? 1f : playSpeed;
            if (anim == null) return;
            var names = anim.Cast<AnimationState>().Select(s => s.name).ToList();
            string Find(string state, params string[] words)
            {
                if (overrides != null && overrides.TryGetValue(state, out string o) && names.Contains(o)) return o;
                return names.FirstOrDefault(n => words.Any(w => n.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0));
            }
            idle = Find("idle", "idle", "breath", "stand");
            move = Find("move", "move", "walk", "run", "crawl", "hover") ?? idle;
            attack = Find("attack", "attack", "strike", "bite", "lunge", "cast");
            hit = Find("hit", "hit", "hurt", "flinch", "recoil");
            death = Find("death", "death", "die", "dead");
            special = Find("special", "special", "charge", "windup", "roar");
            attacks = names.Where(n => n.StartsWith("attack", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (attacks.Length == 0 && attack != null) attacks = new[] { attack };
            idles = names.Where(n => n.StartsWith("idle", StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        private void Start()
        {
            c = GetComponent<Character>();
            hadCharacter = c != null;
            if (anim != null) foreach (AnimationState s in anim) s.speed = speed;
            Loop(idle);
        }

        private void Loop(string clip)
        {
            if (anim == null || string.IsNullOrEmpty(clip) || anim[clip] == null) return;
            if (!anim.IsPlaying(clip)) anim.CrossFade(clip, 0.2f);
        }

        internal void Play(string state)
        {
            if (anim == null || dead) return;
            string clip = state == "attack" ? (attacks.Length > 0 ? attacks[UnityEngine.Random.Range(0, attacks.Length)] : attack)
                : state == "hit" ? hit : state == "death" ? death : state == "special" ? special
                : anim[state] != null ? state : null; // a clip asked for by name (a creature skill's "clip")
            if (clip == null || anim[clip] == null) return;
            bool busy = oneShot != null && Time.time < oneShotUntil;
            if (state == "hit" && busy && oneShot != hit) return; // don't cut an attack or a wind-up short
            anim.Stop(clip);
            anim.CrossFade(clip, 0.08f);
            oneShot = clip;
            oneShotUntil = Time.time + anim[clip].length / Mathf.Max(0.05f, speed);
        }

        private void Update()
        {
            if (anim == null) return;
            // Die() ends by destroying the Character component itself and leaves the body as a corpse
            if (hadCharacter && (c == null || c.GetIsDead()))
            {
                if (!dead && death != null) { Play("death"); dead = true; fallFrom = anim.transform.localPosition.y; fallStart = Time.time; }
                // a hovering body comes down to the floor as it dies, instead of hanging in the air as a corpse
                if (dead && fallFrom > 0.05f)
                {
                    Vector3 lp = anim.transform.localPosition;
                    float k = Mathf.Clamp01((Time.time - fallStart) / 0.7f);
                    lp.y = Mathf.Lerp(fallFrom, 0.05f, k * k);
                    anim.transform.localPosition = lp;
                }
                return;
            }
            if (dead) // revived (or a clone reset): back on its feet
            {
                dead = false;
                anim.Stop();
            }
            if (oneShot != null && Time.time < oneShotUntil) return;
            oneShot = null;
            bool moving = c != null && c.GetMover() != null && c.GetMover().isMoving;
            if (moving) { Loop(move); return; }
            // now and then a different idle (looking around), then back to the plain one
            if (idles.Length > 1 && Time.time > nextIdleSwap)
            {
                nextIdleSwap = Time.time + UnityEngine.Random.Range(6f, 14f);
                string pick = idles[UnityEngine.Random.Range(0, idles.Length)];
                if (pick != idle && anim[pick] != null)
                {
                    anim.CrossFade(pick, 0.25f);
                    oneShot = pick;
                    oneShotUntil = Time.time + anim[pick].length / Mathf.Max(0.05f, speed);
                    return;
                }
            }
            Loop(idle);
        }

        internal static void Trigger(Character who, string state)
        {
            if (who == null) return;
            MobAnimDriver d = who.GetComponent<MobAnimDriver>();
            if (d != null) d.Play(state);
        }

        /// <summary>Vanilla abilities on a creature with its own clips play its attack.</summary>
        [HarmonyPatch(typeof(Ability), nameof(Ability.AbilityUse))]
        private static class Patch_AbilityUse
        {
            private static void Prefix(Ability __instance)
            {
                if (__instance is ModMobAbility) return; // plays its own, at the right moment
                Character c = __instance.GetComponent<Character>();
                if (c != null && c != Player.instance) Trigger(c, "attack");
            }
        }

        [HarmonyPatch]
        private static class Patch_Damage
        {
            // the overloads that do the work (the short ones pass through these)
            private static IEnumerable<System.Reflection.MethodBase> TargetMethods() => new[]
            {
                AccessTools.Method(typeof(Character), nameof(Character.DamageHealth), new[] { typeof(int), typeof(Ability), typeof(bool) }),
                AccessTools.Method(typeof(Character), nameof(Character.DamageHealth), new[] { typeof(List<DmgObj>), typeof(string), typeof(Ability) }),
            }.Where(m => m != null);

            private static void Postfix(Character __instance)
            {
                if (__instance != null && __instance != Player.instance && !__instance.GetIsDead()) Trigger(__instance, "hit");
            }
        }
    }
}
