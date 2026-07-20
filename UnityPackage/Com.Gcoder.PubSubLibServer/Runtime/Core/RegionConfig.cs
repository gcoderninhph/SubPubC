using Natify;

namespace PubSubLib
{
    public class RegionConfig : PubSubConfig
    {
        public INatifyClient NatifyClient;

        public RegionConfig(INatifyClient natifyClient)
        {
            NatifyClient = natifyClient;
        }
    }
}