namespace CustomProfiler.Commands;

using CommandSystem;
using System;
using ICommand = CommandSystem.ICommand;

[CommandHandler(typeof(RemoteAdminCommandHandler))]
[CommandHandler(typeof(GameConsoleCommandHandler))]
class profiler : ICommand
{
    public string[] Aliases { get; set; } = new string[] { };

    public string Description { get; set; } = "Prints performance metrics to the console";

    public string usage { get; set; } = "profiler";

    string ICommand.Command { get; } = "profiler";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        response = "\n" + CustomProfilerPlugin.getMetrics(true);
        return true;
    }
}

[CommandHandler(typeof(RemoteAdminCommandHandler))]
[CommandHandler(typeof(GameConsoleCommandHandler))]
class resetProfiler : ICommand
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
class stopProfiler : ICommand
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
class startProfiler : ICommand
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
class opton : ICommand
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
class optoff : ICommand
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
