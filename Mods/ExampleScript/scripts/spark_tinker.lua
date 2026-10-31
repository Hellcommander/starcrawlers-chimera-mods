-- The Spark Tinker's rules, one of everything a script can do. spark_tinker.json uses these by name, so the class itself
-- stays plain data:
--   "do": "ExampleScript:arc"               a new action, with a field of its own ("reach")
--   "when": "ExampleScript:FullCharge"      a new condition
--   "value": "ExampleScript:salvaged"       a new number for value checks, saved with the run
--   "on": "ExampleScript:charged"           a new event for procs
--   "pluginData": { "ExampleScript:prices"  a new kind of content any mod's json can add to (scrap_prices.json)
-- An example to start your own script from: use, copy and change it for any purpose, with no conditions and no credit needed.

local util = require("util")

local prices = {}   -- creature kind -> credits, from every "pluginData" entry
local payout = mod.setting("Payout", 1, "Multiplies the credits every piece of scrap pays.")

mod.data("prices", function(entry)
  for _, p in ipairs(entry.value) do prices[p.kind] = p.credits end
end)

-- Arc Lash: shocks the target and every enemy within "reach", builds a charge, and tells procs about it.
mod.action("arc", function(call)
  local amount = call.amount()
  local marks = game.enemies_near(call.target, call.arg("reach", 2))
  local hitTarget = false
  for _, foe in ipairs(marks) do
    if foe == call.target then hitTarget = true end
  end
  if not hitTarget then table.insert(marks, call.target) end
  for _, foe in ipairs(marks) do
    call.run({ act = "damage", type = "Shock", min = amount, max = amount }, foe)
  end
  call.caster.add_counter("charge", 1)
  mod.fire("charged", call.target)
end, { describe = "Shocks {who} and everyone within reach for {amount}, and builds a charge", hostile = true })

mod.event("charged", "when you build a charge")

-- Overload's condition.
mod.condition("FullCharge", function(call, who)
  return call.caster.counter("charge") >= 3
end, "you hold three charges or more")

-- Overload's action: every charge is spent on everyone near you.
mod.action("discharge", function(call)
  local me = call.caster
  local charges = me.counter("charge")
  if charges == 0 then return end
  for _, foe in ipairs(game.enemies_near(me, call.arg("reach", 3))) do
    foe.damage(call.amount() * charges, "Shock", me, "Overload")
  end
  game.vfx("PA_Engineer_Boltron.PS_summon", me.pos)
  game.text("<color=#9FDCFF>Overload x" .. charges .. "</color>", me.pos)
  me.set_counter("charge", 0)
end, { describe = function(spec, who) return "Spends your charges: " .. spec.amount .. " Shock damage per charge to everyone near you" end })

-- A number for value checks: the scrap salvaged this run.
mod.value("salvaged", function(call, who) return mod.saved("salvaged", 0) end, "the scrap you have salvaged")

-- Every kill pays scrap by creature kind and goes in the tally saved with the run.
mod.on("kill", function(e)
  mod.save("salvaged", mod.saved("salvaged", 0) + 1)
  local credits = e.other and prices[e.other.kind]
  if credits then
    local pay = util.round(credits * payout.value)
    game.add_credits(pay)
    game.text("<color=#FFD966>Scrap: " .. pay .. "</color>", e.other.pos)
  end
end)

mod.on("ready", function()
  mod.log(util.count(prices) .. " scrap prices on the board, scripting API level " .. game.api_level)
end)

-- F9 shows the tally; a global function so other scripts (and the loader's self-test) can ask for it too.
function tally() return mod.saved("salvaged", 0) end
mod.key("F9", function() game.notify("Scrap salvaged: " .. tally()) end)
