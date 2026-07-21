using System;
using UnityEngine;

#nullable enable

namespace PubSubLib
{
    public class UnitTemplate : IUnit
    {
        public event Action<byte[]>? Init;
        private bool _isCommited;
        public long Id { get; } = 0;
        public string Type { get; } = string.Empty;
        public Vector2 Position { get; set; }
        public bool IsAlive { get; } = false;
        public object? Target { get; } = null;
        public int Version { get; } = 0;

        public byte[]? Data
        {
            get => null;
            set
            {
                if (_isCommited) return;
                _isCommited = true;
                if (value != null && Init != null)
                {
                    Init.Invoke(value);
                }
            }
        }

        public void PublishEvent(string eventName, object? data, bool reliable = true)
        {
        }

        public void Destroy()
        {
        }
    }
}