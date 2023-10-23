namespace CustomProfiler.Patches.Optimizations;

using CustomProfiler.Extensions;
using HarmonyLib;
using PlayerRoles.PlayableScps.Scp096;
using PlayerRoles.PlayableScps.Scp173;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using static HarmonyLib.AccessTools;

[HarmonyPatch]
public static class ObserversTrackerPatch
{
    public const float SqrDistCannotSee = 57f;

    private static IEnumerable<MethodInfo> TargetMethods()
    {
        return new MethodInfo[]
        {
            Method(typeof(Scp173ObserversTracker), nameof(Scp173ObserversTracker.IsObservedBy)),
            Method(typeof(Scp096TargetsTracker), nameof(Scp096TargetsTracker.IsObservedBy)),
        };
    }

    [HarmonyTranspiler]
    [HarmonyPatch(nameof(Scp173ObserversTracker.IsObservedBy))]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator generator)
    {
        instructions.BeginTranspiler(out List<CodeInstruction> newInstructions);

        Label allowLabel = generator.DefineLabel();
        LocalBuilder tempLocal = generator.DeclareLocal(typeof(Vector3));

        int index = newInstructions.FindIndex(x => x.opcode == OpCodes.Stloc_0) + 1;

        newInstructions[index].labels.Add(allowLabel);

        newInstructions.InsertRange(index, new CodeInstruction[]
        {
            // if ((position - target.transform.position).sqrMagnitude > SqrDistCannotSee * SqrDistCannotSee)
            //     return false;
            new(OpCodes.Ldloc_0),
            new(OpCodes.Ldarg_1),
            new(OpCodes.Call, PropertyGetter(typeof(Component), nameof(Component.transform))),
            new(OpCodes.Call, PropertyGetter(typeof(Transform), nameof(Transform.position))),
            new(OpCodes.Call, Method(typeof(Vector3), "op_Subtraction", [typeof(Vector3), typeof(Vector3)])),
            new(OpCodes.Stloc_S, tempLocal),
            new(OpCodes.Ldloca_S, tempLocal),
            new(OpCodes.Call, PropertyGetter(typeof(Vector3), "sqrMagnitude")),
            new(OpCodes.Ldc_R4, SqrDistCannotSee * SqrDistCannotSee),
            new(OpCodes.Ble_Un_S, allowLabel),
            new(OpCodes.Ldc_I4_0),
            new(OpCodes.Ret)
        });

        return newInstructions.FinishTranspiler();
    }
}
