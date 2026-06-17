using Google.Protobuf;
using Natify;

namespace PubSubLib;

internal interface INatifyAdapter : IDisposable
{
    void Publish<T>(string topic, T msg) where T : Google.Protobuf.IMessage;
    void Subscribe<T>(string topic, Action<Data<T>> handler) where T : Google.Protobuf.IMessage, new();
}
