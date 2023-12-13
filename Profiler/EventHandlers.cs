using InventorySystem.Items.Pickups;
using MEC;
using Mirror;
using PluginAPI.Core;
using PluginAPI.Core.Attributes;
using PluginAPI.Enums;
using PluginAPI.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine.Profiling;

namespace CustomProfiler
{
    internal class EventHandlers
    {
        //[PluginEvent(ServerEventType.PlayerJoined)]
        public void OnPlayerJoined(PlayerJoinedEvent ev)
        {
            //This is for testing, DONT USE THIS
            //Round.Start();
            //Timing.CallDelayed(1f, () => Round.Restart(false));
        }
    }
}
