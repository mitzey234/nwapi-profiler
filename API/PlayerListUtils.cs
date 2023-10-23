namespace CustomProfiler.API;

using CustomProfiler.Patches.Optimizations;
using System.Collections.Generic;
using System.Linq;

public static class PlayerListUtils
{
    static PlayerListUtils()
    {
        AllHubs = new(FpcPositionDistributorPatch.PotentialMaxPlayers);
        VerifiedHubs = new(FpcPositionDistributorPatch.PotentialMaxPlayers);

        AllHubs.AddRange(ReferenceHub.AllHubs);
        VerifiedHubs.AddRange(ReferenceHub.AllHubs.Where(IsVerified));

        ReferenceHub.OnPlayerAdded += PlayerAdded;
        ReferenceHub.OnPlayerRemoved += PlayerRemoved;
        CharacterClassManager.OnInstanceModeChanged += InstanceModeChanged;
    }

    public static readonly List<ReferenceHub> AllHubs;
    public static readonly List<ReferenceHub> VerifiedHubs;

    private static void PlayerAdded(ReferenceHub hub)
    {
        AllHubs.Add(hub);
    }

    private static void PlayerRemoved(ReferenceHub hub)
    {
        AllHubs.Remove(hub);
        VerifiedHubs.Remove(hub);
    }

    private static void InstanceModeChanged(ReferenceHub hub, ClientInstanceMode mode)
    {
        if (mode == ClientInstanceMode.ReadyClient)
        {
            VerifiedHubs.Add(hub);
        }
        else
        {
            VerifiedHubs.Remove(hub);
        }
    }

    private static bool IsVerified(ReferenceHub hub) => hub.characterClassManager._targetInstanceMode == ClientInstanceMode.ReadyClient;
}
