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
using static HarmonyLib.AccessTools;

[HarmonyPatch(typeof(FpcServerPositionDistributor))]
public static class FpcPositionDistributorPatch
{
    public const int PotentialMaxPlayers = 110;

    private static readonly FpcSyncData[] PreviouslySent = new FpcSyncData[PotentialMaxPlayers * PotentialMaxPlayers];
    private static readonly RelativePosition[] CachedRelativePositions = new RelativePosition[PotentialMaxPlayers];
    private static readonly bool[] IsCached = new bool[PotentialMaxPlayers];

    private static void ClearPreviouslySent()
    {
        int i;

        for (i = 0; i < PotentialMaxPlayers * PotentialMaxPlayers;)
        {
            PreviouslySent[i++] = default;
        }

        for (i = 0; i < PotentialMaxPlayers;)
        {
            CachedRelativePositions[i] = default;
            IsCached[i++] = false;
        }
    }

    private static void ClearCachedRelativePositions()
    {
        for (int i = 0; i < PotentialMaxPlayers; i++)
        {
            CachedRelativePositions[i] = default;
            IsCached[i] = false;
        }
    }

    private static void OnRoleChanged(ReferenceHub hub, PlayerRoleBase oldRole, PlayerRoleBase newRole)
    {
        if (oldRole is not IFpcRole)
            return;

        for (int i = (hub.PlayerId - 1) * PotentialMaxPlayers; i < PotentialMaxPlayers; i++)
        {
            PreviouslySent[i] = default;
        }
    }

    private static void OnPlayerAdded(ReferenceHub hub)
    {
        for (int i = (hub.PlayerId - 1) * PotentialMaxPlayers; i < PotentialMaxPlayers; i++)
        {
            PreviouslySent[i] = default;
        }

        for (int i = hub.PlayerId - 1; i < (PotentialMaxPlayers * PotentialMaxPlayers); i += PotentialMaxPlayers)
        {
            PreviouslySent[i] = default;
        }
    }

    private static void OnPlayerRemoved(ReferenceHub hub)
    {
        for (int i = (hub.PlayerId - 1) * PotentialMaxPlayers; i < PotentialMaxPlayers; i++)
        {
            PreviouslySent[i] = default;
        }

        for (int i = hub.PlayerId - 1; i < (PotentialMaxPlayers * PotentialMaxPlayers); i += PotentialMaxPlayers)
        {
            PreviouslySent[i] = default;
        }
    }

    [HarmonyPrepare]
    private static void Init()
    {
        FpcServerPositionDistributor._bufferSyncData = null;//new FpcSyncData[PotentialMaxPlayers];
        FpcServerPositionDistributor._bufferPlayerIDs = null;//new int[PotentialMaxPlayers];

        ReferenceHub.OnPlayerAdded += OnPlayerAdded;
        ReferenceHub.OnPlayerRemoved += OnPlayerRemoved;
        PlayerRoleManager.OnRoleChanged += OnRoleChanged;
        Inventory.OnServerStarted += ClearPreviouslySent;
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
        return new CodeInstruction[]
        {
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldarg_1),
            new(OpCodes.Call, Method(typeof(FpcPositionDistributorPatch), nameof(CustomWriteAll))),
            new(OpCodes.Ret),
        };
    }

    [HarmonyTranspiler]
    [HarmonyPatch(nameof(FpcServerPositionDistributor.LateUpdate))]
    private static IEnumerable<CodeInstruction> LateUpdate_Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator generator)
    {
        instructions.BeginTranspiler(out List<CodeInstruction> newInstructions);

        newInstructions.InsertRange(newInstructions.Count - 1, new CodeInstruction[]
        {
            new CodeInstruction(OpCodes.Call, Method(typeof(FpcPositionDistributorPatch), nameof(ClearCachedRelativePositions)))
                .MoveLabelsFrom(newInstructions[newInstructions.Count - 1]),
        });

        return newInstructions.FinishTranspiler();
    }

    private static void CustomWriteAll(ReferenceHub receiver, NetworkWriter writer)
    {
        ushort totalSent = 0;
        bool canValidateVisibility;
        VisibilityController visibilityController;

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

        Span<byte> _bufferPlayerIDs = stackalloc byte[roles.Count];
        Span<FpcSyncData> _bufferSyncData = stackalloc FpcSyncData[roles.Count];

        for (int i = 0; i < roles.Count; i++)
        {
            FpcStandardRoleBase fpcRole = roles[i];

            if (fpcRole.Pooled)
                continue;

            if (fpcRole._lastOwner.netId == receiver.netId)
                continue;

            bool invisible = canValidateVisibility && !visibilityController.ValidateVisibility(fpcRole._lastOwner);

            if (!invisible)
            {
                _bufferPlayerIDs[totalSent] = (byte)fpcRole._lastOwner.PlayerId;
                _bufferSyncData[totalSent++] = GetNewSyncData(receiver, fpcRole._lastOwner, fpcRole.FpcModule, invisible);
            }
        }

        writer.WriteUShort(totalSent);

        for (int i = 0; i < totalSent;)
        {
            writer.WriteByte(_bufferPlayerIDs[i]);
            _bufferSyncData[i++].Write(writer);
        }
    }

    private static FpcSyncData GetNewSyncData(ReferenceHub receiver, ReferenceHub target, FirstPersonMovementModule fpmm, bool isInvisible)
    {
        ref FpcSyncData prevSyncData = ref GetPrevSyncData(receiver, target);

        return prevSyncData = isInvisible ? default : new(prevSyncData, fpmm.SyncMovementState, fpmm.IsGrounded, GetCachedRelativePosition(target), fpmm.MouseLook);
    }

    private static ref FpcSyncData GetPrevSyncData(ReferenceHub receiver, ReferenceHub target)
    {
        return ref PreviouslySent[((receiver.PlayerId - 1) * PotentialMaxPlayers) + (target.PlayerId - 1)];
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
