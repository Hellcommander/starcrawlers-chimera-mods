using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// Physical moves: throwing someone into whatever is behind them, leaping to a target, and picking thrown things
    /// (knives, coins: ground fields with a name) back up to slice through the path or bounce into the nearest enemy.
    /// </summary>
    internal static class Moves
    {
        private static bool Alive(Character c) => c != null && !c.GetIsDead();

        private static bool Movable(Character t) =>
            Alive(t) && t.GetMover() != null && !t.HasTag("BOSS") && !t.HasTag("NOKNOCKBACK") && !t.FLAG_isRooted;

        private static Character OccupantOf(NavNode n) => n == null ? null : TileStacks.On(n).FirstOrDefault(c => Alive(c));

        /// <summary>
        /// "throw": the target flies up to `amount` tiles straight away from you. If it slams into someone, both take the
        /// action's "actions" (impact); if it slams into a wall, it takes them alone. Landing in a field or hazard sets it off.
        /// </summary>
        internal static void Throw(ActionSpec a, ActionContext ctx, Character t)
        {
            if (!Movable(t) || t == ctx.caster || ctx.caster == null) return;
            NavNode start = t.GetMover().GetCurrentNavNode();
            if (start == null) return;
            Vector3 dir = t.transform.position - ctx.caster.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = ctx.caster.transform.forward;
            dir.Normalize();
            NavNode node = start;
            Character hitWho = null;
            bool wall = false;
            for (int i = 0; i < Math.Max(1, a.amount); i++)
            {
                NavNode next = node.GetLinks().Where(l => l != null && Vector3.Dot((l.transform.position - node.transform.position).normalized, dir) > 0.7f)
                    .OrderByDescending(l => Vector3.Dot((l.transform.position - node.transform.position).normalized, dir)).FirstOrDefault();
                if (next == null || next.BlockCheck() || next.Block_Move) { wall = true; break; }
                Character there = OccupantOf(next);
                if (there != null && there != t) { hitWho = there; break; }
                if (!t.GetMover().MoveCheck(node, next)) { wall = true; break; }
                node = next;
            }
            if (node != start)
            {
                if (t.ai != null) t.ai.ForceMove = true;
                t.GetMover().MoveTo(start, node);
            }
            if (t.anim != null) t.Flinch();
            if (hitWho == null && !wall) return;
            Actions.SpawnVfx("PA_Force_Psyker_Kinetic_Slam.impactParticle@0.4", t.GetHitFx().position);
            Sfx("moves_throw_impact", t.transform.position, ctx, 0.8f);
            Game.LOG(hitWho != null ? $"{ctx.sourceName}: {t.GetName()} slams into {hitWho.GetName()}!" : $"{ctx.sourceName}: {t.GetName()} slams into the wall!");
            var impact = a.actions ?? new List<ActionSpec>();
            Actions.Run(impact, ctx, t);
            if (hitWho != null && !TileStacks.IsAlly(hitWho) && hitWho != Player.instance) Actions.Run(impact, ctx, hitWho);
        }

        /// <summary>"leap": you land on the free tile next to the target nearest to you (a hookshot, a pounce).</summary>
        internal static void Leap(ActionSpec a, ActionContext ctx, Character t)
        {
            Character me = ctx.caster;
            if (!Alive(me) || !Alive(t) || me == t || me.GetMover() == null || t.GetMover() == null) return;
            NavNode here = me.GetMover().GetCurrentNavNode(), there = t.GetMover().GetCurrentNavNode();
            if (here == null || there == null || there.GetLinks().Contains(here)) return; // already beside it
            NavNode land = there.GetLinks().Where(l => l != null && !l.IsOccupied() && !l.BlockCheck() && !l.Block_Move && !l.HasDoor())
                .OrderBy(l => (l.transform.position - me.transform.position).sqrMagnitude).FirstOrDefault();
            if (land == null) return;
            Actions.SpawnVfx("CYBERNINJA_DASH.particle_dash", me.GetHitFx().position);
            Streak(me.GetHitFx().position, land.transform.position + Vector3.up * 1.1f, new Color(0.55f, 0.95f, 1f, 0.7f), 0.06f, 0.35f);
            Sfx("moves_leap", me.transform.position, ctx, 0.5f);
            here.ClearOccupant();
            me.GetMover().transform.position = land.transform.position;
            Triggers.Styled("leap", t, () => me.GetMover().SetCurrentNavNode(land));
            land.OnEnterNode(me);
            land.SetOccupant(me);
            me.transform.rotation = Quaternion.LookRotation(new Vector3(t.transform.position.x - land.transform.position.x, 0f, t.transform.position.z - land.transform.position.z));
            if (me == Player.instance)
            {
                Player.CURRENT_ROOM = land.GetRoom();
                land.GetRoom().Discover();
                Map.instance.UpdateOcclusion();
                CullManager.instance.UpdateCulling();
                DashCam.Teleported(here.transform.position, land.transform.position, blink: false, DashAs(ctx), t.transform.position);
            }
        }

        /// <summary>
        /// "warp": a blink through folded space with a flash at both ends. warpTo "behind": you land on the free tile
        /// beside the target farthest from where you stood, facing it (a pounce from nowhere). "random" / "away": the
        /// one the action is aimed at ("to": "self" for you) lands on a random free tile within `amount` (default 3) in
        /// the same room, or the one of those farthest from you. Bosses, rooted and NOKNOCKBACK enemies don't warp.
        /// "vfx" replaces the flash.
        /// </summary>
        internal static void Warp(ActionSpec a, ActionContext ctx, Character t)
        {
            Character me = ctx.caster;
            if (!Alive(t) || !Alive(me)) return;
            string mode = (a.warpTo ?? "random").ToLowerInvariant();
            Character mover = mode == "behind" ? me : t;
            if (mover.GetMover() == null || (mover != me && !Movable(mover))) return;
            NavNode from = mover.GetMover().GetCurrentNavNode();
            if (from == null) return;
            NavNode land;
            if (mode == "behind")
            {
                NavNode there = t.GetMover()?.GetCurrentNavNode();
                if (there == null || me == t) return;
                land = there.GetLinks().Where(l => Free(l) && l.GetRoom() == there.GetRoom())
                    .OrderByDescending(l => (l.transform.position - me.transform.position).sqrMagnitude).FirstOrDefault();
            }
            else
            {
                float r = Math.Max(1, a.amount > 0 ? a.amount : 3) + 0.25f;
                var spots = Map.instance.GetNavNodesAt(from.transform.position, r, validNodesOnly: true)
                    .Where(n => Free(n) && n != from && n.GetRoom() == from.GetRoom()).ToList();
                land = mode == "away" ? spots.OrderByDescending(n => (n.transform.position - me.transform.position).sqrMagnitude).FirstOrDefault()
                     : spots.Count > 0 ? spots[UnityEngine.Random.Range(0, spots.Count)] : null;
            }
            if (land == null) return;
            string flash = string.IsNullOrEmpty(a.vfx) ? "CYBERNINJA_DASH.particle_shadowstep" : a.vfx;
            Actions.SpawnVfx(flash, mover.GetHitFx().position);
            from.ClearOccupant();
            mover.GetMover().transform.position = land.transform.position;
            mover.GetMover().SetCurrentNavNode(land);
            land.OnEnterNode(mover);
            land.SetOccupant(mover);
            Actions.SpawnVfx(flash, mover.GetHitFx().position);
            Sfx("moves_leap", land.transform.position, ctx, 0.4f);
            if (mode == "behind")
                me.transform.rotation = Quaternion.LookRotation(new Vector3(t.transform.position.x - land.transform.position.x, 0f, t.transform.position.z - land.transform.position.z));
            if (mover != me && mover.ai != null) mover.ai.ForceMove = true;
            if (mover == Player.instance)
            {
                Player.CURRENT_ROOM = land.GetRoom();
                land.GetRoom().Discover();
                Map.instance.UpdateOcclusion();
                CullManager.instance.UpdateCulling();
                DashCam.Teleported(from.transform.position, land.transform.position, blink: true, DashAs(ctx), null);
            }
        }

        /// <summary>The creature the casting ability turns you into for its dash shot ("dashAs"), or null.</summary>
        private static string DashAs(ActionContext ctx) => (ctx.source as ModAbility)?.Spec?.dashAs;

        private static bool Free(NavNode n) => n != null && !n.IsOccupied() && !n.BlockCheck() && !n.Block_Move && !n.HasDoor();

        /// <summary>
        /// "show": a model (the "model" spec, next to the class file) appears on the target for a moment, larger than
        /// life if you like: "pop" swells in and fades, "snap" opens and then clamps shut (jaws, a trap). showTime = seconds.
        /// </summary>
        internal static void Show(ActionSpec a, ActionContext ctx, Character t)
        {
            if (a.model == null || t == null) return;
            // centred on the body (its hit point), so a snap closes on the middle of it, not its feet
            GameObject go = ModelAssets.Spawn(a.model, ClassRegistry.FolderOf(ctx.classId), t.GetHitFx().position, null);
            if (go == null) return;
            Vector3 eye = Camera.main != null ? Camera.main.transform.position : (ctx.caster != null ? ctx.caster.transform.position : go.transform.position);
            Vector3 face = eye - go.transform.position; face.y = 0f;
            if (face.sqrMagnitude > 0.001f) go.transform.rotation = Quaternion.LookRotation(face);
            var anim = go.AddComponent<ShowAnim>();
            anim.life = a.showTime > 0f ? a.showTime : 0.8f;
            anim.snap = (a.showStyle ?? "").ToLowerInvariant() == "snap";
        }

        internal class ShowAnim : MonoBehaviour
        {
            public float life = 0.8f;
            public bool snap;
            private float born;
            private void Start() { born = Time.time; Apply(0f); }
            private void Update()
            {
                float k = (Time.time - born) / Mathf.Max(0.1f, life);
                if (k >= 1f) { Destroy(gameObject); return; }
                Apply(k);
            }
            private void Apply(float k)
            {
                // swell in over the first fifth; "snap" then clamps flat (top and bottom meeting) around the middle;
                // everything shrinks away at the end
                float grow = Mathf.SmoothStep(0.25f, 1f, Mathf.Clamp01(k / 0.2f));
                float y = snap ? Mathf.Lerp(1f, 0.12f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((k - 0.45f) / 0.12f))) : 1f;
                float end = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((k - 0.8f) / 0.2f));
                transform.localScale = new Vector3(grow * end, grow * end * y, grow * end);
            }
        }

        /// <summary>
        /// "recall": every field named fieldName comes back to you; each one runs the actions on every enemy standing
        /// on it or in the straight line between it and you (each enemy once per returning field).
        /// </summary>
        internal static void Recall(ActionSpec a, ActionContext ctx)
        {
            if (ctx.caster == null || string.IsNullOrEmpty(a.fieldName)) return;
            Vector3 me = ctx.caster.transform.position;
            var mids = GroundFields.TakeUp(a.fieldName);
            if (mids.Count > 0) Sfx("moves_recall", me, ctx);
            foreach (Vector3 from in mids)
            {
                var hit = new HashSet<Character>();
                float len = Vector3.Distance(from, me);
                for (float d = 0f; d <= len; d += 0.4f)
                {
                    NavNode n = Map.instance.GetNearestNavNodeAt(Vector3.Lerp(from, me, len > 0f ? d / len : 0f), 0.8f);
                    if (n == null) continue;
                    foreach (Character c in TileStacks.On(n)) if (Alive(c) && IsFoe(c)) hit.Add(c);
                }
                Actions.SpawnVfx("PA_DualWield_SinisterStrike.impactEffect@0.3", from + Vector3.up * 0.4f);
                Streak(from + Vector3.up * 0.5f, ctx.caster.GetHitFx().position, new Color(0.85f, 0.9f, 1f, 0.9f), 0.05f, 0.45f);
                var sub = Copy(ctx, from);
                foreach (Character c in hit) { sub.target = c; Actions.Run(a.actions, sub, c); }
            }
            if (mids.Count > 0) Game.LOG($"{ctx.sourceName}: {mids.Count} {a.fieldName.ToLowerInvariant()}{(mids.Count > 1 ? "s" : "")} come back.");
        }

        /// <summary>
        /// "ricochet": every field named fieldName is used up; from each, the actions hit the nearest enemy within
        /// `radius` (default 4) of it - coins a shot bounces off.
        /// </summary>
        internal static void Ricochet(ActionSpec a, ActionContext ctx)
        {
            if (ctx.caster == null || string.IsNullOrEmpty(a.fieldName)) return;
            float r = a.radius > 0f ? a.radius : 4f;
            var mids = GroundFields.TakeUp(a.fieldName);
            int bounces = 0;
            foreach (Vector3 from in mids)
            {
                Character next = AI_Manager.instance.GetEnemiesAt(from, r).Where(c => Alive(c) && IsFoe(c))
                    .OrderBy(c => (c.transform.position - from).sqrMagnitude).FirstOrDefault();
                Actions.SpawnVfx("EFFECT_Smuggler_Bounty@0.5", from + Vector3.up * 0.5f);
                if (next == null) continue;
                bounces++;
                Streak(from + Vector3.up * 0.5f, next.GetHitFx().position, new Color(1f, 0.85f, 0.35f, 0.95f), 0.045f, 0.55f);
                if (bounces <= 4) Sfx("moves_ricochet", from, ctx, 0.5f);
                var sub = Copy(ctx, from);
                sub.target = next;
                Actions.SpawnVfx("PA_Smuggler_Fistful.PS_impact@0.15", next.GetHitFx().position);
                Actions.Run(a.actions, sub, next);
            }
            if (bounces > 0) Game.LOG($"{ctx.sourceName}: {bounces} ricochet{(bounces > 1 ? "s" : "")}!");
        }

        // ---- streaks: short-lived lines that show the motion (a ricochet's path, a knife flying home, a hook line) ----

        private static Material streakMat;

        private static Material StreakMaterial()
        {
            if (streakMat != null) return streakMat;
            Shader s = null;
            foreach (string n in new[] { "Legacy Shaders/Particles/Additive", "Particles/Additive", "Sprites/Default" })
                if ((s = Shader.Find(n)) != null) break;
            if (s == null) { Plugin.Log.LogWarning("Streaks: no line shader found"); return null; }
            streakMat = new Material(s) { name = "SCCL_Streak", mainTexture = Texture2D.whiteTexture };
            if (streakMat.HasProperty("_TintColor")) streakMat.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            Plugin.Log.LogInfo($"Streaks use shader '{s.name}'");
            return streakMat;
        }

        private static Mesh streakMesh;

        /// <summary>A unit bar along +z (0..1), square cross-section: stretched between two points it makes a streak.</summary>
        private static Mesh StreakMesh()
        {
            if (streakMesh != null) return streakMesh;
            var v = new List<Vector3>(); var t = new List<int>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { int i = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d); t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3, i, i + 2, i + 1, i, i + 3, i + 2 }); }
            Quad(new Vector3(-0.5f, 0, 0), new Vector3(0.5f, 0, 0), new Vector3(0.5f, 0, 1), new Vector3(-0.5f, 0, 1));   // flat, both sides
            Quad(new Vector3(0, -0.5f, 0), new Vector3(0, 0.5f, 0), new Vector3(0, 0.5f, 1), new Vector3(0, -0.5f, 1));   // upright, both sides
            streakMesh = new Mesh { name = "SCCL_Streak" };
            streakMesh.SetVertices(v); streakMesh.SetTriangles(t, 0);
            streakMesh.SetUVs(0, v.Select(p => new Vector2(p.x + 0.5f, p.z)).ToList());
            streakMesh.SetColors(v.Select(_ => Color.white).ToList()); // particle shaders multiply by vertex colour
            streakMesh.RecalculateBounds();
            UnityEngine.Object.DontDestroyOnLoad(streakMesh);
            return streakMesh;
        }

        /// <summary>A short-lived glowing bar from one point to another that fades out (a crossed blade of light).</summary>
        internal static void Streak(Vector3 from, Vector3 to, Color color, float width = 0.04f, float life = 0.45f)
        {
            Material shared = StreakMaterial();
            if (shared == null || (to - from).sqrMagnitude < 0.0001f) return;
            var go = new GameObject("SCCL_Streak");
            go.transform.position = from;
            go.transform.rotation = Quaternion.LookRotation(to - from);
            go.transform.localScale = new Vector3(width, width, (to - from).magnitude);
            go.AddComponent<MeshFilter>().sharedMesh = StreakMesh();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = new Material(shared);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var fade = go.AddComponent<StreakFade>();
            fade.life = life; fade.color = color;
        }

        internal class StreakFade : MonoBehaviour
        {
            public float life = 0.45f;
            public Color color = Color.white;
            private float born;
            private Material m;
            private void Start() { born = Time.time; m = GetComponent<MeshRenderer>().material; Apply(1f); }
            private void Apply(float k)
            {
                Color c = color; c.a *= k;
                if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(c.r * 0.5f, c.g * 0.5f, c.b * 0.5f, 0.5f * c.a));
                if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            }
            private void Update()
            {
                float k = 1f - (Time.time - born) / Mathf.Max(0.05f, life);
                if (k <= 0f) { Destroy(m); Destroy(gameObject); return; }
                Apply(k);
            }
        }

        private static Color Hex(string hex, Color fallback) => !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out Color c) ? c : fallback;

        /// <summary>"tether": a line from you to the target for a moment (a hook line, a grab). "look" = colour (#RRGGBB).</summary>
        internal static void Tether(ActionSpec a, ActionContext ctx, Character t)
        {
            if (!Alive(t) || ctx.caster == null) return;
            Streak(ctx.caster.GetHitFx().position, t.GetHitFx().position, Hex(a.look, new Color(0.8f, 0.8f, 0.82f, 0.9f)), 0.025f, 0.6f);
            Sfx("moves_tether", ctx.caster.transform.position, ctx, 0.5f);
        }

        /// <summary>A shared move's sound (Mods/Library/audio_licensed), a little under the ability's own sound.</summary>
        private static void Sfx(string name, Vector3 at, ActionContext ctx, float volume = 0.6f) =>
            ModAudio.Play("audio_licensed/" + name + ".wav", at, ctx.classId, volume);

        private static bool IsFoe(Character c) => c != Player.instance && c.ai != null && c.ai.GetTeam() != AI.AI_TEAM.Player && c.ai.GetTeam() != AI.AI_TEAM.Passive;

        private static ActionContext Copy(ActionContext ctx, Vector3 center) => new ActionContext
        {
            caster = ctx.caster, source = ctx.source, classId = ctx.classId, sourceName = ctx.sourceName, rank = ctx.rank,
            damageMult = ctx.damageMult, chanceBonus = ctx.chanceBonus, center = center, deferredSelfConsume = ctx.deferredSelfConsume,
        };
    }
}
