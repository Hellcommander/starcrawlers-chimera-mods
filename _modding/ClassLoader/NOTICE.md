# License of this folder

This folder is the class loader's source code. It is under the Breachwork license, `_modding/LICENSE.md`: use, change,
share and build on it for any purpose, commercially too. Credit Arendeth and Breachwork, add substantial original work of
your own, and don't reupload it unchanged. **Juggernaut Games** may use it in its games (section 7).

- **Not covered:** the `DevTest*.cs` files are Private Components (section 6). They are not published, and the
  loader builds without them (`dotnet build -c Release -p:PlayerBuild=true`).
- **Scripts are not covered either:** a mod's Lua scripts (`Mods/*/scripts/*.lua`) are their author's own, like code plugins.
- **`lib/MoonSharp.Interpreter.dll` is third-party:** the MoonSharp Lua interpreter (BSD 3-Clause, `lib/MoonSharp-LICENSE.txt`),
  redistributed unmodified next to the loader's dll so scripts can run. It is not ours, so its own license applies.
- **Code plugins are not covered:** one written against the plugin API is its author's own code, on its author's terms
  (section 2 of the project license).
- This code drives the game through its own methods and Harmony patches and contains no copy of the game's code.
  Building or running it needs a lawfully obtained copy of StarCrawlers Chimera; it grants no rights in the game.
