using ScottPlot;
using ScottPlot.Drawing;
using ScottPlot.Plottable;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace SpectrumPlotter
{
    class RainbowPlot : IPlottable
    {
        public double Min { get; set; }
        public double Max { get; set; }
        public int Steps { get; set; } = 512;

        public bool IsVisible { get; set; } = true;
        public int XAxisIndex { get; set; }
        public int YAxisIndex { get; set; }
        public int BarHeight { get; set; } = 20;

        /* Konstruktor */
        public RainbowPlot(double minWavelength, double maxWavelength)
        {
            Min = minWavelength;
            Max = maxWavelength;
        }

        public AxisLimits GetAxisLimits()
        {
            return new(Min, Max, 0, 0);
        }

        public void Render(PlotDimensions dims, Bitmap bmp, bool lowQuality = false)
        {
            using Graphics gfx = Graphics.FromImage(bmp);
            using Pen pen = new(Color.Black, 1);


            /* Grenzen des Zeichenbereichs */
            int xLeftBound = (int)dims.DataOffsetX;
            int xRightBound = xLeftBound + (int)dims.DataWidth;
            int yBottomBound = (int)dims.DataOffsetY;
            int yTopBound = yBottomBound - (int)dims.DataHeight;

            /* Berechnung der Pixelbreite */
            double xMinPixel = dims.GetPixelX(Min);
            double xMaxPixel = dims.GetPixelX(Max);
            int barWidth = Math.Max(1, (int)((xMaxPixel - xMinPixel) / Steps));

            for (int index = 0; index < Steps; index++)
            {
                double wavelength = Min + (Max - Min) / Steps * index;
                Color color = WavelengthToColor(wavelength);

                using Brush colorBrush = new SolidBrush(color);
                int xPixel = (int)dims.GetPixelX(wavelength);
                int yPixel = (int)(dims.DataOffsetY + dims.DataHeight - BarHeight + 1);

                /* Nur zeichnen, wenn innerhalb der Achsen */
                if (xPixel + barWidth > xLeftBound && xPixel <= xRightBound)
                {
                    gfx.FillRectangle(colorBrush,
                        Math.Max(xLeftBound + 1, xPixel),
                        Math.Max(yTopBound, yPixel),
                        Math.Min(barWidth, xRightBound - xPixel) +1,
                        BarHeight);
                }
            }
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
