namespace CustomProfiler.Commands;

using CommandSystem;
using CustomProfiler.API;
using CustomProfiler.Metrics;
using MEC;
using NorthwoodLib.Pools;
using PlayerRoles.RoleAssign;
using PluginAPI.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using ICommand = CommandSystem.ICommand;

[CommandHandler(typeof(RemoteAdminCommandHandler))]
[CommandHandler(typeof(GameConsoleCommandHandler))]
class profiler : ICommand, IHiddenCommand
{
    public string[] Aliases { get; set; } = new string[] { };

    public string Description { get; set; } = "Prints performance metrics to the console";

    public string usage { get; set; } = "profiler";

    string ICommand.Command { get; } = "profiler";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        response = "\n" + MethodMetrics.getMethodMetrics(true);
        return true;
    }
}

[CommandHandler(typeof(RemoteAdminCommandHandler))]
[CommandHandler(typeof(GameConsoleCommandHandler))]
class resetProfiler : ICommand, IHiddenCommand
{
    public string[] Aliases { get; set; } = new string[] { };

    public string Description { get; set; } = "Resets metrics";

    public string usage { get; set; } = "resetprofiler";

    string ICommand.Command { get; } = "resetprofiler";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        response = "Resetting";
        CustomProfilerPlugin.reset();
        return true;
    }
}

[CommandHandler(typeof(RemoteAdminCommandHandler))]
[CommandHandler(typeof(GameConsoleCommandHandler))]
class stopProfiler : ICommand, IHiddenCommand
{
    public string[] Aliases { get; set; } = new string[] { };

    public string Description { get; set; } = "Stops the profiler";

    public string usage { get; set; } = "stopProfiler";

    string ICommand.Command { get; } = "stopProfiler";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        response = "Stopping";
        CustomProfilerPlugin.disableProfiler();
        return true;
    }
}

[CommandHandler(typeof(RemoteAdminCommandHandler))]
[CommandHandler(typeof(GameConsoleCommandHandler))]
class startProfiler : ICommand, IHiddenCommand
{
    public string[] Aliases { get; set; } = new string[] { };

    public string Description { get; set; } = "Starts the profiler";

    public string usage { get; set; } = "startprofiler";

    string ICommand.Command { get; } = "startprofiler";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        response = "Starting";
        CustomProfilerPlugin.enableProfiler();
        return true;
    }
}

[CommandHandler(typeof(RemoteAdminCommandHandler))]
[CommandHandler(typeof(GameConsoleCommandHandler))]
class opton : ICommand, IHiddenCommand
{
    public string[] Aliases { get; set; } = new string[] { };

    public string Description { get; set; } = "opton";

    public string usage { get; set; } = "opton";

    string ICommand.Command { get; } = "opton";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        CustomProfilerPlugin.enableOptimizations();
        response = "Enabling";
        return true;
    }
}

[CommandHandler(typeof(RemoteAdminCommandHandler))]
[CommandHandler(typeof(GameConsoleCommandHandler))]
class optoff : ICommand, IHiddenCommand
{
    public string[] Aliases { get; set; } = new string[] { };

    public string Description { get; set; } = "optoff";

    public string usage { get; set; } = "optoff";

    string ICommand.Command { get; } = "optoff";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        CustomProfilerPlugin.disableOptimizations();
        response = "Disabling";
        return true;
    }
}

[CommandHandler(typeof(RemoteAdminCommandHandler))]
[CommandHandler(typeof(GameConsoleCommandHandler))]
class uncap : ICommand, IHiddenCommand
{
    public string[] Aliases { get; set; } = new string[] { };

    public string Description { get; set; } = "Uncaps the TPS of the server runtime, togglable";

    public string usage { get; set; } = "uncap";

    string ICommand.Command { get; } = "uncap";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        CustomProfilerPlugin.upcapped = !CustomProfilerPlugin.upcapped;
        response = CustomProfilerPlugin.upcapped ? "Enabling" : "Disabling";
        return true;
    }
}

