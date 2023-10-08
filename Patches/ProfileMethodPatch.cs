namespace CustomProfiler.Patches;

using CommandSystem.Commands.RemoteAdmin;
using CustomPlayerEffects;
using CustomProfiler.Extensions;
using FacilitySoundtrack;
using HarmonyLib;
using Interactables.Interobjects.DoorUtils;
using InventorySystem;
using InventorySystem.Items;
using InventorySystem.Items.Armor;
using InventorySystem.Items.Firearms;
using InventorySystem.Items.Firearms.BasicMessages;
using InventorySystem.Items.Pickups;
using InventorySystem.Items.Usables.Scp244;
using MapGeneration;
using Mirror;
using PlayerRoles;
using PluginAPI.Core;
using PluginAPI.Core.Zones.Heavy;
using RelativePositioning;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using static CustomProfiler.Patches.ProfileMethodPatch.TestPatch7;
using static HarmonyLib.AccessTools;
using static PlayerList;

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

    //Save firearm instances and keep their updates limited
    [HarmonyPatch(typeof(FirearmPickup), "Update")]
    public class TestPatch5
    {
        public static HashSet<FirearmPickup> instances = new();

        public static bool Prefix(FirearmPickup __instance)
        {
            if (!instances.Contains(__instance))
            {
                instances.Add(__instance);
            }
            return true;
        }
    }

    //Save BodyArmorPickup instances and keep their updates limited
    [HarmonyPatch(typeof(BodyArmorPickup), "Update")]
    public class TestPatch6
    {
        public static HashSet<BodyArmorPickup> instances = new();

        public static bool Prefix(BodyArmorPickup __instance)
        {
            if (!instances.Contains(__instance))
            {
                instances.Add(__instance);
            }
            return true;
        }
    }

    //Disable updating for inventory system, this will be handled every second instead
    [HarmonyPatch(typeof(Inventory), "Update")]
    public class TestPatch7
    {
        public static void forceUpdate (Inventory instance)
        {
            instance.Update();
        }

        public static bool Prefix(Inventory __instance)
        {
            __instance.enabled = false;
            return true;
        }
    }

    //Force updating for inventory system
    [HarmonyPatch(typeof(InventoryExtensions), nameof(InventoryExtensions.ServerAddItem))]
    class TestPatch8
    {
        public static void Postfix(Inventory inv)
        {
            forceUpdate(inv);
        }
    }

    //Force updating for inventory system
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.UserCode_CmdDropItem__UInt16__Boolean))]
    class TestPatch9
    {
        public static void Postfix(Inventory __instance)
        {
            forceUpdate(__instance);
        }
    }

    //Force updating for inventory system
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.NetworkCurItem))]
    [HarmonyPatch(MethodType.Setter)]
    class TestPatch10
    {
        public static void Postfix(Inventory __instance)
        {
            forceUpdate(__instance);
        }
    }

    //Force updating for inventory system
    [HarmonyPatch(typeof(InventoryExtensions), nameof(InventoryExtensions.ServerRemoveItem))]
    class TestPatch11
    {
        public static void Postfix(Inventory inv)
        {
            forceUpdate(inv);
        }
    }

    //Force updating for inventory system
    [HarmonyPatch(typeof(InventoryExtensions), nameof(InventoryExtensions.ServerDropAmmo))]
    class TestPatch12
    {
        public static void Postfix(Inventory inv)
        {
            forceUpdate(inv);
        }
    }

    //Force updating for inventory system
    [HarmonyPatch(typeof(InventoryExtensions), nameof(InventoryExtensions.ServerSetAmmo))]
    class TestPatch13
    {
        public static void Postfix(Inventory inv)
        {
            forceUpdate(inv);
        }
    }

    //Force updating for inventory system
    [HarmonyPatch(typeof(InventoryExtensions), nameof(InventoryExtensions.ServerSetAmmo))]
    class TestPatch14
    {
        public static void Postfix(Inventory inv)
        {
            forceUpdate(inv);
        }
    }

    //Force updating for inventory system
    [HarmonyPatch(typeof(FirearmBasicMessagesHandler), nameof(FirearmBasicMessagesHandler.ServerRequestReceived))]
    class TestPatch18
    {
        public static void Postfix(NetworkConnection conn, RequestMessage msg)
        {
            ReferenceHub referenceHub;
            if (!ReferenceHub.TryGetHub(conn.identity.gameObject, out referenceHub))
            {
                return;
            }
            forceUpdate(referenceHub.inventory);
        }
    }

    //This helps with cleanup
    [HarmonyPatch(typeof(Scp244DeployablePickup), nameof(Scp244DeployablePickup.OnDestroy))]
    internal class TestPatch15
    {

        public static void Postfix(Scp244DeployablePickup __instance)
        {
            try
            {
                TestPatch16.timers.Remove(__instance.GetHashCode());
            } catch (Exception e)
            {
                //ignore
            }
        }
    }

    //Scp244 has a very intensive update, this limits it to 2 times a second
    [HarmonyPatch(typeof(Scp244DeployablePickup), "Update")]
    internal class TestPatch16
    {

        public static Dictionary<int, float> timers = new();

        public static bool Prefix(Scp244DeployablePickup __instance)
        {
            __instance.UpdateCurrentRoom();
            int hash = __instance.GetHashCode();
            if (!timers.TryGetValue(hash, out float timer)) timers.Add(hash, 0f);
            timers[hash] += Time.deltaTime;
            if (timers[hash] >= 0.5f)
            {
                timers[hash] = 0f;
                __instance.UpdateConditions();
            }
            __instance.UpdateRange();
            //__instance.UpdateEffects();
            return false;
        }
    }

    //Reduce this to 4 times per second - experimental, this might be important, it breaks InsufficientLighting updates
    [HarmonyPatch(typeof(InsufficientLighting), "AlwaysUpdate")]
    internal class TestPatch17
    {
        public static bool Prefix(InsufficientLighting __instance)
        {
            StaticUnityMethods.OnUpdate -= __instance.AlwaysUpdate;
            return true;
        }
    }


    internal static bool DisableProfiler = false;

    private static HarmonyMethod ProfilerTranspiler = new(typeof(ProfileMethodPatch), nameof(Transpiler));

    private static HashSet<MethodBase> ProfiledMethods = new();
    private static Dictionary<long, MethodBase> ProfiledMethodsByHash = new();

    internal static void ApplyProfiler(MethodBase method)
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
        if (!ProfiledMethodsByHash.TryGetValue(hash, out MethodBase method)) return;
        try
        {
            if (!CustomProfilerPlugin.metrics.TryGetValue(method, out methodMetrics metrics))
            {
                metrics = new methodMetrics();
                metrics.method = method;
                metrics.name = (method.DeclaringType != null ? method.DeclaringType.FullName : "Unknown Type") + "." + method.Name;
                CustomProfilerPlugin.metrics.Add(method, metrics);
            }
            metrics.invocationCount++;
            metrics.tickCount += totalTicks;
            if (!CustomProfilerPlugin.activeStacks.Contains(method) && metrics.calls.Count > 0) return;
            StackTrace t = new StackTrace();
            MethodBase callingMethod = t.FrameCount >= 3 && !t.GetFrame(2).GetMethod().Name.Contains("_Patch0") && !t.GetFrame(2).GetMethod().Name.Contains("_Patch1") ? t.GetFrame(2).GetMethod() : (t.FrameCount >= 4 && !t.GetFrame(3).GetMethod().Name.Contains("_Patch0") && !t.GetFrame(3).GetMethod().Name.Contains("_Patch1") ? t.GetFrame(3).GetMethod() : null);
            if (callingMethod == null || t.FrameCount < 3) return;
            if (!metrics.calls.TryGetValue(callingMethod, out methodMetrics subMetrics))
            {
                subMetrics = new methodMetrics();
                subMetrics.methodBase = callingMethod;
                subMetrics.name = (callingMethod.DeclaringType != null ? callingMethod.DeclaringType.FullName : "Unknown Type") + "." + callingMethod.Name;
                metrics.calls.Add(callingMethod, subMetrics);
            }
            subMetrics.invocationCount++;
            subMetrics.tickCount += totalTicks;
        } catch (Exception e)
        {
            Log.Error(e.Message + "\n" + e.StackTrace);
        }

        //Log.Info("Test: " + method.DeclaringType.Assembly.FullName + " - " + method.Name + " - " + method.DeclaringType);
        // log any info you want about the method here.
        // feel free to use StackTrace or StackFrame.
    }
}
