namespace Optimizer.Extensions;

using HarmonyLib;
using NorthwoodLib.Pools;
using System.Collections.Generic;

public static class TranspilerExtensions
{
    public static void BeginTranspiler(this IEnumerable<CodeInstruction> instructions, out List<CodeInstruction> newInstructions)
    {
        newInstructions = ListPool<CodeInstruction>.Shared.Rent(instructions);
    }

    public static IEnumerable<CodeInstruction> FinishTranspiler(this List<CodeInstruction> newInstructions)
    {
        for (int i = 0; i < newInstructions.Count; i++)
        {
            yield return newInstructions[i];
        }

        ListPool<CodeInstruction>.Shared.Return(newInstructions);
    }
}
