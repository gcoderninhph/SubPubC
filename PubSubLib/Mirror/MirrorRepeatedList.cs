using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace PubSubLib.Mirror;

public class MirrorRepeatedList<T> : IList<T>
{
    private readonly List<T> _list = new();
    private readonly object _lock = new();
    private bool _isDirty;

    public int Count => _list.Count;
    public bool IsReadOnly => false;

    public T this[int index]
    {
        get => _list[index];
        set
        {
            lock (_lock)
            {
                _list[index] = value;
                _isDirty = true;
            }
        }
    }

    public bool IsDirty
    {
        get { lock (_lock) return _isDirty; }
        private set { lock (_lock) _isDirty = value; }
    }

    public void ClearDirty()
    {
        lock (_lock) _isDirty = false;
    }

    public T[]? TrySnapshot()
    {
        lock (_lock)
        {
            if (!_isDirty) return null;
            _isDirty = false;
            return _list.ToArray();
        }
    }

    public T[] ToArray()
    {
        lock (_lock) return _list.ToArray();
    }

    public void Add(T item)
    {
        lock (_lock)
        {
            _list.Add(item);
            _isDirty = true;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _list.Clear();
            _isDirty = true;
        }
    }

    public bool Contains(T item)
    {
        return _list.Contains(item);
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        lock (_lock) _list.CopyTo(array, arrayIndex);
    }

    public int IndexOf(T item)
    {
        return _list.IndexOf(item);
    }

    public void Insert(int index, T item)
    {
        lock (_lock)
        {
            _list.Insert(index, item);
            _isDirty = true;
        }
    }

    public bool Remove(T item)
    {
        lock (_lock)
        {
            if (_list.Remove(item))
            {
                _isDirty = true;
                return true;
            }
            return false;
        }
    }

    public void RemoveAt(int index)
    {
        lock (_lock)
        {
            _list.RemoveAt(index);
            _isDirty = true;
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        return _list.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return _list.GetEnumerator();
    }
}
