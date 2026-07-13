namespace PubSubLib.Mirror
{
    public interface IMirrorListItemDirtyProxy
    {
        void SetDirtyMarker(System.Action? markDirty);
    }
}
