namespace CustomProfiler.Patches.Optimizations;

using CustomProfiler.Extensions;
using Footprinting;
using HarmonyLib;
using InventorySystem.Items.Armor;
using InventorySystem.Items.Pickups;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using static HarmonyLib.AccessTools;

[HarmonyPatch(typeof(BodyArmorPickup))]
public static class BodyArmorPickupPatch
{
    [HarmonyTranspiler]
    [HarmonyPatch(nameof(BodyArmorPickup.Update))]
    private static IEnumerable<CodeInstruction> Update_Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator generator)
    {
        return new CodeInstruction[]
        {
            new(OpCodes.Ret),
        };
    }

    [HarmonyTranspiler]
    [HarmonyPatch(nameof(BodyArmorPickup.Start))]
    private static IEnumerable<CodeInstruction> Start_Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator generator)
    {
        Label nullLabel = generator.DefineLabel();

        // base.Start();
        // this.enabled = false;
        // this._rb = (base.PhysicsModule as PickupStandardPhysics).Rb;
        //
        // if (this.PreviousOwner.Hub != null)
        // {
        //     this._rb.rotation = this.PreviousOwner.Hub.transform.rotation * BodyArmorPickup.StartRotation;
        // }
        return new CodeInstruction[]
        {
            // base.Start();
            new(OpCodes.Ldarg_0),
            new(OpCodes.Call, Method(typeof(ItemPickupBase), nameof(ItemPickupBase.Start))),

            // this.enabled = false;
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldc_I4_0),
            new(OpCodes.Call, PropertySetter(typeof(Behaviour), nameof(Behaviour.enabled))),

            // this._rb = (base.PhysicsModule as PickupStandardPhysics).Rb;
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldarg_0),
            new(OpCodes.Call, PropertyGetter(typeof(ItemPickupBase), nameof(ItemPickupBase.PhysicsModule))),
            new(OpCodes.Isinst, typeof(PickupStandardPhysics)),
            new(OpCodes.Callvirt, PropertyGetter(typeof(PickupStandardPhysics), nameof(PickupStandardPhysics.Rb))),
            new(OpCodes.Stfld, Field(typeof(BodyArmorPickup), nameof(BodyArmorPickup._rb))),

            // if (this.PreviousOwner.Hub != null)
            // {
            //     this._rb.rotation = this.PreviousOwner.Hub.transform.rotation * BodyArmorPickup.StartRotation;
            // }
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldflda, Field(typeof(ItemPickupBase), nameof(ItemPickupBase.PreviousOwner))),
            new(OpCodes.Ldfld, Field(typeof(Footprint), nameof(Footprint.Hub))),
            new(OpCodes.Ldnull),
            new(OpCodes.Call, Method(typeof(ReferenceHub), "op_Equality", [typeof(ReferenceHub), typeof(ReferenceHub)])),
            new(OpCodes.Brtrue_S, nullLabel),

            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldfld, Field(typeof(BodyArmorPickup), nameof(BodyArmorPickup._rb))),
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldflda, Field(typeof(ItemPickupBase), nameof(ItemPickupBase.PreviousOwner))),
            new(OpCodes.Ldfld, Field(typeof(Footprint), nameof(Footprint.Hub))),
            new(OpCodes.Callvirt, PropertyGetter(typeof(Component), nameof(Component.transform))),
            new(OpCodes.Callvirt, PropertyGetter(typeof(Transform), nameof(Transform.rotation))),
            new(OpCodes.Ldsfld, Field(typeof(BodyArmorPickup), nameof(BodyArmorPickup.StartRotation))),
            new(OpCodes.Call, Method(typeof(Quaternion), "op_Multiply", [typeof(Quaternion), typeof(Quaternion)])),
            new(OpCodes.Callvirt, PropertySetter(typeof(Rigidbody), nameof(Rigidbody.rotation))),

            new CodeInstruction(OpCodes.Ret)
                .WithLabels(nullLabel),
        };
    }
}
