using NUnit.Framework;
using UnityEngine;

namespace Pano2StereoVR.Study.Tests
{
    /// <summary>Placement of the head-direction arrow of the orientation alignment (StudyAlignHint, user 2026-11-24).</summary>
    public sealed class StudyAlignHintTests
    {
        /// <summary>Direction for yaw and pitch in degrees (+ = right / up; x right, y up, z forward), as StudyRig.YawPitchOf.</summary>
        private static Vector3 Dir(float yawDeg, float pitchDeg)
        {
            float y = yawDeg * Mathf.Deg2Rad;
            float p = pitchDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(p) * Mathf.Sin(y), Mathf.Sin(p), Mathf.Cos(p) * Mathf.Cos(y));
        }

        private static bool Place(Vector3 head, Vector3 cue, float bob, out Vector3 dir, out Vector3 up, out float dev)
        {
            return StudyAlignHint.Place(head, cue, StudyAlignHint.OffsetFromCueDeg, StudyAlignHint.MaxFromViewCentreDeg, bob,
                out dir, out up, out dev);
        }

        [Test]
        public void HeadTooLowPutsAnUpwardArrowBelowTheCross()
        {
            Assert.IsTrue(Place(Dir(0f, -10f), Dir(0f, 0f), 0f, out Vector3 dir, out Vector3 up, out float dev));
            Assert.AreEqual(10f, dev, 1e-3f);
            StudyRig.YawPitchOf(dir, out float yaw, out float pitch);
            Assert.AreEqual(0f, yaw, 1e-3f);
            Assert.AreEqual(-6f, pitch, 1e-3f, "6 deg below the cross, 4 deg above the view centre");
            Assert.Greater(up.y, 0.99f, "the arrow points up");
            Assert.AreEqual(0f, up.x, 1e-4f);
            Assert.AreEqual(0f, Vector3.Dot(up, dir), 1e-4f, "the tip direction lies in the view plane");
        }

        [Test]
        public void HeadDownAndRightPutsAnUpLeftArrowAtTheLowerRightOfTheCross()
        {
            Vector3 cue = Dir(0f, 0f);
            Assert.IsTrue(Place(Dir(8f, -8f), cue, 0f, out Vector3 dir, out Vector3 up, out _));
            StudyRig.YawPitchOf(dir, out float yaw, out float pitch);
            Assert.Greater(yaw, 0f, "right of the cross");
            Assert.Less(pitch, 0f, "below the cross");
            Assert.AreEqual(StudyAlignHint.OffsetFromCueDeg, Vector3.Angle(dir, cue), 1e-2f);
            Assert.Less(up.x, -0.5f, "points left");
            Assert.Greater(up.y, 0.5f, "points up");
        }

        [Test]
        public void LargeDeviationKeepsTheArrowNearTheViewCentre()
        {
            Vector3 head = Dir(60f, 0f);
            Assert.IsTrue(Place(head, Dir(0f, 0f), 0f, out Vector3 dir, out Vector3 up, out float dev));
            Assert.AreEqual(60f, dev, 1e-2f);
            Assert.AreEqual(StudyAlignHint.MaxFromViewCentreDeg, Vector3.Angle(head, dir), 1e-2f);
            StudyRig.YawPitchOf(dir, out float yaw, out _);
            Assert.AreEqual(40f, yaw, 1e-2f);
            // head turned right: the tip points along the horizon towards smaller yaw (to the viewer's left)
            Assert.AreEqual(0f, up.y, 1e-4f);
            Vector3 left = new Vector3(-Mathf.Cos(40f * Mathf.Deg2Rad), 0f, Mathf.Sin(40f * Mathf.Deg2Rad));
            Assert.AreEqual(1f, Vector3.Dot(up, left), 1e-4f);
        }

        [Test]
        public void SmallDeviationPutsTheArrowAtTheViewCentre()
        {
            Vector3 head = Dir(0f, -5.5f);
            Assert.IsTrue(Place(head, Dir(0f, 0f), 0f, out Vector3 dir, out Vector3 up, out _));
            Assert.AreEqual(0f, Vector3.Angle(head, dir), 1e-2f);
            Assert.Greater(up.y, 0.99f);
        }

        [Test]
        public void BobMovesAlongTheArrowAndNeverPassesTheCross()
        {
            Vector3 head = Dir(0f, -10f);
            Vector3 cue = Dir(0f, 0f);
            Assert.IsTrue(Place(head, cue, 0.6f, out Vector3 dir, out _, out _));
            StudyRig.YawPitchOf(dir, out _, out float pitch);
            Assert.AreEqual(-5.4f, pitch, 1e-3f);
            Place(head, cue, 50f, out dir, out _, out _); // tip at the cross: no tangent, but dir stays on the arc
            Assert.LessOrEqual(Vector3.Angle(head, dir), 10f + 1e-3f);
            Assert.AreEqual(0f, StudyAlignHint.Bob(0f), 1e-6f);
            Assert.AreEqual(StudyAlignHint.BobAmplitudeDeg, StudyAlignHint.Bob(StudyAlignHint.BobPeriodSeconds / 4f), 1e-4f);
        }

        [Test]
        public void SameDirectionGivesNoArrow()
        {
            Assert.IsFalse(Place(Dir(10f, 5f), Dir(10f, 5f), 0f, out _, out _, out float dev));
            Assert.AreEqual(0f, dev, 1e-2f);
        }

        [Test]
        public void ScreenAngleIsMeasuredFromRightCounterclockwise()
        {
            Assert.AreEqual(90f, StudyAlignHint.ScreenAngleDeg(0f, 3f), 1e-3f);
            Assert.AreEqual(135f, StudyAlignHint.ScreenAngleDeg(-2f, 2f), 1e-3f);
            Assert.AreEqual(0f, StudyAlignHint.ScreenAngleDeg(4f, 0f), 1e-3f);
        }
    }
}
