using MyConnection;

namespace PubSubLib.Client;

public interface IPubSubClientModule : IClientModule, IDisposable
{
    static IPubSubClientModule Create(Config config)
    {
        return new PubSubClientModule(config);
    }

    IPubSubClient Get();
}