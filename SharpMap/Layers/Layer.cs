// Copyright 2005, 2006 - Morten Nielsen (www.iter.dk)
//
// This file is part of SharpMap.
// SharpMap is free software; you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
// 
// SharpMap is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Lesser General Public License for more details.

// You should have received a copy of the GNU Lesser General Public License
// along with SharpMap; if not, write to the Free Software
// Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA 

using System;
using System.Linq;
using ProjNet.CoordinateSystems.Transformations;
using SharpMap.Fetcher;
using SharpMap.Geometries;
using SharpMap.Projection;
using SharpMap.Providers;
using System.Threading;
using System.Collections.Generic;

namespace SharpMap.Layers
{
    public class Layer : BaseLayer
    {
        private bool isFetching;
        private bool needsUpdate = true;
        private double newResolution;
        private BoundingBox newExtent;
        private MemoryProvider cache;

        public IProvider DataSource { get; set; }

        /// <summary>
        /// Gets or sets the SRID of this VectorLayer's data source
        /// </summary>
        public new int SRID
        {
            get
            {
                if (DataSource == null)
                    throw (new Exception("DataSource property not set on layer '" + LayerName + "'"));
                return DataSource.SRID;
            }
        }

        public new ICoordinateTransformation CoordinateTransformation { get; set; }

        /// <summary>
        /// Returns the extent of the layer
        /// </summary>
        /// <returns>Bounding box corresponding to the extent of the features in the layer</returns>
        public override BoundingBox Envelope
        {
            get
            {
                if (DataSource == null) return null;

                lock (DataSource)
                {
                    bool wasOpen = DataSource.IsOpen;
                    if (!wasOpen)
                        DataSource.Open();
                    BoundingBox box = DataSource.GetExtents();
                    if (!wasOpen) //Restore state
                        DataSource.Close();
                    if (CoordinateTransformation != null)
                        return ProjectionHelper.Transform(box, CoordinateTransformation);
                    return box;
                }
            }
        }

        public Layer(string layername) 
        {
            LayerName = layername;
            cache = new MemoryProvider();
        }
        
        public override IEnumerable<IFeature> GetFeaturesInView(BoundingBox box, double resolution)
        {
            return cache.GetFeaturesInView(box, resolution);
        }

        public override IEnumerable<IFeature> GetFeatureInfo(BoundingBox box, double resolution)
        {
            return GetFeaturesInView(box, resolution);
        }

        public override void AbortFetch()
        {
        }

        public override void ViewChanged(bool changeEnd, BoundingBox extent, double resolution)
        {
            if (!Enabled) return;
            if (DataSource == null) return;
            if (!changeEnd) return;
            
            newExtent = extent;
            newResolution = resolution;

            if (isFetching)
            {
                needsUpdate = true;
                return;
            }
            StartNewFetch(extent, resolution);
        }

        private void StartNewFetch(BoundingBox extent, double resolution)
        {
            isFetching = true;
            needsUpdate = false;
            
            if (CoordinateTransformation != null)
                extent = ProjectionHelper.InverseTransform(extent, CoordinateTransformation);

            var fetcher = new Fetcher(extent, resolution, DataSource, DataArrived);
            new Thread(fetcher.FetchOnThread).Start();
        }

        private void DataArrived(IEnumerable<IFeature> features)
        {
            //the data in the cache is stored in the map projection so it projected only once.
            if (features == null) throw new ArgumentException("argument features may not be null");

            features = features.ToList();
            if (CoordinateTransformation != null)
                foreach (var feature in features)
                    ProjectionHelper.Transform(feature.Geometry, CoordinateTransformation);
            
            cache = new MemoryProvider(features);

            isFetching = false;
            OnDataChanged();

            if (needsUpdate)
            {
                StartNewFetch(newExtent, newResolution);
            }
        }

        protected void OnDataChanged()
        {
            if (DataChanged != null)
            {
                DataChanged(this, new DataChangedEventArgs(null, false, null, LayerName));
            }
        }

        private delegate void DataArrivedDelegate(IEnumerable<IFeature> features);

        private class Fetcher
        {
            private readonly BoundingBox extent;
            private readonly double resolution;
            private readonly DataArrivedDelegate dataArrived;
            private readonly IProvider provider;

            public Fetcher(BoundingBox extent, double resolution, IProvider provider, DataArrivedDelegate dataArrived)
            {
                this.dataArrived = dataArrived;
                this.extent = extent;
                this.provider = provider;
                this.resolution = resolution;
            }

            public void FetchOnThread()
            {
                lock (provider)
                {
                    provider.Open();
                    var features = provider.GetFeaturesInView(extent, resolution);
                    provider.Close();
                    if (dataArrived != null) dataArrived(features);
                }
            }
        }

        public override event DataChangedEventHandler DataChanged;

        public override void ClearCache()
        {
            cache.Clear();
        }
    }
}
