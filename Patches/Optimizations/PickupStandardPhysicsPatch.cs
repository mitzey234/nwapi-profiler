﻿namespace CustomProfiler.Patches.Optimizations;

using CustomProfiler.Extensions;
using HarmonyLib;
using InventorySystem.Items.Pickups;
using Mirror;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using static InventorySystem.Items.Pickups.PickupStandardPhysics;
using static HarmonyLib.AccessTools;
using UnityEngine;

/// <summary>
/// This patch reduces pickup update rate from every 1/4 second to every 5 seconds.
/// </summary>
[HarmonyPatch(typeof(PickupStandardPhysics))]
public static class PickupStandardPhysicsPatch
{
    [HarmonyTranspiler]
    [HarmonyPatch("UpdateServer")]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator generator)
    {
        instructions.BeginTranspiler(out List<CodeInstruction> newInstructions);

        newInstructions.InsertRange(0, new CodeInstruction[]
        {
            new(OpCodes.Ldarg_0),
            new(OpCodes.Call, Method(typeof(PickupStandardPhysicsPatch), nameof(CustomUpdateServer))),
            new(OpCodes.Ret),
        });

        newInstructions.FindLast((CodeInstruction x) => x.opcode == OpCodes.Ldc_R8).operand = 5d;

        return newInstructions.FinishTranspiler();
    }

    private static void CustomUpdateServer(PickupStandardPhysics _this)
    {
        bool isSleeping = _this.Rb.IsSleeping() && _this._freezingMode != FreezingMode.NeverFreeze;

        if (isSleeping)
        {
            if (_this._serverPrevSleeping)
            {
                return;
            }

            _this._serverEverDecelerated = true;
            _this.ServerSetSyncData(_this.ServerWriteRigidbody);
        }
        else
        {
            float sqrMagnitude = _this.Rb.velocity.sqrMagnitude;
            if (sqrMagnitude < _this._serverPrevVelSqr)
            {
                _this._serverEverDecelerated = true;
            }

            _this._serverPrevVelSqr = sqrMagnitude;

            if (!_this._serverPrevSleeping && ((Time.frameCount + _this._pickup.GetInstanceID()) % (ServerStatic.ServerTickrate * 5) != 0))
            {
                return;
            }

            _this.ServerSendRpc(_this.ServerWriteRigidbody);
        }

        _this._serverPrevSleeping = isSleeping;
    }
}
