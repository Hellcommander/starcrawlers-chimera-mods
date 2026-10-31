using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// Tile sharing ([Tiles] ShareTiles): you and your allies (pets, Conjoined bodies) can stand on the same tile.
    /// A NavNode still has one occupant, the "primary" (you, if you're there), which all of the engine's own logic
    /// keeps using: blocking, single-target attacks, clicks. The others on that tile are kept in a stack here. Area
    /// effects are taught to hit the whole stack (see the area patches), the characters are spread out on the tile so
    /// they don't stand inside each other, and everyone on a shared tile is Crowded (less damage per extra body).
    /// Enemies never share tiles.
    /// </summary>
    internal static class TileStacks
    {
        internal static ConfigEntry<bool> ShareTiles;
        internal static ConfigEntry<int> MaxPerTile;
        internal static ConfigEntry<bool> PetsOntoYou;
        internal static ConfigEntry<int> CrowdPenalty;

        internal const string CoreId = "SCCL_Core";
        internal static string CrowdedId => ModEffect.MakeId(CoreId, "Crowded");

        private static readonly Dictionary<NavNode, List<Character>> stacks = new Dictionary<NavNode, List<Character>>();
        private static readonly AccessTools.FieldRef<NavNode, Character> Occ = AccessTools.FieldRefAccess<NavNode, Character>("occupant");

        internal static void Bind(ConfigFile cfg)
        {
            ShareTiles = cfg.Bind("Tiles", "ShareTiles", true, "You and your allies (pets, Conjoined bodies) can stand on the same tile. Enemies never share. Off = one character per tile, as in vanilla.");
            MaxPerTile = cfg.Bind("Tiles", "MaxPerTile", 3, "Most characters on one shared tile (2-4).");
            PetsOntoYou = cfg.Bind("Tiles", "PetsMayJoinYourTile", false, "Pets and bodies may walk onto the tile you stand on by themselves. Off = only you can step onto theirs.");
            CrowdPenalty = cfg.Bind("Tiles", "CrowdedDamagePenalty", 15, "Percent less damage each character on a shared tile deals, per other character there (0 = no penalty).");
        }

        /// <summary>The built-in Crowded effect, registered like a class effect so the game applies and saves it.</summary>
        internal static void RegisterEffect()
        {
            var def = new ClassDef
            {
                id = CoreId, name = "Breachwork Loader", folder = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(TileStacks).Assembly.Location), "assets"),
                tiers = new List<TierDef>(),
                effects = new List<EffectSpec>
                {
                    new EffectSpec
                    {
                        id = "Crowded", name = "Crowded", icon = "crowded.png", debuff = true, duration = -1, maxStacks = 3,
                        description = "Sharing a tile: everyone here gets in each other's way.",
                        damageDealtPerStack = -Mathf.Clamp(CrowdPenalty.Value, 0, 50) / 100f,
                    },
                },
            };
            ClassRegistry.RegisterEffects(def, null);
        }

        // ---- queries ------------------------------------------------------------------------------------------

        private static bool Alive(Character c) => c != null && !c.GetIsDead();

        internal static bool IsAlly(Character c) => c != null && (c == Player.instance || (c.ai != null && c.ai.GetTeam() == AI.AI_TEAM.Player)) && !c.HasTag("BOSS");

        /// <summary>Everyone on the tile: the primary first, then the others.</summary>
        internal static List<Character> On(NavNode n)
        {
            if (n == null) return new List<Character>();
            Character p = Occ(n);
            if (!stacks.TryGetValue(n, out var list)) return p != null ? new List<Character> { p } : new List<Character>();
            var r = new List<Character>();
            if (p != null) r.Add(p);
            r.AddRange(list.Where(c => c != null && c != p));
            return r;
        }

        /// <summary>The characters sharing a tile with its primary (empty for a normal tile).</summary>
        internal static IEnumerable<Character> Riders(NavNode n)
        {
            if (n == null || !stacks.TryGetValue(n, out var list)) return Enumerable.Empty<Character>();
            Character p = Occ(n);
            return list.Where(c => Alive(c) && c != p);
        }

        internal static IEnumerable<KeyValuePair<NavNode, List<Character>>> All => stacks.Where(kv => kv.Key != null);

        internal static int Count(NavNode n) => On(n).Count(Alive);

        /// <summary>Can <paramref name="c"/> step onto <paramref name="n"/> while others stand there?</summary>
        internal static bool CanJoin(NavNode n, Character c) => CanJoin(n, c, placing: false);

        /// <summary><paramref name="placing"/>: put there by a load or spawn, not walking (pets may then join your tile).</summary>
        internal static bool CanJoin(NavNode n, Character c, bool placing)
        {
            if (!ShareTiles.Value || n == null || !IsAlly(c)) return false;
            var here = On(n).Where(Alive).ToList();
            if (here.Count == 0 || here.Contains(c)) return false;
            if (here.Count >= Mathf.Clamp(MaxPerTile.Value, 2, 4)) return false;
            if (!here.All(IsAlly)) return false;
            if (!placing && c != Player.instance && here.Contains(Player.instance) && !PetsOntoYou.Value) return false;
            return true;
        }

        // ---- bookkeeping --------------------------------------------------------------------------------------

        private static bool busy;

        private static void Normalize(NavNode n)
        {
            if (!stacks.TryGetValue(n, out var list)) return;
            list.RemoveAll(c => !Alive(c) || c.GetMover() == null || c.GetMover().GetCurrentNavNode() != n);
            Character p = Occ(n);
            if (Alive(p) && !list.Contains(p) && p.GetMover() != null && p.GetMover().GetCurrentNavNode() == n) list.Insert(0, p);
            if (!Alive(p) || !list.Contains(p)) p = null;
            // You are the primary when you're there; otherwise keep the current one.
            if (list.Contains(Player.instance)) p = Player.instance;
            else if (p == null) p = list.FirstOrDefault();
            busy = true;
            try { Occ(n) = p; }
            finally { busy = false; }
            if (list.Count <= 1) stacks.Remove(n);
            Arrange(n);
            UpdateCrowded(n);
        }

        // ---- the Crowded penalty ------------------------------------------------------------------------------

        private static void UpdateCrowded(NavNode n)
        {
            var here = On(n).Where(Alive).ToList();
            foreach (Character c in here) Crowd(c, here.Count - 1);
        }

        private static void Crowd(Character c, int others)
        {
            if (!Alive(c) || EffectManager.instance == null) return;
            Effect e = c.effects.GetEffect(CrowdedId);
            if (others <= 0 || CrowdPenalty.Value <= 0) { if (e != null) { e.RemoveInstant(); c.CalculateStats(); } return; }
            if (e == null) e = EffectManager.instance.ApplyEffect(CrowdedId, c);
            (e as ModEffect)?.SetStacks(others);
        }

        /// <summary>
        /// Makes <paramref name="c"/> its tile's occupant for a moment (for vanilla code that only looks at the occupant).
        /// The next change on the tile, or the turn's sweep, puts you back in front.
        /// </summary>
        internal static void Promote(NavNode n, Character c)
        {
            if (n != null && stacks.TryGetValue(n, out var list) && list.Contains(c) && Alive(c)) Occ(n) = c;
        }

        /// <summary>Drops stacks whose tiles are gone (the level they were on was replaced).</summary>
        internal static void Reset()
        {
            foreach (NavNode n in stacks.Keys.ToList()) if (n == null) stacks.Remove(n);
        }

        /// <summary>Re-checks every stack (a teleport or an odd ClearOccupant can leave one stale).</summary>
        internal static void Sweep()
        {
            foreach (NavNode n in stacks.Keys.ToList())
            {
                if (n == null) { stacks.Remove(n); continue; }
                Normalize(n);
            }
            // Crowded left over from a save, or from a stack that broke up some odd way.
            var crowded = new HashSet<Character>(stacks.Values.SelectMany(l => l));
            var everyone = (Traverse.Create(AI_Manager.instance).Field("ais").GetValue<List<AI>>() ?? new List<AI>())
                .Where(a => a != null).Select(a => a.GetCharacter()).Concat(new[] { (Character)Player.instance });
            foreach (Character c in everyone)
                if (Alive(c) && !crowded.Contains(c) && c.effects.GetEffect(CrowdedId) != null) Crowd(c, 0);
        }

        // ---- spreading the characters out on the tile ---------------------------------------------------------

        private static void Arrange(NavNode n)
        {
            var here = On(n).Where(Alive).ToList();
            float spacing = 1.6f;
            NavNode link = n.GetLinks().FirstOrDefault(l => l != null);
            if (link != null) spacing = Vector3.Distance(n.transform.position, link.transform.position);
            for (int i = 0; i < here.Count; i++)
            {
                StackSpot s = here[i].GetComponent<StackSpot>();
                if (s == null) continue;
                s.node = n;
                if (here.Count <= 1) { s.Release(); continue; }
                bool youHere = here.Contains(Player.instance);
                if (here[i] == Player.instance) { s.offset = Vector3.zero; s.behindPlayer = false; continue; }
                int k = youHere ? here.Where(c => c != Player.instance).ToList().IndexOf(here[i]) : i;
                int m = youHere ? here.Count - 1 : here.Count;
                // Around you: behind your shoulders. Elsewhere: spread on a small ring.
                s.behindPlayer = youHere;
                s.slot = k;
                s.slots = m;
                float a = (k + 0.5f) / m * Mathf.PI * 2f;
                s.offset = youHere ? Vector3.zero : new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * spacing * 0.22f;
                s.spacing = spacing;
            }
        }

        /// <summary>Keeps a stacked character at its spot on the tile while it isn't walking.</summary>
        internal class StackSpot : MonoBehaviour
        {
            public NavNode node;
            public Vector3 offset;
            public bool behindPlayer;
            public int slot, slots = 1;
            public float spacing = 1.6f;
            private Character ch;
            private bool active;

            internal void Release() { active = false; node = null; }

            private void LateUpdate()
            {
                if (node == null) return;
                if (ch == null) ch = GetComponent<Character>();
                Character_Mover m = ch != null ? ch.GetMover() : null;
                if (m == null || m.GetCurrentNavNode() != node || (bool)Traverse.Create(m).Field("isMoving").GetValue()) return;
                Vector3 off = offset;
                if (behindPlayer && Player.instance != null)
                {
                    Vector3 back = -Player.instance.transform.forward, right = Player.instance.transform.right;
                    float side = slots <= 1 ? 0f : Mathf.Lerp(-1f, 1f, slot / (float)(slots - 1));
                    off = (back * 0.32f + right * side * 0.22f) * spacing;
                }
                transform.position = node.transform.position + off;
                active = true;
            }

            private void OnDisable() { if (active && node != null && ch != null) transform.position = node.transform.position; }
        }

        // ---- patches ------------------------------------------------------------------------------------------

        // NavNode.SetOccupant / OnExitNode / ClearOccupant are one-liners the JIT inlines into their callers, so they
        // can't be patched reliably. The vanilla code is left to overwrite the single occupant field, and the callers
        // below reconcile the stack right after.

        private static bool OnTile(Character c, NavNode n) => Alive(c) && c.GetMover() != null && c.GetMover().GetCurrentNavNode() == n;

        /// <summary>After <paramref name="c"/> took tile <paramref name="n"/>: if allies were there, they share it.</summary>
        private static void Arrived(NavNode n, Character c, Character prev)
        {
            if (!ShareTiles.Value || n == null || c == null) return;
            bool stacked = stacks.ContainsKey(n);
            if (prev != null && prev != c && OnTile(prev, n) && IsAlly(prev) && IsAlly(c))
            {
                if (!stacked) { stacks[n] = new List<Character> { prev }; }
                else if (!stacks[n].Contains(prev)) stacks[n].Add(prev);
                stacked = true;
            }
            if (!stacked) return;
            if (!stacks[n].Contains(c)) stacks[n].Add(c);
            foreach (Character x in stacks[n]) if (x != null && x.GetComponent<StackSpot>() == null) x.gameObject.AddComponent<StackSpot>();
            Normalize(n);
        }

        /// <summary>After <paramref name="c"/> left tile <paramref name="n"/> (the vanilla code just emptied it).</summary>
        private static void Departed(NavNode n, Character c)
        {
            if (n == null) return;
            if (!stacks.ContainsKey(n))
            {
                // Vanilla OnExitNode empties the tile whoever leaves; if you're still standing on it, it's yours.
                if (Occ(n) == null && c != Player.instance && Player.instance != null && OnTile(Player.instance, n)) Occ(n) = Player.instance;
                return;
            }
            stacks[n].Remove(c);
            Occ(n) = null;
            Normalize(n);
            if (c != null) { Crowd(c, 0); c.GetComponent<StackSpot>()?.Release(); }
        }

        [HarmonyPatch(typeof(Character_Mover), "StartMove")]
        private static class Patch_StartMove
        {
            private static void Prefix(NavNode startNode, NavNode targetNode, out Character __state) => __state = targetNode != null ? Occ(targetNode) : null;

            private static void Postfix(Character_Mover __instance, NavNode startNode, NavNode targetNode, Character __state)
            {
                Character c = MoverChar(__instance);
                if (startNode != targetNode) Departed(startNode, c);
                Arrived(targetNode, c, __state);
            }
        }

        [HarmonyPatch(typeof(NavNode), nameof(NavNode.OnEnterNode))]
        private static class Patch_Enter
        {
            private static void Prefix(NavNode __instance, out Character __state) => __state = Occ(__instance) ?? (__instance == hiddenAt ? hiddenOcc : null);
            private static void Postfix(NavNode __instance, Character c, Character __state) => Arrived(__instance, c, __state);
        }

        /// <summary>Teleports, knockbacks, spawning and loading place characters through these.</summary>
        [HarmonyPatch]
        private static class Patch_Place
        {
            private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(Character_Mover), nameof(Character_Mover.SetCurrentNavNode));
                yield return AccessTools.Method(typeof(Character_Mover), nameof(Character_Mover.SetPosition));
                yield return AccessTools.Method(typeof(Character_Mover), nameof(Character_Mover.Init));
            }

            private static void Prefix(Character_Mover __instance, out NavNode __state) => __state = MoverNode(__instance);

            private static void Postfix(Character_Mover __instance, NavNode __state)
            {
                if (__state != null && __state != MoverNode(__instance)) Departed(__state, MoverChar(__instance) ?? __instance.GetComponent<Character>());
            }
        }

        [HarmonyPatch(typeof(Character_Mover), nameof(Character_Mover.AbortMove))]
        private static class Patch_Abort
        {
            private static void Postfix(Character_Mover __instance)
            {
                foreach (string f in new[] { "moveOrigin", "moveTarget", "previousNode" })
                {
                    NavNode n = Traverse.Create(__instance).Field(f).GetValue<NavNode>();
                    if (n != null && stacks.ContainsKey(n)) Normalize(n);
                }
                NavNode cur = MoverNode(__instance);
                if (cur != null && stacks.ContainsKey(cur)) Normalize(cur);
            }
        }

        /// <summary>A character dying on a shared tile: the others keep it (Die clears the occupant).</summary>
        [HarmonyPatch(typeof(Character), nameof(Character.Die))]
        private static class Patch_Die
        {
            private static void Prefix(Character __instance, out NavNode __state) => __state = __instance.GetMover()?.GetCurrentNavNode();

            private static void Postfix(Character __instance, NavNode __state)
            {
                if (__state == null || !stacks.ContainsKey(__state)) return;
                stacks[__state].Remove(__instance);
                if (Occ(__state) == __instance || Occ(__state) == null) Occ(__state) = null;
                Normalize(__state);
            }
        }

        // Vanilla checks that a tile is free by its occupant field. Where allies may share, the occupant is hidden for
        // the length of the vanilla call and put back after it, so the game's own checks run unchanged.
        private static NavNode hiddenAt;
        private static Character hiddenOcc;

        private static void Hide(NavNode n)
        {
            hiddenAt = n;
            hiddenOcc = Occ(n);
            Occ(n) = null;
        }

        /// <summary>Puts a hidden occupant back unless the vanilla call already gave the tile one.</summary>
        private static void Unhide()
        {
            if (hiddenAt != null && Occ(hiddenAt) == null) Occ(hiddenAt) = hiddenOcc;
            hiddenAt = null;
            hiddenOcc = null;
        }

        /// <summary>Movement: an ally may walk onto a tile only allies stand on (room permitting).</summary>
        [HarmonyPatch(typeof(Character_Mover), nameof(Character_Mover.MoveCheck))]
        private static class Patch_MoveCheck
        {
            private static void Prefix(Character_Mover __instance, NavNode targetNode)
            {
                if (ShareTiles.Value && targetNode != null && targetNode.IsOccupied() && CanJoin(targetNode, __instance.GetComponent<Character>())) Hide(targetNode);
            }

            private static void Finalizer() => Unhide();
        }

        /// <summary>Walking (forward, back or strafing) into a tile your allies hold: step onto it instead of nudging them.</summary>
        internal static bool PlayerWalksOnto(Player_Control pc, Vector3 direction)
        {
            if (!ShareTiles.Value || Game.RESOLVING || Game.TARGETING) return false;
            NavNode n = pc.GetNodeInDirection(new Vector3(Mathf.Round(direction.x), 0f, Mathf.Round(direction.z)));
            return n != null && n.IsOccupied() && CanJoin(n, Player.instance);
        }

        /// <summary>Area effects see everyone on a shared tile.</summary>
        [HarmonyPatch(typeof(AI_Manager), nameof(AI_Manager.GetEnemiesAt), new[] { typeof(Vector3), typeof(float), typeof(AI.AI_TEAM) })]
        private static class Patch_EnemiesAt
        {
            private static void Postfix(Vector3 pos, float distance, AI.AI_TEAM myTeam, List<Character> __result)
            {
                if (stacks.Count == 0 || __result == null) return;
                foreach (var kv in All)
                {
                    if (Vector3.Distance(kv.Key.transform.position, pos) > distance + 0.01f) continue;
                    foreach (Character c in Riders(kv.Key))
                        if (c.ai != null && c.ai.GetBaseTeam() != myTeam && c.ai.GetBaseTeam() != AI.AI_TEAM.Passive && !__result.Contains(c)) __result.Add(c);
                }
            }
        }

        [HarmonyPatch(typeof(AI_Manager), nameof(AI_Manager.GetPlayerAlliesAt))]
        private static class Patch_AlliesAt
        {
            private static void Postfix(Vector3 pos, float distance, List<Character> __result)
            {
                if (stacks.Count == 0 || __result == null) return;
                foreach (var kv in All)
                {
                    if (Vector3.Distance(kv.Key.transform.position, pos) > distance + 0.01f) continue;
                    foreach (Character c in Riders(kv.Key))
                        if ((c == Player.instance || (c.ai != null && c.ai.GetTeam() == AI.AI_TEAM.Player)) && !__result.Contains(c)) __result.Add(c);
                }
            }
        }

        /// <summary>The AI moves anyone who isn't their tile's occupant somewhere free; sharing a tile is fine.</summary>
        [HarmonyPatch(typeof(AI), nameof(AI.ValidatePosition))]
        private static class Patch_Validate
        {
            private static bool Prefix(AI __instance)
            {
                Character c = __instance.GetCharacter();
                NavNode n = c != null && c.GetMover() != null ? c.GetMover().GetCurrentNavNode() : null;
                if (n == null) return true;
                if (stacks.ContainsKey(n) && stacks[n].Contains(c)) return false;
                // Loading can place a character onto an ally's tile before either knows; join them instead of moving.
                Character occ = Occ(n);
                if (ShareTiles.Value && occ != null && occ != c && Alive(occ) && IsAlly(occ) && CanJoin(n, c, placing: true))
                {
                    Arrived(n, c, occ);
                    return false;
                }
                return true;
            }
        }

        private static readonly AccessTools.FieldRef<Character_Mover, NavNode> MoverNode = AccessTools.FieldRefAccess<Character_Mover, NavNode>("currentNode");
        private static readonly AccessTools.FieldRef<Character_Mover, Character> MoverChar = AccessTools.FieldRefAccess<Character_Mover, Character>("character");

        /// <summary>
        /// Placing a character (spawn, load) on a tile allies already hold joins them; vanilla sends it to a random
        /// tile in the room instead, which would break up a saved stack on every load.
        /// </summary>
        [HarmonyPatch(typeof(Character_Mover), nameof(Character_Mover.Init))]
        private static class Patch_MoverInit
        {
            private static void Prefix(Character_Mover __instance)
            {
                if (!ShareTiles.Value || Map.instance == null) return;
                Character me = __instance.GetComponent<Character>();
                NavNode at = Map.instance.GetNavNodeAt(__instance.transform.position);
                if (me == null || at == null || !at.IsOccupied() || !CanJoin(at, me, placing: Game.Game_isLoading) || c_isEnemyLoad(me)) return;
                Hide(at); // vanilla keeps the tile, and its OnEnterNode joins the hidden occupant (Patch_Enter)
            }

            private static void Finalizer() => Unhide();

            private static bool c_isEnemyLoad(Character c) => c != Player.instance && c.ai == null;
        }

        [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.DeserializeLevel))]
        private static class Patch_NewLevel
        {
            private static void Prefix() => Reset();
        }

        [HarmonyPatch(typeof(Player_AbilityControl), nameof(Player_AbilityControl.Turn))]
        private static class Patch_Turn
        {
            private static void Postfix() { try { Sweep(); } catch (Exception e) { Plugin.Log.LogError("Tile stacks: " + e); } }
        }
    }
}
