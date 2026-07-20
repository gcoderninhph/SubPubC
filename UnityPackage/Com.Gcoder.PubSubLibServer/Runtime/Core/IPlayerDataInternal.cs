using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace PubSubLib
{

public interface IPlayerDataInternal : IPlayerData
{
    bool IsInitDone { get; }
    void SetOnline(bool isOnline);
    void SetPlayerId(long playerId);
    System.Collections.Generic.List<System.Action> PrepareMessageDispatch(string subject, byte[] data);
}
}
