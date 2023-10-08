namespace CustomProfiler;

using CustomPlayerEffects;
using CustomProfiler.Patches;
using HarmonyLib;
using Interactables.Interobjects.DoorUtils;
using InventorySystem.Items.Armor;
using InventorySystem.Items.Firearms;
using InventorySystem.Items.Pickups;
using InventorySystem.Items.Usables.Scp244;
using InventorySystem.Items.Usables.Scp244.Hypothermia;
using Mirror;
using PlayerRoles;
using PlayerRoles.FirstPersonControl;
using PlayerRoles.FirstPersonControl.NetworkMessages;
using PlayerRoles.PlayableScps.Scp079.Cameras;
using PlayerRoles.Voice;
using PluginAPI.Core;
using PluginAPI.Core.Attributes;
using PluginAPI.Enums;
using RelativePositioning;
using RoundRestarting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Utils;
using Utils.NonAllocLINQ;
using VoiceChat;
using VoiceChat.Networking;
using static CustomProfiler.Patches.ProfileMethodPatch;
using static CustomProfiler.Patches.ProfileMethodPatch.TestPatch7;
using static UnityEngine.GraphicsBuffer;

public class methodMetrics
{
    public long invocationCount = 0;
    public long tickCount = 0;
    public string name = "";
    public MethodBase method = null;
    public MethodBase methodBase = null;
    public Dictionary<MethodBase, methodMetrics> calls = new();

    public double ticksPerInvoke
    {
        get { return tickCount == 0 ? -1 : Math.Round(tickCount / invocationCount * 100.0)/100.0; }
    }


}

public sealed class CustomProfilerPlugin
{
    public static readonly Harmony Harmony = new("me.zereth.customprofiler");
    public static readonly Harmony HarmonyOptimizations = new("me.zereth.customprofiler.optimizations");
    public const string Version = "1.0.0";

    internal static Dictionary<MethodBase, methodMetrics> metrics = new();

    internal static List<MethodBase> activeStacks = new();

    static bool optimized = false;

    [PluginEntryPoint("CustomProfiler", Version, "A custom profiler for SCP:SL.", "Zereth")]
    [PluginPriority(LoadPriority.Highest)]
    public void Entry()
    {
        // try to apply some patches here and see what works.
        // if you get any errors you dont know how to fix, contact me.

        HarmonyOptimizations.PatchAll();
        optimized = true;

        StaticUnityMethods.OnLateUpdate += onUpdate;
    }

    public static void disableOptimizations ()
    {
        StaticUnityMethods.OnLateUpdate -= onUpdate;
        Scp079Camera.AllInstances.ForEach(c => c.enabled = true);
        ReferenceHub.AllHubs.ForEach(p => {
            foreach (StatusEffectBase b in p.playerEffectsController.AllEffects)
            {
                b.enabled = true;
            }
        });
        if (optimized) HarmonyOptimizations.UnpatchAll(HarmonyOptimizations.Id);
    }

    public static void enableOptimizations ()
    {
        if (optimized) return;
        HarmonyOptimizations.PatchAll();
        StaticUnityMethods.OnLateUpdate += onUpdate;
    }

    internal static void reset()
    {
        metrics.Clear();
    }

    internal static void disableProfiler ()
    {
        ProfileMethodPatch.DisableProfiler = true;

        metrics.Clear();
    }

    public static int patched = 0;

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
            if (!t.IsSubclassOf(typeof(MonoBehaviour))) continue;
            if (t.FullName.Contains("UnityEngine") || t.FullName.StartsWith("System.") || t.FullName.StartsWith("LiteNetLib.") || t.FullName.Contains("Mirror") || t.FullName.Contains("RelativePositioning") || t.FullName.Contains("StaticUnityMethods") || t.FullName.Contains("VoiceChatPlaybackBase") || t.FullName.Contains("Radio.RadioItem") || t.FullName.Contains("DoorVariant.Update") || t.FullName.Contains("Distributors.Locker") || t.FullName.Contains("DoorUtils.DoorVariant") || t.FullName.Contains("RoomLightController")) continue;
            if (t.IsSubclassOf(typeof(DoorVariant))) continue;


            foreach (MethodInfo m in GetFullyConstructedMethods(t, includeNonPublic: true))
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

    public static void onUpdate()
    {
        timer += Time.deltaTime;
        if (RoundRestart.IsRoundRestarting) return;
        if (timer <= 1.0f) return;
        timer = 0;

        //Application.targetFrameRate = 1000;

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

        foreach (FirearmPickup p in TestPatch5.instances)
        {
            if (p == null) continue;
            bool state = Player.GetPlayers().Count(player => (player.Position - p.Position).sqrMagnitude < 100) > 0;
            if (p.enabled != state) p.enabled = state;
        }
        TestPatch5.instances.RemoveWhere(p => p == null || p.enabled);

        foreach (BodyArmorPickup p in TestPatch6.instances)
        {
            if (p == null) continue;
            p.Update();
            bool state = p.IsAffected;
            if (p.enabled != state) p.enabled = state;
        }
        TestPatch6.instances.RemoveWhere(p => p == null || p.enabled);

        if (GlobalChatIndicatorManager._singletonSet && GlobalChatIndicatorManager._singleton.enabled)
        {
            Log.Debug("Disabled global chat indicator");
            GlobalChatIndicatorManager._singleton.enabled = false;
        }

        activeStacks.Clear();

        var sortedDict1 = from entry in metrics orderby entry.Value.invocationCount descending select entry;
        var sortedDict2 = from entry in metrics orderby entry.Value.tickCount descending select entry;
        var sortedDict3 = from entry in metrics.Where(x => x.Value.invocationCount > 10) orderby entry.Value.ticksPerInvoke descending select entry;

        int count = 0;
        foreach (KeyValuePair<MethodBase, methodMetrics> kvp in sortedDict1)
        {
            if (activeStacks.Contains(kvp.Value.method))
            {
                count++;
                activeStacks.Add(kvp.Value.method);
            }
            if (count >= 5) break;
        }

        count = 0;
        foreach (KeyValuePair<MethodBase, methodMetrics> kvp in sortedDict2)
        {
            if (activeStacks.Contains(kvp.Value.method))
            {
                count++;
                activeStacks.Add(kvp.Value.method);
            }
            if (count >= 5) break;
        }

        count = 0;
        foreach (KeyValuePair<MethodBase, methodMetrics> kvp in sortedDict3)
        {
            if (activeStacks.Contains(kvp.Value.method))
            {
                count++;
                activeStacks.Add(kvp.Value.method);
            }
            if (count >= 10) break;
        }

    }

