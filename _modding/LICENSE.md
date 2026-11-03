# Breachwork — License

**Version 3.1, 2026. Copyright © 2026 Arendeth ("we", "us").**

This project is an unofficial modification for *StarCrawlers Chimera*. It is not made, endorsed or supported by
Juggernaut Games. "StarCrawlers" and related names are trademarks of their owners.

Short version: **you may use, change and build on Breachwork for anything, commercially too.** Credit us, add something
of your own, and don't reupload it unchanged. The exceptions are the sounds, which can't be sold as sounds or audio packs, the game's own
material, and our private tests and tooling.

*Changes from version 3.0:* this is now the one full text: `Mods/LICENSE.md` is an exact copy, not a shorter license of
its own. Section 3 describes the sounds as they are now built and shipped. Copies already shared keep the terms they
came with.

---

## 1. What you may do

This license covers everything we made for the Project: the class loader and its source, the mod manager, the asset
generators, and the mods' classes, items, art, models, sounds and docs, apart from what sections 3 to 6 say.

You may use, copy, change, share and build on it, for any purpose, including commercially. If you release something
made from it:

1. **Credit** "Arendeth" and "Breachwork, a mod pack for StarCrawlers Chimera".
2. **Add something of your own.** A release must contain substantial original work.
3. **Don't reupload it unchanged.** Don't share the Project unchanged or mirror its releases without permission.
   Personal backups are fine.

Compatibility patches, translations, expansions, class packs, faction packs, gameplay overhauls and similar projects are
welcome. Keeping a copy of this license, or a link to it, with your release is enough to pass these terms on.

## 2. Plugins and scripts by other people

A code plugin (a `.dll` the loader runs from a mod's `plugins/` folder) or a Lua script (from a mod's `scripts/` folder)
written by someone else is its author's own work, not part of the Project. Its author decides its terms, and this
license doesn't apply to it. Anything of ours shipped along with one stays under this license.

The example code in `Mods/ExamplePlugin/src/`, `Mods/RealtimeWithPause/src/`, `Mods/CharacterPortraits/src/`, `Mods/CharacterPortraits/toolsrc/`, `Mods/Threading/src/` and
`Mods/ExampleScript/scripts/` is free to use, copy and change for any purpose, with no conditions and no credit needed.
So is the event bus, which lives in the loader because it needs the game's internals: in `_modding/ClassLoader/`, the files
`EventBus.cs`, `Bus*.cs`, `*Lane.cs`, `NodeIndex.cs`, `Pursuit.cs`, `ZoneLoad.cs`, `ParticleGate.cs` and `ParticleLoad.cs`.

We don't review plugins or scripts and don't stand behind them.

## 3. Sounds

**No sound in this Project may be sold as a sound or in an audio pack, by anyone**, altered or not. That covers the
`audio_licensed/` and `sfx/` folders and anything made from them. This keeps anyone from claiming the sounds, or the packs
behind them, as their own.

**Licensed sound effects.** The sounds in every `audio_licensed/` folder under `Mods/`, which ship sealed in each mod's
`sounds.pak`, are commercial sound effects by third parties, licensed to the person who goes by the alias Arendeth.
Neither they nor anything made from them may be used commercially at all, the Developer included.
They were rebuilt for this Project from the packs' clips by plain signal processing, with no AI. They remain derivative
works of the originals. `Mods/AUDIO_CREDITS.md` lists the pack and original file behind each one:

- Function Loops, *Ultimate SFX for Games* (sold through Humble Bundle);
- Tao & Sound, *Magic Spells SFX Bundle*, *Inventory SFX Bundle* and *Puzzle Audio Kit*;
- Ovani Sound, *Ice*, *Microscale Bugs* and *Robotic Beings Sound FX Packs*;
- Noise Alchemy, for clips layered into some of them.

These sounds may be shared **only inside the mods**, for use with StarCrawlers Chimera, sealed as they are in the release
packages. They are not in the source repository. No one may adapt or relicense them, extract them, share them on their
own, put them in a sound library or asset pack, or use them with AI tools or AI training. They may be removed from a copy
of the mods; the game's own sounds then play instead. If you adapt a mod, replace them with your own. A team that wants
to ship them, or sounds made from them, needs its own licence from the original publishers.

