using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controls a full-screen dim overlay using the tutorial highlight shader.
/// It dims the screen and visually highlights a region around a target UI element.
/// </summary>
[RequireComponent(typeof(Graphic))]
public class TutorialHighlightOverlay : HighlightOverlayBase, ICanvasRaycastFilter
{
    [Tooltip("Mask alpha threshold used to decide whether clicks pass through the highlight.")]
    [Range(0f, 1f)]
    [SerializeField] private float _clickThroughAlphaThreshold = 0.05f;

    [Header("Click Settings")]
    [Tooltip("Optional UI controller to toggle when click limit is reached.")]
    [SerializeField] private TutorialUIController _tutorialUI;

    private int _clicksAllowed;
    private int _clickCount;
    private float _exitDelay;
    private bool _isClosing;

    private void LateUpdate()
    {
        if (_clicksAllowed > 0 && !_isClosing && Input.GetMouseButtonDown(0))
        {
            if (IsInsideHighlight(Input.mousePosition))
            {
                _clickCount++;
                if (_clickCount >= _clicksAllowed)
                {
                    _clicksAllowed = 0;
                    if (_tutorialUI != null)
                    {
                        if (_exitDelay > 0f)
                        {
                            if (!_isClosing)
                            {
                                _isClosing = true;
                                StartCoroutine(CloseAfterDelay(_exitDelay));
                            }
                        }
                        else
                        {
                            _tutorialUI.ToggleTutorial(false);
                        }
                    }
                }
            }
        }
    }

    /// <summary>Configure how many valid highlight clicks are allowed before auto-closing the tutorial.</summary>
    public void SetClickLimit(int clicksAllowed, float exitDelay)
    {
        _clicksAllowed = Mathf.Max(0, clicksAllowed);
        _clickCount = 0;
        _exitDelay = Mathf.Max(0f, exitDelay);
        _isClosing = false;
    }

    private IEnumerator CloseAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        _isClosing = false;
        _tutorialUI?.ToggleTutorial(false);
    }

    /// <summary>
    /// Returns true if the given screen position lies inside the current highlighted region.
    /// Uses mask alpha when available; falls back to extent approximation otherwise.
    /// </summary>
    public bool IsInsideHighlight(Vector2 screenPos)
    {
        if (!TryGetOverlayUv(screenPos, _uiCamera, out Vector2 uv))
            return false;

        return IsInsideHighlightAtUv(uv, _clickThroughAlphaThreshold);
    }

    /// <summary>
    /// UI raycast filter:
    /// - return true  => this overlay blocks clicks
    /// - return false => click passes through to UI below
    /// We pass clicks through only inside the highlight mask region.
    /// </summary>
    public bool IsRaycastLocationValid(Vector2 sp, Camera eventCamera)
    {
        if (_graphic == null || !_graphic.raycastTarget || !_graphic.enabled)
            return false;

        if (!TryGetOverlayUv(sp, eventCamera, out Vector2 uv))
            return true;

        bool isInsideHighlight = IsInsideHighlightAtUv(uv, _clickThroughAlphaThreshold);
        return !isInsideHighlight;
    }
}