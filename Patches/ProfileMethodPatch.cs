namespace CustomProfiler.Patches;

using CustomProfiler.API;
using CustomProfiler.Extensions;
using HarmonyLib;
using PluginAPI.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using static CustomProfiler.Extensions.TranspilerExtensions;
using static HarmonyLib.AccessTools;

public static class ProfileMethodPatch
{
    public const int MaxPatches = 4000;

    internal static bool DisableProfiler = false;

    private static ProfiledMethodInfo[] ProfilerInfos = new ProfiledMethodInfo[MaxPatches];

    private static readonly HarmonyMethod ProfilerTranspiler = new(typeof(ProfileMethodPatch), nameof(Transpiler));

    private static HashSet<int> OptimizedMethods;

    internal static IEnumerable<AsRef<ProfiledMethodInfo>> GetProfilerInfos()
    {
        int maxIndex = ProfiledMethodsTracker.MaxIndex;

        for (int i = 0; i < maxIndex; i++)
        {
            yield return new(ref ProfilerInfos[i]);
        }
    }

    internal static void ApplyProfiler(MethodBase method)
    {
        if (!ProfiledMethodsTracker.AddMethod(method))
        {
            throw new Exception("Failed to add method.");
        }

        OptimizedMethods ??= new(CustomProfilerPlugin.HarmonyOptimizations.GetPatchedMethods().Select(x => x.GetHashCode()).Distinct());

        if (OptimizedMethods.Contains(method.GetHashCode()))
        {
            return;
        }

        if (method.GetMethodBody() == null)
        {
            throw new ArgumentException("Cannot patch a method without a body.");
        }

        if (method.DeclaringType?.IsGenericType ?? false)
        {
            if (method.DeclaringType.ContainsGenericParameters)
            {
                throw new ArgumentException($"Cannot patch method with a generic declaring type that is not constructed.");
            }
        }

        Type baseType = method.DeclaringType?.BaseType;

        while (baseType != null)
        {
            if (baseType.IsGenericType)
            {
                if (baseType.ContainsGenericParameters)
                {
                    throw new ArgumentException($"Cannot patch generic method within a declaring type deriving from a generic type that is not constructed.");
                }
            }

            baseType = baseType.BaseType;
        }

        CustomProfilerPlugin.Harmony.Patch(method, prefix: null, postfix: null, transpiler: ProfilerTranspiler, finalizer: null);
    }

