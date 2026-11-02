# Looking-Glass Rogue: an example of conditions and triggers

A chaotic, fast Guile class (Agility + Psi Power) and a worked example of the loader's conditions, counters and extra
triggers (`Mods/README.md`, "Conditions, counters and triggers"). Where `Mods/ExampleClasses/cadence_fencer.xml`
shows each feature once, this class leans on them for its whole kit. Everything is in `looking_glass_rogue.json`.

## Named conditions

Defined once in the class's `"conditions"` and used by name everywhere else:

| Name | Holds when | Used by |
|---|---|---|
| `Small` / `Big` | you have Shrunk / Grown | Sentence First, One Side Other Side, The Looking-Glass, a Curiouser whimsy, Vanishing Grin's steps |
| `Curious` | you have 4+ Wonder | One Side's haste, All Mad Here's silence and warp, The Grinning Cat's `usableWhen` |
| `WellMarked` | the target has 3+ Painted Red | (spare, for your own patches) |
| `Reeling` | the target is Topsy-Turvy **or** Silenced (`any`) | Grin of Teeth, Claw and Grin |
| `Puzzled` | the Riddles counter is 3+ | (spare) |

## Where each feature shows up

- **`when` on actions**: Sentence First adds a Crushing hit `when: "Big"` and Wonder `when: "Small"`, and its perk can
  behead `when` the target is sentenced and under 33% health. One Side, Other Side hastes you `when: "Curious"`.
- **`if` / `then` / `else`**: One Side, Other Side takes size or gives it; The Looking-Glass summons on the first cast and
  swaps on the next.
- **`usableWhen` / `targetWhen`**: The Grinning Cat needs `all: ["Curious", {inCombat}]`; Six Impossible Things needs an hour on the clock; Down the Rabbit Hole and Which Way? refuse bosses (`not: {tag: "boss"}`).
- **Counters**: `Riddles` is reset on `combatStart` (`counterSet`), raised by No-Name Wood and by All Mad
  Here (`counterMax`), and read by All Mad Here's damage (`stacksOf: "counter:Riddles"`).
- **Triggers** (all in `procs`):
  - `combatStart`: resets the Tea Clock and the riddles (Curiouser).
  - `turn` with `when: {inCombat}`: ticks the Tea Clock.
  - `cast` with `every: 3`: a random whimsy, weighted by Wonder (`weightPerStack`).
  - `effectGained` with `match: "@Shrunk"` / `"@Grown"` and `maxPerTurn: 1`.
  - `effectLost` with `match: "@GrinningCat"`: you come out of the cat Grinning.
  - `dodge`, `kill`: Wonder.
  - `wait` with `when: {enemiesNear:3 atLeast 1}`: Six Impossible Things's extra hour.
  - `lowHealth` with a `threshold`, a `cooldown` and an `enemiesNear` condition: Vanishing Grin's emergency shrink.
  - `move` with `every: 2` and `when: "Small"`.
  - `effectGained` driving another body: The Looking-Glass keeps the reflection at the opposite size.
  - `hurt` with `when: {has @Sentenced, of attacker}`: Sentence First's crime.
- **Resource effects**: Wonder (a decaying meter), the Tea Clock (`burstAt: 6` pours tea and rolls a random outcome).
- **A transformation with a kit**: The Grinning Cat (`kit`, `keep`, `preventDeath` + `onSaveActions`), with procs of
  its own (`dodge` rakes the attacker, `hit` with `when: {lacks}` paints the unpainted).
- **Delayed effects**: Sentenced runs a `random` verdict from `onExpireActions`, weighted by Wonder.
- **A copy of you**: The Looking-Glass (`creature: "self"`, a second cast with `swap` + `to: "summon"`); `modelScale` on the size effects shows them.
- **Spending a meter**: Six Impossible Things turns Tea Clock stacks into `castRules` stacks (`bonusPerStack`, `consumeStacks`).
- **Moves**: `warp` (`random` in All Mad Here), `swap` (Which Way?, The Looking-Glass), `teleport`, and `show` (Grin of Teeth's oversized grin snapping shut on the target).
- **Summons**: your Reflection and the Lingering Grin (`stationary`, `taunt`, and `onDeathActions` with a `chain`).
- **A delayed drop**: Down the Rabbit Hole stuns, shrinks with `modelScale`, and lands through `onExpireActions`.

Art, particles, meshes and sounds come from the AAMT pipelines (`_modding/make_kelvin_glass_assets.py`).
