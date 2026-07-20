using System;
using Google.Protobuf;
using MyConnection;
using PubSubLib.Contracts;
using PubSubLib.Messages;

namespace PubSubLib.Client
{
    internal sealed class PlayerSpeaksClientModule : IPlayerSpeaksClientModule
    {
        private readonly PlayerSpeaksClient _client;
        private readonly int _pingIntervalMs;
        private IClient? _clientPtr;
        private ISubscribe? _tcpSub;
        private ISubscribe? _msgSub;
        private ISubscribe? _welcomeSub;
        private long _lastPingTicks;
        private bool _playerIdSet;
    
        public PlayerSpeaksClientModule(int pingIntervalMs)
        {
            _pingIntervalMs = pingIntervalMs;
            _client = new PlayerSpeaksClient();
            _client.OnSendToServer = (dataName, playerId, subject, bytes) =>
            {
                _clientPtr?.SendOnTcp("PlayerSpeaks.ClientMsg", new ClientMirrorMessage
                {
                    DataName = dataName,
                    PlayerId = playerId,
                    Subject = subject,
                    Data = ByteString.CopyFrom(bytes)
                });
            };
        }
    
        public void SetIClient(IClient client)
        {
            _clientPtr = client;
            _welcomeSub = client.SubscribeTcp<PlayerSpeaksWelcomeMsg>("PlayerSpeaks.Welcome", OnWelcome);
            _tcpSub = client.SubscribeTcp<PlayerSpeaksEvent>("PlayerSpeaks.Evt", OnEvent);
            _msgSub = client.SubscribeTcp<MirrorMessageEvent>("PlayerSpeaks.Msg", OnMsg);
        }
    
        private void OnWelcome(PlayerSpeaksWelcomeMsg msg)
        {
            try
            {
                _client.SetPlayerId(msg.PlayerId);
                _playerIdSet = true;
            }
            catch (Exception ex) { PubSubLog.Error(ex, "OnWelcome failed"); }
        }
    
        private void OnEvent(PlayerSpeaksEvent evt)
        {
            try { _client.ApplyUpdate(evt.DataName, evt.Data.ToByteArray(), evt.Commit); }
            catch (Exception ex) { PubSubLog.Error(ex, "OnEvent failed"); }
        }
    
        private void OnMsg(MirrorMessageEvent msg)
        {
            try { _client.DispatchMessage(msg.DataName, msg.Subject, msg.Data.ToByteArray()); }
            catch (Exception ex) { PubSubLog.Error(ex, "OnMsg failed"); }
        }
    
        public IPlayerSpeaksClient Get()
        {
            return _client;
        }
    
        public void Tick()
        {
            if (!_playerIdSet || _clientPtr == null) return;
    
            var now = DateTime.UtcNow.Ticks;
            if (now - _lastPingTicks < _pingIntervalMs * TimeSpan.TicksPerMillisecond) return;
    
            _lastPingTicks = now;
            _clientPtr.SendOnTcp("PlayerSpeaks.Ping", new PlayerPingMsg { PlayerId = _client.PlayerId });
        }
    
        public void Dispose()
        {
            _welcomeSub?.UnSubscribe();
            _tcpSub?.UnSubscribe();
            _msgSub?.UnSubscribe();
            _client.Dispose();
        }
    }

}

