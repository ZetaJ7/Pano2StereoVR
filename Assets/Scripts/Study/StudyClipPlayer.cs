using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Video;

namespace Pano2StereoVR.Study
{
    /// <summary>Playback statistics of one segment (written to playback.csv).</summary>
    public sealed class PlaybackStats
    {
        public string File = "";
        public long FramesExpected;
        public int FramesShown;
        public int FramesDropped;
        public bool Missing;
        public string Error = "";
        /// <summary>Seconds the segment was on screen (until the end of one play, or until the participant advanced).</summary>
        public float ViewSeconds;
        /// <summary>Completed full plays (loop ends) while the segment was on screen.</summary>
        public int Loops;
        /// <summary>Stopped by the QA skip control.</summary>
        public bool Aborted;
        public readonly List<float> FrameIntervalsMs = new List<float>();

        public float DtMeanMs => FrameIntervalsMs.Count == 0 ? float.NaN : FrameIntervalsMs.Average();

        public float DtP99Ms
        {
            get
            {
                if (FrameIntervalsMs.Count == 0)
                {
                    return float.NaN;
                }
                List<float> s = FrameIntervalsMs.OrderBy(x => x).ToList();
                int i = Mathf.Clamp(Mathf.CeilToInt(0.99f * s.Count) - 1, 0, s.Count - 1);
                return s[i];
            }
        }
    }

    /// <summary>How a segment is played. Self-paced segments loop after one full play until the participant advances.</summary>
    public sealed class PlayRequest
    {
        /// <summary>Clip length used when the file is missing (placeholder) or reports no length.</summary>
        public float NominalSeconds = 10f;
        public bool SelfPaced;
        /// <summary>Advancing may end a non-looping play before the clip end (replays).</summary>
        public bool EarlyAdvance;
        /// <summary>Polled every frame; true stops playback at once (QA skip).</summary>
        public Func<bool> Abort;
        /// <summary>Earliest advance (s); 0 or less = one full play of the clip.</summary>
        public float MinViewSeconds;
        /// <summary>Automatic advance (s); 0 or less = no limit.</summary>
        public float MaxViewSeconds;
        /// <summary>Polled every frame once advancing is allowed.</summary>
        public Func<bool> AdvanceRequested;
        /// <summary>Called once when advancing becomes allowed (show the hint).</summary>
        public Action AdvanceAllowed;
        public Action<Texture> OnTexture;
        public Action<float, long> OnFrame;

        /// <summary>Earliest advance for a clip of the given length.</summary>
        public float EffectiveMinSeconds(double clipLength)
        {
            return MinViewSeconds > 0f ? MinViewSeconds : (float)clipLength;
        }

        /// <summary>Automatic advance for a clip of the given length (never earlier than the minimum); 0 = none.</summary>
        public float EffectiveMaxSeconds(double clipLength)
        {
            return MaxViewSeconds > 0f ? Math.Max(MaxViewSeconds, EffectiveMinSeconds(clipLength)) : 0f;
        }
    }

    /// <summary>
    /// Two VideoPlayer slots that render top-bottom stereo clips into RenderTextures. A slot can be prepared
    /// while the other one plays or while the participant answers, so playback starts without loading delay.
    /// Missing files are replaced by a placeholder pattern for the nominal clip length (QA mode only; a formal
    /// session cannot start while stimuli are missing).
    /// </summary>
    public sealed class StudyClipPlayer : MonoBehaviour
    {
        public sealed class Slot
        {
            public VideoPlayer Player;
            public RenderTexture Texture;
            public string Path = "";
            public bool Prepared;
            public bool Failed;
            public string Error = "";
            public int Dropped;
            public bool Ended;
            public int Loops;
        }

        private readonly Slot[] _slots = new Slot[2];
        private Texture2D _placeholder;

        public Texture Placeholder => _placeholder;

