using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using I2.Loc;
using UnityEngine;

namespace SCClassLoader
{
    [BepInPlugin(GUID, "Breachwork Loader", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string GUID = "arendeth.starcrawlers.classloader";
        public const string Version = "1.4.0";

        internal static ManualLogSource Log;
        internal static Plugin Instance;
        internal static string GameDir => Paths.GameRootPath;

        /// <summary>How many tiers the in-game skills screen can show per tree.</summary>
        internal static int MaxTiers = 5;

        private static bool dumped, archivesStarted;
#if !PLAYER
        private static bool quitGuard;
#endif

        internal static BepInEx.Configuration.ConfigEntry<bool> ApartmentSaveFollowsAutosave;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            Migrations.Run(GameDir);
            ModsPage.Listen(Log);
            ModChoices.ConfigDir = Paths.ConfigPath;
            ApartmentSaveFollowsAutosave = Config.Bind("Saving", "ApartmentSaveFollowsAutosave", true,
                "Only save when using the Apartment Portal if autosave is enabled (Ironmode always saves). False = vanilla forced save.");
            TierSlots.Bind(Config);
            StashTabs.Bind(Config);
            SideAreas.Bind(Config);
            BreachFinder.Bind(Config);
            HotbarPages.Bind(Config);
            Graveyard.Bind(Config);
            Unlocks.Bind(Config);
            SecurityOdds.Bind(Config);
            SaveGating.Bind(Config);
            PetHandling.Bind(Config);
            PetWake.Bind(Config);
            PartyPanel.Bind(Config);
            TileStacks.Bind(Config);
            ResourceBars.Bind(Config);
            RunePanel.Bind(Config);
            CraftPanel.Bind(Config);
            GameFixes.Bind(Config);
            ClassLoot.Bind(Config);
            BugReport.Bind(Config);
            CompanionTraps.Bind(Config);
            Traps.Bind(Config);
            Elites.Bind(Config);
            ChemistryFixes.Bind(Config);
            HealthRegen.Bind(Config);
            StackCaps.Bind(Config);
            StuckTurns.Bind(Config);
            EndGame.Bind(Config);
            SafeReturn.Bind(Config);
            PerkFixes.Bind(Config);
            ModderBalance.Bind(Config);
            Synergies.Bind(Config);
            SkillSaveFixes.Bind(Config);
            SkillRefund.Bind(Config);
            CerberusFight.Bind(Config);
            WinCredits.Bind(Config);
            Endless.Bind(Config);
            NameLength.Bind(Config);
            Factions.Bind(Config);
            Diagonal.Bind(Config);
            DashCam.Bind(Config);
            CombatClarity.Bind(Config);
            Crew.Bind(Config);
            Commands.Bind(Config);
            MinionMenu.Bind(Config);
            UiLook.Bind(Config);
            HoverTip.Bind(Config);
            TextFit.Bind(Config);
            PortalButton.Bind(Config);
            PortalButton.BindSpecials(Config);
            AudioBuffer.Bind(Config);
            ClassRegistry.LoadDefinitions();
            new Harmony(GUID).PatchAll(typeof(Plugin).Assembly);
            PluginHost.LoadAll();
            Log.LogInfo($"Breachwork Loader {Version} ready");
        }

        private void Update() { ModsPage.Tick(); Forms.Tick(); CrewScreen.Tick(); Commands.Tick(); MinionMenu.Tick(); BonusSkills.Tick(); HoverTip.Tick(); StuckTurns.Tick(); TextFit.Tick(); TooltipFit.Tick(); PluginHost.Tick(); ModAudio.Tick(); }

        // after the game's Update and coroutines, which rewrite what these cap
        private void LateUpdate() => CerberusFight.Tick();

        internal static void OnGameStart(Game game)
        {
            // The limit is the number of tier slots the skills screens have, not the longest vanilla tree.
            int vanillaMax = game.skills.Where(s => s != null && s.isLive).Select(s => s.skillTiers.Count).DefaultIfEmpty(5).Max();
            int slots = TierSlots.Count();
            // The in-game skills screen may not exist yet; its Awake gets the same extra slots.
            MaxTiers = slots > 0 ? Math.Min(slots, TierSlots.Target.Value) : TierSlots.Target.Value;
            Log.LogInfo($"Tier slots per tree: {MaxTiers} (longest vanilla tree {vanillaMax})");

            if (!dumped)
            {
                dumped = true;
                try { Dumper.Dump(game); }
                catch (Exception e) { Log.LogError("Reference dump failed: " + e); }
            }
            ClassRegistry.Register(game);
            try { TileStacks.RegisterEffect(); }
            catch (Exception e) { Log.LogError("Crowded effect failed: " + e); }
            try { Afflictions.RegisterEffects(); }
            catch (Exception e) { Log.LogError("Taunted/Silenced effects failed: " + e); }
            try { Echoes.RegisterEffects(); }
            catch (Exception e) { Log.LogError("Echo effects failed: " + e); }
            Tweaks.Load();
            Tweaks.Apply();
            try { ItemRegistry.Register(); }
            catch (Exception e) { Log.LogError("Mod items failed: " + e); }
            try { CustomVfx.Load(); }
            catch (Exception e) { Log.LogError("Mod particles failed: " + e); }
            try { Wear.Load(); }
            catch (Exception e) { Log.LogError("Wear models failed: " + e); }
            try { Factions.Load(); }
            catch (Exception e) { Log.LogError("Factions failed: " + e); }
            try { SideAreaData.Load(); }
            catch (Exception e) { Log.LogError("Side area data failed: " + e); }
            try { Synergies.Load(); }
            catch (Exception e) { Log.LogError("Synergies failed: " + e); }
            try { ClassRegistry.ResolveStarterItems(); }
            catch (Exception e) { Log.LogError("Class starter items failed: " + e); }
            try { Crafting.Load(); }
            catch (Exception e) { Log.LogError("Recipes failed: " + e); }
            try { EnemyAbilities.LoadEffects(); }
            catch (Exception e) { Log.LogError("Enemy skill effects failed: " + e); }
            try { EnemyRegistry.Register(); }
            catch (Exception e) { Log.LogError("Mod enemies failed: " + e); }
            try { EnemyAbilities.LoadPatches(); }
            catch (Exception e) { Log.LogError("Enemy patches failed: " + e); }
            PluginHost.GameReady();
#if !PLAYER
            if (Environment.GetEnvironmentVariable("SCCL_DEVTEST") == "audiofast") ModAudio.ArchiveFirst = true;
#endif
            if (!archivesStarted) { archivesStarted = true; ModArchives.StartPreload(ModAudio.ArchiveFirst); }

#if !PLAYER
            string dev = Environment.GetEnvironmentVariable("SCCL_DEVTEST");
            if (!string.IsNullOrEmpty(dev) && !quitGuard)
            {
                // Self-tests only: Unity sometimes hangs while shutting down; don't let that stall a test batch.
                quitGuard = true;
                Application.quitting += () => new System.Threading.Thread(() =>
                {
                    System.Threading.Thread.Sleep(8000);
                    System.Diagnostics.Process.GetCurrentProcess().Kill();
                }) { IsBackground = true }.Start();
            }
            if (dev == "3") Instance.StartCoroutine(DevTestCombat.Run());
            else if (dev == "stash") Instance.StartCoroutine(DevTestStash.Run());
            else if (dev == "side") Instance.StartCoroutine(DevTestSide.Run());
            else if (dev == "items") Instance.StartCoroutine(DevTestItems.Run());
            else if (dev == "craft") Instance.StartCoroutine(DevTestCraft.Run());
            else if (dev == "warcraft") Instance.StartCoroutine(DevTestWarcraft.Run());
            else if (dev == "enemies") Instance.StartCoroutine(DevTestEnemies.Run());
            else if (dev == "hotbar") Instance.StartCoroutine(DevTestHotbar.Run());
            else if (dev == "qol") Instance.StartCoroutine(DevTestQol.Run());
            else if (dev == "gambler") Instance.StartCoroutine(DevTestGambler.Run());
            else if (dev == "elites") Instance.StartCoroutine(DevTestElites.Run());
            else if (dev == "chemistry") Instance.StartCoroutine(DevTestChemistry.Run());
            else if (dev == "regen") Instance.StartCoroutine(DevTestRegen.Run());
            else if (dev == "stackcaps") Instance.StartCoroutine(DevTestStackCaps.Run());
            else if (dev == "stuck") Instance.StartCoroutine(DevTestStuck.Run());
            else if (dev == "endgame") Instance.StartCoroutine(DevTestEndGame.Run());
            else if (dev != null && dev.StartsWith("ichorvs:")) Instance.StartCoroutine(DevTestIchorCompare.Run(dev.Substring("ichorvs:".Length)));
            else if (dev != null && dev.StartsWith("deep:")) { DevTestEndGame.Deep = true; Instance.StartCoroutine(DevTestEndGame.Run(true, dev.Substring("deep:".Length))); }
            else if (dev == "refund") Instance.StartCoroutine(DevTestRefund.Run());
            else if (dev == "cerberus") Instance.StartCoroutine(DevTestEndGame.Run(true));
            else if (dev != null && dev.StartsWith("cerberus:")) Instance.StartCoroutine(DevTestEndGame.Run(true, dev.Substring("cerberus:".Length)));
            else if (dev == "saves") Instance.StartCoroutine(DevTestSaves.Run());
            else if (dev == "hotbarlayout") Instance.StartCoroutine(DevTestHotbarLayout.Run(null));
            else if (dev != null && dev.StartsWith("hotbarlayout:")) Instance.StartCoroutine(DevTestHotbarLayout.Run(dev.Substring("hotbarlayout:".Length)));
            else if (dev == "safereturn") Instance.StartCoroutine(DevTestSafeReturn.Run());
            else if (dev == "perks") Instance.StartCoroutine(DevTestPerks.Run());
            else if (dev == "perks2") Instance.StartCoroutine(DevTestPerks2.Run());
            else if (dev == "menufps") Instance.StartCoroutine(DevTestMenuFps.Run());
            else if (dev == "names") Instance.StartCoroutine(DevTestNames.Run());
            else if (dev == "names2") Instance.StartCoroutine(DevTestNames.RunLoadList());
            else if (dev == "drones") Instance.StartCoroutine(DevTestDrones.Run());
            else if (dev == "pets") Instance.StartCoroutine(DevTestPets.Run());
            else if (dev == "conjoined") Instance.StartCoroutine(DevTestConjoined.Run());
            else if (dev == "tiles") Instance.StartCoroutine(DevTestTiles.Run());
            else if (dev == "seer") Instance.StartCoroutine(DevTestSeer.Run());
            else if (dev == "astralvoid") Instance.StartCoroutine(DevTestAstralVoid.Run());
            else if (dev == "wisps") Instance.StartCoroutine(DevTestWisps.Run());
            else if (dev == "dashcam") Instance.StartCoroutine(DevTestDashCam.Run());
            else if (dev == "kelvin" || dev == "glass" || dev == "glassfight") Instance.StartCoroutine(DevTestFrostWonder.Run(dev));
            else if (dev == "smoke") Instance.StartCoroutine(DevTestSmoke.Run());
            else if (dev == "ground") Instance.StartCoroutine(DevTestGround.Run());
            else if (dev == "looks") Instance.StartCoroutine(DevTestLooks.Run());
            else if (dev == "vfx") Instance.StartCoroutine(DevTestLooks.RunVfx());
            else if (dev == "tips") Instance.StartCoroutine(DevTestTips.Run());
            else if (dev == "resources") Instance.StartCoroutine(DevTestResources.Run());
            else if (dev == "capscale") Instance.StartCoroutine(DevTestCapScale.Run());
            else if (dev == "poolgone") Instance.StartCoroutine(DevTestPoolGone.Run());
            else if (dev == "runes") Instance.StartCoroutine(DevTestRunes.Run());
            else if (dev == "augments") Instance.StartCoroutine(DevTestAugments.Run());
            else if (dev == "kinetics") Instance.StartCoroutine(DevTestKinetics.Run());
            else if (dev == "stereo") Instance.StartCoroutine(DevTestStereo.Run());
            else if (dev == "control") Instance.StartCoroutine(DevTestControl.Run());
            else if (dev == "forms") Instance.StartCoroutine(DevTestForms.Run());
            else if (dev == "tidewright") Instance.StartCoroutine(DevTestTidewright.Run());
            else if (dev == "runecharge") Instance.StartCoroutine(DevTestRuneCharge.Run());
            else if (dev == "crossscribed") Instance.StartCoroutine(DevTestCrossScribed.Run());
            else if (dev == "reactions") Instance.StartCoroutine(DevTestReactions.Run());
            else if (dev == "sortfix") Instance.StartCoroutine(DevTestSortFix.Run());
            else if (dev == "factions") Instance.StartCoroutine(DevTestFactions.Run());
            else if (dev == "echoes") Instance.StartCoroutine(DevTestEchoes.Run());
            else if (dev == "sidekinds") Instance.StartCoroutine(DevTestSideKinds.Run());
            else if (dev == "towers") Instance.StartCoroutine(DevTestTowers.Run());
            else if (dev == "towersvoid") Instance.StartCoroutine(DevTestTowers.RunVoid());
            else if (dev == "interlopers") Instance.StartCoroutine(DevTestInterlopers.Run());
            else if (dev == "diagonal") Instance.StartCoroutine(DevTestDiagonal.Run());
            else if (dev == "synergies") Instance.StartCoroutine(DevTestSynergies.Run());
            else if (dev == "synergycost") Instance.StartCoroutine(DevTestSynergyCost.Run());
            else if (dev == "synergycost2") Instance.StartCoroutine(DevTestSynergyCost.RunBundled());
            else if (dev == "classsyn") Instance.StartCoroutine(DevTestClassSynergies.Run());
            else if (dev == "allsyn") Instance.StartCoroutine(DevTestClassSynergies.RunAll());
            else if (dev == "conditions") Instance.StartCoroutine(DevTestConditions.Run());
            else if (dev == "triggers") Instance.StartCoroutine(DevTestTriggers.Run());
            else if (dev == "traps") Instance.StartCoroutine(DevTestTraps.Run());
            else if (dev == "skillmods") Instance.StartCoroutine(DevTestSkillMods.Run());
            else if (dev == "trapshove") Instance.StartCoroutine(DevTestTraps.RunShove());
            else if (dev == "dice") Instance.StartCoroutine(DevTestTraps.RunDice());
            else if (dev == "equipped") Instance.StartCoroutine(DevTestEquipped.Run());
            else if (dev == "fulgurant") Instance.StartCoroutine(DevTestFulgurant.Run());
            else if (dev == "snareseer") Instance.StartCoroutine(DevTestSnareseer.Run());
            else if (dev == "revenge") Instance.StartCoroutine(DevTestRevenge.Run());
            else if (dev == "mimeticist") Instance.StartCoroutine(DevTestMimeticist.Run());
            else if (dev == "bulkhead") Instance.StartCoroutine(DevTestBulkhead.Run());
            else if (dev == "ichorborn") Instance.StartCoroutine(DevTestIchorborn.Run());
            else if (dev == "nosaves") Instance.StartCoroutine(DevTestNoSaves.Run());
            else if (dev == "ichorgoo") Instance.StartCoroutine(DevTestLooks.RunIchorGoo());
            else if (dev == "reactant") Instance.StartCoroutine(DevTestReactant.Run());
            else if (dev == "miasmancer") Instance.StartCoroutine(DevTestMiasmancer.Run());
            else if (dev == "miasmasyn") Instance.StartCoroutine(DevTestMiasmaSynergies.Run());
            else if (dev == "wildcaster") Instance.StartCoroutine(DevTestWildcaster.Run());
            else if (dev == "spellfever") Instance.StartCoroutine(DevTestSpellFever.Run());
            else if (dev == "models") Instance.StartCoroutine(DevTestModels.Run());
            else if (dev == "itemmodels") Instance.StartCoroutine(DevTestItemModels.Run());
            else if (dev == "loadscreen") Instance.StartCoroutine(DevTestLoadScreen.Run());
            else if (dev == "bodypicker") Instance.StartCoroutine(DevTestBodyPicker.Run());
            else if (dev == "crew") Instance.StartCoroutine(DevTestCrew.Run());
            else if (dev == "commands") Instance.StartCoroutine(DevTestCommands.Run());
            else if (dev == "petwake") Instance.StartCoroutine(DevTestPetWake.Run());
            else if (dev == "minionmenu") Instance.StartCoroutine(DevTestMinionMenu.Run(null));
            else if (dev == "todos") Instance.StartCoroutine(DevTestTodos.Run(null));
            else if (dev == "todos2") Instance.StartCoroutine(DevTestTodos.RunFresh());
            else if (dev == "modder") Instance.StartCoroutine(DevTestModder.Run(null));
            else if (dev != null && dev.StartsWith("modder:")) Instance.StartCoroutine(DevTestModder.Run(dev.Substring("modder:".Length)));
            else if (dev != null && dev.StartsWith("todos:")) Instance.StartCoroutine(DevTestTodos.Run(dev.Substring("todos:".Length)));
            else if (dev != null && dev.StartsWith("minionmenu:")) Instance.StartCoroutine(DevTestMinionMenu.Run(dev.Substring("minionmenu:".Length)));
            else if (dev == "bodies") Instance.StartCoroutine(DevTestBodies.Run());
            else if (dev == "wear") Instance.StartCoroutine(DevTestWear.Run());
            else if (dev == "combatflow") Instance.StartCoroutine(DevTestCombatFlow.Run());
            else if (dev == "settings") Instance.StartCoroutine(DevTestSettings.Run());
            else if (dev == "audiofast") Instance.StartCoroutine(DevTestAudioFallback.RunFast());
            else if (dev == "audiofallback") Instance.StartCoroutine(DevTestAudioFallback.Run());
            else if (dev == "aiportrait") Instance.StartCoroutine(DevTestAiPortrait.Run());
            else if (dev == "aiportrait2") Instance.StartCoroutine(DevTestAiPortrait.RunAllowed());
            else if (dev == "realtime") Instance.StartCoroutine(DevTestRealtime.Run());
            else if (dev == "realtime2") Instance.StartCoroutine(DevTestRealtime.RunOn());
            else if (dev == "plugins") Instance.StartCoroutine(DevTestPlugins.Run());
            else if (dev == "plugins2") Instance.StartCoroutine(DevTestPlugins.RunAllowed());
            else if (dev == "scripts") Instance.StartCoroutine(DevTestScripts.Run());
            else if (dev == "plugins3") Instance.StartCoroutine(DevTestPlugins.RunReloaded());
            else if (dev == "mods") Instance.StartCoroutine(DevTestMods.Run());
            else if (dev == "mods2") Instance.StartCoroutine(DevTestMods.RunAfterRestart());
            else if (dev == "modorder") Instance.StartCoroutine(DevTestMods.RunOrder());
            else if (dev == "content") Instance.StartCoroutine(DevTestContent.Run());
            else if (dev == "content2") Instance.StartCoroutine(DevTestContent.RunAfterRestart());
            else if (dev == "ui") Instance.StartCoroutine(DevTestUi.Run());
            else if (dev == "textfit") Instance.StartCoroutine(DevTestTextFit.Run());
            else if (dev == "bugreport") Instance.StartCoroutine(DevTestBugReport.Run());
            else if (dev == "keys") Instance.StartCoroutine(DevTestKeys.Run());
            else if (dev == "portal") Instance.StartCoroutine(DevTestPortal.Run());
            else if (dev == "places" || (dev != null && dev.StartsWith("places:"))) Instance.StartCoroutine(DevTestPlaces.Run(dev));
            else if (dev == "dump") Application.Quit(); // just refresh vanilla_reference.txt
            else if (!string.IsNullOrEmpty(dev)) Instance.StartCoroutine(DevTest.Run(dev));
#endif
        }
    }

