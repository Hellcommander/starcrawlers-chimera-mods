using System.Collections.Generic;
using HarmonyLib;

namespace SCClassLoader
{
    /// <summary>
    /// Every hit on an enemy makes it look at its attacker and call for help, and every friend in earshot looks too, each with a
    /// raycast. The game has a flag for "already called" that nothing ever sets, so a crowd under fire repeats all of it per hit.
    /// An enemy now answers an attacker, and calls for help, once a turn; the next hit only tells it where they are. That part
    /// runs with the bus on or [Fixes] EnemyAlarmOncePerTurn; the rest of this file needs the bus.
    /// </summary>
    internal static class Alarm
    {
        private static readonly Dictionary<AI, Character> answered = new Dictionary<AI, Character>();
        private static readonly HashSet<AI> called = new HashSet<AI>();
        private static readonly HashSet<AI> heard = new HashSet<AI>();

        private const float SwingSeconds = 0.6f;
        private static readonly Dictionary<Character, float> swinging = new Dictionary<Character, float>();

        internal static int Spared;

        private static bool Once => EventBus.On || BugFixes.AlarmOnce.Value;

        internal static void NewTurn()
        {
            answered.Clear();
            called.Clear();
            heard.Clear();
            swinging.Clear();
        }

        /// <summary>A flinch replaces whatever the body is playing, so in a crowd swings never showed. A swing now plays out and the flinches wait.</summary>
        [HarmonyPatch(typeof(Ability), nameof(Ability.Use))]
        private static class Patch_Swing
        {
            private static void Prefix(Ability __instance)
            {
                if (EventBus.On && __instance is Ability_Mob && __instance.GetCaster() != null) swinging[__instance.GetCaster()] = UnityEngine.Time.time + SwingSeconds;
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.Flinch))]
        private static class Patch_Flinch
        {
            private static bool Prefix(Character __instance) => !EventBus.On || !swinging.TryGetValue(__instance, out float until) || UnityEngine.Time.time >= until;
        }

        private static readonly AccessTools.FieldRef<AI_Manager, List<AI>> Ais = AccessTools.FieldRefAccess<AI_Manager, List<AI>>("ais");
        private static readonly List<AI> listed = new List<AI>();
        private static readonly List<UnityEngine.Vector3> spots = new List<UnityEngine.Vector3>();
        private static readonly UnityEngine.Vector3 Nowhere = new UnityEngine.Vector3(float.MaxValue, 0f, 0f);
        private static int listedFrame = -1;

        /// <summary>A noise asks who's in earshot once per team, and each asking read every AI's position from the engine. They're read once a frame now.</summary>
        [HarmonyPatch(typeof(AI_Manager), nameof(AI_Manager.GetAlliesAt), new[] { typeof(UnityEngine.Vector3), typeof(float), typeof(AI.AI_TEAM) })]
        private static class Patch_Earshot
        {
            private static bool Prefix(AI_Manager __instance, UnityEngine.Vector3 pos, float distance, AI.AI_TEAM team, ref List<AI> __result)
            {
                if (!EventBus.On) return true;
                List<AI> all = Ais(__instance);
                if (listedFrame != UnityEngine.Time.frameCount || listed.Count != all.Count)
                {
                    listedFrame = UnityEngine.Time.frameCount;
                    listed.Clear();
                    spots.Clear();
                    foreach (AI ai in all)
                    {
                        listed.Add(ai);
                        spots.Add(ai != null ? ai.transform.position : Nowhere);
                    }
                }
                float reach = distance * distance;
                __result = new List<AI>();
                for (int i = 0; i < listed.Count; i++)
                {
                    if ((pos - spots[i]).sqrMagnitude > reach) continue;
                    AI ai = listed[i];
                    if (ai != null && ai.GetTeam() == team && ai.GetCharacter() != null && !ai.GetCharacter().GetIsDead()) __result.Add(ai);
                }
                return false;
            }
        }

        /// <summary>Every enemy asks this of every candidate target, and the game copies all the tiles of the room and its neighbours to look for one character. It knows where they stand.</summary>
        [HarmonyPatch(typeof(Room), nameof(Room.GetLinkedRoomsContains))]
        private static class Patch_InReach
        {
            private static bool Prefix(Room __instance, Character c, ref bool __result)
            {
                if (!EventBus.On || c == null || c.GetMover() == null) return true;
                NavNode at = c.GetMover().GetCurrentNavNode();
                Room room = at != null ? at.GetRoom() : null;
                if (room == null) return true;
                __result = at.GetOccupant() == c && (room == __instance || __instance.linkedRooms.Contains(room));
                return false;
            }
        }

        [HarmonyPatch(typeof(AI), nameof(AI.AttackedBy))]
        private static class Patch_Attacked
        {
            private static bool Prefix(AI __instance, Character c)
            {
                if (!Once || c == null || c.GetIsDead() || __instance.GetBaseTeam() == AI.AI_TEAM.Passive) return true;
                bool again = __instance.GetState() == AI.AI_STATE.Active && __instance.targetCharacter == c
                    && answered.TryGetValue(__instance, out Character last) && last == c;
                answered[__instance] = c;
                if (!again) return true;
                __instance.targetCharacterLastNode = c.GetMover().GetCurrentNavNode();
                Spared++;
                return false;
            }
        }

        [HarmonyPatch(typeof(AI_Manager), nameof(AI_Manager.EnemyAlert), new[] { typeof(AI), typeof(Character) })]
        private static class Patch_Call
        {
            private static bool Prefix(AI caller) => !Once || caller == null || called.Add(caller);
        }

        [HarmonyPatch(typeof(AI), nameof(AI.EnemyAlert), new[] { typeof(Character) })]
        private static class Patch_Heard
        {
            // an enemy still looking for someone keeps listening; one with a target only needs telling once a turn
            private static bool Prefix(AI __instance)
            {
                if (!Once || __instance.GetState() != AI.AI_STATE.Active || __instance.targetCharacter == null) return true;
                return heard.Add(__instance);
            }
        }
    }
}
