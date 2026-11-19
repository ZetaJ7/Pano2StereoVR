using System;

namespace Pano2StereoVR.Study
{
    /// <summary>
    /// Coverage maps (one byte per texel, row 0 at the bottom) for the shapes the study UI draws: rounded
    /// rectangles (buttons, cards, pills, round badges), their soft shadow, and the tick of a check box.
    /// StudyUi turns them into nine-sliced sprites at runtime, so the project needs no image assets.
    /// Pure C#: no engine calls, so the maps can be checked in the EditMode tests outside the editor.
    /// </summary>
    public static class StudyShapes
    {
        /// <summary>
        /// Rounded square of size x size texels. inset: distance of the shape's edge from the texture edge;
        /// radius: corner radius; feather: width of the edge ramp in texels (1 = anti-aliased hard edge, larger
        /// = blurred, centred on the edge); smooth: ease the ramp (used for shadows).
        /// </summary>
        public static byte[] RoundedBox(int size, float inset, float radius, float feather, bool smooth)
        {
            if (size <= 0)
            {
                return new byte[0];
            }
            var a = new byte[size * size];
            float half = size * 0.5f - inset;
            float r = Math.Max(0f, Math.Min(radius, half));
            float ramp = Math.Max(1e-3f, feather);
            float c = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float qx = Math.Abs(x + 0.5f - c) - (half - r);
                    float qy = Math.Abs(y + 0.5f - c) - (half - r);
                    float ox = Math.Max(qx, 0f);
                    float oy = Math.Max(qy, 0f);
                    // signed distance to the rounded box: negative inside
                    float d = (float)Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0f) - r;
                    float t = Clamp01(0.5f - d / ramp);
                    if (smooth)
                    {
                        t = t * t * (3f - 2f * t);
                    }
                    a[y * size + x] = (byte)Math.Round(t * 255f);
                }
            }
            return a;
        }

        /// <summary>Tick (check mark) with round ends in a size x size texture.</summary>
        public static byte[] CheckMark(int size)
        {
            if (size <= 0)
            {
                return new byte[0];
            }
            var a = new byte[size * size];
            // start, corner and end of the tick in texture coordinates (y up)
            float x0 = 0.22f * size, y0 = 0.52f * size;
            float x1 = 0.42f * size, y1 = 0.30f * size;
            float x2 = 0.79f * size, y2 = 0.71f * size;
            float halfWidth = 0.065f * size;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float d = Math.Min(SegmentDistance(px, py, x0, y0, x1, y1), SegmentDistance(px, py, x1, y1, x2, y2));
                    a[y * size + x] = (byte)Math.Round(Clamp01(0.5f - (d - halfWidth)) * 255f);
                }
            }
            return a;
        }

        private static float SegmentDistance(float px, float py, float ax, float ay, float bx, float by)
        {
            float vx = bx - ax, vy = by - ay;
            float wx = px - ax, wy = py - ay;
            float len2 = vx * vx + vy * vy;
            float t = len2 > 0f ? Clamp01((wx * vx + wy * vy) / len2) : 0f;
            float dx = wx - t * vx, dy = wy - t * vy;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private static float Clamp01(float v)
        {
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }
    }
}