    [HarmonyPatch(typeof(Game), "Start")]
    internal static class Patch_GameStart
    {
        private static void Postfix(Game __instance) => Plugin.OnGameStart(__instance);
    }

    /// <summary>Serves text for SCCL/... terms so custom names need no I2 language source.</summary>
    [HarmonyPatch(typeof(LocalizationManager), nameof(LocalizationManager.TryGetTranslation))]
    internal static class Patch_Translation
    {
        private static bool Prefix(string Term, ref string Translation, ref bool __result)
        {
            if (Term == null || !ClassRegistry.Strings.TryGetValue(Term, out string text)) return true;
            Translation = text;
            __result = true;
            return false;
        }
    }

    /// <summary>
    /// Abilities scale off their home tree by name (GetRank("SKILLTREE_BigGuns")). When the home tree is
    /// not taken but a custom tree borrowing from it is, report that tree's rank instead.
    /// </summary>
    [HarmonyPatch(typeof(Player_SkillControl), nameof(Player_SkillControl.GetRank))]
    internal static class Patch_GetRank
    {
        private static void Postfix(Player_SkillControl __instance, string internalName, ref int __result)
        {
            if (__result != -1 || __instance.skillTrees == null) return;
            foreach (Skill_Tree t in __instance.skillTrees)
            {
                if (t != null && ClassRegistry.Borrowed.TryGetValue(t.internalName, out var src) && src.Contains(internalName))
                    __result = Math.Max(__result, t.GetRank());
            }
        }
    }

