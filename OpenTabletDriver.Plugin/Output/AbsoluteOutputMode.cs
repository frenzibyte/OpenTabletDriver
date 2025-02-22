﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Platform.Display;
using OpenTabletDriver.Plugin.Platform.Pointer;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Plugin.Output
{
    /// <summary>
    /// An absolutely positioned output mode.
    /// </summary>
    [PluginIgnore]
    public abstract class AbsoluteOutputMode : IOutputMode
    {
        private IList<IFilter> filters, preFilters, postFilters;
        private Vector2 min, max;
        private Matrix3x2 transformationMatrix;

        public IList<IFilter> Filters
        {
            set
            {
                this.filters = value ?? Array.Empty<IFilter>();
                if (Info.Driver.InterpolatorActive)
                    this.preFilters = Filters.Where(t => t.FilterStage == FilterStage.PreTranspose).ToList();
                else
                    this.preFilters = Filters.Where(t => t.FilterStage == FilterStage.PreTranspose || t.FilterStage == FilterStage.PreInterpolate).ToList();
                this.postFilters = filters.Where(t => t.FilterStage == FilterStage.PostTranspose).ToList();
            }
            get => this.filters;
        }

        private TabletState tablet;
        public TabletState Tablet
        {
            set
            {
                this.tablet = value;
                UpdateTransformMatrix();
            }
            get => this.tablet;
        }

        private Area outputArea, inputArea;

        /// <summary>
        /// The area in which the tablet's input is transformed to.
        /// </summary>
        public Area Input
        {
            set
            {
                this.inputArea = value;
                UpdateTransformMatrix();
            }
            get => this.inputArea;
        }

        /// <summary>
        /// The area in which the final processed output is transformed to.
        /// </summary>
        public Area Output
        {
            set
            {
                this.outputArea = value;
                UpdateTransformMatrix();
            }
            get => this.outputArea;
        }

        /// <summary>
        /// The class in which the final absolute positioned output is handled.
        /// </summary>
        public abstract IAbsolutePointer Pointer { get; }

        /// <summary>
        /// Whether to clip all tablet inputs to the assigned areas.
        /// </summary>
        /// <remarks>
        /// If false, input outside of the area can escape the assigned areas, but still will be transformed.
        /// If true, input outside of the area will be clipped to the edges of the assigned areas.
        /// </remarks>
        public bool AreaClipping { set; get; }

        /// <summary>
        /// Whether to stop accepting input outside of the assigned areas.
        /// </summary>
        /// <remarks>
        /// If true, <see cref="AreaClipping"/> is automatically implied true.
        /// </remarks>
        public bool AreaLimiting { set; get; }

        protected void UpdateTransformMatrix()
        {
            if (Input != null && Output != null && Tablet?.Digitizer != null)
                this.transformationMatrix = CalculateTransformation(Input, Output, Tablet.Digitizer);

            var halfDisplayWidth = Output?.Width / 2 ?? 0;
            var halfDisplayHeight = Output?.Height / 2 ?? 0;

            var minX = Output?.Position.X - halfDisplayWidth ?? 0;
            var maxX = Output?.Position.X + Output?.Width - halfDisplayWidth ?? 0;
            var minY = Output?.Position.Y - halfDisplayHeight ?? 0;
            var maxY = Output?.Position.Y + Output?.Height - halfDisplayHeight ?? 0;

            this.min = new Vector2(minX, minY);
            this.max = new Vector2(maxX, maxY);
        }

        protected static Matrix3x2 CalculateTransformation(Area input, Area output, DigitizerIdentifier tablet)
        {
            // Convert raw tablet data to millimeters
            var res = Matrix3x2.CreateScale(
                tablet.Width / tablet.MaxX,
                tablet.Height / tablet.MaxY);

            // Translate to the center of input area
            res *= Matrix3x2.CreateTranslation(
                -input.Position.X, -input.Position.Y);

            // Apply rotation
            res *= Matrix3x2.CreateRotation(
                (float)(-input.Rotation * System.Math.PI / 180));

            // Scale millimeters to pixels
            res *= Matrix3x2.CreateScale(
                output.Width / input.Width, output.Height / input.Height);

            // Translate output to virtual screen coordinates
            res *= Matrix3x2.CreateTranslation(
                output.Position.X, output.Position.Y);

            return res;
        }

        public virtual void Read(IDeviceReport report)
        {
            if (report is ITabletReport tabletReport)
            {
                if (Tablet.Digitizer.ActiveReportID.IsInRange(tabletReport.ReportID))
                {
                    if (Pointer is IVirtualTablet pressureHandler)
                        pressureHandler.SetPressure((float)tabletReport.Pressure / (float)Tablet.Digitizer.MaxPressure);
                        
                    if (Transpose(tabletReport) is Vector2 pos)
                        Pointer.SetPosition(pos);
                }
            }
        }

        /// <summary>
        /// Transposes, transforms, and performs all absolute positioning calculations to a <see cref="ITabletReport"/>.
        /// </summary>
        /// <param name="report">The <see cref="ITabletReport"/> in which to transform.</param>
        /// <returns>The transformed <see cref="Vector2"/> from the <see cref="ITabletReport"/>.</returns>
        public Vector2? Transpose(ITabletReport report)
        {
            var pos = new Vector2(report.Position.X, report.Position.Y);

            // Pre Filter
            foreach (IFilter filter in this.preFilters ??= Array.Empty<IFilter>())
                pos = filter.Filter(pos);

            // Apply transformation
            pos = Vector2.Transform(pos, this.transformationMatrix);

            // Clipping to display bounds
            var clippedPoint = Vector2.Clamp(pos, this.min, this.max);
            if (AreaLimiting && clippedPoint != pos)
                return null;

            if (AreaClipping)
                pos = clippedPoint;

            // Post Filter
            foreach (IFilter filter in this.postFilters ??= Array.Empty<IFilter>())
                pos = filter.Filter(pos);

            return pos;
        }
    }
}
