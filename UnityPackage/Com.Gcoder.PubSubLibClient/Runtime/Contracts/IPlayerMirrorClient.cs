using System;

namespace PubSubLib.Mirror
{
    public interface IPlayerMirrorClient
    {
        long PlayerId { get; set; }
    
        string DataName { get; }
    
        bool IsInitialized { get; }
    
        void ApplyUpdate(byte[] data, string commit);
        void DispatchMessage(string subject, byte[] data);
        void OnSendMessage(Action<string, long, byte[]> handler);
    }

}

