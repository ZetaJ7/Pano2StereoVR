using NUnit.Framework;

namespace Pano2StereoVR.Study.Tests
{
    public sealed class RandomDotStereogramTests
    {
        [Test]
        public void TexelSizeAndDisparityConversionFollowQuadGeometry()
        {
            // 20 deg over 512 texels = 140.625 arcsec per texel
            Assert.AreEqual(140.625, RandomDotStereogram.TexelArcsec(20, 512), 1e-9);
            Assert.AreEqual(17, RandomDotStereogram.DisparityTexels(2400, 20, 512));
            Assert.AreEqual(9, RandomDotStereogram.DisparityTexels(1200, 20, 512));
            Assert.AreEqual(4, RandomDotStereogram.DisparityTexels(600, 20, 512));
            Assert.AreEqual(2, RandomDotStereogram.DisparityTexels(300, 20, 512));
            Assert.AreEqual(0, RandomDotStereogram.DisparityTexels(0, 20, 512));
            Assert.AreEqual(9 * 140.625, RandomDotStereogram.ActualDisparityArcsec(1200, 20, 512), 1e-9);
        }

        [Test]
        public void PixelScaleFollowsTheEyeProjection()
        {
            // symmetric 90 deg frustum over 2000 px: one pixel at the centre spans atan(2 / 2000)
            double expected = System.Math.Atan(2.0 / 2000.0) * 180.0 / System.Math.PI * 3600.0;
            Assert.AreEqual(expected, RandomDotStereogram.PixelArcsecAtCentre(1.0, 1.0, 2000), 1e-6);
            Assert.AreEqual(expected, RandomDotStereogram.PixelArcsecFromFov(90.0, 2000), 1e-6);
            // asymmetric frustum: only the total tangent span matters at the centre
            Assert.AreEqual(expected, RandomDotStereogram.PixelArcsecAtCentre(0.8, 1.2, 2000), 1e-6);
            // texture size for one texel per pixel: 2 tan(10 deg) / tan(pixel angle)
            int px = RandomDotStereogram.TexturePxForOnePixelTexels(20.0, expected);
            Assert.AreEqual((int)System.Math.Round(2.0 * System.Math.Tan(10.0 * System.Math.PI / 180.0) / (2.0 / 2000.0)), px);
            Assert.AreEqual(353, px);
            Assert.AreEqual(64, RandomDotStereogram.TexturePxForOnePixelTexels(1.0, expected), "clamped to the minimum");
            Assert.Throws<System.ArgumentOutOfRangeException>(() => RandomDotStereogram.PixelArcsecAtCentre(1, 1, 0));
        }

        [Test]
        public void CrossedDisparityShiftsLeftEyeRightAndRightEyeLeft()
        {
            RandomDotStereogram.EyeShifts(9, true, out int l, out int r);
            Assert.AreEqual(5, l);
            Assert.AreEqual(-4, r);
            Assert.AreEqual(9, l - r);
            RandomDotStereogram.EyeShifts(9, false, out l, out r);
            Assert.AreEqual(-5, l);
            Assert.AreEqual(4, r);
            RandomDotStereogram.EyeShifts(4, true, out l, out r);
            Assert.AreEqual(2, l);
            Assert.AreEqual(-2, r);
        }

        [TestCase("circle", 6)]
        [TestCase("square", 9)]
        [TestCase("triangle", 4)]
        [TestCase("cross", 2)]
        public void ShapeRegionCarriesTheRequestedDisparity(string shape, int d)
        {
            const int size = 96;
            byte[] img = RandomDotStereogram.GenerateTopBottom(size, shape, d, true, 1234);
            Assert.AreEqual(size * size * 2, img.Length);
            RandomDotStereogram.EyeShifts(d, true, out int ls, out int rs);
            double c = (size - 1) / 2.0;
            double radius = size * 0.22;
            int inside = 0;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    byte left = img[(y + size) * size + x];
                    bool inLeft = RandomDotStereogram.InsideShape(shape, x - c - ls, y - c, radius);
                    bool inRight = RandomDotStereogram.InsideShape(shape, x - c - rs, y - c, radius);
                    if (inLeft)
                    {
                        // the matching right-eye texel sits d texels further left
                        int xr = x - d;
                        Assert.IsTrue(xr >= 0);
                        Assert.AreEqual(left, img[y * size + xr], "shape texel mismatch at " + x + "," + y);
                        inside++;
                    }
                    else if (!inRight)
                    {
                        Assert.AreEqual(left, img[y * size + x], "background must be identical in both eyes");
                    }
                }
            }
            Assert.Greater(inside, 100);
        }

        [Test]
        public void ZeroDisparityGivesIdenticalEyes()
        {
            const int size = 64;
            byte[] img = RandomDotStereogram.GenerateTopBottom(size, "circle", 0, true, 7);
            for (int i = 0; i < size * size; i++)
            {
                Assert.AreEqual(img[i], img[i + size * size]);
            }
        }
    }
}
