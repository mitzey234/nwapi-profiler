namespace CustomProfiler.Patches.Optimizations;

using CustomProfiler.Extensions;
using HarmonyLib;
using InventorySystem.Items.Pickups;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

/// <summary>
/// This patch reduces pickup update rate from every 1/4 second to every 5 seconds.
/// </summary>
[HarmonyPatch(typeof(PickupStandardPhysics))]
public static class PickupStandardPhysicsPatch
{
    [HarmonyTranspiler]
    [HarmonyPatch("UpdateServer")]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator generator)
    {
        instructions.BeginTranspiler(out List<CodeInstruction> newInstructions);

        newInstructions.FindLast((CodeInstruction x) => x.opcode == OpCodes.Ldc_R8).operand = 5d;

        return newInstructions.FinishTranspiler();
    }
}
