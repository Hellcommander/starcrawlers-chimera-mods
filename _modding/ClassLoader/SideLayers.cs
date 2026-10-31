using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace SCClassLoader
{
    /// <summary>
    /// Towers: side area kinds with a "tower" block are a stack of layers. You arrive on the ground layer; each layer
    /// above is tougher, has one more hard anomaly, and the top holds the objective and the vault. Every layer stays as
    /// you left it: what's dead stays dead, what you dropped is still on the floor. Two ways between layers:
    /// - Stairs, the borrowed map's own, used the way the game uses them: a stair that would take you on to the next
    ///   floor goes up a layer, one that would take you back goes down (out to the real floor, on the ground layer).
    ///   You come out on the nearest stair of the other sort, so a stair always leads back to where you took it.
    /// - Lifts, placed by the tower: one up far from where you arrive, one down where you arrive. A lift moves after
    ///   you've stood on it for a few turns, and its noise draws the enemies nearby.
    /// Either can be "unlock": "key": shut until the layer's overseer, who carries the override, is down. Enemies
    /// already fighting you beside you come along whichever way you go.
    ///
    /// Only the layer you're on is a live map. The others are LevelSaveInfo snapshots in SideAreas.Data
    /// ("tower_save_N"), next to the layer's own keys (objective, vault, anomalies, lifts: "tower_state_N"), so a save
    /// made on any layer reloads into the whole tower, and a save without the mod never sees any of it.
    /// </summary>
    internal static class SideLayers
    {
        private static Dictionary<string, object> D => SideAreas.Data;

        private static T Get<T>(string key, T fallback = default) =>
            D != null && D.TryGetValue(key, out object v) && v is T t ? t : fallback;

        internal const string KeyTag = "SCCLT_OVERSEER";

        private static bool riding;

        /// <summary>How the layer being built is reached: by stair or lift, going up or down, from where.</summary>
        private struct Arrival { public bool stair, up; public Vector3 from; }
        private static Arrival? next;

        private static TowerSpec Spec => SideAreas.InSide ? SideKinds.Current.tower : null;

        /// <summary>You're in a tower right now.</summary>
        internal static bool Active => Spec != null && D.ContainsKey("tower_n");

        /// <summary>Layer you're on (0 = ground) and how many there are.</summary>
        internal static int At => Get("tower_at", 0);
        internal static int Count => Get("tower_n", 1);
        internal static bool OnTop => At >= Count - 1;

        /// <summary>The green pad only appears on the ground layer, and stairs only lead out from there.</summary>
        internal static bool ExitHere => !Active || At == 0;

        internal static bool ObjectiveHere
        {
            get
            {
                TowerSpec t = Spec;
                if (t == null) return true;
                switch ((t.objectiveLayer ?? "top").ToLowerInvariant())
                {
                    case "ground": return At == 0;
                    case "every": return true;
                    default: return OnTop;
                }
            }
        }

        internal static bool VaultHere => Spec == null || !Spec.vaultOnTop || OnTop;

        private static bool IsKey(string unlock) => string.Equals(unlock, "key", StringComparison.OrdinalIgnoreCase);
        private static bool Locked => Get("lift_locked", false);
        private static bool LiftLocked => Locked && IsKey(Spec.lift.unlock);
        private static bool StairsLocked => Locked && IsKey(Spec.stairs.unlock);
        private static string Overseer => (Spec.overseer?.name ?? "overseer").ToLowerInvariant();

        /// <summary>The keys that belong to one layer and travel with its snapshot.</summary>
        private static bool LayerKey(string k) =>
            (k.StartsWith("obj_") && k != "obj_total") || k.StartsWith("timer_") || k.StartsWith("hunted_") || k.StartsWith("alarm_") || k.StartsWith("lift_")
            || k.StartsWith("vault_") || k == "echo_anoms" || k == "echo_turn" || k == "collapse_turns" || k == "deeper_pos";

        // ---- building -----------------------------------------------------------------------------------------

        /// <summary>Start of SideAreas.Build: a fresh breach forgets any old tower and, for a tower kind, rolls its height.</summary>
        internal static void Begin(KindSpec kind, int theme, bool firstLayer)
        {
            if (!firstLayer || D == null) return;
            Clear();
            next = null;
            if (kind.tower == null) return;
            int[] l = kind.tower.layers != null && kind.tower.layers.Length > 0 ? kind.tower.layers : new[] { 3 };
            int lo = Math.Max(2, l[0]), hi = Math.Max(lo, l.Length > 1 ? l[1] : l[0]);
            D["tower_n"] = Random.Range(lo, hi + 1);
            D["tower_at"] = 0;
            D["tower_theme_0"] = theme;
            D["tower_maps"] = new string[0];
        }

        /// <summary>A map for the new layer, one the tower hasn't used yet if there is one (null = not a tower).</summary>
        internal static Map PickMap(Map[] pool)
        {
            if (!Active) return null;
            var used = Get("tower_maps", new string[0]);
            var fresh = pool.Where(m => !used.Contains(m.internalName)).ToArray();
            if (fresh.Length == 0) fresh = pool;
            Map pick = fresh[Random.Range(0, fresh.Length)];
            D["tower_maps"] = used.Concat(new[] { pick.internalName }).ToArray();
            return pick;
        }

        private static IEnumerable<Stair> AllStairs => Map.instance.mapStairs.Where(s => s != null && s.myNode != null);

        /// <summary>
        /// A floor's maps only carry stairs leading on; the game links the one you arrive at back to where you came
        /// from. A tower does the same: the stair you first arrived at on a layer leads back down (out, on the ground
        /// layer), and every other stair leads up.
        /// </summary>
        internal static bool IsBackStair(Stair s) => s?.myNode != null && Spot("lift_backstair") is Vector3 v && Vector3.Distance(s.myNode.transform.position, v) < 0.7f;

        private static IEnumerable<Stair> StairsOn => AllStairs.Where(s => !IsBackStair(s));

        /// <summary>
        /// The stair really goes to another layer of this tower: the back stair above the ground layer, any other
        /// below the top, and only in towers whose stairs link layers at all. Everything else would only lead out.
        /// </summary>
        internal static bool LeadsToLayer(Stair s) =>
            s?.myNode != null && Active && Spec.stairs.link && (IsBackStair(s) ? At > 0 : !OnTop);

        /// <summary>End of SideAreas.Build: the layer's lifts, its overseer, and the climb's toll on whoever lives here.</summary>
        internal static void OnLayerBuilt()
        {
            TowerSpec t = Spec;
            if (!Active) return;
            int layer = At;
            if (layer == 0) D["tower_anoms_0"] = Get("echo_anoms", new string[0]);
            NavNode arrival = Player.instance.GetMover().GetCurrentNavNode();
            Stair came = AllStairs.FirstOrDefault(s => s.myNode == arrival);
            if (came != null) Save("lift_backstair", came.myNode.transform.position);
            if (layer > 0)
            {
                // the lift down stands beside the stair you arrive at; come up by lift and you step off it
                NavNode down = arrival.GetLinks().FirstOrDefault(n => n != null && !n.IsOccupied() && !n.HasDoor() && !n.hazards.Any() && Map.instance.GetStairAt(n.transform.position) == null)
                               ?? Map.instance.GetClosestNode(arrival, 3f, validNodesOnly: true);
                if (down != null)
                {
                    Save("lift_down", down.transform.position);
                    if (!(next is Arrival a && a.stair)) Put(down);
                }
                if (t.toughnessPerLayer > 0)
                    foreach (Character c in Echoes.Enemies().ToList())
                    {
                        int hp = Mathf.Max(1, Mathf.RoundToInt(c.GetHealthMax() * (1f + t.toughnessPerLayer * layer)));
                        c.SetHealth(hp, hp);
                    }
                int elites = t.elitePerLayer > 0 ? SideKinds.MakeElites(t.elitePerLayer * layer) : 0;
                if (elites > 0) Game.LOG($"<color=#FFC35C>The climb shows: {elites} more elite{(elites > 1 ? "s" : "")} up here.</color>");
            }
            if (!OnTop)
            {
                NavNode up = UpSpot(t.lift, arrival);
                if (up != null) Save("lift_up", up.transform.position);
                else Plugin.Log.LogWarning($"Towers: no room for a lift up on layer {layer + 1}");
                bool stairsUp = t.stairs.link && StairsOn.Any();
                if ((up != null && IsKey(t.lift.unlock)) || (stairsUp && IsKey(t.stairs.unlock))) PickOverseer(t.overseer ?? new OverseerSpec(), arrival);
            }
            next = null;
            PlaceLifts();
            Game.LOG($"<color=#7FE0FF>{SideKinds.Tinted(SideKinds.Current)}: layer {layer + 1} of {Count}.</color> " + (OnTop ? $"<color=#FFD966>{t.topText}</color>" : WaysUp()));
            Plugin.Log.LogInfo($"Towers: built layer {layer + 1}/{Count} ({Map.instance.internalName}), stairs {StairsOn.Count()} on / {AllStairs.Count(IsBackStair)} back, anomalies {string.Join(",", Get("echo_anoms", new string[0]))}");
        }

        private static string WaysUp()
        {
            TowerSpec t = Spec;
            bool stairs = t.stairs.link && StairsOn.Any(), lift = D.ContainsKey("lift_up");
            var ways = new List<string>();
            if (lift) ways.Add(LiftLocked ? "a lift up, locked" : $"a lift up (slow and loud: {t.lift.callTurns} turns on it)");
            if (stairs) ways.Add(StairsLocked ? "stairs up, sealed" : "stairs up");
            string s = ways.Count == 0 ? "No way up from here." : $"Ways up: {string.Join(", ", ways)}.";
            return Locked ? s + $" The {Overseer} carries the override." : s;
        }

        /// <summary>The kind's arrival line for a layer above the ground (null = not a tower).</summary>
        internal static string ArrivalLine() => Active ? $"<color=#7FE0FF>{string.Format(Spec.upText, At + 1, Count)}</color>" : null;

        private static NavNode UpSpot(LiftSpec l, NavNode from)
        {
            Room here = from.GetRoom();
            Vector3 me = from.transform.position;
            foreach (Room r in Echoes.Rooms(here).Where(r => r != Echoes.VaultRoom && !r.Properties.IsHallway)
                         .OrderBy(r => Mathf.Abs(Vector3.Distance(r.transform.position, me) - l.distance)).Take(6).OrderBy(_ => Random.value))
                for (int i = 0; i < 6; i++)
                {
                    NavNode n = r.GetRandomNode();
                    if (n != null && !n.IsOccupied() && !n.HasDoor() && !n.hazards.Any() && Map.instance.GetStairAt(n.transform.position) == null) return n;
                }
            return SideAreas.RandomFreeNode(here);
        }

        private static void PickOverseer(OverseerSpec o, NavNode from)
        {
            Vector3 me = from.transform.position;
            Character c = Echoes.Enemies().Where(x => !x.tags.Any(t => t?.tagName != null && t.tagName.StartsWith("SCCL")) && x.GetMover()?.GetCurrentNavNode()?.GetRoom() != from.GetRoom())
                .OrderByDescending(x => Vector3.Distance(x.transform.position, me)).Take(5).OrderBy(_ => Random.value).FirstOrDefault();
            if (c == null) return; // nobody to carry it: the way up is simply open
            int hp = Mathf.Max(1, Mathf.RoundToInt(c.GetHealthMax() * o.health));
            c.SetHealth(hp, hp);
            c.SetName(o.name);
            c.AddTag(KeyTag);
            if (!string.IsNullOrEmpty(o.drop)) c.AddTag(Echoes.DropTag + SideAreaData.Rarity(o.drop));
            D["lift_locked"] = true;
        }

        // ---- the lifts ----------------------------------------------------------------------------------------

        internal class Lift : MonoBehaviour { public bool up; }

        /// <summary>The lifts on this layer, switched off ones included: culling turns off whatever is out of sight.</summary>
        internal static Lift[] Lifts() => Map.instance != null ? Map.instance.GetComponentsInChildren<Lift>(true) : new Lift[0];

        private static void Save(string key, Vector3 p) => D[key] = new[] { p.x, p.y, p.z };

        private static Vector3? Spot(string key) => Get<object>(key) is float[] p ? new Vector3(p[0], p[1], p[2]) : (Vector3?)null;

        private static void Put(NavNode n)
        {
            SideAreas.MoveOccupantAway(n);
            Player.instance.GetMover().GetCurrentNavNode()?.ClearOccupant();
            Player.instance.transform.position = n.transform.position;
            Player.instance.GetMover().SetCurrentNavNode(n);
            CullManager.instance.UpdateCulling();
        }

        private static void PlaceLifts()
        {
            foreach (Lift old in Lifts()) Object.Destroy(old.gameObject);
            LiftSpec l = Spec?.lift;
            if (l == null) return;
            foreach (var (key, up) in new[] { ("lift_down", false), ("lift_up", true) })
            {
                if (!(Spot(key) is Vector3 v)) continue;
                TeleportPad pad = Contracts.PlacePad(v, up && LiftLocked ? l.lockedColor : l.color, up ? "SCCL_LiftUp" : "SCCL_LiftDown");
                pad.gameObject.AddComponent<Lift>().up = up;
                if (l.model != null) ModelAssets.Spawn(l.model, SideKinds.Current.pack?.folder, v, pad.transform);
            }
            BreachFinder.Refresh();
        }

        internal static IEnumerable<(Vector3 pos, bool up)> LiftSpots()
        {
            if (!Active) yield break;
            if (Spot("lift_down") is Vector3 d) yield return (d, false);
            if (Spot("lift_up") is Vector3 u) yield return (u, true);
        }

        [HarmonyPatch(typeof(TeleportPad), nameof(TeleportPad.OnEnterNode))]
        private static class Patch_LiftEnter
        {
            private static bool Prefix(TeleportPad __instance, Character c)
            {
                Lift lift = __instance.GetComponent<Lift>();
                if (lift == null) return true;
                if (c == Player.instance && !Game.Game_isLoading && !riding && Active) Call(lift.up);
                return false;
            }
        }

        private static void Call(bool up)
        {
            LiftSpec l = Spec.lift;
            if (up && LiftLocked) { Game.LOG($"<color={l.lockedColor}>The lift won't go up without its override. The {Overseer} on this layer has it.</color>"); return; }
            if (!SideKinds.ExitsOpen(say: true)) return;
            if (l.callTurns <= 0) { Plugin.Instance.StartCoroutine(Ride(up ? 1 : -1, stair: false, Player.instance.transform.position)); return; }
            D["lift_call"] = new[] { up ? 1 : -1, 0 };
            int woke = 0;
            if (l.noise > 0)
                foreach (Character c in Echoes.Enemies().Where(c => Vector3.Distance(c.transform.position, Player.instance.transform.position) <= l.noise && c.ai.GetState() != AI.AI_STATE.Active).ToList())
                {
                    Echoes.Hunt(c);
                    woke++;
                }
            Game.LOG($"<color={l.color}>You call the lift {(up ? "up" : "down")}. Stay on it for {l.callTurns} turn{(l.callTurns > 1 ? "s" : "")}.</color>{(woke > 0 ? " <color=#FF8F5C>The machinery is loud: something heard it.</color>" : "")}");
            AudioManager.PlayEvent("event:/Environment/Door_Office_Lock", Player.instance.transform.position);
        }

        /// <summary>Each turn in a tower: a lift cleared by a hazard comes back, and a called lift counts down while you stay on it.</summary>
        internal static void Turn()
        {
            if (!Active || riding) return;
            if (LiftSpots().Count() > Lifts().Length) PlaceLifts();
            if (!(Get<object>("lift_call") is int[] call)) return;
            Vector3 me = Player.instance.transform.position;
            Lift on = Lifts().FirstOrDefault(x => x != null && x.up == call[0] > 0 && Vector3.Distance(x.transform.position, me) < 0.7f);
            if (on == null) { D.Remove("lift_call"); Game.LOG("<color=#9FB7C9>You step off and the lift stops where it is.</color>"); return; }
            call[1]++;
            if (call[1] < Spec.lift.callTurns) { Game.LOG($"<color={Spec.lift.color}>The lift is coming... {call[1]}/{Spec.lift.callTurns}</color>"); return; }
            D.Remove("lift_call");
            Plugin.Instance.StartCoroutine(Ride(call[0], stair: false, me));
        }

        // ---- the stairs ---------------------------------------------------------------------------------------

        /// <summary>
        /// A stair used in a tower (from SideAreas' UseStair patch). False = it's the way out (a stair back, on the
        /// ground layer); true = the tower took care of it, by moving you a layer or saying why not.
        /// </summary>
        internal static bool TakeStair(Stair s)
        {
            if (!Active || s == null) return false;
            TowerSpec t = Spec;
            bool up = !IsBackStair(s);
            if (!up && At == 0) return false;
            if (!t.stairs.link) { Game.LOG("<color=#7FE0FF>These stairs only go round in circles. The lifts are the way between layers.</color>"); return true; }
            if (up && OnTop) { Game.LOG($"<color=#9FB7C9>{t.stairs.blockedText}</color>"); return true; }
            if (up && StairsLocked) { Game.LOG($"<color={t.lift.lockedColor}>{string.Format(t.stairs.lockedText, Overseer)}</color>"); return true; }
            if (riding || !SideKinds.ExitsOpen(say: true)) return true;
            Plugin.Instance.StartCoroutine(Ride(up ? 1 : -1, stair: true, s.myNode.transform.position));
            return true;
        }

        [HarmonyPatch(typeof(Character), nameof(Character.Die))]
        private static class Patch_OverseerDown
        {
            private static void Postfix(Character __instance)
            {
                if (__instance == null || !__instance.HasTag(KeyTag) || !Active) return;
                __instance.RemoveTag(KeyTag);
                Unlock("You take the override off the {0}. The way up is open.");
            }
        }

        private static void Unlock(string text)
        {
            if (!Locked) return;
            D.Remove("lift_locked");
            if (text != null) Game.LOG($"<color=#9FE08A>{string.Format(text, Overseer)}</color>");
            AudioManager.PlayEvent("event:/Abilities/Hacker/Hacker_Hack");
            PlaceLifts();
        }

        // ---- moving between layers ----------------------------------------------------------------------------

        internal static IEnumerator Ride(int dir, bool stair, Vector3 from)
        {
            if (riding || !Active) yield break;
            int to = At + dir;
            if (to < 0 || to >= Count) yield break;
            riding = true;
            for (float t = 0; t < 5f && (Game.RESOLVING || Game.LOCK || Player.instance.GetMover().isMoving); t += Time.unscaledDeltaTime) yield return null;
            if (Player.instance == null || Player.instance.GetIsDead() || !Active) { riding = false; yield break; }
            HUD.instance.DoLoadingScreen();
            yield return null;
            bool failed = false;
            try { DoRide(to, new Arrival { stair = stair, up = dir > 0, from = from }); }
            catch (Exception e)
            {
                // a layer that won't come back is a tower you can't stay in: out to the real floor
                Plugin.Log.LogError("Towers: moving between layers failed, going back to the real floor: " + e);
                SideAreas.Unstick();
                failed = true;
            }
            next = null;
            riding = false;
            if (failed) yield return SideAreas.Return(keep: false);
        }

        private struct Rider { public string id, name; public float health; }

        /// <summary>For the devtest: leave out the end-of-frame occlusion pass, to see what it changes.</summary>
        internal static bool SkipSettle;

        /// <summary>For the devtest: how many got on with you last time, and how many made it.</summary>
        internal static int LastBoarded, LastCame;

        private static void DoRide(int to, Arrival how)
        {
            LevelManager lm = LevelManager.instance;
            int origin = Game.FLOOR, from = At;
            D.Remove("lift_call");
            var riders = Boarders(Spec.lift);
            LastBoarded = riders.Count;
            LastCame = 0;

            // the layer you leave becomes a snapshot, with its own keys beside it
            D["tower_save_" + from] = SideAreas.SideSerialize();
            D["tower_state_" + from] = D.Where(kv => LayerKey(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            try { Echoes.OnReturn(); } // takes the layer's effects off you
            catch (Exception e) { Plugin.Log.LogError("Towers: leaving the layer: " + e); }
            foreach (string k in D.Keys.Where(LayerKey).ToList()) D.Remove(k);
            D.Remove("side_level");

            SideAreas.Free();
            Game.Game_isTransitioning = true;
            Player_AbilityControl.instance.LevelChangeStart();
            Player.instance.GetMover().GetCurrentNavNode()?.ClearOccupant();
            lm.ClearLevel();
            Menu_MiniMap.instance.Reset();
            D["tower_at"] = to;
            D["side_depth"] = to + 1;
            D["side_theme"] = ThemeOf(to);

            next = how;
            if (Get<object>("tower_save_" + to) is LevelSaveInfo save) Restore(to, save, how);
            else
            {
                D["anoms_" + origin] = AnomaliesFor(to);
                SideAreas.Build(origin, ThemeOf(to), firstLayer: false);
            }
            Board(riders, how);
            BreachFinder.Refresh();
            if (!SkipSettle) Plugin.Instance.StartCoroutine(Settle());
            Plugin.Log.LogInfo($"Towers: {(how.stair ? "stairs" : "lift")} from layer {from + 1} to {to + 1} of {Count} ({Map.instance.internalName}), {riders.Count} came along");
        }

        /// <summary>
        /// What the game's own teleport pads do after moving you: at the end of the frame, once the old layer's map
        /// is really gone, work out again which rooms are on show from where you stand.
        /// </summary>
        private static IEnumerator Settle()
        {
            yield return new WaitForEndOfFrame();
            if (Player.instance == null || Map.instance == null) yield break;
            Player.CURRENT_ROOM = Player.instance.GetMover().GetCurrentNavNode()?.GetRoom() ?? Player.CURRENT_ROOM;
            Map.instance.UpdateOcclusion();
            CullManager.instance.UpdateCulling();
        }

        private static int ThemeOf(int layer)
        {
            if (Get<object>("tower_theme_" + layer) is int t) return t;
            int below = layer > 0 ? ThemeOf(layer - 1) : Game.FLOOR;
            string rule = Spec.layerTheme ?? "same";
            t = rule.Equals("same", StringComparison.OrdinalIgnoreCase) ? below : SideAreas.PickTheme(below, rule);
            D["tower_theme_" + layer] = t;
            return t;
        }

        /// <summary>The ground's anomalies, plus one more hard one for every layer climbed.</summary>
        private static string[] AnomaliesFor(int layer)
        {
            if (Get<object>("tower_anoms_" + layer) is string[] s) return s;
            var list = (layer > 0 ? AnomaliesFor(layer - 1) : Get("tower_anoms_0", new string[0])).ToList();
            KindSpec k = SideKinds.Current;
            AnomalySpec extra = !Spec.extraAnomaly ? null : SideAreaData.Anomalies
                .Where(a => a.hard && a.weight > 0 && a.timer == null && !list.Contains(a.id) && Game.FLOOR >= a.minFloor && (k.exclude == null || !k.exclude.Contains(a.id, StringComparer.OrdinalIgnoreCase))
                            && !list.Any(p => (a.notWith?.Contains(p) ?? false) || (SideAreaData.Anomaly(p)?.notWith?.Contains(a.id) ?? false)))
                .OrderBy(_ => Random.value).FirstOrDefault();
            if (extra != null) list.Add(extra.id);
            s = list.ToArray();
            D["tower_anoms_" + layer] = s;
            return s;
        }

        private static void Restore(int layer, LevelSaveInfo save, Arrival how)
        {
            LevelManager lm = LevelManager.instance;
            int origin = Game.FLOOR;
            if (Get<object>("tower_state_" + layer) is Dictionary<string, object> state)
                foreach (var kv in state) D[kv.Key] = kv.Value;
            D.Remove("tower_save_" + layer);
            D.Remove("tower_state_" + layer);

            Game.Game_isLoading = true;
            SideAreas.SideDeserialize(save);
            Game.Game_isLoading = false;
            InteractiveManager.instance.Cleanup();
            lm.LoadRenderSettingsForLevel(origin);
            lm.LoadMusicForLevel(origin);

            // up a stair you come out on a stair back down, and the other way round; a lift lands on its partner
            Stair stair = !how.stair ? null : how.up ? AllStairs.FirstOrDefault(IsBackStair)
                : StairsOn.OrderBy(s => Vector3.Distance(s.myNode.transform.position, how.from)).FirstOrDefault();
            Vector3 at = stair != null ? stair.myNode.transform.position : Spot(how.up ? "lift_down" : "lift_up") ?? Player.instance.transform.position;
            NavNode node = stair?.myNode ?? Map.instance.GetNavNodeAt(at) ?? Map.instance.GetNearestNavNodeAt(at, 6f, true);
            SideAreas.MoveOccupantAway(node);
            Player.instance.transform.position = node.transform.position;
            Player.instance.GetMover().SetCurrentNavNode(node);
            if (stair != null) Player.instance.transform.rotation = stair.transform.rotation;
            Player_Control.instance.playerCamera.transform.localEulerAngles = Vector3.zero;
            Camera.main.transform.localEulerAngles = Vector3.zero;
            Player_Control.instance.ResetRotations();
            CullManager.instance.UpdateCulling();
            Menu_MiniMap.instance.Init();
            Player_AbilityControl.instance.LevelChangeComplete();
            Game.Game_isTransitioning = false;
            Game.instance.EndTurn();
            Game.instance.PostCleanup();
            SideAreas.ApplyFog(ThemeOf(layer));
            try { Echoes.OnEchoRestored(); SideKinds.OnEchoRestored(); Echoes.ReapplyPlayerEffects(); }
            catch (Exception e) { Plugin.Log.LogError("Towers: settling the layer: " + e); }
            PlaceLifts();
            Object.Instantiate(lm.warpParticle, Player.instance.GetHitFx().position, Quaternion.identity);
            AudioManager.PlayEvent("event:/Abilities/Misc/Psykonics_Warp");
            Game.LOG($"<color=#7FE0FF>{string.Format(how.up ? Spec.upText : Spec.downText, layer + 1, Count)}</color> It's as you left it.");
        }

        /// <summary>Whoever is already fighting you right beside you comes along.</summary>
        private static List<Rider> Boarders(LiftSpec l)
        {
            var list = new List<Rider>();
            if (l.ride <= 0 || l.riders <= 0) return list;
            Vector3 me = Player.instance.transform.position;
            foreach (Character c in Echoes.Enemies().Where(c => c.ai.GetState() == AI.AI_STATE.Active && !c.tags.Any(t => t?.tagName != null && t.tagName.StartsWith("SCCL"))
                                                               && Vector3.Distance(c.transform.position, me) <= l.ride)
                         .OrderBy(c => Vector3.Distance(c.transform.position, me)).Take(l.riders).ToList())
            {
                list.Add(new Rider { id = c.internalName, name = c.GetName(), health = c.GetHealth() / (float)Mathf.Max(1, c.GetHealthMax()) });
                AI_Manager.instance.RemoveCharacter(c);
                c.xpValue = 0;
                c.AddTag("NOXP");
                c.GetMover().GetCurrentNavNode()?.ClearOccupant();
                Object.Destroy(c.gameObject);
            }
            return list;
        }

        private static void Board(List<Rider> riders, Arrival how)
        {
            if (riders.Count == 0) return;
            NavNode me = Player.instance.GetMover().GetCurrentNavNode();
            var spots = me.GetLinks().Where(n => n != null && !n.IsOccupied() && !n.HasDoor()).ToList();
            int came = 0;
            foreach (Rider r in riders)
            {
                NavNode n = spots.FirstOrDefault() ?? Map.instance.GetClosestNode(me, 6f, validNodesOnly: true);
                Character prefab = EnemyRegistry.Resolve(r.id);
                if (n == null || prefab == null) continue;
                spots.Remove(n);
                Character c = AI_Manager.instance.Spawn_Character(prefab, n.transform.position, AI.AI_TEAM.Hostile);
                if (c == null) continue;
                c.SetName(r.name);
                c.SetHealth(Mathf.Max(1, Mathf.RoundToInt(c.GetHealthMax() * r.health)), c.GetHealthMax());
                Echoes.Hunt(c);
                came++;
            }
            LastCame = came;
            if (came > 0) Game.LOG($"<color=#FF8F5C>{came} of them came {(how.up ? "up" : "down")} {(how.stair ? "the stairs" : "in the lift")} with you.</color>");
        }

        // ---- reload and leaving -------------------------------------------------------------------------------

        /// <summary>A reloaded layer: its lifts, and a lock whose overseer died before the save.</summary>
        internal static void OnEchoRestored()
        {
            if (!Active) return;
            D.Remove("lift_call");
            PlaceLifts();
            Plugin.Instance.StartCoroutine(CheckLockSoon());
        }

        private static IEnumerator CheckLockSoon()
        {
            yield return new WaitForSeconds(0.5f);
            if (Active && Locked && !Contracts.AllCharacters().Any(c => !c.GetIsDead() && c.HasTag(KeyTag))) Unlock(null);
        }

        internal static void OnReturn() => Clear();

        private static void Clear()
        {
            if (D == null) return;
            foreach (string k in D.Keys.Where(k => k.StartsWith("tower_") || k.StartsWith("lift_")).ToList()) D.Remove(k);
        }

        internal static string TrackerLine()
        {
            if (!Active) return null;
            string s = $"{SideKinds.Tinted(SideKinds.Current)}: layer {At + 1}/{Count}";
            if (OnTop) return s + ", <color=#FFD966>the top</color>";
            if (Get<object>("lift_call") is int[] c) return s + $", <color={Spec.lift.color}>lift coming {c[1]}/{Spec.lift.callTurns}</color>";
            return s + ", " + WaysUp();
        }
    }
}
