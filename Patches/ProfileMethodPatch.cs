namespace CustomProfiler.Patches;

using CustomProfiler.Extensions;
using CustomProfiler.Metrics;
using HarmonyLib;
using NorthwoodLib.Pools;
using PluginAPI.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using static HarmonyLib.AccessTools;

public static class ProfileMethodPatch
{
    internal static bool DisableProfiler = false;

    private static HarmonyMethod ProfilerTranspiler = new(typeof(ProfileMethodPatch), nameof(Transpiler));

    private static HashSet<MethodBase> optimizedMethods;
    private static HashSet<MethodBase> ProfiledMethods = new();
    private static Dictionary<long, MethodBase> ProfiledMethodsByHash = new();

    internal static void ApplyProfiler(MethodBase method)
    {
        if (ProfiledMethods.Contains(method))
        {
            return;
        }

        optimizedMethods ??= new(CustomProfilerPlugin.HarmonyOptimizations.GetPatchedMethods());

        if (optimizedMethods.Contains(method))
        {
            return;
        }

        if (method.GetMethodBody() == null)
        {
            throw new ArgumentException("Cannot patch a method without a body.");
        }

        Type[] args;

        if (method.IsGenericMethod)
        {
            throw new ArgumentException($"Cannot patch generic method");

            if (method.ContainsGenericParameters)
            {
                throw new ArgumentException($"Cannot patch generic method that is not constructed.");
            }

            args = method.GetGenericArguments();

            for (int i = 0; i < args.Length; i++)
            {
                if (!args[i].IsValueType)
                {
                    throw new ArgumentException($"Cannot patch generic method using reference type arguments. This can cause instability.");
                }
            }
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

        ProfiledMethods.Add(method);
        ProfiledMethodsByHash.Add(method.GetHashCode(), method);
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator generator)
    {
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
                //     ProfileMethodPatch.ProfileMethod(method.GetHashCode(), Stopwatch.GetTimestamp() - startTimestamp)
                // }
                new CodeInstruction(OpCodes.Ldloc_S, profilerDisabled).WithLabels(stolenLabels),
                new(OpCodes.Brtrue_S, justReturnLabel),
                new(OpCodes.Ldc_I4, method.GetHashCode()),
                new(OpCodes.Call, Method(typeof(Stopwatch), nameof(Stopwatch.GetTimestamp))),
                new(OpCodes.Ldloc_S, startTimestamp),
                new(OpCodes.Sub),
                new(OpCodes.Call, Method(typeof(ProfileMethodPatch), nameof(ProfileMethod))),
            });

            index = newInstuctions.FindLastIndex(index, x => x.opcode == OpCodes.Ret);
        }

        return newInstuctions.FinishTranspiler();
    }

    private static void ProfileMethod(int hash, long totalTicks)
    {
        if (!ProfiledMethodsByHash.TryGetValue(hash, out MethodBase method)) return;
        try
        {
            if (!MethodMetrics.methodMetrics.TryGetValue(method, out MethodMetrics metrics))
            {
                metrics = new MethodMetrics();
                metrics.method = method;
                metrics.name = (method.DeclaringType != null ? method.DeclaringType.FullName : "Unknown Type") + "." + method.Name;
                MethodMetrics.methodMetrics.Add(method, metrics);
            }
            metrics.invocationCount++;
            metrics.tickCount += totalTicks;
            if (!CustomProfilerPlugin.activeStacks.Contains(method) && metrics.calls.Count > 0) return;
            StackTrace t = new StackTrace();
            MethodBase callingMethod = t.FrameCount >= 3 && !t.GetFrame(2).GetMethod().Name.Contains("_Patch0") && !t.GetFrame(2).GetMethod().Name.Contains("_Patch1") ? t.GetFrame(2).GetMethod() : (t.FrameCount >= 4 && !t.GetFrame(3).GetMethod().Name.Contains("_Patch0") && !t.GetFrame(3).GetMethod().Name.Contains("_Patch1") ? t.GetFrame(3).GetMethod() : null);
            if (callingMethod == null || t.FrameCount < 3) return;
            if (!metrics.calls.TryGetValue(callingMethod, out MethodMetrics subMetrics))
            {
                subMetrics = new MethodMetrics();
                subMetrics.methodBase = callingMethod;
                subMetrics.name = (callingMethod.DeclaringType != null ? callingMethod.DeclaringType.FullName : "Unknown Type") + "." + callingMethod.Name;
                metrics.calls.Add(callingMethod, subMetrics);
            }
            subMetrics.invocationCount++;
            subMetrics.tickCount += totalTicks;
        } catch (Exception e)
        {
            Log.Error(e.Message + "\n" + e.StackTrace);
        }

        //Log.Info("Test: " + method.DeclaringType.Assembly.FullName + " - " + method.Name + " - " + method.DeclaringType);
        // log any info you want about the method here.
        // feel free to use StackTrace or StackFrame.
    }
}