        private void Awake()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                var go = new GameObject("VideoSlot" + i);
                go.transform.SetParent(transform, false);
                VideoPlayer vp = go.AddComponent<VideoPlayer>();
                vp.playOnAwake = false;
                vp.isLooping = false;
                vp.skipOnDrop = true;
                vp.waitForFirstFrame = true;
                vp.audioOutputMode = VideoAudioOutputMode.None;
                vp.renderMode = VideoRenderMode.RenderTexture;
                vp.aspectRatio = VideoAspectRatio.Stretch;
                vp.source = VideoSource.Url;
                var slot = new Slot { Player = vp };
                vp.prepareCompleted += p => slot.Prepared = true;
                vp.errorReceived += (p, msg) => { slot.Failed = true; slot.Error = msg; };
                vp.frameDropped += p => slot.Dropped++;
                vp.loopPointReached += p => { slot.Ended = true; slot.Loops++; };
                _slots[i] = slot;
            }
            _placeholder = MakePlaceholder(256);
        }

        private void OnDestroy()
        {
            foreach (Slot s in _slots)
            {
                if (s?.Player != null)
                {
                    s.Player.Stop();
                    s.Player.targetTexture = null;
                }
                if (s?.Texture != null)
                {
                    s.Texture.Release();
                    Destroy(s.Texture);
                }
            }
            if (_placeholder != null)
            {
                Destroy(_placeholder);
            }
        }

        /// <summary>Start loading a clip into a slot (0 or 1).</summary>
        public Slot Prepare(int index, string path)
        {
            Slot s = _slots[index];
            s.Player.Stop();
            s.Path = path ?? "";
            s.Prepared = false;
            s.Failed = false;
            s.Error = "";
            s.Dropped = 0;
            s.Ended = false;
            s.Loops = 0;
            s.Player.isLooping = false;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                s.Failed = true;
                s.Error = "missing file";
                return s;
            }
            s.Player.url = path;
            s.Player.Prepare();
            return s;
        }

        public IEnumerator WaitPrepared(Slot s, float timeoutSeconds)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!s.Prepared && !s.Failed && Time.realtimeSinceStartup - t0 < timeoutSeconds)
            {
                yield return null;
            }
            if (!s.Prepared && !s.Failed)
            {
                s.Failed = true;
                s.Error = "prepare timeout";
            }
            if (s.Prepared)
            {
                EnsureTexture(s);
            }
        }

        private static void EnsureTexture(Slot s)
        {
            int w = (int)Math.Max(1, s.Player.width);
            int h = (int)Math.Max(1, s.Player.height);
            if (s.Texture == null || s.Texture.width != w || s.Texture.height != h)
            {
                if (s.Texture != null)
                {
                    s.Texture.Release();
                    Destroy(s.Texture);
                }
                s.Texture = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
                {
                    wrapModeU = TextureWrapMode.Repeat,
                    wrapModeV = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    useMipMap = false,
                    name = "StudyClip_" + Path.GetFileNameWithoutExtension(s.Path),
                };
                s.Texture.Create();
            }
            s.Player.targetTexture = s.Texture;
        }

        /// <summary>Play a prepared slot once to its end (not self-paced).</summary>
        public IEnumerator Play(Slot s, float nominalSeconds, PlaybackStats stats, Action<Texture> onTexture,
            Action<float, long> onFrame)
        {
            return Play(s, new PlayRequest { NominalSeconds = nominalSeconds, OnTexture = onTexture, OnFrame = onFrame }, stats);
        }

        /// <summary>
        /// Play a prepared slot. Not self-paced: once to the end. Self-paced: the clip loops; advancing is
        /// allowed after the minimum viewing time and happens when AdvanceRequested returns true or at the
        /// maximum viewing time. OnTexture is called once with the texture to show; OnFrame every display
        /// frame with the elapsed time and the current video frame index. Missing clips show the placeholder
        /// with the nominal length.
        /// </summary>
        public IEnumerator Play(Slot s, PlayRequest req, PlaybackStats stats)
        {
            stats.File = Path.GetFileName(s.Path);
            bool placeholder = s.Failed || !s.Prepared;
            double length = !placeholder && s.Player.length > 0 ? s.Player.length : req.NominalSeconds;
            length = Math.Max(0.1, length);
            float minView = req.EffectiveMinSeconds(length);
            float maxView = req.EffectiveMaxSeconds(length);
            bool allowed = false;
            float start = Time.realtimeSinceStartup;

            if (placeholder)
            {
                stats.Missing = true;
                stats.Error = s.Error;
                req.OnTexture?.Invoke(_placeholder);
                while (true)
                {
                    float el = Time.realtimeSinceStartup - start;
                    if (req.Abort != null && req.Abort())
                    {
                        stats.Aborted = true;
                        break;
                    }
                    if (!req.SelfPaced && el >= length)
                    {
                        break;
                    }
                    if ((req.SelfPaced || req.EarlyAdvance) && Advance(req, el, minView, maxView, ref allowed))
                    {
                        break;
                    }
                    stats.FrameIntervalsMs.Add(Time.unscaledDeltaTime * 1000f);
                    req.OnFrame?.Invoke(el, -1);
                    yield return null;
                }
                stats.ViewSeconds = Time.realtimeSinceStartup - start;
                stats.Loops = (int)Math.Floor(stats.ViewSeconds / length + 1e-6);
                yield break;
            }

            long frameCount = (long)s.Player.frameCount;
            req.OnTexture?.Invoke(s.Texture);
            s.Ended = false;
            s.Dropped = 0;
            s.Loops = 0;
            s.Player.isLooping = req.SelfPaced;
            s.Player.Play();
            start = Time.realtimeSinceStartup;
            float timeout = (float)length + 3f;
            var seen = new HashSet<long>();
            long last = -1;
            int wraps = 0;
            while (true)
            {
                float el = Time.realtimeSinceStartup - start;
                if (req.Abort != null && req.Abort())
                {
                    stats.Aborted = true;
                    break;
                }
                if (!req.SelfPaced && (s.Ended || el >= timeout))
                {
                    break;
                }
                if ((req.SelfPaced || req.EarlyAdvance) && Advance(req, el, minView, maxView, ref allowed))
                {
                    break;
                }
                long f = s.Player.frame;
                if (f >= 0)
                {
                    if (last >= 0 && f < last)
                    {
                        wraps++;
                    }
                    last = f;
                    seen.Add(wraps * Math.Max(1, frameCount) + f);
                }
                stats.FrameIntervalsMs.Add(Time.unscaledDeltaTime * 1000f);
                req.OnFrame?.Invoke(el, f);
                yield return null;
            }
            s.Player.Pause();
            s.Player.isLooping = false;
            stats.ViewSeconds = Time.realtimeSinceStartup - start;
            stats.Loops = req.SelfPaced ? s.Loops : (s.Ended ? 1 : 0);
            // frames that should have been shown: one play, or the viewing time at the clip frame rate when looping
            stats.FramesExpected = req.SelfPaced && s.Player.frameRate > 0
                ? (long)Math.Round(stats.ViewSeconds * s.Player.frameRate)
                : frameCount;
            stats.FramesShown = seen.Count;
            stats.FramesDropped = s.Dropped;
            if (!req.SelfPaced && !s.Ended && !stats.Aborted && !(req.EarlyAdvance && allowed))
            {
                stats.Error = "timeout";
            }
        }

        private static bool Advance(PlayRequest req, float elapsed, float minView, float maxView, ref bool allowed)
        {
            if (!allowed && elapsed >= minView)
            {
                allowed = true;
                req.AdvanceAllowed?.Invoke();
            }
            if (maxView > 0f && elapsed >= maxView)
            {
                return true;
            }
            return allowed && req.AdvanceRequested != null && req.AdvanceRequested();
        }

        public void StopAll()
        {
            foreach (Slot s in _slots)
            {
                s.Player.Stop();
            }
        }

        private static Texture2D MakePlaceholder(int size)
        {
            // Top-bottom placeholder: checker in the left half (top) and stripes in the right half (bottom),
            // so a missing clip is obvious and the eye assignment can still be checked.
            var tex = new Texture2D(size * 2, size * 2, TextureFormat.RGBA32, false)
            {
                wrapModeU = TextureWrapMode.Repeat,
                wrapModeV = TextureWrapMode.Clamp,
                name = "StudyPlaceholder",
            };
            var px = new Color32[tex.width * tex.height];
            for (int y = 0; y < tex.height; y++)
            {
                bool top = y >= size;
                for (int x = 0; x < tex.width; x++)
                {
                    bool on = top ? ((x / 32 + y / 32) % 2 == 0) : (x / 32 % 2 == 0);
                    byte v = (byte)(on ? 150 : 90);
                    px[y * tex.width + x] = top ? new Color32(v, (byte)(v / 2), (byte)(v / 2), 255) : new Color32((byte)(v / 2), (byte)(v / 2), v, 255);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }
    }
}
