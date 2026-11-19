using UnityEngine;
using UnityEngine.UI;

namespace Pano2StereoVR.Study
{
    /// <summary>
    /// Keeps a button's label colour in step with its state (normal, selected, disabled). The Button
    /// ColorBlock only tints the background image, so the label needs its own colour for a clear
    /// disabled state and for selected choice buttons.
    /// </summary>
    public sealed class StudyButtonStyle : MonoBehaviour
    {
        public Button Button;
        public Text Label;
        public Color Background;
        public Color Hover;
        public Color Pressed;
        public Color DisabledBackground;
        public Color SelectedBackground;
        public Color LabelColor;
        public Color DisabledLabel;
        public Color SelectedLabel;

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
            Refresh();
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
        }
    }
}
