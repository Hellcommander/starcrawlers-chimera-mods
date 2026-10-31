using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// Dashes in third person. While a skill carries you across the floor (Dash, Lunge, a charge, a vault, a
    /// backflip: every player move with its own speed), the camera swings out behind and above you so the move reads,
    /// then glides back into your eyes once you land. You show as the Nemesis crawler of your class (the game's own
    /// humanoid rivals that share the player trees), running; your first-person hands hide meanwhile. The camera
    /// stays on the line of the move and never further back than the open floor behind you, so it can't end up in a
    /// wall. Such moves are drawn a little slower (still well under half a second) so there is something to see; what
    /// they do in the game happens at once as before.
    ///
    /// The loader's own moves put you on the new tile at once ("leap": a pounce, a charge, a zip line; "warp" on
    /// yourself: a blink). For those the shot replays the move: a hop from where you stood to where you landed, or a
    /// blink out and back in, while the game already has you on the landing tile.
    ///
    /// An ability with "dashAs" (a creature id) turns you into that creature for the move instead: magic that becomes
    /// its own light. You burst into it (its deathVfx), it streaks through the target and on to your landing tile with
    /// its glow and particles, and bursts back into you once you land.
    ///
    /// A skill or effect with "thirdPerson": N (seconds) is drawn the same way without moving you: the camera pulls out
    /// over your shoulder for N seconds while it plays, so an effect on your own body (a crown of lightning, wings
    /// tearing off, a shockwave around you) can be seen, then glides back. [Camera] SkillThirdPerson turns it off.
    /// </summary>
    internal static class DashCam
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> SkillShots;

        private const float In = 0.2f, Hold = 0.2f, Out = 0.32f, MinMove = 0.34f;
        private const float Back = 1.9f, Height = 1.55f, LookAhead = 0.9f, LookHeight = 0.75f;

        internal static void Bind(ConfigFile cfg)
        {
            Enabled = cfg.Bind("Camera", "DashThirdPerson", true,
                "While a skill dashes, lunges, charges or vaults you across the floor, the camera pulls out behind you (third person) and returns when you land.");
            SkillShots = cfg.Bind("Camera", "SkillThirdPerson", true,
                "Skills and effects made to be seen on your own body (they say \"thirdPerson\" in their files) pull the camera out over your shoulder while they play.");
        }

        /// <summary>Tests: how far the camera is out right now (0 = your eyes, 1 = fully behind you).</summary>
        internal static float Blend => runner != null ? runner.blend : 0f;
        internal static bool BodyShown => runner != null && runner.body != null && runner.body.activeSelf;
        /// <summary>Tests: the creature you are shown as right now ("Hoarlight"), or null for your crawler.</summary>
        internal static string Form => runner != null && runner.body != null && runner.body.activeSelf ? runner.form : null;
        internal static int Shots { get; private set; }

        private static Runner runner;

        /// <summary>The crawler body of your class, as the game keeps it.</summary>
        internal static Character CrawlerPrefab() => Runner.Prefab(Runner.BodyId());

        private static bool CanShoot =>
            Enabled != null && Enabled.Value && Player_Control.instance != null && Player_Control.instance.playerCamera != null
            && Player.instance != null && !Player.instance.GetIsDead() && Player.instance.GetMover() != null;

        /// <summary>
        /// The player was just put from `from` onto `to` in one go (Moves' leap and warp). blink: a warp (gone, then there);
        /// otherwise a leap, drawn as a quick hop along the floor. form: a creature id to become for the move ("dashAs"),
        /// which flies through `via` (the target) on its way.
        /// </summary>
        internal static void Teleported(Vector3 from, Vector3 to, bool blink, string form = null, Vector3? via = null)
        {
            if (!CanShoot || (to - from).sqrMagnitude < 0.25f) return;
            if (!string.IsNullOrEmpty(form) && WispSpec(form, out _, out _) == null)
            {
                Plugin.Log.LogWarning($"dashAs \"{form}\": no creature with a model by that id; showing your crawler");
                form = null;
            }
            if (runner == null) runner = new GameObject("SCCL_DashCam").AddComponent<Runner>();
            runner.Replay(Player.instance.GetMover(), from, to, blink, string.IsNullOrEmpty(form) ? null : form, via);
            Shots++;
        }

        /// <summary>Over your shoulder for that many seconds while a skill or effect plays on you ("thirdPerson").</summary>
        internal static void Showcase(float seconds)
        {
            if (seconds <= 0f || SkillShots == null || !SkillShots.Value || Player_Control.instance == null || Player_Control.instance.playerCamera == null
                || Player.instance == null || Player.instance.GetIsDead() || Player.instance.GetMover() == null) return;
            if (runner == null) runner = new GameObject("SCCL_DashCam").AddComponent<Runner>();
            runner.HoldOn(Player.instance.GetMover(), seconds);
            Shots++;
        }

        [HarmonyPatch(typeof(Character_Mover), nameof(Character_Mover.PlayerMove))]
        private static class PlayerMovePatch
        {
            private static void Prefix(Character_Mover __instance)
            {
                if (!CanShoot || !__instance.isPlayer || __instance.moveSpeedOverride <= 0f) return;
                if (__instance.playerStartNode == null || __instance.playerTargetNode == null || __instance.playerStartNode == __instance.playerTargetNode) return;
                // The whole slide in MinMove seconds or more (a vanilla Dash is 1/7 s).
                __instance.moveSpeedOverride = Mathf.Min(__instance.moveSpeedOverride, 1f / MinMove);
                if (runner == null) runner = new GameObject("SCCL_DashCam").AddComponent<Runner>();
                runner.Begin(__instance, __instance.playerStartNode, __instance.playerTargetNode);
                Shots++;
            }
        }

        /// <summary>A creature's model spec and the folder its files are in, or null.</summary>
        private static ModelSpec WispSpec(string id, out string folder, out EnemyDef def)
        {
            folder = null;
            def = EnemyRegistry.DefOf(EnemyRegistry.Creature(id) ?? EnemyRegistry.Resolve(id));
            if (def?.model == null) return null;
            folder = System.IO.Path.GetDirectoryName(def.file) ?? def.pack?.folder;
            return def.model;
        }

        private class Runner : MonoBehaviour
        {
            internal float blend;
            internal GameObject body;
            private Character_Mover mover;
            private Vector3 dir, from, to;
            private float behindStart, landedAt = -1f;
            private bool active;
            // a replayed move: the game already has you at `to`; the shot draws you from `from` over replayTime seconds
            private bool replay, blink;
            private float replayStart, replayTime;
            // becoming a creature for the move: its id, and the point it flies through (the target) on the way
            internal string form;
            private Vector3 via;
            private bool hasVia;
            private const float FormScale = 1.8f, FormHeight = 0.95f;
            private Vector3 fpLocalPos;
            private Quaternion fpLocalRot;
            private Animator bodyAnim;
            private string bodyId;
            private readonly List<Renderer> hidden = new List<Renderer>();
            private bool handsHidden;

            private void Awake() => DontDestroyOnLoad(gameObject);

            // a showcase: no move, the shot holds until showUntil
            private bool showcase;
            private float showUntil;

            internal void HoldOn(Character_Mover m, float seconds)
            {
                // a move already being shot keeps its shot, just held a little longer
                if (active && !showcase) { showUntil = Time.time + seconds; return; }
                replay = false; showcase = true;
                form = null; hasVia = false;
                showUntil = Mathf.Max(showUntil, Time.time + seconds);
                Vector3 at = m.transform.position;
                Transform cam = Player_Control.instance.playerCamera.transform;
                Aim(m, m.GetCurrentNavNode(), at, at + Flat(cam.forward, m.transform.forward));
            }

            internal void Begin(Character_Mover m, NavNode start, NavNode target)
            {
                replay = false; showcase = false;
                form = null; hasVia = false;
                Aim(m, start, start.transform.position, target.transform.position);
            }

            internal void Replay(Character_Mover m, Vector3 a, Vector3 b, bool isBlink, string becomes, Vector3? through)
            {
                replay = true; showcase = false;
                form = becomes;
                // a creature flies (no blink, no hop), through the target when there is one
                blink = isBlink && form == null;
                hasVia = form != null && through.HasValue && (through.Value - a).sqrMagnitude > 0.25f;
                via = hasVia ? new Vector3(through.Value.x, a.y, through.Value.z) : b;
                float length = hasVia ? Vector3.Distance(a, via) + Vector3.Distance(via, b) : Vector3.Distance(a, b);
                replayStart = Time.time;
                replayTime = Mathf.Clamp(0.1f * length, MinMove * 0.75f, form != null ? 0.6f : 0.5f);
                Aim(m, Map.instance != null ? Map.instance.GetNearestNavNodeAt(a, 0.6f) : null, a, hasVia ? via : b);
                to = b;
            }

            /// <summary>Where a replayed move has you at progress p (0..1): along from-via-to by distance, eased.</summary>
            private Vector3 Along(float p)
            {
                float e = Mathf.SmoothStep(0f, 1f, p);
                if (!hasVia) return Vector3.Lerp(from, to, e);
                float l1 = Vector3.Distance(from, via), l2 = Vector3.Distance(via, to), d = e * (l1 + l2);
                return d <= l1 ? Vector3.Lerp(from, via, l1 > 0f ? d / l1 : 1f) : Vector3.Lerp(via, to, l2 > 0f ? (d - l1) / l2 : 1f);
            }

            private void Aim(Character_Mover m, NavNode start, Vector3 a, Vector3 b)
            {
                mover = m;
                Vector3 d = b - a;
                d.y = 0f;
                if (d.sqrMagnitude < 0.01f) d = m.transform.forward;
                dir = d.normalized;
                from = a;
                to = b;
                behindStart = start != null ? OpenBehind(start, dir) : 0.4f;
                landedAt = -1f;
                if (body != null && bodyId != (form ?? BodyId())) { Destroy(body); body = null; }
                Camera cam = Player_Control.instance.playerCamera;
                if (!active)
                {
                    fpLocalPos = cam.transform.localPosition;
                    fpLocalRot = cam.transform.localRotation;
                    active = true;
                }
            }

            /// <summary>How far the camera may sit behind `start` along -dir: the open tiles there, plus most of the tile itself.</summary>
            private static float OpenBehind(NavNode start, Vector3 dir)
            {
                float open = 0.4f;
                NavNode n = start;
                for (int i = 0; i < 2; i++)
                {
                    NavNode b = Diagonal.Link(n, -dir);
                    if (b == null || b.Block_Move || b.BlockCheck() || !n.GetLinks().Contains(b) || (n.HasDoor() && !n.GetDoor().isOpen)) break;
                    open += 1f;
                    n = b;
                }
                return open;
            }

            private void LateUpdate()
            {
                if (!active) return;
                Player_Control pc = Player_Control.instance;
                Camera cam = pc != null ? pc.playerCamera : null;
                if (cam == null || mover == null || Player.instance == null || Player.instance.GetIsDead()) { End(); return; }

                float p = replay ? Mathf.Clamp01((Time.time - replayStart) / replayTime) : 1f;
                bool moving = showcase ? false : replay ? p < 1f : mover.isMoving;
                if (showcase) { if (Time.time < showUntil) landedAt = -1f; else if (landedAt < 0f) landedAt = Time.time - Hold; }
                else if (moving) landedAt = -1f;
                else if (landedAt < 0f) landedAt = Time.time;
                bool leaving = landedAt >= 0f && Time.time - landedAt > Hold;
                blend = Mathf.Clamp01(blend + Time.deltaTime / (leaving ? -Out : In));

                Vector3 real = mover.transform.position;
                Vector3 at = !replay ? real
                    : blink ? (p < 0.5f ? from : to)
                    : form != null ? Along(p)
                    : Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, p)) + Vector3.up * (0.3f * Mathf.Sin(Mathf.PI * p));
                // Never further back than open floor: the path already covered plus what lies behind the start.
                float travelled = Vector3.Dot(at - from, dir);
                float back = Mathf.Min(Back, Mathf.Max(0f, travelled) + behindStart);
                Vector3 tpPos = at - dir * back + Vector3.up * Height;
                Vector3 look = at + dir * LookAhead + Vector3.up * LookHeight;
                Quaternion tpRot = Quaternion.LookRotation(look - tpPos);

                Transform parent = cam.transform.parent;
                // your eyes where the shot has you (a replay starts them back where you stood)
                Vector3 fpPos = (parent != null ? parent.TransformPoint(fpLocalPos) : fpLocalPos) + (at - real);
                Quaternion fpRot = parent != null ? parent.rotation * fpLocalRot : fpLocalRot;
                float k = Mathf.SmoothStep(0f, 1f, blend);
                cam.transform.position = Vector3.Lerp(fpPos, tpPos, k);
                cam.transform.rotation = Quaternion.Slerp(fpRot, tpRot, k);

                // a blink: gone for the middle of the move
                ShowBody(k > 0.3f, !(replay && blink && p > 0.3f && p < 0.7f), at, moving);
                if (leaving && blend <= 0f) { showcase = false; showUntil = 0f; End(); }
            }

            private void ShowBody(bool shot, bool visible, Vector3 at, bool moving)
            {
                if (!shot)
                {
                    if (body != null && body.activeSelf && form != null) Burst(body.transform.position);
                    if (body != null) body.SetActive(false);
                    ShowHands();
                    return;
                }
                if (!handsHidden) HideHands();
                if (body == null) { if (form != null) MakeForm(); else MakeBody(); }
                if (body == null) return;
                Vector3 place = form != null ? at + Vector3.up * (FormHeight - formLift) : at;
                if (form != null && visible != body.activeSelf) Burst(visible ? place : body.transform.position);
                body.SetActive(visible);
                body.transform.position = place;
                // down the move while moving, then turning to face where you face
                Quaternion face = moving ? Quaternion.LookRotation(dir) : Quaternion.LookRotation(Flat(mover.transform.forward, dir));
                body.transform.rotation = Quaternion.Slerp(body.transform.rotation, face, moving ? 1f : 10f * Time.deltaTime);
                if (bodyAnim != null)
                {
                    Set(bodyAnim, "inCombat", true);
                    Set(bodyAnim, "isMoving", moving);
                }
            }

            private float formLift;
            private string formBurst;

            /// <summary>The creature's own burst (its deathVfx) where you turn into it and back (`at` is the form's root).</summary>
            private void Burst(Vector3 at)
            {
                if (!string.IsNullOrEmpty(formBurst)) Actions.SpawnVfx(formBurst, at + Vector3.up * formLift);
            }

            /// <summary>You as the creature: its model, glow and live particles on a bare object (nothing that thinks or collides).</summary>
            private void MakeForm()
            {
                ModelSpec spec = WispSpec(form, out string folder, out _);
                if (spec == null) return;
                var g = new GameObject("SCCL_DashForm_" + form);
                g.SetActive(false);
                if (!ModelAssets.Apply(g, spec, folder)) { Destroy(g); return; }
                // Larger than the summon (it is all of you), centred at chest height.
                g.transform.localScale = Vector3.one * FormScale;
                formLift = (spec.offset != null && spec.offset.Length >= 2 ? spec.offset[1] : 1f) * FormScale;
                formBurst = spec.deathVfx;
                body = g;
                bodyId = form;
                bodyAnim = null;
                DontDestroyOnLoad(g);
            }

            private static Vector3 Flat(Vector3 v, Vector3 fallback)
            {
                v.y = 0f;
                return v.sqrMagnitude < 0.01f ? fallback : v.normalized;
            }

            private static void Set(Animator a, string p, bool v)
            {
                foreach (AnimatorControllerParameter q in a.parameters)
                    if (q.name == p && q.type == AnimatorControllerParameterType.Bool) { a.SetBool(p, v); return; }
            }

            private void End()
            {
                Camera cam = Player_Control.instance != null ? Player_Control.instance.playerCamera : null;
                if (cam != null && active)
                {
                    cam.transform.localPosition = fpLocalPos;
                    cam.transform.localRotation = fpLocalRot;
                }
                if (body != null) body.SetActive(false);
                ShowHands();
                active = false;
                blend = 0f;
            }

            /// <summary>The first-person hands and weapons: hidden while you see yourself from behind.</summary>
            private void HideHands()
            {
                handsHidden = true;
                hidden.Clear();
                foreach (Renderer r in Player_Control.instance.GetComponentsInChildren<Renderer>())
                    if (r.enabled && (r is MeshRenderer || r is SkinnedMeshRenderer)) { r.enabled = false; hidden.Add(r); }
            }

            private void ShowHands()
            {
                foreach (Renderer r in hidden) if (r != null) r.enabled = true;
                hidden.Clear();
                handsHidden = false;
            }

            // ---- the body you see -----------------------------------------------------------------------------

            /// <summary>The Nemesis crawler of your first tree that has one (Nemesis_Cyberninja for a Cyberninja), else one for its archetype.</summary>
            internal static string BodyId()
            {
                var trees = Player_SkillControl.instance?.skillTrees?.Where(t => t != null && !string.IsNullOrEmpty(t.internalName)).ToList() ?? new List<Skill_Tree>();
                foreach (Skill_Tree t in trees)
                {
                    string id = "Nemesis_" + t.internalName.Replace("SKILLTREE_", "");
                    if (Prefab(id) != null) return id;
                }
                switch (trees.FirstOrDefault(t => t.internalName != Conjoined.ClassId)?.skillClass)
                {
                    case SKILL_CLASS.Guile: return "Nemesis_Smuggler";
                    case SKILL_CLASS.Intellect: return "Nemesis_Engineer";
                    default: return "Nemesis_Soldier";
                }
            }

            internal static Character Prefab(string id) =>
                AI_Manager.instance?.allCharacters?.FirstOrDefault(c => c != null && c.internalName == id);

            /// <summary>A copy of the crawler's looks only: meshes, bones and its Animator, nothing that thinks, collides or makes sound.</summary>
            private void MakeBody()
            {
                if (body != null) Destroy(body);
                bodyId = BodyId();
                Character prefab = Prefab(bodyId);
                if (prefab == null) return;
                // Built under an inactive parent so none of its scripts wake up.
                var hold = new GameObject("SCCL_DashBodyHold");
                hold.SetActive(false);
                GameObject g = Instantiate(prefab.gameObject, hold.transform);
                for (int pass = 0; pass < 3; pass++)
                    foreach (Component c in g.GetComponentsInChildren<Component>(true))
                        if (c is MonoBehaviour || c is Collider || c is Rigidbody || c is Light || c is Canvas)
                            DestroyImmediate(c);
                g.name = "SCCL_DashBody";
                g.SetActive(false);
                g.transform.SetParent(null, false);
                Destroy(hold);
                bodyAnim = g.GetComponentInChildren<Animator>(true);
                if (bodyAnim != null) { bodyAnim.cullingMode = AnimatorCullingMode.AlwaysAnimate; bodyAnim.applyRootMotion = false; }
                body = g;
                DontDestroyOnLoad(g);
            }
        }
    }
}
