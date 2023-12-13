namespace Optimizer;

using CustomPlayerEffects;
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
    public static readonly Harmony HarmonyOptimizations = new("me.zereth.customprofiler.optimizations");
    public const string Version = "1.0.0";

    [PluginConfig]
    public static Config PluginConfig;

    [PluginEntryPoint("Optimizations", Version, "Contains various optimizations for SCP:SL", "Zereth")]
    [PluginPriority(LoadPriority.Highest)]
    public void Entry()
    {
        // try to apply some patches here and see what works.
        // if you get any errors you dont know how to fix, contact me.

        HarmonyOptimizations.PatchAll();

        StaticUnityMethods.OnLateUpdate += onUpdate;
        SceneManager.sceneUnloaded += onSceneUnload;
    }

    public static void onSceneUnload(Scene scene)
    {
        if (scene.name != "Facility") return;

        //clean up InteractableColliders
        InteractableCollider.AllInstances.Clear();
        foreach (RespawnEffectsController c in RespawnEffectsController.AllControllers) UnityEngine.Object.Destroy(c);
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

    internal static float timer = 0;

    internal static int upcount = 0;

    public static bool upcapped = false;

    public static void onUpdate()
    {
        timer += Time.deltaTime;

        if (RoundRestart.IsRoundRestarting)
            return;

        if (timer <= 1.0f)
            return;
        timer = 0;

        upcount++;

        Application.targetFrameRate = upcapped ? 1000 : 60;

        if (Player.GetPlayers().Count(p => p.Role == RoleTypeId.Scp079) == 0) Scp079Camera.AllInstances.ForEach(c => c.enabled = false);
        else Scp079Camera.AllInstances.ForEach(c => c.enabled = true);
        ReferenceHub.AllHubs.ForEach(p => {
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
    }
}