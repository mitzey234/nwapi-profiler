namespace CustomProfiler;

using CustomPlayerEffects;
using CustomProfiler.API;
using CustomProfiler.Extensions;
using CustomProfiler.Patches;
using HarmonyLib;
using Interactables;
using InventorySystem.Items.Firearms.BasicMessages;
using InventorySystem.Items.Pickups;
using InventorySystem.Items.Usables.Scp244.Hypothermia;
using Mirror;
using PlayerRoles;
using PlayerRoles.FirstPersonControl;
using PlayerRoles.FirstPersonControl.NetworkMessages;
using PlayerRoles.PlayableScps.Scp079.Cameras;
using PluginAPI.Core;
using PluginAPI.Core.Attributes;
using PluginAPI.Enums;
using PluginAPI.Events;
using Respawning;
using RoundRestarting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using Utils;
using Utils.NonAllocLINQ;
using VoiceChat;

public sealed class Plugin
{
    public static readonly Harmony Harmony = new("me.zereth.customprofiler");
    public const string Version = "1.0.0";

    internal static List<MethodBase> activeStacks = new();

    static EventHandlers ev;

    public static bool updateFields = false;

    [PluginConfig]
    public static Config PluginConfig;

    [PluginEntryPoint("CustomProfiler", Version, "A custom profiler for SCP:SL.", "Zereth")]
    [PluginPriority(LoadPriority.Highest)]
    public void Entry()
    {
        // try to apply some patches here and see what works.
        // if you get any errors you dont know how to fix, contact me.

        ev = new EventHandlers();

        EventManager.RegisterEvents(ev);

        StaticUnityMethods.OnLateUpdate += onUpdate;
    }

    internal static void reset()
    {
        foreach (AsRef<ProfileMethodPatch.ProfiledMethodInfo> asRefInfo in ProfileMethodPatch.GetProfilerInfos())
        {
            ref ProfileMethodPatch.ProfiledMethodInfo info = ref asRefInfo.Value;

            info.InvocationCount = 0;
            info.TotalTicks = 0;
            info.MaxTicks = 0;
            info.TotalMemory = 0;
        }
    }

    internal static void disableProfiler ()
    {
        ProfileMethodPatch.DisableProfiler = true;

        //MethodMetrics.methodMetrics.Clear(); //Sometimes I wanna keep the data, lets reset it when enabling
    }

    public static int patched = 0;

    public static HashSet<FieldInfo> allFields = new();

    internal static void enableProfiler()
    {
        if (patched > 0)
        {
            ProfileMethodPatch.DisableProfiler = false;
            return;
        }

        Type[] types = typeof(GameCore.Console).Assembly.GetTypes().IncludingNestedTypes().ToArray();

        // use hashset so we dont
        // try to patch the same method twice.
        HashSet<MethodBase> methods = new();


        int failed = 0;

        foreach (Type t in types)
        {
            foreach (MethodInfo m in t.GetFullyConstructedMethods(includeNonPublic: true))
            {
                if (!m.AllowsProfiling())
                    continue;

                methods.Add(m);
            }
        }

        methods.Add(typeof(PickupStandardPhysics).GetMethod("ServerWriteRigidbody", BindingFlags.Instance | BindingFlags.NonPublic, null, CallingConventions.Any, new Type[] { typeof(NetworkWriter) }, null));
        methods.Add(typeof(FpcOverrideMessage).GetMethod("Write", BindingFlags.Instance | BindingFlags.Public, null, CallingConventions.Any, new Type[] { typeof(NetworkWriter) }, null));
        methods.Add(typeof(FpcServerPositionDistributor).GetMethod("GetNewSyncData", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static, null, CallingConventions.Any, new Type[] { typeof(ReferenceHub), typeof(ReferenceHub), typeof(FirstPersonMovementModule), typeof(bool) }, null));
        methods.Add(typeof(ExplosionUtils).GetMethod("ServerSpawnEffect", BindingFlags.Instance | BindingFlags.Public | BindingFlags.Static, null, CallingConventions.Any, new Type[] { typeof(Vector3), typeof(ItemType) }, null));

        Log.Info("Patching " + methods.Count + " methods");

        foreach (MethodBase m in methods)
        {
            try
            {
                ProfileMethodPatch.ApplyProfiler(m);
            }
            catch (Exception e)
            {
                failed++;
                Log.Error($"{m.DeclaringType?.FullName ?? "null"}::{m.Name} => " + e.ToString());
            }

            patched++;
        }

        Log.Info("Failed to patch " + failed + " methods");
    }

    internal static float timer = 0;

    internal static int upcount = 0;

    public static void onUpdate()
    {
        timer += Time.deltaTime;

        if (RoundRestart.IsRoundRestarting)
            return;

        if (timer <= 1.0f)
            return;
        timer = 0;

        if (ProfileMethodPatch.DisableProfiler && allFields.Count > 0) allFields.Clear();

        upcount++;
        allFields.RemoveWhere(x => x == null);

        activeStacks.Clear();
    }
}
