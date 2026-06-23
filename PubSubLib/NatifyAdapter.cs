using Natify;
using PubSubLib.Contracts;

namespace PubSubLib
{
    internal sealed class NatifyAdapter : INatifyAdapter
    {
        private readonly object _client;
        private readonly bool _isUnity;

        public NatifyAdapter(NatifyClientFast client)
        {
            _client = client;
            _isUnity = false;
        }

        public NatifyAdapter(NatifyClient client)
        {
            _client = client;
            _isUnity = true;
        }

        public void Publish<T>(string topic, T msg) where T : Google.Protobuf.IMessage
        {
            if (_isUnity)
                ((NatifyClient)_client).Publish(topic, msg);
            else
                ((NatifyClientFast)_client).Publish(topic, msg);
        }

        public void Subscribe<T>(string topic, Action<Data<T>> handler) where T : Google.Protobuf.IMessage, new()
        {
            if (_isUnity)
                ((NatifyClient)_client).OnMessage(topic, handler);
            else
                ((NatifyClientFast)_client).OnMessage(topic, handler);
        }

        public void SubscribeAsync<T>(string topic, Func<Data<T>, Task> handler) where T : Google.Protobuf.IMessage, new()
        {
            if (_isUnity)
                ((NatifyClient)_client).OnMessage<T>(topic, async args =>
                {
                    try { await handler(args); }
                    catch (Exception ex) { PubSubLog.Error(ex, "SubscribeAsync failed"); }
                });
            else
                ((NatifyClientFast)_client).OnMessage<T>(topic, async args =>
                {
                    try { await handler(args); }
                    catch (Exception ex) { PubSubLog.Error(ex, "SubscribeAsync failed"); }
                });
        }

        public void Dispose()
        {
            if (_isUnity)
                ((NatifyClient)_client).Dispose();
            else
                ((NatifyClientFast)_client).Dispose();
        }
    }
}
