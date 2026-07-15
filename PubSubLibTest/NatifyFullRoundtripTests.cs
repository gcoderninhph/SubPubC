using Natify;
using PubSubLib.Messages;

namespace PubSubLibTest;

public class NatifyFullRoundtripTests : IAsyncLifetime
{
    private const string NatsUrl = "nats://localhost:4222";

    private INatifyServer _server = null!;
    private INatifyClient _client = null!;

    public async Task InitializeAsync()
    {
        _server = await INatifyServer.CreateAsync(NatsUrl, "testSrv", "SrvGroup", "testCli");
        _client = await INatifyClient.CreateFast(NatsUrl, "testCli", "CliGroup", "VN", "testSrv");
        await Task.Delay(2000);
    }

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        await _client.DisposeAsync();
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
