// An example to start your own plugin from: use, copy and change it for any purpose, with no conditions and no credit needed.

using HarmonyLib;
using UnityEngine;

namespace RealtimeWithPause
{
    /// <summary>
    /// One player action held back for its tick. The game keeps what an action is aimed at in statics that the next hover or
    /// click overwrites, so an order takes a copy of them and puts them back just before it runs.
    /// </summary>
    internal sealed class Order
    {
        internal PLAYER_ACTION action;
        private Ability ability;
        private TARGET_STYLE style;
        private Character targetC;
        private NavNode targetN;
        private InteractiveObject targetO;
        private Hazard targetH;
        private Item targetI, item, itemAbility;
        private Door door;
        private Destructible smash;
        private NavNode from, to;

        private static readonly AccessTools.FieldRef<Player, Door> TargetDoor = AccessTools.FieldRefAccess<Player, Door>("targetDoor");

        internal bool Instant => action == PLAYER_ACTION.UseAbility && ability != null && ability.isInstant;

        internal string Name
        {
            get
            {
                switch (action)
                {
                    case PLAYER_ACTION.UseAbility: return ability != null ? ability.GetDisplayName() : "Skill";
                    case PLAYER_ACTION.UseItem: return item != null ? item.GetName() : "Item";
                    case PLAYER_ACTION.Move: return "Step";
                    case PLAYER_ACTION.AutoAttack: return "Attack";
                    case PLAYER_ACTION.SmashAttack: return "Smash";
                    case PLAYER_ACTION.OpenDoor: return "Open door";
                    case PLAYER_ACTION.CloseDoor: return "Close door";
                    case PLAYER_ACTION.Wait: return "Wait";
                    default: return action.ToString();
                }
            }
        }

        /// <summary>Copies what the action is aimed at and leaves the game as if the action had started, free for the next order.</summary>
        internal static Order Take(PLAYER_ACTION action)
        {
            Character_Mover m = Game.PLAYER_MOVER ?? Player.instance.GetMover();
            var o = new Order
            {
                action = action, ability = Game.PLAYER_ABILITY, style = Game.TARGETSTYLE,
                targetC = Game.TARGET_C, targetN = Game.TARGET_N, targetO = Game.TARGET_O, targetH = Game.TARGET_H, targetI = Game.TARGET_I,
                item = Game.PLAYER_ITEM, itemAbility = Game.PLAYER_ITEM_ABILITY,
                door = TargetDoor(Player.instance), smash = Player_AbilityControl.instance.smashTarget,
                from = m.playerStartNode, to = m.playerTargetNode
            };
            // Only what ends an aim in the game ends it here: a Wait or a held-back swing leaves it, and its overlay, alone.
            if (action == PLAYER_ACTION.UseAbility || action == PLAYER_ACTION.UseItem || action == PLAYER_ACTION.Move)
            {
                // As on the way into a real cast: skills that aim at an item let go of it in OnCancelUse unless the turn is resolving.
                Game.SetResolve(true);
                Game.CancelAbilityTargeting();
                Game.SetResolve(false);
                Forget();
            }
            return o;
        }

        /// <summary>A later order that takes this one's place instead of queueing behind it.</summary>
        internal bool SameAs(Order o)
        {
            if (action != o.action) return false;
            switch (action)
            {
                case PLAYER_ACTION.Wait:
                case PLAYER_ACTION.Move: return true;
                case PLAYER_ACTION.AutoAttack: return targetC == o.targetC;
                case PLAYER_ACTION.UseAbility: return ability == o.ability;
                default: return false;
            }
        }

        /// <summary>Puts the aim back for the game to read. Returns why the order can't go ahead any more, or null.</summary>
        internal string Restore()
        {
            Player p = Player.instance;
            switch (action)
            {
                case PLAYER_ACTION.UseAbility:
                    if (ability == null) return "gone";
                    if (targetC != null && targetC.GetIsDead()) targetC = null;
                    Game.PLAYER_ABILITY = ability;
                    Game.TARGETSTYLE = style;
                    Game.TARGET_C = targetC;
                    Game.TARGET_N = targetN;
                    Game.TARGET_O = targetO;
                    Game.TARGET_H = targetH;
                    Game.TARGET_I = targetI;
                    Game.PLAYER_ITEM = item;
                    Game.PLAYER_ITEM_ABILITY = itemAbility;
                    if (!ability.CanUse()) return "not ready";
                    if (!Game.instance.IsOkToUseAbility()) return "no valid target";
                    return null;
                case PLAYER_ACTION.UseItem:
                    if (item == null) return "gone";
                    Game.PLAYER_ITEM = item;
                    return null;
                case PLAYER_ACTION.AutoAttack:
                    if (targetC == null || targetC.GetIsDead()) return "target gone";
                    if (!Player_AbilityControl.instance.ability_autoAttack.CanUseOn(targetC)) return "out of reach";
                    Game.TARGET_C = targetC;
                    return null;
                case PLAYER_ACTION.SmashAttack:
                    if (smash == null) return "gone";
                    Player_AbilityControl.instance.smashTarget = smash;
                    return null;
                case PLAYER_ACTION.OpenDoor:
                case PLAYER_ACTION.CloseDoor:
                    if (door == null) return "gone";
                    p.SetTargetDoor(door);
                    return null;
                case PLAYER_ACTION.Move:
                    Character_Mover m = Game.PLAYER_MOVER ?? p.GetMover();
                    if (to == null || m.GetCurrentNavNode() != from) return "you've moved";
                    if (p.FLAG_isRooted) return "rooted";
                    if (!m.MoveCheck(from, to)) return "blocked";
                    m.playerStartNode = from;
                    m.playerTargetNode = to;
                    return null;
                default:
                    return null;
            }
        }

        internal static void Forget()
        {
            Game.ClearPlayerAbility();
            Game.TARGETSTYLE = TARGET_STYLE.None;
            Game.PLAYER_ITEM = null;
            Game.PLAYER_ITEM_ABILITY = null;
        }
    }
}
