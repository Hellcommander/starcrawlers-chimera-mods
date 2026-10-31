using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace SCClassLoader
{
    /// <summary>One job on a breach's board.</summary>
    internal class Offer
    {
        public string employer, target, type;
        public int need, credits, tier;
        /// <summary>Optional extra: "goods" (bring out one of the target's goods) or "sweep" (leave none of the target's people standing).</summary>
        public string bonus = "";
        public int xp;

        internal FactionSpec Employer => Factions.Get(employer);
        internal FactionSpec Target => Factions.Get(target);

        internal string Ser() => $"{employer}|{target}|{type}|{need}|{credits}|{tier}|{bonus}|{xp}";

        internal static Offer Parse(string s)
        {
            string[] p = (s ?? "").Split('|');
            if (p.Length < 6) return null;
            int.TryParse(p[3], out int need); int.TryParse(p[4], out int cr); int.TryParse(p[5], out int tier);
            int xp = 0;
            if (p.Length > 7) int.TryParse(p[7], out xp);
            var o = new Offer { employer = p[0], target = p[1], type = p[2], need = need, credits = cr, tier = tier, bonus = p.Length > 6 ? p[6] : "", xp = xp };
            return o.Employer != null ? o : null;
        }

        internal static readonly string[] TierNames = { "Cakewalk", "Easy", "Normal", "Challenging", "Hard", "Very Hard", "Extreme" };
        private static readonly string[] TierColors = { "#7CE08A", "#9FE08A", "#FFD966", "#FFC35C", "#FF8F5C", "#FF6F5C", "#FF4A6A" };
        internal string TierName => $"<color={TierColors[Mathf.Clamp(tier + 2, 0, 6)]}>{TierNames[Mathf.Clamp(tier + 2, 0, 6)]}</color>";

        /// <summary>What to do, in a line.</summary>
        internal string Goal
        {
            get
            {
                string t = Target != null ? Target.Tinted() : "someone";
                switch (type)
                {
                    case "retrieve": return $"Recover the dead drop case {t} are sitting on";
                    case "collect": return $"Collect {need} dead drop cases before {t} do";
                    case "exterminate": return $"Put down {need} of {t}'s marked contacts";
                    case "assassinate": return $"Take out the marked target {t} are protecting";
                    case "plant": return $"Plant {need} tap{(need > 1 ? "s" : "")} on the beacon pads ({t} are listening in)";
                    default: return type;
                }
            }
        }

        internal string BonusText
        {
            get
            {
                string t = Target != null ? Target.Tinted() : "the target";
                return bonus == "goods" ? $"Bonus: also bring out one of {t}'s goods" : bonus == "sweep" ? $"Bonus: leave none of {t}'s people standing" : "";
            }
        }

        internal int BonusCredits => bonus == "" ? 0 : Mathf.FloorToInt(credits * 0.4f / 10f) * 10;
    }

    /// <summary>
    /// Jobs in the side areas. Stepping on a breach opens its job board: one offer per danger tier (Cakewalk to
    /// Hard, sometimes Very Hard and Extreme, as in the first game), each from an employer you've met, picked by
    /// your standing, against one of its rivals. Inside the echo the job places its objectives (dead drop cases,
    /// marked enemies, beacon pads); leaving by the way out turns it in. Every echo is also occupied by one faction's
    /// operatives, who leave you alone if that faction thinks well of you; it holds stolen goods, maybe one of a
    /// faction's encounters, and hit teams if a faction wants you dead. All state is in the mod's save slot.
    /// </summary>
    internal static class Contracts
    {
        internal const string CaseItem = "Factions:DeadDropCase";
        private const string MarkTag = "SCCLF_MARK", HitTag = "SCCLF_HIT", StarsTag = "SCCLF_STARS", TakenTag = "SCCLF_TAKEN", OpTag = "SCCLF_OP_";

        private static Dictionary<string, object> D => SideAreas.Data;

        // ---- the board ----------------------------------------------------------------------------------------

        internal static List<Offer> Board(int floor)
        {
            var d = D;
            if (d == null) return new List<Offer>();
            if (d.TryGetValue("board_" + floor, out object v) && v is string[] saved)
                return saved.Select(Offer.Parse).Where(o => o != null).ToList();
            var tiers = new List<int> { -2, -1, 0, 1, 2 };
            if (Random.value < 0.5f) tiers.Add(3);
            if (Random.value < 0.25f) tiers.Add(4);
            var offers = tiers.Select(t => Make(t, floor, null)).Where(o => o != null).ToList();
            // contacts you asked for work put a job of theirs on the next board
            foreach (FactionSpec f in Factions.All.Where(f => d.ContainsKey("req_" + f.id)).ToList())
            {
                d.Remove("req_" + f.id);
                Offer o = Make(0, floor, f);
                if (o != null && offers.Count > 0) offers[Random.Range(0, offers.Count)] = o;
            }
            d["board_" + floor] = offers.Select(o => o.Ser()).ToArray();
            foreach (Offer o in offers) { Factions.Discover(o.Employer, "hiring at the breach"); }
            return offers;
        }

        internal static void ClearBoard(int floor) => D?.Remove("board_" + floor);

        private static T Weighted<T>(IList<T> items, Func<T, float> w)
        {
            float total = items.Sum(x => Math.Max(0f, w(x)));
            if (total <= 0f) return items.Count > 0 ? items[Random.Range(0, items.Count)] : default;
            float roll = Random.value * total;
            foreach (T x in items) { roll -= Math.Max(0f, w(x)); if (roll <= 0f) return x; }
            return items.Last();
        }

        /// <summary>Base pay for a job on this floor.</summary>
        internal static int Pay(int floor) => 60 + 30 * Math.Max(0, floor);

        /// <summary>Pay by danger, as the first game scaled it: -10% ... +25%.</summary>
        private static readonly float[] TierPay = { 0.9f, 0.95f, 1f, 1.05f, 1.1f, 1.15f, 1.25f };

        private static Offer Make(int tier, int floor, FactionSpec forced)
        {
            // employers are factions you've met (the board introduces them), likelier the better they think of you
            var employers = Factions.All.Where(f => f.runsFor.Count > 0 && (Factions.Known(f) || f.discovered)).ToList();
            if (employers.Count == 0) employers = Factions.All.Where(f => f.runsFor.Count > 0).ToList();
            if (employers.Count == 0) return null;
            FactionSpec emp = forced ?? Weighted(employers, f => (Factions.Rep(f) + 101f) / 201f);
            var rivals = (emp.rivals ?? new List<RivalSpec>()).Select(r => (spec: Factions.Get(r.id), r.weight)).Where(x => x.spec != null && x.spec != emp).ToList();
            FactionSpec tgt = rivals.Count > 0 ? Weighted(rivals, x => x.weight).spec : null;
            var types = emp.runsFor.Where(t => tgt == null || tgt.runsAgainst.Contains(t)).ToList();
            string type = types.Count > 0 ? types[Random.Range(0, types.Count)] : emp.runsFor.Contains("retrieve") ? "retrieve" : emp.runsFor[0];
            int need, bonus;
            switch (type)
            {
                case "collect": need = Random.Range(2, 5); bonus = 5 * Random.Range(0, 6); break;
                case "exterminate": need = Random.Range(3, 7); bonus = 5 * Random.Range(0, 7); break;
                case "assassinate": need = 1; bonus = 5 * Random.Range(0, 5); break;
                case "plant": need = Random.Range(1, 4); bonus = 5 * Random.Range(0, 4); break;
                default: type = "retrieve"; need = 1; bonus = 5 * Random.Range(-2, 3); break;
            }
            if (type != "retrieve" && type != "assassinate") need = Mathf.Clamp(need + (tier >= 2 ? 1 : tier <= -1 ? -1 : 0), 1, 8);
            float mod = TierPay[Mathf.Clamp(tier + 2, 0, 6)];
            int credits = Mathf.FloorToInt((Pay(floor) + 2 * bonus + 15 * (need - 1)) * mod * (1f + 0.1f * Math.Max(0, tier)) / 10f) * 10;
            int xp = Mathf.RoundToInt((25 + 12 * Math.Max(0, floor)) * (1f + 0.25f * tier) * (1f + 0.05f * need));
            string extra = tgt != null && Random.value < 0.5f ? (Random.value < 0.5f && tgt.goodsId != null ? "goods" : "sweep") : "";
            return new Offer { employer = emp.id, target = tgt?.id ?? "", type = type, need = need, credits = Math.Max(10, credits), tier = tier, bonus = extra, xp = Math.Max(5, xp) };
        }

        // ---- the active job -----------------------------------------------------------------------------------

        internal static Offer Job => D != null && D.TryGetValue("job", out object v) ? Offer.Parse(v as string) : null;
        private static int Progress => D != null && D.TryGetValue("job_progress", out object v) && v is int i ? i : 0;
        private static void SetProgress(int n) { if (D != null) D["job_progress"] = n; }

        internal static void Take(Offer o)
        {
            if (D == null || o == null) return;
            D["job"] = o.Ser();
            SetProgress(0);
            D.Remove("job_beacons");
            Factions.Discover(o.Target, "named in your job");
            Game.LOG($"<color=#FFD966>Job taken</color> for {o.Employer.Tinted()}: {o.Goal}. Leave the echo by the way out (the green pad) to turn it in.");
        }

        private static string CaseId => Crafting.ResolveItem(CaseItem, null);

        internal static int CasesCarried() =>
            Menu_Inventory.GetItems()?.Where(i => i != null && i.itemId == CaseId).Sum(i => Math.Max(1, i.stack)) ?? 0;

        /// <summary>Objectives done so far: cases in your bag, or marks down / taps planted.</summary>
        internal static int Done(Offer o) => o == null ? 0 : (o.type == "retrieve" || o.type == "collect") ? CasesCarried() : Progress;

        internal static bool BonusDone(Offer o)
        {
            if (o == null || o.bonus == "" || o.Target == null) return false;
            if (o.bonus == "goods") return GoodsCarried().Any(i => OwnerOf(i) == o.Target);
            return SideAreas.InSide && Map.instance != null && !Mobs(null, includeTagged: true).Any(c => c.HasTag(OpTag + o.Target.id));
        }

        internal static string TrackerText()
        {
            Offer o = Job;
            if (o == null || !SideAreas.InSide) return null;
            int done = Math.Min(Done(o), o.need);
            string state = done >= o.need ? "<color=#9FE08A>done: take the way out</color>" : $"<color=#FFD966>{done}/{o.need}</color>";
            string bonus = o.bonus == "" ? "" : $"\n<size=85%>{o.BonusText}: {(BonusDone(o) ? "<color=#9FE08A>yes</color>" : "<color=#9FB7C9>not yet</color>")}</size>";
            return $"{o.Employer.Tinted()} job ({o.TierName}): {o.Goal}  {state}{bonus}";
        }

        // ---- inside the echo ----------------------------------------------------------------------------------

        /// <summary>Called once a new echo is built and you stand in it.</summary>
        internal static void OnEchoBuilt()
        {
            if (!Factions.Active) return;
            Room arrival = Player.instance.GetCurrentRoom();
            Offer o = Job;
            try
            {
                if (o != null) PlaceObjectives(o, arrival);
                if (o != null && o.tier >= 2) Harden(o.tier - 1);
                Occupy(o, arrival);
                SpawnGoods(o, arrival);
                HitTeams(o, arrival);
                FactionEvents.PlaceEncounter(o, arrival);
                FactionOps.Step("echo");
            }
            catch (Exception e) { Plugin.Log.LogError("Factions: setting up the echo: " + e); }
        }

/// <summary>Every AI character on the map, passive ones included (the game's room query skips those).</summary>
        internal static IEnumerable<Character> AllCharacters()
        {
            // the list also keeps AIs from maps you've left: only those standing in this map's rooms count
            var rooms = new HashSet<Room>(Map.instance != null ? Map.instance.GetRooms().Where(r => r != null) : Enumerable.Empty<Room>());
            return (Traverse.Create(AI_Manager.instance).Field("ais").GetValue<List<AI>>() ?? new List<AI>())
                .Where(a => a != null && a.GetCharacter() != null && a.gameObject.activeInHierarchy && a.GetCharacter().GetMover()?.GetCurrentNavNode() != null
                            && rooms.Contains(a.GetCharacter().GetMover().GetCurrentNavNode().GetRoom()))
                .Select(a => a.GetCharacter()).Distinct();
        }

        private static List<Character> Mobs(Room avoid, bool includeTagged = false) =>
            AllCharacters().Where(c => avoid == null || c.GetMover().GetCurrentNavNode().GetRoom() != avoid)
                .Where(c => c != null && !c.GetIsDead() && c.ai != null && (c.ai.GetTeam() == AI.AI_TEAM.Hostile || (includeTagged && c.ai.GetBaseTeam() == AI.AI_TEAM.Passive))
                            && !c.HasTag("BOSS") && !c.HasTag("BOSSHEALTHBAR")
                            && (includeTagged || (!c.HasTag(MarkTag) && !c.HasTag(HitTag) && !c.tags.Any(t => t != null && t.tagName.StartsWith(OpTag)))))
                .Distinct().ToList();

        private static void ApplyFx(Character c, string effect, int stacks = 1) =>
            Actions.ApplyEffect(new ActionSpec { @do = "effect", effect = Factions.EffectId(effect), stacks = stacks },
                new ActionContext { caster = Player.instance, classId = Factions.EffectClass, sourceName = "Contract" }, c);

        private static void PlaceObjectives(Offer o, Room arrival)
        {
            switch (o.type)
            {
                case "retrieve":
                case "collect":
                    for (int i = 0; i < o.need; i++) Drop(CaseId, arrival, null);
                    break;
                case "exterminate":
                    {
                        var mobs = Mobs(arrival).OrderBy(_ => Random.value).Take(o.need).ToList();
                        foreach (Character c in mobs) Mark(c, "ContractMark", $"{o.Target?.Short ?? "Marked"} contact");
                        if (mobs.Count < o.need) { o.need = Math.Max(1, mobs.Count); D["job"] = o.Ser(); }
                        break;
                    }
                case "assassinate":
                    {
                        Character c = Mobs(arrival).OrderByDescending(m => m.GetHealthMax()).FirstOrDefault();
                        if (c == null) break;
                        Mark(c, "MarkedTarget", $"{o.Target?.Short ?? "The"} principal");
                        int hp = c.GetHealthMax() * (2 + Math.Max(0, o.tier));
                        c.SetHealth(hp, hp);
                        break;
                    }
                case "plant":
                    {
                        var spots = new List<float>();
                        for (int i = 0; i < o.need; i++)
                        {
                            NavNode n = SideAreas.RandomFreeNode(arrival);
                            if (n == null) continue;
                            spots.AddRange(new[] { n.transform.position.x, n.transform.position.y, n.transform.position.z });
                            PlaceBeacon(n.transform.position, o.Employer);
                        }
                        D["job_beacons"] = spots.ToArray();
                        if (spots.Count / 3 < o.need) { o.need = Math.Max(1, spots.Count / 3); D["job"] = o.Ser(); }
                        break;
                    }
            }
        }

        private static void Mark(Character c, string effect, string name)
        {
            c.AddTag(MarkTag);
            c.SetName(name);
            ApplyFx(c, effect);
        }

        /// <summary>Danger above Challenging: everything in the echo is tougher.</summary>
        private static void Harden(int stacks)
        {
            foreach (Character c in Mobs(null, includeTagged: true)) ApplyFx(c, "Hardened", Mathf.Clamp(stacks, 1, 3));
        }

        /// <summary>Puts an item on the floor of a random room away from the arrival point.</summary>
        private static Item Drop(string id, Room avoid, Action<Item> setup)
        {
            NavNode n = SideAreas.RandomFreeNode(avoid);
            Item item = n != null ? ItemDatabase.instance.GetItem(id) : null;
            if (item == null) return null;
            setup?.Invoke(item);
            Map.instance.AddItem(item);
            item.transform.position = n.transform.position;
            LootManager.instance.DoLootSpew(n.transform.position, item, 1f, n.GetRoom(), true, null, 0.3f);
            return item;
        }

        /// <summary>1-2 stolen goods per echo, most often the job target's (always one of theirs for a goods bonus).</summary>
        private static void SpawnGoods(Offer o, Room arrival)
        {
            var owners = Factions.All.Where(f => !string.IsNullOrEmpty(f.goodsId)).ToList();
            if (owners.Count == 0) return;
            int n = Random.Range(1, 3);
            for (int i = 0; i < n; i++)
            {
                bool forBonus = i == 0 && o?.bonus == "goods" && o.Target?.goodsId != null;
                FactionSpec owner = forBonus || (o?.Target != null && o.Target.goodsId != null && Random.value < 0.6f) ? o.Target : owners[Random.Range(0, owners.Count)];
                int stars = Mathf.Clamp(1 + Random.Range(0, Math.Max(1, Game.FLOOR / 2 + 1)) + (o != null ? Math.Max(0, o.tier) : 0), 1, 5);
                Drop(owner.goodsId, arrival, it => it.SetTag(StarsTag, stars));
            }
        }

        // ---- faction presence ---------------------------------------------------------------------------------

        /// <summary>The faction whose people are in this echo: the job's target, else any faction.</summary>
        internal static FactionSpec Occupier => D != null && D.TryGetValue("echo_owner", out object v) ? Factions.Get(v as string) : null;

        /// <summary>
        /// 2-4 of the echo's enemies are the occupier's operatives. Respected or better: they're on the clock, not
        /// at war with you, and leave you alone (shoot one and they all remember). Hostile or worse: they're out for
        /// you, as a hit team.
        /// </summary>
        private static void Occupy(Offer o, Room arrival)
        {
            FactionSpec f = o?.Target ?? Factions.All[Random.Range(0, Factions.All.Count)];
            D["echo_owner"] = f.id;
            var pool = Mobs(arrival);
            var ops = pool.OrderBy(_ => Random.value).Take(Random.Range(2, 5)).ToList();
            Plugin.Log.LogInfo($"Factions: {f.id} occupy the echo with {ops.Count} of {pool.Count} enemies");
            foreach (Character c in ops)
            {
                c.AddTag(OpTag + f.id);
                c.SetName($"{f.Short} {f.operative}");
                ApplyFx(c, Factions.Rep(f) <= -26 ? "HitTeam" : "Operative");
            }
            ApplyAttitude(f);
            if (ops.Count > 0)
                Game.LOG($"{f.Tinted()} have people in here. {(Factions.Rep(f) >= 26 ? "They know you; they'll keep their distance." : Factions.Rep(f) <= -26 ? "<color=#FF8F5C>They know you, too.</color>" : "They won't make exceptions for you.")}");
        }

        /// <summary>Operatives of a faction that respects you stand down; restored after a reload too.</summary>
        internal static void ApplyAttitude(FactionSpec f)
        {
            if (f == null || Map.instance == null) return;
            bool friendly = Factions.Rep(f) >= 26 && !(D != null && D.ContainsKey("echo_angered"));
            foreach (Character c in Mobs(null, includeTagged: true).Where(c => c.HasTag(OpTag + f.id)))
            {
                if (friendly) { c.ai.InitTeam(AI.AI_TEAM.Passive); c.ai.ChangeState(AI.AI_STATE.Idle); }
                else if (c.ai.GetBaseTeam() == AI.AI_TEAM.Passive) c.ai.InitTeam(AI.AI_TEAM.Hostile);
            }
        }

        /// <summary>Shooting at a friendly faction's people: -5 with them, and every one of them in the echo turns on you.</summary>
        [HarmonyPatch(typeof(AI), nameof(AI.AttackedBy))]
        private static class Patch_AttackOperative
        {
            private static void Prefix(AI __instance, Character c)
            {
                Character me = __instance.GetCharacter();
                if (me == null || c == null || __instance.GetBaseTeam() != AI.AI_TEAM.Passive || !(c == Player.instance || TileStacks.IsAlly(c))) return;
                FactionSpec f = Factions.All.FirstOrDefault(x => me.HasTag(OpTag + x.id));
                if (f == null || D == null) return;
                D["echo_angered"] = true;
                Factions.AdjustRep(f, -5, "you opened fire on their people");
                ApplyAttitude(f);
                foreach (Character o in Mobs(null, includeTagged: true).Where(x => x.HasTag(OpTag + f.id))) { o.ai.ChangeState(AI.AI_STATE.Active); o.ai.SetTarget(c); }
            }
        }

        /// <summary>Killing a faction's people costs 2 standing with it, unless your job is against them.</summary>
        [HarmonyPatch(typeof(Character), nameof(Character.Die))]
        private static class Patch_OperativeDown
        {
            private static void Prefix(Character __instance)
            {
                if (__instance == null || __instance.GetIsDead() || !SideAreas.InSide) return;
                FactionSpec f = Factions.All.FirstOrDefault(x => __instance.HasTag(OpTag + x.id));
                if (f == null) return;
                if (Job?.Target == f) return;
                Factions.AdjustRep(f, -2, $"you killed one of their {f.operative}s");
            }
        }

        // ---- hit teams ----------------------------------------------------------------------------------------

        /// <summary>
        /// A faction that hates you and has no favour left for you sends a hit team (the worst one; at most one team
        /// per echo). Hard jobs and worse always find a team from the target waiting.
        /// </summary>
        private static void HitTeams(Offer o, Room arrival)
        {
            FactionSpec who = null;
            float best = 0f;
            foreach (FactionSpec f in Factions.All)
            {
                int rep = Factions.Rep(f), fav = Factions.Favour(f);
                if (fav > 0) continue;
                float chance = rep < -75 ? 0.9f : rep <= -51 ? 0.6f : rep <= -26 ? 0.3f : 0f;
                if ((Factions.Flag(f, "Betrayed") || Factions.Flag(f, "Deceitful")) && rep <= 0) chance += 0.3f;
                chance += 0.04f * -fav;
                if (chance > best && Random.value < chance) { best = chance; who = f; }
            }
            if (who == null && o != null && o.tier >= 2) who = o.Target;
            if (who != null && HitTeam(who, arrival, 3, nearPlayer: false) > 0)
                Game.LOG($"<color=#FF8F5C>Word on the channels: {who.Tinted()} have put a hit team in here.</color>");
        }

        /// <summary>Turns up to `size` enemies in one room into the faction's hit team. Returns how many.</summary>
        internal static int HitTeam(FactionSpec who, Room avoid, int size, bool nearPlayer)
        {
            if (who == null || Map.instance == null) return 0;
            Vector3 me = Player.instance.transform.position;
            var rooms = Map.instance.GetRooms().Where(r => r != null && r != avoid && (AI_Manager.instance.GetMobsInRoom(r)?.Any(c => c != null && !c.GetIsDead() && c.ai != null && c.ai.GetTeam() == AI.AI_TEAM.Hostile) ?? false));
            Room room = (nearPlayer ? rooms.OrderBy(r => Vector3.Distance(r.transform.position, me)) : rooms.OrderBy(_ => Random.value)).FirstOrDefault();
            var team = (room != null ? AI_Manager.instance.GetMobsInRoom(room) : Mobs(avoid))
                .Where(c => c != null && !c.GetIsDead() && c.ai != null && c.ai.GetTeam() == AI.AI_TEAM.Hostile && !c.HasTag(MarkTag) && !c.HasTag(HitTag) && !c.HasTag("BOSS")).Take(size).ToList();
            foreach (Character c in team)
            {
                c.AddTag(HitTag);
                c.SetName($"{who.Short} hit man");
                ApplyFx(c, "HitTeam");
                if (nearPlayer) { c.ai.ChangeState(AI.AI_STATE.Active); c.ai.SetTarget(Player.instance); }
            }
            return team.Count;
        }

        // ---- beacons ------------------------------------------------------------------------------------------

        internal class Beacon : MonoBehaviour { }

        internal static TeleportPad PlacePad(Vector3 pos, string color, string name)
        {
            TeleportPad pad = Object.Instantiate(Game.instance.TELEPORTPAD_PREFAB, pos, Quaternion.identity);
            pad.name = name;
            pad.transform.SetParent(Map.instance.transform);
            pad.color_clear = ColorUtility.TryParseHtmlString(color ?? "#FFD966", out Color c) ? c : Color.yellow;
            if (pad.lineRenderer != null) Object.Destroy(pad.lineRenderer);
            foreach (ParticleSystem ps in pad.GetComponentsInChildren<ParticleSystem>(true)) { var m = ps.main; m.startColor = pad.color_clear; }
            pad.Init();
            Plugin.Instance.StartCoroutine(Tint(pad));
            return pad;
        }

        private static void PlaceBeacon(Vector3 pos, FactionSpec f) => PlacePad(pos, f?.color, "SCCL_JobBeacon").gameObject.AddComponent<Beacon>();

        private static System.Collections.IEnumerator Tint(TeleportPad pad)
        {
            yield return null;
            if (pad != null) pad.SetClear();
        }

        /// <summary>A reloaded echo gets its beacons, encounter and the occupier's attitude back.</summary>
        internal static void OnEchoRestored()
        {
            Offer o = Job;
            if (o != null && o.type == "plant" && D.TryGetValue("job_beacons", out object v) && v is float[] p)
                for (int i = 0; i + 2 < p.Length; i += 3) PlaceBeacon(new Vector3(p[i], p[i + 1], p[i + 2]), o.Employer);
            FactionEvents.RestoreEncounter();
            Plugin.Instance.StartCoroutine(AttitudeSoon());
        }

        private static System.Collections.IEnumerator AttitudeSoon()
        {
            yield return new WaitForSeconds(0.5f);
            ApplyAttitude(Occupier);
        }

        [HarmonyPatch(typeof(TeleportPad), nameof(TeleportPad.OnEnterNode))]
        private static class Patch_Beacon
        {
            private static bool Prefix(TeleportPad __instance, Character c)
            {
                if (__instance.GetComponent<Beacon>() == null) return true;
                if (c != Player.instance || Game.Game_isLoading) return false;
                Offer o = Job;
                if (o != null && D.TryGetValue("job_beacons", out object v) && v is float[] p)
                {
                    Vector3 at = __instance.transform.position;
                    var rest = new List<float>();
                    for (int i = 0; i + 2 < p.Length; i += 3)
                        if (Vector3.Distance(new Vector3(p[i], p[i + 1], p[i + 2]), at) > 0.5f) rest.AddRange(new[] { p[i], p[i + 1], p[i + 2] });
                    D["job_beacons"] = rest.ToArray();
                    SetProgress(Progress + 1);
                    Game.LOG($"{o.Employer.Tinted()}: tap planted ({Math.Min(Progress, o.need)}/{o.need}).");
                    AudioManager.PlayEvent("event:/Abilities/Hacker/Hacker_Hack");
                }
                Object.Destroy(__instance.gameObject);
                return false;
            }
        }

        // ---- marks and goods ----------------------------------------------------------------------------------

        [HarmonyPatch(typeof(Character), nameof(Character.Die))]
        private static class Patch_MarkDown
        {
            private static void Prefix(Character __instance) => MarkDown(__instance);
        }

        /// <summary>A contract mark taken out of the fight (killed, or tamed and so out of it for good) counts as down.</summary>
        internal static void MarkDown(Character c)
        {
            if (c == null || c.GetIsDead() || !c.HasTag(MarkTag)) return;
            c.RemoveTag(MarkTag);
            Offer o = Job;
            if (o == null || (o.type != "exterminate" && o.type != "assassinate")) return;
            SetProgress(Progress + 1);
            Game.LOG($"{o.Employer.Tinted()}: mark down ({Math.Min(Progress, o.need)}/{o.need}).");
        }

        /// <summary>Picking up a faction's goods: they notice, -2 per star (unless they handed it to you).</summary>
        [HarmonyPatch(typeof(Menu_Inventory), nameof(Menu_Inventory.AddToInventory), new[] { typeof(Item), typeof(bool), typeof(bool), typeof(UI_Item) })]
        private static class Patch_Goods
        {
            private static void Postfix(Menu_Inventory __instance, Item item)
            {
                if (!Factions.Active || __instance != Menu_Inventory.instance || item == null || !item.HasTag("SCCLF_GOODS") || item.HasTag(TakenTag)) return;
                FactionSpec owner = OwnerOf(item);
                item.SetTag(TakenTag, 1);
                if (owner != null) Factions.AdjustRep(owner, -2 * Stars(item), $"you took their {item.GetName(false)}");
            }
        }

        internal static FactionSpec OwnerOf(Item i) =>
            i == null ? null : Factions.All.FirstOrDefault(f => i.HasTag("SCCLF_OWNER_" + f.id));

        internal static int Stars(Item i) => Mathf.Clamp(i != null ? i.GetTag(StarsTag) : 1, 1, 5);

        /// <summary>A faction's goods, given to you (already "taken", so no penalty).</summary>
        internal static void GiveGoods(FactionSpec f, int stars)
        {
            if (f?.goodsId == null) return;
            Item it = ItemDatabase.instance.GetItem(f.goodsId);
            if (it == null) return;
            it.SetTag(StarsTag, Mathf.Clamp(stars, 1, 5));
            it.SetTag(TakenTag, 1);
            Menu_Inventory.Take(it);
        }

        internal static List<Item> GoodsCarried() =>
            Menu_Inventory.GetItems()?.Where(i => i != null && i.HasTag("SCCLF_GOODS")).ToList() ?? new List<Item>();

        // ---- turning in ---------------------------------------------------------------------------------------

        /// <summary>
        /// Leaving the echo: the job pays out (or fails), the bonus pays either way (the first game's alternative
        /// objectives), cases are handed over, favour drifts.
        /// </summary>
        internal static void OnReturn()
        {
            if (!Factions.Active || D == null) return;
            Offer o = Job;
            if (o != null)
            {
                bool pass = Done(o) >= o.need;
                bool extra = BonusDone(o);
                if (pass)
                {
                    Menu_Inventory.instance.AdjustCredits(o.credits);
                    AudioManager.PlayEvent("event:/UI/UI_CashRegister");
                    Game.LOG($"<color=#9FE08A>Job done</color> for {o.Employer.Tinted()}: <color=#FFD966>+{o.credits} credits</color>, +{o.xp} XP.");
                    Factions.GiveXp(o.xp);
                    Factions.AdjustRep(o.Employer, 5, "job done");
                    Factions.AdjustRep(o.Target, -5, "you worked against them");
                }
                else
                {
                    Game.LOG($"<color=#FF8F5C>Job failed</color> for {o.Employer.Tinted()} ({Math.Min(Done(o), o.need)}/{o.need}).");
                    Factions.AdjustRep(o.Employer, -5, "job failed");
                    Factions.AdjustRep(o.Target, -5, "they know who came for them");
                }
                if (extra)
                {
                    Menu_Inventory.instance.AdjustCredits(o.BonusCredits);
                    Game.LOG($"<color=#9FE08A>Bonus</color> for {o.Employer.Tinted()}: <color=#FFD966>+{o.BonusCredits} credits</color>.");
                    Factions.AdjustRep(o.Employer, 2, "the extra mile");
                    Factions.AdjustFavour(o.Employer, 1);
                    if (o.bonus == "goods")
                    {
                        Item g = GoodsCarried().FirstOrDefault(i => OwnerOf(i) == o.Target);
                        if (g != null) { Menu_Inventory.instance.RemoveItem(g, null, isSilent: true); Object.Destroy(g.gameObject); }
                    }
                }
                foreach (Item c in Menu_Inventory.GetItems().Where(i => i != null && i.itemId == CaseId).ToList())
                {
                    Menu_Inventory.instance.RemoveItem(c, null, isSilent: true);
                    Object.Destroy(c.gameObject);
                }
                D.Remove("job"); D.Remove("job_progress"); D.Remove("job_beacons");
            }
            D.Remove("echo_owner"); D.Remove("echo_angered");
            FactionEvents.ClearEncounter();
            if (!SideAreas.Revisit) Factions.TickFavour(); // once per breach, not every time you step back in and out
        }

        // ---- auctions -----------------------------------------------------------------------------------------

        internal class Bid
        {
            public FactionSpec bidder;
            public int credits, rep, favour;
            public RARITY item = RARITY.None;
            public bool fence;
        }

        /// <summary>Base value for goods on this floor (what a fair buyer starts from).</summary>
        private static int Retail() => 40 + 30 * Math.Max(0, Game.FLOOR);

        /// <summary>
        /// Up to three of the owner's enemies bid for stolen goods (more likely the better the goods), offering
        /// credits, reputation and favour; with several bidders each leans on credits, an item, or standing. A fence
        /// always takes it for a little cash and no questions. Seeded per item and floor, so reopening the board
        /// shows the same bids.
        /// </summary>
        internal static List<Bid> Bids(Item goods)
        {
            var bids = new List<Bid>();
            FactionSpec owner = OwnerOf(goods);
            int g = Stars(goods);
            var rng = new System.Random(goods.GetInstanceID() * 31 + Game.FLOOR * 7919);
            int R(int a, int bIncl) => rng.Next(a, bIncl + 1);
            float F(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            int retail = Retail();

            var pool = (owner != null ? Factions.EnemiesOf(owner) : Factions.All).Where(f => f != owner && Factions.Rep(f) > -76
                && !Factions.Flag(f, "Hated") && !Factions.Flag(f, "Betrayed") && !Factions.Flag(f, "Deceitful")).OrderBy(_ => rng.Next()).ToList();
            int p = 30 + 30 * g + R(0, 10 * g);
            int count = 0;
            if (rng.Next(100) < p) { count = 1; p += Math.Min(0, -50 + 20 * g); if (rng.Next(100) < p) { count = 2; p += Math.Min(0, -100 + 20 * g); if (rng.Next(100) < p) count = 3; } }
            float[] mult = { 0.8f, 0.85f, 0.9f, 0.95f, 1f };
            foreach (FactionSpec f in pool.Take(count))
            {
                var b = new Bid { bidder = f };
                float cr = retail * mult[g - 1] * F(0.9f, 1.1f) + (g > 1 ? retail * 0.05f * g : 0f);
                b.rep = Math.Min(5, (g - 1) + R(0, g));
                b.favour = Factions.Rep(f) > 10 ? Mathf.Clamp((g - 3) + R(-1, g - 1), 0, 3) : 0;
                if (count > 1)
                {
                    switch (rng.Next(3))
                    {
                        case 0: cr *= F(1.2f, 1.4f); b.rep = Math.Max(0, b.rep - 2); b.favour = Math.Max(0, b.favour - 1); break;
                        case 1: b.item = g > 2 && rng.Next(100) < 5 * g ? RARITY.Epic : RARITY.Rare; cr *= F(0.7f, 0.8f); break;
                        default: b.rep = Math.Max(b.rep, g + R(1, g)); b.favour = Factions.Rep(f) > 10 ? Math.Min(6, b.favour + R(1, 3)) : 0; cr *= F(0.8f, 0.9f); break;
                    }
                }
                b.credits = Math.Max(10, Mathf.RoundToInt(cr / 5f) * 5);
                bids.Add(b);
            }
            bids.Add(new Bid { fence = true, credits = Math.Max(1, Mathf.RoundToInt(retail * (F(0.1f, 0.2f) + F(0.05f * g, 0.06f * g)))) });
            return bids;
        }

        internal static string Describe(Bid b)
        {
            if (b.fence) return $"Fence it quietly: <color=#FFD966>{b.credits} credits</color>, nobody's standing changes";
            var parts = new List<string> { $"<color=#FFD966>{b.credits} credits</color>" };
            if (b.rep > 0) parts.Add($"<color=#9FE08A>+{b.rep}</color> standing");
            if (b.favour > 0) parts.Add($"<color=#7FE0FF>+{b.favour}</color> favour");
            if (b.item != RARITY.None) parts.Add($"a {b.item.ToString().ToLowerInvariant()} item");
            return $"{b.bidder.Tinted(b.bidder.name)} offers {string.Join(", ", parts)}";
        }

        internal static void Accept(Item goods, Bid b)
        {
            FactionSpec owner = OwnerOf(goods);
            int g = Stars(goods);
            Menu_Inventory.instance.RemoveItem(goods, null, isSilent: true);
            string name = goods.GetName(false);
            Object.Destroy(goods.gameObject);
            Menu_Inventory.instance.AdjustCredits(b.credits);
            AudioManager.PlayEvent("event:/UI/UI_CashRegister");
            if (b.fence) { Game.LOG($"The fence takes the {name}: <color=#FFD966>+{b.credits} credits</color>."); return; }
            Factions.Discover(b.bidder, "a buyer");
            Game.LOG($"{b.bidder.Tinted()} buy the {name}: <color=#FFD966>+{b.credits} credits</color>.");
            Factions.AdjustRep(b.bidder, b.rep, "the sale");
            Factions.AdjustFavour(b.bidder, b.favour);
            if (b.item != RARITY.None)
            {
                Item it = LootManager.instance.Generate_Loot(LOOT_TYPE.Random, b.item);
                if (it != null) Menu_Inventory.Take(it);
            }
            if (owner != null) Factions.AdjustRep(owner, -Math.Max(1, g), "their goods went to a rival");
        }
    }
}
