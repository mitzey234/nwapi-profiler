namespace CustomProfiler.Commands;

using CommandSystem;
using CustomProfiler.Metrics;
using CustomProfiler.Patches;
using MEC;
using PlayerRoles.RoleAssign;
using PluginAPI.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
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
class memory : ICommand, IHiddenCommand
{
    public string[] Aliases { get; set; } = new string[] { };

    public string Description { get; set; } = "Prints memory metrics to the console";

    public string usage { get; set; } = "memory";

    string ICommand.Command { get; } = "memory";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        response = "\n" + CustomProfilerPlugin.getMemoryMetrics(true);
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