namespace PubSubLib
{
    internal interface IPubSubInternal
    {
        void OnUnitPositionChanged(Unit unit);
        void OnUnitDestroyed(Unit unit);
        void PublishEvent(Unit unit, string eventName, object? data, bool reliable);
    }
}