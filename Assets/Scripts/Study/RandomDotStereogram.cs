using System;

namespace Pano2StereoVR.Study
{
    /// <summary>
    /// Random-dot stereogram for the in-headset stereo screening. The pattern is shown on a quad of
    /// angular size <c>quadSizeDeg</c> textured with <c>texturePx</c> texels per eye. Since plan format 3 the
    /// texture size is chosen so that one texel covers about one rendered eye pixel at the view centre, and
    /// disparities are given in whole pixels (see <see cref="TexturePxForOnePixelTexels"/>). A crossed (near) disparity of d texels shifts the shape
    /// to the right in the left eye and to the left in the right eye, half each.
    /// The output is one top-bottom luminance image: rows [size, 2*size) hold the left eye (top half in
    /// texture coordinates, row 0 is the bottom), rows [0, size) the right eye.
    /// </summary>
    public static class RandomDotStereogram
    {
        public const byte Dark = 20;
        public const byte Light = 235;

        public static double TexelArcsec(double quadSizeDeg, int texturePx)
        {
            if (texturePx <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(texturePx));
            }
            return quadSizeDeg * 3600.0 / texturePx;
        }

        public static int DisparityTexels(double disparityArcsec, double quadSizeDeg, int texturePx)
        {
            return Math.Max(0, (int)Math.Round(disparityArcsec / TexelArcsec(quadSizeDeg, texturePx), MidpointRounding.AwayFromZero));
        }

        /// <summary>
        /// Angular width (arcsec) of one rendered pixel at the centre of a planar eye projection whose frustum
        /// spans tanLeft + tanRight (tangents of the left and right half-angles) over widthPx pixels.
        /// </summary>
        public static double PixelArcsecAtCentre(double tanLeft, double tanRight, int widthPx)
        {
            if (widthPx <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(widthPx));
            }
            return Math.Atan((tanLeft + tanRight) / widthPx) * 180.0 / Math.PI * 3600.0;
        }

        /// <summary>Same as <see cref="PixelArcsecAtCentre"/> for a symmetric frustum of horizontal field hfovDeg.</summary>
        public static double PixelArcsecFromFov(double hfovDeg, int widthPx)
        {
            double t = Math.Tan(0.5 * hfovDeg * Math.PI / 180.0);
            return PixelArcsecAtCentre(t, t, widthPx);
        }

        /// <summary>
        /// Texture width (texels) that makes one texel cover one rendered pixel for a quad of angular width
        /// quadSizeDeg centred on the view axis: 2 tan(quadSizeDeg / 2) / pixelTan, with pixelTan = tan(pixel angle).
        /// </summary>
        public static int TexturePxForOnePixelTexels(double quadSizeDeg, double pixelArcsec, int minPx = 64, int maxPx = 4096)
        {
            double pixelTan = Math.Tan(pixelArcsec / 3600.0 * Math.PI / 180.0);
            if (pixelTan <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(pixelArcsec));
            }
            double px = 2.0 * Math.Tan(0.5 * quadSizeDeg * Math.PI / 180.0) / pixelTan;
            return Math.Max(minPx, Math.Min(maxPx, (int)Math.Round(px, MidpointRounding.AwayFromZero)));
        }

        /// <summary>Displayed disparity in arcseconds after rounding to whole texels.</summary>
        public static double ActualDisparityArcsec(double disparityArcsec, double quadSizeDeg, int texturePx)
        {
            return DisparityTexels(disparityArcsec, quadSizeDeg, texturePx) * TexelArcsec(quadSizeDeg, texturePx);
        }

        /// <summary>Horizontal shift of the shape in each eye (texels, +x = right). leftShift - rightShift = +d for crossed.</summary>
        public static void EyeShifts(int disparityTexels, bool crossed, out int leftShift, out int rightShift)
        {
            int a = (disparityTexels + 1) / 2;
            int b = disparityTexels / 2;
            if (crossed)
            {
                leftShift = a;
                rightShift = -b;
            }
            else
            {
                leftShift = -a;
                rightShift = b;
            }
        }

        /// <summary>Shape membership; x, y relative to the shape centre in texels (y up), r = shape radius.</summary>
        public static bool InsideShape(string shape, double x, double y, double r)
        {
            switch (shape)
            {
                case "circle":
                    return x * x + y * y <= r * r;
                case "square":
                    return Math.Abs(x) <= 0.85 * r && Math.Abs(y) <= 0.85 * r;
                case "triangle":
                    return y >= -0.5 * r && y <= r - Math.Sqrt(3.0) * Math.Abs(x);
                case "cross":
                    return (Math.Abs(x) <= 0.3 * r && Math.Abs(y) <= r) || (Math.Abs(y) <= 0.3 * r && Math.Abs(x) <= r);
                default:
                    throw new ArgumentException("unknown shape '" + shape + "'");
            }
        }

        /// <summary>
        /// Generate the top-bottom stereogram (width = size, height = 2 * size), row-major from the bottom row.
        /// Background dots are identical in both eyes; the shape region carries its own dot field shifted per eye,
        /// so the shape is invisible to either eye alone.
        /// </summary>
        public static byte[] GenerateTopBottom(int size, string shape, int disparityTexels, bool crossed, int seed,
            int dotSize = 2, double shapeRadiusFraction = 0.22)
        {
            if (size <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }
            if (dotSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(dotSize));
            }
            int cells = (size + dotSize - 1) / dotSize;
            var rng = new Random(seed);
            var background = new bool[cells * cells];
            var foreground = new bool[cells * cells];
            for (int i = 0; i < background.Length; i++)
            {
                background[i] = rng.NextDouble() < 0.5;
                foreground[i] = rng.NextDouble() < 0.5;
            }

            EyeShifts(disparityTexels, crossed, out int leftShift, out int rightShift);
            double r = size * shapeRadiusFraction;
            double c = (size - 1) / 2.0;
            var img = new byte[size * size * 2];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    img[(y + size) * size + x] = Sample(x, y, leftShift);
                    img[y * size + x] = Sample(x, y, rightShift);
                }
            }
            return img;

            byte Sample(int x, int y, int shift)
            {
                bool inside = InsideShape(shape, x - c - shift, y - c, r);
                bool[] field = inside ? foreground : background;
                int sx = inside ? Mod(x - shift, size) : x;
                bool on = field[(y / dotSize) * cells + sx / dotSize];
                return on ? Light : Dark;
            }
        }

        private static int Mod(int a, int m)
        {
            int r = a % m;
            return r < 0 ? r + m : r;
        }
    }
}