    private static unsafe IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator generator)
    {
        if (!ProfiledMethodsTracker.GetMethodIndex(method, out int methodIndex))
        {
            Log.Error("Could not locate method index?");
            return instructions;
        }

        instructions.BeginTranspiler(out List<CodeInstruction> newInstuctions);

        // This is a method that does not return.
        // Likely throws an exception.
        if (!newInstuctions.Any(x => x.opcode == OpCodes.Ret))
            return instructions;

        // long startTimestamp;
        LocalBuilder startTimestamp = generator.DeclareLocal(typeof(long));

        Label profilerDisabledLabel = generator.DefineLabel();

        newInstuctions[0].labels.Add(profilerDisabledLabel);

        // We get the starting timestamp recorded by the timer mechanism
        // and store it into a variable we can use later.
        newInstuctions.InsertRange(0, new CodeInstruction[]
        {
            // if (!ProfileMethodPatch.DisableProfiler)
            // {
            //     long startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            // }
            new(OpCodes.Ldsfld, Field(typeof(ProfileMethodPatch), nameof(DisableProfiler))),
            new(OpCodes.Brtrue_S, profilerDisabledLabel),
            new(OpCodes.Call, Method(typeof(Stopwatch), nameof(Stopwatch.GetTimestamp))),
            new(OpCodes.Stloc_S, startTimestamp),
        });

        int index = newInstuctions.FindLastIndex(x => x.opcode == OpCodes.Ret);

        Label returnIfProfilerDisabled = generator.DefineLabel();
        Label doProfilerCheck = generator.DefineLabel();
        Label shouldAssign = generator.DefineLabel();
        List<Label> originalReturnLabels = newInstuctions[index].ExtractLabels();

        newInstuctions[index].labels.Add(returnIfProfilerDisabled);

        // if (!ProfileMethodPatch.DisableProfiler)
        // {
        //     long totalTicks = Stopwatch.GetTimestamp() - startTimestamp;
        //
        //     ref ProfiledMethodInfo profilerInfo = ref ProfileMethodPatch.ProfilerInfos[methodIndex];
        //
        //     profilerInfo.InvocationCount++;
        //     profilerInfo.TotalTicks += (uint)totalTicks;
        //
        //     if (profilerInfo.MaxTicks < (uint)totalTicks)
        //     {
        //         profilerInfo.MaxTicks = (uint)totalTicks;
        //     }
        // }
        newInstuctions.InsertRange(index, new CodeInstruction[]
        {
            new CodeInstruction(OpCodes.Ldsfld, Field(typeof(ProfileMethodPatch), nameof(DisableProfiler)))
                .WithLabels(doProfilerCheck).WithLabels(originalReturnLabels),
            new(OpCodes.Brtrue_S, returnIfProfilerDisabled),

            // long totalTicks = Stopwatch.GetTimestamp() - startTimestamp;
            new(OpCodes.Call, Method(typeof(Stopwatch), nameof(Stopwatch.GetTimestamp))),
            new(OpCodes.Ldloc_S, startTimestamp),
            new(OpCodes.Sub),
            new(OpCodes.Stloc_S, startTimestamp),

            // ref ProfiledMethodInfo profilerInfo = ref ProfileMethodPatch.ProfilerInfos[methodIndex];
            new(OpCodes.Ldsfld, Field(typeof(ProfileMethodPatch), nameof(ProfilerInfos))),
            new(OpCodes.Ldc_I4, methodIndex),
            new(OpCodes.Ldelema, typeof(ProfiledMethodInfo)),
            new(OpCodes.Dup),

            // profilerInfo.InvocationCount++;
            new(OpCodes.Ldflda, Field(typeof(ProfiledMethodInfo), nameof(ProfiledMethodInfo.InvocationCount))),
            new(OpCodes.Dup),
            new(OpCodes.Ldind_U4),
            new(OpCodes.Ldc_I4_1),
            new(OpCodes.Add),
            new(OpCodes.Stind_I4),

            // profilerInfo.TotalTicks += (uint)totalTicks;
            new(OpCodes.Dup),
            new(OpCodes.Ldflda, Field(typeof(ProfiledMethodInfo), nameof(ProfiledMethodInfo.TotalTicks))),
            new(OpCodes.Dup),
            new(OpCodes.Ldind_U4),
            new(OpCodes.Ldloc_S, startTimestamp),
            new(OpCodes.Conv_U4),
            new(OpCodes.Add),
            new(OpCodes.Stind_I4),

            // if (profilerInfo.MaxTicks < (uint)totalTicks)
            // {
            //     profilerInfo.MaxTicks = (uint)totalTicks;
            // }
            new(OpCodes.Ldflda, Field(typeof(ProfiledMethodInfo), nameof(ProfiledMethodInfo.MaxTicks))),// [uint&] MaxTicks
            new(OpCodes.Dup), // [uint&] MaxTicks | [uint&] MaxTicks
            new(OpCodes.Ldind_U4), // [uint&] MaxTicks | [uint] MaxTicks
            new(OpCodes.Ldloc_S, startTimestamp), // [uint&] MaxTicks | [uint] MaxTicks | [long] total
            new(OpCodes.Conv_U4), // [uint&] MaxTicks | [uint] MaxTicks | [uint] total
            new(OpCodes.Blt_Un_S, shouldAssign), // [uint&] MaxTicks
            new(OpCodes.Pop), //
            new(OpCodes.Ret),
            new CodeInstruction(OpCodes.Ldloc_S, startTimestamp).WithLabels(shouldAssign),// [uint&] MaxTicks | [long] total
            new(OpCodes.Conv_U4),// // [uint&] MaxTicks | [uint] total
            new(OpCodes.Stind_I4),//
        });

        CodeInstruction profilerCheckBegin = newInstuctions[index];

        index = newInstuctions.FindLastIndex(index, x => x.opcode == OpCodes.Ret);

        while (index != -1)
        {
            CodeInstruction instruction = newInstuctions[index];

            instruction.opcode = OpCodes.Br;
            instruction.operand = doProfilerCheck;
            instruction.MoveLabelsTo(profilerCheckBegin);

            index = newInstuctions.FindLastIndex(index, x => x.opcode == OpCodes.Ret);
        }

        return newInstuctions.FinishTranspiler();
    }

    /// <summary>
    /// A struct for containing profiler info.
    /// </summary>
    /// <remarks><b>Do not create instances of this struct, or you will encounter problems.</b></remarks>
    [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Ansi, Pack = 1, Size = MySize)]
    public unsafe struct ProfiledMethodInfo
    {
        public const int MySize = 4 + 4 + 4;

        /// <summary>
        /// Gets the total number of invocations by the method associated with this instance.
        /// </summary>
        [FieldOffset(0)]
        public uint InvocationCount;

        /// <summary>
        /// Gets the total ticks taken to execute by the method associated with this instance over all invocations.
        /// </summary>
        [FieldOffset(4)]
        public uint TotalTicks;

        /// <summary>
        /// Gets the maximum tick count generated by the method associated with this instance.
        /// </summary>
        [FieldOffset(8)]
        public uint MaxTicks;

        /// <summary>
        /// Gets the method associated with this instance.
        /// </summary>
        /// <remarks>
        /// <b>NEVER call this method unless you are using the struct by reference.</b>
        /// <code>ref ProfileMethodPatch.ProfiledMethodInfo info = ref ProfileMethodPatch.ProfilerInfos[someIndex];</code>
        /// </remarks>
        public readonly string GetMyMethod
        {
            get
            {
                IntPtr byteOffset = Unsafe.ByteOffset(ref ProfilerInfos[0], ref Unsafe.AsRef(this));
                int myIndex = byteOffset.ToInt32() / MySize;

                ProfiledMethodsTracker.GetMethod(myIndex, out string result);
                return result;
            }
        }

        /// <summary>
        /// Gets the average tick count for the method associated with this instance.
        /// </summary>
        public readonly uint AvgTicks => TotalTicks / Math.Max(1, InvocationCount);
    }

    public static class ProfiledMethodsTracker
    {
        public static int MaxIndex => Math.Min(patchedCount - 1, MaxPatches - 1);

        private static volatile int patchedCount = 0;

        private static Dictionary<int, int> patched = new(7000);
        private static Dictionary<int, string> byIndex = new(7000);

        public static bool AddMethod(MethodBase method)
        {
            if (patchedCount == MaxPatches)
                return false;

            if (patched.ContainsKey(method.GetHashCode()))
                return false;

            int index = Interlocked.Exchange(ref patchedCount, patchedCount + 1);

            patched.Add(method.GetHashCode(), index);
            byIndex.Add(index, string.Concat(method.DeclaringType.FullName, ".", method.Name));
            return true;
        }

        public static bool GetMethodIndex(MethodBase method, out int index)
        {
            return patched.TryGetValue(method.GetHashCode(), out index);
        }

        public static bool GetMethod(int index, out string method)
        {
            return byIndex.TryGetValue(index, out method);
        }
    }
}
