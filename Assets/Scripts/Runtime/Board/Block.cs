using UnityEngine;

/// <summary>
/// One block on the board. It tracks its grid slot + tier, color data, and a little visual state.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class Block : MonoBehaviour
{
    private const float ShrinkTargetScale = 0.1f;
    private const int SpecialBlockMaxHitPoints = 15;

    [Header("State")]
    [SerializeField] private BlockColorData _colorData;
    [SerializeField] private int _column;
    [SerializeField] private int _row;
    [SerializeField] private int _tierLevel;
    [SerializeField] private bool _isSpecial;
    [SerializeField] private int _specialHitPoints;
    [SerializeField] private GameObject _specialTargetEffects;

    private BlockGrid _grid;
    [SerializeField] private Renderer _renderer;
    private Quaternion _baseRendererRotation;
    private bool _isMoving;
    private MaterialPropertyBlock _materialPropertyBlock;

    /// <summary>True while this block is sliding around; we skip targeting during that time.</summary>
    public bool IsMoving => _isMoving;

    /// <summary>Color data used for matching (same asset means same color).</summary>
    public BlockColorData ColorData => _colorData;

    /// <summary>Grid column index (0 is leftmost).</summary>
    public int Column => _column;

    /// <summary>Grid row index (0 is the front/bottom line, Z=0).</summary>
    public int Row => _row;

    /// <summary>Tier level in a stack (0 = bottom, 1 = one above, etc.).</summary>
    public int TierLevel => _tierLevel;

    /// <summary>Current grid width, or 0 if we are not attached to a grid yet.</summary>
    public int GridWidth => _grid != null ? _grid.Width : 0;

    /// <summary>Current grid height, or 0 if we are not attached to a grid yet.</summary>
    public int GridHeight => _grid != null ? _grid.Height : 0;

    /// <summary>Attackable means front row + bottom tier.</summary>
    public bool IsInFrontAndBottomTier() => _row == 0 && _tierLevel == 0;
    public bool IsSpecialTarget => _isSpecial;

    /// <summary>Spawn/setup this block at a specific grid slot and world position.</summary>
    public void PlaceAt(BlockGrid grid, int column, int row, int tierLevel, Vector3 worldPosition, BlockColorData colorData)
    {
        _grid = grid;
        _column = column;
        _row = row;
        _tierLevel = tierLevel;
        _colorData = colorData;
        transform.position = worldPosition;
        transform.localScale = Vector3.one;

        // Reset/toggle special visuals every time the block is (re)placed from pool.
        _isSpecial = _colorData != null && _colorData.IsSpecialTarget;
        _specialHitPoints = _isSpecial ? SpecialBlockMaxHitPoints : 0;
        if (_specialTargetEffects != null)
            _specialTargetEffects.SetActive(_isSpecial);

        if (colorData != null && _renderer != null)
        {
            if (colorData.BlockMaterial != null)
            {
                _renderer.sharedMaterial = colorData.BlockMaterial;
            }

            if (_materialPropertyBlock == null)
                _materialPropertyBlock = new MaterialPropertyBlock();
            _materialPropertyBlock.Clear();
            colorData.ApplyBlockColorTo(_materialPropertyBlock);
            _renderer.SetPropertyBlock(_materialPropertyBlock);

            _baseRendererRotation = _renderer.transform.rotation;
        }
    }

    /// <summary>Update indices + world position after a move/slide.</summary>
    public void MoveTo(int column, int row, int tierLevel, Vector3 worldPosition)
    {
        _column = column;
        _row = row;
        _tierLevel = tierLevel;
        transform.position = worldPosition;
    }

    /// <summary>Send this block back to the pool.</summary>
    public void ReturnToPool()
    {
        ResetSpecialState();
        _grid?.ReturnBlock(this);
    }

    /// <summary>
    /// Apply one hit and tell the caller if this block should be destroyed now.
    /// Special targets need multiple hits.
    /// </summary>
    public bool ApplyHitAndShouldDestroy()
    {
        if (!_isSpecial) return true;

        _specialHitPoints = Mathf.Max(0, _specialHitPoints - 1);
        return _specialHitPoints <= 0;
    }

    public void ResetSpecialState()
    {
        _isSpecial = false;
        _specialHitPoints = 0;
        transform.localScale = Vector3.one;
        if (_specialTargetEffects != null)
            _specialTargetEffects.SetActive(false);
    }

    /// <summary>Shrink this block down, then return it to the pool.</summary>
    public async Awaitable PlayShrinkThenReturnAsync(BlockGrid grid, float duration)
    {
        if (this == null || grid == null) return;
        if (duration <= 0f)
        {
            grid.ReturnBlock(this);
            return;
        }
        Vector3 startScale = transform.localScale;
        Vector3 endScale = startScale * ShrinkTargetScale;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (this == null || grid == null) return;
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            transform.localScale = Vector3.Lerp(startScale, endScale, t);
            await Awaitable.NextFrameAsync();
        }
        if (this != null && grid != null)
            grid.ReturnBlock(this);
    }

    /// <summary>Smoothly move this block to a new world position.</summary>
    public async Awaitable MoveToAnimatedAsync(int column, int row, int tierLevel, Vector3 worldPosition, float duration)
    {
        if (this == null) return;
        _column = column;
        _row = row;
        _tierLevel = tierLevel;
        if (duration <= 0f)
        {
            transform.position = worldPosition;
            return;
        }
        _isMoving = true;
        try
        {
            Vector3 start = transform.position;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (this == null) return;
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                transform.position = Vector3.Lerp(start, worldPosition, t);
                await Awaitable.NextFrameAsync();
            }
            if (this != null)
                transform.position = worldPosition;
        }
        finally
        {
            if (this != null)
                _isMoving = false;
        }
    }

    /// <summary>
    /// Quick side-hit wobble: rotate out, then back. Purely visual.
    /// </summary>
    public async Awaitable PlaySideHitReactionAsync(Vector3 offset, float duration = 0.1f)
    {
        if (this == null) return;
        if (_renderer == null) return;
        if (duration <= 0f || offset == Vector3.zero) return;

        Transform target = _renderer.transform;
        Quaternion startRot = target.rotation;
        // Keep the peak relative to the original base rotation so repeated hits don't over-twist it.
        Quaternion peakRot = Quaternion.Euler(_baseRendererRotation.eulerAngles + offset);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (this == null) return;
            if (target == null) return;
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // Out-and-back curve over 0..1.
            float phase = t <= 0.5f ? t / 0.5f : (1f - t) / 0.5f;
            target.rotation = Quaternion.Slerp(startRot, peakRot, phase);

            await Awaitable.NextFrameAsync();
        }

        if (this != null && target != null)
            target.rotation = startRot;
    }

    /// <summary>Small permanent scale bump used for special-target hit feedback.</summary>
    public void ApplyHitScale(float delta)
    {
        if (this == null || Mathf.Approximately(delta, 0f)) return;
        transform.localScale += Vector3.one * delta;
    }
}
