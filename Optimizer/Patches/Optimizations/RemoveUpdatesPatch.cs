using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using PluginAPI.Core;

using HarmonyLib;

using static HarmonyLib.AccessTools;
using CursorManagement;
using System.Reflection.Emit;
using NorthwoodLib.Pools;
using Optimizer;

namespace GameOptimizer.Patches
{
	/// <summary>
	/// Removes unnecessary <see cref="StaticUnityMethods"/> updates from <see cref="FactoryManager"/>.
	/// </summary>
	[HarmonyPatch]
	internal static class RemoveUpdatesPatch
	{
		[HarmonyTargetMethods]
		public static IEnumerable<MethodBase> TargetMethods()
		{
			yield return Method(typeof(FactoryManager), nameof(FactoryManager.OnUpdate));
			yield return Method(typeof(FactoryManager), nameof(FactoryManager.OnFixedUpdate));
			yield return Method(typeof(FactoryManager), nameof(FactoryManager.OnLateUpdate));
		}

		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase method)
		{
			Label notLoadedLabel = generator.DefineLabel();

			return new CodeInstruction[]
			{
				new CodeInstruction(OpCodes.Ldnull),
				new CodeInstruction(OpCodes.Ldftn, (MethodInfo)method),
				new CodeInstruction(OpCodes.Newobj, Constructor(typeof(Action), new Type[] { typeof(object), typeof(IntPtr) })),
				new CodeInstruction(OpCodes.Call, Method(typeof(StaticUnityMethods), "remove_" + method.Name)),
				new CodeInstruction(OpCodes.Ret).WithLabels(notLoadedLabel)
			};
		}
	}
}
