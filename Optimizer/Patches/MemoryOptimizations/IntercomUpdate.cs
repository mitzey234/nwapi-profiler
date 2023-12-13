namespace Optimizer.Patches.MemoryOptimizations;

using Optimizer.Extensions;
using HarmonyLib;
using PlayerRoles.Voice;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using static HarmonyLib.AccessTools;

[HarmonyPatch(typeof(Intercom))]
public static class IntercomUpdate
{
    private static Func<ReferenceHub, bool> checkPlayer;

    [HarmonyTranspiler]
    [HarmonyPatch(nameof(Intercom.Awake))]
    private static IEnumerable<CodeInstruction> AwakeTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator generator)
    {
        instructions.BeginTranspiler(out List<CodeInstruction> newInstructions);

        newInstructions.InsertRange(0, new CodeInstruction[]
        {
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldftn, Method(typeof(Intercom), nameof(Intercom.CheckPlayer))),
            new(OpCodes.Newobj, Constructor(typeof(Func<ReferenceHub, bool>), [typeof(ReferenceHub), typeof(nint)])),
            new(OpCodes.Stsfld, Field(typeof(IntercomUpdate), nameof(checkPlayer))),
        });

        if (Intercom._singleton)
        {
            checkPlayer = Intercom._singleton.CheckPlayer;
        }

        return newInstructions.FinishTranspiler();
    }

    [HarmonyTranspiler]
    [HarmonyPatch(nameof(Intercom.Update))]
    private static IEnumerable<CodeInstruction> UpdateTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator generator)
    {
        instructions.BeginTranspiler(out List<CodeInstruction> newInstructions);

        int index = newInstructions.FindIndex(x => x.opcode == OpCodes.Newobj);

        newInstructions[index].opcode = OpCodes.Ldsfld;
        newInstructions[index].operand = Field(typeof(IntercomUpdate), nameof(checkPlayer));

        newInstructions.RemoveRange(index -= 2, 2);

        return newInstructions.FinishTranspiler();
    }
}
