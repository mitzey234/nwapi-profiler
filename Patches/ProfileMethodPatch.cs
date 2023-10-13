namespace CustomProfiler.Patches;

using CustomProfiler.API;
using CustomProfiler.Extensions;
using HarmonyLib;
using NorthwoodLib.Pools;
using PluginAPI.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using static HarmonyLib.AccessTools;

public static class ProfileMethodPatch
{
    internal static bool DisableProfiler = false;

    private static ProfiledMethodInfo[] ProfilerInfos = new ProfiledMethodInfo[4000];

    private static HarmonyMethod ProfilerTranspiler = new(typeof(ProfileMethodPatch), nameof(Transpiler));

    private static HashSet<MethodBase> OptimizedMethods;

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

        OptimizedMethods ??= new(CustomProfilerPlugin.HarmonyOptimizations.GetPatchedMethods());

        if (OptimizedMethods.Contains(method))
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

        try
        {
            CustomProfilerPlugin.Harmony.Patch(method, prefix: null, postfix: null, transpiler: ProfilerTranspiler, finalizer: null);
        }
        catch (Exception e)
        {
            Log.Error(e.ToString());
        }
    }

    private static unsafe IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator generator)
    {
        if (!ProfiledMethodsTracker.GetMethodIndex(method, out int methodIndex))
        {
            Log.Error("Could not locate method index?");
            return instructions;
        }

        instructions.BeginTranspiler(out List<CodeInstruction> newInstuctions);

        // long startTimestamp;
        LocalBuilder startTimestamp = generator.DeclareLocal(typeof(long));
        LocalBuilder profilerDisabled = generator.DeclareLocal(typeof(bool));

        Label profilerDisabledLabel = generator.DefineLabel();

        newInstuctions[0].labels.Add(profilerDisabledLabel);

        // We get the starting timestamp recorded by the timer mechanism
        // and store it into a variable we can use later.
        newInstuctions.InsertRange(0, new CodeInstruction[]
        {
            // bool profilerDisabled = ProfileMethodPatch.DisableProfiler;
            // if (!profilerDisabled)
            // {
            //     long startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            // }
            new(OpCodes.Ldsfld, Field(typeof(ProfileMethodPatch), nameof(DisableProfiler))),
            new(OpCodes.Dup),
            new(OpCodes.Stloc_S, profilerDisabled),
            new(OpCodes.Brtrue_S, profilerDisabledLabel),
            new(OpCodes.Call, Method(typeof(Stopwatch), nameof(Stopwatch.GetTimestamp))),
            new(OpCodes.Stloc_S, startTimestamp),
        });

        int index = newInstuctions.FindLastIndex(x => x.opcode == OpCodes.Ret);

        // For each return instruction, we steal its labels
        // and insert a profile call to store the recorded time
        // taken by the method to execute.
        while (index != -1)
        {
            Label justReturnLabel = generator.DefineLabel();
            List<Label> stolenLabels = newInstuctions[index].ExtractLabels();
            newInstuctions[index].labels.Add(justReturnLabel);

            newInstuctions.InsertRange(index, new CodeInstruction[]
            {
                // if (!profilerDisabled)
                // {
                //     ProfileMethodPatch.ProfileMethod(Stopwatch.GetTimestamp() - startTimestamp, ref ProfilerInfos[methodIndex], methodIndex)
                // }
                new CodeInstruction(OpCodes.Ldloc_S, profilerDisabled).WithLabels(stolenLabels),
                new(OpCodes.Brtrue_S, justReturnLabel),

                new(OpCodes.Call, Method(typeof(Stopwatch), nameof(Stopwatch.GetTimestamp))),
                new(OpCodes.Ldloc_S, startTimestamp),
                new(OpCodes.Sub),
                new(OpCodes.Ldsfld, Field(typeof(ProfileMethodPatch), nameof(ProfilerInfos))),
                new(OpCodes.Ldc_I4, methodIndex),
                new(OpCodes.Ldelema, typeof(ProfiledMethodInfo)),
                new(OpCodes.Ldc_I4, methodIndex),
                new(OpCodes.Call, Method(typeof(ProfileMethodPatch), nameof(ProfileMethod))),
            });

            index = newInstuctions.FindLastIndex(index, x => x.opcode == OpCodes.Ret);
        }

        return newInstuctions.FinishTranspiler();
    }

    private static void ProfileMethod(long totalTicks, ref ProfiledMethodInfo info, int methodIndex)
    {
        info.InvocationCount++;
        info.TotalTicks += totalTicks;
        info.MaxTicks = Math.Max(info.MaxTicks, totalTicks);
    }

    /// <summary>
    /// A struct for containing profiler info.
    /// </summary>
    /// <remarks><b>Do not create instances of this struct, or you will encounter problems.</b></remarks>
    [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Ansi, Pack = 1, Size = 24)]
    public unsafe struct ProfiledMethodInfo
    {
        /// <summary>
        /// Gets the total number of invocations by the method associated with this instance.
        /// </summary>
        [FieldOffset(0)]
        public long InvocationCount;

        /// <summary>
        /// Gets the total ticks taken to execute by the method associated with this instance over all invocations.
        /// </summary>
        [FieldOffset(8)]
        public long TotalTicks;

        /// <summary>
        /// Gets the maximum tick count generated by the method associated with this instance.
        /// </summary>
        [FieldOffset(16)]
        public long MaxTicks;

        /// <summary>
        /// Gets the method associated with this instance.
        /// </summary>
        /// <remarks>
        /// <b>NEVER call this method unless you are using the struct by reference.</b>
        /// <code>ref ProfileMethodPatch.ProfiledMethodInfo info = ref ProfileMethodPatch.ProfilerInfos[someIndex];</code>
        /// </remarks>
        public readonly MethodBase GetMyMethod
        {
            get
            {
                int myIndex;

                fixed (ProfiledMethodInfo* info = &this)
                {
                    fixed (ProfiledMethodInfo* field = &ProfilerInfos[0])
                    {
                        myIndex = ((int)info - (int)field) / 24;
                    }
                }

                ProfiledMethodsTracker.GetMethod(myIndex, out MethodBase result);
                return result;
            }
        }

        /// <summary>
        /// Gets the average tick count for the method associated with this instance.
        /// </summary>
        public readonly long AvgTicks => TotalTicks / Math.Max(1, InvocationCount);
    }

    public static class ProfiledMethodsTracker
    {
        public static int MaxIndex => patchedCount - 1;

        private static volatile int patchedCount = 0;

        private static Dictionary<MethodBase, int> patched = new(4000);
        private static Dictionary<int, MethodBase> byIndex = new(4000);

        public static bool AddMethod(MethodBase method)
        {
            if (patched.ContainsKey(method))
                return false;

            int index = Interlocked.Exchange(ref patchedCount, patchedCount + 1);

            patched.Add(method, index);
            byIndex.Add(index, method);
            return true;
        }

        public static bool GetMethodIndex(MethodBase method, out int index)
        {
            return patched.TryGetValue(method, out index);
        }

        public static bool GetMethod(int index, out MethodBase method)
        {
            return byIndex.TryGetValue(index, out method);
        }
    }
}
