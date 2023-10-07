namespace CustomProfiler;

using CustomPlayerEffects;
using CustomProfiler.Patches;
using HarmonyLib;
using Interactables.Interobjects.DoorUtils;
using InventorySystem.Items.Armor;
using InventorySystem.Items.Firearms;
using InventorySystem.Items.Usables.Scp244.Hypothermia;
using PlayerRoles;
using PlayerRoles.PlayableScps.Scp079.Cameras;
using PluginAPI.Core;
using PluginAPI.Core.Attributes;
using PluginAPI.Enums;
using RoundRestarting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Utils.NonAllocLINQ;
using VoiceChat;
using static CustomProfiler.Patches.ProfileMethodPatch;

public class methodMetrics
{
    public long invocationCount = 0;
    public long tickCount = 0;
    public string name = "";
    public MethodInfo method = null;
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
    public const string Version = "1.0.0";

    internal static Dictionary<MethodInfo, methodMetrics> metrics = new();

    internal static List<MethodInfo> activeStacks = new();

    internal static List<MethodInfo> patched = new();

    [PluginEntryPoint("CustomProfiler", Version, "A custom profiler for SCP:SL.", "Zereth")]
    [PluginPriority(LoadPriority.Highest)]
    public void Entry()
    {
        // try to apply some patches here and see what works.
        // if you get any errors you dont know how to fix, contact me.

        Harmony.PatchAll();

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
    }

    public static void enableOptimizations ()
    {
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

    internal static void enableProfiler()
    {
        if (CustomProfilerPlugin.patched.Count > 0)
        {
            ProfileMethodPatch.DisableProfiler = false;
            return;
        }

        Type[] types = typeof(GameCore.Console).Assembly.GetTypes();

        // use hashset so we dont
        // try to patch the same method twice.
        HashSet<MethodInfo> methods = new();

        int failed = 0;
        int patched = 0;

        foreach (Type t in types)
        {
            if (!t.IsSubclassOf(typeof(MonoBehaviour))) continue;
            if (t.FullName.Contains("UnityEngine") || t.FullName.StartsWith("System.Object") || t.FullName.Contains("Mirror.")) continue;
            if (t.IsSubclassOf(typeof(DoorVariant))) continue; 
            
            foreach (MethodInfo m in GetFullyConstructedMethods(t, includeNonPublic: true))
            {
                if (m.DeclaringType.FullName.Contains("UnityEngine")) continue;
                methods.Add(m);
                //break;
            }
        }

        Log.Info("Patching " + methods.Count + " methods");

        foreach (MethodInfo m in methods)
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
            CustomProfilerPlugin.patched.Add(m);
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
            foreach (StatusEffectBase b in p.playerEffectsController.AllEffects)
            {
                b.Update();
                if (b is Hypothermia) (b as Hypothermia).SubEffects.ForEach(e => e.enabled = e.IsActive);
                if (b is CardiacArrest) (b as CardiacArrest).SubEffects.ForEach(e => e.enabled = e.IsActive);
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

        foreach (FirearmPickup p in TestPatch9.instances)
        {
            if (p == null) continue;
            bool state = Player.GetPlayers().Count(player => (player.Position - p.Position).sqrMagnitude < 100) > 0;
            if (p.enabled != state) p.enabled = state;
        }
        TestPatch9.instances.RemoveWhere(p => p == null || p.enabled);

        foreach (BodyArmorPickup p in TestPatch10.instances)
        {
            if (p == null) continue;
            p.Update();
            bool state = p.IsAffected;
            if (p.enabled != state) p.enabled = state;
        }
        TestPatch10.instances.RemoveWhere(p => p == null || p.enabled);

        foreach (FirearmWorldmodelLaser p in TestPatch11.instances)
        {
            if (p == null) continue;
            p.LateUpdate();
            bool near = Player.GetPlayers().Count(player => (player.Position - p.transform.position).sqrMagnitude < 100) > 0;
            bool state = !p._pickupMode || near;
            if (p.enabled != state) p.enabled = state;
        }
        TestPatch11.instances.RemoveWhere(p => p == null || p.enabled);


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
        foreach (KeyValuePair<MethodInfo, methodMetrics> kvp in sortedDict1)
        {
            if (activeStacks.Contains(kvp.Value.method))
            {
                count++;
                activeStacks.Add(kvp.Value.method);
            }
            if (count >= 5) break;
        }

        count = 0;
        foreach (KeyValuePair<MethodInfo, methodMetrics> kvp in sortedDict2)
        {
            if (activeStacks.Contains(kvp.Value.method))
            {
                count++;
                activeStacks.Add(kvp.Value.method);
            }
            if (count >= 5) break;
        }

        count = 0;
        foreach (KeyValuePair<MethodInfo, methodMetrics> kvp in sortedDict3)
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
        foreach (KeyValuePair<MethodInfo, methodMetrics> kvp in sortedDict)
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
            if (count > 5) break;
        }
        if (print) Log.Info("");
        output += "\n";

        count = 0;
        sortedDict = from entry in metrics orderby entry.Value.tickCount descending select entry;
        if (print) Log.Info("Tick count: ");
        output += ("Tick count: \n");
        foreach (KeyValuePair<MethodInfo, methodMetrics> kvp in sortedDict)
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
            if (count > 5) break;
        }
        if (print) Log.Info("");
        output += "\n";

        count = 0;
        if (print) Log.Info("Ticks per invoke: ");
        output += ("Ticks per invoke: \n");
        foreach (KeyValuePair<MethodInfo, methodMetrics> kvp in secondSortedDict)
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
