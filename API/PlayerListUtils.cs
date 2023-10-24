namespace CustomProfiler.API;
using System.Collections.Generic;

public static class PlayerListUtils
{
    static PlayerListUtils()
    {
        AllHubs = new(
            );
        VerifiedHubs = new(MaxPlayers);

        foreach (ReferenceHub hub in ReferenceHub.AllHubs)
        {
            AllHubs.Add(hub);
            InstanceModeChanged(hub, hub.characterClassManager._targetInstanceMode);
        }

        ReferenceHub.OnPlayerAdded += PlayerAdded;
        ReferenceHub.OnPlayerRemoved += PlayerRemoved;
        CharacterClassManager.OnInstanceModeChanged += InstanceModeChanged;
    }

    public const int MaxPlayers = 500;

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
        if (IsVerified(hub))
        {
            VerifiedHubs.Add(hub);
        }
        else
        {
            VerifiedHubs.Remove(hub);
        }
    }

    private static bool IsVerified(ReferenceHub hub)
    {
        if (hub.characterClassManager._targetInstanceMode != ClientInstanceMode.ReadyClient)
            return false;

        if ((hub.characterClassManager.netIdentity.connectionToClient?.address ?? "localhost") == "localhost")
            return false;

        return true;
    }
}