using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// A body glow fades in by walking every renderer under it and under the body it wraps, twice each, reading each material's
    /// name and looking its colours up by text, every frame of the fade, once per mesh of every enemy wearing one. This is the
    /// same fade without the repeats: names and start colours are remembered, the lists are reused, and with many fading at once
    /// each takes its turn over a few frames. The time it takes and where it ends are the game's own.
    /// </summary>
    internal static class MeshFade
    {
        private const int FadesPerStep = 16, MostStride = 6;

        private sealed class Memory
        {
            public readonly Dictionary<int, int[]> colours = new Dictionary<int, int[]>(); // by material; empty for one that isn't a glow
            public readonly Dictionary<long, float> starts = new Dictionary<long, float>();
        }

        private static readonly ConditionalWeakTable<PSMeshRendererUpdater, Memory> memories = new ConditionalWeakTable<PSMeshRendererUpdater, Memory>();
        private static readonly AccessTools.FieldRef<PSMeshRendererUpdater, string[]> ColourNames = AccessTools.FieldRefAccess<PSMeshRendererUpdater, string[]>("colorProperties");
        private static readonly List<Renderer> renderers = new List<Renderer>();
        private static readonly List<Material> materials = new List<Material>();
        private static readonly List<ME_LightCurves> curves = new List<ME_LightCurves>();
        private static readonly List<Light> lights = new List<Light>();
        private static readonly List<ParticleSystem> particles = new List<ParticleSystem>();
        private static readonly List<ME_TrailRendererNoise> trails = new List<ME_TrailRendererNoise>();
        private static readonly int[] None = new int[0];

        private static int frame, fadesThisFrame, stride = 1;

        internal static int Skipped;

        private static int[] ColoursOf(PSMeshRendererUpdater self, Memory memory, Material mat)
        {
            int id = mat.GetInstanceID();
            if (memory.colours.TryGetValue(id, out int[] found)) return found;
            found = None;
            if (mat.name.Contains("MeshEffect"))
            {
                string[] names = ColourNames(self);
                var ids = new int[names.Length];
                for (int i = 0; i < names.Length; i++) ids[i] = names[i] != null && mat.HasProperty(names[i]) ? Shader.PropertyToID(names[i]) : 0;
                found = ids;
            }
            memory.colours[id] = found;
            return found;
        }

        private static void Paint(PSMeshRendererUpdater self, Memory memory, GameObject under, float alpha, Dictionary<string, float> starts)
        {
            under.GetComponentsInChildren(true, renderers);
            string[] names = ColourNames(self);
            foreach (Renderer r in renderers)
            {
                r.GetMaterials(materials);
                int hash = r.GetHashCode();
                for (int m = 0; m < materials.Count; m++)
                {
                    Material mat = materials[m];
                    if (mat == null) continue;
                    int[] ids = ColoursOf(self, memory, mat);
                    for (int p = 0; p < ids.Length; p++)
                    {
                        if (ids[p] == 0) continue;
                        long key = ((long)hash << 20) ^ ((long)m << 4) ^ (long)p;
                        if (!memory.starts.TryGetValue(key, out float start))
                        {
                            starts.TryGetValue(hash.ToString() + m + names[p], out start);
                            memory.starts[key] = start;
                        }
                        Color c = mat.GetColor(ids[p]);
                        c.a = alpha * start;
                        mat.SetColor(ids[p], c);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(PSMeshRendererUpdater), "UpdateVisibleStatus")]
        private static class Patch_Fade
        {
            private static bool Prefix(PSMeshRendererUpdater __instance, float ___alpha, bool ___needUpdate, Dictionary<string, float> ___startAlphaColors)
            {
                if (!EventBus.On) return true;
                if (Time.frameCount != frame)
                {
                    frame = Time.frameCount;
                    stride = Mathf.Clamp(1 + fadesThisFrame / FadesPerStep, 1, MostStride);
                    fadesThisFrame = 0;
                }
                if (___needUpdate)
                {
                    fadesThisFrame++;
                    if (stride > 1 && (__instance.GetInstanceID() & int.MaxValue) % stride != frame % stride) { Skipped++; return false; } // the last step always lands
                }

                Memory memory = memories.GetOrCreateValue(__instance);
                bool active = __instance.IsActive;
                Paint(__instance, memory, __instance.gameObject, ___alpha, ___startAlphaColors);
                if (__instance.MeshObject != null) Paint(__instance, memory, __instance.MeshObject, ___alpha, ___startAlphaColors);
                else __instance.IsActive = false; // the body went first: fade out and clean up, as the game can't

                __instance.GetComponentsInChildren(true, curves);
                foreach (ME_LightCurves c in curves) c.enabled = active;
                if (!active)
                {
                    __instance.GetComponentsInChildren(true, lights);
                    for (int i = 0; i < lights.Count; i++)
                        if (___startAlphaColors.TryGetValue(lights[i].GetHashCode().ToString() + i, out float start)) lights[i].intensity = ___alpha * start;
                }
                __instance.GetComponentsInChildren(true, particles);
                foreach (ParticleSystem p in particles)
                {
                    if (!active && !p.isStopped) p.Stop();
                    if (active && p.isStopped) p.Play();
                }
                __instance.GetComponentsInChildren(false, trails);
                foreach (ME_TrailRendererNoise t in trails) t.IsActive = active;
                return false;
            }
        }
    }
}
