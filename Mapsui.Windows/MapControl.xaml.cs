﻿// Copyright 2008 - Paul den Dulk (Geodan)
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
// Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA f

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SharpMap;
using SharpMap.Fetcher;
using SharpMap.Layers;
using SharpMap.Providers;
using SharpMap.Utilities;
using SilverlightRendering;
using SharpMap.Rendering;

namespace Mapsui.Windows
{
    public partial class MapControl : UserControl
    {
        #region Fields

        private Map map;
        private readonly View view = new View();
        private Point previousMousePosition;
        private Point currentMousePosition;
        private Point downMousePosition;
        private string errorMessage;
        private readonly FpsCounter fpsCounter = new FpsCounter();
        private readonly DoubleAnimation zoomAnimation = new DoubleAnimation();
        private readonly Storyboard zoomStoryBoard = new Storyboard();
        private double toResolution = double.NaN;
        private bool mouseDown;
        private bool IsInBoxZoomMode { get; set; }
        private bool viewInitialized;
        private readonly Canvas renderCanvas = new Canvas();
        private readonly IRenderer renderer;
        private bool invalid;

        #endregion

        #region EventHandlers

        public event EventHandler ErrorMessageChanged;
        public event EventHandler<ViewChangedEventArgs> ViewChanged;
        public event EventHandler<MouseInfoEventArgs> MouseInfoOver;
        public event EventHandler MouseInfoLeave;
        public event EventHandler<MouseInfoEventArgs> MouseInfoDown;
        public event EventHandler<FeatureInfoEventArgs> FeatureInfo;

        #endregion

        #region Properties

        public IList<ILayer> MouseInfoOverLayers { get; private set; }
        public IList<ILayer> MouseInfoDownLayers { get; private set; }

        public bool ZoomToBoxMode { get; set; }
        public View View { get { return view; } }


        public Map Map
        {
            get
            {
                return map;
            }
            set
            {
                if (map != null)
                {
                    var temp = map;
                    map = null;
                    temp.Dispose();
                }

                map = value;
                //all changes of all layers are returned through this event handler on the map
                if (map != null)
                {
                    map.DataChanged += MapDataChanged;
                }
                OnViewChanged(true);
                RefreshGraphics();
            }
        }

        public FpsCounter FpsCounter
        {
            get
            {
                return fpsCounter;
            }
        }

        public string ErrorMessage
        {
            get
            {
                return errorMessage;
            }
        }

        #endregion

        #region Dependency Properties

        private static readonly DependencyProperty ResolutionProperty =
          DependencyProperty.Register(
          "Resolution", typeof(double), typeof(MapControl),
          new PropertyMetadata(OnResolutionChanged));

        #endregion

        public MapControl()
        {
            InitializeComponent();
            Map = new Map();
            MouseInfoOverLayers = new List<ILayer>();
            MouseInfoDownLayers = new List<ILayer>();
            Loaded += MapControlLoaded;
            KeyDown += MapControlKeyDown;
            KeyUp += MapControlKeyUp;
            MouseLeftButtonDown += MapControlMouseLeftButtonDown;
            MouseLeftButtonUp += MapControlMouseLeftButtonUp;
            MouseMove += MapControlMouseMove;
            MouseLeave += MapControlMouseLeave;
            MouseWheel += MapControlMouseWheel;
            SizeChanged += MapControlSizeChanged;
            CompositionTarget.Rendering += CompositionTargetRendering;
            canvas.Children.Add(renderCanvas);
            renderer = new MapRenderer(renderCanvas);
#if !SILVERLIGHT
            Dispatcher.ShutdownStarted += DispatcherShutdownStarted;
            canvas.IsManipulationEnabled = true;
            canvas.ManipulationDelta += OnManipulationDelta;
            canvas.ManipulationCompleted += OnManipulationCompleted;
            canvas.ManipulationInertiaStarting += OnManipulationInertiaStarting;
#endif
        }

        #region Public methods

        public void OnViewChanged(bool changeEnd)
        {
            OnViewChanged(changeEnd, false);
        }

