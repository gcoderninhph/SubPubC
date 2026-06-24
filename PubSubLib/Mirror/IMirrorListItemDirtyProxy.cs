namespace PubSubLib.Mirror
{
    internal interface IMirrorListItemDirtyProxy
    {
        void SetDirtyMarker(System.Action? markDirty);
    }
}
