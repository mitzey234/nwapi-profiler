namespace CustomProfiler
{
    using CustomPlayerEffects;
    using CustomProfiler.Patches;
    using HarmonyLib;
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

    public sealed class CustomProfilerPlugin
    {
        public static readonly Harmony Harmony = new("me.zereth.customprofiler");
        public const string Version = "1.0.0";

        internal static Dictionary<int, double> invocationCount = new();

        internal static Dictionary<int, double> tickCount = new();

        internal static Dictionary<int, string> translations = new();

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
            invocationCount.Clear();

            tickCount.Clear();

            translations.Clear();
        }

        internal static void disableProfiler ()
        {
            ProfileMethodPatch.DisableProfiler = true;

            invocationCount.Clear();
            tickCount.Clear();
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
                if (!t.IsSubclassOf(typeof(MonoBehaviour)))
                    continue;

                foreach (MethodInfo m in GetFullyConstructedMethods(t, includeNonPublic: true))
                {
                    methods.Add(m);
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
            if (Player.GetPlayers().Count(p => p.Role == RoleTypeId.Scp079) == 0) Scp079Camera.AllInstances.ForEach(c => c.enabled = false);
            else Scp079Camera.AllInstances.ForEach(c => c.enabled = true);
            ReferenceHub.AllHubs.ForEach(p => {
                foreach (StatusEffectBase b in p.playerEffectsController.AllEffects)
                {
                    b.Update();
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

        public static string getMetrics (bool print = false)
        {
            string output = "";
            int count = 0;
            var sortedDict = from entry in invocationCount orderby entry.Value descending select entry;
            Dictionary<int, float> calculation = new();
            foreach (KeyValuePair<int, double> kvp in sortedDict) calculation.Add(kvp.Key, (float)(tickCount[kvp.Key] / kvp.Value));
            var secondSortedDict = from entry in calculation orderby entry.Value descending select entry;
            foreach (KeyValuePair<int, double> kvp in sortedDict)
            {
                if (!translations.TryGetValue(kvp.Key, out string name)) name = kvp.Key.ToString();
                if (print) Log.Info("Invocation count: " + name + " - " + kvp.Value + " - Avg. Ticks Per: " + calculation[kvp.Key]);
                output += ("Invocation count: " + name + " - " + kvp.Value + " - Avg. Ticks Per: " + calculation[kvp.Key]) + "\n";
                count++;
                if (count > 5) break;
            }
            count = 0;
            sortedDict = from entry in tickCount orderby entry.Value descending select entry;
            foreach (KeyValuePair<int, double> kvp in sortedDict)
            {
                if (!translations.TryGetValue(kvp.Key, out string name)) name = kvp.Key.ToString();
                if (print) Log.Info("Tick count: " + name + " - " + kvp.Value + " - Avg. Ticks Per: " + calculation[kvp.Key]);
                output += ("Tick count: " + name + " - " + kvp.Value + " - Avg. Ticks Per: " + calculation[kvp.Key]) + "\n";
                count++;
                if (count > 5) break;
            }
            count = 0;
            foreach (KeyValuePair<int, float> kvp in secondSortedDict)
            {
                if (!translations.TryGetValue(kvp.Key, out string name)) name = kvp.Key.ToString();
                if (print) Log.Info("Ticks per invoke: " + name + " - " + kvp.Value + " - Invocation count: " + invocationCount[kvp.Key]);
                output += ("Ticks per invoke: " + name + " - " + kvp.Value + " - Invocation count: " + invocationCount[kvp.Key]) + "\n";
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
}
