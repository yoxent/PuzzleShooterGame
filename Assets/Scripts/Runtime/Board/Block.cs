using UnityEngine;

/// <summary>
/// A single block (cube) on the board. Has a color and grid position; can be part of a tier stack (tier level 0 = bottom, 1 = second cube, etc.). Y = tier level.
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

    /// <summary>True while the block is lerping to a new position. Not attackable during this time.</summary>
    public bool IsMoving => _isMoving;

    /// <summary>Color data for matching (same asset = same color).</summary>
    public BlockColorData ColorData => _colorData;

    /// <summary>Grid column (0 = leftmost).</summary>
    public int Column => _column;

    /// <summary>Grid row (0 = bottom line, Z=0).</summary>
    public int Row => _row;

    /// <summary>Tier level (0-based): 0 = bottom cube (y=0), 1 = second (y=1), etc.</summary>
    public int TierLevel => _tierLevel;

    /// <summary>Grid width from the current level (LevelBlockSetup). 0 if no grid.</summary>
    public int GridWidth => _grid != null ? _grid.Width : 0;

    /// <summary>Grid height from the current level (LevelBlockSetup). 0 if no grid.</summary>
    public int GridHeight => _grid != null ? _grid.Height : 0;

    /// <summary>True if this block is on the front row (Z=0) and bottom tier. Only such blocks are attackable by the shooter.</summary>
    public bool IsInFrontAndBottomTier() => _row == 0 && _tierLevel == 0;
    public bool IsSpecialTarget => _isSpecial;

    /// <summary>Place this block at a grid cell and world position. Tier level 0 = y=0, 1 = y=1, etc.</summary>
    public void PlaceAt(BlockGrid grid, int column, int row, int tierLevel, Vector3 worldPosition, BlockColorData colorData)
    {
        _grid = grid;
        _column = column;
        _row = row;
        _tierLevel = tierLevel;
        _colorData = colorData;
        transform.position = worldPosition;
        transform.localScale = Vector3.one;

        //special target
        _isSpecial = _colorData != null && _colorData.IsSpecialTarget;
        _specialHitPoints = _isSpecial ? SpecialBlockMaxHitPoints : 0;
        if (_isSpecial && _specialTargetEffects != null)
            _specialTargetEffects.SetActive(true);

        if (colorData != null && _renderer != null)
        {
            if (colorData.BlockMaterial != null)
            {
                _renderer.sharedMaterial = colorData.BlockMaterial;
            }

            MaterialPropertyBlock _propertyBlock = new MaterialPropertyBlock();
            colorData.ApplyBlockColorTo(_propertyBlock);
            _renderer.SetPropertyBlock(_propertyBlock);

            _baseRendererRotation = _renderer.transform.rotation;
        }
    }

    /// <summary>Update grid position, tier level, and world position (e.g. after slide).</summary>
    public void MoveTo(int column, int row, int tierLevel, Vector3 worldPosition)
    {
        _column = column;
        _row = row;
        _tierLevel = tierLevel;
        transform.position = worldPosition;
    }

    /// <summary>Return this block to the pool. Call from grid or when clearing.</summary>
    public void ReturnToPool()
    {
        ResetSpecialState();
        _grid?.ReturnBlock(this);
    }

    /// <summary>
    /// Applies one hit to this block and returns true when the block should be destroyed.
    /// Special targets need multiple hits before they can be destroyed.
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
        _specialTargetEffects.SetActive(false);
    }

    /// <summary>Lerp scale down to 0.3 then return to pool. Call from grid when destroying. Bails if this block or grid is destroyed (e.g. exiting play mode).</summary>
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

    /// <summary>Lerp position to world position over duration, then update grid state. Bails if this block is destroyed (e.g. exiting play mode).</summary>
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
    /// Side-hit reaction for neighboring blocks: rotate toward an offset, then back to the start.
    /// Does not change grid indices (column/row/tier) or world position.
    /// </summary>
    public async Awaitable PlaySideHitReactionAsync(Vector3 offset, float duration = 0.1f)
    {
        if (this == null) return;
        if (_renderer == null) return;
        if (duration <= 0f || offset == Vector3.zero) return;

        Transform target = _renderer.transform;
        Quaternion startRot = target.rotation;
        // Cap total rotation by keeping the peak relative to the original base rotation,
        // so repeated hits don't keep accumulating more and more twist.
        Quaternion peakRot = Quaternion.Euler(_baseRendererRotation.eulerAngles + offset);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (this == null) return;
            if (target == null) return;
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // Out-and-back over 0..1.
            float phase = t <= 0.5f ? t / 0.5f : (1f - t) / 0.5f;
            target.rotation = Quaternion.Slerp(startRot, peakRot, phase);

            await Awaitable.NextFrameAsync();
        }

        if (this != null && target != null)
            target.rotation = startRot;
    }

    /// <summary>
    /// Permanently increases this block's visual scale by the given delta on all axes.
    /// Used for special-target hit feedback.
    /// </summary>
    public void ApplyHitScale(float delta)
    {
        if (this == null || Mathf.Approximately(delta, 0f)) return;
        transform.localScale += Vector3.one * delta;
    }
}
