using Natify;
using PubSubLib.Messages;

namespace PubSubLibTest;

public class NatifyBasicConnectivityTests : IAsyncLifetime
{
    private const string NatsUrl = "nats://localhost:4222";

    private INatifyServer _server = null!;
    private INatifyClient _client = null!;

    public async Task InitializeAsync()
    {
        _server = await INatifyServer.CreateAsync(NatsUrl, "testSrv", "SrvGroup", "testCli");
        _client = await INatifyClient.CreateFast(NatsUrl, "testCli", "CliGroup", "VN", "testSrv");
    }

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        await _client.DisposeAsync();
    }

    [Fact]
    public void ServerPublish_ClientReceives()
    {
        var signal = new ManualResetEventSlim();
        PubSubCommand? received = null;

        _client.OnMessage<PubSubCommand>("test.cmd", data =>
        {
            received = data.Value;
            signal.Set();
        });

        Thread.Sleep(1000);

        _server.Publish("test.cmd", "VN", new PubSubCommand
        {
            AddWatcher = new AddWatcherCmd { WatcherId = 99, PosX = 1, PosY = 2, Radius = 100 }
        });

        Assert.True(signal.Wait(5000), "Client did not receive message from server");
        Assert.NotNull(received);
        Assert.Equal(99, received.AddWatcher.WatcherId);
    }

    [Fact]
    public void ClientPublish_ServerReceives()
    {
        var signal = new ManualResetEventSlim();
        PubSubCommand? received = null;

        _server.OnMessage<PubSubCommand>("test.cmd2", args =>
        {
            received = args.data.Value;
            signal.Set();
        });

        Thread.Sleep(1000);

        _client.Publish("test.cmd2", new PubSubCommand
        {
            RemoveWatcher = new RemoveWatcherCmd { WatcherId = 55 }
        });

        Assert.True(signal.Wait(5000), "Server did not receive message from client");
        Assert.NotNull(received);
        Assert.Equal(55, received.RemoveWatcher.WatcherId);
    }
}
