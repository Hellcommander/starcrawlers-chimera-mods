// An example to start your own plugin from: use, copy and change it for any purpose, with no conditions and no credit needed.

using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using Breachwork;
using HarmonyLib;
using SCClassLoader;

namespace ExamplePlugin
{
    /// <summary>
    /// The Bounty Hand's rules, one of everything a plugin can do. bounty_hand.json uses these by name, so the class itself
    /// stays plain data:
    ///   "do": "ExamplePlugin:siphon"              a new action, with a field of its own ("reach")
    ///   "when": "ExamplePlugin:Outnumbered"       a new condition
    ///   "value": "ExamplePlugin:ledger"           a new number for value checks, saved with the run
    ///   "on": "ExamplePlugin:collected"           a new event for procs
    ///   "pluginData": { "ExamplePlugin:bounties"  a new kind of content any mod's json can add to (bounties.json)
    /// </summary>
    public class BountyPlugin : BreachworkPlugin
    {
        private const string Ledger = "ledger";

        /// <summary>Creature kind -> credits, from every "pluginData" entry.</summary>
        private readonly Dictionary<string, int> bounties = new Dictionary<string, int>();
        private ConfigEntry<float> payout;

        internal static PluginContext Ctx;

        private class Bounty { public string kind { get; set; } public int credits { get; set; } }

        public override void OnLoad()
        {
            Ctx = Context;
            payout = Context.Bind("Payout", 1f, "Multiplies the credits every bounty pays.");

            Context.AddAction("siphon", Siphon, (spec, who) => $"Takes <color=#FF7272>{spec.amount}</color> health from {who} and everyone near, and gives it to you", hostile: true);
            Context.AddCondition("Outnumbered", (call, who) => Api.EnemiesNear(call.Caster.transform.position, 5f).Count >= 3, "three or more enemies are near you");
            Context.AddValue(Ledger, (call, who) => Context.GetSaved(Ledger, 0), "the kills in your ledger");
            Context.AddEvent("collected", "when you collect a debt");
            Context.On("kill", OnKill);
            Context.OnData("bounties", d =>
            {
                foreach (Bounty b in d.As<List<Bounty>>()) bounties[b.kind] = b.credits;
            });
            // Anything the API doesn't cover is one Harmony patch away (see Patch_TurnCount below).
            Context.Harmony.PatchAll(GetType().Assembly);
        }

        public override void OnGameReady() => Context.Log.Info($"{bounties.Count} bounties on the board, API level {Api.Level}");

        /// <summary>Hurts the target and everyone near it, heals the caster for the total, and tells any procs about it.</summary>
        private void Siphon(ActionCall call)
        {
            int amount = call.Amount();
            float reach = call.Arg("reach", 2f);
            var marks = Api.EnemiesNear(call.Target.transform.position, reach);
            if (!marks.Contains(call.Target)) marks.Add(call.Target);
            foreach (Character c in marks)
                call.Run(new ActionSpec { @do = "damage", type = "Energy", min = amount, max = amount }, c);
            call.Run(new ActionSpec { @do = "heal", amount = amount * marks.Count }, call.Caster);
            Context.FireEvent("collected", call.Target);
        }

        private void OnKill(GameEvent e)
        {
            Context.SetSaved(Ledger, Context.GetSaved(Ledger, 0) + 1);
            if (e.Other == null || !bounties.TryGetValue(e.Other.characterType.ToString(), out int credits)) return;
            int pay = UnityEngine.Mathf.RoundToInt(credits * payout.Value);
            Api.RunActions(new[] { new ActionSpec { @do = "credits", amount = pay } }, e.Bearer, e.Bearer, "Bounty");
            Api.FloatText($"<color=#FFD966>Bounty: {pay}</color>", e.Other.transform.position);
        }
    }

    /// <summary>Counts your turns with a plain Harmony patch.</summary>
    [HarmonyPatch(typeof(Player_AbilityControl), nameof(Player_AbilityControl.Turn))]
    internal static class Patch_TurnCount
    {
        private static void Postfix() => BountyPlugin.Ctx?.SetSaved("turns", BountyPlugin.Ctx.GetSaved("turns", 0) + 1);
    }
}
