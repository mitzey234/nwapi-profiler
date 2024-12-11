using Optimizer.Patches.Optimizations;
using PluginAPI.Core.Attributes;
using PluginAPI.Enums;

namespace Optimizer;

public class EventHandlers
{
    [PluginEvent(ServerEventType.RoundStart)]
    void OnRoundStart()
    {
        if (ReferenceHub.TryGetHostHub(out ReferenceHub hub))
        {
            hub.gameObject.AddComponent(typeof(BasicStuff.GrenadeTamer));
            hub.gameObject.AddComponent(typeof(BasicStuff.PhysicsTamer));
        }
    }
}