﻿using CameraShaking;
using GameCore;
using InventorySystem.Items.Firearms;
using WaterPhysics;

namespace Optimizer.Patches;

using FacilitySoundtrack;
using HarmonyLib;
using Interactables.Interobjects.DoorUtils;
using PlayerRoles.FirstPersonControl.Thirdperson;
using PlayerRoles.Ragdolls;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using static HarmonyLib.AccessTools;

/// <summary>
/// This patch automatically forces behaviours to disable themselves.
/// </summary>
[HarmonyPatch]
public static class DisableSelfPatch
{
    private static IEnumerable<MethodInfo> TargetMethods()
    {
        yield return Method(typeof(DoorNametagExtension), "FixedUpdate");
        yield return Method(typeof(ZoneAmbientSoundtrack), "UpdateVolume");
        yield return Method(typeof(SoundtrackManager), "Update");
        yield return Method(typeof(MainCameraController), "LateUpdate");
        yield return Method(typeof(AnimatedCharacterModel), "Update"); //This may or may not break things, it seems ok tho
        yield return Method(typeof(BasicRagdoll), "Update");
        yield return Method(typeof(DynamicRagdoll), "Update");
        yield return Method(typeof(AlphaWarheadNukesitePanel), "Update");
        yield return Method(typeof(AlphaWarheadOutsitePanel), "Update");
        yield return Method(typeof(WorldmodelLaserExtension), "LateUpdate");
        yield return Method(typeof(BuoyantWater), "FixedUpdate");
        yield return Method(typeof(CameraShakeController), "LateUpdate");
        yield return Method(typeof(Console), "Update");
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator generator)
    {
        return new CodeInstruction[]
        {
            // this.enabled = false;
            // return;
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldc_I4_0),
            new(OpCodes.Call, PropertySetter(typeof(Behaviour), nameof(Behaviour.enabled))),
            new(OpCodes.Ret),
        };
    }
}