using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace PubSubLib.Mirror;

public class MirrorRepeatedList<T> : IList<T>
{
    private readonly List<T> _list = new();

    public int Count => _list.Count;
    public bool IsReadOnly => false;

    public T this[int index]
    {
        get => _list[index];
        set
        {
            _list[index] = value;
            IsDirty = true;
        }
    }

    public bool IsDirty { get; private set; }

    public void ClearDirty()
    {
        IsDirty = false;
    }

    public T[] ToArray()
    {
        return _list.ToArray();
    }

    public void Add(T item)
    {
        _list.Add(item);
        IsDirty = true;
    }

    public void Clear()
    {
        _list.Clear();
        IsDirty = true;
    }

    public bool Contains(T item)
    {
        return _list.Contains(item);
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        _list.CopyTo(array, arrayIndex);
    }

    public int IndexOf(T item)
    {
        return _list.IndexOf(item);
    }

    public void Insert(int index, T item)
    {
        _list.Insert(index, item);
        IsDirty = true;
    }

    public bool Remove(T item)
    {
        var result = _list.Remove(item);
        if (result)
            IsDirty = true;
        return result;
    }

    public void RemoveAt(int index)
    {
        _list.RemoveAt(index);
        IsDirty = true;
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
