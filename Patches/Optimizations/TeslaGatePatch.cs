﻿namespace CustomProfiler.Patches.Optimizations;

using HarmonyLib;
using Mirror;
using PlayerRoles;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using static HarmonyLib.AccessTools;

[HarmonyPatch]
public static class TeslaGatePatch
{
    [HarmonyTranspiler]
    [HarmonyPatch(typeof(TeslaGate), "IsInIdleRange", typeof(Vector3))]
    private static IEnumerable<CodeInstruction> IsInIdleRange_Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator generator)
    {
        LocalBuilder tempAddr = generator.DeclareLocal(typeof(Vector3));

        //
        // return (this.Position - position).sqrMagnitude < (this.distanceToIdle * this.distanceToIdle);
        //
        return new CodeInstruction[]
        {
            new(OpCodes.Ldarg_0),
            new(OpCodes.Callvirt, PropertyGetter(typeof(TeslaGate), nameof(TeslaGate.Position))),
            new(OpCodes.Ldarg_1),
            new(OpCodes.Call, Method(typeof(Vector3), "op_Subtraction")),
            new(OpCodes.Stloc_S, tempAddr),
            new(OpCodes.Ldloca_S, tempAddr),
            new(OpCodes.Call, PropertyGetter(typeof(Vector3), nameof(Vector3.sqrMagnitude))),
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldfld, Field(typeof(TeslaGate), nameof(TeslaGate.distanceToIdle))),
            new(OpCodes.Dup),
            new(OpCodes.Mul),
            new(OpCodes.Clt),
            new(OpCodes.Ret),
        };
    }

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(TeslaGate), "InRange", typeof(Vector3))]
    private static IEnumerable<CodeInstruction> InRange_Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator generator)
    {
        LocalBuilder tempAddr = generator.DeclareLocal(typeof(Vector3));

        //
        // return (this.Position - position).sqrMagnitude < (this.sizeOfTrigger * this.sizeOfTrigger);
        //
        return new CodeInstruction[]
        {
            new(OpCodes.Ldarg_0),
            new(OpCodes.Callvirt, PropertyGetter(typeof(TeslaGate), nameof(TeslaGate.Position))),
            new(OpCodes.Ldarg_1),
            new(OpCodes.Call, Method(typeof(Vector3), "op_Subtraction")),
            new(OpCodes.Stloc_S, tempAddr),
            new(OpCodes.Ldloca_S, tempAddr),
            new(OpCodes.Call, PropertyGetter(typeof(Vector3), nameof(Vector3.sqrMagnitude))),
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldfld, Field(typeof(TeslaGate), nameof(TeslaGate.sizeOfTrigger))),
            new(OpCodes.Dup),
            new(OpCodes.Mul),
            new(OpCodes.Clt),
            new(OpCodes.Ret),
        };
    }

    // Doing a room calculation is actually more intense than just checking the distance.
    [HarmonyPatch(typeof(TeslaGateController), "FixedUpdate")]
    private static class TeslaGateControllerPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator generator)
        {
            return new CodeInstruction[]
            {
                // FixedUpdatePatch.FixedUpdate(this);
                // return;
                new(OpCodes.Ldarg_0), // this
                new(OpCodes.Call, Method(typeof(TeslaGateControllerPatch), nameof(FixedUpdate))),
                new(OpCodes.Ret), // return;
            };
        }

        public static void FixedUpdate(TeslaGateController _this)
        {
            if (!NetworkServer.active)
                return;

            var aliveHubs = ReferenceHub.AllHubs.Where(PlayerRolesUtils.IsAlive).ToArray();

            for (int i = 0; i < _this.TeslaGates.Count; i++)
            {
                TeslaGate teslaGate = _this.TeslaGates[i];

                if (!teslaGate.isActiveAndEnabled)
                    continue;

                if (teslaGate.InactiveTime > 0f)
                {
                    teslaGate.NetworkInactiveTime = Mathf.Max(0f, teslaGate.InactiveTime - Time.fixedDeltaTime);
                    continue;
                }

                bool isIdling;
                bool isTriggered;

                if (teslaGate.InProgress)
                {
                    ProcessInProgress(aliveHubs, teslaGate, out isIdling, out isTriggered);
                }
                else
                {
                    ProcessNotInProgress(aliveHubs, teslaGate, out isIdling, out isTriggered);
                }

                if (isTriggered)
                    teslaGate.ServerSideCode();

                if (isIdling != teslaGate.isIdling)
                    teslaGate.ServerSideIdle(isIdling);
            }
        }

        private static void ProcessInProgress(ReferenceHub[] hubs, TeslaGate teslaGate, out bool isIdling, out bool isTriggered)
        {
            isIdling = false;
            isTriggered = false;

            for (int h = 0; h < hubs.Length; h++)
            {
                if (teslaGate.IsInIdleRange(hubs[h].transform.position))
                {
                    isIdling = true;
                    return;
                }
            }
        }

        private static void ProcessNotInProgress(ReferenceHub[] hubs, TeslaGate teslaGate, out bool isIdling, out bool isTriggered)
        {
            isIdling = false;
            isTriggered = false;

            int h;
            for (h = 0; h < hubs.Length; h++)
            {
                if (teslaGate.IsInIdleRange(hubs[h].transform.position))
                {
                    isIdling = true;
                    goto ProcessIdling;
                }

                if (teslaGate.InRange(hubs[h].transform.position))
                {
                    isTriggered = true;
                    goto ProcessTriggered;
                }
            }

            ProcessTriggered:
            for (; h < hubs.Length; h++)
            {
                if (teslaGate.IsInIdleRange(hubs[h].transform.position))
                {
                    isIdling = true;
                    return;
                }
            }
            return;

            ProcessIdling:
            for (; h < hubs.Length; h++)
            {
                if (teslaGate.InRange(hubs[h].transform.position))
                {
                    isTriggered = true;
                    return;
                }
            }
        }
    }
}
