using Footprinting;
using InventorySystem.Items.MicroHID;
using InventorySystem.Items.ThrowableProjectiles;
using Mirror;
using Utils;
using CustomPlayerEffects;
using Optimizer.Extensions;
using Elevators;
using HarmonyLib;
using InventorySystem.Items.Armor;
using InventorySystem.Items.Firearms.Attachments;
using InventorySystem.Items.Pickups;
using InventorySystem.Items.Usables.Scp244;
using MapGeneration.Distributors;
using PlayerRoles.FirstPersonControl.NetworkMessages;
using PlayerRoles.PlayableScps.Scp079;
using PlayerRoles.PlayableScps.Scp079.Cameras;
using PlayerRoles.Voice;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using UnityEngine;
using Utils.NonAllocLINQ;
using VoiceChat.Networking;
using static HarmonyLib.AccessTools;

namespace Optimizer.Patches.Optimizations;

[HarmonyPatch]
internal class BasicStuff
{

	//This patch will capture all explosions and ensure that every tick is limited to a certain amount of explosions
	//Also ensures that only so many item pickups can have forces applied to them in a single tick
	[HarmonyPatch(typeof(ExplosionGrenade))]
	internal class Grenades
	{
		[HarmonyPatch(nameof(ExplosionGrenade.Explode))]
		[HarmonyPrefix]
		public static bool Explode(Footprint attacker, Vector3 position, ExplosionGrenade settingsReference)
		{
			if (GrenadeTamer.queue.FindIndex(r => r.Equals(attacker, position, settingsReference)) == -1)
			{
				GrenadeTamer.RegisterExplosion(attacker, position, settingsReference);
				return false;
			}
			return true;
		}
		
		[HarmonyPatch(nameof(ExplosionGrenade.ExplodeRigidbody))]
		[HarmonyPrefix]
		public static bool ExplodeRigidbody(Rigidbody rb, Vector3 pos, float radius, ExplosionGrenade setts)
		{
			PhysicsTamer.RegisterExplosion(rb, pos, radius, setts);
			return false;
		}
	}
	
	//This allows the optimizer to make sure that clientside explosion effects are rendered at the correct frame
	[HarmonyPatch(typeof(ExplosionUtils))]
	internal class GrenadeEffects
	{
		[HarmonyPatch(nameof(ExplosionUtils.ServerSpawnEffect))]
		public static bool Prefix(Vector3 pos)
		{
			if (!GrenadeTamer.queue.TryGetFirst(r => r.position.Equals(pos), out Details d) || !d.ready) return false;
			return true;
		}
	}
	
	//This delays the time it takes for unactivated grenades to become activated from nearby explosions by specific amounts of time depending on physics
	//It also makes sure at least 1 frame has passed since the physics has been applied
	[HarmonyPatch(typeof(TimedGrenadePickup))]
	internal class GrenadeChainDelay
	{

		public static Dictionary<TimedGrenadePickup, int> instances = new Dictionary<TimedGrenadePickup, int>();
		
		[HarmonyPatch(nameof(TimedGrenadePickup.Update))]
		public static bool Prefix(TimedGrenadePickup __instance)
		{
			if (__instance._replaceNextFrame && __instance.PhysicsModule is PickupStandardPhysics)
			{
				if (PhysicsTamer.queue.TryGetValue((__instance.PhysicsModule as PickupStandardPhysics).Rb, out PhysicsDetails d) && d.ready == false) return false;
				if (!instances.ContainsKey(__instance)) instances.Add(__instance, 0);
				instances[__instance]++;
				if (instances[__instance] < 2) return false;
				instances.Remove(__instance);
				return true;
			}
			return true;
		}
	}
	
	
	//Scp244 has a very intensive update, this limits it to 2 times a second
	[HarmonyPatch(typeof(Scp244DeployablePickup))]
	internal class Scp244Optimizer
	{
		public sealed class FloatValue
		{
			public float Time;
		}

		public static ConditionalWeakTable<Scp244DeployablePickup, FloatValue> timers = new ConditionalWeakTable<Scp244DeployablePickup, FloatValue>();

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
	internal class InsufficientLightingLimiter
	{
		internal static List<InsufficientLighting> disabled = new List<InsufficientLighting>();

