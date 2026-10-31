using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>Writes Mods/vanilla_reference.txt: every tree, tier, ability and item id a class file can use.</summary>
    internal static class Dumper
    {
        public static void Dump(Game game)
        {
            var sb = new StringBuilder();
            sb.AppendLine("StarCrawlers Chimera - vanilla reference for class mods (auto-generated, overwritten each launch)");
            sb.AppendLine($"Skills screen tier slots per tree: {Plugin.MaxTiers}");
            sb.AppendLine("Archetypes: Muscle, Guile, Intellect");
            sb.AppendLine("Stats: " + string.Join(", ", Enum.GetNames(typeof(STAT_TYPE)).Where(n => n != "Null")));
            sb.AppendLine("Damage/resistance types: " + string.Join(", ", Enum.GetNames(typeof(DAMAGE_TYPE)).Where(n => n != "Null")));
            sb.AppendLine();

            var debug = new StringBuilder();
            for (int i = 0; i < game.skills.Count; i++)
            {
                Skill_Tree t = game.skills[i];
                if (t == null) continue;
                sb.AppendLine($"=== TREE \"{t.internalName}\"  {S(t.displayName)}  [{t.skillClass}]{(t.isLive ? "" : "  (not live)")}");
                sb.AppendLine($"    starter credits {t.starterGear_credits}; starter items: {string.Join(", ", (t.starterGear_items ?? new Item[0]).Where(x => x != null).Select(x => x.itemId))}");
                sb.AppendLine($"    tags: {string.Join(", ", (t.playerTags ?? new Tag[0]).Where(x => x != null).Select(x => x.tagName + "=" + x.tagAmount))}");
                sb.AppendLine($"    priorities: {string.Join(", ", (t.attributePriorities ?? new StatObj[0]).Where(x => x != null).Select(x => x.statType + "=" + x.amount))}");
                debug.AppendLine($"[{i}] {t.internalName} go='{t.gameObject.name}' scene='{t.gameObject.scene.name}' tiers={t.skillTiers.Count}");

                for (int j = 0; j < t.skillTiers.Count; j++)
                {
                    Skill_Tier st = t.skillTiers[j];
                    if (st == null) { sb.AppendLine($"    tier {j}: <null>"); continue; }
                    Ability a = st.ability != null ? st.ability : st.passive;
                    string kind = st.ability != null ? "ability" : st.passive != null ? "passive" : "none";
                    sb.AppendLine($"    tier {j}: {S(st.displayName)}  {kind}={(a != null ? a.internalName : "-")}  effect={(string.IsNullOrEmpty(st.effect) ? "-" : st.effect)}");
                    if (a != null)
                        sb.AppendLine($"        {S(a.displayName)}  stamina {a.staminaCost}, cooldown {a.cooldown}, hit {a.hitChance}, range {a.range}, dmg [{string.Join(", ", a.damageList.Where(d => d != null).Select(d => $"{d.dmgType} {d.damage}-{d.damageMax}"))}]");
                    string attrs = string.Join(", ", (st.attributeChanges ?? new StatObj[0]).Where(x => x != null && x.amount != 0).Select(x => $"{x.statType} {x.amount:+#;-#}"));
                    string res = string.Join(", ", (st.resistanceChanges ?? new DmgObj[0]).Where(x => x != null && x.damage != 0).Select(x => $"{x.dmgType} {x.damage:+#;-#}"));
                    if (attrs.Length > 0 || res.Length > 0) sb.AppendLine($"        bonuses: {attrs}{(res.Length > 0 ? " | resist " + res : "")}");

                    debug.AppendLine($"    T{j} {st.GetType().Name} go='{Path(st.transform, t.transform)}' sameGoAsTree={st.gameObject == t.gameObject}");
                    if (st.ability != null) debug.AppendLine($"        ability {st.ability.GetType().Name} '{st.ability.internalName}' path='{Path(st.ability.transform, t.transform)}' childOfTier={st.ability.transform.IsChildOf(st.transform)} childOfTree={st.ability.transform.IsChildOf(t.transform)} scene='{st.ability.gameObject.scene.name}'");
                    if (st.passive != null) debug.AppendLine($"        passive {st.passive.GetType().Name} '{st.passive.internalName}' path='{Path(st.passive.transform, t.transform)}' childOfTier={st.passive.transform.IsChildOf(st.transform)} childOfTree={st.passive.transform.IsChildOf(t.transform)}");
                }
                sb.AppendLine();
            }

            ItemDatabase db = ItemDatabase.instance;
            if (db != null)
            {
                void List(string title, ItemDatabase.DatabaseItem[] arr)
                {
                    if (arr == null || arr.Length == 0) return;
                    sb.AppendLine($"=== ITEMS: {title}  (usable as \"base\" for mod items)");
                    foreach (var d in arr)
                    {
                        if (string.IsNullOrEmpty(d.id)) continue;
                        sb.AppendLine($"    {d.id}  {ItemInfo(d.item)}");
                    }
                    sb.AppendLine();
                }
                List("weapons", db.items_weapon);
                List("ammo", db.items_ammo);
                List("gear", db.items_gear);
                List("gear: head", db.items_gear_head);
                List("gear: neck", db.items_gear_neck);
                List("gear: torso", db.items_gear_torso);
                List("gear: hands", db.items_gear_hands);
                List("gear: legs", db.items_gear_leg);
                List("gear: feet", db.items_gear_feet);
                List("gear: accessory", db.items_gear_accessory);
                List("consumables", db.items_consumable);
                List("chems", db.items_chems);
            }

            AI_Manager aim = AI_Manager.instance;
            if (aim?.allCharacters != null)
            {
                sb.AppendLine("=== ENEMIES (usable as \"base\" for mod enemies; abilities usable as \"ENEMY/ABILITY\")");
                foreach (Character c in aim.allCharacters.Where(c => c != null).OrderBy(c => c.internalName))
                {
                    string team = c.ai != null ? c.ai.aiTeam + "/" + c.ai.aiStyle : "-";
                    string tags = string.Join(",", (c.tags ?? new System.Collections.Generic.List<Tag>()).Where(t => t != null).Select(t => t.tagName).Where(t => t == "BOSS" || t == "RARE" || t == "RARE_ELITE" || t == "MINION"));
                    sb.AppendLine($"    {c.internalName}  {S(c.displayName)}  {c.characterType}, {team}{(tags.Length > 0 ? ", " + tags : "")}{(c.GetComponent<Mob_Variant>() != null ? ", has variants" : "")}");
                    foreach (Ability a in c.GetComponents<Ability>())
                        sb.AppendLine($"        {a.internalName}  ({a.GetType().Name}) cd {a.cooldown}, range {a.range}, prio {a.priority}, dmg [{string.Join(", ", (a.damageList ?? new System.Collections.Generic.List<DmgObj>()).Where(d => d != null).Select(d => $"{d.dmgType} {d.damage}-{d.damageMax}"))}]");
                }
                sb.AppendLine();
            }

            EffectManager em = EffectManager.instance;
            if (em != null)
            {
                sb.AppendLine("=== EFFECTS (use in \"effect\" actions, or as \"vfx\"/\"castVfx\"/\"impactVfx\" when marked [vfx])");
                foreach (var e in (em.allEffects ?? new EffectManager.EffectPrefab[0]))
                {
                    if (e.effect == null) continue;
                    string n;
                    try { n = e.effect.GetName(); } catch { n = ""; }
                    sb.AppendLine($"    {e.effect.internalName}  {(string.IsNullOrEmpty(n) ? "" : "\"" + n + "\"")}{(e.effect.isDebuff ? " debuff" : "")}{(e.effect.spawnParticle != null ? " [vfx]" : "")}");
                }
                sb.AppendLine();
            }
            sb.AppendLine("=== SUMMONABLE PREFABS (\"creature\" for summon actions; any creature name also works)");
            foreach (string v in Summons.ListPrefabs()) sb.AppendLine("    " + v);
            sb.AppendLine("    nanite_cloud, nanite_plague");
            sb.AppendLine();
            sb.AppendLine("=== ABILITY PARTICLES (\"castVfx\"/\"impactVfx\"/proc \"vfx\")");
            foreach (string v in Actions.ListVfx()) sb.AppendLine("    " + v);
            sb.AppendLine();

            Directory.CreateDirectory(ClassRegistry.ModsDir);
            File.WriteAllText(System.IO.Path.Combine(ClassRegistry.ModsDir, "vanilla_reference.txt"), sb.ToString());

            var mc = Menu_Creation.instance;
            if (mc != null && mc.skillGrid != null)
            {
                var rt = (RectTransform)mc.skillGrid.transform;
                debug.AppendLine($"creation grid: cell={mc.skillGrid.cellSize} spacing={mc.skillGrid.spacing} constraint={mc.skillGrid.constraint}/{mc.skillGrid.constraintCount} rect={rt.rect.size} children={rt.childCount} parent='{rt.parent?.name}' scroll={(mc.skillGrid.GetComponentInParent<UnityEngine.UI.ScrollRect>() != null)}");
                var ps = mc.previewMenu;
                if (ps != null) debug.AppendLine($"preview tiers per tree: {ps.tree_0_tiers?.Length}");
            }
            File.WriteAllText(System.IO.Path.Combine(Paths(), "structure_debug.txt"), debug.ToString());
            Plugin.Log.LogInfo("Wrote Mods/vanilla_reference.txt");
        }

        private static string ItemInfo(Item it)
        {
            if (it == null) return "";
            string name = S(it.itemName);
            switch (it)
            {
                case Item_Ammo a:
                    return $"{name}  ammo, dmg [{string.Join(", ", a.damageMods.Where(d => d != null).Select(d => $"{d.dmgType} {d.damage}-{d.damageMax}"))}]{(string.IsNullOrEmpty(a.onHitEffect) ? "" : ", on hit " + a.onHitEffect)}";
                case Item_Weapon w:
                    return $"{name}  {w.weaponType}, dmg [{string.Join(", ", w.damageList.Where(d => d != null).Select(d => $"{d.dmgType} {d.damage}-{d.damageMax}"))}], ammo use {w.ammoUse}";
                case Item_Equipment e:
                    return $"{name}  {e.equipType}{(e.equipTrigger != null && !string.IsNullOrEmpty(e.equipTrigger.equipment_effect) ? ", equip effect " + e.equipTrigger.equipment_effect : "")}";
                case Item_Consumable c:
                    string use = c.useTrigger == null ? "no use trigger" : c.useTrigger.use_ability != null ? "ability " + c.useTrigger.use_ability.internalName : "effect " + (c.useTrigger.use_effect ?? "-");
                    return $"{name}  {c.GetType().Name.Replace("Item_Consumable_", "").Replace("Item_Consumable", "consumable")}, {use}";
                default:
                    return name;
            }
        }

        private static string Paths() => System.IO.Path.GetDirectoryName(typeof(Dumper).Assembly.Location);

        private static string S(I2.Loc.LocalizedString s)
        {
            try { string r = s; return string.IsNullOrEmpty(r) ? "(" + s.mTerm + ")" : "\"" + r.Replace("\n", " ") + "\""; }
            catch { return "(" + s.mTerm + ")"; }
        }

        private static string Path(Transform t, Transform root)
        {
            string p = t.name;
            while (t.parent != null && t != root) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }
    }
}
