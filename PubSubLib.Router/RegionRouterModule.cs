using System.Collections.Concurrent;
using MyConnection;
using Natify;
using PubSubLib.Messages;

namespace PubSubLib.Router;

internal sealed class RegionRouterModule : IRegionRouterModule
{
    private readonly IRegionNatifyClient _natifyClient;
    private IServer? _server;
    private readonly ConcurrentDictionary<long, IConnection> _connections = new();
    private readonly ConcurrentDictionary<IConnection, long> _watcherIds = new();

    internal RegionRouterModule(NatifyServer server, string regionId)
    {
        _natifyClient = IRegionNatifyClient.Create(server, regionId);
    }

    public void SetServer(IServer server)
    {
        _server = server;

        server.OnConnect(conn =>
        {
            if (!long.TryParse(conn.User.Id, out var watcherId))
                return;

            _connections[watcherId] = conn;
            _watcherIds[conn] = watcherId;

            _natifyClient.SendAddWatcher(new AddWatcherCmd
            {
                WatcherId = watcherId,
                PosX = 0,
                PosY = 0,
                Radius = 0
            });
        });

        server.OnDisconnect(conn =>
        {
            if (!_watcherIds.TryRemove(conn, out var watcherId))
                return;

            _connections.TryRemove(watcherId, out _);

            _natifyClient.SendRemoveWatcher(new RemoveWatcherCmd
            {
                WatcherId = watcherId
            });
        });

        server.SubscribeUdp<PubSubCommand>("PubSub.Cmd", OnPubSubCommand);

        _natifyClient.OnBatchEnter(OnBatchEnter);
        _natifyClient.OnBatchLeave(OnBatchLeave);
        _natifyClient.OnSyncEnter(OnSyncEnter);
        _natifyClient.OnSyncLeave(OnSyncLeave);
        _natifyClient.OnUnitEvent(OnUnitEvent);
    }

    // ===== Inbound PubSub commands =====

    private void OnPubSubCommand(IConnection conn, PubSubCommand cmd)
    {
        if (!conn.Connected)
            return;

        if (!_watcherIds.TryGetValue(conn, out var watcherId))
        {
            if (!long.TryParse(conn.User.Id, out watcherId))
                return;

            _watcherIds[conn] = watcherId;
            _connections[watcherId] = conn;
        }

        switch (cmd.CmdCase)
        {
            case PubSubCommand.CmdOneofCase.MoveWatcher:
                cmd.MoveWatcher.WatcherId = watcherId;
                _natifyClient.SendMoveWatcher(cmd.MoveWatcher);
                break;
            case PubSubCommand.CmdOneofCase.PingUnits:
                cmd.PingUnits.WatcherId = watcherId;
                _natifyClient.SendPingUnits(cmd.PingUnits);
                break;
        }
    }

    // ===== Outbound PubSub events (target specific watchers) =====

    private void OnBatchEnter(BatchEnterMsg msg)
    {
        if (_server == null) return;
        foreach (var watcherId in msg.WatcherIds)
        {
            if (_connections.TryGetValue(watcherId, out var conn) && conn.Connected)
                _server.SendOnTcp("PubSub.Evt", conn, new PubSubEvent { BatchEnter = msg });
        }
    }

    private void OnBatchLeave(BatchLeaveMsg msg)
    {
        if (_server == null) return;
        foreach (var watcherId in msg.WatcherIds)
        {
            if (_connections.TryGetValue(watcherId, out var conn) && conn.Connected)
                _server.SendOnTcp("PubSub.Evt", conn, new PubSubEvent { BatchLeave = msg });
        }
    }

    private void OnSyncEnter(SyncEnterMsg msg)
    {
        if (_server == null) return;
        if (_connections.TryGetValue(msg.WatcherId, out var conn) && conn.Connected)
            _server.SendOnTcp("PubSub.Evt", conn, new PubSubEvent { SyncEnter = msg });
    }

    private void OnSyncLeave(SyncLeaveMsg msg)
    {
        if (_server == null) return;
        if (_connections.TryGetValue(msg.WatcherId, out var conn) && conn.Connected)
            _server.SendOnTcp("PubSub.Evt", conn, new PubSubEvent { SyncLeave = msg });
    }

    private void OnUnitEvent(UnitEventMsg msg)
    {
        if (_server == null) return;
        foreach (var watcherId in msg.WatcherIds)
        {
            if (_connections.TryGetValue(watcherId, out var conn) && conn.Connected)
            {
                if (!msg.UseUdp)
                    _server.SendOnTcp("PubSub.Evt", conn, new PubSubEvent { UnitEvent = msg });
                else
                    _server.SendOnUdp("PubSub.Evt", conn, new PubSubEvent { UnitEvent = msg });
            }
        }
    }

    public void Dispose()
    {
        _natifyClient.Dispose();
    }
}
