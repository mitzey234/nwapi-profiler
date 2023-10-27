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

public sealed class CustomProfilerPlugin
{
    public static readonly Harmony Harmony = new("me.zereth.customprofiler");
    public static readonly Harmony HarmonyOptimizations = new("me.zereth.customprofiler.optimizations");
    public const string Version = "1.0.0";

    internal static List<MethodBase> activeStacks = new();

    static bool optimized = false;

    static EventHandlers ev;

    public static bool upcapped = false;

    public static bool updateFields = false;

    [PluginConfig]
    public static Config PluginConfig;

    [PluginEntryPoint("CustomProfiler", Version, "A custom profiler for SCP:SL.", "Zereth")]
    [PluginPriority(LoadPriority.Highest)]
    public void Entry()
    {
        // try to apply some patches here and see what works.
        // if you get any errors you dont know how to fix, contact me.

        HarmonyOptimizations.PatchAll();
        optimized = true;

        ev = new EventHandlers();

        EventManager.RegisterEvents(ev);

        StaticUnityMethods.OnLateUpdate += onUpdate;
        SceneManager.sceneUnloaded += onSceneUnload;
    }

    public static void onSceneUnload (Scene scene)
    {
        if (scene.name != "Facility") return;

        //clean up InteractableColliders
        InteractableCollider.AllInstances.Clear();
        foreach (RespawnEffectsController c in RespawnEffectsController.AllControllers)
        {
            UnityEngine.Object.Destroy(c);
        }
        RespawnEffectsController.AllControllers.RemoveAll(x => x == null || x.netIdentity == null);

        //Clean up ItemPickupBases
        List<ItemPickupBase> array = UnityEngine.Object.FindObjectsOfType<ItemPickupBase>().Where(i => i != null).ToList();
        array.ForEach(i => i.PhysicsModuleSyncData.Clear());

        //Cleanup FirearmClientsideStateDatabase
        FirearmClientsideStateDatabase.AdsTracker.Clear();
        FirearmClientsideStateDatabase.PreReloadStatuses.Clear();
        FirearmClientsideStateDatabase.ReloadTimes.Clear();
        FirearmClientsideStateDatabase.ReloadTracker.Clear();
    }

    public static void disableOptimizations ()
    {
        StaticUnityMethods.OnLateUpdate -= onUpdate;
        SceneManager.sceneUnloaded -= onSceneUnload;
        Scp079Camera.AllInstances.ForEach(c => c.enabled = true);
        ReferenceHub.AllHubs.ForEach(p => {
            foreach (StatusEffectBase b in p.playerEffectsController.AllEffects)
            {
                b.enabled = true;
            }
        });

        if (optimized)
        {
            HarmonyOptimizations.UnpatchAll(HarmonyOptimizations.Id);
            optimized = false;
        }
    }

    public static void enableOptimizations ()
    {
        if (optimized)
            return;

        HarmonyOptimizations.PatchAll();
        StaticUnityMethods.OnLateUpdate += onUpdate;
        optimized = true;
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

        if (ProfileMethodPatch.DisableProfiler && allFields.Count > 0)
            allFields.Clear();
        upcount++;
        allFields.RemoveWhere(x => x == null);

        Application.targetFrameRate = upcapped ? 1000 : 60;

        if (Player.GetPlayers().Count(p => p.Role == RoleTypeId.Scp079) == 0) Scp079Camera.AllInstances.ForEach(c => c.enabled = false);
        else Scp079Camera.AllInstances.ForEach(c => c.enabled = true);
        ReferenceHub.AllHubs.ForEach(p => {

            /* this was part of PersonalRadioPlayback disabling, but it seems to break the game
            try
            {
                IVoiceRole voiceRole2 = p.roleManager.CurrentRole as IVoiceRole;
                if (ReferenceHub.HostHub != p && voiceRole2 != null && voiceRole2.VoiceModule is HumanVoiceModule)
                {
                    (voiceRole2.VoiceModule as HumanVoiceModule).RadioPlayback.Update();
                }
            } catch (Exception e)
            {
                Log.Error(e.Message + "\n" + e.StackTrace);
            } 
            */

            foreach (StatusEffectBase b in p.playerEffectsController.AllEffects)
            {
                b.Update();
                if (b is Hypothermia) (b as Hypothermia).SubEffects.ForEach(e => e.enabled = e.IsActive);
                if (b is CardiacArrest) (b as CardiacArrest).SubEffects.ForEach(e => e.enabled = e.IsActive);
                if (b is InsufficientLighting) (b as InsufficientLighting).AlwaysUpdate();
                if (b.IsEnabled && b.enabled == false)
                {
                    b.enabled = true;
                    //Log.Debug(p.nicknameSync.MyNick + " Enabled: " + b.name);
                }
                else if (!b.IsEnabled && b.enabled == true)
                {
                    b.enabled = false;
                    //Log.Debug(p.nicknameSync.MyNick + " Disabled: " + b.name);
                }
            }
        });

        if (GlobalChatIndicatorManager._singletonSet && GlobalChatIndicatorManager._singleton.enabled)
        {
            Log.Debug("Disabled global chat indicator");
            GlobalChatIndicatorManager._singleton.enabled = false;
        }

        activeStacks.Clear();
    }
}
