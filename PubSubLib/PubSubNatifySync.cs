using Google.Protobuf;
using Natify;
using PubSubLib.Contracts;
using PubSubLib.Messages;

namespace PubSubLib;

internal sealed class PubSubNatifySync : IDisposable
{
    private readonly INatifyAdapter _natify;
    private readonly PubSub _pubSub;

    private const string CmdTopic = "PubSub.Cmd";
    private const string EvtTopic = "PubSub.Evt";

    public PubSubNatifySync(INatifyAdapter natify, PubSub pubSub)
    {
        _natify = natify;
        _pubSub = pubSub;

        _natify.Subscribe<PubSubCommand>(CmdTopic, OnCommand);
    }

    // ===== Inbound =====

    private void OnCommand(Data<PubSubCommand> data)
    {
        try
        {
            var cmd = data.Value;
            switch (cmd.CmdCase)
            {
                case PubSubCommand.CmdOneofCase.AddWatcher:
                    HandleAddWatcher(cmd.AddWatcher);
                    break;
                case PubSubCommand.CmdOneofCase.RemoveWatcher:
                    HandleRemoveWatcher(cmd.RemoveWatcher);
                    break;
                case PubSubCommand.CmdOneofCase.MoveWatcher:
                    HandleMoveWatcher(cmd.MoveWatcher);
                    break;
                case PubSubCommand.CmdOneofCase.PingUnits:
                    HandlePingUnits(cmd.PingUnits);
                    break;
                case PubSubCommand.CmdOneofCase.PublishEvent:
                    HandlePublishEvent(cmd.PublishEvent);
                    break;
            }
        }
        catch (Exception ex) { PubSubLog.Error(ex, "PubSubNatifySync.OnCommand failed"); }
    }

    private void HandleAddWatcher(AddWatcherCmd cmd)
    {
        _pubSub.AddWatcher(cmd.WatcherId, new Vector2 { x = cmd.PosX, y = cmd.PosY }, cmd.Radius);
    }

    private void HandleRemoveWatcher(RemoveWatcherCmd cmd)
    {
        _pubSub.RemoveWatcher(cmd.WatcherId);
    }

    private void HandleMoveWatcher(MoveWatcherCmd cmd)
    {
        _pubSub.MoveWatcher(cmd.WatcherId, new Vector2 { x = cmd.PosX, y = cmd.PosY }, cmd.Radius);
    }

    private void HandlePingUnits(PingUnitsCmd cmd)
    {
        var typeVersions = new Dictionary<string, Dictionary<long, int>>();
        foreach (var group in cmd.Units)
        {
            var dict = new Dictionary<long, int>();
            var ids = group.UnitIds;
            var versions = group.Versions;
            var count = Math.Min(ids.Count, versions.Count);
            for (int i = 0; i < count; i++)
                dict[ids[i]] = versions[i];
            typeVersions[group.Type] = dict;
        }
        _pubSub.WatcherPingUnits(cmd.WatcherId, typeVersions);
    }

    private void HandlePublishEvent(PublishEventCmd cmd)
    {
        _pubSub.HandleNatifyPublishEvent(cmd.UnitId, cmd.UnitType, cmd.EventName,
            cmd.Data != null ? cmd.Data.ToByteArray() : null, !cmd.UseUdp);
    }

    // ===== Outbound =====

    public void OnBatchEnter(IUnit unit, List<long> watcherIds)
    {
        var data = ByteString.CopyFrom(unit.Data ?? Array.Empty<byte>());
        var msg = new BatchEnterMsg
        {
            UnitId = unit.Id,
            UnitType = unit.Type,
            PosX = unit.Position.x,
            PosY = unit.Position.y,
            Data = data,
            Version = unit.Version
        };
        msg.WatcherIds.AddRange(watcherIds);

        _natify.Publish(EvtTopic, new PubSubEvent { BatchEnter = msg });
    }

    public void OnBatchLeave(IUnit unit, List<long> watcherIds)
    {
        var msg = new BatchLeaveMsg
        {
            UnitId = unit.Id,
            UnitType = unit.Type
        };
        msg.WatcherIds.AddRange(watcherIds);

        _natify.Publish(EvtTopic, new PubSubEvent { BatchLeave = msg });
    }

    public void OnSyncEnter(long watcherId, List<IUnit> units)
    {
        var msg = new SyncEnterMsg { WatcherId = watcherId };
        foreach (var u in units)
        {
            var data = ByteString.CopyFrom(u.Data ?? Array.Empty<byte>());
            msg.Units.Add(new UnitEnterItem
            {
                Id = u.Id,
                Type = u.Type,
                PosX = u.Position.x,
                PosY = u.Position.y,
                Data = data,
                Version = u.Version
            });
        }

        _natify.Publish(EvtTopic, new PubSubEvent { SyncEnter = msg });
    }

    public void OnSyncLeave(long watcherId, List<UnitKey> keys)
    {
        var msg = new SyncLeaveMsg { WatcherId = watcherId };
        var groups = new Dictionary<string, TypeGroup>();
        foreach (var k in keys)
        {
            if (!groups.TryGetValue(k.Type, out var g))
            {
                g = new TypeGroup { Type = k.Type };
                groups[k.Type] = g;
            }
            g.UnitIds.Add(k.Id);
        }
        msg.Keys.AddRange(groups.Values);

        _natify.Publish(EvtTopic, new PubSubEvent { SyncLeave = msg });
    }

    public void OnUnitEvent(IUnit unit, List<long> watcherIds, string eventName, object? data, bool reliable)
    {
        var msg = new UnitEventMsg
        {
            UnitId = unit.Id,
            UnitType = unit.Type,
            EventName = eventName,
            Data = data is byte[] b ? ByteString.CopyFrom(b) : ByteString.Empty,
            UseUdp = !reliable
        };
        msg.WatcherIds.AddRange(watcherIds);

        _natify.Publish(EvtTopic, new PubSubEvent { UnitEvent = msg });
    }

    public void Dispose()
    {
        _natify.Dispose();
    }
}
