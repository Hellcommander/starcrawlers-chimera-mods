using System.Collections.Generic;
using System.Linq;
using HighlightPlus;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace SCClassLoader
{
    /// <summary>
    /// A boss with a "challenge" isn't in the side area at all until you call it: a prop (the Corrupted Mirror's cracked
    /// glass) stands on a tile by a far room's wall, marked on the minimap. Clicking it from the next tile asks first;
    /// saying yes breaks the prop and the boss steps out beside it, already after you. Walk away and it never happens. Not
    /// a registered interactive, so a save without the mod has nothing to load: the spot and the boss are in the side
    /// areas' saved state, which travels with the layer, and the prop is put back from that.
    /// </summary>
    internal class Challenge : InteractiveObject
    {
        private const string SpotKey = "boss_spot", IdKey = "boss_challenge";
        private const float LampRange = 4.5f, LampIntensity = 3f;

        private static Dictionary<string, object> D => SideAreas.Data;

        private BossSpec boss;
        private NavNode node;

        protected override void Start() { } // not one of the culled objects: it stays lit through a blackout

        internal static Challenge Here => Object.FindObjectOfType<Challenge>();

        internal static BossSpec Waiting => D != null && D.TryGetValue(IdKey, out object v) && v is string id ? SideAreaData.Boss(id) : null;

        internal static IEnumerable<Vector3> MarkerSpots() =>
            Waiting != null && D.TryGetValue(SpotKey, out object v) && v is float[] p ? new[] { new Vector3(p[0], p[1], p[2]) } : new Vector3[0];

        internal static string TrackerLine() => Waiting is BossSpec b ? $"<color={b.color}>{b.challenge.tracker ?? b.name}</color>" : null;

        /// <summary>Against a wall of a far room, where it blocks no way through and you walk up to its face, not its edge.</summary>
        private static NavNode Spot(Room arrival)
        {
            Room room = Bosses.Lair(arrival)?.GetRoom();
            return room?.roomNodes.Where(n => n != null && !n.IsOccupied() && !n.HasDoor() && !n.BlockCheck() && !n.Block_Move && !n.hazards.Any()
                                              && !n.GetLinks().Any(l => l != null && l.HasDoor()))
                .OrderBy(n => Mathf.Abs(n.GetLinks().Count(l => l != null) - 3)).ThenBy(_ => Random.value).FirstOrDefault();
        }

        internal static void Build(BossSpec b, Room arrival)
        {
            NavNode n = Spot(arrival);
            if (n == null) { Plugin.Log.LogWarning($"Boss {b.id}: no tile for its challenge"); return; }
            Vector3 p = n.transform.position;
            D[SpotKey] = new[] { p.x, p.y, p.z };
            D[IdKey] = b.id;
            Place(b, n);
            if (!string.IsNullOrEmpty(b.arrival)) Game.LOG($"<color={b.color}>{b.arrival}</color>");
            Plugin.Log.LogInfo($"Boss: {b.id} waits behind its challenge on {p}");
        }

        /// <summary>A reloaded layer: the prop isn't saved with the map.</summary>
        internal static void Restore()
        {
            BossSpec b = Waiting;
            if (b == null || Here != null || !(D[SpotKey] is float[] p)) return;
            NavNode n = Map.instance.GetNavNodeAt(new Vector3(p[0], p[1], p[2]));
            if (n != null) Place(b, n);
        }

        private static void Place(BossSpec b, NavNode n)
        {
            var go = new GameObject("SCCL_Challenge_" + b.id);
            go.transform.SetParent(Map.instance.transform, worldPositionStays: true);
            go.transform.position = n.transform.position;
            Room room = n.GetRoom();
            Vector3 open = n.GetLinks().Where(l => l != null).Aggregate(Vector3.zero, (a, l) => a + l.transform.position - n.transform.position);
            open.y = 0f;
            if (open.sqrMagnitude > 0.01f) go.transform.rotation = Quaternion.LookRotation(open); // its back to the wall
            ModelAssets.Spawn(b.challenge.model, b.pack?.folder, n.transform.position, go.transform);
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(1f, 2f, 0.6f);
            box.center = Vector3.up;
            if (!string.IsNullOrEmpty(b.challenge.glow) && ColorUtility.TryParseHtmlString(b.challenge.glow, out Color glow))
            {
                var lamp = new GameObject(Places.KeepLit).AddComponent<Light>(); // a blackout leaves it burning
                lamp.transform.SetParent(go.transform, false);
                lamp.transform.localPosition = new Vector3(0f, 1.2f, 0.5f);
                lamp.color = glow;
                lamp.range = LampRange;
                lamp.intensity = LampIntensity;
            }
            go.AddComponent<HighlightEffect>(); // before the component, which picks it up on Awake
            var c = go.AddComponent<Challenge>();
            c.boss = b;
            c.node = n;
            c.room = room;
            c.internalName = go.name;
            c.interactRange = 1.6f;
            n.Block_Move = n.Block_Item = n.Block_Mob = n.Block_Hazard = true;
        }

        public override void OnMouseDown()
        {
            base.OnMouseDown();
            if (Game.LOCK || Game.TARGETING || Game.RESOLVING) return;
            if (RangeCheck()) Player_Control.instance.MouseDownObject(this);
            else Game.LOG($"<color={boss.color}>{boss.challenge.far ?? "Too far away to reach."}</color>");
        }

        public override void Interact()
        {
            ChallengeSpec s = boss.challenge;
            Choice.Show("SCCL_BossChallenge", $"<color={boss.color}>{s.title ?? boss.name}</color>", s.text ?? "",
                new List<(string, System.Action, Color?)>
                {
                    (s.accept ?? "Do it", Accept, UiKit.DangerColor),
                    (s.decline ?? "Leave it", null, null),
                });
        }

        /// <summary>The nearest open tile in its room: a corner prop may have no free neighbour once you stand beside it.</summary>
        private static NavNode Beside(Room room, Vector3 at) =>
            room?.roomNodes.Where(n => n != null && !n.IsOccupied() && !n.HasDoor() && !n.BlockCheck() && !n.Block_Move)
                .OrderBy(n => Vector3.Distance(n.transform.position, at)).ThenBy(_ => Random.value).FirstOrDefault()
            ?? Summons.FreeNode(at, 3.5f);

        /// <summary>The boss comes out onto the nearest open tile and the prop goes. Nothing else starts this fight.</summary>
        private void Accept()
        {
            Vector3 at = transform.position;
            // the map can swap its tiles after the side area is built, so the one we stored may be gone
            NavNode tile = node != null ? node : Map.instance.GetNearestNavNodeAt(at);
            Character c = Bosses.Spawn(boss, null, Beside(room, at));
            if (c == null) return; // no room for it: the prop stays and can be tried again
            D.Remove(SpotKey);
            D.Remove(IdKey);
            if (tile != null) tile.Block_Move = tile.Block_Item = tile.Block_Mob = tile.Block_Hazard = false;
            Destroy(gameObject);
            BreachFinder.Refresh();
            Actions.SpawnVfx(boss.challenge.breakVfx, at + Vector3.up);
            if (!string.IsNullOrEmpty(boss.challenge.sound)) ModAudio.Play(boss.challenge.sound, at, boss.pack?.id);
            Echoes.Hunt(c);
            foreach (Character e in Corrupted.Escort.ToList()) Echoes.Hunt(e);
            // you started this: your people don't wait for it to spot you
            AI_Manager.instance.outOfCombatTimer = 8;
            LevelManager.instance.EnterCombat();
            Bosses.Turn();
        }

        /// <summary>Leaving the layer: a tower keeps these with the layer, and anything else starts clean.</summary>
        internal static void Forget()
        {
            D?.Remove(SpotKey);
            D?.Remove(IdKey);
        }
    }
}
