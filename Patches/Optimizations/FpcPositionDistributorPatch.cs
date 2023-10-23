namespace CustomProfiler.Patches.Optimizations;

using CustomProfiler.API;
using CustomProfiler.Extensions;
using HarmonyLib;
using InventorySystem;
using Mirror;
using PlayerRoles;
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
    private static readonly RelativePosition[] CachedRelativePositions = new RelativePosition[PlayerListUtils.MaxPlayers];
    private static readonly bool[] IsCached = new bool[PlayerListUtils.MaxPlayers];

    private static void ClearCachedRelativePositions()
    {
        for (int i = 0; i < PlayerListUtils.MaxPlayers; i++)
        {
            IsCached[i] = false;
        }
    }

    [HarmonyPrepare]
    private static void Init()
    {
        FpcServerPositionDistributor._bufferSyncData = new FpcSyncData[PlayerListUtils.MaxPlayers];
        FpcServerPositionDistributor._bufferPlayerIDs = new int[PlayerListUtils.MaxPlayers];

        PlayerRoleManager.OnRoleChanged -= FpcServerPositionDistributor.ResetPlayer;
        Inventory.OnServerStarted -= FpcServerPositionDistributor.PreviouslySent.Clear;

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

        for (int i = 0; i < PlayerListUtils.AllHubs.Count; i++)
        {
            ReferenceHub hub = PlayerListUtils.AllHubs[i];

            if (hub.characterClassManager._targetInstanceMode != ClientInstanceMode.ReadyClient)
                continue;

            if (hub.isLocalPlayer)
                continue;

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
        int customPlayerId = hub.GetCustomPlayerId();

        ref bool isCached = ref IsCached[customPlayerId];

        if (isCached)
        {
            return CachedRelativePositions[customPlayerId];
        }

        isCached = true;
        return CachedRelativePositions[customPlayerId] = new(hub.transform.position);
    }
}
