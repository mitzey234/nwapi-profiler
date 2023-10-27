namespace CustomProfiler.Patches.MemoryOptimizations;

using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Reflection;
using System.Runtime.CompilerServices;
using CustomProfiler.Extensions;
using static HarmonyLib.AccessTools;

[HarmonyPatch(typeof(CharacterClassManager), nameof(CharacterClassManager.NetworkMaxPlayers), MethodType.Setter)]
public static class CCMSetMaxPlayers
{
    private static ConditionalWeakTable<CharacterClassManager, Action<ushort, ushort>> storedDelegate = new();
    private static ConditionalWeakTable<CharacterClassManager, Action<ushort, ushort>>.CreateValueCallback createValue = CreateValue;

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator generator)
    {
        instructions.AddMethodAndApplyProfiler(method, generator).BeginTranspiler(out List<CodeInstruction> newInstructions);

        int index = newInstructions.FindIndex(x => x.opcode == OpCodes.Newobj);

        newInstructions.RemoveRange(index -= 1, 2);
        newInstructions.Insert(index, new(OpCodes.Call, Method(typeof(CCMSetMaxPlayers), nameof(GetDelegate))));

        return newInstructions;
    }

    private static Action<ushort, ushort> GetDelegate(CharacterClassManager instance)
    {
        return storedDelegate.GetValue(instance, createValue);
    }

    private static Action<ushort, ushort> CreateValue(CharacterClassManager instance)
    {
        return instance.MaxPlayersHook;
    }
}
