using Natify;
using PubSubLib.Messages;

namespace PubSubLib.Router;

public interface IPlayerSpeaksNatifyClient : IDisposable
{
    static IPlayerSpeaksNatifyClient Create(NatifyServer server, string regionId)
    {
        return new PlayerSpeaksNatifyClient(server, regionId);
    }

    void SendOnlineStatus(PlayerOnlineStatusMsg msg);

    void OnPlayerSpeaks(Action<PlayerSpeaksEvent> callback);
    void OnMirrorMessage(Action<MirrorMessageEvent> callback);
    void SendClientMsg(ClientMirrorMessage msg);
}
