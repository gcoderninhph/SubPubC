using Natify;

namespace PubSubLib
{
    public class PlayerSpeakerConfig
    {
        public int PlayerTimeoutSeconds = 5;
        public int PlayerCleanupIntervalSeconds = 2;
        public INatifyClient? ClientFast = null;
    }
}