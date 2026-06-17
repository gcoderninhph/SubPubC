using System.Collections.Concurrent;

namespace PubSubLib;

internal static class ListPool<T>
{
    private const int DefaultMaxSize = 32;
    private static readonly ConcurrentBag<List<T>> _pool = new();
    private static int _maxSize = DefaultMaxSize;

    public static int MaxSize
    {
        get => _maxSize;
        set => _maxSize = value > 0 ? value : DefaultMaxSize;
    }

    public static List<T> Rent()
    {
        if (_pool.TryTake(out var list))
            return list;
        return new List<T>();
    }

    public static void Return(List<T> list)
    {
        list.Clear();
        if (_pool.Count < _maxSize)
            _pool.Add(list);
    }
}
