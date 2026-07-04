using Natify;
using PubSubLib.Contracts;
using PubSubLib.Messages;

namespace PubSubLib;

internal sealed class RegionNatifySync : IDisposable
{
    private readonly INatifyAdapter _natify;
    private readonly RegionModule _region;

    private const string CmdTopic = "Region.Cmd";
    private const string EvtTopic = "Region.Evt";

    private Action<CreateUnitCmd>? _onCreateUnit;
    private Action<DestroyUnitCmd>? _onDestroyUnit;

    public RegionNatifySync(INatifyAdapter natify, RegionModule region)
    {
        _natify = natify;
        _region = region;

        _natify.Subscribe<RegionCommand>(CmdTopic, OnCommand);
    }

    public void OnCreateUnitCmd(Action<CreateUnitCmd> callback) { _onCreateUnit = callback; }
    public void OnDestroyUnitCmd(Action<DestroyUnitCmd> callback) { _onDestroyUnit = callback; }

    private void OnCommand(Data<RegionCommand> data)
    {
        try
        {
            var cmd = data.Value;
            switch (cmd.CmdCase)
            {
                case RegionCommand.CmdOneofCase.CreateUnit:
                    _onCreateUnit?.Invoke(cmd.CreateUnit);
                    break;
                case RegionCommand.CmdOneofCase.DestroyUnit:
                    _onDestroyUnit?.Invoke(cmd.DestroyUnit);
                    break;
            }
        }
        catch (Exception ex) { PubSubLog.Error(ex, "RegionNatifySync.OnCommand failed"); }
    }

    public void PublishCreateUnit(CreateUnitEvt evt)
    {
        _natify.Publish(EvtTopic, new RegionEvent { CreateUnit = evt });
    }

    public void PublishDestroyUnit(DestroyUnitEvt evt)
    {
        _natify.Publish(EvtTopic, new RegionEvent { DestroyUnit = evt });
    }

    public void Dispose()
    {
        _natify.Dispose();
    }
}
