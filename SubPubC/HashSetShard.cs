#nullable enable

using System.Runtime.CompilerServices;

namespace SubPubC;

/// <summary>
/// Thread-safe hash set that spreads operations across multiple shards to reduce contention.
/// </summary>
public sealed class HashSetShard<T> : IDisposable
    where T : notnull
{
    private readonly int _shardCount;
    private readonly HashSet<T>[] _shards;
    private readonly ReaderWriterLockSlim[] _locks;
    private readonly IEqualityComparer<T> _comparer;

    public HashSetShard(int shard = 8, IEqualityComparer<T>? comparer = null)
    {
        if (shard <= 0)
            throw new ArgumentOutOfRangeException(nameof(shard));

        _shardCount = shard;
        _comparer = comparer ?? EqualityComparer<T>.Default;
        _shards = new HashSet<T>[_shardCount];
        _locks = new ReaderWriterLockSlim[_shardCount];

        for (int i = 0; i < _shardCount; i++)
        {
            _shards[i] = new HashSet<T>(_comparer);
            _locks[i] = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
        }
    }

    public bool Add(T item)
    {
        var (bucket, gate) = GetBucket(item);
        gate.EnterWriteLock();
        try
        {
            return bucket.Add(item);
        }
        finally
        {
            gate.ExitWriteLock();
        }
    }

    public bool Remove(T item)
    {
        var (bucket, gate) = GetBucket(item);
        gate.EnterWriteLock();
        try
        {
            return bucket.Remove(item);
        }
        finally
        {
            gate.ExitWriteLock();
        }
    }

    public bool Contains(T item)
    {
        var (bucket, gate) = GetBucket(item);
        gate.EnterReadLock();
        try
        {
            return bucket.Contains(item);
        }
        finally
        {
            gate.ExitReadLock();
        }
    }

    public int Count
    {
        get
        {
            int total = 0;
            for (int i = 0; i < _shardCount; i++)
            {
                var gate = _locks[i];
                gate.EnterReadLock();
                try
                {
                    total += _shards[i].Count;
                }
                finally
                {
                    gate.ExitReadLock();
                }
            }
            return total;
        }
    }

    public void Clear()
    {
        for (int i = 0; i < _shardCount; i++)
        {
            var gate = _locks[i];
            gate.EnterWriteLock();
            try
            {
                _shards[i].Clear();
            }
            finally
            {
                gate.ExitWriteLock();
            }
        }
    }

    public T[] ToArray()
    {
        List<T> items = [];
        for (int i = 0; i < _shardCount; i++)
        {
            var gate = _locks[i];
            gate.EnterReadLock();
            try
            {
                items.AddRange(_shards[i]);
            }
            finally
            {
                gate.ExitReadLock();
            }
        }
        return [.. items];
    }

    private (HashSet<T> bucket, ReaderWriterLockSlim gate) GetBucket(T item)
    {
        int index = GetShardIndex(item);
        return (_shards[index], _locks[index]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetShardIndex(T item)
    {
        int hash = _comparer.GetHashCode(item) & int.MaxValue;
        return hash % _shardCount;
    }

    public void Dispose()
    {
        foreach (var gate in _locks)
        {
            gate?.Dispose();
        }
    }
}
