using Optimizer;
using UnityEngine;

namespace CustomProfiler.Commands;

using CommandSystem;
using System;
using ICommand = CommandSystem.ICommand;

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
        Plugin.upcapped = !Plugin.upcapped;
        Application.targetFrameRate = Plugin.upcapped ? 1000 : 60;
        response = Plugin.upcapped ? "Enabling" : "Disabling";
        return true;
    }
}