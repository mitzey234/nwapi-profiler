namespace CustomProfiler.Patches.Optimizations;

using CustomProfiler.Extensions;
using HarmonyLib;
using Mirror;
using PluginAPI.Core;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using static HarmonyLib.AccessTools;

[HarmonyPatch(typeof(BreakableWindow))]
public static class BreakableWindowPatch
{
    [HarmonyTranspiler]
    [HarmonyPatch(nameof(BreakableWindow.Update))]
    private static IEnumerable<CodeInstruction> Update_Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator generator)
    {
        Label networkServerActive = generator.DefineLabel();
        LocalBuilder status = generator.DeclareLocal(typeof(BreakableWindow.BreakableWindowStatus));

        return new CodeInstruction[]
        {
            // if (!NetworkServer.active)
            //     return;
            new(OpCodes.Call, PropertyGetter(typeof(NetworkServer), nameof(NetworkServer.active))),
            new(OpCodes.Brtrue_S, networkServerActive),
            new(OpCodes.Ret),

            // this.enabled = false;
            new CodeInstruction(OpCodes.Ldarg_0)
                .WithLabels(networkServerActive),
            new(OpCodes.Ldc_I4_0),
            new(OpCodes.Call, PropertySetter(typeof(Behaviour), nameof(Behaviour.enabled))),

            // BreakableWindow.BreakableWindowStatus status = default;
            new(OpCodes.Ldloca_S, status),
            new(OpCodes.Initobj, typeof(BreakableWindow.BreakableWindowStatus)),

            // status.position = this._transform.position;
            new(OpCodes.Ldloca_S, status),
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldfld, Field(typeof(BreakableWindow), nameof(BreakableWindow._transform))),
            new(OpCodes.Callvirt, PropertyGetter(typeof(Transform), nameof(Transform.position))),
            new(OpCodes.Stfld, Field(typeof(BreakableWindow.BreakableWindowStatus), nameof(BreakableWindow.BreakableWindowStatus.position))),

            // status.rotation = this._transform.rotation;
            new(OpCodes.Ldloca_S, status),
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldfld, Field(typeof(BreakableWindow), nameof(BreakableWindow._transform))),
            new(OpCodes.Callvirt, PropertyGetter(typeof(Transform), nameof(Transform.rotation))),
            new(OpCodes.Stfld, Field(typeof(BreakableWindow.BreakableWindowStatus), nameof(BreakableWindow.BreakableWindowStatus.rotation))),

            // status.broken = this.isBroken;
            new(OpCodes.Ldloca_S, status),
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldfld, Field(typeof(BreakableWindow), nameof(BreakableWindow.isBroken))),
            new(OpCodes.Stfld, Field(typeof(BreakableWindow.BreakableWindowStatus), nameof(BreakableWindow.BreakableWindowStatus.broken))),

            // this.NetworksyncStatus = status;
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldloc_S, status),
            new(OpCodes.Call, PropertySetter(typeof(BreakableWindow), nameof(BreakableWindow.NetworksyncStatus))),

            new(OpCodes.Ret),
        };
    }

    [HarmonyTranspiler]
    [HarmonyPatch(nameof(BreakableWindow.LateUpdate))]
    private static IEnumerable<CodeInstruction> LateUpdate_Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator generator)
    {
        return new CodeInstruction[]
        {
            new(OpCodes.Ret),
        };
    }

    [HarmonyTranspiler]
    [HarmonyPatch(nameof(BreakableWindow.ServerDamageWindow))]
    private static IEnumerable<CodeInstruction> ServerDamageWindow_Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator generator)
    {
        Label networkServerActive = generator.DefineLabel();
        Label shouldBreak = generator.DefineLabel();

        LocalBuilder status = generator.DeclareLocal(typeof(BreakableWindow.BreakableWindowStatus));

        //
        // if (!NetworkServer.active)
        //     return;
        //
        // this.health -= damage;
        //
        // if (this.health > 0f)
        //     return;
        //
        // this.isBroken = true;
        // 
        // BreakableWindow.BreakableWindowStatus status = this.syncStatus;
        // status.broken = true;
        // this.NetworksyncStatus = status;
        //
        return new CodeInstruction[]
        {
            // if (!NetworkServer.active)
            //     return;
            new(OpCodes.Call, PropertyGetter(typeof(NetworkServer), nameof(NetworkServer.active))),
            new(OpCodes.Brtrue_S, networkServerActive),
            new(OpCodes.Ret),

            // this.health -= damage;
            new CodeInstruction(OpCodes.Ldarg_0)
                .WithLabels(networkServerActive),
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldfld, Field(typeof(BreakableWindow), nameof(BreakableWindow.health))),
            new(OpCodes.Ldarg_1),
            new(OpCodes.Sub),
            new(OpCodes.Stfld, Field(typeof(BreakableWindow), nameof(BreakableWindow.health))),

            // if (this.health > 0f)
            //     return;
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldfld, Field(typeof(BreakableWindow), nameof(BreakableWindow.health))),
            new(OpCodes.Ldc_R4, 0f),
            new(OpCodes.Ble_Un_S, shouldBreak),
            new(OpCodes.Ret),

            // this.isBroken = true;
            new CodeInstruction(OpCodes.Ldarg_0)
                .WithLabels(shouldBreak),
            new(OpCodes.Ldc_I4_1),
            new(OpCodes.Stfld, Field(typeof(BreakableWindow), nameof(BreakableWindow.isBroken))),

            // BreakableWindow.BreakableWindowStatus status = this.syncStatus;
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldfld, Field(typeof(BreakableWindow), nameof(BreakableWindow.syncStatus))),
            new(OpCodes.Stloc_S, status),

            // status.broken = true;
            new(OpCodes.Ldloca_S, status),
            new(OpCodes.Ldc_I4_1),
            new(OpCodes.Stfld, Field(typeof(BreakableWindow.BreakableWindowStatus), nameof(BreakableWindow.BreakableWindowStatus.broken))),

            // this.NetworksyncStatus = status;
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldloc_S, status),
            new(OpCodes.Call, PropertySetter(typeof(BreakableWindow), nameof(BreakableWindow.NetworksyncStatus))),
            new(OpCodes.Ret),
        };
    }


    [HarmonyTranspiler]
    [HarmonyPatch(nameof(BreakableWindow.NetworksyncStatus), MethodType.Setter)]
    private static IEnumerable<CodeInstruction> NetworksyncStatus_Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator generator)
    {
        instructions.BeginTranspiler(out List<CodeInstruction> newInstructions);

        Label notBroken = generator.DefineLabel();

        newInstructions[0].labels.Add(notBroken);

        newInstructions.InsertRange(0, new CodeInstruction[]
        {
            // if (value.broken)
            //     BreakableWindowPatch.OnBroken(this);
            new(OpCodes.Ldarga_S, 1),
            new(OpCodes.Ldfld, Field(typeof(BreakableWindow.BreakableWindowStatus), nameof(BreakableWindow.BreakableWindowStatus.broken))),
            new(OpCodes.Brfalse_S, notBroken),
            new(OpCodes.Ldarg_0),
            new(OpCodes.Call, Method(typeof(BreakableWindowPatch), nameof(OnBroken))),
        });

        return newInstructions.FinishTranspiler();
    }

    private static void OnBroken(BreakableWindow _this)
    {
        for (int i = 0; i < _this.meshRenderers.Count; i++)
        {
            _this.meshRenderers[i].gameObject.layer = 28;
            Object.Destroy(_this.meshRenderers[i]);
        }

        _this.meshRenderers.Clear();
    }
}
