using System.Linq;
using NUnit.Framework;

namespace Pano2StereoVR.Study.Tests
{
    /// <summary>Result page of the natural viewing range and the Part 2 advance icon (user 11-20).</summary>
    public sealed class StudyEyeRangeShapesTests
    {
        [Test]
        public void ShownValuesAreWholeDegreesOrAtLeastTheMaximum()
        {
            Assert.AreEqual("23°", EyeRangeShapes.ShownDegrees(23.4, 40f));
            Assert.AreEqual("22°", EyeRangeShapes.ShownDegrees(21.5, 40f), "half rounds away from zero");
            Assert.AreEqual("≥40°", EyeRangeShapes.ShownDegrees(40.0, 40f), "censored directions");
            Assert.AreEqual("≥40°", EyeRangeShapes.ShownDegrees(41.0, 40f));
            Assert.AreEqual("39°", EyeRangeShapes.ShownDegrees(39.2, 40f));
            Assert.AreEqual("—", EyeRangeShapes.ShownDegrees(double.NaN, 40f));
        }

        [Test]
        public void RingsEveryTenDegreesUpToTheMaximum()
        {
            CollectionAssert.AreEqual(new[] { 10f, 20f, 30f, 40f }, EyeRangeShapes.RingDegrees(40f));
            CollectionAssert.AreEqual(new[] { 10f, 20f, 30f, 35f }, EyeRangeShapes.RingDegrees(35f));
            CollectionAssert.IsEmpty(EyeRangeShapes.RingDegrees(0f));
        }

        [Test]
        public void RingTextureHasInkOnTheRingsAndIsSymmetric()
        {
            const int size = 128;
            byte[] a = EyeRangeShapes.RangeRings(size, new[] { 0.5f, 1f }, 8, 3f);
            Assert.AreEqual(size * size, a.Length);
            double outer = 0.5 * size - 3 - 1;
            int c = size / 2;
            Assert.Greater(a[c * size + (int)(c + 0.5 * outer)], 200, "on the inner ring");
            Assert.Greater(a[c * size + (int)(c + outer)], 200, "on the outer ring");
            Assert.AreEqual(0, a[0], "corner");
            Assert.AreEqual(0, a[(c + 10) * size + c + 25], "between rings and spokes");
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Assert.AreEqual(a[y * size + x], a[y * size + (size - 1 - x)], 1, "left-right symmetry");
                }
            }
        }

        [Test]
        public void FastForwardIconIsTwoRightPointingTriangles()
        {
            const int w = 96, h = 60;
            byte[] a = EyeRangeShapes.FastForward(w, h);
            Assert.AreEqual(w * h, a.Length);
            int mid = h / 2;
            Assert.AreEqual(255, a[mid * w + 2], "base of the first triangle");
            Assert.AreEqual(255, a[mid * w + w / 2 + 2], "base of the second triangle");
            Assert.AreEqual(0, a[1 * w + w / 2 - 2], "tip side of the first triangle is clear near the top");
            Assert.AreEqual(0, a[(h - 2) * w + w - 2], "tip side of the second triangle is clear near the bottom");
            // each triangle covers about half of its box
            double ink = a.Sum(v => v / 255.0) / (w * h);
            Assert.AreEqual(0.5, ink, 0.03);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Assert.AreEqual(a[y * w + x], a[(h - 1 - y) * w + x], "top-bottom symmetry");
                }
            }
        }
    }
}
