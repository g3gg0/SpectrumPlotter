using ScottPlot;
using ScottPlot.Drawing;
using ScottPlot.Plottable;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Linq;
using System.Numerics;

namespace SpectrumPlotter
{
    class RainbowSignalPlotXY : SignalPlotXY
    {
        public int Steps { get; set; } = 512;

        private class PointFx : IEquatable<PointFx>
        {
            private float x;
            private float y;
            private float o;

            public PointFx(float x, float y, float orig)
            {
                this.x = x;
                this.y = y;
                this.o = orig;
            }

            public float X
            {
                get => x;
                set => x = value;
            }

            public float Y
            {
                get => y;
                set => y = value;
            }

            public float O
            {
                get => o;
                set => o = value;
            }


            /// <summary>
            /// Translates a <see cref='System.Drawing.PointFx'/> by a given <see cref='System.Drawing.Size'/> .
            /// </summary>
            public static PointFx operator +(PointFx pt, Size sz) => Add(pt, sz);

            /// <summary>
            /// Translates a <see cref='System.Drawing.PointFx'/> by the negative of a given <see cref='System.Drawing.Size'/> .
            /// </summary>
            public static PointFx operator -(PointFx pt, Size sz) => Subtract(pt, sz);

            /// <summary>
            /// Translates a <see cref='System.Drawing.PointFx'/> by a given <see cref='System.Drawing.SizeF'/> .
            /// </summary>
            public static PointFx operator +(PointFx pt, SizeF sz) => Add(pt, sz);

            /// <summary>
            /// Translates a <see cref='System.Drawing.PointFx'/> by the negative of a given <see cref='System.Drawing.SizeF'/> .
            /// </summary>
            public static PointFx operator -(PointFx pt, SizeF sz) => Subtract(pt, sz);

            /// <summary>
            /// Compares two <see cref='System.Drawing.PointFx'/> objects. The result specifies whether the values of the
            /// <see cref='System.Drawing.PointFx.X'/> and <see cref='System.Drawing.PointFx.Y'/> properties of the two
            /// <see cref='System.Drawing.PointFx'/> objects are equal.
            /// </summary>
            public static bool operator ==(PointFx left, PointFx right) => left.X == right.X && left.Y == right.Y;

            /// <summary>
            /// Compares two <see cref='System.Drawing.PointFx'/> objects. The result specifies whether the values of the
            /// <see cref='System.Drawing.PointFx.X'/> or <see cref='System.Drawing.PointFx.Y'/> properties of the two
            /// <see cref='System.Drawing.PointFx'/> objects are unequal.
            /// </summary>
            public static bool operator !=(PointFx left, PointFx right) => !(left == right);

            /// <summary>
            /// Translates a <see cref='System.Drawing.PointFx'/> by a given <see cref='System.Drawing.Size'/> .
            /// </summary>
            public static PointFx Add(PointFx pt, Size sz) => new PointFx(pt.X + sz.Width, pt.Y + sz.Height, pt.o);

            public static PointFx Subtract(PointFx pt, Size sz) => new PointFx(pt.X - sz.Width, pt.Y - sz.Height, pt.o);

            public static PointFx Add(PointFx pt, SizeF sz) => new PointFx(pt.X + sz.Width, pt.Y + sz.Height, pt.o);

            public static PointFx Subtract(PointFx pt, SizeF sz) => new PointFx(pt.X - sz.Width, pt.Y - sz.Height, pt.o);

            public override  bool Equals([NotNullWhen(true)] object? obj) => obj is PointFx && Equals((PointFx)obj);

            public bool Equals(PointFx other) => this == other;

            public override int GetHashCode() => HashCode.Combine(X.GetHashCode(), Y.GetHashCode());

            public override string ToString() => $"{{X={x}, Y={y}}}";
        }


        private new IEnumerable<PointFx> ProcessInterval(int x, int from, int length, PlotDimensions dims)
        {
            NumericConversion.DoubleToGeneric(dims.XMin + dims.XSpan / dims.DataWidth * x - OffsetX, out double start);
            NumericConversion.DoubleToGeneric(dims.XMin + dims.XSpan / dims.DataWidth * (x + 1) - OffsetX, out double end);

            int startIndex = Array.BinarySearch(Xs, from, length, start);
            if (startIndex < 0)
            {
                startIndex = ~startIndex;
            }

            int endIndex = Array.BinarySearch(Xs, from, length, end);
            if (endIndex < 0)
            {
                endIndex = ~endIndex;
            }

            if (startIndex == endIndex)
            {
                yield break;
            }

            Strategy.MinMaxRangeQuery(startIndex, endIndex - 1, out double min, out double max);

            var pointsCount = endIndex - startIndex;

            yield return new PointFx(x + dims.DataOffsetX, dims.GetPixelY((Strategy.SourceElement(startIndex) * ScaleYAsDouble) + OffsetYAsDouble), (float) Xs[startIndex]);
            if (pointsCount > 1)
            {
                yield return new PointFx(x + dims.DataOffsetX, dims.GetPixelY(min * ScaleYAsDouble + OffsetYAsDouble), (float)Xs[startIndex]);
                yield return new PointFx(x + dims.DataOffsetX, dims.GetPixelY(max * ScaleYAsDouble + OffsetYAsDouble), (float)Xs[startIndex]);
                yield return new PointFx(x + dims.DataOffsetX, dims.GetPixelY((Strategy.SourceElement(endIndex - 1) * ScaleYAsDouble) + OffsetYAsDouble), (float)Xs[endIndex - 1]);
            }
        }