    public static string getMetrics (bool print = false)
    {
        string output = "";
        int count = 0;
        var sortedDict = from entry in metrics orderby entry.Value.invocationCount descending select entry;
        var secondSortedDict = from entry in metrics.Where(x => x.Value.invocationCount > 10) orderby entry.Value.ticksPerInvoke descending select entry;
        if (print) Log.Info("Invocation count: ");
        output += ("Invocation count: \n");
        foreach (KeyValuePair<MethodBase, methodMetrics> kvp in sortedDict)
        {
            if (print) Log.Info(kvp.Value.name + " - " + kvp.Value.invocationCount + " - Avg. Ticks Per: " + kvp.Value.ticksPerInvoke);
            output += (kvp.Value.name + " - " + kvp.Value.invocationCount + " - Avg. Ticks Per: " + kvp.Value.ticksPerInvoke) + "\n";
            var sortedDict2 = from entry in kvp.Value.calls orderby entry.Value.invocationCount descending select entry;
            foreach (KeyValuePair<MethodBase, methodMetrics> kvp2 in sortedDict2)
            {
                if (print) Log.Info("\t" + kvp2.Value.name + " - " + kvp2.Value.invocationCount + " - Avg. Ticks Per: " + kvp2.Value.ticksPerInvoke);
                output += ("\t" + kvp2.Value.name + " - " + kvp2.Value.invocationCount + " - Avg. Ticks Per: " + kvp2.Value.ticksPerInvoke) + "\n";
            }
            count++;
            if (count > 10) break;
        }
        if (print) Log.Info("");
        output += "\n";

        count = 0;
        sortedDict = from entry in metrics orderby entry.Value.tickCount descending select entry;
        if (print) Log.Info("Tick count: ");
        output += ("Tick count: \n");
        foreach (KeyValuePair<MethodBase, methodMetrics> kvp in sortedDict)
        {
            if (print) Log.Info(kvp.Value.name + " - " + kvp.Value.tickCount + " - " + kvp.Value.invocationCount + " - Avg. Ticks Per: " + kvp.Value.ticksPerInvoke);
            output += (kvp.Value.name + " - " + kvp.Value.tickCount + " - " + kvp.Value.invocationCount + " - Avg. Ticks Per: " + kvp.Value.ticksPerInvoke) + "\n";
            var sortedDict2 = from entry in kvp.Value.calls orderby entry.Value.invocationCount descending select entry;
            foreach (KeyValuePair<MethodBase, methodMetrics> kvp2 in sortedDict2)
            {
                if (print) Log.Info("\t" + kvp2.Value.name + " - " + kvp2.Value.invocationCount + " - Avg. Ticks Per: " + kvp2.Value.ticksPerInvoke);
                output += ("\t" + kvp2.Value.name + " - " + kvp2.Value.invocationCount + " - Avg. Ticks Per: " + kvp2.Value.ticksPerInvoke) + "\n";
            }
            count++;
            if (count > 10) break;
        }
        if (print) Log.Info("");
        output += "\n";

        count = 0;
        if (print) Log.Info("Ticks per invoke: ");
        output += ("Ticks per invoke: \n");
        foreach (KeyValuePair<MethodBase, methodMetrics> kvp in secondSortedDict)
        {
            if (print) Log.Info(kvp.Value.name + " - " + kvp.Value.ticksPerInvoke + " - Invocation count: " + kvp.Value.invocationCount);
            output += (kvp.Value.name + " - " + kvp.Value.ticksPerInvoke + " - Invocation count: " + kvp.Value.invocationCount) + "\n";
            var sortedDict2 = from entry in kvp.Value.calls orderby entry.Value.invocationCount descending select entry;
            foreach (KeyValuePair<MethodBase, methodMetrics> kvp2 in sortedDict2)
            {
                if (print) Log.Info("\t" + kvp2.Value.name + " - " + kvp2.Value.invocationCount + " - Avg. Ticks Per: " + kvp2.Value.ticksPerInvoke);
                output += ("\t" + kvp2.Value.name + " - " + kvp2.Value.invocationCount + " - Avg. Ticks Per: " + kvp2.Value.ticksPerInvoke) + "\n";
            }
            count++;
            if (count > 10) break;
        }
        return output;
    }

    private static IEnumerable<MethodInfo> GetFullyConstructedMethods(Type type, bool includeNonPublic)
    {
        if (type.IsGenericType && !type.IsConstructedGenericType)
        {
            yield break;
        }

        BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly;

        if (includeNonPublic)
        {
            flags |= BindingFlags.NonPublic;
        }

        while (type != null)
        {
            MethodInfo[] methods = type.GetMethods(flags);

            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo m = methods[i];

                if (m.IsGenericMethod)
                    continue;

                if (!m.HasMethodBody())
                    continue;

                yield return m;
            }

            type = type.BaseType;
        }
    }
}
