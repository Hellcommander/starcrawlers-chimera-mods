using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SCClassLoader
{
    /// <summary>A faction from a mod's "factions" list (Mods/Factions has the bundled ones).</summary>
    public class FactionSpec
    {
        public string id;
        public string name;
        public string @short;
        public string color = "#FFFFFF";
        public string description;
        public int startRep;
        public List<RivalSpec> rivals = new List<RivalSpec>();
        /// <summary>Job types it hires for, and job types that can be run against it: retrieve | collect | exterminate | assassinate | plant.</summary>
        public List<string> runsFor = new List<string>();
        public List<string> runsAgainst = new List<string>();
        /// <summary>Its stolen-goods item (this mod's id or "Mod:Id").</summary>
        public string goods;
        public BoonSpec boon;
        /// <summary>Old single favour reward; folded into rewards.</summary>
        public FavourSpec favour;
        public bool enabled = true;
        /// <summary>Known from the start; others turn up on a board, in an echo, or through another contact.</summary>
        public bool discovered = true;
        /// <summary>What its people in the echoes are called ("Halyard runner").</summary>
        public string operative = "operative";
        public ContactSpec contact;
        /// <summary>What calling in favours can get you.</summary>
        public List<RewardSpec> rewards = new List<RewardSpec>();
        /// <summary>What it does to you when its favour runs out.</summary>
        public List<OperationSpec> operations = new List<OperationSpec>();
        /// <summary>Events its people stage in the echoes, with options that depend on your standing.</summary>
        public List<EncounterSpec> encounters = new List<EncounterSpec>();

        [JsonIgnore] internal ModPack pack;
        [JsonIgnore] internal string goodsId;
        internal string Short => string.IsNullOrEmpty(@short) ? name : @short;
        internal string Tinted(string text = null) => $"<color={color}>{text ?? Short}</color>";
    }

    public class RivalSpec
    {
        public string id;
        public int weight = 10;
    }

    /// <summary>Passive bonus while you're Valued or better; it grows a step per tier (Valued 1 ... Allied 4).</summary>
    public class BoonSpec
    {
        public string name;
        public string description;
        public Dictionary<string, int> stats;
        public float damageDealtPerStack;
    }

    /// <summary>Your person on the inside: who picks up when you call.</summary>
    public class ContactSpec
    {
        public string name;
        public string title;
        public string bio;
        /// <summary>A line per standing: any tier name (Wanted ... Allied), or the groups "hostile" (Hostile or worse),
        /// "cold" (Suspicious), "neutral", "warm" (Valued, Respected) and "close" (Trusted, Allied).</summary>
        public Dictionary<string, string> mood = new Dictionary<string, string>();
        /// <summary>"owed" (you owe them), "none", "some" (1-4), "plenty" (5+).</summary>
        public Dictionary<string, string> favourLines = new Dictionary<string, string>();
    }

    /// <summary>A favour you can call in.</summary>
    public class RewardSpec
    {
        public string name;
        public string text;
        public int favour = 3;
        /// <summary>Reputation needed to be offered it at all.</summary>
        public int minRep = -10;
        public string loot;
        public string rarity = "Rare";
        public int credits;
        public int xp;
        /// <summary>Also clears their pending operation against you.</summary>
        public bool callOff;
    }

    /// <summary>
    /// Something a faction does to you once its favour has run out (as in the first game): it's queued when favour
    /// is at or below favourAtMost and reputation at or below repAtMost, a warning reaches you, and it fires
    /// after delay steps (each new floor or echo is a step).
    /// </summary>
    public class OperationSpec
    {
        public string id;
        public string name;
        /// <summary>ambush (a hit team where you are) | hunter (a bounty hunter comes for you) | squeeze (credits)</summary>
        public string type = "ambush";
        public int favourAtMost = -2;
        public int repAtMost = -26;
        public int[] delay = { 1, 3 };
        public string warning;
        public string text;
        /// <summary>ambush: team size; hunter: health multiplier; squeeze: percent of your credits.</summary>
        public int strength = 3;
    }

    /// <summary>A standing-gated event in an echo (the first game's faction-gated event options).</summary>
    public class EncounterSpec
    {
        public string id;
        public string title;
        public string text;
        /// <summary>Only offered while your reputation with this faction is in this range.</summary>
        public int minRep = -100;
        public int maxRep = 100;
        public List<OptionSpec> options = new List<OptionSpec>();
    }

    public class OptionSpec
    {
        public string text;
        public ConditionSpec requires;
        public List<OutcomeSpec> outcome = new List<OutcomeSpec>();
        /// <summary>What happens, shown after picking it.</summary>
        public string result;
    }

    public class ConditionSpec
    {
        public int? repAtLeast;
        public int? repAtMost;
        /// <summary>Favour needed; spent when spendFavour.</summary>
        public int favour;
        public bool spendFavour;
        /// <summary>Credits paid.</summary>
        public int credits;
        public string flag;
        public string notFlag;
        /// <summary>A class tree you need (any rank), e.g. SKILLTREE_Hacker.</summary>
        public string skill;
    }

    public class OutcomeSpec
    {
        /// <summary>Whose standing changes: this faction by default, or another faction's id.</summary>
        public string faction;
        public int rep;
        public int favour;
        public int credits;
        public int xp;
        public string loot;
        public string rarity = "Rare";
        /// <summary>Restores this fraction of your health (0.5 = half).</summary>
        public float heal;
        /// <summary>The faction's people turn on you right here.</summary>
        public bool fight;
        /// <summary>You get one of the faction's goods (no theft penalty: they handed it over).</summary>
        public bool goods;
        public string discover;
    }

    /// <summary>What calling in a favour gets you.</summary>
    public class FavourSpec
    {
        public string text;
        /// <summary>LOOT_TYPE: Weapon, Gear, Consumable, Random, ...</summary>
        public string loot = "Random";
        public string rarity = "Rare";
        public int credits;
        public int cost = 3;
    }

    /// <summary>
    /// Standing with the factions, after the first StarCrawlers: reputation -100..100 in nine tiers, favour -10..10
    /// that drifts toward your tier after every side area, and the Betrayed / Deceitful marks that cap a faction's
    /// reputation at 0 for the rest of the run. Saved per run in the mod's save slot.
    /// </summary>
    internal static class Factions
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<KeyCode> StandingKey;

        internal static readonly List<FactionSpec> All = new List<FactionSpec>();
        internal const string EffectClass = "SCCL_Factions";

        internal static bool Active => Enabled.Value && All.Count > 0;

        internal static void Bind(ConfigFile cfg)
        {
            Enabled = cfg.Bind("Factions", "Enabled", true, "Factions offer jobs at side-area breaches, and track your standing with them. Off = breaches work as before.");
            StandingKey = cfg.Bind("Factions", "StandingKey", KeyCode.F9, "Opens the faction standing screen.");
        }

        internal static FactionSpec Get(string id) => All.FirstOrDefault(f => string.Equals(f.id, id, StringComparison.OrdinalIgnoreCase));

        // ---- loading ------------------------------------------------------------------------------------------

        internal static void Load()
        {
            All.Clear();
            foreach (ModPack p in ModPacks.Ordered)
                foreach (var (file, obj) in p.data)
                    foreach (JObject e in (obj["factions"] as JArray ?? new JArray()).OfType<JObject>())
                    {
                        FactionSpec f;
                        try { f = e.ToObject<FactionSpec>(); }
                        catch (Exception ex) { Plugin.Log.LogError($"{file}: a faction could not be read: {ex.Message}"); continue; }
                        if (string.IsNullOrEmpty(f.id)) { Plugin.Log.LogError($"{file}: a faction is missing \"id\""); continue; }
                        All.RemoveAll(x => string.Equals(x.id, f.id, StringComparison.OrdinalIgnoreCase)); // a later mod replaces it
                        if (!f.enabled) continue;
                        f.pack = p;
                        f.goodsId = string.IsNullOrEmpty(f.goods) ? null : Crafting.ResolveItem(f.goods, p);
                        f.runsFor = (f.runsFor ?? new List<string>()).Select(x => x.ToLowerInvariant()).ToList();
                        f.runsAgainst = (f.runsAgainst ?? new List<string>()).Select(x => x.ToLowerInvariant()).ToList();
                        f.rewards = f.rewards ?? new List<RewardSpec>();
                        if (f.rewards.Count == 0 && f.favour != null)
                            f.rewards.Add(new RewardSpec { name = "A favour", text = f.favour.text, favour = f.favour.cost, loot = f.favour.loot, rarity = f.favour.rarity, credits = f.favour.credits });
                        f.operations = f.operations ?? new List<OperationSpec>();
                        for (int i = 0; i < f.operations.Count; i++) if (string.IsNullOrEmpty(f.operations[i].id)) f.operations[i].id = "op" + i;
                        f.encounters = f.encounters ?? new List<EncounterSpec>();
                        All.Add(f);
                    }
            foreach (FactionSpec f in All)
                foreach (RivalSpec r in f.rivals ?? new List<RivalSpec>())
                    if (Get(r.id) == null) Plugin.Log.LogWarning($"Faction {f.id}: rival '{r.id}' isn't a loaded faction");
            if (All.Count > 0) Plugin.Log.LogInfo($"Factions: {string.Join(", ", All.Select(f => f.id))}");
            RegisterEffects();
        }

        /// <summary>Boons, plus the marks put on enemies by jobs and hit teams.</summary>
        private static void RegisterEffects()
        {
            var effects = new List<EffectSpec>
            {
                new EffectSpec { id = "ContractMark", name = "Contract Mark", icon = "contract_mark.png", debuff = true, duration = -1,
                    description = "Somebody is paying for this one. Put it down." },
                new EffectSpec { id = "MarkedTarget", name = "Marked Target", icon = "contract_mark.png", debuff = true, duration = -1,
                    description = "The name at the top of the contract: tougher than the rest, and expecting company.", damageDealtMult = 1.2f, damageTakenMult = 0.75f },
                new EffectSpec { id = "HitTeam", name = "Hit Team", icon = "hit_team.png", duration = -1,
                    description = "Sent in by a faction that wants you dead.", damageDealtMult = 1.25f, damageTakenMult = 0.8f },
                new EffectSpec { id = "Hunter", name = "Bounty Hunter", icon = "hit_team.png", duration = -1,
                    description = "Somebody put a price on you, and this one means to collect.", damageDealtMult = 1.3f, damageTakenMult = 0.75f },
                new EffectSpec { id = "Operative", name = "Faction Operative", icon = "operative.png", duration = -1,
                    description = "Working for an outside faction. If they think well enough of you, they'll leave you alone; shoot first and they won't." },
                new EffectSpec { id = "Hardened", name = "Hard Job", icon = "hit_team.png", duration = -1, maxStacks = 3,
                    description = "This echo was a hard job: everything here hits harder and takes more to put down.", damageDealtPerStack = 0.1f, damageTakenPerStack = -0.06f },
            };
            foreach (FactionSpec f in All.Where(f => f.boon != null))
                effects.Add(new EffectSpec
                {
                    id = "Boon" + f.id, name = f.boon.name ?? f.name + " favour", icon = $"faction_{f.id.ToLowerInvariant()}.png",
                    description = (f.boon.description ?? "") + " Grows with your standing.", duration = -1, maxStacks = 4,
                    stats = f.boon.stats, damageDealtPerStack = f.boon.damageDealtPerStack,
                });
            string folder = All.FirstOrDefault()?.pack?.folder;
            var def = new ClassDef
            {
                id = EffectClass, name = "Factions", tiers = new List<TierDef>(), effects = effects,
                folder = folder != null ? System.IO.Path.Combine(folder, "icons") : null,
            };
            ClassRegistry.RegisterEffects(def, null);
        }

        internal static string EffectId(string name) => ModEffect.MakeId(EffectClass, name);

        // ---- saved standing -----------------------------------------------------------------------------------

        private static Dictionary<string, object> D => SideAreas.Data;

        internal static int Rep(FactionSpec f)
        {
            var d = D;
            return d != null && d.TryGetValue("fac_rep_" + f.id, out object v) && v is int i ? i : Mathf.Clamp(f.startRep, -100, 100);
        }

        internal static int Favour(FactionSpec f)
        {
            var d = D;
            return d != null && d.TryGetValue("fac_fav_" + f.id, out object v) && v is int i ? i : 0;
        }

        internal static bool Flag(FactionSpec f, string flag)
        {
            var d = D;
            return d != null && d.TryGetValue("fac_flags_" + f.id, out object v) && v is string s && s.Split(',').Contains(flag);
        }

        private static void SetFlag(FactionSpec f, string flag, bool on)
        {
            var d = D;
            if (d == null) return;
            var set = (d.TryGetValue("fac_flags_" + f.id, out object v) && v is string s ? s.Split(',') : new string[0]).Where(x => x.Length > 0).ToList();
            set.Remove(flag);
            if (on) set.Add(flag);
            d["fac_flags_" + f.id] = string.Join(",", set);
        }

        // ---- tiers --------------------------------------------------------------------------------------------

        internal static string Category(int rep) =>
            rep < -75 ? "Wanted" : rep <= -51 ? "Hated" : rep <= -26 ? "Hostile" : rep <= -11 ? "Suspicious" :
            rep <= 10 ? "Neutral" : rep <= 25 ? "Valued" : rep <= 50 ? "Respected" : rep <= 75 ? "Trusted" : "Allied";

        internal static string CategoryColor(int rep) =>
            rep < -50 ? "#FF5C5C" : rep <= -26 ? "#FF8F5C" : rep <= -11 ? "#FFC35C" : rep <= 10 ? "#C9D6E0" : rep <= 50 ? "#9FE08A" : "#5CFFB0";

        internal static string Tinted(int rep) => $"<color={CategoryColor(rep)}>{Category(rep)}</color>";

        /// <summary>Boon steps: Valued 1, Respected 2, Trusted 3, Allied 4.</summary>
        internal static int BoonTier(int rep) => rep > 75 ? 4 : rep > 50 ? 3 : rep > 25 ? 2 : rep > 10 ? 1 : 0;

        // ---- changes ------------------------------------------------------------------------------------------

        /// <summary>
        /// Changes reputation (clamped to -100..100) and keeps the marks:
        /// at -51 or below they hate you; climbing back to -10 from there marks you Deceitful.
        /// At 51 or above they trust you; dropping to 10 from there marks you Betrayed.
        /// Betrayed or Deceitful factions never go above 0 again this run.
        /// </summary>
        internal static void AdjustRep(FactionSpec f, int amount, string why = null)
        {
            if (f == null || amount == 0 || D == null) return;
            int before = Rep(f);
            int rep = Mathf.Clamp(before + amount, -100, 100);
            if (rep <= -51) SetFlag(f, "Hated", true);
            if (Flag(f, "Hated") && rep >= -10) { SetFlag(f, "Hated", false); SetFlag(f, "Deceitful", true); Game.LOG($"{f.Tinted()} remember what you were to them. <color=#FF8F5C>Deceitful</color>: they'll never trust you past neutral."); }
            if (rep >= 51 && !Flag(f, "Betrayed")) SetFlag(f, "Trusted", true);
            if (Flag(f, "Trusted") && rep <= 10) { SetFlag(f, "Trusted", false); SetFlag(f, "Betrayed", true); Game.LOG($"{f.Tinted()} trusted you, once. <color=#FF5C5C>Betrayed</color>: they'll never trust you past neutral."); }
            if ((Flag(f, "Betrayed") || Flag(f, "Deceitful")) && rep > 0) rep = 0;
            D["fac_rep_" + f.id] = rep;
            if (rep == before) return;
            string change = rep > before ? $"<color=#9FE08A>+{rep - before}</color>" : $"<color=#FF8F5C>{rep - before}</color>";
            Game.LOG($"{f.Tinted()} {change} ({rep}, {Tinted(rep)}){(string.IsNullOrEmpty(why) ? "" : ": " + why)}.");
            if (Category(rep) != Category(before)) Game.LOG($"{f.Tinted()} now see you as {Tinted(rep)}.");
            UpdateBoons();
        }

        internal static void AdjustFavour(FactionSpec f, int amount)
        {
            if (f == null || amount == 0 || D == null) return;
            D["fac_fav_" + f.id] = Mathf.Clamp(Favour(f) + amount, -10, 10);
        }

        /// <summary>After every side area: favour drifts with your tier (Allied +3 ... Wanted down to -3; neutral decays to 0).</summary>
        internal static void TickFavour()
        {
            foreach (FactionSpec f in All)
            {
                int rep = Rep(f), fav = Favour(f), d;
                switch (Category(rep))
                {
                    case "Allied": d = 3; break;
                    case "Trusted": d = Random.Range(2, 4); break;
                    case "Respected": d = Random.Range(1, 3); break;
                    case "Valued": d = Random.Range(0, 2); break;
                    case "Neutral": d = fav == 0 ? 0 : -Math.Sign(fav) * Math.Min(Math.Abs(fav) >= 4 ? Random.Range(1, 4) : Math.Abs(fav) == 3 ? Random.Range(1, 3) : 1, Math.Abs(fav)); break;
                    case "Suspicious": d = -Random.Range(0, 2); break;
                    case "Hostile": d = -Random.Range(0, 3); break;
                    case "Hated": d = -Random.Range(1, 3); break;
                    default: d = -Random.Range(1, 4); break; // Wanted
                }
                if (Flag(f, "Betrayed")) d -= Random.Range(1, 4);
                if (Flag(f, "Deceitful")) d -= 1;
                AdjustFavour(f, d);
            }
        }

        /// <summary>Why a reward can't be called in right now, or null if it can.</summary>
        internal static string CantCallIn(FactionSpec f, RewardSpec r)
        {
            if (Rep(f) < r.minRep) return $"needs {Category(r.minRep)} standing";
            if (Favour(f) < r.favour) return $"needs {r.favour} favour";
            return null;
        }

        /// <summary>Spends favour on one of the faction's rewards.</summary>
        internal static bool CallIn(FactionSpec f, RewardSpec r)
        {
            if (f == null || r == null || Player.instance == null || CantCallIn(f, r) != null) return false;
            AdjustFavour(f, -r.favour);
            GiveLoot(r.loot, r.rarity);
            if (r.credits > 0 && Menu_Inventory.instance != null) Menu_Inventory.instance.AdjustCredits(r.credits);
            if (r.xp > 0) GiveXp(r.xp);
            if (r.callOff) FactionOps.CallOff(f);
            Game.LOG($"{f.Tinted()}: {r.text ?? "they come through for you."}{(r.credits > 0 ? $" <color=#FFD966>+{r.credits} credits</color>" : "")}");
            AudioManager.PlayEvent("event:/UI/UI_CashRegister");
            return true;
        }

        internal static void GiveLoot(string loot, string rarity)
        {
            if (string.IsNullOrEmpty(loot) || !Enum.TryParse(loot, true, out LOOT_TYPE lt) || !Enum.TryParse(rarity ?? "Rare", true, out RARITY rar)) return;
            Item it = LootManager.instance.Generate_Loot(lt, rar);
            if (it != null) Menu_Inventory.Take(it);
        }

        internal static void GiveXp(int xp)
        {
            Player p = Player.instance;
            if (p == null || xp <= 0) return;
            p.AddXp(xp, p.level + 2, "contract"); // two levels up: the game pays it in full
        }

        // ---- discovery ----------------------------------------------------------------------------------------

        internal static bool Known(FactionSpec f)
        {
            var d = D;
            return f != null && (f.discovered || (d != null && d.TryGetValue("fac_known_" + f.id, out object v) && v is bool b && b));
        }

        internal static void Discover(FactionSpec f, string how = null)
        {
            if (f == null || Known(f) || D == null) return;
            D["fac_known_" + f.id] = true;
            Game.LOG($"<color=#FFD966>New contact:</color> {f.Tinted(f.name)}{(string.IsNullOrEmpty(how) ? "" : " (" + how + ")")}. {StandingKey.Value} to see them.");
        }

        // ---- the contact's lines ------------------------------------------------------------------------------

        internal static string MoodLine(FactionSpec f)
        {
            var m = f.contact?.mood;
            if (m == null) return "";
            string cat = Category(Rep(f));
            if (m.TryGetValue(cat, out string s)) return s;
            string group = cat == "Wanted" || cat == "Hated" || cat == "Hostile" ? "hostile" : cat == "Suspicious" ? "cold" : cat == "Neutral" ? "neutral"
                : cat == "Valued" || cat == "Respected" ? "warm" : "close";
            if ((Flag(f, "Betrayed") || Flag(f, "Deceitful")) && m.TryGetValue("betrayed", out string b)) return b;
            return m.TryGetValue(group, out s) ? s : "";
        }

        internal static string FavourLine(FactionSpec f)
        {
            var m = f.contact?.favourLines;
            if (m == null) return "";
            int fav = Favour(f);
            string key = fav < 0 ? "owed" : fav == 0 ? "none" : fav < 5 ? "some" : "plenty";
            return m.TryGetValue(key, out string s) ? s : "";
        }

        /// <summary>Puts each faction's boon on you at its current step (or takes it off).</summary>
        internal static void UpdateBoons()
        {
            Player p = Player.instance;
            if (p == null || p.effects == null || EffectManager.instance == null) return;
            foreach (FactionSpec f in All.Where(x => x.boon != null))
            {
                string id = EffectId("Boon" + f.id);
                int tier = BoonTier(Rep(f));
                Effect e = p.effects.GetEffect(id);
                if (tier <= 0) { if (e != null) { p.effects.RemoveEffect(id, true); p.CalculateStats(); } continue; }
                if (e == null)
                {
                    e = EffectManager.instance.ApplyEffect(id, p);
                    if (e == null) continue;
                    e.caster = p;
                    e.Activate();
                }
                if (e is ModEffect m) m.SetStacks(tier);
                p.CalculateStats();
            }
            if (Menu_HUD.instance != null) Menu_HUD.instance.UpdatePlayerEffects();
        }

        internal static string RivalsText(FactionSpec f) =>
            string.Join(", ", (f.rivals ?? new List<RivalSpec>()).Select(r => Get(r.id)).Where(x => x != null).Select(x => x.Tinted()));

        /// <summary>Whoever counts the faction as a rival, or is counted as one of its rivals.</summary>
        internal static IEnumerable<FactionSpec> EnemiesOf(FactionSpec f) =>
            All.Where(x => x != f && ((f.rivals ?? new List<RivalSpec>()).Any(r => string.Equals(r.id, x.id, StringComparison.OrdinalIgnoreCase))
                                       || (x.rivals ?? new List<RivalSpec>()).Any(r => string.Equals(r.id, f.id, StringComparison.OrdinalIgnoreCase))));
    }
}
