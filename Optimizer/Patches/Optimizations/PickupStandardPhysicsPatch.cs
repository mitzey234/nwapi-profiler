namespace Optimizer.Patches.Optimizations;

using Optimizer.Extensions;
using HarmonyLib;
using InventorySystem.Items.Pickups;
using MEC;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using static HarmonyLib.AccessTools;
using static InventorySystem.Items.Pickups.PickupStandardPhysics;

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

    [HarmonyTranspiler]
    [HarmonyPatch(MethodType.Constructor, typeof(ItemPickupBase), typeof(FreezingMode))]
    private static IEnumerable<CodeInstruction> Transpiler2(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator generator)
    {
        instructions.BeginTranspiler(out List<CodeInstruction> newInstructions);

        newInstructions.InsertRange(newInstructions.Count - 1, new CodeInstruction[]
        {
            new(OpCodes.Ldarg_0),
            new(OpCodes.Call, Method(typeof(PickupStandardPhysicsPatch), nameof(OnCreated))),
        });

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

            if (!_this._serverPrevSleeping && ((Time.frameCount + _this._pickup.GetInstanceID()) % (Application.targetFrameRate) != 0))
            {
                return;
            }

            _this.ServerSendRpc(_this.ServerWriteRigidbody);
        }

        _this._serverPrevSleeping = isSleeping;
    }

    private static void OnCreated(PickupStandardPhysics _this)
    {
        Timing.RunCoroutine(SendRigidBody(_this));
    }

    private static IEnumerator<float> SendRigidBody(PickupStandardPhysics _this)
    {
        yield return Timing.WaitForOneFrame;
        _this.ServerSendRpc(_this.ServerWriteRigidbody);
    }
}