        private void OnViewChanged(bool changeEnd, bool userAction)
        {
            if (map != null)
            {
                //call down
                map.ViewChanged(changeEnd, view.Extent, view.Resolution);
                //call up
                if (ViewChanged != null)
                {
                    ViewChanged(this, new ViewChangedEventArgs { View = view, UserAction = userAction });
                }
            }
        }

        public void Refresh()
        {
            map.ViewChanged(true, view.Extent, view.Resolution);
            RefreshGraphics();
        }

        private void RefreshGraphics() //should be private soon
        {
#if !SILVERLIGHT
            InvalidateVisual();
#endif
            InvalidateArrange();
            invalid = true;
        }

        public void Clear()
        {
            if (map != null)
            {
                map.ClearCache();
            }
            RefreshGraphics();
        }

        public void ZoomIn()
        {
            if (double.IsNaN(toResolution))
                toResolution = view.Resolution;

            toResolution = ZoomHelper.ZoomIn(map.Resolutions, toResolution);
            ZoomMiddle();
        }

        public void ZoomOut()
        {
            if (double.IsNaN(toResolution))
                toResolution = view.Resolution;

            toResolution = ZoomHelper.ZoomOut(map.Resolutions, toResolution);
            ZoomMiddle();
        }

        #endregion

        #region Protected and private methods

        protected void OnErrorMessageChanged(EventArgs e)
        {
            if (ErrorMessageChanged != null)
            {
                ErrorMessageChanged(this, e);
            }
        }

        private static void OnResolutionChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
        {
            var newResolution = (double)e.NewValue;
            ((MapControl)dependencyObject).ZoomIn(newResolution);
        }

        private void ZoomIn(double resolution)
        {
            Point mousePosition = currentMousePosition;
            // When zooming we want the mouse position to stay above the same world coordinate.
            // We calcultate that in 3 steps.

            // 1) Temporarily center on the mouse position
            view.Center = view.ViewToWorld(mousePosition.X, mousePosition.Y);

            // 2) Then zoom 
            view.Resolution = resolution;

            // 3) Then move the temporary center of the map back to the mouse position
            view.Center = view.ViewToWorld(
              view.Width - mousePosition.X,
              view.Height - mousePosition.Y);

            OnViewChanged(true);
            RefreshGraphics();
        }

        private void ZoomMiddle()
        {
            currentMousePosition = new Point(ActualWidth / 2, ActualHeight / 2);
            StartZoomAnimation(view.Resolution, toResolution);
        }

        private void MapControlLoaded(object sender, RoutedEventArgs e)
        {
            if (!viewInitialized) InitializeView();
            UpdateSize();
            InitAnimation();

#if !SILVERLIGHT
            Focusable = true;
#else
            IsTabStop = true;
#endif
            Focus();
        }

        private void InitAnimation()
        {
            zoomAnimation.Duration = new Duration(new TimeSpan(0, 0, 0, 0, 1000));
            zoomAnimation.EasingFunction = new QuarticEase();
            Storyboard.SetTarget(zoomAnimation, this);
            Storyboard.SetTargetProperty(zoomAnimation, new PropertyPath("Resolution"));
            zoomStoryBoard.Children.Add(zoomAnimation);
        }

        private void MapControlMouseWheel(object sender, MouseWheelEventArgs e)
        {
            currentMousePosition = e.GetPosition(this); //Needed for both MouseMove and MouseWheel event for mousewheel event

            if (double.IsNaN(toResolution))
            {
                toResolution = view.Resolution;
            }

            if (e.Delta > 0)
            {
                toResolution = ZoomHelper.ZoomIn(map.Resolutions, toResolution);
            }
            else if (e.Delta < 0)
            {
                toResolution = ZoomHelper.ZoomOut(map.Resolutions, toResolution);
            }

            e.Handled = true; //so that the scroll event is not sent to the html page.

            //some cheating for personal gain
            view.CenterX += 0.000000001;
            view.CenterY += 0.000000001;
            OnViewChanged(false, true);

            StartZoomAnimation(view.Resolution, toResolution);
        }

