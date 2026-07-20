using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
﻿using Natify;

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
