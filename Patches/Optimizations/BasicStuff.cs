namespace CustomProfiler.Patches.Optimizations;

using CustomPlayerEffects;
using HarmonyLib;
using InventorySystem;
using InventorySystem.Items.Armor;
using InventorySystem.Items.Firearms;
using InventorySystem.Items.Firearms.BasicMessages;
using InventorySystem.Items.Pickups;
using InventorySystem.Items.Usables.Scp244;
using Mirror;
using System;
using System.Collections.Generic;
using UnityEngine;

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
            inv.Update();
        }
    }

    //Force updating for inventory system
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.UserCode_CmdDropItem__UInt16__Boolean))]
    class TestPatch9
    {
        public static void Postfix(Inventory __instance)
        {
            __instance.Update();
        }
    }

    //Force updating for inventory system
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.NetworkCurItem))]
    [HarmonyPatch(MethodType.Setter)]
    class TestPatch10
    {
        public static void Postfix(Inventory __instance)
        {
            __instance.Update();
        }
    }

    //Force updating for inventory system
    [HarmonyPatch(typeof(InventoryExtensions), nameof(InventoryExtensions.ServerRemoveItem))]
    class TestPatch11
    {
        public static void Postfix(Inventory inv)
        {
            inv.Update();
        }
    }

    //Force updating for inventory system
    [HarmonyPatch(typeof(InventoryExtensions), nameof(InventoryExtensions.ServerDropAmmo))]
    class TestPatch12
    {
        public static void Postfix(Inventory inv)
        {
            inv.Update();
        }
    }

    //Force updating for inventory system
    [HarmonyPatch(typeof(InventoryExtensions), nameof(InventoryExtensions.ServerSetAmmo))]
    class TestPatch13
    {
        public static void Postfix(Inventory inv)
        {
            inv.Update();
        }
    }

    //Force updating for inventory system
    [HarmonyPatch(typeof(InventoryExtensions), nameof(InventoryExtensions.ServerSetAmmo))]
    class TestPatch14
    {
        public static void Postfix(Inventory inv)
        {
            inv.Update();
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
            referenceHub.inventory.Update();
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
            }
            catch (Exception e)
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

    [HarmonyPatch(typeof(ItemPickupBase), "OnDestroy")]
    internal class TestPatch19
    {
        public static void Postfix(ItemPickupBase __instance)
        {
            __instance.PhysicsModuleSyncData.Clear();
            __instance.syncObjects.Clear();
        }
    }
}