        private void StartZoomAnimation(double begin, double end)
        {
            zoomStoryBoard.Pause(); //using Stop() here causes unexpected results while zooming very fast.
            zoomAnimation.From = begin;
            zoomAnimation.To = end;
            zoomAnimation.Completed += ZoomAnimationCompleted;
            zoomStoryBoard.Begin();
        }

        private void ZoomAnimationCompleted(object sender, EventArgs e)
        {
            toResolution = double.NaN;
        }

        private void MapControlSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!viewInitialized) InitializeView();
            UpdateSize();
            OnViewChanged(true);
            Refresh();
        }

        private void UpdateSize()
        {
            var rect = new RectangleGeometry();
            rect.Rect = new Rect(0f, 0f, ActualWidth, ActualHeight);

            if (View != null)
            {
                view.Width = ActualWidth;
                view.Height = ActualHeight;
            }
        }

        private void MapControlMouseLeave(object sender, MouseEventArgs e)
        {
            previousMousePosition = new Point();
            ReleaseMouseCapture();
        }

        public void MapDataChanged(object sender, DataChangedEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new DataChangedEventHandler(MapDataChanged), new[] { sender, e });
            }
            else
            {
                if (e.Cancelled)
                {
                    errorMessage = "Cancelled";
                    OnErrorMessageChanged(EventArgs.Empty);
                }
                else if (e.Error is System.Net.WebException)
                {
                    errorMessage = "WebException: " + e.Error.Message;
                    OnErrorMessageChanged(EventArgs.Empty);
                }
                else if (e.Error != null)
                {
                    errorMessage = e.Error.GetType() + ": " + e.Error.Message;
                    OnErrorMessageChanged(EventArgs.Empty);
                }
                else // no problems
                {
                    RefreshGraphics();
                }

            }
        }

        private void MapControlMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var eventArgs = GetMouseInfoEventArgs(e.GetPosition(this), MouseInfoDownLayers);
            OnMouseInfoDown(eventArgs ?? new MouseInfoEventArgs());
            previousMousePosition = e.GetPosition(this);
            downMousePosition = e.GetPosition(this);
            mouseDown = true;
            CaptureMouse();
            Focus();
        }

        private void MapControlMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (IsInBoxZoomMode || ZoomToBoxMode)
            {
                ZoomToBoxMode = false;
                SharpMap.Geometries.Point previous = View.ViewToWorld(previousMousePosition.X, previousMousePosition.Y);
                SharpMap.Geometries.Point current = View.ViewToWorld(e.GetPosition(this).X, e.GetPosition(this).Y);
                ZoomToBox(previous, current);
            }
            else
            {
                HandleFeatureInfo(e);
            }

            OnViewChanged(true, true);
            mouseDown = false;

            previousMousePosition = new Point();
            ReleaseMouseCapture();
        }

        private void HandleFeatureInfo(MouseButtonEventArgs e)
        {
            if (FeatureInfo == null) return; // don't fetch if you the call back is not set.

            if (downMousePosition == e.GetPosition(this))
            {
                foreach (var layer in Map.Layers)
                {
                    if (layer is IFeatureInfo)
                    {
                        (layer as IFeatureInfo).GetFeatureInfo(view.Extent, view.Resolution, OnFeatureInfo);
                    }
                }
            }
        }

        private void OnFeatureInfo(IDictionary<string, IEnumerable<IFeature>> features)
        {
            if (FeatureInfo != null)
            {
                FeatureInfo(this, new FeatureInfoEventArgs { FeatureInfo = features});
            }
        }

        private void MapControlMouseMove(object sender, MouseEventArgs e)
        {
            if (IsInBoxZoomMode || ZoomToBoxMode)
            {
                DrawBbox(e.GetPosition(this));
                return;
            }

            if (!mouseDown) RaiseMouseInfoEvents(e.GetPosition(this));

            if (mouseDown)
            {
                if (previousMousePosition == new Point())
                {
                    return; // It turns out that sometimes MouseMove+Pressed is called before MouseDown
                }

                currentMousePosition = e.GetPosition(this); //Needed for both MouseMove and MouseWheel event
                MapTransformHelper.Pan(view, currentMousePosition, previousMousePosition);
                previousMousePosition = currentMousePosition;
                OnViewChanged(false, true);
                RefreshGraphics();
            }
        }

        private void RaiseMouseInfoEvents(Point mousePosition)
        {
            if (!mouseDown)
            {
                var mouseEventArgs = GetMouseInfoEventArgs(mousePosition, MouseInfoOverLayers);
                if (mouseEventArgs == null) OnMouseInfoLeave();
                else OnMouseInfoOver(mouseEventArgs);
            }
        }

        private MouseInfoEventArgs GetMouseInfoEventArgs(Point mousePosition, IEnumerable<ILayer> layers)
        {
            var margin = 8 * View.Resolution;
            var point = View.ViewToWorld(new SharpMap.Geometries.Point(mousePosition.X, mousePosition.Y));

            foreach (var layer in layers)
            {
                var feature = layer.GetFeaturesInView(Map.GetExtents(), 0).FirstOrDefault(f =>
                    f.Geometry.GetBoundingBox().GetCentroid().Distance(point) < margin);
                if (feature != null)
                {
                    return new MouseInfoEventArgs { LayerName = layer.LayerName, Feature = feature };
                }
            }
            return null;
        }

        protected void OnMouseInfoLeave()
        {
            if (MouseInfoLeave != null)
            {
                MouseInfoLeave(this, new EventArgs());
            }
        }

        protected void OnMouseInfoOver(MouseInfoEventArgs e)
        {
            if (MouseInfoOver != null)
            {
                MouseInfoOver(this, e);
            }
        }

        protected void OnMouseInfoDown(MouseInfoEventArgs e)
        {
            if (MouseInfoDown != null)
            {
                MouseInfoDown(this, e);
            }
        }

        private void InitializeView()
        {
            if (ActualWidth.IsNanOrZero()) return;
            if (map == null) return;
            if (map.GetExtents() == null) return;
            if (map.GetExtents().Width.IsNanOrZero()) return;
            if (map.GetExtents().Height.IsNanOrZero()) return;
            if (map.GetExtents().GetCentroid() == null) return;

            if ((view.CenterX > 0) && (view.CenterY > 0) && (view.Resolution > 0))
            {
                viewInitialized = true; //view was already initialized
                return;
            }

            view.Center = map.GetExtents().GetCentroid();
            view.Resolution = map.GetExtents().Width / ActualWidth;
            viewInitialized = true;
        }

        private void CompositionTargetRendering(object sender, EventArgs e)
        {
            if (!viewInitialized) InitializeView();
            if (!viewInitialized) return; //stop if the line above failed. 
            if (!invalid) return;

            if ((renderer != null) && (map != null))
            {
                renderer.Render(view, map.Layers);
                fpsCounter.FramePlusOne();
                invalid = false;
            }
        }

