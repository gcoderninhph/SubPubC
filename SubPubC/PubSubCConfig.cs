using Microsoft.Extensions.DependencyInjection;
using NATS.Client;
using Serilog;

#nullable enable

namespace SubPubC;

public static class PubSubCConfig
{

    private static IConnection? _natsConnection { get; set; }
    private static float _gridSize { get; set; } = 100;
    public static IConnection NatsConnection => _natsConnection ?? throw new Exception("NATS Connection is not initialized. Please call AddPubSubC in your IServiceCollection first.");
    public static float GridSize => _gridSize;

    public static IServiceCollection AddPubSubC(
            this IServiceCollection services, float gridSize = 100, string nats = "nats://localhost:4222")
    {

        _natsConnection = new ConnectionFactory().CreateConnection(nats);
        _gridSize = gridSize;

        var unitEnter = NatsConnection.SubscribeAsync("Unit.Enter");
        var unitMove = NatsConnection.SubscribeAsync("Unit.Move");
        var unitEvent = NatsConnection.SubscribeAsync("Unit.Event");
        var unitExit = NatsConnection.SubscribeAsync("Unit.Exit");
        var watcherEnter = NatsConnection.SubscribeAsync("Watcher.Enter");
        var watcherMove = NatsConnection.SubscribeAsync("Watcher.Move");
        var watcherExit = NatsConnection.SubscribeAsync("Watcher.Exit");

        unitEnter.MessageHandler += (a, b) =>
        {
            try
            {
                var (unitId, position) = ParseUnitMessage(b.Message.Data);
                Unit.Enter(unitId, position);
            }
            catch (Exception ex)
            {
                Log.Error($"Error processing Unit.Enter message: {ex.Message}", ex);
            }
        };

        unitMove.MessageHandler += (a, b) =>
        {
            try
            {
                var (unitId, position) = ParseUnitMessage(b.Message.Data);
                Unit.Move(unitId, position);
            }
            catch (Exception ex)
            {
                Log.Error($"Error processing Unit.Move message: {ex.Message}", ex);
            }
        };

        unitEvent.MessageHandler += (a, b) =>
        {
            try
            {
                var msg = System.Text.Encoding.ASCII.GetString(b.Message.Data);
                var parts = msg.Split(',');
                if (parts.Length != 2) throw new Exception("Invalid Unit.Event message");
                var unitId = parts[0];
                var eventName = parts[1];
                Unit.Event(unitId, eventName);
            }
            catch (Exception ex)
            {
                Log.Error($"Error processing Unit.Event message: {ex.Message}", ex);
            }
        };

        unitExit.MessageHandler += (a, b) =>
        {
            try
            {
                var unitId = System.Text.Encoding.ASCII.GetString(b.Message.Data);
                Unit.Exit(unitId);
            }
            catch (Exception ex)
            {
                Log.Error($"Error processing Unit.Leave message: {ex.Message}", ex);
            }
        };

        watcherEnter.MessageHandler += (a, b) =>
        {
            try
            {
                var (watcherId, position, range) = ParseWatcherMessage(b.Message.Data); ;
                Log.Information($"Watcher.Enter: {watcherId} at position ({position.x}, {position.y}) with range {range}");
                Watcher.Enter(watcherId, position, range);
            }
            catch (Exception ex)
            {
                Log.Error($"Error processing Watcher.Enter message: {ex.Message}", ex);
            }
        };

        watcherMove.MessageHandler += (a, b) =>
        {
            try
            {
                var (watcherId, position, range) = ParseWatcherMessage(b.Message.Data); ;
                Watcher.Move(watcherId, position, range);
            }
            catch (Exception ex)
            {
                Log.Error($"Error processing Watcher.Move message: {ex.Message}", ex);
            }
        };

        watcherExit.MessageHandler += (a, b) =>
        {
            try
            {
                var watcherId = System.Text.Encoding.ASCII.GetString(b.Message.Data);
                Log.Information($"Watcher.Exit: {watcherId}");
                Watcher.Exit(watcherId);
            }
            catch (Exception ex)
            {
                Log.Error($"Error processing Watcher.Leave message: {ex.Message}", ex);
            }
        };


        Watcher.OnUnitEnter += (watcherId, unitIds) =>
        {
            try
            {
                string msg = string.Join(',', unitIds);
                NatsConnection.Publish($"Watcher.{watcherId}.Unit.Enter", System.Text.Encoding.ASCII.GetBytes(msg));
            }
            catch (Exception ex)
            {
                Log.Error($"Error publishing Watcher.{watcherId}.Unit.Enter message: {ex.Message}", ex);
            }

        };

        Watcher.OnUnitEvent += (watcherId, unitId, eventName) =>
        {
            try
            {
                NatsConnection.Publish($"Watcher.{watcherId}.Unit.Event.{eventName}", System.Text.Encoding.ASCII.GetBytes(unitId));
            }
            catch (Exception ex)
            {
                Log.Error($"Error publishing Watcher.{watcherId}.Unit.Event.{eventName} message: {ex.Message}", ex);
            }
        };

        Watcher.OnUnitExit += (watcherId, unitIds) =>
        {
            try
            {
                string msg = string.Join(',', unitIds);
                NatsConnection.Publish($"Watcher.{watcherId}.Unit.Exit", System.Text.Encoding.ASCII.GetBytes(msg));
            }
            catch (Exception ex)
            {
                Log.Error($"Error publishing Watcher.{watcherId}.Unit.Exit message: {ex.Message}", ex);
            }
        };


        unitEnter.Start();
        unitMove.Start();
        unitEvent.Start();
        unitExit.Start();
        watcherEnter.Start();
        watcherMove.Start();
        watcherExit.Start();

        return services;
    }


    private static (string unitId, Vec2 position) ParseUnitMessage(byte[] data)
    {
        string msg = System.Text.Encoding.ASCII.GetString(data);
        string[] msgs = msg.Split(',');
        if (msgs.Length != 3) throw new Exception("Invalid Unit Message");
        string unitId = msgs[0];
        Vec2 position = new()
        {
            x = float.Parse(msgs[1]),
            y = float.Parse(msgs[2])
        };
        return (unitId, position);
    }

    private static (string watcherId, Vec2 position, float range) ParseWatcherMessage(byte[] data)
    {
        string msg = System.Text.Encoding.ASCII.GetString(data);
        string[] msgs = msg.Split(',');
        if (msgs.Length != 4) throw new Exception("Invalid Watcher Message");
        string watcherId = msgs[0];
        Vec2 position = new()
        {
            x = float.Parse(msgs[1]),
            y = float.Parse(msgs[2])
        };
        float range = float.Parse(msgs[3]);
        return (watcherId, position, range);
    }
}

public struct Vec2
{
    public float x;
    public float y;
}

public static class SubC
{
    public static string[] GetAllGridCellsInRange(Vec2 position, float range)
    {
        List<string> cells = [];

        int minX = (int)Math.Floor((position.x - range) / PubSubCConfig.GridSize);
        int maxX = (int)Math.Floor((position.x + range) / PubSubCConfig.GridSize);
        int minY = (int)Math.Floor((position.y - range) / PubSubCConfig.GridSize);
        int maxY = (int)Math.Floor((position.y + range) / PubSubCConfig.GridSize);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                cells.Add($"{x}:{y}");
            }
        }

        return [.. cells];
    }

    public static string GetGridCellByPosition(Vec2 position)
    {
        int cellX = (int)Math.Floor(position.x / PubSubCConfig.GridSize);
        int cellY = (int)Math.Floor(position.y / PubSubCConfig.GridSize);
        return $"{cellX}:{cellY}";
    }

}
