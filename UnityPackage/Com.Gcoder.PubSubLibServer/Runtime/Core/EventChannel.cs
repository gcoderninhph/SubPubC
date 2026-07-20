using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using PubSubLib.Contracts;

namespace PubSubLib
{
    internal sealed class EventChannel : IDisposable
    {
        private readonly Channel<Action> _channel;
        private readonly ChannelWriter<Action> _writer;
        private readonly ChannelReader<Action> _reader;
        private readonly Thread _worker;
        private readonly CancellationTokenSource _cts;

        private readonly ReaderWriterLockSlim _subLock = new();

        private List<Action<(List<long>, IUnit)>> _onUnitEnterBatch = new();
        private List<Action<(List<long>, IUnit)>> _onUnitLeaveBatch = new();
        private List<Action<(long, List<IUnit>)>> _onUnitEnterSync = new();
        private List<Action<(long, List<UnitKey>)>> _onUnitLeaveSync = new();
        private List<Action<(List<long>, IUnit, string, object, bool)>> _onUnitEvent = new();

        private Action _onIdleCheck = () => { };

        internal void SetOnIdleCheck(Action handler)
        {
            _onIdleCheck = handler ?? (() => { });
        }

        internal ISubscrible AddOnUnitEnterBatch(Action<(List<long>, IUnit)> cb)
        {
            _subLock.EnterWriteLock();
            try
            {
                _onUnitEnterBatch.Add(cb);
            }
            finally
            {
                _subLock.ExitWriteLock();
            }

            return new Subscrible(() =>
            {
                _subLock.EnterWriteLock();
                try
                {
                    _onUnitEnterBatch.Remove(cb);
                }
                finally
                {
                    _subLock.ExitWriteLock();
                }
            });
        }

        internal ISubscrible AddOnUnitLeaveBatch(Action<(List<long>, IUnit)> cb)
        {
            _subLock.EnterWriteLock();
            try
            {
                _onUnitLeaveBatch.Add(cb);
            }
            finally
            {
                _subLock.ExitWriteLock();
            }

            return new Subscrible(() =>
            {
                _subLock.EnterWriteLock();
                try
                {
                    _onUnitLeaveBatch.Remove(cb);
                }
                finally
                {
                    _subLock.ExitWriteLock();
                }
            });
        }

        internal ISubscrible AddOnUnitEnterSync(Action<(long, List<IUnit>)> cb)
        {
            _subLock.EnterWriteLock();
            try
            {
                _onUnitEnterSync.Add(cb);
            }
            finally
            {
                _subLock.ExitWriteLock();
            }

            return new Subscrible(() =>
            {
                _subLock.EnterWriteLock();
                try
                {
                    _onUnitEnterSync.Remove(cb);
                }
                finally
                {
                    _subLock.ExitWriteLock();
                }
            });
        }

        internal ISubscrible AddOnUnitLeaveSync(Action<(long, List<UnitKey>)> cb)
        {
            _subLock.EnterWriteLock();
            try
            {
                _onUnitLeaveSync.Add(cb);
            }
            finally
            {
                _subLock.ExitWriteLock();
            }

            return new Subscrible(() =>
            {
                _subLock.EnterWriteLock();
                try
                {
                    _onUnitLeaveSync.Remove(cb);
                }
                finally
                {
                    _subLock.ExitWriteLock();
                }
            });
        }

        internal ISubscrible AddOnUnitEvent(Action<(List<long>, IUnit, string, object, bool)> cb)
        {
            _subLock.EnterWriteLock();
            try
            {
                _onUnitEvent.Add(cb);
            }
            finally
            {
                _subLock.ExitWriteLock();
            }

            return new Subscrible(() =>
            {
                _subLock.EnterWriteLock();
                try
                {
                    _onUnitEvent.Remove(cb);
                }
                finally
                {
                    _subLock.ExitWriteLock();
                }
            });
        }

        internal List<Action<(List<long>, IUnit)>> SnapshotOnUnitEnterBatch()
        {
            _subLock.EnterReadLock();
            try
            {
                return new List<Action<(List<long>, IUnit)>>(_onUnitEnterBatch);
            }
            finally
            {
                _subLock.ExitReadLock();
            }
        }

        internal List<Action<(List<long>, IUnit)>> SnapshotOnUnitLeaveBatch()
        {
            _subLock.EnterReadLock();
            try
            {
                return new List<Action<(List<long>, IUnit)>>(_onUnitLeaveBatch);
            }
            finally
            {
                _subLock.ExitReadLock();
            }
        }

        internal List<Action<(long, List<IUnit>)>> SnapshotOnUnitEnterSync()
        {
            _subLock.EnterReadLock();
            try
            {
                return new List<Action<(long, List<IUnit>)>>(_onUnitEnterSync);
            }
            finally
            {
                _subLock.ExitReadLock();
            }
        }

        internal List<Action<(long, List<UnitKey>)>> SnapshotOnUnitLeaveSync()
        {
            _subLock.EnterReadLock();
            try
            {
                return new List<Action<(long, List<UnitKey>)>>(_onUnitLeaveSync);
            }
            finally
            {
                _subLock.ExitReadLock();
            }
        }

        internal List<Action<(List<long>, IUnit, string, object, bool)>> SnapshotOnUnitEvent()
        {
            _subLock.EnterReadLock();
            try
            {
                return new List<Action<(List<long>, IUnit, string, object, bool)>>(_onUnitEvent);
            }
            finally
            {
                _subLock.ExitReadLock();
            }
        }

        public EventChannel()
        {
            _channel = Channel.CreateUnbounded<Action>();
            _writer = _channel.Writer;
            _reader = _channel.Reader;
            _cts = new CancellationTokenSource();
            _worker = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "PubSubLib.EventChannel"
            };
        }

        public void Start() => _worker.Start();

        public void Enqueue(Action action)
        {
            _writer.TryWrite(action);
        }

        public void Dispose()
        {
            _cts.Cancel();
            _writer.Complete();
            _worker.Join(3000);
            _cts.Dispose();
            _subLock.Dispose();
        }

        private void WorkerLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    _reader.WaitToReadAsync(_cts.Token).AsTask().Wait(1000);
                }
                catch (Exception ex)
                {
                    PubSubLog.Error(ex, "EventChannel WaitToReadAsync failed");
                }

                while (_reader.TryRead(out var action))
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        PubSubLog.Error(ex, "EventChannel action failed");
                    }
                }

                try
                {
                    _onIdleCheck();
                }
                catch (Exception ex)
                {
                    PubSubLog.Error(ex, "EventChannel idle check failed");
                }
            }
        }
    }
}