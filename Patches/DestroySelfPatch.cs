namespace CustomProfiler.Patches;

using HarmonyLib;
using InventorySystem.Items.Firearms;
using PlayerRoles.PlayableScps.HumeShield;
using PlayerRoles.PlayableScps.Scp939.Mimicry;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using VoiceChat;
using static HarmonyLib.AccessTools;
using UnityEngine.Rendering.HighDefinition;
using FacilitySoundtrack;

/// <summary>
/// This patch automatically forces objects to destroy themselves.
/// </summary>
/// <remarks>Note that the <see cref="GameObject"/> any component is attached to will not be destroyed, instead the component itself will.</remarks>
[HarmonyPatch]
public static class DestroySelfPatch
{
    private static IEnumerable<MethodInfo> TargetMethods()
    {
        yield return Method(typeof(MainCameraController), "LateUpdate");
        yield return Method(typeof(LiquidWobble), "Start");
        yield return Method(typeof(FirearmWorldmodelLaser), "Awake");
        yield return Method(typeof(EnvMimicryStandardButton), "Awake");
        yield return Method(typeof(HumeShieldBarController), "Awake");
        yield return Method(typeof(VoiceChatMicCapture), "Awake");
        yield return Method(typeof(Scp173InsanitySoundtrack), "Awake");
        yield return Method(typeof(StatusBar), "Update");
        yield return Method(typeof(HDAdditionalLightData), "Awake");
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator generator)
    {
        return new CodeInstruction[]
        {
            // UnityEngine.Object.Destroy(this, 0f);
            // return;
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldc_R4, 0f),
            new(OpCodes.Call, Method(typeof(Object), nameof(Object.Destroy), [typeof(Object), typeof(float)])),
            new(OpCodes.Ret),
        };
    }
}
