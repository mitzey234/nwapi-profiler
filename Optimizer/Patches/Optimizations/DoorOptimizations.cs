using Footprinting;
using Interactables.Interobjects;
using Interactables.Interobjects.DoorUtils;
using Utils.NonAllocLINQ;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace Optimizer.Patches.Optimizations;

[HarmonyPatch(typeof(PryableDoor))]
public static class PryableDoorPatch {
    [HarmonyPatch(nameof(PryableDoor.TryPryGate))]
    [HarmonyPrefix]
    public static bool TryOverridePosition(PryableDoor __instance, ReferenceHub player)
    {
        Plugin.StartDoor(__instance);
        return true;
    }
}

[HarmonyPatch(typeof(DoorEvents))]
public static class DoorEventsPatch
{
    [HarmonyPatch(nameof(DoorEvents.TriggerAction))]
    [HarmonyPrefix]
    public static bool TriggerAction(DoorVariant variant, DoorAction action, ReferenceHub user)
    {
        Plugin.StartDoor(variant);
        return true;
    }
}

[HarmonyPatch(typeof(BreakableDoor))]
public static class BreakableDoorPatch {
    [HarmonyPatch(nameof(BreakableDoor.ServerDamage))]
    [HarmonyPrefix]
    public static bool ServerDamage(BreakableDoor __instance, float hp, DoorDamageType type, Footprint attacker)
    {
        Plugin.StartDoor(__instance);
        return true;
    }
}
    
[HarmonyPatch(typeof(DoorVariant))]
public static class DoorVariantPatch {
    public static Dictionary<DoorVariant, float> doortimes = new Dictionary<DoorVariant, float>();

    public static HashSet<DoorVariant> ignore = new HashSet<DoorVariant>();
    
    public static HashSet<DoorVariant> subIgnore = new HashSet<DoorVariant>();
    
    [HarmonyPatch(nameof(DoorVariant.ServerInteract))]
    [HarmonyPrefix]
    public static bool ServerInteract(DoorVariant __instance)
    {
        if (__instance is ElevatorDoor) return true;
        Plugin.StartDoor(__instance);
        return true;
    }
    
    [HarmonyPatch(nameof(DoorVariant.Update))]
    [HarmonyPrefix]
    public static bool Update(DoorVariant __instance)
    {
        if (ignore.Contains(__instance) || subIgnore.Contains(__instance)) return true;
        if (__instance is ElevatorDoor or CheckpointDoor)
        {
            ignore.Add(__instance);
            return true;
        }
        if (!doortimes.ContainsKey(__instance)) doortimes[__instance] = 7f;
        doortimes[__instance] -= Time.deltaTime;
        //Log.Debug(doortimes[__instance] + " - " + __instance.name + __instance.GetType());
        if (doortimes[__instance] <= 0)
        {
            if (__instance.name.Contains("PortallessBreakableDoor") || ignore.Count(d => (d.transform.position - __instance.transform.position).sqrMagnitude <= 9) > 0)
            {
                subIgnore.Add(__instance);
                return true;
            }
            __instance.enabled = false;
            doortimes[__instance] = 0f;
        }
        return true;
    }
}