**Unique sounds.** The `sfx/` sounds are new, unique sounds made with our asset tool: rendered or transformed far from
any clip of the Dark Fantasy Studio and Noise Alchemy packs (the former by Nicolas Jeudy), or synthesized with no clip
at all. Those packs are designed as components for making new sounds. You may share and adapt the `sfx/` sounds and use
them in your own work, a commercial mod or game included, as long as you don't sell them as sounds or audio packs. A
pack's own clip, or a light edit of one, is never included and never counts as unique.
The packs' license requires this credit, which every package and `Mods/AUDIO_CREDITS.md` carry, and anyone who shares,
uses or adapts those sounds must keep it with them:

"Sound effects created with NOISE ALCHEMY – NOISEALCHEMY.COM"

## 4. Game material

We claim no rights in the game and grant none. Anything that belongs to the game or its makers must never be included in
what you share: its executable, code, data and assets, anything decompiled, extracted or dumped from it or from other
games, `_modding/decompiled/`, `_modding/sc1_decompiled/` and `Mods/vanilla_reference.txt`. Game ids in our files are
references the game needs to find its own assets; naming them licenses nothing. Building or running the Project needs a
lawfully obtained copy of StarCrawlers Chimera.

## 5. Other third-party material

These keep their own licenses, and nothing here changes them: BepInEx (LGPL-2.1), HarmonyX / Harmony (MIT), MonoMod and
Mono.Cecil (MIT), Newtonsoft.Json (MIT), MoonSharp (BSD 3-Clause) and any other library in `BepInEx/` or
`_modding/ClassLoader/lib/`.

`_modding/archive_dredmor_derived/` holds work derived from third-party *Dungeons of Dredmor* mods. It is a private
archive, not covered by this license, and must not be redistributed. Our content was only *inspired by* other games' and
mods' themes and mechanics; it reuses no names, text, art or audio from them.

## 6. Private components

These stay private, all rights reserved, and may not be published or shared without our permission: the test harnesses
(the loader's self-test sources and scripts, listed in `_modding/ClassLoader/NOTICE.md`, and `_modding/ModManager/SelfTest.cs`),
the release tooling (`_modding/pack.ps1`, `_modding/player/`, `_modding/DEV_README.template.md`) and the design notes
(`_modding/*.md` other than `_modding/LICENSE.md`). The player texts from `_modding/player/` may be passed on unchanged
inside the release packages they ship in.

## 7. Juggernaut Games

Juggernaut Games, with its successors, affiliates, publishers, employees and contractors while they work on its titles (the
"Developer"), may use anything in this Project in StarCrawlers Chimera and any other of its titles, updates, DLC or tools,
commercially, with no credit needed and no further permission. That is a **perpetual, irrevocable, worldwide,
royalty-free, non-exclusive** grant, and it includes keeping and using the private components, though it may not publish
those outside its own products. It doesn't cover the `audio_licensed/` sounds or section 5's material; the developer package
carries those so the mods can be played complete. We waive any claim against the Developer for using, or independently
creating, similar features.

## 8. Contributions

By sending us a change, asset or fix, you confirm it is your own work, license it under these terms, and grant us a
perpetual, worldwide, royalty-free license to use, change, relicense and include it in the Project.

## 9. No warranty

The Project is provided **"as is"**, without warranty of any kind, including any warranty that it is fit for a particular
purpose or that it keeps your saves intact. **Back up your saves.** To the extent the law allows, we are not liable for
any damage that arises from using the Project.

## 10. Which terms apply

If a file or folder carries a third party's license, or says it is free to use, that notice wins for that file or
folder. `BepInEx/plugins/StarCrawlersClassLoader/LICENSE.txt`, `_modding/ClassLoader/NOTICE.md`, the `license` line in
each `mod.json` and other short summaries change nothing; where one differs, this text wins.
