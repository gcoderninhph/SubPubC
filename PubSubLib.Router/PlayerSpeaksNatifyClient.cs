using Natify;
using PubSubLib.Messages;

namespace PubSubLib.Router;

internal sealed class PlayerSpeaksNatifyClient : IPlayerSpeaksNatifyClient
{
    private readonly NatifyServer _server;
    private readonly string _regionId;

    private Action<PlayerSpeaksEvent>? _onPlayerSpeaks;
    private Action<MirrorMessageEvent>? _onMirrorMessage;

    private const string EvtTopic = "PlayerSpeaks.Evt";
    private const string MsgTopic = "PlayerSpeaks.Msg";
    private const string StatusTopic = "PlayerSpeaks.Status";

    internal PlayerSpeaksNatifyClient(NatifyServer server, string regionId)
    {
        _server = server;
        _regionId = regionId;
        _server.OnMessage<PlayerSpeaksEvent>(EvtTopic, OnEvent);
        _server.OnMessage<MirrorMessageEvent>(MsgTopic, OnMsg);
    }

    private void OnEvent((string regionId, Data<PlayerSpeaksEvent> data) args)
    {
        _onPlayerSpeaks?.Invoke(args.data.Value);
    }

    private void OnMsg((string regionId, Data<MirrorMessageEvent> data) args)
    {
        _onMirrorMessage?.Invoke(args.data.Value);
    }

    public void SendOnlineStatus(PlayerOnlineStatusMsg msg)
    {
        _server.Publish(StatusTopic, _regionId, msg);
    }

    public void OnPlayerSpeaks(Action<PlayerSpeaksEvent> callback)
    {
        _onPlayerSpeaks = callback;
    }

    public void OnMirrorMessage(Action<MirrorMessageEvent> callback)
    {
        _onMirrorMessage = callback;
    }

    public void Dispose()
    {
        _server.Dispose();
    }
}
