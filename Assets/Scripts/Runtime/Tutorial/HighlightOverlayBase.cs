using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Base class for highlight overlays that drive the TutorialHighlight shader.
/// Handles material instance, mask sprite, scale and UV offset math.
/// </summary>
[RequireComponent(typeof(Graphic))]
public abstract class HighlightOverlayBase : MonoBehaviour
{
    [Header("Shader / Material")]
    [Tooltip("Graphic that uses the tutorial highlight material (Image, RawImage, etc.).")]
    [SerializeField] protected Graphic _graphic;

    [Tooltip("Camera used for UI (null for Screen Space - Overlay).")]
    [SerializeField] protected Camera _uiCamera;

    [Header("Highlight Settings")]
    [Tooltip("Approximate extent of the highlighted region in normalized viewport units (used for hit-testing only).")]
    [SerializeField] protected float _highlightExtent = 0.15f;

    protected Material _runtimeMaterial;
    protected Vector2 _currentHighlightUv = new Vector2(0.5f, 0.5f);
    protected Vector2 _highlightScale = Vector2.one;
    protected Vector2 _userOffset = Vector2.zero;
    protected Texture2D _maskTexture;

    protected static readonly int HighlightMaskId = Shader.PropertyToID("_HighlightMask");
    protected static readonly int HighlightScaleId = Shader.PropertyToID("_HighlightScale");
    protected static readonly int HighlightOffsetId = Shader.PropertyToID("_HighlightOffset");

    protected virtual void Awake()
    {
        if (_graphic != null)
        {
            // Clone the material so changes are per-instance.
            _runtimeMaterial = Instantiate(_graphic.material);
            _graphic.material = _runtimeMaterial;
        }
    }

    /// <summary>Enable/disable the dim overlay.</summary>
    public void SetVisible(bool visible)
    {
        if (_graphic != null)
            _graphic.enabled = visible;
    }

    /// <summary>Set the highlight mask scale (X,Y) used by the shader.</summary>
    private void SetHighlightScale(Vector2 scale)
    {
        // Treat zero scale as "use default".
        if (Mathf.Approximately(scale.x, 0f) && Mathf.Approximately(scale.y, 0f))
        {
            scale = Vector2.one;
        }

        _highlightScale = scale;

        if (_runtimeMaterial != null)
        {
            _runtimeMaterial.SetVector(HighlightScaleId, new Vector4(_highlightScale.x, _highlightScale.y, 0f, 0f));
        }
    }

    /// <summary>Set additional UV offset for fine-tuning the highlight center.</summary>
    private void SetHighlightOffset(Vector2 offset)
    {
        _userOffset = offset;

        if (_runtimeMaterial != null)
        {
            Vector2 baseOffset = new Vector2(_currentHighlightUv.x - 0.5f, _currentHighlightUv.y - 0.5f);
            Vector2 combined = baseOffset + _userOffset;
            _runtimeMaterial.SetVector(HighlightOffsetId, new Vector4(combined.x, combined.y, 0f, 0f));
        }
    }

    /// <summary>Apply scale and offset from overlay settings.</summary>
    public void ApplyOverlaySettings(HighlightOverlaySettings settings)
    {
        SetHighlightScale(settings.HighlightScale);
        SetHighlightOffset(settings.HighlightOffset);
    }

    /// <summary>Swap the highlight mask sprite on the runtime material.</summary>
    public void SetMaskSprite(Sprite sprite)
    {
        if (_runtimeMaterial != null && sprite != null)
        {
            _runtimeMaterial.SetTexture(HighlightMaskId, sprite.texture);
            _maskTexture = sprite.texture;
        }
    }

    /// <summary>
    /// Map a screen position into the overlay's 0..1 UV space.
    /// </summary>
    protected bool TryGetOverlayUv(Vector2 screenPos, Camera eventCamera, out Vector2 uv)
    {
        uv = new Vector2(0.5f, 0.5f);

        if (_graphic == null)
            return false;

        RectTransform rt = _graphic.rectTransform;
        if (rt == null)
            return false;

        Camera cam = eventCamera != null ? eventCamera : _uiCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screenPos, cam, out Vector2 localPoint))
            return false;

        Rect rect = rt.rect;
        if (rect.width <= 0f || rect.height <= 0f)
            return false;

        float u = Mathf.InverseLerp(rect.xMin, rect.xMax, localPoint.x);
        float v = Mathf.InverseLerp(rect.yMin, rect.yMax, localPoint.y);
        uv = new Vector2(u, v);
        return true;
    }

    /// <summary>
    /// Evaluate whether a UV lies inside the highlight based on the mask and thresholds.
    /// </summary>
    protected bool IsInsideHighlightAtUv(Vector2 overlayUv, float alphaThreshold)
    {
        Vector2 baseOffset = new Vector2(_currentHighlightUv.x - 0.5f, _currentHighlightUv.y - 0.5f);
        Vector2 combinedOffset = baseOffset + _userOffset;
        Vector2 maskUv = (overlayUv - new Vector2(0.5f, 0.5f)) * _highlightScale + new Vector2(0.5f, 0.5f);
        maskUv += combinedOffset;

        if (_maskTexture != null && _maskTexture.isReadable)
        {
            if (maskUv.x < 0f || maskUv.x > 1f || maskUv.y < 0f || maskUv.y > 1f)
                return false;

            float alpha = _maskTexture.GetPixelBilinear(maskUv.x, maskUv.y).a;
            return alpha >= alphaThreshold;
        }

        // Fallback if texture is not readable: use viewport-distance approximation.
        return Vector2.Distance(overlayUv, _currentHighlightUv) < _highlightExtent;
    }
}

