namespace CustomProfiler.Patches.Optimizations;

using CustomProfiler.Extensions;
using HarmonyLib;
using InventorySystem;
using InventorySystem.Items;
using PluginAPI.Events;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using static HarmonyLib.AccessTools;

[HarmonyPatch(typeof(Inventory))]
public static class InventoryPatch
{
    //
    // This commit fixes the microhid lasting forever.
    //
    // Note that optimizing the Inventory.Update method probably isnt worth it, as it isnt very intense.
    //
    // Y'know, you may be right

    //[HarmonyTranspiler]
    //[HarmonyPatch(nameof(Inventory.NetworkCurItem), MethodType.Setter)]
    private static IEnumerable<CodeInstruction> NetworkCurItem_Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator generator)
    {
        instructions.BeginTranspiler(out List<CodeInstruction> newInstructions);

        Label actionIsNull = generator.DefineLabel();
        Label actionIsNotNull = generator.DefineLabel();
        LocalBuilder newValueEqual = generator.DeclareLocal(typeof(bool));

        newInstructions.InsertRange(0, new CodeInstruction[]
        {
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldfld, Field(typeof(Inventory), nameof(Inventory.CurItem))),
            new(OpCodes.Ldarg_1),
            new(OpCodes.Call, Method(typeof(ItemIdentifier), "op_Equality", [typeof(ItemIdentifier), typeof(ItemIdentifier)])),
            new(OpCodes.Stloc_S, newValueEqual),
        });

        Label justReturn = generator.DefineLabel();

        newInstructions[newInstructions.Count - 1].labels.Add(justReturn);

        newInstructions.InsertRange(newInstructions.Count - 1, new CodeInstruction[]
        {
            new(OpCodes.Ldloc_S, newValueEqual),
            new(OpCodes.Brtrue_S, justReturn),

            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldfld, Field(typeof(Inventory), nameof(Inventory._hub))),
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldflda, Field(typeof(Inventory), nameof(Inventory._prevCurItem))),
            new(OpCodes.Ldfld, Field(typeof(ItemIdentifier), nameof(ItemIdentifier.SerialNumber))),
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldflda, Field(typeof(Inventory), nameof(Inventory.CurItem))),
            new(OpCodes.Ldfld, Field(typeof(ItemIdentifier), nameof(ItemIdentifier.SerialNumber))),
            new(OpCodes.Newobj, Constructor(typeof(PlayerChangeItemEvent), [typeof(ReferenceHub), typeof(ushort), typeof(ushort)])),
            new(OpCodes.Call, typeof(EventManager).GetMethods().First(x => x.Name == "ExecuteEvent" && !x.IsGenericMethod)),
            new(OpCodes.Pop),

            new(OpCodes.Ldsfld, Field(typeof(Inventory), nameof(Inventory.OnCurrentItemChanged))),
            new(OpCodes.Dup),
            new(OpCodes.Brtrue_S, actionIsNotNull),
            new(OpCodes.Pop),
            new(OpCodes.Br_S, actionIsNull),

            new CodeInstruction(OpCodes.Ldarg_0)
                .WithLabels(actionIsNotNull),
            new(OpCodes.Ldfld, Field(typeof(Inventory), nameof(Inventory._hub))),
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldfld, Field(typeof(Inventory), nameof(Inventory._prevCurItem))),
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldfld, Field(typeof(Inventory), nameof(Inventory.CurItem))),
            new(OpCodes.Callvirt, Method(typeof(System.Action<ReferenceHub, ItemIdentifier, ItemIdentifier>), "Invoke")),

            new CodeInstruction(OpCodes.Ldarg_0)
                .WithLabels(actionIsNull),
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldfld, Field(typeof(Inventory), nameof(Inventory.CurItem))),
            new(OpCodes.Stfld, Field(typeof(Inventory), nameof(Inventory._prevCurItem))),
        });

        return newInstructions.FinishTranspiler();
    }

    //[HarmonyTranspiler]
    //[HarmonyPatch(nameof(Inventory.Update))]
    private static IEnumerable<CodeInstruction> Update_Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator generator)
    {
        instructions.BeginTranspiler(out List<CodeInstruction> newInstructions);

        CodeInstruction replacement = new(OpCodes.Nop);

        int beginIndex = newInstructions.FindIndex(x => x.LoadsField(Field(typeof(Inventory), nameof(Inventory._prevCurItem)))) - 1;
        int lastIndex = newInstructions.FindIndex(x => x.Calls(PropertyGetter(typeof(Inventory), nameof(Inventory.IsObserver)))) - 1;

        for (int i = beginIndex; i < lastIndex + 1; i++)
        {
            newInstructions[i].MoveLabelsTo(replacement);
        }

        newInstructions.RemoveRange(beginIndex, lastIndex - beginIndex + 1);

        newInstructions.Insert(beginIndex, replacement);

        return newInstructions.FinishTranspiler();
    }
}
