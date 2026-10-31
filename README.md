# Breachwork: loader and mods for StarCrawlers Chimera

Arendeth's(Hellcommander here on github) (no that is not my real name) mod project for *StarCrawlers Chimera*. It is unofficial and not made or endorsed by Juggernaut Games.

**Download (players):** `Breachwork-<version>.7z` from the
[Nexus Mods page](https://www.nexusmods.com/starcrawlerschimera/mods/1). It holds BepInEx, the compiled loader and
every mod; unpack it into the game folder.

The repo lives in the game's install folder and tracks only our own work. The `.gitignore` allowlists:

| Path | What it is | License part |
|---|---|---|
| `_modding/ClassLoader/` | The BepInEx plugin source (C#) | Public, Breachwork license |
| `_modding/ModManager/` | The desktop mod manager (C#, WinForms) | Public, Breachwork license |
| `_modding/*.py`, `_modding/audio/` | Generators for icons, props and textures; the sound pipeline and its references to the licensed sounds | Public, Breachwork license |
| `_modding/LICENSE.md` | The master license | Public |
| `Mods/` | Every mod (classes, items, art, meshes) and `Mods/README.md`, the modder docs | Public, Breachwork license (`Mods/LICENSE.md`); no sound may be sold as a sound or audio pack; `audio_licensed/` not commercially at all |
| `Mods/ExamplePlugin/src/`, `Mods/RealtimeWithPause/src/`, `Mods/CharacterPortraits/src/`, `Mods/CharacterPortraits/toolsrc/` | The example code plugins' source | Free for any use (`_modding/LICENSE.md` section 2) |
| `Mods/ExampleScript/scripts/` | The example script mod's Lua | Free for any use (`_modding/LICENSE.md` section 2) |
| `_modding/ClassLoader/lib/` | MoonSharp, the Lua interpreter behind script mods (third-party) | BSD 3-Clause (`lib/MoonSharp-LICENSE.txt`) |
| `BepInEx/plugins/StarCrawlersClassLoader/` | The plugin's license and runtime art; the DLL itself is built from source | Public, Breachwork license |

The repo never contains anything from the game or other people. That means no decompiled code, no
`vanilla_reference.txt`, no BepInEx framework, no saves and no test screenshots. Mod audio isn't here either: the release packages carry it, and a build from a clone
plays the game's own sounds in its place. The test harnesses, release tooling and design notes stay private
(`_modding/LICENSE.md` section 6).

## Build

```
cd _modding/ClassLoader
dotnet build -c Release -p:PlayerBuild=true
cd ../ModManager
dotnet build -c Release -p:PlayerBuild=true
```

The build needs the game and BepInEx installed in this folder. `PlayerBuild` leaves out the private test harness, so a
clone has to set it. The output stays in each project's `bin/` folder.
