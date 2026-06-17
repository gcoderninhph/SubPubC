using Natify;
using PubSubLib.Messages;

namespace PubSubLibTest;

public class NatifyFullRoundtripTests : IDisposable
{
    private const string NatsUrl = "nats://localhost:4222";

    private readonly NatifyServer _server;
    private readonly NatifyClientFast _client;

    public NatifyFullRoundtripTests()
    {
        _server = new NatifyServer(NatsUrl, "testSrv", "SrvGroup", "testCli");
        _client = new NatifyClientFast(NatsUrl, "testCli", "CliGroup", "VN", "testSrv");
        Thread.Sleep(2000);
    }

    public void Dispose()
    {
        _server.Dispose();
        _client.Dispose();
    }

    [Fact]
    public void Roundtrip_RequestResponse()
    {
        // Client subscribes to response
        var clientReceived = new ManualResetEventSlim();
        PubSubCommand? relayed = null;
        _client.OnMessage<PubSubCommand>("resp", data =>
        {
            relayed = data.Value;
            clientReceived.Set();
        });

        // Server relays from "req" to "resp"
        _server.OnMessage<PubSubCommand>("req", args =>
        {
            _server.Publish("resp", "VN", args.data.Value);
        });

        Thread.Sleep(500);

        _client.Publish("req", new PubSubCommand
        {
            AddWatcher = new AddWatcherCmd { WatcherId = 999, PosX = 1, PosY = 2, Radius = 100 }
        });

        Assert.True(clientReceived.Wait(5000), "Relayed message not received");
        Assert.NotNull(relayed);
        Assert.Equal(999, relayed.AddWatcher.WatcherId);
    }
}