    /// <summary>Vanilla parses "ACH_" + tree name into an enum and throws for unknown trees.</summary>
    [HarmonyPatch]
    internal static class Patch_SkillEvent
    {
        private static System.Reflection.MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("SteamStatsAndAchievements"), "SkillEvent");

        private static bool Prefix(string internalName) => !ClassRegistry.IsModTree(internalName);
    }

    /// <summary>Every tree gets a lore entry by name; there is no lore for custom trees.</summary>
    [HarmonyPatch(typeof(Lore_Control), "Start")]
    internal static class Patch_LoreStart
    {
        private static void Postfix(Lore_Control __instance) => __instance.allLore.RemoveAll(t => t != null && ClassRegistry.IsModTree(t.tagName));
    }

    /// <summary>Stops picking a custom tree together with a tree it borrows abilities from.</summary>
    [HarmonyPatch(typeof(Menu_Creation), nameof(Menu_Creation.Skill_Select))]
    internal static class Patch_SkillSelect
    {
        private static bool Prefix(Menu_Creation __instance, SkillSelect s)
        {
            if (s == null) return false; // a preset naming a class that isn't installed (vanilla would throw mid-load)
            if (s.isPreview || s.isSelected || s.skillTree == null) return true;
            foreach (SkillSelect sel in __instance.selectedSkills)
            {
                if (sel == null || sel.skillTree == null) continue;
                string why = ClassRegistry.Conflict(s.skillTree, sel.skillTree);
                if (why == null) continue;
                AudioManager.PlayEvent("event:/UI/UI_Error");
                string a = s.skillTree.displayName, b = sel.skillTree.displayName;
                if (__instance.skillDescription != null)
                    __instance.skillDescription.text = $"<color=#FF7272>{a} can't be combined with {b}: one {why}.</color>";
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Player_SkillControl), nameof(Player_SkillControl.LoadSkillTrees))]
    internal static class Patch_LoadSkillTrees
    {
        private static void Postfix(Player_SkillControl __instance, string[] loadTrees)
        {
            for (int i = 0; i < loadTrees.Length && i < __instance.skillTrees.Length; i++)
            {
                if (!string.IsNullOrEmpty(loadTrees[i]) && __instance.skillTrees[i] == null)
                    Plugin.Log.LogWarning($"Save uses skill tree '{loadTrees[i]}' which is not installed; that slot will be empty.");
            }
        }
    }
}
