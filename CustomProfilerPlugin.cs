namespace CustomProfiler;

using CustomPlayerEffects;
using CustomProfiler.Extensions;
using CustomProfiler.Metrics;
using CustomProfiler.Patches;
using HarmonyLib;
using Interactables;
using Interactables.Interobjects.DoorUtils;
using InventorySystem.Items.Armor;
using InventorySystem.Items.Firearms;
using InventorySystem.Items.Pickups;
using InventorySystem.Items.Usables.Scp244.Hypothermia;
using Mirror;
using PlayerRoles;
using PlayerRoles.FirstPersonControl;
using PlayerRoles.FirstPersonControl.NetworkMessages;
using PlayerRoles.PlayableScps;
using PlayerRoles.PlayableScps.Scp049.Zombies;
using PlayerRoles.PlayableScps.Scp079.Cameras;
using PlayerRoles.PlayableScps.Scp939;
using PluginAPI.Core;
using PluginAPI.Core.Attributes;
using PluginAPI.Enums;
using PluginAPI.Events;
using Respawning;
using RoundRestarting;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
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
        MethodMetrics.methodMetrics.Clear();
    }

    internal static void disableProfiler ()
    {
        ProfileMethodPatch.DisableProfiler = true;

        MethodMetrics.methodMetrics.Clear();
    }

    public static int patched = 0;

    public static HashSet<FieldInfo> allFields = new();

    public static void updatecollections ()
    {
        //If you want to go back to JUST the game assembly
        //HashSet<Type> types = typeof(GameCore.Console).Assembly.GetTypes().ToHashSet();
        //List<Type> types2 = typeof(Harmony).Assembly.GetTypes().ToList();
        //foreach (Type t in types2) if (!types.Contains(t)) types.Add(t);

        HashSet<Type> types = new();
        AppDomain.CurrentDomain.GetAssemblies().ForEach(a => a.GetTypes().ToList().ForEach(t => { if (!types.Contains(t)) types.Add(t); }));

        HashSet<Type> toSearch = new()
        {
            typeof(List<>),
            typeof(HashSet<>),
            typeof(Dictionary<,>),
            typeof(ArrayList),
            typeof(BitArray),
            typeof(Stack<>),
            typeof(Array)
        };

        Dictionary<Type, List<Type>> overrideTypes = new()
        {
            { typeof(ScpAttackAbilityBase<>), new List<Type> { typeof(Scp939Role), typeof(ZombieRole) } }
        };

        foreach (Type type in types)
        {
            if (type.IsGenericType && overrideTypes.ContainsKey(type))
            {
                foreach (Type t in overrideTypes[type])
                {
                    Type secondaryType = type.MakeGenericType(t);
                    foreach (FieldInfo field in secondaryType.GetFields(BindingFlags.DeclaredOnly | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static))
                    {
                        if (field.DeclaringType.GetCustomAttribute(typeof(CompilerGeneratedAttribute)) != null) continue;
                        Type fieldType = field.FieldType;
                        if (!fieldType.IsGenericType) continue;
                        if (!toSearch.Contains(fieldType.GetGenericTypeDefinition())) continue;
                        allFields.Add(field);
                    }   
                }
            }
            else if (type.IsGenericType) continue;
            foreach (FieldInfo field in type.GetFields(BindingFlags.DeclaredOnly | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static))
            {
                if (field.DeclaringType.GetCustomAttribute(typeof(CompilerGeneratedAttribute)) != null) continue;
                Type fieldType = field.FieldType;
                if (!fieldType.IsGenericType) continue;
                if (!toSearch.Contains(fieldType.GetGenericTypeDefinition())) continue;

                allFields.Add(field);
            }
        }
    }

    internal static void enableProfiler()
    {
        if (patched > 0)
        {
            ProfileMethodPatch.DisableProfiler = false;
            return;
        }

        Type[] types = typeof(GameCore.Console).Assembly.GetTypes();

        // use hashset so we dont
        // try to patch the same method twice.
        HashSet<MethodBase> methods = new();

        int failed = 0;

        foreach (Type t in types)
        {
            if (!t.IsSubclassOf(typeof(MonoBehaviour)))
                continue;
            if (t.FullName.Contains("UnityEngine") || t.FullName.StartsWith("System.") || t.FullName.StartsWith("LiteNetLib.") || t.FullName.Contains("Mirror") || t.FullName.Contains("RelativePositioning") || t.FullName.Contains("StaticUnityMethods") || t.FullName.Contains("VoiceChatPlaybackBase") || t.FullName.Contains("Radio.RadioItem") || t.FullName.Contains("DoorVariant.Update") || t.FullName.Contains("Distributors.Locker") || t.FullName.Contains("DoorUtils.DoorVariant") || t.FullName.Contains("RoomLightController")) continue;
            if (t.IsSubclassOf(typeof(DoorVariant))) continue;


            foreach (MethodInfo m in t.GetFullyConstructedMethods(includeNonPublic: true))
            {
                if (m.DeclaringType.FullName.Contains("UnityEngine") || m.DeclaringType.FullName.StartsWith("System.") || m.DeclaringType.FullName.StartsWith("LiteNetLib.") || m.DeclaringType.FullName.Contains("Mirror") || m.DeclaringType.FullName.Contains("RelativePositioning") || m.DeclaringType.FullName.Contains("StaticUnityMethods") || m.DeclaringType.FullName.Contains("VoiceChatPlaybackBase") || m.DeclaringType.FullName.Contains("Radio.RadioItem") || m.DeclaringType.FullName.Contains("DoorVariant.Update") || m.DeclaringType.FullName.Contains("Distributors.Locker") || m.DeclaringType.FullName.Contains("DoorUtils.DoorVariant") || m.DeclaringType.FullName.Contains("RoomLightController")) continue;
                methods.Add(m);
                //break;
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

        if (upcount % 10 == 0 && !ProfileMethodPatch.DisableProfiler)
            updatecollections();
        if (ProfileMethodPatch.DisableProfiler && allFields.Count > 0)
            allFields.Clear();
        upcount++;
        allFields.RemoveWhere(x => x == null);

        Application.targetFrameRate = upcapped ? 1000 : 60;

        if (Player.GetPlayers().Count(p => p.Role == RoleTypeId.Scp079) == 0) Scp079Camera.AllInstances.ForEach(c => c.enabled = false);
        else Scp079Camera.AllInstances.ForEach(c => c.enabled = true);
        ReferenceHub.AllHubs.ForEach(p => {
            p.inventory.Update();

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

        foreach (FirearmPickup p in ProfileMethodPatch.TestPatch5.instances)
        {
            if (p == null) continue;
            bool state = Player.GetPlayers().Count(player => (player.Position - p.Position).sqrMagnitude < 100) > 0;
            if (p.enabled != state) p.enabled = state;
        }
        ProfileMethodPatch.TestPatch5.instances.RemoveWhere(p => p == null || p.enabled);

        foreach (BodyArmorPickup p in ProfileMethodPatch.TestPatch6.instances)
        {
            if (p == null) continue;
            p.Update();
            bool state = p.IsAffected;
            if (p.enabled != state) p.enabled = state;
        }
        ProfileMethodPatch.TestPatch6.instances.RemoveWhere(p => p == null || p.enabled);

        if (GlobalChatIndicatorManager._singletonSet && GlobalChatIndicatorManager._singleton.enabled)
        {
            Log.Debug("Disabled global chat indicator");
            GlobalChatIndicatorManager._singleton.enabled = false;
        }

        activeStacks.Clear();

        var sortedDict1 = from entry in MethodMetrics.methodMetrics orderby entry.Value.invocationCount descending select entry;
        var sortedDict2 = from entry in MethodMetrics.methodMetrics orderby entry.Value.tickCount descending select entry;
        var sortedDict3 = from entry in MethodMetrics.methodMetrics.Where(x => x.Value.invocationCount > 10) orderby entry.Value.ticksPerInvoke descending select entry;

        int count = 0;
        foreach (KeyValuePair<MethodBase, MethodMetrics> kvp in sortedDict1)
        {
            if (activeStacks.Contains(kvp.Value.method))
            {
                count++;
                activeStacks.Add(kvp.Value.method);
            }
            if (count >= 5) break;
        }

        count = 0;
        foreach (KeyValuePair<MethodBase, MethodMetrics> kvp in sortedDict2)
        {
            if (activeStacks.Contains(kvp.Value.method))
            {
                count++;
                activeStacks.Add(kvp.Value.method);
            }
            if (count >= 5) break;
        }

        count = 0;
        foreach (KeyValuePair<MethodBase, MethodMetrics> kvp in sortedDict3)
        {
            if (activeStacks.Contains(kvp.Value.method))
            {
                count++;
                activeStacks.Add(kvp.Value.method);
            }
            if (count >= 10) break;
        }

    }

    public static string getMemoryMetrics (bool print = false)
    {
        string output = "Memory Check: " + allFields.Count + "\n";
        if (print) Log.Debug("Memory check: " + allFields.Count);
        List<KeyValuePair<string, int>> values = new();
        foreach (FieldInfo field in allFields)
        {
            //Log.Info($"{field.DeclaringType.FullName}.{field.Name} - {field.FieldType.Name}");
            if (typeof(ICollection).IsAssignableFrom(field.FieldType))
            {
                Type type;
                PropertyInfo info;
                object reference;
                object counter;
                try
                {
                    type = typeof(ICollection);
                    info = type.GetProperty("Count", BindingFlags.Public | BindingFlags.Instance);
                    reference = field.GetValue(null);
                    counter = null;
                }
                catch (Exception e)
                {
                    //Log.Error($"{field.DeclaringType.FullName}.{field.Name} (ICollection) Failed: {e.InnerException}\n{e.StackTrace}");
                    continue;
                }
                try
                {
                    counter = info.GetValue(reference);
                }
                catch (Exception e)
                {
                    //Log.Error($"{field.DeclaringType.FullName}.{field.Name} (ICollection) Failed: {e.InnerException}\n{e.StackTrace}");
                    continue;
                }
                values.Add(new KeyValuePair<string, int>($"{field.DeclaringType.FullName}.{field.Name}", (int)counter));
            }
            if (field.FieldType.GetGenericTypeDefinition() == typeof(HashSet<>))
            {
                Type type;
                PropertyInfo info;
                object reference;
                object counter;
                try
                {
                    type = typeof(HashSet<>).MakeGenericType(field.FieldType.GenericTypeArguments[0]);
                    info = type.GetProperty("Count", BindingFlags.Public | BindingFlags.Instance);
                    reference = field.GetValue(null);
                    counter = null;
                }
                catch (Exception e)
                {
                    //Log.Error($"{field.DeclaringType.FullName}.{field.Name} (HashSet) Failed: {e.InnerException}\n{e.StackTrace}");
                    continue;
                }
                try
                {
                    counter = info.GetValue(reference);
                }
                catch (Exception e)
                {
                    //Log.Error($"{field.DeclaringType.FullName}.{field.Name} (HashSet) Failed: {e.InnerException}\n{e.StackTrace}");
                    continue;
                }
                values.Add(new KeyValuePair<string, int>($"{field.DeclaringType.FullName}.{field.Name}", (int)counter));
            }
        }
        var sorted6 = from entry in values orderby entry.Value descending select entry;
        int numbers = 0;
        foreach (KeyValuePair<string, int> p in sorted6)
        {
            output += $"{p.Key}: {p.Value}\n";
            if (print) Log.Debug($"{p.Key}: {p.Value}");
            numbers++;
            if (numbers > 10) break;
        }
        return output;
    }
}
