namespace CustomProfiler.Patches.Optimizations;

using CustomPlayerEffects;
using CustomProfiler.Extensions;
using Elevators;
using HarmonyLib;
using Hazards;
using InventorySystem.Items.Armor;
using InventorySystem.Items.Firearms.Attachments;
using InventorySystem.Items.Pickups;
using InventorySystem.Items.Usables.Scp244;
using MapGeneration.Distributors;
using PlayerRoles;
using PlayerRoles.FirstPersonControl;
using PlayerRoles.FirstPersonControl.NetworkMessages;
using PlayerRoles.PlayableScps.Scp079;
using PlayerRoles.PlayableScps.Scp079.Cameras;
using PlayerRoles.Voice;
using PluginAPI.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using UnityEngine;
using Utils.NonAllocLINQ;
using VoiceChat.Networking;
using static HarmonyLib.AccessTools;

[HarmonyPatch]
internal class BasicStuff
{

    // This causes SCP-939 to be able to see players from any distance as long
    // as they have been seen previously, causing a serious disadvantage
    // for human classes.
    //[HarmonyPatch(typeof(PlayerRoles.FirstPersonControl.FirstPersonMovementModule))]
    //[HarmonyPatch("MaxMovementSpeed", MethodType.Getter)]
    class TestPatch1
    {
        public static bool Prefix(ref float __result)
        {
            __result = 100f;
            return false;
        }
    }

    //Scp244 has a very intensive update, this limits it to 2 times a second
    [HarmonyPatch(typeof(Scp244DeployablePickup))]
    internal class TestPatch16
    {
        public sealed class FloatValue
        {
            public float Time;
        }

        public static ConditionalWeakTable<Scp244DeployablePickup, FloatValue> timers = new();

        [HarmonyPatch(nameof(Scp244DeployablePickup.Update))]
        public static bool Prefix(Scp244DeployablePickup __instance)
        {
            __instance.UpdateCurrentRoom();

            if (!timers.TryGetValue(__instance, out FloatValue value))
            {
                timers.Add(__instance, value = new FloatValue());
            }

            ref float time = ref value.Time;

            if (time >= 0.5f)
            {
                time -= 0.5f;
                __instance.UpdateConditions();
            }

            __instance.UpdateRange();
            __instance.UpdateEffects();
            time += Time.deltaTime;
            return false;
        }

        [HarmonyPatch(nameof(Scp244DeployablePickup.GrowSpeed), MethodType.Getter)]
        public static float Postfix(float result)
        {
            return result * (0.5f / Time.deltaTime);
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

    [HarmonyPatch(typeof(ItemPickupBase), "OnDestroy")]
    internal class TestPatch19
    {
        public static void Postfix(ItemPickupBase __instance)
        {
            __instance.PhysicsModuleSyncData.Clear();
            __instance.syncObjects.Clear();
        }
    }

    [HarmonyPatch(typeof(BodyArmorPickup), "OnTriggerStay")]
    internal class TestPatch20
    {
        public static bool Prefix(BodyArmorPickup __instance)
        {
            if (__instance._rb.IsSleeping()) return false;
            else return true;
        }
    }

    //Disable workstation controller when its not actually active
    [HarmonyPatch(typeof(WorkstationController), "Update")]
    internal class TestPatch21
    {
        public static bool Prefix(WorkstationController __instance)
        {
            if (__instance.Status == 0)
            {
                __instance.enabled = false;
                return false;
            }
            else return true;
        }
    }

    //Enable workstation when users interact with it
    [HarmonyPatch(typeof(WorkstationController), "ServerInteract")]
    internal class TestPatch22
    {
        public static void Postfix(WorkstationController __instance)
        {
            __instance.enabled = true;
        }
    }

    //Disable Scp079Generator controller when its not actually active
    [HarmonyPatch(typeof(Scp079Generator), "Update")]
    internal class TestPatch23
    {
        public static bool Prefix(Scp079Generator __instance)
        {
            if (__instance.Engaged || !__instance.Activating)
            {
                __instance.enabled = false;
                return false;
            }
            else return true;
        }
    }

    //Enable Scp079Generator when users interact with it
    [HarmonyPatch(typeof(Scp079Generator), "ServerInteract")]
    internal class TestPatch24
    {
        public static void Postfix(Scp079Generator __instance)
        {
            __instance.enabled = true;
        }
    }

    [HarmonyPatch(typeof(VoiceTransceiver))]
    public static class TestPatch27
    {
        // We dont call update inside of a foreach loop.
        // We call after the ratelimit check.

        [HarmonyTranspiler]
        [HarmonyPatch(nameof(VoiceTransceiver.ServerReceiveMessage))]
        private static IEnumerable<CodeInstruction> VoiceTransceiverServerReceiveMessage_Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator generator)
        {
            instructions.BeginTranspiler(out List<CodeInstruction> newInstructions);

            int index = newInstructions.FindIndex(x => x.Calls(Method(typeof(VoiceModuleBase), nameof(VoiceModuleBase.CheckRateLimit))));

            newInstructions.InsertRange(index + 1, new CodeInstruction[]
            {
                // voiceRole.VoiceModule.Update();
                new(OpCodes.Ldloc_0),
                new(OpCodes.Callvirt, PropertyGetter(typeof(IVoiceRole), nameof(IVoiceRole.VoiceModule))),
                new(OpCodes.Callvirt, Method(typeof(VoiceModuleBase), nameof(VoiceModuleBase.Update))),
            });

            return newInstructions.FinishTranspiler();
        }
    }

