namespace PubSubLib.Client
{
    public interface IRegionOnCommit
    {
        void OnCommitUnit(string commit);
    }
}