		public static bool Prefix(InsufficientLighting __instance)
		{
			if (!disabled.Contains(__instance)) disabled.Add(__instance);
			StaticUnityMethods.OnUpdate -= __instance.AlwaysUpdate;
			return true;
		}
	}

	//Cleanup
	[HarmonyPatch(typeof(InsufficientLighting), nameof(InsufficientLighting.OnDestroy))]
	internal class InsufficientLightingCleanup
	{
		public static bool Prefix(InsufficientLighting __instance)
		{
			InsufficientLightingLimiter.disabled.Remove(__instance);
			return true;
		}
	}

	//Help clean up items
	[HarmonyPatch(typeof(ItemPickupBase), "OnDestroy")]
	internal class PickupMemoryCleaner
	{
		public static void Prefix(ItemPickupBase __instance)
		{
			if (!NetworkServer.active) return;
			__instance.PhysicsModuleSyncData.Clear();
			__instance.syncObjects.Clear();
		}
	}

	//Prevent this method when its not needed
	[HarmonyPatch(typeof(BodyArmorPickup), "OnTriggerStay")]
	internal class BodyArmorTiggerLimiter
	{
		public static bool Prefix(BodyArmorPickup __instance)
		{
			if (__instance._rb.IsSleeping()) return false;
			else return true;
		}
	}

	//Disable workstation controller when its not actually active
	[HarmonyPatch(typeof(WorkstationController))]
	internal class WorkstationControllerOptimizer
	{

		[HarmonyPrefix]
		[HarmonyPatch(nameof(WorkstationController.Update))]
		public static bool Update(WorkstationController __instance)
		{
			if (__instance.Status == 0)
			{
				__instance.enabled = false;
				return false;
			}
			else return true;
		}

		[HarmonyPostfix]
		[HarmonyPatch(nameof(WorkstationController.ServerInteract))]
		public static void ServerInteract(WorkstationController __instance)
		{
			__instance.enabled = true;
		}
	}

	//Disable Scp079Generator controller when its not actually active
	[HarmonyPatch(typeof(Scp079Generator))]
	internal class GeneratorControllerOptimizer
	{
		public static Dictionary<Scp079Generator, float> instances = new Dictionary<Scp079Generator, float>();

		[HarmonyPrefix]
		[HarmonyPatch(nameof(Scp079Generator.Update))]
		public static bool Update(Scp079Generator __instance)
		{
			if (!instances.ContainsKey(__instance)) instances.Add(__instance, Time.timeSinceLevelLoad);
			if (__instance.Engaged || !__instance.Activating)
			{
				__instance.enabled = false;
				float timeDelta = Time.timeSinceLevelLoad - instances[__instance];
				if (__instance._currentTime != 0f && __instance._currentTime < __instance._totalActivationTime)
				{
					__instance._currentTime -= __instance.DropdownSpeed * timeDelta;
				}
				__instance._currentTime = Mathf.Clamp(__instance._currentTime, 0f, __instance._totalActivationTime);
				
				int num = Mathf.FloorToInt(__instance._totalActivationTime - __instance._currentTime);
				if (num != (int)__instance._syncTime)
				{
					__instance.Network_syncTime = (short)num;
				}
				instances[__instance] = Time.timeSinceLevelLoad;
				return false;
			}
			else
			{
				instances[__instance] = Time.timeSinceLevelLoad;
				return true;
			}
		}

		[HarmonyPostfix]
		[HarmonyPatch(nameof(Scp079Generator.ServerInteract))]
		public static void ServerInteract(Scp079Generator __instance)
		{
			__instance.enabled = true;
		}
		
		[HarmonyPostfix]
		[HarmonyPatch(nameof(Scp079Generator.OnDestroy))]
		public static void Destroy(Scp079Generator __instance)
		{
			instances.Remove(__instance);
		}
	}

	//Relocate the update method to be after rate limit checking
	[HarmonyPatch]
	public static class VoiceRecieverOptimization
	{
		// We dont call update inside of a foreach loop.
		// We call after the ratelimit check.

