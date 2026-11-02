using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// Every ranged shot and every AI change into a fight searches the whole body, bone by bone, for the parts that draw a weapon,
    /// and a part only ever draws once. Nothing adds those parts during play, so with the bus on a body is searched once.
    /// </summary>
    internal static class Swaps
    {
        private static readonly Dictionary<GameObject, TransformSwap[]> known = new Dictionary<GameObject, TransformSwap[]>();

        internal static TransformSwap[] Of(GameObject body, bool includeInactive)
        {
            if (!EventBus.On) return body.GetComponentsInChildren<TransformSwap>(includeInactive);
            if (!known.TryGetValue(body, out TransformSwap[] swaps)) known[body] = swaps = body.GetComponentsInChildren<TransformSwap>(includeInactive);
            return swaps;
        }

        /// <summary>A load has destroyed these bodies.</summary>
        internal static void Forget() => known.Clear();

        [HarmonyPatch]
        private static class Patch_Search
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(MA_Ranged), nameof(MA_Ranged.Use)));
                yield return AccessTools.Method(typeof(AI), nameof(AI.ChangeState));
            }

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code, MethodBase original)
            {
                bool found = false;
                foreach (CodeInstruction ci in code)
                {
                    if ((ci.opcode == OpCodes.Callvirt || ci.opcode == OpCodes.Call) && ci.operand is MethodInfo m && m.IsGenericMethod && m.Name == nameof(GameObject.GetComponentsInChildren)
                        && m.GetGenericArguments()[0] == typeof(TransformSwap) && m.GetParameters().Length == 1)
                    {
                        ci.opcode = OpCodes.Call;
                        ci.operand = AccessTools.Method(typeof(Swaps), nameof(Of));
                        found = true;
                    }
                    yield return ci;
                }
                if (!found) Plugin.Log.LogWarning($"Event bus: no weapon-part search found in {original.DeclaringType?.Name}.{original.Name}");
            }
        }
    }
}
