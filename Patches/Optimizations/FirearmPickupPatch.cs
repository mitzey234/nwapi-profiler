namespace CustomProfiler.Patches.Optimizations;

using CustomProfiler.Extensions;
using HarmonyLib;
using InventorySystem.Items.Firearms;
using PluginAPI.Core;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using static HarmonyLib.AccessTools;

[HarmonyPatch(typeof(FirearmPickup))]
public static class FirearmPickupPatch
{
    [HarmonyTranspiler]
    [HarmonyPatch(nameof(FirearmPickup.Update))]
    private static IEnumerable<CodeInstruction> Update_Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator generator)
    {
        instructions.BeginTranspiler(out List<CodeInstruction> newInstructions);

        newInstructions.InsertRange(0, new CodeInstruction[]
        {
            //
            // this.enabled = false;
            //
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldc_I4_0),
            new(OpCodes.Call, PropertySetter(typeof(Behaviour), nameof(Behaviour.enabled))),
        });

        return newInstructions.FinishTranspiler();
    }

    [HarmonyTranspiler]
    [HarmonyPatch(nameof(FirearmPickup.NetworkStatus), MethodType.Setter)]
    private static IEnumerable<CodeInstruction> NetworkStatus_Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator generator)
    {
        instructions.BeginTranspiler(out List<CodeInstruction> newInstructions);

        newInstructions.InsertRange(0, new CodeInstruction[]
        {
            //
            // this.enabled = true;
            //
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldc_I4_1),
            new(OpCodes.Call, PropertySetter(typeof(Behaviour), nameof(Behaviour.enabled))),
        });

        return newInstructions.FinishTranspiler();
    }
}
