using System.Collections;
using System.Collections.Generic;
using System.Linq;
using I2.Loc;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// A player ability defined entirely by an AbilitySpec. Configuration is looked up by GameObject name in Awake,
    /// so clones made by the game (Instantiate of the skill tree) are always fully set up.
    /// </summary>
    public class ModAbility : Ability_Player
    {
        internal class Runtime
        {
            public AbilitySpec spec;
            public string classId;
            public string internalName;
            public Sprite icon;
            public LocalizedString name, desc, perkName, perkDesc;
        }

        internal const string Prefix = "SCCLAB_";
        internal static readonly Dictionary<string, Runtime> Specs = new Dictionary<string, Runtime>();

        private Runtime rt;
        private bool bound;

        internal AbilitySpec Spec { get { Bind(); return rt?.spec; } }
        internal Character Caster => caster;
        internal string ClassId { get { Bind(); return rt?.classId; } }
        internal bool Perked => rt?.spec.perk != null && perkPoints > 0;
        private PerkSpec Perk => Perked ? rt.spec.perk : null;
        private int Rank => skillTree != null ? Mathf.Max(0, skillTree.rank) : 0;

        /// <summary>The class's rank, counted like "rank:TREE" conditions: 1 when just taken.</summary>
        internal int ClassRank => Rank + 1;

        protected override void Awake()
        {
            Bind();
            base.Awake();
        }

        internal void Bind()
        {
            if (bound) return;
            string key = gameObject.name.Replace("(Clone)", "").Trim();
            if (!key.StartsWith(Prefix) || !Specs.TryGetValue(key.Substring(Prefix.Length), out rt)) return;
            bound = true;
            AbilitySpec s = rt.spec;
            internalName = rt.internalName;
            displayName = rt.name;
            description = rt.desc;
            perkDisplayName = rt.perkName;
            perkDescription = rt.perkDesc;
            perkName_replaces = false;
            perkDescription_replaces = false;
            displaySprite = rt.icon;
            perkSprite = null;
            isPassive = s.type == "passive";
            isInstant = s.instant;
            isMelee = s.melee;
            isRanged = !s.melee && s.range > 1.6f;
            hitChance = 100;
            if (perkPoints < 0 && s.perk != null) perkPoints = 0;
            useString = new LocalizedString[] { rt.name };
            targetStyle = isPassive ? TARGET_STYLE.None : s.target switch
            {
                "enemy" => TARGET_STYLE.Character,
                "pet" => TARGET_STYLE.Character,
                "floor" => TARGET_STYLE.Floor,
                "item" => TARGET_STYLE.Item,
                _ => TARGET_STYLE.None,
            };
            // Tooltips read damageList; show the first damage line.
            ActionSpec d = s.actions?.FirstOrDefault(a => a.@do == "damage");
            damageList = new List<DmgObj>();
            if (d != null && System.Enum.TryParse(d.type ?? "Crushing", true, out DAMAGE_TYPE dt))
                damageList.Add(new DmgObj(dt, d.min) { damageMax = d.max });
            RefreshStats();
        }

        /// <summary>The levels of your synergies this skill is paying for (0 when it sets no price).</summary>
        private int SynergyLevels => rt?.spec.synergyPenalty != null ? Synergies.LevelsOf(rt.classId) : 0;

        private float SynergyDamage => Mathf.Max(0.1f, 1f - SynergyLevels * (rt?.spec.synergyPenalty?.damage ?? 0) / 100f);

        internal void Reprice() { if (rt?.spec.synergyPenalty != null) RefreshStats(); }

        private void RefreshStats()
        {
            if (rt == null) return;
            Runes.Tuning t = Tuned;
            AbilitySpec s = t?.spec ?? rt.spec;
            PerkSpec p = Perk;
            range = s.target == "self" ? 0f : s.range + (p?.rangeDelta ?? 0f) + (t?.range ?? 0f);
            if (t != null && t.adds.ContainsKey("melee")) range = Mathf.Min(range, 1.6f); // rewritten into a swing: it reaches the next tile
            int priced = SynergyLevels;
            staminaCost = Mathf.Max(0, s.staminaCost + (p?.staminaDelta ?? 0) + (t?.stamina ?? 0) + (s.stackCost == null ? t?.cost ?? 0 : PoolGone ? StackCostAmount : 0) + priced * (rt.spec.synergyPenalty?.stamina ?? 0));
            cooldown =Mathf.Max(0, s.cooldown + (p?.cooldownDelta ?? 0) + (t?.cooldown ?? 0) + priced * (rt.spec.synergyPenalty?.cooldown ?? 0));
            // a rewrite (SkillMods) can move the aim and the delivery: the game reads these when you click the icon
            isInstant = s.instant;
            isMelee = s.melee;
            isRanged = !s.melee && range > 1.6f;
            targetStyle = isPassive ? TARGET_STYLE.None : s.target switch
            {
                "enemy" => TARGET_STYLE.Character,
                "pet" => TARGET_STYLE.Character,
                "floor" => TARGET_STYLE.Floor,
                "item" => TARGET_STYLE.Item,
                _ => TARGET_STYLE.None,
            };
            if (t != null && (t.element != null || t.damageSwaps.Count > 0) && damageList != null && damageList.Count > 0)
            {
                ActionSpec d = Runes.Apply(rt.internalName, "rules", rt.spec.actions, rt.spec, rt.classId, t).FirstOrDefault(a => a.@do == "damage");
                if (d != null && System.Enum.TryParse(d.type ?? "Crushing", true, out DAMAGE_TYPE dt)) damageList[0] = new DmgObj(dt, d.min) { damageMax = d.max };
            }
        }

        /// <summary>What the runes in this ability's sockets and the rewrites on it (passives, stances: SkillMods) add up to (null when none).</summary>
        private Runes.Tuning Tuned => rt != null ? Runes.For(rt.internalName, rt.spec, rt.spec.runeSlots > 0 ? OpenRuneSlots : 0, SkillMods.Active(this)) : null;

        /// <summary>The spec as rewritten right now (target, shape, particles...); the file's own when nothing rewrites it.</summary>
        internal AbilitySpec Eff => Tuned?.spec ?? rt?.spec;

        /// <summary>Everything worked into this skill right now, its augments included.</summary>
        internal Runes.Tuning Tuning { get { Bind(); return Tuned; } }

        /// <summary>The game asks on the click: a stance may have turned an aimed bolt into a nova or a laid trap since the hotbar was built.</summary>
        public override TARGET_STYLE GetTargetStyle()
        {
            Bind();
            if (rt != null) RefreshStats();
            return base.GetTargetStyle();
        }

        /// <summary>Rune sockets open at the class's current rank.</summary>
        internal int RankRuneSlots { get { Bind(); return Runes.OpenSlots(rt?.spec, ClassRank); } }

        /// <summary>Rune sockets working right now: the open ones an augment hasn't sealed.</summary>
        internal int OpenRuneSlots => Mathf.Max(0, RankRuneSlots - Augments.Sealed(this));

        private int StackCostAmount => Mathf.Max(0, (rt.spec.stackCost?.amount ?? 0) + (rt.spec.stackCost != null ? Tuned?.cost ?? 0 : 0));

        /// <summary>The pool this skill pays with no longer exists (its mod is gone): the amount moves to stamina, so the skill still works.</summary>
        private bool PoolGone => Actions.PoolGone(rt.spec.stackCost?.effect, rt.classId);

        private float Radius { get { Runes.Tuning t = Tuned; return Mathf.Max(t?.minRadius ?? 0f, (t?.spec ?? rt.spec).radius + (Perk?.radiusDelta ?? 0f) + (t?.radius ?? 0f)); } }

        /// <summary>An order to your summons rather than a move of your own; transformations leave it usable.</summary>
        internal bool IsCommand
        {
            get
            {
                Bind();
                if (rt == null || isPassive) return false;
                if (rt.spec.command.HasValue) return rt.spec.command.Value;
                var acts = rt.spec.actions ?? new List<ActionSpec>();
                bool onYou = rt.spec.target == "self" && rt.spec.radius <= 0f; // default-target actions then land on you
                bool AtSummons(ActionSpec a) { string to = (a.to ?? "").ToLowerInvariant(); return to == "summon" || to == "summons"; }
                bool Harmless(ActionSpec a) => AtSummons(a) || (a.to ?? "").ToLowerInvariant() == "self" || (onYou && string.IsNullOrEmpty(a.to));
                return acts.Any(AtSummons) && acts.All(Harmless) && !acts.Any(a => a.@do == "summon");
            }
        }

        private IEnumerable<ActionSpec> AllActions => Runes.Apply(rt.internalName, Perked ? "perk" : "base",
            (rt.spec.actions ?? new List<ActionSpec>()).Concat(Perk?.addActions ?? new List<ActionSpec>()), rt.spec, rt.classId, Tuned);

        public override void Refresh() { Bind(); RefreshStats(); }
        public override void PerkPointApplied() { base.PerkPointApplied(); RefreshStats(); }
        public override void PerkPointRemoved() { base.PerkPointRemoved(); RefreshStats(); }

        public override bool CanUse()
        {
            Bind();
            RefreshStats();
            if (rt == null || isPassive) return false;
            if (caster == null) caster = Player.instance;
            if (rt.spec.itemCost != null && !Crafting.HasAll(Costs)) return false;
            if (rt.spec.creditCost > 0 && caster == Player.instance && Menu_Inventory.instance != null && Menu_Inventory.instance.GetCredits() < rt.spec.creditCost) return false;
            if (rt.spec.stackCost?.effect != null && !PoolGone && Actions.StacksOn(caster, Actions.EffectId(rt.spec.stackCost.effect, rt.classId)) < StackCostAmount) return false;
            if (!string.IsNullOrEmpty(rt.spec.requires))
            {
                string req = rt.spec.requires;
                if (req.StartsWith("summon:", System.StringComparison.OrdinalIgnoreCase))
                {
                    if (Summons.Matching(req.Substring(7), new ActionContext { caster = caster, source = this, classId = rt.classId }).Count == 0) return false;
                }
                else if (!Conjoined.Requirement(req)) return false;
            }
            if (!Equipped.Check(rt.spec.equipped, caster)) { _useFail = "equipped"; return false; }
            if (rt.spec.usableWhen != null && !When.Check(rt.spec.usableWhen, MakeContext(), caster)) return false;
            return base.CanUse();
        }

        /// <summary>Clicking a skill your gear doesn't fit says what it needs (the game would repeat an older reason).</summary>
        public override void ProcessUseFail()
        {
            if (_useFail != "equipped" || rt == null) { base.ProcessUseFail(); return; }
            string msg = $"{GetDisplayName()} needs {Equipped.Requirement(rt.spec.equipped)}";
            Game.LOG(msg);
            HUD.instance?.MakeFct("<size=42>" + msg + " </size>");
            AudioManager.PlayEvent("event:/UI/UI_Error");
        }

        /// <summary>itemCost with item references resolved (lazily: mod items exist only after classes load).</summary>
        private List<CostSpec> Costs
        {
            get
            {
                foreach (CostSpec c in rt.spec.itemCost)
                    if (c.item != null && c.resolved == null) c.resolved = Crafting.ResolveItem(c.item, null);
                return rt.spec.itemCost;
            }
        }

        /// <summary>target "item": inventory items (not equipped, stash or shop items) matching itemTag/itemId.</summary>
        public override bool CanUseOn(Item i)
        {
            if (rt == null || rt.spec.target != "item" || i == null || !i.inPlayerInventory || i.inPlayerStash || i.inVendorInventory) return false;
            if (i is Item_Equipment eq && Menu_Player.instance.IsEquipped(eq)) return false;
            if (!string.IsNullOrEmpty(rt.spec.itemTag) && !i.HasTag(rt.spec.itemTag)) return false;
            if (!string.IsNullOrEmpty(rt.spec.itemId) && i.itemId != Crafting.ResolveItem(rt.spec.itemId, null)) return false;
            return true;
        }

        public override bool CanUseOn(Character c)
        {
            if (rt == null || c == null || c.GetIsDead() || c == caster || c.ai == null) return false;
            if (rt.spec.target == "pet")
            {
                // one of your own summons (of petGroup when set)
                if (c.ai.GetTeam() != AI.AI_TEAM.Player || !c.HasTag("MINION")) return false;
                if (!string.IsNullOrEmpty(rt.spec.petGroup) && !c.HasTag(Summons.GroupTag(rt.spec.petGroup))) return false;
                return RangeCheck(c.GetMover() != null ? c.GetMover().GetCurrentNavNode() : null);
            }
            AI.AI_TEAM team = c.ai.GetBaseTeam();
            if (team == AI.AI_TEAM.Player || team == AI.AI_TEAM.Passive) return false;
            if (rt.spec.targetWhen != null && !When.Check(rt.spec.targetWhen, MakeContext(), c)) return false;
            return base.CanUseOn(c);
        }

        public override bool CanUseOn(NavNode n)
        {
            if (rt == null || n == null || !RangeCheck(n)) return false;
            if (AllActions.Any(a => a.@do == "teleport"))
            {
                // Like the vanilla Warp: a free, walkable node in the room you are standing in.
                if (n.IsOccupied() || n.BlockCheck() || n.Block_Move) return false;
                NavNode here = caster != null && caster.GetMover() != null ? caster.GetMover().GetCurrentNavNode() : null;
                if (here == null || n.GetRoom() != here.GetRoom()) return false;
            }
            return true;
        }

        internal ActionContext MakeContext() => new ActionContext
        {
            caster = caster != null ? caster : Player.instance,
            source = this,
            classId = rt.classId,
            sourceName = displayName,
            rank = Rank,
            damageMult = (Perk?.damageMult ?? 1f) * (Tuned?.power ?? 1f) * SynergyDamage,
            chanceBonus = Perk?.chanceBonus ?? 0,
            hitVfx = Tuned?.impactVfx ?? rt?.spec?.impactVfx,
        };

        private static bool IsSelf(ActionSpec a) => (a.to ?? "").ToLowerInvariant() == "self";

        public override IEnumerator Use()
        {
            Bind();
            RefreshStats();
            if (caster == null) caster = Player.instance;
            Runes.Tuning tuned = Tuned;
            AbilitySpec s = tuned?.spec ?? rt.spec;
            ActionContext ctx = MakeContext();
            ctx.deferredSelfConsume = new Dictionary<string, int>();
            ctx.paced = new List<IEnumerator>();
            if (s.target != "self" && !AllActions.Any(x => x.@do == "echo")) Actions.LastUsed = this; // echo only replays offensive spells
            caster.ModStamina(-GetStaminaCost());
            if (rt.spec.itemCost != null && caster == Player.instance) foreach (CostSpec c in Costs) Crafting.Consume(c);
            if (rt.spec.stackCost?.effect != null && caster.effects.GetEffect(Actions.EffectId(rt.spec.stackCost.effect, rt.classId)) is ModEffect paid)
                paid.RemoveStacks(StackCostAmount);
            if (rt.spec.creditCost > 0 && caster == Player.instance)
            {
                Menu_Inventory.instance.AdjustCredits(-Mathf.Min(rt.spec.creditCost, Menu_Inventory.instance.GetCredits()));
                AudioManager.PlayEvent("event:/UI/UI_CashRegister");
            }
            if (s.target == "item")
            {
                ctx.item = Game.TARGET_I;
                Game.ClearTarget(ctx.item);
                Menu_Inventory.instance.ClearHighlight(null);
                Menu_Inventory.instance.ClearDim();
                if (ctx.item == null) yield break;
            }

            Character primary = null;
            NavNode node = null;
            Vector3 center = caster.GetMover().GetCurrentNavNode().transform.position;
            if (s.target == "enemy")
            {
                primary = Game.TARGET_C != null ? Game.TARGET_C : target;
                if (primary == null) yield break;
                center = primary.GetMover() != null && primary.GetMover().GetCurrentNavNode() != null ? primary.GetMover().GetCurrentNavNode().transform.position : primary.transform.position;
            }
            else if (s.target == "pet")
            {
                // the pet is the spot, never a victim; "phase" spends it and puts you where it was
                Character pet = Game.TARGET_C != null ? Game.TARGET_C : target;
                NavNode at = pet != null && pet.GetMover() != null ? pet.GetMover().GetCurrentNavNode() : null;
                if (at == null) yield break;
                center = at.transform.position;
                if (AllActions.Any(a => a.@do == "phase"))
                {
                    Actions.SpawnVfx(s.castVfx, pet.GetHitFx().position);
                    PetHandling.Unsummon(pet, GetDisplayName(), breakdown: false);
                    if (at.GetOccupant() == pet) at.ClearOccupant();
                    if (!at.IsOccupied() && !at.BlockCheck()) yield return Teleport(at);
                }
            }
            else if (s.target == "floor")
            {
                node = Game.TARGET_N != null ? Game.TARGET_N : targetNode;
                if (node == null) yield break;
                center = node.transform.position;
            }

            ModAudio.Play(s.sound, caster.GetHitFx().position, rt.classId);
            if (caster == Player.instance) DashCam.Showcase(s.thirdPerson);
            Actions.SpawnVfx(s.castVfx, caster.GetHitFx().position);
            if (s.melee && primary != null) Ability.DoWeaponAttack(caster, primary);
            Vector3 from = caster.GetMover().GetCurrentNavNode().transform.position;
            bool shaped = Shapes.Is(s.shape);
            if (shaped && !string.IsNullOrEmpty(s.travelVfx))
            {
                // the sweep: one streak down a line, three across a cone
                float flight = 0f;
                foreach (Vector3 end in Shapes.Ends(from, center, s.shape, range, s.angle))
                    flight = Mathf.Max(flight, Actions.SpawnTravel(s.travelVfx, caster.GetHitFx().position, end + Vector3.up * 0.9f, s.travelSpeed));
                if (flight > 0f) yield return new WaitForSeconds(flight * 0.6f);
            }
            else if (!string.IsNullOrEmpty(s.travelVfx) && (primary != null || node != null))
            {
                Vector3 to = primary != null ? primary.GetHitFx().position : center + Vector3.up * 0.3f;
                float flight = Actions.SpawnTravel(s.travelVfx, caster.GetHitFx().position, to, s.travelSpeed);
                if (flight > 0f) yield return new WaitForSeconds(flight);
            }

            if (node != null && AllActions.Any(a => a.@do == "teleport"))
            {
                node = LandingNode(node);
                if (node == null) yield break;
                yield return Teleport(node);
                center = node.transform.position;
            }

            if (primary != null && s.dodgeable && DodgeCheck(caster, primary))
            {
                primary.OnDodge(caster);
                SkillMods.Redraw(this);
                RefreshStats();
                yield break;
            }

            ctx.center = center;
            SkillMods.Announce(tuned, this);
            var selfActions = AllActions.Where(IsSelf).ToList();
            var targetActions = AllActions.Where(a => !IsSelf(a) && a.@do != "teleport").ToList();

            var victims = new List<Character>();
            float radius = Radius;
            if (shaped) victims.AddRange(Shapes.Hit(AI_Manager.instance.GetEnemiesAt(from, range + 1f), from, center, s.shape, range, s.width, s.angle));
            else if (radius > 0f) victims.AddRange(AI_Manager.instance.GetEnemiesAt(center, radius));
            if (primary != null && !victims.Contains(primary)) victims.Insert(0, primary);
            int pierces = SkillMods.Pierces(tuned);
            if (pierces > 0) victims = victims.OrderBy(v => (v.transform.position - from).sqrMagnitude).Take(pierces + 1).ToList();
            // a rewrite (a stance, a passive) may deliver the spell another way: a laid trap, a walking bomb, a loose drifter
            bool delivered = tuned?.form != null && targetActions.Count > 0 && SkillMods.Deliver(tuned, this, ctx, victims, center, from, radius, targetActions);
            // wild magic may change the spell's form as it leaves you
            if (!delivered && caster == Player.instance && (s.target == "enemy" || s.target == "floor") && targetActions.Count > 0 && WildMagic.RollForm(out string wildForm))
                WildMagic.Convert(wildForm, GetDisplayName(), ctx, victims, center, from, range, targetActions);

            if (delivered) { }
            else if (victims.Count == 0 && (s.target == "self" || s.target == "item") && radius <= 0f)
            {
                Actions.Run(targetActions, ctx, caster);
            }
            else if (victims.Count == 0 && s.target == "floor")
            {
                // Nobody at the aim point: ground (coins, knives, fields) still goes down there.
                ctx.groundAtCenter = true;
                Actions.Run(targetActions.Where(a => a.@do == "field" || a.@do == "hazard" || a.@do == "trap" || a.@do == "raisewall").ToList(), ctx, caster);
                ctx.groundAtCenter = false;
            }
            if (!shaped && !string.IsNullOrEmpty(s.areaVfx) && (radius > 0f || s.target == "floor")) Actions.SpawnVfx(s.areaVfx, center + Vector3.up * 0.1f);
            var struck = new List<Character>();
            foreach (Character v in victims)
            {
                if (v == null || v.GetIsDead()) continue;
                Actions.SpawnVfx(Tuned?.impactVfx ?? s.impactVfx, v.GetHitFx().position);
                ctx.target = v;
                struck.Add(v);
                Actions.Run(targetActions, ctx, v);
                yield return new WaitForSeconds(0.05f);
            }
            foreach (IEnumerator move in ctx.paced.ToList()) yield return StartCoroutine(move);
            ctx.paced.Clear();
            Actions.Run(selfActions, ctx, caster);
            Actions.FlushDeferred(ctx);
            if (tuned != null && tuned.mods.Count > 0) { SkillMods.Spend(tuned); SkillMods.Redraw(this); RefreshStats(); }
            // Worn after the cast, so a rune's last cast still counts.
            if (s.runeWear > 0 && caster == Player.instance)
            {
                foreach (string b in Runes.Wear(rt.internalName, s, OpenRuneSlots, tuned?.burn ?? 0)) Game.LOG($"<color=#FF9F43>{b}</color>");
                RefreshStats();
            }
            if (s.resetOnKill && GetCooldownAmount() > 0 && struck.Any(v => v == null || v.GetIsDead()))
            {
                SetCooldownAmount(0);
                if (Menu_HUD.instance != null) Menu_HUD.instance.Refresh_Cooldown();
                Game.LOG($"<color=#FFD15C>{GetDisplayName()} is ready again!</color>");
            }
            if (victims.Count > 0 || s.target != "self") Ability.AlertInRadius(center, 4f);
            yield return new WaitForSeconds(0.2f);
        }

        /// <summary>
        /// Never land on someone: the occupant would lose its tile and the camera would sit inside them. An occupied
        /// aim point lands on the free tile beside it nearest to you.
        /// </summary>
        private NavNode LandingNode(NavNode node)
        {
            NavNode here = caster.GetMover().GetCurrentNavNode();
            if (node == here || (!node.IsOccupied() && !node.BlockCheck())) return node;
            Vector3 from = caster.transform.position;
            return node.GetLinks().Where(l => l != null && l != here && !l.IsOccupied() && !l.BlockCheck() && !l.HasDoor())
                       .OrderBy(l => (l.transform.position - from).sqrMagnitude).FirstOrDefault();
        }

        private IEnumerator Teleport(NavNode node)
        {
            caster.isTeleporting = true;
            caster.GetMover().GetCurrentNavNode().ClearOccupant();
            yield return new WaitForSeconds(0.1f);
            caster.GetMover().transform.position = node.transform.position;
            caster.GetMover().SetCurrentNavNode(node);
            node.OnEnterNode(caster);
            node.SetOccupant(caster);
            if (caster == Player.instance)
            {
                Player.CURRENT_ROOM = node.GetRoom();
                node.GetRoom().Discover();
                Map.instance.UpdateOcclusion();
                CullManager.instance.UpdateCulling();
            }
            yield return null;
            caster.isTeleporting = false;
        }

        /// <summary>Called by ProcHub for every ModAbility the player knows.</summary>
        internal List<ProcSpec> Procs { get { Bind(); return rt?.spec.procs; } }

        internal void HandleProc(string on, Character other)
        {
            Bind();
            if (rt?.spec.procs == null) return;
            if (!Equipped.Check(rt.spec.equipped, caster ?? Player.instance)) return;
            foreach (ProcSpec p in rt.spec.procs)
                if (string.Equals(p.on, on, System.StringComparison.OrdinalIgnoreCase))
                    ProcHub.RunProc(p, MakeContext(), on, other);
        }

        public override string GetTooltipDescription()
        {
            Bind();
            if (rt != null) RefreshStats(); // what a rewrite does to its reach and cost shows before the next click
            return rt == null ? base.GetTooltipDescription() : Rules();
        }

        public override string GetPerkTooltipDescription()
        {
            Bind();
            if (rt?.spec.perk == null) return "";
            PerkSpec p = rt.spec.perk;
            var lines = new List<string>();
            if (!string.IsNullOrEmpty(p.description)) lines.Add(p.description);
            if (p.damageMult != 1f) lines.Add($"• Damage x{p.damageMult:0.##}.");
            if (p.cooldownDelta != 0) lines.Add($"• Cooldown {p.cooldownDelta:+#;-#}.");
            if (p.staminaDelta != 0) lines.Add($"• Stamina cost {p.staminaDelta:+#;-#}.");
            if (p.radiusDelta != 0) lines.Add($"• Area {p.radiusDelta:+0.#;-0.#}.");
            if (p.rangeDelta != 0) lines.Add($"• Range {p.rangeDelta:+0.#;-0.#}.");
            if (p.chanceBonus != 0) lines.Add($"• Proc chances +{p.chanceBonus}%.");
            string add = Actions.Describe(p.addActions, rt.classId, DefaultWho());
            if (add.Length > 0) lines.Add(add);
            return string.Join("\n", lines);
        }

        /// <summary>Does any of its actions (procs and perk included, nested ones too) add a weapon share?</summary>
        private static bool RollsWeapon(AbilitySpec s)
        {
            bool Any(IEnumerable<ActionSpec> list, int depth) => list != null && depth < 8 && list.Any(a => a != null &&
                (a.weapon > 0f || Any(a.actions, depth + 1) || Any(a.then, depth + 1) || Any(a.@else, depth + 1) ||
                 (a.options != null && a.options.Any(o => Any(o.actions, depth + 1)))));
            return Any(s.actions, 0) || Any(s.perk?.addActions, 0) || (s.procs != null && s.procs.Any(p => Any(p.actions, 0)));
        }

        private string DefaultWho()
        {
            AbilitySpec s = Eff;
            float r = Radius;
            if (Tuned?.form == "trap") return "whoever sets it off";
            if (Tuned?.form == "monster") return r > 0 ? $"enemies within {r:0.#} of it" : "enemies beside it";
            if (Tuned?.form == "drifter") return "whoever it meets";
            if (Shapes.Is(s.shape)) return Shapes.Who(s.shape);
            if (s.target == "self") return r > 0 ? $"enemies within {r:0.#}" : "you";
            return r > 0 ? "each enemy in the area" : "the target";
        }

        private string PenaltyLine(SynergyPenaltySpec p)
        {
            var costs = new List<string>();
            if (p.damage != 0) costs.Add($"{p.damage}% less damage");
            if (p.stamina != 0) costs.Add($"{p.stamina:+#;-#} stamina");
            if (p.cooldown != 0) costs.Add($"{p.cooldown:+#;-#} cooldown");
            int levels = SynergyLevels;
            return $"<color=#FF9C7A>Takes the strain of your synergies with this class: {string.Join(", ", costs)} per synergy level (now {levels}).</color>";
        }

        private string Rules()
        {
            Runes.Tuning tuned = Tuned;
            AbilitySpec s = tuned?.spec ?? rt.spec;
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(s.description)) parts.Add(s.description);
            var info = new List<string>();
            if (!isPassive)
            {
                if (s.target == "self" && Radius > 0 && s.target != rt.spec.target) info.Add("Around you, no aiming");
                if (s.target == "enemy") info.Add($"Target an enemy within {range:0.#}");
                else if (s.target == "pet") info.Add($"Target {(string.IsNullOrEmpty(s.petGroup) ? "one of your summons" : "one of your " + s.petGroup + "s")} within {range:0.#}");
                else if (s.target == "floor") info.Add($"Target a spot within {range:0.#}");
                else if (s.target == "item") info.Add($"Use on an inventory item{(string.IsNullOrEmpty(s.itemTag) ? "" : " tagged " + s.itemTag.ToLowerInvariant())}");
                if (s.itemCost != null && s.itemCost.Count > 0) info.Add("costs " + string.Join(", ", Costs.Select(Crafting.Describe)));
                if (s.creditCost > 0) info.Add($"costs <color=#FFD966>{s.creditCost} credits</color>");
                if (s.stackCost?.effect != null && !PoolGone)
                {
                    string fx =ModEffect.Specs.TryGetValue(Actions.EffectId(s.stackCost.effect, rt.classId), out var frt) ? frt.spec.name : s.stackCost.effect.TrimStart('@', '#');
                    info.Add($"costs <color=#FFD966>{StackCostAmount} {fx}</color>");
                }
                if (SkillMods.Pierces(tuned) > 0) info.Add("flies on along a line");
                else if (Shapes.Is(s.shape)) info.Add(Shapes.Describe(s.shape, range, s.width, s.angle));
                else if (Radius > 0) info.Add($"area {Radius:0.#}");
                if (s.dodgeable) info.Add("can be dodged");
                if (s.instant) info.Add("instant");
                if (s.resetOnKill) info.Add("<color=#FFD15C>a kill makes it ready again</color>");
                if (IsCommand) info.Add("a command (usable in any transformation)");
                if (!string.IsNullOrEmpty(s.ifSkill)) info.Add($"<color=#7FE0A0>from your {Synergies.TreeNames(s.ifSkill)} training</color>");
                if (!string.IsNullOrEmpty(s.attachTo)) info.Add($"<color=#7FE0A0>right-click {s.attachTo} on your hotbar to use it</color>");
                var bonus = BonusSkills.For(this);
                if (bonus.Count > 0) info.Add($"<color=#7FE0A0>right-click for {string.Join(", ", bonus.Select(b => b.GetDisplayName()))}</color>");
                if (s.usableWhen != null) info.Add($"<color=#FFD15C>only when {When.Describe(s.usableWhen, rt.classId)}</color>");
                if (s.targetWhen != null) info.Add($"<color=#FFD15C>only on targets where {When.Describe(s.targetWhen, rt.classId)}</color>");
            }
            if (!string.IsNullOrEmpty(s.equipped))
            {
                // red while your current gear doesn't fit it
                bool ok = Equipped.Check(s.equipped, caster ?? Player.instance);
                info.Add($"<color={(ok ? "#FFD15C" : "#FF6B6B")}>{(isPassive ? "only works with" : "needs")} {Equipped.Requirement(s.equipped)}</color>");
                // which one weapon its weapon share comes from right now
                if (ok && RollsWeapon(s) && Equipped.Hand(s.equipped, Player.instance, out bool off) is Item_Weapon w)
                    info.Add(off ? $"swings your off-hand {w.GetName(false)} at {Mathf.RoundToInt(100f * Equipped.Power(Player.instance, w, true))}% power"
                                 : $"swings your {w.GetName(false)}");
            }
            if (info.Count > 0) parts.Add("<color=#9FB7C9>" + string.Join(", ", info) + ".</color>");
            string reshaped = SkillMods.Lines(tuned, rt.classId);
            if (reshaped.Length > 0) parts.Add(reshaped);
            string reshapes = SkillMods.Summary(s.modifies, rt.classId);
            if (reshapes.Length > 0) parts.Add(reshapes);
            if (rt.spec.synergyPenalty != null) parts.Add(PenaltyLine(rt.spec.synergyPenalty));
            if (s.runeSlots > 0)
            {
                Runes.Tuning t = Tuned;
                string set = t == null ? "none set" : string.Join(", ", t.runes.Select(r => ItemRegistry.NameOf(r.id) +
                    (s.runeWear > 0 ? $" ({Runes.CastsLeft(rt.internalName, s, r.id)} casts left)" : "")));
                int open = OpenRuneSlots;
                var spent = Runes.Sockets(rt.internalName, s.runeSlots).Take(open).Where(x => Runes.IsSpent(rt.internalName, s, x)).Select(ItemRegistry.NameOf).ToList();
                if (t == null && spent.Count > 0) set = "none active";
                parts.Add($"<color=#B58CFF>Rune sockets {open}/{s.runeSlots} open: {set}.</color>" +
                    (spent.Count > 0 ? $" <color=#FF7272>Out of charge: {string.Join(", ", spent)}.</color>" : "") +
                    (s.runeCharge > 0 ? $" <color=#FF9F43>Runes hold {s.runeWear} casts (rarer ones fewer); a spent rune does nothing until you recharge it for {s.runeCharge} stamina a cast.</color>"
                        : s.runeWear > 0 ? $" <color=#FF9F43>Runes last {s.runeWear} casts (rarer ones fewer), then a spare from your bag slots in.</color>" : "") +
                    (t != null && t.instability > 0 && s.runeMeter != null ? $" <color=#FF9F43>+{t.instability} {Runes.EffectName(s.runeMeter, rt.classId)} per cast.</color>" : "") +
                    (open < RankRuneSlots ? $" <color=#FF9F43>{RankRuneSlots - open} sealed by augments.</color>" : "") +
                    (RankRuneSlots < s.runeSlots ? $" <color=#7F8FA0>Next socket at rank {Runes.OpensAt(s, RankRuneSlots)}.</color>" : "") +
                    $" <color=#7F8FA0>({RunePanel.KeyName} opens the rune matrix)</color>");
                if (Augments.Has(this)) parts.Add(Augments.TipLine(this));
                if (s.runeVariants != null && s.runeVariants.Count > 0)
                    parts.Add("Signature combos:\n" + string.Join("\n", s.runeVariants.Select(v => "• " + Runes.DescribeVariant(v, rt.classId, t != null && t.variants.Contains(v)))));
            }
            if (!string.IsNullOrEmpty(s.rules))
            {
                parts.Add(s.rules);
                return string.Join("\n\n", parts);
            }
            string acts = Actions.Describe(Runes.Apply(rt.internalName, "rules", rt.spec.actions, s, rt.classId, Tuned), rt.classId, DefaultWho());
            if (acts.Length > 0) parts.Add(acts);
            foreach (ProcSpec p in s.procs ?? new List<ProcSpec>())
                parts.Add(Actions.DescribeProc(p, rt.classId, Perk?.chanceBonus ?? 0));
            return string.Join("\n\n", parts);
        }
    }
}
