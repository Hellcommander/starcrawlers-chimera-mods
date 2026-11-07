using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// A breakable prop in a generated place: the game's own Destructible (so blasts smash it, a creature walking into a
    /// scenery one smashes it), plus a click from the next tile to smash it by hand. A smashed prop that blocked its tile
    /// lets you walk there. Which ones are smashed is kept in the side area's own save data, per place and layer (a place's
    /// rooms are built by the loader, so the game's room save doesn't list them), and replayed quietly when it comes back.
    /// </summary>
    public class PlaceProp : MonoBehaviour
    {
        public string propId;
        public bool blocks;
        /// <summary>The tile it stands on: the object sits at the tile's centre (its meshes may sit against a wall).</summary>
        public Vector3 tile => transform.position;

        private Destructible D => GetComponent<Destructible>();

        /// <summary>Build-time: the collider round its meshes and the game's breakable logic.</summary>
        internal static void Make(GameObject g, PlacePropSpec spec, Vector3 tile)
        {
            var rs = g.GetComponentsInChildren<Renderer>(true);
            Bounds b = rs.Length > 0 ? rs.Select(r => r.bounds).Aggregate((a, x) => { a.Encapsulate(x); return a; }) : new Bounds(tile + Vector3.up * 0.4f, Vector3.one * 0.6f);
            var bc = g.AddComponent<BoxCollider>();
            bc.isTrigger = true; // clicks and blasts find it; nothing bumps into it
            bc.center = g.transform.InverseTransformPoint(b.center);
            bc.size = Vector3.Max(b.size, Vector3.one * 0.3f);
            var pp = g.AddComponent<PlaceProp>();
            pp.propId = spec.id;
            pp.blocks = spec.blocks;
            var d = g.AddComponent<Destructible>();
            d.FMOD_OnDestroy = string.IsNullOrEmpty(spec.breakSound) ? "event:/Effects/Smash Big" : spec.breakSound;
            d.shakePower = 0.15f;
            d.destructNoise = 4f; // the crash carries a little way
        }

        private NavNode Node => Map.instance != null ? Map.instance.GetNavNodeAt(tile) : null;
        private string Title => string.IsNullOrEmpty(propId) ? "Prop" : char.ToUpperInvariant(propId[0]) + propId.Substring(1);

        /// <summary>
        /// Its box sits over its tile and gets the mouse first, so it passes the hover on: a floor skill being aimed
        /// lands on its tile (and smashes it, see Patch_AimedAt); otherwise it names itself.
        /// </summary>
        private void OnMouseEnter()
        {
            if (D == null || D.triggered) return;
            if (Game.TARGETING) { Node?.iOnMouseEnter(); return; }
            if (HUD.instance != null && HUD.instance.OverMenuCheck()) return;
            Tooltip.instance?.Show_TinyTip(Title, "Breakable", null);
        }

        private void OnMouseExit()
        {
            if (Game.TARGETING) { Node?.iOnMouseExit(); return; }
            Tooltip.instance?.Hide();
        }

        /// <summary>The tile a skill was just aimed at, while that skill resolves.</summary>
        private static NavNode aimed;

        [HarmonyPatch(typeof(Game), nameof(Game.Resolve_UseAbility))]
        private static class Patch_AimedAt
        {
            private static void Prefix() => aimed = Game.TARGET_N;
        }

        /// <summary>A skill aimed at a prop's tile hits the prop: it breaks as the turn ends.</summary>
        [HarmonyPatch(typeof(Game), nameof(Game.EndTurn))]
        private static class Patch_HitWhenAimed
        {
            private static void Prefix()
            {
                NavNode n = aimed;
                aimed = null;
                if (n == null || Game.Game_isLoading) return;
                try
                {
                    foreach (PlaceProp pp in FindObjectsOfType<PlaceProp>().Where(x => x != null && Vector3.Distance(x.tile, n.transform.position) < 0.3f).ToList())
                    {
                        Destructible d = pp.D;
                        if (d != null && !d.triggered) d.Trigger(pp.tile + Vector3.up * 0.5f);
                    }
                }
                catch (Exception e) { Plugin.Log.LogWarning("Places: a skill aimed at a prop: " + e.Message); }
            }
        }

        /// <summary>Why the last click didn't smash it (self-tests).</summary>
        internal static string LastRefusal;

        private void OnMouseDown()
        {
            if (Game.TARGETING) return; // aiming a skill: the click is the skill's (its tile is the target)
            LastRefusal = TrySmash();
        }

        private string TrySmash()
        {
            Player p = Player.instance;
            Destructible d = D;
            if (d == null || d.triggered) return "already smashed";
            if (p == null || p.GetIsDead() || Game.Game_isLoading) return "no player";
            if (Game.RESOLVING || Game.LOCK || Game.TARGETING || Game.VENDOR || Game.EVENT)
                return $"busy (resolving {Game.RESOLVING}, lock {Game.LOCK}, targeting {Game.TARGETING}, vendor {Game.VENDOR}, event {Game.EVENT})";
            if (HUD.instance != null && HUD.instance.OverMenuCheck()) return "over a menu";
            NavNode here = p.GetMover()?.GetCurrentNavNode();
            if (here == null || p.GetMover().isMoving) return "moving";
            Vector3 a = here.transform.position, t = tile;
            if (Mathf.Abs(a.x - t.x) > 1.2f || Mathf.Abs(a.z - t.z) > 1.2f || Mathf.Abs(a.y - t.y) > 0.6f) return $"too far ({a} to {t})"; // next to it, diagonals too
            Plugin.Instance.StartCoroutine(Smash(d));
            return null;
        }

        private IEnumerator Smash(Destructible d)
        {
            Player p = Player.instance;
            try { p.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(tile - p.transform.position, Vector3.up)); } catch { }
            d.Trigger(Vector3.Lerp(p.transform.position, tile, 0.7f) + Vector3.up * 0.5f);
            yield return null;
            Game.instance.PlayerAction(PLAYER_ACTION.Wait); // smashing it is your turn: the world moves as after a wait
        }

        private static bool replaying;
        private static string Key => Map.instance != null ? "smashed|" + Map.instance.internalName : null;

        /// <summary>A place is back (a load, a return trip, another tower layer): its smashed props go again, silently.</summary>
        internal static void OnRestored()
        {
            string key = Key;
            if (key == null || !(SideAreas.Data?.TryGetValue(key, out object v) == true && v is float[] at) || at.Length < 3) return;
            var rooms = new System.Collections.Generic.HashSet<Room>(Map.instance.mapRooms.Where(r => r != null));
            var props = FindObjectsOfType<PlaceProp>(true).Where(x => x != null && rooms.Contains(x.GetComponentInParent<Room>(true))).ToList();
            int done = 0;
            replaying = true;
            try
            {
                for (int i = 0; i + 2 < at.Length; i += 3)
                {
                    var t = new Vector3(at[i], at[i + 1], at[i + 2]);
                    PlaceProp pp = props.FirstOrDefault(x => Vector3.Distance(x.tile, t) < 0.3f);
                    Destructible d = pp != null ? pp.D : null;
                    if (d == null || d.triggered) continue;
                    d.FMOD_OnDestroy = ""; d.shakePower = 0f; d.destructNoise = 0f; // gone already, not breaking now
                    d.Trigger(t);
                    done++;
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Places: putting smashed props back: " + e.Message); }
            finally { replaying = false; }
            Plugin.Log.LogInfo($"Places: {done} smashed prop(s) gone again ({at.Length / 3} kept, {props.Count} props in {rooms.Count} rooms, map {Map.instance.internalName}" +
                               $"{(done < at.Length / 3 && props.Count > 0 ? $", nearest to the first kept {props.Min(x => Vector3.Distance(x.tile, new Vector3(at[0], at[1], at[2]))):0.00} m" : "")})");
        }

        /// <summary>Its tile opens up when it goes, and the place remembers it.</summary>
        [HarmonyPatch(typeof(Destructible), nameof(Destructible.Trigger), new[] { typeof(Vector3), typeof(PSMeshRendererUpdater) })]
        private static class Patch_Smashed
        {
            private static void Postfix(Destructible __instance)
            {
                PlaceProp pp = __instance != null ? __instance.GetComponent<PlaceProp>() : null;
                if (pp == null || !__instance.triggered || Map.instance == null) return;
                try
                {
                    var data = SideAreas.Data;
                    if (!replaying && data != null && Key != null)
                    {
                        float[] was = data.TryGetValue(Key, out object v) && v is float[] f ? f : new float[0];
                        Vector3 t = pp.tile;
                        data[Key] = was.Concat(new[] { t.x, t.y, t.z }).ToArray();
                    }
                    if (!pp.blocks) return;
                    NavNode n = Map.instance.GetNavNodeAt(pp.tile);
                    if (n == null || Vector3.Distance(n.transform.position, pp.tile) > 0.3f) return;
                    n.Block_Move = n.Block_Item = n.Block_Mob = n.Block_Hazard = false;
                }
                catch (Exception e) { Plugin.Log.LogWarning("Places: freeing a smashed prop's tile: " + e.Message); }
            }
        }
    }

    /// <summary>
    /// Fire-and-forget events that are really loops. The game plays a loop (the hacker's working hum, a charge, a ray)
    /// on an instance it stops itself; fired through AudioManager.PlayEvent as a one-shot, nothing ever stops it, and it
    /// hums on behind everything for the rest of the session. Any looping event played that way is cut short instead,
    /// the way the game cuts the hack sound after a hack. Mod skills, reactions and objectives all play sounds that way.
    /// </summary>
    internal static class GameSounds
    {
        private const float LoopCut = 0.6f;
        private static readonly System.Collections.Generic.Dictionary<string, bool> loops = new System.Collections.Generic.Dictionary<string, bool>();

        /// <summary>The hack sound: an uplink planted, a node tapped, an objective done.</summary>
        internal static void HackDone() => AudioManager.PlayEvent("event:/Abilities/Hacker/Hacker_Hack");

        private static bool IsLoop(string path)
        {
            if (loops.TryGetValue(path, out bool l)) return l;
            l = false;
            try
            {
                FMOD.Studio.EventDescription d = FMODUnity.RuntimeManager.GetEventDescription(path);
                if (d.isValid() && d.isOneshot(out bool one) == FMOD.RESULT.OK) l = !one;
            }
            catch { }
            if (l) Plugin.Log.LogInfo($"Sounds: {path} is a loop; played as a one-shot it is cut after {LoopCut}s");
            return loops[path] = l;
        }

        [HarmonyPatch(typeof(AudioManager), nameof(AudioManager.PlayEvent), new[] { typeof(string), typeof(Vector3) })]
        private static class Patch_LoopAsOneShot
        {
            private static bool Prefix(string path, Vector3 pos)
            {
                if (string.IsNullOrEmpty(path) || Plugin.Instance == null || !IsLoop(path)) return true;
                try
                {
                    FMOD.Studio.EventInstance e = FMODUnity.RuntimeManager.CreateInstance(path);
                    e.set3DAttributes(FMODUnity.RuntimeUtils.To3DAttributes(pos));
                    e.start();
                    Plugin.Instance.StartCoroutine(StopSoon(e));
                    return false;
                }
                catch (Exception ex) { Plugin.Log.LogWarning($"Sounds: {path}: " + ex.Message); return true; }
            }
        }

        private static IEnumerator StopSoon(FMOD.Studio.EventInstance e)
        {
            yield return new WaitForSecondsRealtime(LoopCut);
            e.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            e.release();
        }
    }
}