        public override void Render(PlotDimensions dims, Bitmap bmp, bool lowQuality = false)
        {
            PointFx[] PointBefore;
            PointFx[] PointAfter;
            int searchFrom;
            int searchTo;

            // Calculate point before displayed points
            NumericConversion.DoubleToGeneric(dims.XMin - OffsetX, out double x);
            int pointBeforeIndex = Array.BinarySearch(Xs, MinRenderIndex, MaxRenderIndex - MinRenderIndex + 1, x);
            if (pointBeforeIndex < 0)
            {
                pointBeforeIndex = ~pointBeforeIndex;
            }

            if (pointBeforeIndex > MinRenderIndex)
            {
                PointBefore = new PointFx[]
                {
                        new PointFx(dims.GetPixelX(NumericConversion.GenericToDouble(Xs, pointBeforeIndex - 1) + OffsetX),
                                   dims.GetPixelY((Strategy.SourceElement(pointBeforeIndex - 1) * ScaleYAsDouble) + OffsetYAsDouble), (float)NumericConversion.GenericToDouble(Xs, pointBeforeIndex - 1))
                };
                searchFrom = pointBeforeIndex;
            }
            else
            {
                PointBefore = new PointFx[] { };
                searchFrom = MinRenderIndex;
            }

            // Calculate point after displayed points
            NumericConversion.DoubleToGeneric(dims.XMax - OffsetX, out x);
            int pointAfterIndex = Array.BinarySearch(Xs, MinRenderIndex, MaxRenderIndex - MinRenderIndex + 1, x);
            if (pointAfterIndex < 0)
            {
                pointAfterIndex = ~pointAfterIndex;
            }

            if (pointAfterIndex <= MaxRenderIndex)
            {
                PointAfter = new PointFx[]
                {
                        new PointFx(dims.GetPixelX(NumericConversion.GenericToDouble(Xs, pointAfterIndex) + OffsetX),
                                   dims.GetPixelY((Strategy.SourceElement(pointAfterIndex) * ScaleYAsDouble)+ OffsetYAsDouble), (float)NumericConversion.GenericToDouble(Xs, pointAfterIndex))
                };
                searchTo = pointAfterIndex;
            }
            else
            {
                PointAfter = new PointFx[] { };
                searchTo = MaxRenderIndex;
            }

            IEnumerable<PointFx> VisiblePoints;
            if (UseParallel)
            {
                VisiblePoints = Enumerable.Range(0, (int)Math.Round(dims.DataWidth))
                                          .AsParallel()
                                          .AsOrdered()
                                          .Select(x => ProcessInterval(x, searchFrom, searchTo - searchFrom + 1, dims))
                                          .SelectMany(x => x);

            }
            else
            {
                VisiblePoints = Enumerable.Range(0, (int)Math.Round(dims.DataWidth))
                                          .Select(x => ProcessInterval(x, searchFrom, searchTo - searchFrom + 1, dims))
                                          .SelectMany(x => x);
            }

            PointFx[] PointsToDraw = PointBefore.Concat(VisiblePoints).Concat(PointAfter).ToArray();

            // Interpolate before displayed point to make it x = -1 (close to visible area)
            // this fix extreme zoom in bug
            if (PointBefore.Length > 0 && PointsToDraw.Length >= 2 && !StepDisplay)
            {
                // only extrapolate if points are different (otherwise extrapolated point may be infinity)
                if (PointsToDraw[0].X != PointsToDraw[1].X)
                {
                    float x0 = -1 + dims.DataOffsetX;
                    float y0 = PointsToDraw[1].Y + (PointsToDraw[0].Y - PointsToDraw[1].Y) * (x0 - PointsToDraw[1].X) / (PointsToDraw[0].X - PointsToDraw[1].X);
                    PointsToDraw[0] = new PointFx(x0, y0, 0);
                }
            }

            // Interpolate after displayed point to make it x = datasize.Width(close to visible area)
            // this fix extreme zoom in bug
            if (PointAfter.Length > 0 && PointsToDraw.Length >= 2 && !StepDisplay)
            {
                PointFx lastPoint = PointsToDraw[PointsToDraw.Length - 2];
                PointFx afterPoint = PointsToDraw[PointsToDraw.Length - 1];

                // only extrapolate if points are different (otherwise extrapolated point may be infinity)
                if (afterPoint.X != lastPoint.X)
                {
                    float x1 = dims.DataWidth + dims.DataOffsetX;
                    float y1 = lastPoint.Y + (afterPoint.Y - lastPoint.Y) * (x1 - lastPoint.X) / (afterPoint.X - lastPoint.X);
                    PointsToDraw[PointsToDraw.Length - 1] = new PointFx(x1, y1, 0);
                }
            }



            using Graphics gfx = Graphics.FromImage(bmp);
            using Pen pen = new(Color.Black, 1);

            float baseline = dims.DataHeight + dims.DataOffsetY;
            float zero = Math.Max(dims.DataOffsetY, Math.Min(baseline, dims.GetPixelY(0)));

            for (int index = 0; index < PointsToDraw.Length - 1; index++)
            {
                float dotX = PointsToDraw[index].X;
                float dotXNext = PointsToDraw[index + 1].X;
                float dotY = Math.Max(dims.DataOffsetY, Math.Min(baseline, PointsToDraw[index].Y));
                float dotYNext = Math.Max(dims.DataOffsetY, Math.Min(baseline, PointsToDraw[index + 1].Y));
                Color color = WavelengthToColor(PointsToDraw[index].O);

                using Brush colorBrush = new SolidBrush(color);

                PointF[] poly = [new(dotX, dotY), new(dotX, zero), new(dotXNext, zero), new(dotXNext, dotYNext)];
                gfx.FillPolygon(colorBrush, poly);
            }
            base.Render(dims, bmp, lowQuality);
        }

