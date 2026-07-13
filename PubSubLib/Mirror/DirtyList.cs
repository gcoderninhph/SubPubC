using System;
using System.Collections;
using System.Collections.Generic;

namespace PubSubLib.Mirror
{
    public sealed class DirtyList<T> : IList<T>, IReadOnlyList<T>
    {
        private readonly List<T> _list = new();
        private System.Action? _onDirty;

        public int Count => _list.Count;
        public bool IsReadOnly => false;

        public T this[int index]
        {
            get => _list[index];
            set
            {
                if (!EqualityComparer<T>.Default.Equals(_list[index], value))
                {
                    _list[index] = value;
                    _onDirty?.Invoke();
                }
            }
        }

        public DirtyList() { }

        public DirtyList(IEnumerable<T> items, System.Action? onDirty)
        {
            if (items != null)
                _list.AddRange(items);
            _onDirty = onDirty;
        }

        public void SetDirtyCallback(System.Action? onDirty)
        {
            _onDirty = onDirty;
        }

        public void Add(T item)
        {
            _list.Add(item);
            _onDirty?.Invoke();
        }

        public void Insert(int index, T item)
        {
            _list.Insert(index, item);
            _onDirty?.Invoke();
        }

        public bool Remove(T item)
        {
            if (_list.Remove(item))
            {
                _onDirty?.Invoke();
                return true;
            }
            return false;
        }

        public void RemoveAt(int index)
        {
            _list.RemoveAt(index);
            _onDirty?.Invoke();
        }

        public void Clear()
        {
            _list.Clear();
            _onDirty?.Invoke();
        }

        public int IndexOf(T item) => _list.IndexOf(item);
        public bool Contains(T item) => _list.Contains(item);
        public void CopyTo(T[] array, int arrayIndex) => _list.CopyTo(array, arrayIndex);
        public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _list.GetEnumerator();
    }
}
