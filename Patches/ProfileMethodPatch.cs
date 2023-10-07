﻿namespace CustomProfiler.Patches
{
    using CustomProfiler.Extensions;
    using FacilitySoundtrack;
    using HarmonyLib;
    using MapGeneration;
    using Mirror;
    using PlayerRoles;
    using PluginAPI.Core;
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Diagnostics;
    using System.Linq;
    using System.Reflection;
    using System.Reflection.Emit;
    using UnityEngine;
    using static HarmonyLib.AccessTools;

    public static class ProfileMethodPatch
    {
        //This stops doing pointless math calulations, probbaly causes desync in terms of max movement speed
        [HarmonyPatch(typeof(PlayerRoles.FirstPersonControl.FirstPersonMovementModule))]
        [HarmonyPatch("MaxMovementSpeed", MethodType.Getter)]
        class TestPatch1
        {
            public static bool Prefix(ref float __result)
            {
                __result = 100f;
                return false;
            }
        }

        //This optimizes Tesla gates position testing
        [HarmonyPatch(typeof(TeslaGate), "IsInIdleRange", new Type[] { typeof(Vector3) })]
        class TestPatch2
        {
            public static bool Prefix(Vector3 position, TeslaGate __instance, ref bool __result)
            {
                __result = (__instance.Position - position).sqrMagnitude < __instance.distanceToIdle*__instance.distanceToIdle;
                return false;
            }
        }

        //This optimizes Tesla gates position testing
        [HarmonyPatch(typeof(TeslaGate), "InRange", new Type[] { typeof(Vector3) })]
        class TestPatch3
        {
            public static bool Prefix(Vector3 position, TeslaGate __instance, ref bool __result)
            {
                __result = (__instance.Position - position).sqrMagnitude < __instance.sizeOfTrigger * __instance.sizeOfTrigger;
                return false;
            }
        }

        //This optimizes Tesla gates and the players that it checks
        [HarmonyPatch(typeof(TeslaGateController))]
        [HarmonyPatch("FixedUpdate", MethodType.Normal)]
        class TestPatch4
        {
            public static bool Prefix(TeslaGateController __instance)
            {
                if (NetworkServer.active)
                {
                    using (List<TeslaGate>.Enumerator enumerator = __instance.TeslaGates.GetEnumerator())
                    {
                        while (enumerator.MoveNext())
                        {
                            TeslaGate teslaGate = enumerator.Current;
                            if (teslaGate.isActiveAndEnabled)
                            {
                                if (teslaGate.InactiveTime > 0f)
                                {
                                    teslaGate.NetworkInactiveTime = Mathf.Max(0f, teslaGate.InactiveTime - Time.fixedDeltaTime);
                                }
                                else
                                {
                                    bool isIdling = false;
                                    bool isTriggered = false;
                                    foreach (ReferenceHub referenceHub in ReferenceHub.AllHubs)
                                    {
                                        RoomIdentifier r = RoomIdUtils.RoomAtPosition(referenceHub.gameObject.transform.position);
                                        if (referenceHub.IsAlive() && r != null && r.Zone == FacilityZone.HeavyContainment)
                                        {
                                            if (!isIdling)
                                            {
                                                isIdling = teslaGate.IsInIdleRange(referenceHub);
                                            }
                                            if (!isTriggered && teslaGate.PlayerInRange(referenceHub) && !teslaGate.InProgress)
                                            {
                                                isTriggered = true;
                                            }
                                        }
                                    }
                                    if (isTriggered)
                                    {
                                        teslaGate.ServerSideCode();
                                    }
                                    if (isIdling != teslaGate.isIdling)
                                    {
                                        teslaGate.ServerSideIdle(isIdling);
                                    }
                                }
                            }
                        }
                        return false;
                    }
                }
                foreach (TeslaGate teslaGate2 in __instance.TeslaGates)
                {
                    teslaGate2.ClientSideCode();
                }
                return false;
            }
        }

        //This kills clientside code that shouldn't be here
        [HarmonyPatch(typeof(ZoneAmbientSoundtrack), "UpdateVolume")]
        class TestPatch5
        {
            public static bool Prefix(ZoneAmbientSoundtrack __instance)
            {
                __instance.enabled = false;
                return false;
            }
        }

        //This kills clientside code that shouldn't be here
        [HarmonyPatch(typeof(StatusBar), "Update")]
        class TestPatch6
        {
            public static bool Prefix(StatusBar __instance)
            {
                __instance.enabled = false;
                return false;
            }
        }

        //This kills clientside code that shouldn't be here
        [HarmonyPatch(typeof(MainCameraController), "LateUpdate")]
        class TestPatch7
        {
            public static bool Prefix(MainCameraController __instance)
            {
                __instance.enabled = false;
                return false;
            }
        }

        internal static bool DisableProfiler = false;

        private static HarmonyMethod ProfilerTranspiler = new(typeof(ProfileMethodPatch), nameof(Transpiler));

        private static HashSet<MethodInfo> ProfiledMethods = new();
        private static Dictionary<long, MethodInfo> ProfiledMethodsByHash = new();

        internal static void ApplyProfiler(MethodInfo method)
        {
            if (ProfiledMethods.Contains(method))
            {
                return;
            }

            if (method.GetMethodBody() == null)
            {
                throw new ArgumentException("Cannot patch a method without a body.");
            }

            Type[] args;

            if (method.IsGenericMethod)
            {
                throw new ArgumentException($"Cannot patch generic method");

                if (method.ContainsGenericParameters)
                {
                    throw new ArgumentException($"Cannot patch generic method that is not constructed.");
                }

                args = method.GetGenericArguments();

                for (int i = 0; i < args.Length; i++)
                {
                    if (!args[i].IsValueType)
                    {
                        throw new ArgumentException($"Cannot patch generic method using reference type arguments. This can cause instability.");
                    }
                }
            }

            if (method.DeclaringType?.IsGenericType ?? false)
            {
                if (method.DeclaringType.ContainsGenericParameters)
                {
                    throw new ArgumentException($"Cannot patch method with a generic declaring type that is not constructed.");
                }
            }

            Type baseType = method.DeclaringType?.BaseType;

            while (baseType != null)
            {
                if (baseType.IsGenericType)
                {
                    if (baseType.ContainsGenericParameters)
                    {
                        throw new ArgumentException($"Cannot patch generic method within a declaring type deriving from a generic type that is not constructed.");
                    }
                }

                baseType = baseType.BaseType;
            }

            CustomProfilerPlugin.Harmony.Patch(method, prefix: null, postfix: null, transpiler: ProfilerTranspiler, finalizer: null);

            ProfiledMethods.Add(method);
            ProfiledMethodsByHash.Add(method.GetHashCode(), method);
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator generator)
        {
            instructions.BeginTranspiler(out List<CodeInstruction> newInstuctions);

            // long startTimestamp;
            LocalBuilder startTimestamp = generator.DeclareLocal(typeof(long));
            LocalBuilder profilerDisabled = generator.DeclareLocal(typeof(bool));

            Label profilerDisabledLabel = generator.DefineLabel();

            newInstuctions[0].labels.Add(profilerDisabledLabel);

            // We get the starting timestamp recorded by the timer mechanism
            // and store it into a variable we can use later.
            newInstuctions.InsertRange(0, new CodeInstruction[]
            {
                // bool profilerDisabled = ProfileMethodPatch.DisableProfiler;
                // if (!profilerDisabled)
                // {
                //     long startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
                // }
                new(OpCodes.Ldsfld, Field(typeof(ProfileMethodPatch), nameof(DisableProfiler))),
                new(OpCodes.Dup),
                new(OpCodes.Stloc_S, profilerDisabled),
                new(OpCodes.Brtrue_S, profilerDisabledLabel),
                new(OpCodes.Call, Method(typeof(Stopwatch), nameof(Stopwatch.GetTimestamp))),
                new(OpCodes.Stloc_S, startTimestamp),
            });

            int index = newInstuctions.FindLastIndex(x => x.opcode == OpCodes.Ret);

            // For each return instruction, we steal its labels
            // and insert a profile call to store the recorded time
            // taken by the method to execute.
            while (index != -1)
            {
                Label justReturnLabel = generator.DefineLabel();
                List<Label> stolenLabels = newInstuctions[index].ExtractLabels();
                newInstuctions[index].labels.Add(justReturnLabel);

                newInstuctions.InsertRange(index, new CodeInstruction[]
                {
                    // if (!profilerDisabled)
                    // {
                    //     ProfileMethodPatch.ProfileMethod(method.GetHashCode(), Stopwatch.GetTimestamp() - startTimestamp)
                    // }
                    new CodeInstruction(OpCodes.Ldloc_S, profilerDisabled).WithLabels(stolenLabels),
                    new(OpCodes.Brtrue_S, justReturnLabel),
                    new(OpCodes.Ldc_I4, method.GetHashCode()),
                    new(OpCodes.Call, Method(typeof(Stopwatch), nameof(Stopwatch.GetTimestamp))),
                    new(OpCodes.Ldloc_S, startTimestamp),
                    new(OpCodes.Sub),
                    new(OpCodes.Call, Method(typeof(ProfileMethodPatch), nameof(ProfileMethod))),
                });

                index = newInstuctions.FindLastIndex(index, x => x.opcode == OpCodes.Ret);
            }

            return newInstuctions.FinishTranspiler();
        }

        private static void ProfileMethod(int hash, long totalTicks)
        {
            if (!ProfiledMethodsByHash.TryGetValue(hash, out MethodInfo method)) return;
            try
            {
                string name = (method.DeclaringType != null ? method.DeclaringType.Name : "Unknown Type") + "." + method.Name;
                CustomProfilerPlugin.translations[hash] = name;
                bool found = CustomProfilerPlugin.invocationCount.TryGetValue(hash, out double count);
                if (!found) CustomProfilerPlugin.invocationCount.Add(hash, 1);
                else CustomProfilerPlugin.invocationCount[hash] = count + 1;

                found = CustomProfilerPlugin.tickCount.TryGetValue(hash, out count);
                if (!found) CustomProfilerPlugin.tickCount.Add(hash, totalTicks);
                else CustomProfilerPlugin.tickCount[hash] = count + totalTicks;
            } catch (Exception e)
            {
            }

            //Log.Info("Test: " + method.DeclaringType.Assembly.FullName + " - " + method.Name + " - " + method.DeclaringType);
            // log any info you want about the method here.
            // feel free to use StackTrace or StackFrame.
        }
    }
}
