using System.Collections.Generic;
using HarmonyLib;
using PlayerStatsSystem;
using UnityEngine;

namespace Optimizer.Patches;

[HarmonyPatch(typeof(BreakableWindow))]
public static class BreakableWindowPatch {
    public static Dictionary<BreakableWindow, float> times = new Dictionary<BreakableWindow, float>();
    
    [HarmonyPatch(nameof(BreakableWindow.Damage))]
    [HarmonyPrefix]
    public static bool Damage(BreakableWindow __instance, float damage, DamageHandlerBase handler, Vector3 pos)
    {
        times[__instance] = 7.0f;
        __instance.enabled = true;
        return true;
    }
    
    [HarmonyPatch(nameof(BreakableWindow.Update))]
    [HarmonyPrefix]
    public static bool Update(BreakableWindow __instance)
    {
        if (!times.ContainsKey(__instance)) times[__instance] = 7f;
        times[__instance] -= Time.deltaTime;
        if (times[__instance] <= 0)
        {
            __instance.enabled = false;
            times[__instance] = 0f;
        }
        return true;
    }
}
