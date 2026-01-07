#nullable enable

using System.Runtime.CompilerServices;

namespace SubPubC;

/// <summary>
/// Thread-safe dictionary that spreads operations across multiple shards to reduce contention.
/// </summary>
public sealed class DictionaryShard<TKey, TValue> : IDisposable
    where TKey : notnull
{
    private readonly int _shardCount;
    private readonly Dictionary<TKey, TValue>[] _shards;
    private readonly ReaderWriterLockSlim[] _locks;
    private readonly IEqualityComparer<TKey> _comparer;

    public DictionaryShard(int shard = 8, IEqualityComparer<TKey>? comparer = null)
    {
        if (shard <= 0)
            throw new ArgumentOutOfRangeException(nameof(shard));

        _shardCount = shard;
        _comparer = comparer ?? EqualityComparer<TKey>.Default;
        _shards = new Dictionary<TKey, TValue>[_shardCount];
        _locks = new ReaderWriterLockSlim[_shardCount];

        for (int i = 0; i < _shardCount; i++)
        {
            _shards[i] = new Dictionary<TKey, TValue>(_comparer);
            _locks[i] = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
        }
    }

    public bool TryGetValue(TKey key, out TValue value)
    {
        var (bucket, gate) = GetBucket(key);
        gate.EnterReadLock();
        try
        {
            return bucket.TryGetValue(key, out value!);
        }
        finally
        {
            gate.ExitReadLock();
        }
    }

    public bool ContainsKey(TKey key)
    {
        var (bucket, gate) = GetBucket(key);
        gate.EnterReadLock();
        try
        {
            return bucket.ContainsKey(key);
        }
        finally
        {
            gate.ExitReadLock();
        }
    }

    public TValue this[TKey key]
    {
        get
        {
            if (TryGetValue(key, out var value))
                return value;
            throw new KeyNotFoundException("Key not found in shard dictionary.");
        }
        set
        {
            var (bucket, gate) = GetBucket(key);
            gate.EnterWriteLock();
            try
            {
                bucket[key] = value;
            }
            finally
            {
                gate.ExitWriteLock();
            }
        }
    }

    public TValue GetOrAdd(TKey key, Func<TValue> factory)
    {
        if (factory == null) throw new ArgumentNullException(nameof(factory));

        var (bucket, gate) = GetBucket(key);
        gate.EnterUpgradeableReadLock();
        try
        {
            if (bucket.TryGetValue(key, out var existing))
                return existing;

            var created = factory();
            gate.EnterWriteLock();
            try
            {
                bucket[key] = created;
                return created;
            }
            finally
            {
                gate.ExitWriteLock();
            }
        }
        finally
        {
            gate.ExitUpgradeableReadLock();
        }
    }

    public bool Remove(TKey key)
    {
        var (bucket, gate) = GetBucket(key);
        gate.EnterWriteLock();
        try
        {
            return bucket.Remove(key);
        }
        finally
        {
            gate.ExitWriteLock();
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

    private (Dictionary<TKey, TValue> bucket, ReaderWriterLockSlim gate) GetBucket(TKey key)
    {
        int index = GetShardIndex(key);
        return (_shards[index], _locks[index]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetShardIndex(TKey key)
    {
        int hash = _comparer.GetHashCode(key) & int.MaxValue;
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
