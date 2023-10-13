namespace CustomProfiler.Patches.Optimizations;

using CustomPlayerEffects;
using FacilitySoundtrack;
using HarmonyLib;
using Interactables.Interobjects.DoorUtils;
using InventorySystem;
using InventorySystem.Items.Armor;
using InventorySystem.Items.Firearms;
using InventorySystem.Items.Firearms.Attachments;
using InventorySystem.Items.Firearms.BasicMessages;
using InventorySystem.Items.Pickups;
using InventorySystem.Items.Usables.Scp244;
using MapGeneration.Distributors;
using Mirror;
using PlayerRoles.FirstPersonControl.Thirdperson;
using PlayerRoles.Ragdolls;
using PlayerRoles.Voice;
using PluginAPI.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection.Emit;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using VoiceChat;
using VoiceChat.Networking;
using static HarmonyLib.AccessTools;
using CustomProfiler.Extensions;

[HarmonyPatch]
internal class BasicStuff
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

    //Disable updater, replace with transpiler
    [HarmonyPatch(typeof(VoiceModuleBase), "Update")]
    internal class TestPatch26
    {
        public static bool Prefix(VoiceModuleBase __instance)
        {
            if (__instance.enabled) __instance.enabled = false;
            return true;
        }
    }

    //This is the transpiler, it replaces the updater with a call to the module's update method
    //This may or may not be better since VoiceTransceiver.ServerReceiveMessage seems to fire faster than the update method
    [HarmonyPatch(typeof(VoiceTransceiver))]
    public static class TestPatch27
    {
        [HarmonyTranspiler]
        [HarmonyPatch(nameof(VoiceTransceiver.ServerReceiveMessage))]
        private static IEnumerable<CodeInstruction> VoiceTransceiverServerReceiveMessage_Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator generator)
        {
            instructions.BeginTranspiler(out List<CodeInstruction> newInstructions);

            int index = newInstructions.FindIndex(x => x.opcode == OpCodes.Callvirt && x.Calls(Method(typeof(VoiceModuleBase), nameof(VoiceModuleBase.ValidateReceive))));
            int getModuleIndex = index;
            int loadVarIndex = index;
            bool found = false;
            while (getModuleIndex > 0 && !found) {
                if (newInstructions[getModuleIndex].Calls(PropertyGetter(typeof(IVoiceRole), nameof(IVoiceRole.VoiceModule))))
                {
                    found = true;
                    break;
                }
                getModuleIndex--;
            }
            if (found) loadVarIndex = getModuleIndex - 1;
            else throw new Exception("Could not find the variable loader");

            newInstructions.InsertRange(index+1, new CodeInstruction[]
            {
                new(newInstructions[loadVarIndex].opcode, newInstructions[loadVarIndex].operand),
                new(newInstructions[getModuleIndex].opcode, newInstructions[getModuleIndex].operand),
                new(OpCodes.Callvirt, typeof(VoiceModuleBase).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic, null, CallingConventions.Any, new Type[] { }, null)),
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
            if (time <= 0.25f) return false;
            time = 0;
            return true;
        }
    }
}
