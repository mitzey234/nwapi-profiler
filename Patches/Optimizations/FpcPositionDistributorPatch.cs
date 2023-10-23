namespace CustomProfiler.Patches.Optimizations;

using CustomProfiler.API;
using CustomProfiler.Extensions;
using HarmonyLib;
using InventorySystem;
using Mirror;
using PlayerRoles.FirstPersonControl;
using PlayerRoles.FirstPersonControl.NetworkMessages;
using PlayerRoles.Visibility;
using RelativePositioning;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using static HarmonyLib.AccessTools;

[HarmonyPatch(typeof(FpcServerPositionDistributor))]
public static class FpcPositionDistributorPatch
{
    public const int PotentialMaxPlayers = 110;

    private static readonly RelativePosition[] CachedRelativePositions = new RelativePosition[PotentialMaxPlayers];
    private static readonly bool[] IsCached = new bool[PotentialMaxPlayers];

    private static void ClearCachedRelativePositions()
    {
        for (int i = 0; i < PotentialMaxPlayers; i++)
        {
            CachedRelativePositions[i] = default;
            IsCached[i] = false;
        }
    }

    [HarmonyPrepare]
    private static void Init()
    {
        FpcServerPositionDistributor._bufferSyncData = new FpcSyncData[PotentialMaxPlayers];
        FpcServerPositionDistributor._bufferPlayerIDs = new int[PotentialMaxPlayers];

        Inventory.OnServerStarted += ClearCachedRelativePositions;
    }

    [HarmonyTranspiler]
    [HarmonyPatch(nameof(FpcServerPositionDistributor.EnsureArrayCapacity))]
    private static IEnumerable<CodeInstruction> EnsureArrayCapacity_Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator generator)
    {
        return new CodeInstruction[]
        {
            new(OpCodes.Ret),
        };
    }

    [HarmonyTranspiler]
    [HarmonyPatch(nameof(FpcServerPositionDistributor.WriteAll))]
    private static IEnumerable<CodeInstruction> WriteAll_Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator generator)
    {
        instructions.BeginTranspiler(out List<CodeInstruction> newInstructions);

        newInstructions.InsertRange(0, new CodeInstruction[]
        {
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldarg_1),
            new(OpCodes.Call, Method(typeof(FpcPositionDistributorPatch), nameof(CustomWriteAll))),
            new(OpCodes.Ret),
        });

        return newInstructions.FinishTranspiler();
    }

    [HarmonyTranspiler]
    [HarmonyPatch(nameof(FpcServerPositionDistributor.LateUpdate))]
    private static IEnumerable<CodeInstruction> LateUpdate_Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator generator)
    {
        instructions.BeginTranspiler(out List<CodeInstruction> newInstructions);

        newInstructions.InsertRange(0, new CodeInstruction[]
        {
            new(OpCodes.Call, Method(typeof(FpcPositionDistributorPatch), nameof(CustomLateUpdate))),
            new(OpCodes.Ret),
        });

        return newInstructions.FinishTranspiler();
    }

    public static void CustomLateUpdate()
    {
        if (!NetworkServer.active || !StaticUnityMethods.IsPlaying)
        {
            return;
        }

        FpcServerPositionDistributor._sendCooldown += Time.deltaTime;
        if (FpcServerPositionDistributor._sendCooldown < FpcServerPositionDistributor.SendRate)
        {
            return;
        }

        FpcServerPositionDistributor._sendCooldown -= FpcServerPositionDistributor.SendRate;

        for (int i = 0; i < PlayerListUtils.VerifiedHubs.Count; i++)
        {
            ReferenceHub hub = PlayerListUtils.VerifiedHubs[i];

            hub.connectionToClient.Send(new FpcPositionMessage(hub));
        }

        ClearCachedRelativePositions();
    }

    private static void CustomWriteAll(ReferenceHub receiver, NetworkWriter writer)
    {
        ushort totalSent = 0;
        bool canValidateVisibility;
        VisibilityController visibilityController;

        Span<int> bufferIds = FpcServerPositionDistributor._bufferPlayerIDs;
        Span<FpcSyncData> bufferSyncData = FpcServerPositionDistributor._bufferSyncData;

        if (receiver.roleManager.CurrentRole is ICustomVisibilityRole customVisibilityRole)
        {
            canValidateVisibility = true;
            visibilityController = customVisibilityRole.VisibilityController;
        }
        else
        {
            canValidateVisibility = false;
            visibilityController = null;
        }

        List<FpcStandardRoleBase> roles = GetRolesOfType.Get<FpcStandardRoleBase>();

        for (int i = 0; i < roles.Count; i++)
        {
            FpcStandardRoleBase fpcRole = roles[i];

            if (fpcRole.Pooled)
                continue;

            if (fpcRole._lastOwner.netId == receiver.netId)
                continue;

            if (!canValidateVisibility || visibilityController.ValidateVisibility(fpcRole._lastOwner))
            {
                bufferIds[totalSent] = (byte)fpcRole._lastOwner.PlayerId;

                bufferSyncData[totalSent++] =
                    new(default,
                    fpcRole.FpcModule.SyncMovementState,
                    fpcRole.FpcModule.IsGrounded,
                    GetCachedRelativePosition(fpcRole._lastOwner),
                    fpcRole.FpcModule.MouseLook);
            }
        }

        writer.WriteUShort(totalSent);

        for (int i = 0; i < totalSent;)
        {
            writer.WriteByte((byte)bufferIds[i]);
            bufferSyncData[i++].Write(writer);
        }
    }

    private static RelativePosition GetCachedRelativePosition(ReferenceHub hub)
    {
        ref bool isCached = ref IsCached[hub.PlayerId];

        if (isCached)
        {
            return CachedRelativePositions[hub.PlayerId];
        }

        isCached = true;
        return CachedRelativePositions[hub.PlayerId] = new(hub.transform.position);
    }
}