[CommandHandler(typeof(RemoteAdminCommandHandler))]
[CommandHandler(typeof(GameConsoleCommandHandler))]
class memoryUpdates : ICommand, IHiddenCommand
{
    public string[] Aliases { get; set; } = new string[] { };

    public string Description { get; set; } = "Controls array list fields updater, togglable";

    public string usage { get; set; } = "updatememory";

    string ICommand.Command { get; } = "updatememory";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        CustomProfilerPlugin.upcapped = !CustomProfilerPlugin.upcapped;
        response = CustomProfilerPlugin.upcapped ? "Enabling" : "Disabling";
        return true;
    }
}

[CommandHandler(typeof(RemoteAdminCommandHandler))]
[CommandHandler(typeof(GameConsoleCommandHandler))]
class resetLateJoin : ICommand, IHiddenCommand
{
    public string[] Aliases { get; set; } = new string[] { "rlj" };

    public string Description { get; set; } = "Resets the latejoin timer";

    public string usage { get; set; } = "resetlatejoin";

    string ICommand.Command { get; } = "resetlatejoin";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        RoleAssigner.LateJoinTimer.Restart();
        response = "Stopwatch Reset";
        return true;
    }
}

[CommandHandler(typeof(RemoteAdminCommandHandler))]
class ClearTestingNotice : ICommand
{

    public static CoroutineHandle noticeCo;

    public string[] Aliases { get; set; } = { "ctn" };

    public string Description { get; set; } = "Clears the testing notice broadcast";

    String ICommand.Command { get; } = "clearTestingNotice";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (noticeCo.IsRunning) Timing.KillCoroutines(noticeCo);

        response = "Notce stopped";
        return true;
    }
}

[CommandHandler(typeof(RemoteAdminCommandHandler))]
class BeginTestingNotice : ICommand
{
    public string[] Aliases { get; set; } = { "btn" };

    public string Description { get; set; } = "Starts the testing notice broadcast";

    String ICommand.Command { get; } = "begingTestingNotice";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (ClearTestingNotice.noticeCo.IsRunning) Timing.KillCoroutines(ClearTestingNotice.noticeCo);
        ClearTestingNotice.noticeCo = Timing.RunCoroutine(NoticeLoop());

        response = "Notice Started";
        return true;
    }

    public IEnumerator<float> NoticeLoop()
    {
        while (true)
        {
            Map.Broadcast(duration: 2, message: "<color=yellow>---Server stress testing in progress---</color>", clearPrevius: false);
            yield return Timing.WaitForSeconds(1.5f);
        }
    }
}

[CommandHandler(typeof(RemoteAdminCommandHandler))]
class ShowMonoInstances : ICommand
{
    public string[] Aliases { get; set; } = { "monoblist" };

    public string Description { get; set; } = "Shows the list of mono behaviour instances";

    String ICommand.Command { get; } = "monoblist";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        Type[] bhs = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(includeInactive: true).Select(x => x.GetType()).ToArray();
        Dictionary<Type, int> counts = new(bhs.Length);

        for (int i = 0; i < bhs.Length; i++)
        {
            counts.TryGetValue(bhs[i], out int count);
            counts[bhs[i]] = count + 1;
        }

        StringBuilder builder = StringBuilderPool.Shared.Rent();

        foreach (var pair in counts.OrderByDescending(x => x.Value))
        {
            builder.AppendLine($"{pair.Value:000000} | {pair.Key.FullName}");
        }

        Log.Info(StringBuilderPool.Shared.ToStringReturn(builder));
        response = "info printed to console";
        return true;
    }
}

[CommandHandler(typeof(RemoteAdminCommandHandler))]
class MonoTestCmd : ICommand
{
    public string[] Aliases { get; set; } = { };

    public string Description { get; set; } = "Shows mono memory (test command)";

    String ICommand.Command { get; } = "monomemory";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        response = $"\nused: {MonoNative.mono_gc_get_used_size()}\nheap: {MonoNative.mono_gc_get_heap_size()}";
        return true;
    }
}