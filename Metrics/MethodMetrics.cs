namespace CustomProfiler.Metrics;

using CustomProfiler.API;
using NorthwoodLib.Pools;
using PluginAPI.Core;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using static CustomProfiler.Patches.ProfileMethodPatch;

public class MethodMetrics
{
    public static string getMethodMetrics(bool print = false)
    {
        StringBuilder builder = StringBuilderPool.Shared.Rent();

        IEnumerable<AsRef<ProfiledMethodInfo>> enumerable = GetProfilerInfos();

        int count = 0;
        var sortedDict = from entry in enumerable orderby entry.Value.InvocationCount descending select entry;

        builder.AppendLine("Invocation count: ");

        foreach (AsRef<ProfiledMethodInfo> asRefInfo in sortedDict)
        {
            ref ProfiledMethodInfo info = ref asRefInfo.Value;
            MethodBase method = info.GetMyMethod;

            builder.AppendLine($"{method.DeclaringType.Name}.{method.Name} - {info.InvocationCount} - Avg. Ticks Per: {info.AvgTicks}");

            if (count++ > 10)
                break;
        }

        builder.AppendLine();

        count = 0;
        sortedDict = from entry in enumerable orderby entry.Value.TotalTicks descending select entry;

        builder.AppendLine("Tick count: ");
        foreach (AsRef<ProfiledMethodInfo> asRefInfo in sortedDict)
        {
            ref ProfiledMethodInfo info = ref asRefInfo.Value;
            MethodBase method = info.GetMyMethod;

            builder.AppendLine($"{method.DeclaringType.Name}.{method.Name} - {info.TotalTicks} - Avg. Ticks Per: {info.AvgTicks}");

            if (count++ > 10)
                break;
        }

        builder.AppendLine();

        count = 0;
        sortedDict = from entry in enumerable.Where(x => x.Value.InvocationCount > 10) orderby entry.Value.AvgTicks descending select entry;

        builder.AppendLine("Ticks per invoke:");
        foreach (AsRef<ProfiledMethodInfo> asRefInfo in sortedDict)
        {
            ref ProfiledMethodInfo info = ref asRefInfo.Value;
            MethodBase method = info.GetMyMethod;

            builder.AppendLine($"{method.DeclaringType.Name}.{method.Name} - {info.AvgTicks} - Invocation count: {info.InvocationCount}");

            if (count++ > 10)
                break;
        }

        builder.AppendLine();

        count = 0;
        sortedDict = from entry in enumerable orderby entry.Value.MaxTicks descending select entry;

        builder.AppendLine("Max ticks:");
        foreach (AsRef<ProfiledMethodInfo> asRefInfo in sortedDict)
        {
            ref ProfiledMethodInfo info = ref asRefInfo.Value;
            MethodBase method = info.GetMyMethod;

            builder.AppendLine($"{method.DeclaringType.Name}.{method.Name} - {info.MaxTicks} - Invocation count: {info.InvocationCount}");

            if (count++ > 10)
                break;
        }

        string result = StringBuilderPool.Shared.ToStringReturn(builder);

        if (print)
            Log.Info(result);

        return result;
    }
}
