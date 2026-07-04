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

            _natifyClient.SendCreateUnit(new CreateUnitCmd
            {
                UnitId = watcherId,
                UnitType = ""
            });
        });

        server.OnDisconnect(conn =>
        {
            if (!_watcherIds.TryRemove(conn, out var watcherId))
                return;

            _connections.TryRemove(watcherId, out _);

            _natifyClient.SendDestroyUnit(new DestroyUnitCmd
            {
                UnitId = watcherId,
                UnitType = ""
            });
        });

        server.SubscribeTcp<RegionCommand>("Region.Cmd", OnRegionCommand);

        _natifyClient.OnCreateUnitEvt(OnCreateUnitEvt);
        _natifyClient.OnDestroyUnitEvt(OnDestroyUnitEvt);
    }

    private void OnRegionCommand(IConnection conn, RegionCommand cmd)
    {
        if (!conn.Connected)
            return;

        switch (cmd.CmdCase)
        {
            case RegionCommand.CmdOneofCase.CreateUnit:
                _natifyClient.SendCreateUnit(cmd.CreateUnit);
                break;
            case RegionCommand.CmdOneofCase.DestroyUnit:
                _natifyClient.SendDestroyUnit(cmd.DestroyUnit);
                break;
        }
    }

    private void OnCreateUnitEvt(CreateUnitEvt evt)
    {
        if (_server == null) return;

        foreach (var (watcherId, conn) in _connections)
        {
            if (conn.Connected)
                _server.SendOnTcp("Region.Evt", conn, new RegionEvent { CreateUnit = evt });
        }
    }

    private void OnDestroyUnitEvt(DestroyUnitEvt evt)
    {
        if (_server == null) return;

        foreach (var (watcherId, conn) in _connections)
        {
            if (conn.Connected)
                _server.SendOnTcp("Region.Evt", conn, new RegionEvent { DestroyUnit = evt });
        }
    }

    public void Dispose()
    {
        _natifyClient.Dispose();
    }
}