    //Slow down ServerRole updates
    [HarmonyPatch(typeof(ServerRoles))]
    internal class TestPatch28
    {
        public sealed class FloatValue
        {
            public float Time;
        }

        public static ConditionalWeakTable<ServerRoles, FloatValue> timers = new();

        [HarmonyPatch(nameof(ServerRoles.Update))]
        public static bool Prefix(ServerRoles __instance)
        {
            if (!timers.TryGetValue(__instance, out FloatValue value))
            {
                timers.Add(__instance, value = new FloatValue());
            }

            ref float time = ref value.Time;
            time += Time.deltaTime;
            if (time <= 1f) return false;
            time -= 1f;
            return true;
        }
    }

    //This disables updating posiitons when its not really necessary except when elevator is moving
    [HarmonyPatch(typeof(ElevatorFollowerBase))]
    internal class TestPatch31
    {
        [HarmonyPatch(nameof(ElevatorFollowerBase.OnElevatorMoved))]
        public static void Postfix(ElevatorFollowerBase __instance)
        {
            if (!__instance.InElevator || __instance.TrackedChamber._curSequence == Interactables.Interobjects.ElevatorChamber.ElevatorSequence.Ready)
            {
                if (__instance.enabled)
                {
                    __instance.enabled = false;
                }
            }
            else if (!__instance.enabled)
            {
                __instance.enabled = true;
            }
        }
    }

    [HarmonyPatch(typeof(FpcServerPositionDistributor))]
    [HarmonyPatch("SendRate", MethodType.Getter)]
    class TestPatch33
    {
        public static bool Prefix(ref float __result)
        {
            __result = 1f / Mathf.Clamp(CustomProfilerPlugin.PluginConfig.PlayerPositionUpdateRate, 10, 60); ;
            return false;
        }
    }

    //For some reason Northwood's code will update all of the angles for every single camera in the facility regardless of whether its active or not
    //This patch makes it so it will only do that when the camera is active, saving at least a little performance
    //If this breaks something (which I don't think it will) feel free to delete this whole file.
    [HarmonyPatch(typeof(Scp079Camera), "Update")]
    internal class TestPatch34
    {
        public static bool Prefix(Scp079Camera __instance)
        {
            if (!__instance.IsActive) return false;
            __instance.VerticalAxis.Update(__instance);
            __instance.HorizontalAxis.Update(__instance);
            __instance.ZoomAxis.Update(__instance);
            if (Scp079Role.ActiveInstances.All((Scp079Role x) => x.CurrentCamera != __instance, true))
            {
                __instance.IsActive = false;
                return false;
            }
            Vector3 eulerAngles = __instance._cameraAnchor.rotation.eulerAngles;
            __instance.VerticalRotation = eulerAngles.x;
            __instance.HorizontalRotation = eulerAngles.y;
            __instance.RollRotation = eulerAngles.z;
            __instance.CameraPosition = __instance._cameraAnchor.position;
            return false;
        }
    }

    /* If you want to see how often your sending player positions, uncomment this patch and it will print it to the console every second, though its based on how often GetNewSyncData is called so you might want to change things
    public static int count = 0;
    public static Stopwatch stopwatch = new Stopwatch();
    [HarmonyPatch(typeof(FpcServerPositionDistributor))]
    internal class TestPatch32
    {
        [HarmonyPatch(nameof(FpcServerPositionDistributor.GetNewSyncData))]
        public static void Postfix(ElevatorFollowerBase __instance)
        {
            if (!stopwatch.IsRunning) stopwatch.Start();
            count++;
            if (stopwatch.ElapsedMilliseconds >= 1000)
            {
                Log.Debug("Rate: " + count/(stopwatch.ElapsedMilliseconds/1000) + " - " + FpcServerPositionDistributor.SendRate);
                count = 0;
                stopwatch.Restart();
            }
        }
    }
    */
}