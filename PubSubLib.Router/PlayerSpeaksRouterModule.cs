using System.Collections.Concurrent;
using MyConnection;
using Natify;
using PubSubLib.Messages;

namespace PubSubLib.Router;

internal sealed class PlayerSpeaksRouterModule : IPlayerSpeaksRouterModule
{
    private readonly IPlayerSpeaksNatifyClient _natifyClient;
    private IServer? _server;
    private readonly ConcurrentDictionary<long, IConnection> _connections = new();

    internal PlayerSpeaksRouterModule(NatifyServer server, string regionId)
    {
        _natifyClient = IPlayerSpeaksNatifyClient.Create(server, regionId);
    }

    public void SetServer(IServer server)
    {
        _server = server;

        server.OnConnect(conn =>
        {
            if (!long.TryParse(conn.User.Id, out var playerId))
                return;

            _connections[playerId] = conn;

            _natifyClient.SendOnlineStatus(new PlayerOnlineStatusMsg
            {
                PlayerId = playerId,
                IsOnline = true
            });

            _server!.SendOnTcp("PlayerSpeaks.Welcome", conn, new PlayerSpeaksWelcomeMsg
            {
                PlayerId = playerId
            });
        });

        server.OnDisconnect(conn =>
        {
            if (!long.TryParse(conn.User.Id, out var playerId))
                return;

            _connections.TryRemove(playerId, out _);

            _natifyClient.SendOnlineStatus(new PlayerOnlineStatusMsg
            {
                PlayerId = playerId,
                IsOnline = false
            });
        });

        _natifyClient.OnPlayerSpeaks(OnPlayerSpeaks);
        _natifyClient.OnMirrorMessage(OnMirrorMessage);
    }

    private void OnPlayerSpeaks(PlayerSpeaksEvent evt)
    {
        if (_server == null) return;

        if (_connections.TryGetValue(evt.PlayerId, out var conn) && conn.Connected)
            _server.SendOnTcp("PlayerSpeaks.Evt", conn, evt);
    }

    private void OnMirrorMessage(MirrorMessageEvent msg)
    {
        if (_server == null) return;

        if (_connections.TryGetValue(msg.PlayerId, out var conn) && conn.Connected)
            _server.SendOnTcp("PlayerSpeaks.Msg", conn, msg);
    }
}