		[HarmonyTranspiler]
		[HarmonyPatch(typeof(VoiceTransceiver), nameof(VoiceTransceiver.ServerReceiveMessage))]
		private static IEnumerable<CodeInstruction> VoiceTransceiverServerReceiveMessage_Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator generator)
		{
			instructions.BeginTranspiler(out List<CodeInstruction> newInstructions);

			int index = newInstructions.FindIndex(x => x.Calls(Method(typeof(VoiceModuleBase), nameof(VoiceModuleBase.CheckRateLimit)))) + 1;
			
			newInstructions.InsertRange(index, new[]
			{
                // this.enabled = true;
                new CodeInstruction(OpCodes.Ldloc_0),
				new CodeInstruction(OpCodes.Callvirt, PropertyGetter(typeof(IVoiceRole), nameof(IVoiceRole.VoiceModule))),
				new CodeInstruction(OpCodes.Ldc_I4_1),
				new CodeInstruction(OpCodes.Call, PropertySetter(typeof(Behaviour), nameof(Behaviour.enabled))),
			});

			return newInstructions.FinishTranspiler();
		}
		
		[HarmonyTranspiler]
		[HarmonyPatch(typeof(VoiceModuleBase), nameof(VoiceModuleBase.Update))]
		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator generator)
		{
			instructions.BeginTranspiler(out List<CodeInstruction> newInstructions);
			int index = 0;

			Label beginLabel = generator.DefineLabel();

			newInstructions.InsertRange(index, new[]
			{
				// if (this._sentPackets > this._prevSent) go to beginLabel
				new CodeInstruction(OpCodes.Ldfld, Field(typeof(VoiceModuleBase), nameof(VoiceModuleBase._sentPackets))),
				new CodeInstruction(OpCodes.Ldarg_0),
				new CodeInstruction(OpCodes.Ldfld, Field(typeof(VoiceModuleBase), nameof(VoiceModuleBase._prevSent))),
				new CodeInstruction(OpCodes.Bgt_S, beginLabel),

				//if (VoiceModuleBase.ServerIsSending == true) go to beginLabel
				new CodeInstruction(OpCodes.Ldarg_0),
				new CodeInstruction(OpCodes.Call, PropertyGetter(typeof(VoiceModuleBase), nameof(VoiceModuleBase.ServerIsSending))),
				new CodeInstruction(OpCodes.Brtrue_S, beginLabel),

				// this.enabled = false;
				new CodeInstruction(OpCodes.Ldarg_0),
				new CodeInstruction(OpCodes.Ldc_I4_0),
				new CodeInstruction(OpCodes.Call, PropertySetter(typeof(Behaviour), nameof(Behaviour.enabled))),
			});

			return newInstructions.FinishTranspiler();
		}
	}

	//Slow down ServerRole updates
	[HarmonyPatch(typeof(ServerRoles))]
	internal class ServerRoleLimiter
	{
		public sealed class FloatValue
		{
			public float Time;
		}

		public static ConditionalWeakTable<ServerRoles, FloatValue> timers = new ConditionalWeakTable<ServerRoles, FloatValue>();

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
	internal class ElevatorOptimizer
	{
		[HarmonyPatch(nameof(ElevatorFollowerBase.OnElevatorMoved))]
		public static void Postfix(ElevatorFollowerBase __instance)
		{
			if (!__instance.enabled) __instance.enabled = true;
			if (!__instance.InElevator || __instance.TrackedChamber.CurSequence == Interactables.Interobjects.ElevatorChamber.ElevatorSequence.Ready)
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
	class PlayerPositionUpdateLimiter
	{
		public static bool Prefix(ref float __result)
		{
			__result = 1f / Mathf.Clamp(Plugin.PluginConfig.PlayerPositionUpdateRate, 10, 60); ;
			return false;
		}
	}

	//For some reason Northwood's code will update all of the angles for every single camera in the facility regardless of whether its active or not
	//This patch makes it so it will only do that when the camera is active, saving at least a little performance
	//If this breaks something (which I don't think it will) feel free to delete this whole file.
	[HarmonyPatch(typeof(Scp079Camera), "Update")]
	internal class CameraOptimizer
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

	public class Details
	{
		public Footprint attacker;
		public Vector3 position;
		public ExplosionGrenade settingsReference;
		public bool ready = false;

		public Details(Footprint attacker, Vector3 position, ExplosionGrenade settingsReference)
		{
			this.attacker = attacker;
			this.position = position;
			this.settingsReference = settingsReference;
		}

		public bool Equals(Footprint a, Vector3 p, ExplosionGrenade r)
		{
			return settingsReference.GetHashCode() == r.GetHashCode() && p.Equals(position) &&
			       a.GetHashCode() == attacker.GetHashCode();
		}
	}

	public class PhysicsDetails
	{
		public Rigidbody rb;
		public Vector3 pos;
		public float radius;
		public ExplosionGrenade setts;
		public bool ready = false;

		public PhysicsDetails(Rigidbody rb, Vector3 pos, float radius, ExplosionGrenade setts)
		{
			this.rb = rb;
			this.pos = pos;
			this.radius = radius;
			this.setts = setts;
		}
	}
	
	public class PhysicsTamer : MonoBehaviour
	{
		public static Dictionary<Rigidbody, PhysicsDetails> queue = new Dictionary<Rigidbody, PhysicsDetails>();

		private static readonly int _range = Plugin.PluginConfig.MaxPhysicsRange * Plugin.PluginConfig.MaxPhysicsRange;
        
		public static void RegisterExplosion(Rigidbody rb, Vector3 pos, float radius, ExplosionGrenade setts)
		{
			if ((rb.gameObject.transform.position - pos).sqrMagnitude > _range) return;
			queue.Remove(rb);
			queue.Add(rb, new PhysicsDetails(rb, pos, radius, setts));
		}
		
		public static void process(Rigidbody rb, Vector3 pos, float radius, ExplosionGrenade setts)
		{
			if (rb.isKinematic)
			{
				return;
			}
			if (Physics.Linecast(rb.gameObject.transform.position, pos, MicroHIDItem.WallMask))
			{
				return;
			}
			float num = Mathf.Clamp01(Mathf.InverseLerp(0.5f, 10f, rb.mass)) * 3f + 1f;
			rb.AddExplosionForce((setts._rigidbodyBaseForce / num)*1.5f, pos, radius, setts._rigidbodyLiftForce / num, ForceMode.VelocityChange);
		}

		void Update()
		{
			int count = 0;
			while (queue.Count > 0 && count < Plugin.PluginConfig.MaxExplosionPhysicsPerTick)
			{
				count++;
				PhysicsDetails g = queue.First().Value;
				if (!g.rb.Equals(null)) process(g.rb, g.pos, g.radius, g.setts);
				g.ready = true;
				queue.Remove(g.rb);
			}
			//if (count > 0) Log.Debug("Processed: " + count + " Bodies of " + queue.Count);
		}
	}
	
	public class GrenadeTamer : MonoBehaviour
    {
        public static List<Details> queue = new List<Details>();
        
        public static void RegisterExplosion(Footprint attacker, Vector3 position, ExplosionGrenade settingsReference)
        {
	        queue.Add(new Details(attacker, position, settingsReference));
        }

        private int _frames = 0;

        void Update()
        {
	        if (_frames > 0)
	        {
		        _frames--;
		        return;
	        }
	        int count = 0;
	        while (queue.Count > 0 && count < Plugin.PluginConfig.MaxExplosionsPerTick)
	        {
		        count++;
		        Details g = queue.Find(r => true);
		        g.ready = true;
		        ExplosionUtils.ServerSpawnEffect(g.position, ItemType.GrenadeHE);
		        ExplosionGrenade.Explode(g.attacker, g.position, g.settingsReference, ExplosionType.Grenade);
		        queue.Remove(g);
	        }
	        if (count > 0) _frames++; //This will make it so at least 1 frame passes before we render more explosions
	        //if (count > 0) Log.Debug("Processed: " + count + " Grenades of " + queue.Count);
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