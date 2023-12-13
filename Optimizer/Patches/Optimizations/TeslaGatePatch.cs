namespace Optimizer.Patches.Optimizations;

using HarmonyLib;
using Mirror;
using NorthwoodLib.Pools;
using PlayerRoles;
using PlayerRoles.FirstPersonControl;
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

            var aliveHubs = ListPool<ReferenceHub>.Shared.Rent(ReferenceHub.AllHubs.Where(PlayerRolesUtils.IsAlive));

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

                bool isIdling = true;
                bool isTriggered = false;

                if (!teslaGate.InProgress)
                {
                    ProcessNotInProgress(aliveHubs, teslaGate, out isIdling, out isTriggered);
                }

                if (isTriggered)
                    teslaGate.ServerSideCode();

                if (isIdling != teslaGate.isIdling)
                    teslaGate.ServerSideIdle(isIdling);
            }

            ListPool<ReferenceHub>.Shared.Return(aliveHubs);
        }

        private static void ProcessNotInProgress(List<ReferenceHub> hubs, TeslaGate teslaGate, out bool isIdling, out bool isTriggered)
        {
            isIdling = false;
            isTriggered = false;

            int h;
            for (h = 0; h < hubs.Count; h++)
            {
                //You have to use player ref hub, otherwise you ignore basegame interfaces such as the one for SCP 106
                if (teslaGate.IsInIdleRange(hubs[h]))
                {
                    isIdling = true;
                    goto ProcessIdling;
                }

                if (teslaGate.PlayerInRange(hubs[h]))
                {
                    isTriggered = true;
                    isIdling = true;
                    return;
                }
            }

            ProcessIdling:
            for (; h < hubs.Count; h++)
            {
                if (teslaGate.PlayerInRange(hubs[h]))
                {
                    isTriggered = true;
                    return;
                }
            }
        }
    }
}
