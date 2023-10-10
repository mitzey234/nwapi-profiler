namespace CustomProfiler.Metrics;

using PluginAPI.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class MethodMetrics
{
    internal static Dictionary<MethodBase, MethodMetrics> methodMetrics = new();

    public long invocationCount = 0;
    public long tickCount = 0;
    public string name = "";
    public MethodBase method = null;
    public MethodBase methodBase = null;
    public Dictionary<MethodBase, MethodMetrics> calls = new();

    public double ticksPerInvoke
    {
        get
        {
            return tickCount == 0 ? -1 : Math.Round(tickCount / invocationCount * 100.0) / 100.0;
        }
    }

    public static string getMethodMetrics(bool print = false)
    {
        string output = "";
        int count = 0;
        var sortedDict = from entry in methodMetrics orderby entry.Value.invocationCount descending select entry;
        var secondSortedDict = from entry in methodMetrics.Where(x => x.Value.invocationCount > 10) orderby entry.Value.ticksPerInvoke descending select entry;
        if (print) Log.Info("Invocation count: ");
        output += ("Invocation count: \n");
        foreach (KeyValuePair<MethodBase, MethodMetrics> kvp in sortedDict)
        {
            if (print) Log.Info(kvp.Value.name + " - " + kvp.Value.invocationCount + " - Avg. Ticks Per: " + kvp.Value.ticksPerInvoke);
            output += (kvp.Value.name + " - " + kvp.Value.invocationCount + " - Avg. Ticks Per: " + kvp.Value.ticksPerInvoke) + "\n";
            var sortedDict2 = from entry in kvp.Value.calls orderby entry.Value.invocationCount descending select entry;
            foreach (KeyValuePair<MethodBase, MethodMetrics> kvp2 in sortedDict2)
            {
                if (print) Log.Info("\t" + kvp2.Value.name + " - " + kvp2.Value.invocationCount + " - Avg. Ticks Per: " + kvp2.Value.ticksPerInvoke);
                output += ("\t" + kvp2.Value.name + " - " + kvp2.Value.invocationCount + " - Avg. Ticks Per: " + kvp2.Value.ticksPerInvoke) + "\n";
            }
            count++;
            if (count > 10) break;
        }
        if (print) Log.Info("");
        output += "\n";

        count = 0;
        sortedDict = from entry in methodMetrics orderby entry.Value.tickCount descending select entry;
        if (print) Log.Info("Tick count: ");
        output += ("Tick count: \n");
        foreach (KeyValuePair<MethodBase, MethodMetrics> kvp in sortedDict)
        {
            if (print) Log.Info(kvp.Value.name + " - " + kvp.Value.tickCount + " - " + kvp.Value.invocationCount + " - Avg. Ticks Per: " + kvp.Value.ticksPerInvoke);
            output += (kvp.Value.name + " - " + kvp.Value.tickCount + " - " + kvp.Value.invocationCount + " - Avg. Ticks Per: " + kvp.Value.ticksPerInvoke) + "\n";
            var sortedDict2 = from entry in kvp.Value.calls orderby entry.Value.invocationCount descending select entry;
            foreach (KeyValuePair<MethodBase, MethodMetrics> kvp2 in sortedDict2)
            {
                if (print) Log.Info("\t" + kvp2.Value.name + " - " + kvp2.Value.invocationCount + " - Avg. Ticks Per: " + kvp2.Value.ticksPerInvoke);
                output += ("\t" + kvp2.Value.name + " - " + kvp2.Value.invocationCount + " - Avg. Ticks Per: " + kvp2.Value.ticksPerInvoke) + "\n";
            }
            count++;
            if (count > 10) break;
        }
        if (print) Log.Info("");
        output += "\n";

        count = 0;
        if (print) Log.Info("Ticks per invoke: ");
        output += ("Ticks per invoke: \n");
        foreach (KeyValuePair<MethodBase, MethodMetrics> kvp in secondSortedDict)
        {
            if (print) Log.Info(kvp.Value.name + " - " + kvp.Value.ticksPerInvoke + " - Invocation count: " + kvp.Value.invocationCount);
            output += (kvp.Value.name + " - " + kvp.Value.ticksPerInvoke + " - Invocation count: " + kvp.Value.invocationCount) + "\n";
            var sortedDict2 = from entry in kvp.Value.calls orderby entry.Value.invocationCount descending select entry;
            foreach (KeyValuePair<MethodBase, MethodMetrics> kvp2 in sortedDict2)
            {
                if (print) Log.Info("\t" + kvp2.Value.name + " - " + kvp2.Value.invocationCount + " - Avg. Ticks Per: " + kvp2.Value.ticksPerInvoke);
                output += ("\t" + kvp2.Value.name + " - " + kvp2.Value.invocationCount + " - Avg. Ticks Per: " + kvp2.Value.ticksPerInvoke) + "\n";
            }
            count++;
            if (count > 10) break;
        }
        return output;
    }
}
