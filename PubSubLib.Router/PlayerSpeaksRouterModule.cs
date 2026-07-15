using System.Collections.Concurrent;
using MyConnection;
using Natify;
using PubSubLib.Contracts;
using PubSubLib.Messages;

namespace PubSubLib.Router;

internal sealed class PlayerSpeaksRouterModule : IPlayerSpeaksRouterModule
{
    private readonly IPlayerSpeaksNatifyClient _natifyClient;
    private IServer? _server;
    private readonly ConcurrentDictionary<long, IConnection> _connections = new();
    private ISubscribe? _clientMsgSub;
    private ISubscribe? _pingSub;

    internal PlayerSpeaksRouterModule(INatifyServer server, string regionId)
    {
        _natifyClient = IPlayerSpeaksNatifyClient.Create(server, regionId);
    }

    public void SetServer(IServer server)
    {
        _server = server;

        server.OnConnect(conn =>
        {
            try
            {
                if (!long.TryParse(conn.User.Id, out var playerId))
                    return;

                _connections[playerId] = conn;

                _server!.SendOnTcp("PlayerSpeaks.Welcome", conn, new PlayerSpeaksWelcomeMsg
                {
                    PlayerId = playerId
                });
            }
            catch (Exception ex) { PubSubLog.Error(ex, "OnConnect failed"); }
        });

        server.OnDisconnect(conn =>
        {
            try
            {
                if (!long.TryParse(conn.User.Id, out var playerId))
                    return;

                _connections.TryRemove(playerId, out _);

                _natifyClient.SendOnlineStatus(new PlayerOnlineStatusMsg
                {
                    PlayerId = playerId,
                    IsOnline = false
                });
            }
            catch (Exception ex) { PubSubLog.Error(ex, "OnDisconnect failed"); }
        });

        _natifyClient.OnPlayerSpeaks(OnPlayerSpeaks);
        _natifyClient.OnMirrorMessage(OnMirrorMessage);

        _clientMsgSub = server.SubscribeTcp<ClientMirrorMessage>("PlayerSpeaks.ClientMsg", OnClientMsg);
        _pingSub = server.SubscribeTcp<PlayerPingMsg>("PlayerSpeaks.Ping", OnClientPing);
    }

    private void OnPlayerSpeaks(PlayerSpeaksEvent evt)
    {
        try
        {
            if (_server == null) return;

            if (_connections.TryGetValue(evt.PlayerId, out var conn) && conn.Connected)
                _server.SendOnTcp("PlayerSpeaks.Evt", conn, evt);
        }
        catch (Exception ex) { PubSubLog.Error(ex, "OnPlayerSpeaks failed"); }
    }

    private void OnMirrorMessage(MirrorMessageEvent msg)
    {
        try
        {
            if (_server == null) return;

            if (_connections.TryGetValue(msg.PlayerId, out var conn) && conn.Connected)
                _server.SendOnTcp("PlayerSpeaks.Msg", conn, msg);
        }
        catch (Exception ex) { PubSubLog.Error(ex, "OnMirrorMessage failed"); }
    }

    private void OnClientMsg(IConnection conn, ClientMirrorMessage msg)
    {
        try { _natifyClient.SendClientMsg(msg); }
        catch (Exception ex) { PubSubLog.Error(ex, "OnClientMsg failed"); }
    }

    private void OnClientPing(IConnection conn, PlayerPingMsg msg)
    {
        try
        {
            if (_server == null) return;
            if (!long.TryParse(conn.User.Id, out var playerId)) return;
            _natifyClient.SendPing(new PlayerPingMsg { PlayerId = playerId });
        }
        catch (Exception ex) { PubSubLog.Error(ex, "OnClientPing failed"); }
    }
}
