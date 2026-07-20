using System;
using System.Runtime.CompilerServices;

namespace PubSubLib.Contracts
{
    public sealed class PubSubLogEntry
    {
        public Exception Exception { get; }
        public string Message { get; }
        public string Source { get; }
        public DateTime Timestamp { get; }

        internal PubSubLogEntry(Exception ex, string msg, string src)
        {
            Exception = ex;
            Message = msg;
            Source = src;
            Timestamp = DateTime.UtcNow;
        }
    }

    public static class PubSubLog
    {
        private static event Action<PubSubLogEntry>? _onError;
        private static readonly object _lock = new();

        public static event Action<PubSubLogEntry>? OnError
        {
            add
            {
                lock (_lock) _onError += value;
            }
            remove
            {
                lock (_lock) _onError -= value;
            }
        }

        public static void Error(Exception ex, string message,
            [CallerMemberName] string source = "")
        {
            var handler = _onError;
            if (handler == null) return;
            var entry = new PubSubLogEntry(ex, message, source);
            foreach (Action<PubSubLogEntry> h in handler.GetInvocationList())
            {
                try
                {
                    h(entry);
                }
                catch
                {
                }
            }
        }
    }
}