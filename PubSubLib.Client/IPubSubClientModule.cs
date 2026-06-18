using MyConnection;

namespace PubSubLib.Client;

public interface IPubSubClientModule : IClientModule
{
    static IPubSubClientModule Create(Config config)
    {
        return new PubSubClientModule(config);
    }

    IPubSubClient Get();
}