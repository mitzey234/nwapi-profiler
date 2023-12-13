namespace Optimizer.Commands;

using CommandSystem;
using MEC;
using NorthwoodLib.Pools;
using Optimizer;
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
class uncap : ICommand, IHiddenCommand
{
    public string[] Aliases { get; set; } = new string[] { };

    public string Description { get; set; } = "Uncaps the TPS of the server runtime, togglable";

    public string usage { get; set; } = "uncap";

    string ICommand.Command { get; } = "uncap";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        Plugin.upcapped = !Plugin.upcapped;
        response = Plugin.upcapped ? "Enabling" : "Disabling";
        return true;
    }
}