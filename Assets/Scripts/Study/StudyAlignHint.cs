using UnityEngine;

namespace Pano2StereoVR.Study
{
    /// <summary>
    /// Head-direction hint for the orientation alignment (user 2026-11-24): when the head has stayed outside the
    /// alignment tolerance for <see cref="DelaySeconds"/>, an arrow appears next to the yellow cross on the side the
    /// participant looks from and points at the cross (head too low: below the cross, pointing up; head down and to
    /// the right: lower right of the cross, pointing up and to the left). With a large deviation the arrow stays
    /// within <see cref="MaxFromViewCentreDeg"/> of the view centre, so it is always visible. Yaw and pitch only.
    /// The geometry (<see cref="Place"/>) uses plain vector maths so it can be tested without the engine.
    /// </summary>
    public static class StudyAlignHint
    {
        /// <summary>Time outside the tolerance before the arrow appears (s).</summary>
        public const float DelaySeconds = 2f;

        /// <summary>Angular distance from the cross centre to the arrow centre (deg): clear of the dwell ring.</summary>
        public const float OffsetFromCueDeg = 6f;

        /// <summary>Largest angular distance of the arrow centre from the view centre (deg).</summary>
        public const float MaxFromViewCentreDeg = 20f;

        /// <summary>Amplitude (deg) and period (s) of the small back-and-forth motion along the arrow.</summary>
        public const float BobAmplitudeDeg = 0.6f;
        public const float BobPeriodSeconds = 0.9f;

        /// <summary>
        /// Where the arrow goes for a head direction <paramref name="head"/> and the cue direction <paramref name="cue"/>
        /// (unit vectors, world). <paramref name="dir"/> is the direction of the arrow centre from the eye, on the great
        /// circle from the view centre to the cue, at min(max(deviation - offset, 0), maximum) from the view centre;
        /// <paramref name="up"/> is the unit tangent at <paramref name="dir"/> pointing towards the cue (the arrow's tip
        /// direction). Returns false when the two directions coincide or are opposite (no defined arrow).
        /// </summary>
        public static bool Place(Vector3 head, Vector3 cue, float offsetDeg, float maxFromCentreDeg, float bobDeg,
            out Vector3 dir, out Vector3 up, out float deviationDeg)
        {
            Vector3 h = head.normalized;
            Vector3 c = cue.normalized;
            float cos = Mathf.Clamp(Vector3.Dot(h, c), -1f, 1f);
            float omega = Mathf.Acos(cos);
            deviationDeg = omega * Mathf.Rad2Deg;
            dir = h;
            up = Vector3.zero;
            float sinOmega = Mathf.Sin(omega);
            if (sinOmega < 1e-4f)
            {
                return false;
            }
            float fromCentreDeg = Mathf.Clamp(deviationDeg - offsetDeg, 0f, maxFromCentreDeg) + bobDeg;
            fromCentreDeg = Mathf.Clamp(fromCentreDeg, 0f, deviationDeg);
            float t = fromCentreDeg / deviationDeg;
            // spherical interpolation from the view centre towards the cue
            dir = (Mathf.Sin((1f - t) * omega) / sinOmega) * h + (Mathf.Sin(t * omega) / sinOmega) * c;
            dir = dir.normalized;
            Vector3 towards = c - Vector3.Dot(c, dir) * dir;
            if (towards.sqrMagnitude < 1e-10f)
            {
                return false;
            }
            up = towards.normalized;
            return true;
        }

        /// <summary>Bob offset (deg) at time <paramref name="seconds"/> since the arrow appeared: along the arrow, 0 at start.</summary>
        public static float Bob(float seconds)
        {
            return BobAmplitudeDeg * Mathf.Sin(2f * Mathf.PI * seconds / BobPeriodSeconds);
        }

        /// <summary>
        /// Screen direction of the cue as seen from the head, in degrees (0 = right, 90 = up), from the yaw and pitch
        /// differences cue minus head (deg). Logged with each hint.
        /// </summary>
        public static float ScreenAngleDeg(float dYawDeg, float dPitchDeg)
        {
            return Mathf.Atan2(dPitchDeg, dYawDeg) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Arrow made of three quads in the local XY plane, tip at local +Y (shaft and a two-bar head), same size scale
        /// as the orientation cross (world units at the panel distance). The quads face the viewer when the object is
        /// rotated with LookRotation(direction from the eye, tip direction), as the cross is.
        /// </summary>
        public static GameObject Build(Material material)
        {
            var root = new GameObject("AlignHintArrow");
            const float width = 0.025f;
            const float tipY = 0.07f;
            const float headLen = 0.07f;
            AddBar(root.transform, material, new Vector2(0f, -0.015f), 0f, new Vector2(width, 0.11f));
            float h = 0.5f * headLen * 0.70710678f;
            AddBar(root.transform, material, new Vector2(-h, tipY - h), -45f, new Vector2(width, headLen));
            AddBar(root.transform, material, new Vector2(h, tipY - h), 45f, new Vector2(width, headLen));
            root.SetActive(false);
            return root;
        }

        private static void AddBar(Transform parent, Material material, Vector2 centre, float angleDeg, Vector2 size)
        {
            GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(bar.GetComponent<Collider>());
            bar.name = "Bar";
            bar.transform.SetParent(parent, false);
            bar.transform.localPosition = new Vector3(centre.x, centre.y, 0f);
            bar.transform.localRotation = Quaternion.Euler(0f, 0f, angleDeg);
            bar.transform.localScale = new Vector3(size.x, size.y, 1f);
            bar.GetComponent<Renderer>().sharedMaterial = material;
        }
    }
}
