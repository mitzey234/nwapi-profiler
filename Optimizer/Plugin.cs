using Interactables.Interobjects;
using Interactables.Interobjects.DoorUtils;
using Optimizer.Patches.Optimizations;
using CustomPlayerEffects;
using HarmonyLib;
using Interactables;
using InventorySystem.Items.Pickups;
using InventorySystem.Items.Usables.Scp244.Hypothermia;
using PlayerRoles;
using PlayerRoles.PlayableScps.Scp079.Cameras;
using PluginAPI.Core;
using PluginAPI.Core.Attributes;
using PluginAPI.Enums;
using Respawning;
using RoundRestarting;
using System.Collections.Generic;
using System.Linq;
using Optimizer.Patches;
using UnityEngine;
using UnityEngine.SceneManagement;
using Utils.NonAllocLINQ;
using VoiceChat;

namespace Optimizer;


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

        StaticUnityMethods.OnFixedUpdate += onUpdate;
        SceneManager.sceneUnloaded += onSceneUnload;
    }

    public static void onSceneUnload(Scene scene)
    {
        if (scene.name != "Facility") return;
        
        //Clean up variables
        DoorVariantPatch.doortimes.Clear();
        DoorVariantPatch.ignore.Clear();
        DoorVariantPatch.subIgnore.Clear();
        BreakableWindowPatch.times.Clear();

        //clean up InteractableColliders
        InteractableCollider.AllInstances.Clear();
        foreach (RespawnEffectsController c in RespawnEffectsController.AllControllers) UnityEngine.Object.Destroy(c);
        RespawnEffectsController.AllControllers.RemoveAll(x => x == null || x.netIdentity == null);

        //Clean up ItemPickupBases
        List<ItemPickupBase> array = UnityEngine.Object.FindObjectsOfType<ItemPickupBase>().Where(i => i != null).ToList();
        array.ForEach(i => i.PhysicsModuleSyncData.Clear());
    }

    internal static float timer = 0;

    internal static int upcount = 0;

    public static bool upcapped = false;

    public static void onUpdate()
    {
        if (RoundRestart.IsRoundRestarting) return;
        
        timer += Time.deltaTime;

        if (timer <= 1.0f) return;
        timer = 0;

        upcount++;

        if (Player.GetPlayers().Count(p => p.Role == RoleTypeId.Scp079) == 0) Scp079Camera.AllInstances.ForEach(c => c.enabled = false);
        else Scp079Camera.AllInstances.ForEach(c => c.enabled = true);
        
        ReferenceHub.AllHubs.ForEach(p =>
        {
            if (p.isLocalPlayer) return;
            
            foreach (StatusEffectBase b in p.playerEffectsController.AllEffects)
            {
                b.Update();
                if (b is Hypothermia hypothermia)
                {
                    foreach (HypothermiaSubEffectBase subEffect in hypothermia.SubEffects)
                    {
                        subEffect.enabled = subEffect.IsActive;
                    }
                }
                else if (b is CardiacArrest cardiacArrest)
                {
                    foreach (SubEffectBase subEffect in cardiacArrest.SubEffects)
                    {
                        subEffect.enabled = subEffect.IsActive;
                    }
                }
                else if (b is InsufficientLighting insufficientLighting)
                {
                    insufficientLighting.AlwaysUpdate();
                }

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
        
        foreach (var genPair in BasicStuff.GeneratorControllerOptimizer.instances.ToList())
        {
            genPair.Key.Update();
        }
    }

    public static void StartDoor(DoorVariant door, List<DoorVariant> ignore = null)
    {
        if (ignore == null) ignore = new List<DoorVariant>();
        if (door is CheckpointDoor checkpointDoor) checkpointDoor.SubDoors.Where(d => !ignore.Contains(d)).ToList().ForEach(d => StartDoor(door, checkpointDoor.SubDoors.Concat(ignore).ToList()));
        DoorVariantPatch.doortimes[door] = 7.0f;
        door._existenceCooldown = 0;
        door.SetColliders();
        door.enabled = true;
    }
}