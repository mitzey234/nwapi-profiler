namespace CustomProfiler.Patches.Optimizations;

using CustomPlayerEffects;
using CustomProfiler.Extensions;
using HarmonyLib;
using InventorySystem.Items.Armor;
using InventorySystem.Items.Firearms.Attachments;
using InventorySystem.Items.Pickups;
using InventorySystem.Items.Usables.Scp244;
using MapGeneration.Distributors;
using PlayerRoles.Voice;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using UnityEngine;
using VoiceChat.Networking;
using static HarmonyLib.AccessTools;

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
}
