using TMPro;
using UnityEngine;

public class TutorialUIController : MonoBehaviour
{
    [SerializeField] private GameObject _mainPanel;
    [SerializeField] private GameObject _dialogBox;
    [SerializeField] private TextMeshProUGUI _flavorText;
    [SerializeField] private TextMeshProUGUI _instructionText;
    [SerializeField] private TextMeshProUGUI _defaultInstructionText;
    [SerializeField] private TutorialHighlightOverlay _highlightOverlay;
    [SerializeField] private RectTransform handPointer;

    /// <summary>
    /// Show the tutorial UI with data from a TutorialData struct.
    /// worldTargetPos is the resolved 3D position of the tray slot.
    /// </summary>
    public void DisplayTutorial(TutorialData data)
    {
        _mainPanel.SetActive(true);

        if (!string.IsNullOrEmpty(data.FlavorText) || !string.IsNullOrEmpty(data.InstructionText))
        {
            SetTexts(data.FlavorText, data.InstructionText);
            _defaultInstructionText.gameObject.SetActive(false);
            _dialogBox.SetActive(true);
        }
        else
        {
            _dialogBox.SetActive(false);
            _defaultInstructionText.gameObject.SetActive(true);
        }

        if (_highlightOverlay != null)
        {
            _highlightOverlay.SetVisible(true);

            if (data.OverlaySettings.SpriteOverlay != null)
            {
                _highlightOverlay.SetMaskSprite(data.OverlaySettings.SpriteOverlay);
            }

            _highlightOverlay.ApplyOverlaySettings(data.OverlaySettings);
            _highlightOverlay.SetClickLimit(Mathf.Clamp(data.ClicksAllowed, 1, int.MaxValue), data.ExitDelay);
        }

        handPointer.localPosition = data.PointerPosition;
    }

    public void ToggleTutorial(bool toggle)
    {
        _mainPanel.SetActive(toggle);
        if (_highlightOverlay != null)
        {
            _highlightOverlay.SetVisible(toggle);
        }
    }

    public void SetTexts(string flavor, string instruction)
    {
        _flavorText.text = flavor;
        _instructionText.text = instruction;
    }
}
