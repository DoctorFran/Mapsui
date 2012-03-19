﻿using System;
using System.Collections.Generic;
using System.Linq;
using SharpMap.Geometries;
using SharpMap.Providers;

namespace SharpMap.Layers
{
    public interface IFeatureInfo
    {
        void GetFeatureInfo(BoundingBox box, double resolution, Action<IDictionary<string, IEnumerable<IFeature>>> callback);
    }
}
