using Natify;
using PubSubLib.Contracts;
using PubSubLib.Messages;

namespace PubSubLib.Router;

internal sealed class PlayerSpeaksNatifyClient : IPlayerSpeaksNatifyClient
{
    private readonly INatifyServer _server;
    private readonly string _regionId;

    private Action<PlayerSpeaksEvent>? _onPlayerSpeaks;
    private Action<MirrorMessageEvent>? _onMirrorMessage;

    private const string EvtTopic = "PlayerSpeaks.Evt";
    private const string MsgTopic = "PlayerSpeaks.Msg";
    private const string ClientMsgTopic = "PlayerSpeaks.ClientMsg";
    private const string StatusTopic = "PlayerSpeaks.Status";

    internal PlayerSpeaksNatifyClient(INatifyServer server, string regionId)
    {
        _server = server;
        _regionId = regionId;
        _server.OnMessage<PlayerSpeaksEvent>(EvtTopic, OnEvent);
        _server.OnMessage<MirrorMessageEvent>(MsgTopic, OnMsg);
    }

    private void OnEvent((string regionId, Data<PlayerSpeaksEvent> data) args)
    {
        try { _onPlayerSpeaks?.Invoke(args.data.Value); }
        catch (Exception ex) { PubSubLog.Error(ex, "OnPlayerSpeaks callback failed"); }
    }

    private void OnMsg((string regionId, Data<MirrorMessageEvent> data) args)
    {
        try { _onMirrorMessage?.Invoke(args.data.Value); }
        catch (Exception ex) { PubSubLog.Error(ex, "OnMirrorMessage callback failed"); }
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

    public void SendClientMsg(ClientMirrorMessage msg)
    {
        try { _server.Publish(ClientMsgTopic, _regionId, msg); }
        catch (Exception ex) { PubSubLog.Error(ex, "SendClientMsg publish failed"); }
    }

    public void Dispose()
    {
    }
}
