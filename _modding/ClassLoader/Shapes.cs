using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// Cone and line areas ("shape" on a class or creature skill), aimed from the caster toward the target or aim
    /// point. A line runs the skill's full range and pierces everyone on it, a cone fans out over that range. Measured
    /// on the floor plane, the way the grid sees it.
    /// </summary>
    internal static class Shapes
    {
        internal static bool Is(string shape) => shape == "cone" || shape == "line";

        /// <summary>The ones among `candidates` inside the shape from `from` toward `aim`, nearest first.</summary>
        internal static List<Character> Hit(IEnumerable<Character> candidates, Vector3 from, Vector3 aim, string shape, float reach, float width, float angle)
        {
            Vector3 dir = Flat(aim - from);
            if (dir.sqrMagnitude < 0.0001f) return new List<Character>();
            dir.Normalize();
            float half = Mathf.Max(0.5f, width) * 0.5f + 0.35f; // a tile's slack either side
            float cosHalf = Mathf.Cos(Mathf.Clamp(angle, 10f, 180f) * 0.5f * Mathf.Deg2Rad);
            var hits = new List<(Character c, float along)>();
            foreach (Character c in candidates)
            {
                if (c == null || c.GetIsDead()) continue;
                Vector3 v = Flat(Pos(c) - from);
                float along = Vector3.Dot(v, dir);
                if (along <= 0.3f || along > reach + 0.45f) continue;
                bool inside = shape == "line"
                    ? (v - dir * along).magnitude <= half
                    : Vector3.Dot(v.normalized, dir) >= cosHalf || (v - dir * along).magnitude <= 0.6f; // the tile straight ahead always counts
                if (inside) hits.Add((c, along));
            }
            return hits.OrderBy(h => h.along).Select(h => h.c).ToList();
        }

        /// <summary>Where the shape's sweep particles fly: the far end of a line, or the middle and both edges of a cone.</summary>
        internal static List<Vector3> Ends(Vector3 from, Vector3 aim, string shape, float reach, float angle)
        {
            Vector3 dir = Flat(aim - from);
            if (dir.sqrMagnitude < 0.0001f) return new List<Vector3>();
            dir.Normalize();
            if (shape == "line") return new List<Vector3> { from + dir * reach };
            float a = Mathf.Clamp(angle, 10f, 180f) * 0.5f;
            return new[] { -a, 0f, a }.Select(d => from + Quaternion.Euler(0f, d, 0f) * dir * reach).ToList();
        }

        /// <summary>Tooltip words: "a 60° cone" / "a line 1 wide that pierces".</summary>
        internal static string Describe(string shape, float reach, float width, float angle) =>
            shape == "line" ? $"a line {reach:0.#} long and {Mathf.Max(0.5f, width):0.#} wide that pierces everyone on it"
                            : $"a {Mathf.RoundToInt(Mathf.Clamp(angle, 10f, 180f))}° cone {reach:0.#} long";

        internal static string Who(string shape) => shape == "line" ? "each enemy on the line" : "each enemy in the cone";

        private static Vector3 Pos(Character c) =>
            c.GetMover() != null && c.GetMover().GetCurrentNavNode() != null ? c.GetMover().GetCurrentNavNode().transform.position : c.transform.position;

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
