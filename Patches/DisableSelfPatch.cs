namespace CustomProfiler.Patches;

using HarmonyLib;
using Interactables.Interobjects.DoorUtils;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using static HarmonyLib.AccessTools;

/// <summary>
/// This patch automatically forces behaviours to disable themselves.
/// </summary>
[HarmonyPatch]
public static class DisableSelfPatch
{
    private static IEnumerable<MethodInfo> TargetMethods()
    {
        yield return Method(typeof(DoorNametagExtension), "Start");
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator generator)
    {
        return new CodeInstruction[]
        {
            // this.enabled = false;
            // return;
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldc_I4_0),
            new(OpCodes.Call, PropertySetter(typeof(Behaviour), nameof(Behaviour.enabled))),
            new(OpCodes.Ret),
        };
    }
}