#if !SILVERLIGHT
        private void DispatcherShutdownStarted(object sender, EventArgs e)
        {
            CompositionTarget.Rendering -= CompositionTargetRendering;
            if (map != null)
            {
                map.Dispose();
            }
        }
#endif

        #endregion

        #region Bbox zoom

        public void ZoomToBox(SharpMap.Geometries.Point beginPoint, SharpMap.Geometries.Point endPoint)
        {
            double x, y, resolution;
            var width = Math.Abs(endPoint.X - beginPoint.X);
            var height = Math.Abs(endPoint.Y - beginPoint.Y);
            if (width <= 0) return;
            if (height <= 0) return;

            ZoomHelper.ZoomToBoudingbox(beginPoint.X, beginPoint.Y, endPoint.X, endPoint.Y, ActualWidth, out x, out y, out resolution);
            resolution = ZoomHelper.ClipToExtremes(map.Resolutions, resolution);

            view.Center = new SharpMap.Geometries.Point(x, y);
            view.Resolution = resolution;
            toResolution = resolution;

            OnViewChanged(true, true);
            RefreshGraphics();
            ClearBBoxDrawing();
        }

        private void ClearBBoxDrawing()
        {
            bboxRect.Margin = new Thickness(0, 0, 0, 0);
            bboxRect.Width = 0;
            bboxRect.Height = 0;
        }

        private void MapControlKeyUp(object sender, KeyEventArgs e)
        {
            String keyName = e.Key.ToString().ToLower();
            if (keyName.Equals("ctrl") || keyName.Equals("leftctrl") || keyName.Equals("rightctrl"))
            {
                IsInBoxZoomMode = false;
            }
        }

        private void MapControlKeyDown(object sender, KeyEventArgs e)
        {
            String keyName = e.Key.ToString().ToLower();
            if (keyName.Equals("ctrl") || keyName.Equals("leftctrl") || keyName.Equals("rightctrl"))
            {
                IsInBoxZoomMode = true;
            }
        }

        private void DrawBbox(Point newPos)
        {
            if (mouseDown)
            {
                Point from = previousMousePosition;
                Point to = newPos;

                if (from.X > to.X)
                {
                    Point temp = from;
                    from.X = to.X;
                    to.X = temp.X;
                }

                if (from.Y > to.Y)
                {
                    Point temp = from;
                    from.Y = to.Y;
                    to.Y = temp.Y;
                }

                bboxRect.Width = to.X - from.X;
                bboxRect.Height = to.Y - from.Y;
                bboxRect.Margin = new Thickness(from.X, from.Y, 0, 0);
            }
        }

        #endregion

        public void ZoomToExtent()
        {
            view.Resolution = Width / Map.GetExtents().Width;
            view.Center = Map.GetExtents().GetCentroid();
        }

        #region WPF4 Touch Support

