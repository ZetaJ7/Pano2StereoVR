using UnityEngine;
using UnityEngine.UI;

namespace Pano2StereoVR.Study
{
    /// <summary>
    /// Keeps a short text on one line (per explicit line break) by stepping its font size down, at most to
    /// <see cref="MinRatio"/> of the size it was created with (user 11-19: small notes that wrap onto a new line
    /// look untidy, make those a little smaller instead). A text that would still wrap at the minimum size is a
    /// genuine paragraph: it keeps its original size and wraps as before.
    /// Attached by StudyUi to notes, secondary lines, SSQ item explanations, button labels and the replay hint.
    /// No engine objects are created in field initialisers (see the RectOffset lesson in StudyUi).
    /// </summary>
    [RequireComponent(typeof(Text))]
    public sealed class FitTextToLine : MonoBehaviour
    {
        public float MinRatio = 0.8f;

        private Text _text;
        private int _baseSize;
        private string _lastText;
        private float _lastWidth = -1f;

        public void Init(int baseSize, float minRatio)
        {
            _baseSize = baseSize;
            MinRatio = minRatio;
            _lastText = null;
        }

        /// <summary>Fit again on the next frame (the weight or style of the text changed).</summary>
        public void Refit()
        {
            _lastText = null;
        }

        private void Awake()
        {
            _text = GetComponent<Text>();
            if (_baseSize <= 0) _baseSize = _text.fontSize;
        }

        private void LateUpdate()
        {
            if (_text == null) return;
            float width = ((RectTransform)transform).rect.width;
            if (width <= 1f) return;
            if (_text.text == _lastText && Mathf.Abs(width - _lastWidth) < 0.5f) return;
            _lastText = _text.text;
            _lastWidth = width;
            Fit(width);
        }

        private void Fit(float width)
        {
            const float tolerance = 0.5f;
            int minSize = Mathf.Max(1, Mathf.RoundToInt(_baseSize * MinRatio));
            int size = _baseSize;
            if (string.IsNullOrEmpty(_text.text) || WidestLine(minSize) > width + tolerance)
            {
                size = _baseSize; // a real paragraph: wrap at the normal size
            }
            else
            {
                while (size > minSize && WidestLine(size) > width + tolerance) size--;
            }
            if (_text.fontSize != size)
            {
                _text.fontSize = size; // the setter marks layout and vertices dirty
            }
        }

        /// <summary>Unwrapped width of the widest line at the given size (same measure as Text.preferredWidth).</summary>
        private float WidestLine(int size)
        {
            TextGenerationSettings s = _text.GetGenerationSettings(Vector2.zero);
            s.fontSize = size;
            s.horizontalOverflow = HorizontalWrapMode.Overflow;
            float ppu = _text.pixelsPerUnit > 0f ? _text.pixelsPerUnit : 1f;
            return _text.cachedTextGeneratorForLayout.GetPreferredWidth(_text.text, s) / ppu;
        }
    }
}
