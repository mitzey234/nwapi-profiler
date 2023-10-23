namespace CustomProfiler.API;

using Discord;
using MEC;
using PluginAPI.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

public static class PlayerListUtils
{
    static PlayerListUtils()
    {
        AllHubs = new(MaxPlayers);
        VerifiedHubs = new(MaxPlayers);
        Identifiers = new(MaxPlayers);
        PooledIdentifiers = new(MaxPlayers);
        Lock = new();

        foreach (ReferenceHub hub in ReferenceHub.AllHubs)
        {
            AllHubs.Add(hub);
            InstanceModeChanged(hub, hub.characterClassManager._targetInstanceMode);
        }

        ReferenceHub.OnPlayerAdded += PlayerAdded;
        ReferenceHub.OnPlayerRemoved += PlayerRemoved;
        CharacterClassManager.OnInstanceModeChanged += InstanceModeChanged;
    }

    public const int MaxPlayers = 200;

    public static readonly List<ReferenceHub> AllHubs;
    public static readonly List<ReferenceHub> VerifiedHubs;

    private static readonly Dictionary<ReferenceHub, int> Identifiers;
    private static readonly Queue<int> PooledIdentifiers;
    private static readonly object Lock;

    private static int nextPlayerId;

    public static int GetCustomPlayerId(this ReferenceHub hub)
    {
        if (!Identifiers.TryGetValue(hub, out int value))
            value = -1;

        return value;
    }

    private static void PlayerAdded(ReferenceHub hub)
    {
        AllHubs.Add(hub);
    }

    private static void PlayerRemoved(ReferenceHub hub)
    {
        ReturnId(hub);

        AllHubs.Remove(hub);
        VerifiedHubs.Remove(hub);
    }

    private static void InstanceModeChanged(ReferenceHub hub, ClientInstanceMode mode)
    {
        if (IsVerified(hub))
        {
            AssignNextId(hub);

            VerifiedHubs.Add(hub);
        }
        else
        {
            ReturnId(hub);

            VerifiedHubs.Remove(hub);
        }
    }

    private static bool IsVerified(ReferenceHub hub)
    {
        if (hub.characterClassManager._targetInstanceMode != ClientInstanceMode.ReadyClient)
            return false;

        return true;
    }

    private static void AssignNextId(ReferenceHub hub)
    {
        int result;

        if (Identifiers.ContainsKey(hub))
            return;

        lock (Lock)
        {
            result = PooledIdentifiers.Count > 0
                ? PooledIdentifiers.Dequeue()
                : Interlocked.Exchange(ref nextPlayerId, nextPlayerId + 1);

            Identifiers.Add(hub, result);
            Log.Info($"CUSTOM playerid created: {result}");
        }
    }

    private static void ReturnId(ReferenceHub hub)
    {
        lock (Lock)
        {
            if (!Identifiers.TryGetValue(hub, out int value))
                return;

            if (PooledIdentifiers.Count == MaxPlayers)
                throw new InvalidOperationException("Magic shit is happening that shouldn't be.");

            Identifiers.Remove(hub);
            PooledIdentifiers.Enqueue(value);
            Log.Info($"CUSTOM playerid was returned to the pool: {value}");
        }
    }
}
