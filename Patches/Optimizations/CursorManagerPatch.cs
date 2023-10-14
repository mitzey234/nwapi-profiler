namespace CustomProfiler.Patches.Optimizations;

using CursorManagement;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using static HarmonyLib.AccessTools;

[HarmonyPatch(typeof(CursorManager), nameof(CursorManager.LateUpdate))]
public static class CursorManagerPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator generator)
    {
        return new CodeInstruction[]
        {
            // StaticUnityMethods.OnLateUpdate -= CursorManager.LateUpdate;
            new(OpCodes.Ldnull),
            new(OpCodes.Ldftn, Method(typeof(CursorManager), nameof(CursorManager.LateUpdate))),
            new(OpCodes.Newobj, Constructor(typeof(Action), [typeof(object), typeof(nint)])),
            new(OpCodes.Call, Method(typeof(StaticUnityMethods), "remove_OnLateUpdate")),
            new(OpCodes.Ret),
        };
    }
}
