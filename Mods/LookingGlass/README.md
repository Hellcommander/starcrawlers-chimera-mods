# Looking-Glass Rogue: an example of conditions and triggers

A chaotic, fast Guile class (Agility + Psi Power) and a worked example of the loader's conditions, counters and extra
triggers (`Mods/README.md`, "Conditions, counters and triggers"). Where `Mods/ExampleClasses/cadence_fencer.xml`
shows each feature once, this class leans on them for its whole kit. Everything is in `looking_glass_rogue.json`.

## Named conditions

Defined once in the class's `"conditions"` and used by name everywhere else:

| Name | Holds when | Used by |
|---|---|---|
| `Small` / `Big` | you have Shrunk / Grown | Snicker-Snack, Drink Me, Croquet, a Curiouser whimsy, Vanishing Grin's steps |
| `Curious` | you have 4+ Wonder | Drink Me's haste, All Mad Here's silence and warp, The Grinning Cat's `usableWhen` |
| `WellMarked` | the target has 3+ Painted Red | (spare, for your own patches) |
| `Reeling` | the target is Topsy-Turvy **or** Silenced (`any`) | Grin of Teeth, Claw and Grin |
| `GuardsUp` | you have a Painted Guard out (`summons:PaintedGuard`) | Painted Guard's `allyDeath` proc, via `not` |
| `Puzzled` | the Riddles counter is 3+ | (spare) |

## Where each feature shows up

- **`when` on actions**: Snicker-Snack adds a Crushing hit `when: "Big"`, Wonder `when: "Small"`, and a finishing cut
  `when` the target is under 33% health. Drink Me hastes you `when: "Curious"`.
- **`if` / `then` / `else`**: Drink Me toggles size; Hedgehog Croquet nests two `if`s (Grown throws three tiles,
  Shrunk swaps places, otherwise a one-tile tap).
- **`usableWhen` / `targetWhen`**: The Grinning Cat needs `all: ["Curious", {inCombat}]`; Croquet and Which Way? refuse bosses (`not: {tag: "boss"}`).
- **Counters**: `Riddles` is reset on `combatStart` (`counterSet`), raised by the tea party's riddle and by All Mad
  Here (`counterMax`), and read by All Mad Here's damage (`stacksOf: "counter:Riddles"`).
- **Triggers** (all in `procs`):
  - `combatStart`: resets the Tea Clock and the riddles (Curiouser).
  - `turn` with `when: {inCombat}`: ticks the Tea Clock.
  - `cast` with `every: 3`: a random whimsy, weighted by Wonder (`weightPerStack`).
  - `effectGained` with `match: "@Shrunk"` / `"@Grown"` and `maxPerTurn: 1`.
  - `effectLost` with `match: "@GrinningCat"`: you come out of the cat Grinning.
  - `dodge`, `kill`: Wonder.
  - `crit` with `when: {all: ["Big", {chance: 50}]}` and `maxPerTurn`: Croquet's knockback.
  - `allyDeath` with `when: {not: "GuardsUp"}`: the last Painted Guard falling.
  - `wait` with `when: {enemiesNear:3 atLeast 1}`: the tea party's extra hour.
  - `lowHealth` with a `threshold`, a `cooldown` and an `enemiesNear` condition: Vanishing Grin's emergency shrink.
  - `move` with `every: 2` and `when: "Small"`.
- **Resource effects**: Wonder (a decaying meter), the Tea Clock (`burstAt: 6` pours tea and rolls a random outcome).
- **A transformation with a kit**: The Grinning Cat (`kit`, `keep`, `preventDeath` + `onSaveActions`), with procs of
  its own (`dodge` rakes the attacker, `hit` with `when: {lacks}` paints the unpainted).
- **Moves**: `warp` (`behind` in Warp Pounce, `random` in the tea party and All Mad Here), `swap` (Which Way?,
  Croquet), `throw`, `teleport`, and `show` (Grin of Teeth's oversized grin snapping shut on the target).
- **Summons**: Painted Guards (an animated mesh, an effect whose `hit` proc paints) and the Lingering Grin
  (`stationary`, `taunt`, and `onDeathActions` with a `chain`).

Art, particles, meshes and sounds come from the AAMT pipelines (`_modding/make_kelvin_glass_assets.py`).
