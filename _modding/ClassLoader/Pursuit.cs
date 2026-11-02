using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace SCClassLoader
{
    /// <summary>
    /// Each step an enemy takes lists its room and the rooms beside it, and asks for every tile whether the list has it yet,
    /// which walks the list each time. The game's steps are left as they are; only "does this list have it" is answered from
    /// a set that grows with the list.
    /// </summary>
    internal static class Pursuit
    {
        private const int WorthASet = 32;

        private static readonly HashSet<NavNode> set = new HashSet<NavNode>();
        private static List<NavNode> asked;
        private static int counted;

        internal static bool Has(List<NavNode> list, NavNode node)
        {
            if (!EventBus.On || list.Count < WorthASet) return list.Contains(node);
            if (!ReferenceEquals(list, asked) || list.Count < counted)
            {
                asked = list;
                counted = 0;
                set.Clear();
            }
            for (; counted < list.Count; counted++) set.Add(list[counted]);
            return set.Contains(node);
        }

        [HarmonyPatch]
        private static class Patch_Steps
        {
            private static MethodBase TargetMethod() => AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(AI), "DoPursuit"));

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
            {
                MethodInfo slow = AccessTools.Method(typeof(List<NavNode>), nameof(List<NavNode>.Contains));
                MethodInfo fast = AccessTools.Method(typeof(Pursuit), nameof(Has));
                foreach (CodeInstruction i in code)
                {
                    if (i.Calls(slow)) { i.opcode = OpCodes.Call; i.operand = fast; }
                    yield return i;
                }
            }
        }
    }
}
