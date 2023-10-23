namespace CustomProfiler.API;
using PluginAPI.Core;
using System;
using System.Collections.Generic;
using System.Threading;

public static class PlayerListUtils
{
    static PlayerListUtils()
    {
        AllHubs = new(MaxPlayers);
        Identifiers = new(MaxPlayers);
        PooledIdentifiers = new(MaxPlayers);
        Lock = new();

        AllHubs.AddRange(ReferenceHub.AllHubs);

        ReferenceHub.OnPlayerAdded += PlayerAdded;
        ReferenceHub.OnPlayerRemoved += PlayerRemoved;
    }

    public const int MaxPlayers = 200;

    public static readonly List<ReferenceHub> AllHubs;

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
        AssignNextId(hub);

        AllHubs.Add(hub);
    }

    private static void PlayerRemoved(ReferenceHub hub)
    {
        ReturnId(hub);

        AllHubs.Remove(hub);
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
