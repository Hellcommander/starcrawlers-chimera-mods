using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// What an item change costs beyond the change, with [Performance] EventBus on. Every pickup, potion, ammo stack, rune and
    /// credit redrew every slot of the bag, open or not, and an action that moved several items did it once per item.
    ///
    /// What the bag holds never waits, only how it's drawn: a closed bag draws when it opens (Menu.Show refreshes it anyway),
    /// and an open one draws on a frame's first change and once more from the bus's pump. What other code reads goes through at
    /// once: the list loses its dead entries, the credits line stays right, and the HUD gets its refresh (BuffLane batches that).
    /// "bus:items" goes out at most once a frame for whoever watches the bag, the stash or your gear.
    /// </summary>
    internal static class ItemLane
    {
        internal static int Held;
        private static int frame = -1, shownCash = -1;
        private static bool owed, flushing;

        /// <summary>From the bus's pump, once a frame.</summary>
        internal static void Flush()
        {
            StashTabs.Flush();
            if (!owed) return;
            owed = false;
            Menu_Inventory bag = Menu_Inventory.instance;
            if (bag == null || !bag.GetIsShowing()) return;
            flushing = true;
            try { bag.Refresh(); }
            catch (System.Exception e) { Plugin.Log.LogWarning("Item lane: " + e.Message); }
            finally { flushing = false; }
        }

        internal static void Forget()
        {
            owed = false;
            StashTabs.Landing();
        }

        private static void DrawnLater(Menu_Inventory bag)
        {
            if (bag.creditsDisplay != null && shownCash != bag.CASH)
            {
                shownCash = bag.CASH;
                bag.creditsDisplay.text = "<sprite=50> " + bag.CASH;
            }
            for (int i = bag.items.Count - 1; i >= 0; i--)
                if (bag.items[i] == null) bag.items.RemoveAt(i);
            Menu_HUD.instance?.Refresh();
        }

        // the stash overrides Refresh without calling this one, so this is only ever the bag
        [HarmonyPatch(typeof(Menu_Inventory), nameof(Menu_Inventory.Refresh))]
        private static class Patch_Bag
        {
            private static bool Prefix(Menu_Inventory __instance)
            {
                EventBus.Mark(EventBus.Items);
                if (flushing || !EventBus.On || Game.Game_isLoading) { shownCash = -1; return true; }
                if (__instance.GetIsShowing())
                {
                    if (frame != Time.frameCount)
                    {
                        frame = Time.frameCount;
                        owed = false;
                        shownCash = -1;
                        return true;
                    }
                    owed = true;
                }
                Held++;
                DrawnLater(__instance);
                return false;
            }
        }

        [HarmonyPatch(typeof(Menu_Stash), nameof(Menu_Stash.Refresh))]
        private static class Patch_Stash
        {
            private static void Postfix() => EventBus.Mark(EventBus.Items);
        }
    }
}
