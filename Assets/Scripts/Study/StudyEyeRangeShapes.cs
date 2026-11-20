using System;
using System.Collections.Generic;
using System.Globalization;

namespace Pano2StereoVR.Study
{
    /// <summary>
    /// Pure C# helpers for the natural-viewing-range result page and the Part 2 advance cue (user 11-20): coverage
    /// masks (0-255 alpha, row 0 at the bottom as in Texture2D) for the diagram rings and the fast-forward icon,
    /// the ring values and the text shown for a value. No engine objects are created here.
    /// </summary>
    public static class EyeRangeShapes
    {
        /// <summary>Reference rings in degrees: every step up to the maximum, and the maximum itself.</summary>
        public static List<float> RingDegrees(float maxDeg, float stepDeg = 10f)
        {
            var rings = new List<float>();
            if (maxDeg <= 0f || stepDeg <= 0f)
            {
                return rings;
            }
            for (float r = stepDeg; r < maxDeg - 1e-3f; r += stepDeg)
            {
                rings.Add(r);
            }
            rings.Add(maxDeg);
            return rings;
        }

        /// <summary>
        /// Value as shown on the result page: whole degrees ("23°"), "≥40°" at or beyond the maximum eccentricity
        /// (a censored direction), "—" without data.
        /// </summary>
        public static string ShownDegrees(double valueDeg, float maxDeg)
        {
            if (double.IsNaN(valueDeg))
            {
                return "—";
            }
            if (valueDeg >= maxDeg - 1e-3)
            {
                return "≥" + maxDeg.ToString("0.#", CultureInfo.InvariantCulture) + "°";
            }
            return Math.Round(valueDeg, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "°";
        }

        /// <summary>
        /// Concentric rings (radii as fractions of the outer radius, which is the texture half size minus the line
        /// width) and <paramref name="spokes"/> faint spokes from the centre (0 = up), anti-aliased.
        /// </summary>
        public static byte[] RangeRings(int size, IList<float> ringFractions, int spokes, float lineWidthPx)
        {
            var a = new byte[size * size];
            double c = 0.5 * size;
            double outer = c - lineWidthPx - 1.0;
            double half = 0.5 * lineWidthPx;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    double px = x + 0.5 - c;
                    double py = y + 0.5 - c;
                    double d = Math.Sqrt(px * px + py * py);
                    double cov = 0.0;
                    foreach (float f in ringFractions)
                    {
                        cov = Math.Max(cov, Clamp01(half + 0.5 - Math.Abs(d - f * outer)));
                    }
                    for (int k = 0; k < spokes && d <= outer; k++)
                    {
                        double ang = 2.0 * Math.PI * k / spokes;
                        double ux = Math.Sin(ang), uy = Math.Cos(ang);
                        double along = px * ux + py * uy;
                        if (along < 0.0)
                        {
                            continue;
                        }
                        double perp = Math.Abs(px * uy - py * ux);
                        cov = Math.Max(cov, 0.45 * Clamp01(0.5 * half + 0.5 - perp));
                    }
                    a[y * size + x] = (byte)Math.Round(255.0 * cov);
                }
            }
            return a;
        }

        /// <summary>Fast-forward icon: two right-pointing triangles side by side (4 x 4 supersampled).</summary>
        public static byte[] FastForward(int width, int height)
        {
            var a = new byte[width * height];
            double tw = 0.5 * width;
            double hh = 0.5 * height;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int inside = 0;
                    for (int sy = 0; sy < 4; sy++)
                    {
                        for (int sx = 0; sx < 4; sx++)
                        {
                            double px = x + (sx + 0.5) / 4.0;
                            double py = y + (sy + 0.5) / 4.0;
                            double lx = px < tw ? px : px - tw; // position within its triangle
                            if (Math.Abs(py - hh) <= hh * (1.0 - lx / tw))
                            {
                                inside++;
                            }
                        }
                    }
                    a[y * width + x] = (byte)Math.Round(255.0 * inside / 16.0);
                }
            }
            return a;
        }

        private static double Clamp01(double v)
        {
            return v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v);
        }
    }
}
