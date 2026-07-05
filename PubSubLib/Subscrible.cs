using System.Threading;

namespace PubSubLib;

internal sealed class Subscrible : ISubscrible
{
    private Action? _unsubscribe;

    internal Subscrible(Action unsubscribe)
    {
        _unsubscribe = unsubscribe;
    }

    public void UnSubscribe()
    {
        var u = Interlocked.Exchange(ref _unsubscribe, null);
        u?.Invoke();
    }
}