        struct RGB
        {
            public double R;
            public double G;
            public double B;

            public RGB(double r, double g, double b)
            {
                R = r;
                G = g;
                B = b;
            }

            public static RGB Fade(RGB start, RGB end, double ratio)
            {
                return new RGB(
                    start.R + ratio * (end.R - start.R),
                    start.G + ratio * (end.G - start.G),
                    start.B + ratio * (end.B - start.B)
                );
            }

            public RGB ApplyGamma(double gamma)
            {
                return new RGB(
                    Math.Pow(R, gamma),
                    Math.Pow(G, gamma),
                    Math.Pow(B, gamma)
                );
            }
            public Color ToColor()
            {
                return Color.FromArgb(
                    (int)Math.Clamp(R * 255, 0, 255),
                    (int)Math.Clamp(G * 255, 0, 255),
                    (int)Math.Clamp(B * 255, 0, 255)
                );
            }
        }

        struct WavelengthRange
        {
            public double Start;
            public RGB Color;
        }

        private static Color WavelengthToColor(double wavelength)
        {
            WavelengthRange[] ranges =
            [
                new WavelengthRange { Start = 0,    Color = new RGB(0.05, 0.0, 0.05) },
                new WavelengthRange { Start = 380,  Color = new RGB(0.6, 0.0, 1.0) },
                new WavelengthRange { Start = 440,  Color = new RGB(0.0, 0.0, 1.0) },
                new WavelengthRange { Start = 490,  Color = new RGB(0.0, 1.0, 1.0) },
                new WavelengthRange { Start = 510,  Color = new RGB(0.0, 1.0, 0.0) },
                new WavelengthRange { Start = 580,  Color = new RGB(1.0, 1.0, 0.0) },
                new WavelengthRange { Start = 645,  Color = new RGB(1.0, 0.0, 0.0) },
                new WavelengthRange { Start = 780,  Color = new RGB(0.05, 0.0, 0.0) }
            ];

            RGB color = new(0, 0, 0);

            if (wavelength < ranges[1].Start)
            {
                color = ranges[0].Color;
            }
            else if (wavelength >= ranges[^1].Start)
            {
                color = ranges[^1].Color;
            }
            else
            {
                for (int i = 0; i < ranges.Length - 1; i++)
                {
                    if (wavelength >= ranges[i].Start && wavelength < ranges[i + 1].Start)
                    {
                        double ratio = (wavelength - ranges[i].Start) / (ranges[i + 1].Start - ranges[i].Start);
                        color = RGB.Fade(ranges[i].Color, ranges[i + 1].Color, ratio);
                        break;
                    }
                }
            }

            color = color.ApplyGamma(0.8);

            return color.ToColor();
        }

        public LegendItem[] GetLegendItems() => new LegendItem[] { };

        public void ValidateData(bool deep = false) { }
    }
}
