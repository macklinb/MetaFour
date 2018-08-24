using UnityEngine;
using System.Collections;

public class NetworkDiscoverySimple : UnityEngine.Networking.NetworkDiscovery
{
    public System.Action<string, string> OnReceivedBroadcastEvent;

    public override void OnReceivedBroadcast(string fromAddress, string data)
    {
        if (OnReceivedBroadcastEvent != null)
            OnReceivedBroadcastEvent(fromAddress, data);
    }
}