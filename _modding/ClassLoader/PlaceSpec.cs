using System.Collections.Generic;
using Newtonsoft.Json;

namespace SCClassLoader
{
    /// <summary>
    /// A place type for generated side areas ("sideAreas": { "places": [...] }): how its layout grows, how it looks,
    /// and the art kit its floors, walls, doors and props are built from. Kinds pick places with "place" / "places".
    /// </summary>
    public class PlaceSpec
    {
        public string id, name, text;
        public bool enabled = true;
        /// <summary>
        /// spine: a long gangway with compartments off both sides and something at each end (a ship).
        /// hub: a central hall with corridors out to wings (a base).
        /// grid: a straight corridor with equal rooms at a steady rhythm and small cells off them (a lab).
        /// caves: blobby caverns joined by winding tunnels (a mine).
        /// </summary>
        public string style = "spine";
        /// <summary>Rooms to grow (corridors not counted): [min, max]. A kind's "size" overrides it.</summary>
        public int[] rooms = { 10, 14 };
        /// <summary>Side lengths of ordinary rooms: [min, max] tiles.</summary>
        public int[] roomSize = { 3, 6 };
        /// <summary>Corridor and tunnel lengths: [min, max] tiles.</summary>
        public int[] corridor = { 3, 7 };
        /// <summary>The spine or main corridor: length [min, max] and width in tiles.</summary>
        public int[] spine = { 18, 26 };
        public int spineWidth = 1;
        /// <summary>Ceiling height in metres.</summary>
        public float height = 3f;
        /// <summary>Chance that two rooms with only a wall tile between them get a second way through (loops).</summary>
        public float loops = 0.3f;
        /// <summary>Connections with no door at all (an open archway or a bare gap in the rock).</summary>
        public float archways;
        /// <summary>Share of doors that start locked, and of the rest that are jammed open.</summary>
        public float lockedDoors = 0.2f, jammedOpen = 0.1f;
        /// <summary>Floor tiles per enemy spot and per loot spot.</summary>
        public int tilesPerEnemy = 14, tilesPerLoot = 22;
        /// <summary>Chance each enemy spot stays empty.</summary>
        public int emptyChance = 25;
        public PlaceLookSpec look = new PlaceLookSpec();
        public PlaceKitSpec kit = new PlaceKitSpec();
        /// <summary>Hand-drawn special rooms the generator places (the bridge, the vault, the reactor).</summary>
        public List<PlaceTemplateSpec> templates = new List<PlaceTemplateSpec>();

        [JsonIgnore] public ModPack pack;
    }

    public class PlaceLookSpec
    {
        /// <summary>Flat ambient light, distance fog colour and range.</summary>
        public string ambient = "#202630", fog = "#06080B";
        public float fogStart = 6f, fogEnd = 30f;
        /// <summary>Ceiling lights: colour, strength, reach, how many floor tiles each one covers, and flicker (0-1).</summary>
        public string light = "#FFE2B8";
        public float lightIntensity = 1.3f, lightRange = 7.5f;
        public int tilesPerLight = 14;
        public float flicker;
        /// <summary>A second light colour some rooms get instead (warning lamps, specimen tanks).</summary>
        public string accent;
        public float accentChance;
        /// <summary>Colours of the stand-in surfaces when the kit has no model for them.</summary>
        public string floor = "#3B4047", wall = "#4C535C", ceiling = "#2A2E34", door = "#6B7480";
    }

    /// <summary>The art kit. Every piece is a model (a .glb in the mod folder); a missing piece falls back to a plain stand-in.</summary>
    public class PlaceKitSpec
    {
        /// <summary>1x1 m floor tiles (variants picked at random), wall panels 1 m wide, ceiling tiles.</summary>
        public List<ModelSpec> floor, wall, ceiling;
        /// <summary>A sliding door leaf filling a 1 m doorway, and the wall piece above it.</summary>
        public ModelSpec door, lintel;
        public float doorHeight = 2.3f;
        /// <summary>How the leaf moves when it opens: side (slides into the wall) or up.</summary>
        public string doorSlide = "side";
        /// <summary>The way out: stands against a wall of the arrival room.</summary>
        public ModelSpec exit;
        public float exitSize = 2.2f;
        public List<PlacePropSpec> props = new List<PlacePropSpec>();
    }

    public class PlacePropSpec
    {
        public string id;
        public ModelSpec model;
        /// <summary>wall (against a wall, facing in), corner, centre (away from walls), any.</summary>
        public string where = "wall";
        /// <summary>Takes up its tile (nothing walks there); props that don't block are scenery you walk past.</summary>
        public bool blocks = true;
        /// <summary>Longest side in metres.</summary>
        public float size = 1f;
        public int weight = 10;
        /// <summary>Only in rooms of these roles (empty = any non-corridor room).</summary>
        public List<string> roles;
        /// <summary>Loot can sit on or beside it (crates, lockers).</summary>
        public bool loot;
        /// <summary>
        /// A container you can search: the game's own (the real floor's crate or locker, with its lock, its odds of
        /// being a mimic, and a small haul) stands on the tile instead of the model.
        /// </summary>
        public bool search;
        /// <summary>Glow colour; a small light sits on it.</summary>
        public string glow;
    }

    /// <summary>
    /// A special room drawn as text: '#' rock or hull, '.' floor, 'D' a doorway on the outer ring, 'E' the exit,
    /// 'L' loot, 'S' an enemy spot, 'V' the vault's heart, '1'-'9' or letters in "legend" = props by id.
    /// </summary>
    public class PlaceTemplateSpec
    {
        public string id, role;
        /// <summary>start (the arrival room, holds the exit), bow / stern (the ends of a spine), far (deep inside), any.</summary>
        public string at = "any";
        public int count = 1;
        public float chance = 1f;
        public List<string> map = new List<string>();
        public Dictionary<string, string> legend = new Dictionary<string, string>();
        public bool noEnemies;
    }
}
