using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// Optional side areas. A floor may get a breach pad; stepping onto it opens an "echo" of a neighbouring floor's
    /// theme, scaled to the current floor (Game.FLOOR never changes). The way out leads back to the breach, which stays
    /// open: going in again finds the side area as you left it ("kept_N"), unless it collapsed with you inside. Story
    /// and boss rooms in the echo are sealed so nothing there can move the real story on.
    ///
    /// State lives in one extra slot at the end of LevelManager.levelData.levels, holding only mscorlib and game types,
    /// so the save still loads without the mod (only a save made inside an echo would put you in the wrong place).
    /// </summary>
    internal static class SideAreas
    {
        internal static ConfigEntry<int> Chance;
        internal static ConfigEntry<int> CacheItems;
        internal static ConfigEntry<int> EliteChance;
        internal static ConfigEntry<bool> ConfirmLeave;
        internal static ConfigEntry<int> Brightness;

        /// <summary>[SideAreas] Brightness as a factor for a generated place's ambient and ceiling lights.</summary>
        internal static float BrightnessScale => Brightness != null ? Mathf.Clamp(Brightness.Value, 25, 300) / 100f : 1f;

        private static bool busy;

        private static int BreachChance => Tweaks.Int("breachChance", Chance.Value);

        internal static void Bind(ConfigFile cfg)
        {
            Chance = cfg.Bind("SideAreas", "BreachChance", 60, "Percent chance that a newly generated floor gets a breach to a side area (0 turns them off).");
            CacheItems = cfg.Bind("SideAreas", "BonusCacheItems", 3, "Extra items waiting somewhere in each side area.");
            ConfirmLeave = cfg.Bind("SideAreas", "ConfirmLeave", true, "Ask before a way out takes you back to the real floor (any job is turned in as it stands).");
            Brightness = cfg.Bind("SideAreas", "Brightness", 100, new ConfigDescription(
                "Percent brightness of the generated side areas' lights (100 = as the places are made; a blackout still goes dark).", new AcceptableValueRange<int>(25, 300)));
            EliteChance = cfg.Bind("SideAreas", "EliteChance", 12, "Percent chance that an enemy in a side area is an elite (an affix, more health, maybe a drop). Kinds can set their own.");
        }

        // ---- saved state --------------------------------------------------------------------------------------

        internal static Dictionary<string, object> Data
        {
            get
            {
                LevelManager lm = LevelManager.instance;
                if (lm == null || lm.levelData == null || lm.levelData.levels == null) return null;
                int slot = lm.levels.Length;
                object[] arr = lm.levelData.levels;
                if (arr.Length > slot && arr[slot] is Dictionary<string, object> d) return d;
                if (arr.Length <= slot)
                {
                    Array.Resize(ref arr, slot + 1);
                    lm.levelData.levels = arr;
                }
                d = new Dictionary<string, object>();
                arr[slot] = d;
                return d;
            }
        }

        private static T Get<T>(string key, T fallback = default)
        {
            var d = Data;
            return d != null && d.TryGetValue(key, out object v) && v is T t ? t : fallback;
        }

        private static void Set(string key, object value)
        {
            var d = Data;
            if (d != null) d[key] = value;
        }

        /// <summary>The player is in an echo right now (false in the Apartment, which has its own floor id).</summary>
        internal static bool InSide => Get("side_active", false) && Get("side_origin", -999) == Game.FLOOR;

        private static int Theme => Get("side_theme", Game.FLOOR);

        internal static int Visits => Get("visits", 0);

        // ---- floors and themes ---------------------------------------------------------------------------------

        private static bool IsStoryEnd(int floor)
        {
            Level l = LevelManager.instance.GetLevel(floor);
            return l == null || l is Level_TheEnd;
        }

        private static bool HasMaps(int floor) =>
            floor >= 0 && floor < Atlas.instance.maps.Length && Atlas.instance.maps[floor]?.maps != null && Atlas.instance.maps[floor].maps.Any(m => m != null);

        private static bool ThemeOk(int f) => HasMaps(f) && !IsStoryEnd(f);

        /// <summary>Which floor's maps an echo borrows, by the kind's "theme" rule (near, deeper, shallower, any, same, floor:N).</summary>
        internal static int PickTheme(int origin, string rule = "near")
        {
            rule = (rule ?? "near").ToLowerInvariant();
            IEnumerable<int> order;
            if (rule.StartsWith("floor:") && int.TryParse(rule.Substring(6), out int fixedFloor)) order = new[] { fixedFloor - 1 };
            else switch (rule)
            {
                case "deeper": order = new[] { origin + 1, origin + 2, origin + 3 }; break;
                case "shallower": order = new[] { origin - 1, origin - 2, origin - 3 }; break;
                case "same": order = new[] { origin }; break;
                case "any": order = Enumerable.Range(0, Atlas.instance.maps.Length).Where(f => f != origin).OrderBy(_ => UnityEngine.Random.value).ToList(); break;
                default: order = new int[0]; break;
            }
            foreach (int f in order.Concat(new[] { origin + 1, origin - 1, origin + 2, origin }))
                if (ThemeOk(f)) return f;
            return origin;
        }

        private static bool FloorCanHaveBreach(int floor) =>
            floor >= 0 && floor < 100 && Map.instance != null && Map.instance.internalName != "Tutorial_Map"
            && Map.instance.internalName != "Apartment" && !IsStoryEnd(floor) && PickTheme(floor) != floor;

        /// <summary>The floor whose maps this floor's breach leads into: picked once, so the board can name it.</summary>
        internal static int ThemeOf(int floor)
        {
            if (Get<object>("theme_" + floor) is int t) return t;
            t = PickTheme(floor, SideKinds.KindOf(floor).theme);
            Set("theme_" + floor, t);
            return t;
        }

        /// <summary>How many layers deep you are (1 = through the floor's breach, 2+ = through rifts inside).</summary>
        internal static int Depth => Get("side_depth", 1);

        // ---- the breach pad -------------------------------------------------------------------------------------

        internal static readonly List<Breach> Pads = new List<Breach>();

        internal class Breach : MonoBehaviour
        {
            public bool isReturn;
            /// <summary>A rift inside a side area that leads one layer deeper.</summary>
            public bool isDeeper;
        }

        internal static void DecideBreach(int floor)
        {
            if (Get<object>("breach_" + floor) != null || !FloorCanHaveBreach(floor)) return;
            if (LobbyDoors() != null) { if (!liftPending) Plugin.Instance.StartCoroutine(PlaceLobbyLift(floor)); return; }
            if (UnityEngine.Random.Range(0, 100) >= BreachChance) { Set("breach_" + floor, "none"); return; }
            KindSpec kind = SideKinds.Roll(floor);
            // On a new game the first floor is built before the player has a mover, so there is no current room yet.
            Room here = Player.instance != null && Player.instance.GetMover() != null && Player.instance.GetMover().GetCurrentNavNode() != null
                ? Player.instance.GetCurrentRoom() : null;
            NavNode node = RandomFreeNode(avoid: here);
            if (node == null) { Set("breach_" + floor, "none"); return; }
            Vector3 p = node.transform.position;
            Set("breach_" + floor, new[] { p.x, p.y, p.z });
            PlacePad(p, isReturn: false, kind.color);
            Plugin.Log.LogInfo($"Side areas: {kind.id} breach on floor {floor} at {p}");
        }

        private static bool liftPending;

        /// <summary>Where the first floor's lobby lifts stand (their floor-number displays), or null when there are none.</summary>
        private static List<Vector3> LobbyDoors()
        {
            if (Game.FLOOR != 0 || Map.instance == null) return null;
            var lobby = Map.instance.GetRooms().OfType<Room_Event_Offices_Lobby>().FirstOrDefault();
            var doors = lobby?.elevatorDisplays?.Where(d => d != null).Select(d => d.transform.position).ToList();
            return doors != null && doors.Count > 0 ? doors : null;
        }

        /// <summary>
        /// The first floor's lobby has a bank of lifts that never went anywhere. One of them still runs: floor 1
        /// always gets its breach, on the free tile in front of a lift. The map is still switched off while it is
        /// set up, so this waits until play has started to find that tile (no tile: the usual chance and spot).
        /// </summary>
        private static IEnumerator PlaceLobbyLift(int floor)
        {
            liftPending = true;
            try
            {
                // the player can exist before the map's rooms have their tiles: wait for both
                for (int i = 0; i < 120 && (Game.Game_isLoading || Player.instance == null || Player.instance.GetMover()?.GetCurrentNavNode() == null
                                            || Map.instance == null || !Map.instance.GetRooms().Any(r => r != null && r.roomNodes.Count > 0)); i++)
                    yield return new WaitForSeconds(0.25f);
                yield return new WaitForSeconds(0.5f);
                if (Game.FLOOR != floor || Get<object>("breach_" + floor) != null || Map.instance == null) yield break;
                NavNode node = LobbyLift();
                if (node == null)
                {
                    if (UnityEngine.Random.Range(0, 100) >= BreachChance) { Set("breach_" + floor, "none"); yield break; }
                    node = RandomFreeNode(Player.instance.GetCurrentRoom());
                    if (node == null) { Set("breach_" + floor, "none"); yield break; }
                }
                else Plugin.Instance.StartCoroutine(SayLiftWorks());
                KindSpec kind = SideKinds.Roll(floor);
                Vector3 p = node.transform.position;
                Set("breach_" + floor, new[] { p.x, p.y, p.z });
                PlacePad(p, isReturn: false, kind.color);
                Plugin.Log.LogInfo($"Side areas: {kind.id} breach on floor {floor} at {p} (lobby lift)");
                BreachFinder.Refresh();
            }
            finally { liftPending = false; }
        }

        private static NavNode LobbyLift()
        {
            var doors = LobbyDoors();
            if (doors == null) return null;
            // the map's own rooms hold its tiles (FindObjectsOfType misses them: they sit on inactive objects)
            var free = Map.instance.GetRooms().Where(r => r != null).SelectMany(r => r.roomNodes).Where(n => n != null && !n.IsOccupied() && !n.HasDoor() && !n.hazards.Any()
                && !n.Flag_isHidden && Map.instance.mapStairs.All(st => st == null || st.myNode != n)).Distinct().ToList(); // (GetStairAt returns the nearest stair at any distance)
            float Flat(NavNode n, Vector3 d) => Vector2.Distance(new Vector2(n.transform.position.x, n.transform.position.z), new Vector2(d.x, d.z));
            var picks = doors.Select(d => (door: d, node: free.OrderBy(n => Flat(n, d)).FirstOrDefault())).Where(x => x.node != null && Flat(x.node, x.door) < 2.5f).ToList();
            Plugin.Log.LogInfo($"Side areas: lobby lifts at {string.Join(", ", doors.Select(d => $"({d.x:0.#}, {d.z:0.#})"))}; {free.Count} free tiles on the map, nearest per lift "
                + string.Join(", ", doors.Select(d => free.Count == 0 ? "-" : free.Min(n => Flat(n, d)).ToString("0.0"))) + $"; {picks.Count} usable");
            if (picks.Count == 0) return null;
            return picks[UnityEngine.Random.Range(0, picks.Count)].node;
        }

        private static IEnumerator SayLiftWorks()
        {
            for (int i = 0; i < 20 && (Game.Game_isLoading || Player.instance == null || Player.instance.GetMover()?.GetCurrentNavNode() == null); i++)
                yield return new WaitForSeconds(0.5f);
            yield return new WaitForSeconds(1f);
            Game.LOG("<color=#D070FF>One of the lobby lifts still runs. Its doors stand open on a floor that isn't on the panel.</color>");
        }

        private static void RestoreBreach(int floor)
        {
            if (!(Get<object>("breach_" + floor) is float[] p)) return;
            PlacePad(new Vector3(p[0], p[1], p[2]), isReturn: false, SideKinds.KindOf(floor).color);
        }

        internal static NavNode RandomFreeNode(Room avoid)
        {
            var rooms = Map.instance.GetRooms().Where(r => r != null && r != avoid && !r.Properties.IsSecret && !r.Properties.IsBossRoom
                                                           && !r.Properties.IsHallway && !(r is Room_Event) && r.RandomNodeCheck()).ToList();
            for (int tries = 0; tries < 20 && rooms.Count > 0; tries++)
            {
                NavNode n = rooms[UnityEngine.Random.Range(0, rooms.Count)].GetRandomNode();
                if (n != null && !n.IsOccupied() && !n.HasDoor()) return n;
            }
            return null;
        }

        internal static void PlaceBreach(Vector3 pos, string color) => PlacePad(pos, isReturn: false, color);

        private static void PlacePad(Vector3 pos, bool isReturn, string color = null, bool isDeeper = false)
        {
            TeleportPad pad = UnityEngine.Object.Instantiate(Game.instance.TELEPORTPAD_PREFAB, pos, Quaternion.identity);
            pad.name = isReturn ? "SCCL_BreachReturn" : isDeeper ? "SCCL_BreachDeeper" : "SCCL_Breach";
            pad.transform.SetParent(Map.instance.transform); // not a registered pad, so the map never saves it
            pad.color_clear = isReturn ? new Color(0.3f, 1f, 0.6f)
                : !string.IsNullOrEmpty(color) && ColorUtility.TryParseHtmlString(color, out Color c) ? c : new Color(0.85f, 0.2f, 1f);
            if (pad.lineRenderer != null) UnityEngine.Object.Destroy(pad.lineRenderer);
            foreach (ParticleSystem ps in pad.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.startColor = pad.color_clear; // so a breach never looks like an ordinary blue pad
            }
            var marker = pad.gameObject.AddComponent<Breach>();
            marker.isReturn = isReturn;
            marker.isDeeper = isDeeper;
            Pads.RemoveAll(x => x == null);
            Pads.Add(marker);
            pad.Init();
            Plugin.Instance.StartCoroutine(Tint(pad));
        }

        private static IEnumerator Tint(TeleportPad pad)
        {
            yield return null;
            if (pad != null) pad.SetClear();
        }

        // ---- travel -------------------------------------------------------------------------------------------

        private static IEnumerator WaitForStill(NavNode node)
        {
            for (float t = 0; t < 5f && (Game.RESOLVING || Game.LOCK || Player.instance.GetMover().isMoving); t += Time.unscaledDeltaTime)
                yield return null;
        }

        internal static IEnumerator Enter(NavNode padNode)
        {
            if (busy) yield break;
            busy = true;
            yield return WaitForStill(padNode);
            if (Player.instance == null || Player.instance.GetIsDead() || Player.instance.GetMover().GetCurrentNavNode() != padNode || InSide)
            {
                busy = false;
                yield break;
            }
            HUD.instance.DoLoadingScreen();
            yield return null;
            try { DoEnter(); }
            catch (Exception e)
            {
                // half built: go straight back to the real floor rather than leave the game stuck loading
                Plugin.Log.LogError("Side areas: entering failed, going back to the real floor: " + e);
                try { if (InSide) DoReturn(keep: false); }
                catch (Exception e2) { Plugin.Log.LogError("Side areas: and going back failed: " + e2); }
                Unstick();
            }
            busy = false;
        }

        private static void DoEnter()
        {
            LevelManager lm = LevelManager.instance;
            int origin = Game.FLOOR, theme = ThemeOf(origin);
            Transform pt = Player.instance.transform;
            bool endless = Endless.Is(origin);
            var kept = KeptAt(origin);
            Data?.Remove("spent_" + origin); // older saves marked a used breach as gone for good
            Set("side_origin", origin);
            Set("side_return", new[] { pt.position.x, pt.position.y, pt.position.z, pt.rotation.x, pt.rotation.y, pt.rotation.z, pt.rotation.w });
            if (kept == null)
            {
                Set("side_theme", theme);
                Set("side_depth", endless ? Endless.TakeStartDepth() : 1);
                Set("echo_kind", SideKinds.KindOf(origin).id);
                Set("visits", Visits + 1);
            }

            Free();
            Game.Game_isTransitioning = true;
            Player_AbilityControl.instance.LevelChangeStart();
            Player.instance.GetMover().GetCurrentNavNode()?.ClearOccupant();
            lm.SerializeLevel(origin); // not in the echo yet, so this is the normal save of the real floor
            lm.ClearLevel();
            Menu_MiniMap.instance.Reset();
            Set("side_active", true);
            if (kept != null) Reenter(origin, kept);
            else Build(origin, theme, firstLayer: true);
        }

        // ---- going back in --------------------------------------------------------------------------------------

        /// <summary>The keys that belong to one visit (its layer, tower, objective, vault, anomalies, occupier) and stay with it.</summary>
        private static readonly string[] VisitKeys = { "side_depth", "side_theme", "side_arrival", "side_revisit", "echo_kind", "echo_anoms", "echo_turn", "collapse_turns", "deeper_pos", "echo_owner", "echo_angered", "enc" };

        private static bool VisitKey(string k) =>
            VisitKeys.Contains(k) || (k.StartsWith("obj_") && k != "obj_total") || k.StartsWith("timer_") || k.StartsWith("hunted_") || k.StartsWith("alarm_")
            || k.StartsWith("lift_") || k.StartsWith("vault_") || k.StartsWith("tower_");

        /// <summary>The side area left behind this floor's breach ({"level": LevelSaveInfo, "state": its keys}), or null for a fresh one.</summary>
        private static Dictionary<string, object> KeptAt(int floor) =>
            !Endless.Is(floor) && Get<object>("kept_" + floor) is Dictionary<string, object> k && k.TryGetValue("level", out object l) && l is LevelSaveInfo ? k : null;

        internal static bool HasKept(int floor) => KeptAt(floor) != null;

        /// <summary>This visit went back into a side area already left once (what happens once per breach, like favour, doesn't again).</summary>
        internal static bool Revisit => InSide && Get("side_revisit", false);

        /// <summary>Back through a breach you've used: the layer you left, as you left it, and you arrive where you first did.</summary>
        private static void Reenter(int origin, Dictionary<string, object> kept)
        {
            LevelManager lm = LevelManager.instance;
            Data.Remove("kept_" + origin);
            if (kept.TryGetValue("state", out object s) && s is Dictionary<string, object> state)
                foreach (var kv in state) Data[kv.Key] = kv.Value;
            Set("side_revisit", true);

            Game.Game_isLoading = true;
            try { SideDeserialize((LevelSaveInfo)kept["level"]); }
            finally { Game.Game_isLoading = false; }
            Places.Finish(fresh: false);
            InteractiveManager.instance.Cleanup();
            lm.LoadRenderSettingsForLevel(origin);
            lm.LoadMusicForLevel(origin);

            Vector3? at = Get<object>("side_arrival") is float[] a ? new Vector3(a[0], a[1], a[2])
                : kept.TryGetValue("left", out object l) && l is float[] b ? new Vector3(b[0], b[1], b[2]) : (Vector3?)null;
            NavNode node = (at is Vector3 v ? Map.instance.GetNavNodeAt(v) ?? Map.instance.GetNearestNavNodeAt(v, 6f, true) : null) ?? RandomFreeNode(null);
            MoveOccupantAway(node);
            Player.instance.transform.position = node.transform.position;
            // the arrival tile can hold a place's way out: landing there mustn't count as stepping onto it
            Player.instance.isTeleporting = true;
            try { Player.instance.GetMover().SetCurrentNavNode(node); }
            finally { Player.instance.isTeleporting = false; }
            node.SetOccupant(Player.instance);
            Player.CURRENT_ROOM = node.GetRoom();
            node.GetRoom()?.Discover();
            Player_Control.instance.playerCamera.transform.localEulerAngles = Vector3.zero;
            Camera.main.transform.localEulerAngles = Vector3.zero;
            Player_Control.instance.ResetRotations();
            CullManager.instance.UpdateCulling();
            EnsureExit();
            RestoreDeeper();
            Menu_MiniMap.instance.Init();
            Places.NameOnMap();
            Player_AbilityControl.instance.LevelChangeComplete();
            Game.Game_isTransitioning = false;
            Game.instance.EndTurn();
            Game.instance.PostCleanup();
            ApplyFog(Theme);
            try { Echoes.OnEchoRestored(); Contracts.OnEchoRestored(); SideKinds.OnEchoRestored(); SideLayers.OnEchoRestored(); Echoes.ReapplyPlayerEffects(); }
            catch (Exception e) { Plugin.Log.LogError("Side areas: settling the kept side area: " + e); }
            PruneStairs();
            BreachFinder.Refresh();
            UnityEngine.Object.Instantiate(lm.warpParticle, Player.instance.GetHitFx().position, Quaternion.identity);
            AudioManager.PlayEvent("event:/Abilities/Misc/Psykonics_Warp");
            KindSpec kind = SideKinds.Current;
            Game.LOG($"<color=#D070FF>You step back through the breach into the {kind.name.ToLowerInvariant()}{(Depth > 1 ? $", layer {Depth}" : "")}.</color> It's as you left it.");
            Plugin.Log.LogInfo($"Side areas: back into kept {kind.id} (layer {Depth}) of floor {Theme} ({Map.instance.internalName}) from floor {origin}");
        }

        /// <summary>A rift inside a side area: the echo you're in is dropped and a deeper one built in its place.</summary>
        internal static IEnumerator Dive(NavNode padNode)
        {
            if (busy || !InSide) yield break;
            busy = true;
            yield return WaitForStill(padNode);
            if (Player.instance == null || Player.instance.GetIsDead() || Player.instance.GetMover().GetCurrentNavNode() != padNode)
            {
                busy = false;
                yield break;
            }
            HUD.instance.DoLoadingScreen();
            yield return null;
            try { DoDive(); }
            catch (Exception e) { Plugin.Log.LogError("Side areas: diving failed: " + e); }
            busy = false;
        }

        private static void DoDive()
        {
            LevelManager lm = LevelManager.instance;
            int origin = Game.FLOOR, depth = Depth + 1;
            bool endless = Endless.Here;
            int theme = PickTheme(Theme, endless ? "any" : "deeper");
            try { SideKinds.OnLeaveLayer(); Echoes.OnReturn(); }
            catch (Exception e) { Plugin.Log.LogError("Side areas: leaving the layer: " + e); }
            Data?.Remove("deeper_pos");
            Set("side_depth", depth);
            Set("side_theme", theme);
            Set("visits", Visits + 1);
            if (endless) Endless.Reroll(origin, depth); // every depth of the Ichor Deep is a fresh layer
            else
            {
                // the deeper layer keeps what was wrong with this one and adds one more hard anomaly
                var anoms = Echoes.Rolled(origin).ToList();
                AnomalySpec extra = SideAreaData.Anomalies.Where(a => a.hard && a.weight > 0 && !anoms.Contains(a.id) && a.timer == null)
                    .OrderBy(_ => UnityEngine.Random.value).FirstOrDefault();
                if (extra != null) anoms.Add(extra.id);
                Set("anoms_" + origin, anoms.ToArray());
            }

            Game.Game_isTransitioning = true;
            Player_AbilityControl.instance.LevelChangeStart();
            Player.instance.GetMover().GetCurrentNavNode().ClearOccupant();
            lm.ClearLevel();
            Menu_MiniMap.instance.Reset();
            Build(origin, theme, firstLayer: false);
        }

        internal static void Build(int origin, int theme, bool firstLayer)
        {
            LevelManager lm = LevelManager.instance;

            KindSpec kindHere = SideKinds.KindOf(origin);
            SideLayers.Begin(kindHere, theme, firstLayer);
            // a generated place of the kind's own, or (kinds without one) an echo of a real floor's map
            PlaceSpec place = Endless.Here && !Endless.UsePlace() ? null : Places.Pick(kindHere, origin, firstLayer);
            Map map;
            if (place != null) map = Places.Create(place, kindHere, origin, theme);
            else
            {
                Map[] pool = Atlas.instance.maps[theme].maps.Where(m => m != null).ToArray();
                map = UnityEngine.Object.Instantiate(SideLayers.PickMap(pool) ?? pool[UnityEngine.Random.Range(0, pool.Length)]);
            }
            map.floor = origin; // spawns, loot and traps stay at the real floor's difficulty
            map.Init();
            if (place != null) Places.Finish(fresh: true);
            map.Cleanup();
            InteractiveManager.instance.Cleanup();
            lm.LoadRenderSettingsForLevel(origin); // both redirected to the theme while in the echo
            lm.LoadMusicForLevel(origin);
            SealStoryRooms();
            SpawnCache();

            NavNode node;
            Quaternion rot = Quaternion.identity;
            Stair stair = Map.instance.mapStairs.Where(s => s != null && s.myNode != null).OrderBy(_ => UnityEngine.Random.value).FirstOrDefault();
            if (stair != null) { node = stair.myNode; rot = stair.transform.rotation; }
            else node = RandomFreeNode(null);
            ClearArrivalRoom(node.GetRoom());
            MoveOccupantAway(node);
            Player.instance.transform.position = node.transform.position;
            Player.instance.GetMover().SetCurrentNavNode(node);
            Player.instance.transform.rotation = rot;
            Player_Control.instance.playerCamera.transform.localEulerAngles = Vector3.zero;
            Camera.main.transform.localEulerAngles = Vector3.zero;
            Player_Control.instance.ResetRotations();
            // where a return trip lands: beside the way out (a tower's ground layer keeps its own)
            if (firstLayer || !SideLayers.Active) Set("side_arrival", new[] { node.transform.position.x, node.transform.position.y, node.transform.position.z });
            EnsureExit();
            Menu_MiniMap.instance.Init();
            Places.NameOnMap();
            Player_AbilityControl.instance.LevelChangeComplete();
            Game.Game_isTransitioning = false;
            Game.instance.EndTurn();
            Game.instance.PostCleanup();
            ApplyFog(theme);
            UnityEngine.Object.Instantiate(lm.warpParticle, Player.instance.GetHitFx().position, Quaternion.identity);
            AudioManager.PlayEvent("event:/Abilities/Misc/Psykonics_Warp");
            KindSpec kind = SideKinds.KindOf(origin);
            if (place != null)
                Game.LOG(firstLayer
                    ? $"<color=#D070FF>You slip through the breach into {SideKinds.Article(kind)}: {place.name}.</color> {place.text ?? ""} {kind.arrival ?? "The way you came in is the way out."}"
                    : $"<color=#FF4AD8>The rift drops you a layer deeper (layer {Depth}), into {place.name}.</color> {place.text ?? ""} The way out here still leads back to the real floor.");
            else Game.LOG(firstLayer
                ? $"<color=#D070FF>You slip through the breach into {SideKinds.Article(kind)} of floor {theme + 1}.</color> {kind.arrival ?? "The green pad where you came in leads back."}"
                : SideLayers.ArrivalLine() ?? $"<color=#FF4AD8>The rift drops you a layer deeper (layer {Depth}), into an echo of floor {theme + 1}.</color> The green pad still leads back to the real floor.");
            Plugin.Log.LogInfo($"Side areas: entered {kind.id} (layer {Depth}) of floor {theme} ({Map.instance.internalName}) from floor {origin}");
            try { Endless.OnLayerBuilt(); }
            catch (Exception e) { Plugin.Log.LogError("Ichor Deep: " + e); }
            try { Echoes.OnEchoBuilt(origin); }
            catch (Exception e) { Plugin.Log.LogError("Echoes: " + e); }
            if (firstLayer) Contracts.OnEchoBuilt();
            try { PlaceDeeper(kind); }
            catch (Exception e) { Plugin.Log.LogError("Side areas: rift: " + e); }
            try { SideLayers.OnLayerBuilt(); }
            catch (Exception e) { Plugin.Log.LogError("Towers: " + e); }
            PruneStairs();
            BreachFinder.Refresh();
        }

        /// <summary>Kinds with "deeper" get a rift somewhere far from the arrival point, layer after layer (never during a job).</summary>
        private static void PlaceDeeper(KindSpec kind)
        {
            if (kind.deeper <= 0 || Depth > kind.deeper || Contracts.Job != null) return;
            Room here = Player.instance.GetCurrentRoom();
            NavNode n = Echoes.Rooms(here).Where(r => r != Echoes.VaultRoom).OrderByDescending(r => Vector3.Distance(r.transform.position, Player.instance.transform.position))
                .Take(4).OrderBy(_ => UnityEngine.Random.value).Select(r => r.GetRandomNode()).FirstOrDefault(x => x != null && !x.IsOccupied() && !x.HasDoor());
            if (n == null) return;
            Vector3 p = n.transform.position;
            Set("deeper_pos", new[] { p.x, p.y, p.z });
            PlacePad(p, isReturn: false, "#FF4AD8", isDeeper: true);
            Game.LOG("<color=#FF4AD8>Somewhere in here the echo wears thin: a rift to a deeper layer, worse and richer.</color>");
        }

        /// <summary>A rift cleared by something landing on its tile comes back (checked every turn in a side area).</summary>
        internal static void KeepPads()
        {
            if (!InSide || busy) return;
            if (Get<object>("deeper_pos") is float[] p && !Pads.Any(x => x != null && x.isDeeper)) RestoreDeeper();
            if (!Pads.Any(x => x != null && x.isReturn)) EnsureExit();
        }

        private static void RestoreDeeper()
        {
            if (Get<object>("deeper_pos") is float[] p) PlacePad(new Vector3(p[0], p[1], p[2]), isReturn: false, "#FF4AD8", isDeeper: true);
        }

        /// <summary>
        /// Out to the real floor. The side area waits behind the breach for next time; with <paramref name="keep"/> off
        /// (it broke) the next trip is a fresh one, and when it <paramref name="collapse"/>s the breach goes with it.
        /// </summary>
        internal static IEnumerator Return(bool collapse = false, bool keep = true)
        {
            if (busy || !InSide) yield break;
            busy = true;
            HUD.instance.DoLoadingScreen();
            yield return null;
            try { DoReturn(collapse, keep); }
            catch (Exception e)
            {
                Plugin.Log.LogError("Side areas: returning failed: " + e);
                Unstick();
            }
            busy = false;
        }

        private static void DoReturn(bool collapse = false, bool keep = true)
        {
            LevelManager lm = LevelManager.instance;
            int origin = Game.FLOOR;
            // the Ichor Deep is fresh every time and never collapses, so only the others keep what you leave behind
            bool endless = Endless.Here;
            collapse &= !endless;
            keep &= !collapse && !endless && Data != null;
            var state = keep ? Data.Where(kv => VisitKey(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value) : null;
            Vector3 left = Player.instance.transform.position;
            if (!keep) Data?.Remove("kept_" + origin);
            try { Contracts.OnReturn(); }
            catch (Exception e) { Plugin.Log.LogError("Factions: turning in: " + e); }
            try { SideKinds.OnReturn(); }
            catch (Exception e) { Plugin.Log.LogError("Side area kind: leaving: " + e); }
            try { Echoes.OnReturn(); }
            catch (Exception e) { Plugin.Log.LogError("Echoes: leaving: " + e); }
            SideLayers.OnReturn();
            Free();
            Game.Game_isTransitioning = true;
            Player_AbilityControl.instance.LevelChangeStart();
            if (keep)
            {
                Player.instance.GetMover().GetCurrentNavNode()?.ClearOccupant();
                try { Set("kept_" + origin, new Dictionary<string, object> { { "level", SideSerialize() }, { "state", state }, { "left", new[] { left.x, left.y, left.z } } }); }
                catch (Exception e) { Plugin.Log.LogError("Side areas: keeping the side area for next time: " + e); Data.Remove("kept_" + origin); }
            }
            lm.ClearLevel();
            Menu_MiniMap.instance.Reset();
            Set("side_active", false);
            Data?.Remove("side_level");
            Data?.Remove("deeper_pos");
            Data?.Remove("side_depth");
            Data?.Remove("side_arrival");
            Data?.Remove("side_revisit");
            if (collapse) Set("breach_" + origin, "none"); // so the real floor comes back without it, and never rolls another

            Game.Game_isLoading = true;
            lm.DeserializeLevel(origin);
            Game.Game_isLoading = false;
            InteractiveManager.instance.Cleanup();
            lm.LoadRenderSettingsForLevel(origin);
            lm.LoadMusicForLevel(origin);

            float[] r = Get<float[]>("side_return");
            Vector3 pos = new Vector3(r[0], r[1], r[2]);
            Quaternion rot = new Quaternion(r[3], r[4], r[5], r[6]);
            Plugin.Log.LogInfo($"Side areas: back on floor {origin}: map {(Map.instance != null ? Map.instance.internalName : "NONE")}, player {(Player.instance != null)}, camera {(Player_Control.instance?.playerCamera != null)}");
            NavNode node = Map.instance.GetNavNodeAt(pos) ?? Map.instance.GetNearestNavNodeAt(pos, 6f, true);
            if (node != null) pos = node.transform.position;
            MoveOccupantAway(node);
            Player.instance.transform.position = pos;
            Player.instance.GetMover().SetCurrentNavNode(node);
            CullManager.instance.UpdateCulling();
            Player.instance.transform.rotation = rot;
            Player_Control.instance.playerCamera.transform.rotation = rot;
            Player_Control.instance.ResetRotations();
            Player_AbilityControl.instance.LevelChangeComplete();
            Menu_MiniMap.instance.Reset();
            Game.Game_isTransitioning = false;
            Game.instance.EndTurn();
            Game.instance.PostCleanup();
            Menu_MiniMap.instance.Init();
            lm.GetLevel(origin)?.OnPlayerEntered();
            UnityEngine.Object.Instantiate(lm.warpParticle, Player.instance.GetHitFx().position, Quaternion.identity);
            AudioManager.PlayEvent("event:/Abilities/Misc/Psykonics_Warp");
            Game.LOG(collapse ? "<color=#D070FF>The breach collapses behind you.</color>"
                : $"<color=#D070FF>The breach stays open behind you.</color> <color=#9FB7C9>Step on it again to go back in{(keep ? "; it'll be as you left it" : "")}.</color>");
            BreachFinder.Refresh();
            Plugin.Log.LogInfo($"Side areas: returned to floor {origin}");
        }

        /// <summary>
        /// Before the map goes away: a scene may have carried the player on something that's part of it (the
        /// Excavation turbolift) and locked their moves; clearing the map would take the player with it.
        /// </summary>
        internal static void Free()
        {
            Player p = Player.instance;
            if (p == null) return;
            if (p.transform.parent != null) p.transform.SetParent(null, worldPositionStays: true);
            try { Player_Control.instance.SetMoveLock(false); } catch { }
            try { Player_AbilityControl.instance.LockAbilityUse(false); } catch { }
        }

        /// <summary>
        /// The Excavation floor's arrival room plays the turbolift ride down and marks the run as having seen it. In an
        /// echo neither belongs: no ride, no locks, no story tags; the lift just stands open.
        /// </summary>
        [HarmonyPatch(typeof(Room_Event_Excavation), "Start")]
        private static class Patch_ExcavationLift
        {
            private static bool Prefix(Room_Event_Excavation __instance)
            {
                if (!InSide) return true;
                try
                {
                    __instance.gameObject.name = __instance.PrefabGroupName;
                    if (__instance.laserDoor != null) __instance.laserDoor.SetActive(false);
                    if (__instance.turboliftAnim != null) __instance.turboliftAnim.enabled = false;
                    if (__instance.detonatorEventTrigger != null) __instance.detonatorEventTrigger.SetActive(false);
                    Door d = __instance.doorNode != null ? __instance.doorNode.GetDoor() : null;
                    if (d != null) { d.SetSeal(false); d.Unlock(); }
                    if (__instance.introCrate != null) __instance.introCrate.Deactivate();
                }
                catch (Exception e) { Plugin.Log.LogWarning("Side areas: settling the turbolift room: " + e.Message); }
                return false;
            }
        }

        /// <summary>Whatever went wrong mid-transition, the game must not stay in its loading state.</summary>
        internal static void Unstick()
        {
            Game.Game_isLoading = false;
            Game.Game_isTransitioning = false;
            try { Player_AbilityControl.instance.LevelChangeComplete(); } catch { }
            try { HUD.instance?.ResetLoadingScreen(); } catch { }
        }

        /// <summary>
        /// Echoes borrow whole maps, quest crates included; one that can't find its turn-in spot on the borrowed map
        /// would stop the rest of the map from being set up. In an echo it's simply switched off.
        /// </summary>
        [HarmonyPatch(typeof(InteractiveObject_QuestCrate), nameof(InteractiveObject_QuestCrate.Init))]
        private static class Patch_QuestCrate
        {
            private static Exception Finalizer(Exception __exception, InteractiveObject_QuestCrate __instance)
            {
                if (__exception == null || !InSide) return __exception;
                Plugin.Log.LogWarning($"Side areas: a quest crate couldn't set up in the echo and was left out ({__exception.GetType().Name})");
                if (__instance != null) __instance.gameObject.SetActive(false);
                return null;
            }
        }

        /// <summary>Same courtesy vanilla gives on a first visit to a floor: nobody waits at the arrival stair.</summary>
        private static void ClearArrivalRoom(Room room)
        {
            if (room == null) return;
            foreach (Character m in AI_Manager.instance.GetMobsInRoom(room).Where(m => m != null && !m.GetIsDead()).ToList())
            {
                AI_Manager.instance.RemoveCharacter(m);
                m.xpValue = 0;
                m.AddTag("NOXP");
                m.GetMover().GetCurrentNavNode()?.ClearOccupant();
                UnityEngine.Object.Destroy(m.gameObject);
            }
        }

        internal static void MoveOccupantAway(NavNode node)
        {
            Character occ = node?.GetOccupant();
            if (occ == null || occ == Player.instance) return;
            NavNode to = node.GetRoom()?.GetRandomMobSpawnNode() ?? Map.instance.GetClosestNode(node, 20f, validNodesOnly: true);
            if (to == null) { occ.Die(); return; }
            occ.GetMover().AbortMove();
            node.ClearOccupant();
            occ.GetMover().SetCurrentNavNode(to);
            occ.transform.position = to.transform.position;
        }

        /// <summary>
        /// The way out is a green pad at the arrival point (a tower's only on its ground layer), unless a stair out is
        /// still standing (PruneStairs hasn't run yet on this layer).
        /// </summary>
        private static void EnsureExit()
        {
            if (!SideLayers.ExitHere) return;
            if (Map.instance.mapStairs.Any(s => s != null && s.myNode != null && !SideLayers.LeadsToLayer(s))) return;
            NavNode n = Map.instance.GetClosestNode(Player.instance.GetMover().GetCurrentNavNode(), 3f, validNodesOnly: true);
            if (n != null) PlacePad(n.transform.position, isReturn: true);
        }

        /// <summary>
        /// A borrowed map keeps the real floor's stairs, but in here none of them goes up or down: they all lead
        /// straight out. Only a tower's stairs between its layers stay; the rest are taken away (the minimap tile
        /// goes back to plain floor) and the green pad is the way out. Runs after every build and load. Generated
        /// places are left alone: their only stair is the exit they were built with.
        /// </summary>
        internal static void PruneStairs()
        {
            // a generated place's one "stair" is its designed way out (the airlock, the hatch): it stays
            if (!InSide || Map.instance == null || Places.Here) return;
            int gone = 0;
            foreach (Stair s in Map.instance.mapStairs.ToList())
            {
                if (s == null || SideLayers.LeadsToLayer(s)) continue;
                Map.instance.mapStairs.Remove(s);
                NavNode n = s.myNode;
                if (n != null && n.miniMapTile != null)
                {
                    NavNode plain = n.GetLinks().FirstOrDefault(x => x != null && x.miniMapTile != null && Map.instance.mapStairs.All(o => o == null || o.myNode != x));
                    var mine = n.miniMapTile.GetComponent<MeshRenderer>();
                    var like = plain?.miniMapTile.GetComponent<MeshRenderer>();
                    if (mine != null && like != null) mine.sharedMaterial = like.sharedMaterial;
                }
                UnityEngine.Object.Destroy(s.gameObject);
                gone++;
            }
            if (gone > 0) Plugin.Log.LogInfo($"Side areas: took away {gone} stair(s) that only led out");
            if (!Pads.Any(x => x != null && x.isReturn)) EnsureExit();
        }

        /// <summary>Every way out asks first (unless [SideAreas] ConfirmLeave is off): leaving turns in any job.</summary>
        internal static void AskToLeave()
        {
            if (busy || !InSide || !SideKinds.ExitsOpen(say: true)) return;
            if (!ConfirmLeave.Value) { Plugin.Instance.StartCoroutine(Return()); return; }
            string back = Endless.Here ? "The breach stays open, and the Deep will be a fresh descent next time."
                : "The breach stays open: step on it again to come back, and it'll be as you left it.";
            Choice.Show("SCCL_LeaveSide", "<color=#D070FF>Leave this side area?</color>",
                $"You go back to floor {Game.FLOOR + 1}. {back}" + (Contracts.Job != null ? " Your job is turned in as it stands." : ""),
                new List<(string, Action, Color?)>
                {
                    ("Leave", () => { if (SideKinds.ExitsOpen(say: true)) Plugin.Instance.StartCoroutine(Return()); }, UiKit.DangerColor),
                    ("Stay", null, null),
                });
        }

        private static readonly string[] StoryRoomWords = { "Boss", "Cerberus", "TheEnd", "Hub", "Final" };

        private static void SealStoryRooms()
        {
            foreach (Room r in Map.instance.GetRooms())
            {
                if (r == null) continue;
                bool story = r.Properties.IsBossRoom || (r is Room_Event && StoryRoomWords.Any(w => r.GetType().Name.Contains(w)));
                if (!story) continue;
                foreach (Door d in r.roomDoors)
                    if (d != null) d.SetSeal(true);
                Plugin.Log.LogInfo($"Side areas: sealed {r.GetType().Name} '{r.name}'");
            }
        }

        private static void SpawnCache()
        {
            NavNode n = RandomFreeNode(null);
            if (n == null) return;
            Room room = n.GetRoom();
            RARITY usual = SideKinds.CacheRarity(RARITY.Rare);
            var kinds = new[] { (LOOT_TYPE.Weapon, usual == RARITY.Rare ? RARITY.Epic : usual), (LOOT_TYPE.Random, usual), (LOOT_TYPE.Gear, usual), (LOOT_TYPE.Random, usual) };
            for (int i = 0; i < SideKinds.CacheItems(Tweaks.Int("breachCacheItems", CacheItems.Value)); i++)
            {
                var (type, rarity) = kinds[i % kinds.Length];
                Item item = LootManager.instance.Generate_Loot(type, rarity);
                if (item == null) continue;
                LootManager.instance.SprinkleLootDust(item);
                Map.instance.AddItem(item);
                item.transform.position = n.transform.position;
                LootManager.instance.DoLootSpew(n.transform.position, item, 1.5f, room, animateRotation: true, null, 0.3f);
            }
        }

        internal static void ApplyFog(int theme)
        {
            Level lvl = LevelManager.instance.GetLevel(theme);
            var fog = Player_Control.instance.playerCamera.gameObject.GetComponent<VolumetricFogAndMist.VolumetricFog>();
            if (fog != null)
            {
                // a generated place has its own plain distance fog (Places.ApplyLook), not a floor's fog volume
                fog.enabled = lvl != null && lvl.volumeFogProfile != null && !Places.Here;
                if (fog.enabled)
                {
                    fog.profile = lvl.volumeFogProfile;
                    if (Map.instance.sun != null) fog.sun = Map.instance.sun;
                }
            }
            Player_Control.instance.SetCameraFarClipPlane(Map.instance.cameraFarClipPlane);
        }

        // ---- saving the echo ------------------------------------------------------------------------------------

        // The base Level's own save and load, called on the real floor's Level without the floor subclasses' overrides
        // (their intro/quest objects belong to the real floor). Both write the echo's stair spots (and the load its
        // kill squad numbers) onto that Level, so the real floor's values are put back after each call.
        private static readonly Func<Level, object> BaseSerialize =
            AccessTools.MethodDelegate<Func<Level, object>>(AccessTools.Method(typeof(Level), nameof(Level.Serialize)), virtualCall: false);
        private static readonly Action<Level, object> BaseDeserialize =
            AccessTools.MethodDelegate<Action<Level, object>>(AccessTools.Method(typeof(Level), nameof(Level.Deserialize)), virtualCall: false);

        private static T KeepingRealFloor<T>(Func<Level, T> call)
        {
            Level real = LevelManager.instance.GetLevel(Game.FLOOR);
            var (up, down, index, counter) = (real.stairUpLocs, real.stairDownLocs, real.killSquadIndex, real.killSquadCounter);
            try { return call(real); }
            finally { (real.stairUpLocs, real.stairDownLocs, real.killSquadIndex, real.killSquadCounter) = (up, down, index, counter); }
        }

        internal static LevelSaveInfo SideSerialize() => KeepingRealFloor(real => (LevelSaveInfo)BaseSerialize(real));

        // Map.Deserialize places stairs at the level's stair spots, which during the call are the echo's own (a theme
        // with fewer stair spots than the real floor crashed it).
        internal static void SideDeserialize(LevelSaveInfo info) => KeepingRealFloor(real => { BaseDeserialize(real, info); return true; });

        // ---- patches --------------------------------------------------------------------------------------------

        [HarmonyPatch(typeof(Map), nameof(Map.Init))]
        private static class Patch_MapInit
        {
            private static void Postfix(Map __instance)
            {
                if (Game.Game_isLoading || InSide || Map.instance != __instance || BreachChance <= 0) return;
                try { DecideBreach(Game.FLOOR); }
                catch (Exception e) { Plugin.Log.LogError("Side areas: " + e); }
            }
        }

        [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.SerializeLevel))]
        private static class Patch_SerializeLevel
        {
            private static bool Prefix(LevelManager __instance, int id)
            {
                if (!InSide || id != Game.FLOOR) return true;
                __instance.levelData.activeLevel = Game.FLOOR;
                Set("side_level", SideSerialize());
                return false;
            }
        }

        [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.DeserializeLevel))]
        private static class Patch_DeserializeLevel
        {
            private static bool Prefix(LevelManager __instance, int id)
            {
                if (!InSide || id != Game.FLOOR || !(Get<object>("side_level") is LevelSaveInfo info)) return true;
                try
                {
                    __instance.LoadRenderSettingsForLevel(id);
                    __instance.LoadMusicForLevel(id);
                    SideDeserialize(info);
                    Places.Finish(fresh: false);
                    EnsureExit();
                    RestoreDeeper();
                    try { Echoes.OnEchoRestored(); Contracts.OnEchoRestored(); SideKinds.OnEchoRestored(); SideLayers.OnEchoRestored(); }
                    catch (Exception e) { Plugin.Log.LogError("Factions: restoring the echo: " + e); }
                    PruneStairs();
                    return false;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError("Side areas: could not restore the echo, returning to the real floor: " + e);
                    Set("side_active", false);
                    return true;
                }
            }

            private static void Postfix(int id)
            {
                if (InSide || id != Game.FLOOR || id >= 100) return;
                try { RestoreBreach(id); }
                catch (Exception e) { Plugin.Log.LogError("Side areas: " + e); }
            }
        }

        [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.UseStair))]
        private static class Patch_UseStair
        {
            private static bool Prefix(Stair useStair)
            {
                if (!InSide) return true;
                if (busy || SideLayers.TakeStair(useStair)) return false; // a tower's stairs lead between its layers
                AskToLeave();
                return false;
            }
        }

        [HarmonyPatch(typeof(TeleportPad), nameof(TeleportPad.OnEnterNode))]
        private static class Patch_PadEnter
        {
            private static bool Prefix(TeleportPad __instance, Character c)
            {
                Breach b = __instance.GetComponent<Breach>();
                if (b == null) return true;
                if (c == Player.instance && !Game.Game_isLoading && !busy && !Player.instance.GetIsDead())
                {
                    if (b.isReturn) AskToLeave();
                    else if (b.isDeeper) Plugin.Instance.StartCoroutine(Dive(__instance.myNodes[0]));
                    else if (Endless.Is(Game.FLOOR)) Endless.ShowEntry(__instance.myNodes[0]);
                    // pick a job (or none) first; going back into a side area you left has no board
                    else if (Factions.Active && SideKinds.KindOf(Game.FLOOR).jobs && !HasKept(Game.FLOOR)) FactionUi.ShowBoard(__instance.myNodes[0]);
                    else Plugin.Instance.StartCoroutine(Enter(__instance.myNodes[0]));
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.LoadRenderSettingsForLevel))]
        private static class Patch_Render
        {
            private static void Prefix(ref int i)
            {
                if (InSide && i == Game.FLOOR) i = Theme;
            }

            private static void Postfix() => Places.ApplyLook();
        }

        [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.LoadMusicForLevel))]
        private static class Patch_Music
        {
            private static void Prefix(ref int i)
            {
                if (InSide && i == Game.FLOOR) i = Theme;
            }
        }

        /// <summary>Floor intros (BioLabs, Production, Special Projects) belong to the real floor, not its echo.</summary>
        [HarmonyPatch]
        private static class Patch_OnPlayerEntered
        {
            private static IEnumerable<System.Reflection.MethodBase> TargetMethods() =>
                new[] { typeof(Level), typeof(Level_BioLab), typeof(Level_Production), typeof(Level_SpecialProjects) }
                    .Select(t => (System.Reflection.MethodBase)AccessTools.DeclaredMethod(t, nameof(Level.OnPlayerEntered)));

            private static bool Prefix()
            {
                if (!InSide) return true;
                ApplyFog(Theme);
                return false;
            }
        }

        [HarmonyPatch]
        private static class Patch_Register
        {
            private static IEnumerable<System.Reflection.MethodBase> TargetMethods() =>
                new[] { typeof(Level_BioLab), typeof(Level_Production), typeof(Level_SpecialProjects) }
                    .Select(t => (System.Reflection.MethodBase)AccessTools.DeclaredMethod(t, nameof(Level.Register)));

            private static bool Prefix() => !InSide;
        }

        /// <summary>Kill squads wait until you are back on the real floor.</summary>
        [HarmonyPatch(typeof(Level), nameof(Level.DeployKillSquad))]
        private static class Patch_KillSquad
        {
            private static bool Prefix() => !InSide;
        }
    }
}
