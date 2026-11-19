using UnityEngine;
using UnityEngine.UI;

namespace Pano2StereoVR.Study
{
    /// <summary>
    /// Keeps a button's label in step with its state (normal, selected, disabled). The Button ColorBlock only
    /// tints the background image, so the label needs its own colour for a clear disabled state and for
    /// selected choice buttons; a selected answer is also set bold (<see cref="BoldWhenSelected"/>).
    /// <see cref="Accent"/> is an optional second text on the button (the number of a scale option) that
    /// follows the label colour.
    /// </summary>
    public sealed class StudyButtonStyle : MonoBehaviour
    {
        public Button Button;
        public Text Label;
        public Text Accent;
        public Color Background;
        public Color Hover;
        public Color Pressed;
        public Color DisabledBackground;
        public Color SelectedBackground;
        public Color LabelColor;
        public Color DisabledLabel;
        public Color SelectedLabel;
        /// <summary>Label weight: always bold (primary buttons), or bold only while selected (answer options).</summary>
        public bool Bold;
        public bool BoldWhenSelected;

        public bool Selected { get; private set; }

        public void SetSelected(bool selected)
        {
            Selected = selected;
            if (Button == null)
            {
                return;
            }
            ColorBlock cb = Button.colors;
            cb.normalColor = selected ? SelectedBackground : Background;
            cb.selectedColor = cb.normalColor;
            cb.highlightedColor = selected ? SelectedBackground : Hover;
            Button.colors = cb;
            ApplyWeight();
            Refresh();
        }

        /// <summary>Set the label weight for the current state (a bold label is wider, so the line fit runs again).</summary>
        public void ApplyWeight()
        {
            if (Label == null)
            {
                return;
            }
            FontStyle style = Bold || (BoldWhenSelected && Selected) ? FontStyle.Bold : FontStyle.Normal;
            if (Label.fontStyle != style)
            {
                Label.fontStyle = style;
                FitTextToLine fit = Label.GetComponent<FitTextToLine>();
                if (fit != null)
                {
                    fit.Refit();
                }
            }
        }

        private void LateUpdate()
        {
            Refresh();
        }

        public void Refresh()
        {
            if (Label == null || Button == null)
            {
                return;
            }
            Color c = !Button.IsInteractable() ? DisabledLabel : (Selected ? SelectedLabel : LabelColor);
            if (Label.color != c)
            {
                Label.color = c;
            }
            if (Accent != null && Accent.color != c)
            {
                Accent.color = c;
            }
        }
    }
}