#if !SILVERLIGHT

        private static void OnManipulationInertiaStarting(object sender, ManipulationInertiaStartingEventArgs e)
        {
            e.TranslationBehavior.DesiredDeceleration = 25 * 96.0 / (1000.0 * 1000.0);
        }

        private int manipulationDeltaCount;

        private void OnManipulationDelta(object sender, ManipulationDeltaEventArgs e)
        {
            var currentManipulationPosition = new Point(e.ManipulationOrigin.X + e.DeltaManipulation.Translation.X, e.ManipulationOrigin.Y + e.DeltaManipulation.Translation.Y);
            var previousManipulationPosition = new Point(e.ManipulationOrigin.X, e.ManipulationOrigin.Y);

            if (e.DeltaManipulation.Scale.X != 1.0) //No scale
            {
                view.Center = view.ViewToWorld(currentManipulationPosition.X, currentManipulationPosition.Y);
                view.Resolution = view.Resolution / e.DeltaManipulation.Scale.X;
                view.Center = view.ViewToWorld(view.Width - currentManipulationPosition.X, view.Height - currentManipulationPosition.Y);
            }

            MapTransformHelper.Pan(view, currentManipulationPosition, previousManipulationPosition);

            manipulationDeltaCount++;

            //Currently the manipulation data generates too much updates for the vectorRenderer to be smooth
            //This is a temporarily fix until the rendering method is improved
            if (manipulationDeltaCount % 2 != 0)
                return;

            OnViewChanged(false, true);
            RefreshGraphics();
        }

        private void OnManipulationCompleted(object sender, ManipulationCompletedEventArgs e)
        {
            manipulationDeltaCount = 0;
            Refresh();
        }

#endif

        #endregion
    }

    public class ViewChangedEventArgs : EventArgs
    {
        public View View { get; set; }
        public bool UserAction { get; set; }
    }

    public class MouseInfoEventArgs : EventArgs
    {
        public MouseInfoEventArgs()
        {
            LayerName = string.Empty;
        }

        public string LayerName { get; set; }
        public IFeature Feature { get; set; }
    }

    public class FeatureInfoEventArgs : EventArgs
    {
        public IDictionary<string, IEnumerable<IFeature>> FeatureInfo { get; set; }
    }
}