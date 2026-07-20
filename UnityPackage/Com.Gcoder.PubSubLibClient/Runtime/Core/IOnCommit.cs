namespace PubSubLib.Client
{
    public interface IOnCommit
    {
        void OnCommit(string commit);
    }
}