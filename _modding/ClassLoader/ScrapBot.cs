using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SCClassLoader
{
    /// <summary>
    /// The scrap bot ([EnemyWeapons] ScrapBot, needs [Performance] EventBus on). Built from the Scrap Bot Kit that comes
    /// with the first enemy weapon, it follows you from floor to floor on the Engineer's Junker body. It turns enemy
    /// weapons into scrap it carries: the lesser ones it finds lying near it by itself, the ones in your pack when you ask
    /// from its menu. It is heavily plated, welds scrap onto itself when it's hurt, spends the rest rebuilding the floor's
    /// broken props, and when you open a vendor it sells what it doesn't need to keep.
    ///
    /// Where the weapons and broken props are is kept as plain data (a ledger) as they appear, on screen or not. Each look
    /// the bot takes copies the ledger to the event bus's workers, which pick what it goes for; only the answer comes back
    /// to the main thread, where it is checked against the live object before anything is touched.
    /// </summary>
    internal static class ScrapBot
    {
        internal const string ScrapTag = "SCCL_SCRAP";
        private const float LookSeconds = 0.75f;

        // ---- the ledger: main thread only; workers get copies

        private struct Spot
        {
            public int key, rarity;
            public bool boss;
            public Vector3 pos;
        }

        private static readonly Dictionary<int, Item> weapons = new Dictionary<int, Item>();
        private static readonly Dictionary<int, Spot> weaponSpots = new Dictionary<int, Spot>();
        private static readonly Dictionary<int, Destructible> props = new Dictionary<int, Destructible>();
        private static readonly Dictionary<int, Spot> propSpots = new Dictionary<int, Spot>();
        private static readonly Dictionary<Destructible, Snap> snaps = new Dictionary<Destructible, Snap>();

        /// <summary>How a prop stood before it broke.</summary>
        internal class Snap
        {
            public Vector3 pos;
            public Quaternion rot;
            public int layer;
            public bool hadBody, kinematic, colliderOn, isTrigger;
            public int deco = int.MinValue;
            public List<Destructible> also;
        }

        internal static bool Running => EnemyArms.Enabled.Value && EnemyArms.BotOn.Value && EventBus.On;
        internal static int Tracked => weaponSpots.Count + propSpots.Count;
        internal static int WeaponsTracked => weaponSpots.Count;
        internal static int PropsTracked => propSpots.Count;

        private static void Track(Item it)
        {
            if (it == null || it.inPlayerInventory || !EnemyArms.IsArm(it)) return;
            int k = it.GetInstanceID();
            weapons[k] = it;
            weaponSpots[k] = new Spot { key = k, pos = it.transform.position, rarity = (int)it.rarity, boss = it.HasTag(EnemyArms.BossTag) };
        }

        private static void Untrack(Item it)
        {
            if (it == null) return;
            int k = it.GetInstanceID();
            weapons.Remove(k);
            weaponSpots.Remove(k);
        }

        private static void TrackProp(Destructible d)
        {
            int k = d.GetInstanceID();
            props[k] = d;
            propSpots[k] = new Spot { key = k, pos = d.transform.position };
        }

        private static void UntrackProp(Destructible d)
        {
            int k = d.GetInstanceID();
            props.Remove(k);
            propSpots.Remove(k);
            snaps.Remove(d);
        }

        [HarmonyPatch(typeof(Map), nameof(Map.AddItem))]
        private static class Patch_AddItem
        {
            private static void Postfix(Item i) { if (EnemyArms.Enabled.Value) Track(i); }
        }

        [HarmonyPatch(typeof(Map), nameof(Map.RemoveItem))]
        private static class Patch_RemoveItem
        {
            private static void Postfix(Item i) => Untrack(i);
        }

        /// <summary>A spewed weapon lands a moment after it is added; its spot follows.</summary>
        internal static IEnumerator Settled(Item it)
        {
            yield return new WaitForSeconds(1.2f);
            if (it != null && weapons.ContainsKey(it.GetInstanceID())) Track(it);
        }

        private static Door DoorOf(Destructible d) => Traverse.Create(d).Field("door").GetValue<Door>();

        /// <summary>A prop about to break: how it stands now, so it can be put back.</summary>
        [HarmonyPatch(typeof(Destructible), nameof(Destructible.Trigger), new[] { typeof(Vector3), typeof(PSMeshRendererUpdater) })]
        private static class Patch_Break
        {
            private static void Prefix(Destructible __instance)
            {
                if (!EnemyArms.Enabled.Value || __instance == null || __instance.triggered || Game.Game_isLoading || DoorOf(__instance) != null) return;
                try
                {
                    Transform t = __instance.transform;
                    var body = __instance.GetComponent<Rigidbody>();
                    var col = Traverse.Create(__instance).Field("myCollider").GetValue<Collider>();
                    var deco = __instance.GetComponent<Decoration>();
                    snaps[__instance] = new Snap
                    {
                        pos = t.position, rot = t.rotation, layer = __instance.gameObject.layer,
                        hadBody = body != null, kinematic = body != null && body.isKinematic,
                        colliderOn = col == null || col.enabled, isTrigger = col != null && col.isTrigger,
                        deco = deco != null ? Traverse.Create(deco).Field("integer").GetValue<int>() : int.MinValue,
                        also = new List<Destructible>(__instance.alsoTriggers),
                    };
                    TrackProp(__instance);
                }
                catch (Exception e) { Plugin.Log.LogWarning("Scrap bot: " + e.Message); }
            }
        }

        /// <summary>A loaded floor's broken props that still stand where they were (hidden, debris beside them) can be rebuilt; ones that fell can't.</summary>
        private static bool RebuildsAfterLoad(Destructible d) =>
            d.triggered && d.spawnThis != null && d.GetComponent<Decoration>() == null && DoorOf(d) == null;

        /// <summary>A new floor or a load: the ledger starts over from what's lying there.</summary>
        private static void Rescan()
        {
            weapons.Clear(); weaponSpots.Clear(); props.Clear(); propSpots.Clear();
            foreach (Destructible d in snaps.Keys.Where(d => d != null).ToList()) TrackProp(d); // this floor's, recorded as they broke
            foreach (Destructible d in snaps.Keys.Where(d => d == null).ToList()) snaps.Remove(d);
            if (Map.instance == null) return;
            foreach (Item it in Map.instance.mapItems) Track(it);
            foreach (Room r in Map.instance.mapRooms)
                foreach (Destructible d in r?.GetComponentsInChildren<Destructible>(true) ?? new Destructible[0])
                    if (d != null && !props.ContainsKey(d.GetInstanceID()) && RebuildsAfterLoad(d)) TrackProp(d);
        }

        // ---- scrap

        internal static int Scrap => Player.instance != null ? Math.Max(0, Player.instance.GetTag(ScrapTag)) : 0;

        private static void SetScrap(int n) => Player.instance?.SetTag(ScrapTag, Math.Max(0, n));

        internal static int ScrapFor(Item it)
        {
            int[] per = (EnemyArms.ScrapParts.Value ?? "").Split(',').Select(s => int.TryParse(s.Trim(), out int n) ? Math.Max(0, n) : 1).ToArray();
            int i = Mathf.Clamp((int)it.rarity - (int)RARITY.Common, 0, 3);
            int n0 = per.Length > i ? per[i] : per.Length > 0 ? per[per.Length - 1] : 1;
            return n0 + (it.HasTag(EnemyArms.BossTag) ? Math.Max(0, EnemyArms.BossScrapBonus.Value) : 0);
        }

        internal static bool Scrappable(Item it) => EnemyArms.IsArm(it) && (EnemyArms.ScrapBossWeapons.Value || !it.HasTag(EnemyArms.BossTag));

        private static RARITY HarvestCap() =>
            EnemyArms.HarvestUpTo.Value == "None" ? RARITY.None : ClassRegistry.ParseEnum(EnemyArms.HarvestUpTo.Value, RARITY.Rare, "ScrapBotHarvestUpTo");

        private static string Strip(string s) => System.Text.RegularExpressions.Regex.Replace(s ?? "", "<.*?>", "").Trim();

        internal static int PackCount() =>
            (Menu_Inventory.GetItems() ?? new List<Item>()).Count(i => i != null && i.inPlayerInventory && Scrappable(i));

        /// <summary>Every scrappable enemy weapon in your pack (never what you're wearing) becomes the bot's scrap.</summary>
        internal static int ScrapPack()
        {
            var list = (Menu_Inventory.GetItems() ?? new List<Item>()).Where(i => i != null && i.inPlayerInventory && Scrappable(i)).ToList();
            int got = 0;
            foreach (Item it in list)
            {
                got += ScrapFor(it);
                Menu_Inventory.instance.RemoveItem(it, null, isSilent: true);
                Object.Destroy(it.gameObject);
            }
            SetScrap(Scrap + got);
            Menu_Inventory.instance.Refresh();
            Character bot = Bot();
            if (bot != null && list.Count > 0) Crunch(bot, bot.transform.position);
            Game.LOG(list.Count == 0 ? "The scrap bot finds no enemy weapons in your pack to take apart."
                : $"The scrap bot grinds {list.Count} enemy weapon{(list.Count == 1 ? "" : "s")} into {got} scrap (it carries {Scrap}).");
            return got;
        }

        private static void Crunch(Character bot, Vector3 at)
        {
            bot.ai?.ForceFacing(at);
            try { bot.anim?.Play("Melee"); } catch { }
            Actions.SpawnVfx("PA_Engineer_Boltron.PS_summon", at);
            AudioManager.PlayEvent("event:/Abilities/Prototype/Cannibalize", at);
        }

        // ---- the bot's look: copied out, chosen on a worker, done here

        private struct Look
        {
            public Vector3 from;
            public float weaponReach, propReach;
            public int cap;
            public bool bossToo, canRebuild;
            public Spot[] weapons, props;
        }

        private struct Pick { public int weapon, prop; public bool hasWeapon, hasProp; } // instance ids of runtime objects are negative

        /// <summary>Worker side: plain data in, up to two keys out. The nearest of each in reach.</summary>
        private static Pick Choose(Look l)
        {
            PickedOn = System.Threading.Thread.CurrentThread.ManagedThreadId;
            var p = new Pick();
            float best = float.MaxValue;
            foreach (Spot s in l.weapons)
            {
                if (s.rarity > l.cap || (s.boss && !l.bossToo)) continue;
                float d = (s.pos - l.from).sqrMagnitude;
                if (d <= l.weaponReach * l.weaponReach && d < best) { best = d; p.weapon = s.key; p.hasWeapon = true; }
            }
            if (p.hasWeapon || !l.canRebuild) return p;
            best = float.MaxValue;
            foreach (Spot s in l.props)
            {
                float d = (s.pos - l.from).sqrMagnitude;
                if (d <= l.propReach * l.propReach && d < best) { best = d; p.prop = s.key; p.hasProp = true; }
            }
            return p;
        }

        internal static int Looks, Harvested, Rebuilt, Sold;
        /// <summary>For tests: the thread the last pick ran on.</summary>
        internal static volatile int PickedOn;
        private static bool asking;

        internal class Brain : MonoBehaviour
        {
            private float next;

            private void Update()
            {
                if (Time.time < next || asking || !Running) return;
                next = Time.time + LookSeconds;
                var me = GetComponent<Character>();
                if (me == null || me.GetIsDead() || Game.Game_isLoading || Map.instance == null) return;
                if (!Game.RESOLVING && Repair(me)) return;
                RARITY cap = HarvestCap();
                int cost = EnemyArms.RebuildCost.Value;
                var look = new Look
                {
                    from = me.transform.position,
                    weaponReach = EnemyArms.HarvestRange.Value + 0.5f,
                    propReach = EnemyArms.RebuildRange.Value + 0.5f,
                    cap = cap == RARITY.None ? int.MinValue : (int)cap,
                    bossToo = EnemyArms.ScrapBossWeapons.Value,
                    canRebuild = cost > 0 && Scrap >= cost,
                    weapons = weaponSpots.Values.ToArray(),
                    props = propSpots.Values.ToArray(),
                };
                if (look.weapons.Length == 0 && (look.props.Length == 0 || !look.canRebuild)) return;
                asking = true;
                if (!BusWorkers.Run(() => Choose(look), pick => { asking = false; Act(me, pick); })) asking = false;
                else Looks++;
            }
        }

        /// <summary>Main thread: the worker's pick, checked against the live objects first.</summary>
        private static void Act(Character me, Pick pick)
        {
            try
            {
                if (me == null || me.GetIsDead() || Game.Game_isLoading || Game.RESOLVING) return;
                if (pick.hasWeapon && weapons.TryGetValue(pick.weapon, out Item it))
                {
                    if (it == null || it.inPlayerInventory || it.isBeingMoved) { weapons.Remove(pick.weapon); weaponSpots.Remove(pick.weapon); return; }
                    if (Vector3.Distance(it.transform.position, me.transform.position) > EnemyArms.HarvestRange.Value + 0.6f) { Track(it); return; } // it moved; the next look has it right
                    int got = ScrapFor(it);
                    string name = Strip(it.GetName());
                    Crunch(me, it.transform.position);
                    Map.instance.RemoveItem(it);
                    Untrack(it);
                    Object.Destroy(it.gameObject);
                    SetScrap(Scrap + got);
                    Harvested++;
                    Game.LOG($"Your scrap bot grinds {name} into {got} scrap (it carries {Scrap}).");
                    return;
                }
                if (pick.hasProp && props.TryGetValue(pick.prop, out Destructible d))
                {
                    int cost = EnemyArms.RebuildCost.Value;
                    if (d == null || !d.triggered) { props.Remove(pick.prop); propSpots.Remove(pick.prop); return; }
                    if (cost <= 0 || Scrap < cost) return;
                    snaps.TryGetValue(d, out Snap snap);
                    if (!Rebuild(d, snap)) { UntrackProp(d); return; }
                    UntrackProp(d);
                    SetScrap(Scrap - cost);
                    Rebuilt++;
                    Crunch(me, d.transform.position);
                    AudioManager.PlayEvent("event:/Abilities/Engineer/Engineer_Craft", d.transform.position);
                    Game.LOG($"Your scrap bot rebuilds the {PropName(d)} with {cost} scrap (it carries {Scrap}).");
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Scrap bot: " + e.Message); }
        }

        private static string PropName(Destructible d)
        {
            string n = System.Text.RegularExpressions.Regex.Replace(d.gameObject.name.Replace("(Clone)", ""), @"^(PROP|DECO|DEST|DESTRUCTIBLE)_+", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            n = System.Text.RegularExpressions.Regex.Replace(n, @"[_\d]+", " ").Trim().ToLowerInvariant();
            return n.Length == 0 ? "prop" : n;
        }

        /// <summary>Puts a broken prop back as it stood: its debris gone, its body, collider and parts restored.</summary>
        internal static bool Rebuild(Destructible d, Snap snap)
        {
            if (d == null || !d.triggered || DoorOf(d) != null) return false;
            if (snap == null && !RebuildsAfterLoad(d)) return false;
            var tr = Traverse.Create(d);
            GameObject debris = tr.Field("spawned").GetValue<GameObject>();
            if (debris != null) Object.Destroy(debris);
            tr.Field("spawned").SetValue(null);
            d.gameObject.SetActive(true);
            if (snap != null)
            {
                d.transform.SetPositionAndRotation(snap.pos, snap.rot);
                d.gameObject.layer = snap.layer;
                var body = d.GetComponent<Rigidbody>();
                if (body != null && snap.hadBody)
                {
                    if (!body.isKinematic) { body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
                    body.isKinematic = snap.kinematic;
                }
                if (snap.deco != int.MinValue) d.GetComponent<Decoration>()?.SetInteger(snap.deco);
                if (snap.also != null) d.alsoTriggers = new List<Destructible>(snap.also);
            }
            var col = tr.Field("myCollider").GetValue<Collider>();
            if (col != null) { col.enabled = snap?.colliderOn ?? true; col.isTrigger = snap?.isTrigger ?? col.isTrigger; }
            d.linkedItems = new List<Item>();
            d.triggered = false;
            Actions.SpawnVfx("PA_Engineer_Boltron.PS_summon", d.transform.position);
            return true;
        }

        // ---- selling

        /// <summary>Opening a vendor: the bot sells what it doesn't keep for rebuilding.</summary>
        [HarmonyPatch(typeof(Vendor), nameof(Vendor.Interact))]
        private static class Patch_Vendor
        {
            private static void Postfix(Vendor __instance)
            {
                if (!Running || __instance == null || __instance.isLocked || Bot() == null) return;
                try { Sell(__instance.GetName()); }
                catch (Exception e) { Plugin.Log.LogWarning("Scrap bot sale: " + e.Message); }
            }
        }

        internal static int Sell(string vendor)
        {
            int extra = Scrap - Math.Max(0, EnemyArms.KeepScrap.Value);
            int price = Math.Max(0, EnemyArms.ScrapPrice.Value) * (Game.FLOOR + 1);
            if (extra <= 0 || price <= 0) return 0;
            int credits = extra * price;
            SetScrap(Scrap - extra);
            Menu_Inventory.instance.AdjustCredits(credits);
            Sold += extra;
            Game.LOG($"Your scrap bot sells {extra} scrap to {Strip(vendor)} for <color=#FFD54A>{credits} credits</color> (it keeps {Scrap}).");
            return credits;
        }

        // ---- the kit and the body

        internal static Item BuildKit(Transform holder)
        {
            Item src = ItemRegistry.VanillaItem("CONSUME_Engineer_MechParts");
            if (src == null) return null;
            GameObject go = Object.Instantiate(src.gameObject, holder, false);
            go.name = EnemyArms.KitId;
            var it = go.GetComponent<Item_Consumable>();
            it.itemId = EnemyArms.KitId;
            it.itemInstanceId = "";
            it.itemName = ClassRegistry.Term("ITEM_" + EnemyArms.KitId, "Name", "Scrap Bot Kit");
            it.itemDescription = ClassRegistry.Term("ITEM_" + EnemyArms.KitId, "Desc",
                "Use it to put together a scrap bot: heavily plated, it follows you from floor to floor and grinds enemy weapons into scrap, the lesser ones it finds lying near it and the ones in your pack when you ask from its menu. It welds that scrap onto itself when it's hurt, rebuilds the floor's broken props with it and sells what it doesn't need to vendors. If it's wrecked, you rebuild it on the next floor.");
            it.tags.RemoveAll(t => t != null && t.tagName == "MECHPARTS");
            it.isStackable = false;
            it.override_stackMax = 1;
            it.stack = 1;
            it.itemValue = it.stackValue = 60;
            return it;
        }

        [HarmonyPatch(typeof(Item_Consumable), nameof(Item_Consumable.UseRequest))]
        private static class Patch_UseKit
        {
            private static bool Prefix(Item_Consumable __instance)
            {
                if (__instance == null || __instance.itemId != EnemyArms.KitId) return true;
                if (Game.LOCK || Game.RESOLVING || Game.TARGETING) return false;
                try
                {
                    if (!EventBus.On) { Game.LOG("The scrap bot needs the event bus: turn on [Performance] EventBus in Mod Settings."); return false; }
                    if (Bot() != null) { Game.LOG("Your scrap bot is already running."); return false; }
                    Player.instance.SetTag(EnemyArms.OwnedTag, 1);
                    if (Deploy() == null) { Player.instance.RemoveTag(EnemyArms.OwnedTag); Game.LOG("There's no room here to set the scrap bot down."); return false; }
                    Menu_Inventory.instance.RemoveItem(__instance, null, isSilent: true);
                    Object.Destroy(__instance.gameObject);
                    Menu_Inventory.instance.Refresh();
                }
                catch (Exception e) { Plugin.Log.LogError("Scrap bot kit: " + e); }
                return false;
            }
        }

        internal static Character Bot()
        {
            Character c = AI_Manager.instance?.FindCharacterByTag(EnemyArms.BotTag);
            return c != null && !c.GetIsDead() ? c : null;
        }

        private static Character junkerPrefab;

        /// <summary>The Engineer's Junker body: a small scavenger bot.</summary>
        private static Character Body()
        {
            if (junkerPrefab != null) return junkerPrefab;
            junkerPrefab = Resources.FindObjectsOfTypeAll<PA_Engineer_Junker>().Select(j => j.characterPrefab).FirstOrDefault(c => c != null)
                ?? AI_Manager.instance?.allCharacters?.FirstOrDefault(c => c != null && c.internalName != null && c.internalName.ToUpperInvariant().Contains("JUNKER"));
            if (junkerPrefab == null) Plugin.Log.LogWarning("Scrap bot: no Junker body found");
            return junkerPrefab;
        }

        internal static Character Deploy()
        {
            Player p = Player.instance;
            Character body = Body();
            NavNode at = p?.GetMover()?.GetCurrentNavNode();
            if (body == null || at?.room == null) return null;
            NavNode spot = at.room.GetNavNodesIn(p.transform.position, 4f).FirstOrDefault(n => n != null && n.doorNode == null && !n.IsOccupied() && !n.BlockCheck());
            if (spot == null) return null;
            Character bot = AI_Manager.instance.Spawn_Character(body, spot.transform.position, Quaternion.identity, AI.AI_TEAM.Player);
            bot.level = p.level;
            Effect scalar = EffectManager.instance.ApplyEffect("Effect_PASSIVE_Pet_Scalar", bot);
            if (scalar != null) { scalar.caster = p; scalar.amountA = 12; scalar.amountC = 15f; }
            bot.CalculateStats();
            bot.SetTag("MINION", 1);
            bot.SetTag(EnemyArms.BotTag, 1);
            bot.SetTag("NOXP", 1);
            Dress(bot);
            bot.SetHealth(bot.GetHealthMax(), bot.GetHealthMax()); // built whole, plating and all
            AI_Manager.instance.RegisterMinion(bot);
            AI_Manager.instance.RefreshPlayerMinionList();
            try { bot.anim?.Play("Spawn"); } catch { }
            Actions.SpawnVfx("PA_Engineer_Boltron.PS_summon", bot.transform.position);
            AudioManager.PlayEvent("event:/Abilities/Engineer/Engineer_Craft", bot.transform.position);
            Game.LOG("Your <color=#C79750>scrap bot</color> whirs to life.");
            return bot;
        }

        private static void Dress(Character bot)
        {
            bot.SetName("Scrap Bot");
            if (bot.GetComponent<Brain>() == null) bot.gameObject.AddComponent<Brain>();
            Plate(bot);
        }

        /// <summary>Heavy plating: armour, a big health pool and a knack for blocking, growing with the floor and your level.</summary>
        private static void Plate(Character bot)
        {
            Effect plate = bot.effects.GetEffect("EFFECT_Engineer_Upgrade") ?? EffectManager.instance.ApplyEffect("EFFECT_Engineer_Upgrade", bot);
            if (plate == null) return;
            int lvl = Math.Max(1, Player.instance != null ? Player.instance.level : bot.level);
            plate.attributeMods = new List<StatObj>
            {
                new StatObj(STAT_TYPE.Armor, EnemyArms.BotArmor.Value + 2 * Game.FLOOR),
                new StatObj(STAT_TYPE.HealthMax, EnemyArms.BotHealth.Value + 10 * lvl),
                new StatObj(STAT_TYPE.Block, 25),
                new StatObj(STAT_TYPE.Toughness, 3 + lvl / 2),
            };
            bot.CalculateStats();
        }

        /// <summary>Main thread: a damaged bot patches itself with its own scrap before it does anything else.</summary>
        private static bool Repair(Character me)
        {
            int cost = EnemyArms.RepairCost.Value, max = me.GetHealthMax(), hp = me.GetHealth();
            if (cost <= 0 || Scrap < cost || hp >= max * 0.6f) return false;
            int amount = Math.Max(1, Mathf.RoundToInt(max * EnemyArms.RepairPercent.Value / 100f));
            me.SetHealth(Math.Min(max, hp + amount), max);
            SetScrap(Scrap - cost);
            Repaired++;
            Crunch(me, me.transform.position);
            Game.LOG($"Your scrap bot welds {cost} scrap onto itself (+{Math.Min(amount, max - hp)} health; it carries {Scrap}).");
            return true;
        }

        internal static int Repaired;

        /// <summary>Each new floor (and each load): the ledger is redone, and the bot is there, the saved one found again or rebuilt.</summary>
        [HarmonyPatch(typeof(Level), nameof(Level.OnPlayerEntered))]
        private static class Patch_Entered
        {
            private static void Postfix()
            {
                if (!EnemyArms.Enabled.Value) return;
                Plugin.Instance.StartCoroutine(EnsureSoon());
            }
        }

        private static IEnumerator EnsureSoon()
        {
            yield return new WaitForSeconds(1.5f);
            try
            {
                if (Game.Game_isLoading || Player.instance == null) yield break;
                asking = false;
                Rescan();
                if (!Running || !Player.instance.HasTag(EnemyArms.OwnedTag)) yield break;
                Character bot = Bot();
                if (bot != null) Dress(bot);
                else Deploy();
            }
            catch (Exception e) { Plugin.Log.LogError("Scrap bot: " + e); }
        }
    }
}
