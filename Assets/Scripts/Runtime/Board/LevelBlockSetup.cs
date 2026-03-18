using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Defines the block layout for one level. Grid is stored as a flat array: index = column + row * width.
/// Null color = empty cell. Rows 0–9 (Z 0–9) are the visible 10×10; rows 10+ (Z 10+) are "at the back".
/// Tier N = N cubes stacked at y=0, y=1, ..., y=N-1 (tier 1 = one cube at y=0).
/// Minimum 120 blocks (cubes) per level.
/// </summary>
[CreateAssetMenu(menuName = "Demo/Level Block Setup", fileName = "LevelBlockSetup")]
public class LevelBlockSetup : ScriptableObject
{
    /// <summary>Minimum number of blocks (non-empty cells) required per level.</summary>
    public const int MinBlocksPerLevel = 120;

    [Header("Level")]
    [SerializeField] private bool _isHardLevel = false;

    [Header("Grid size")]
    [Tooltip("Visible grid is 10×10 (rows 0–9). Rows 10+ are at the back (Z=10 onwards). Use height >= 12 for 120+ blocks.")]
    [SerializeField] private int _width = 10;
    [SerializeField] private int _height = 12;

    [Header("Cells (flat: index = column + row * width; null color = empty)")]
    [SerializeField] private LevelCell[] _cells;

    [Header("Shooter (per level)")]
    [Tooltip("Number of shooters to spawn for this level. Projectiles are divided by color among shooters.")]
    [SerializeField][Range(1, 30)] private int _shooterCount = 3;
    [Tooltip("Number of shooter platforms enabled for this level (1–5).")]
    [SerializeField][Range(1, 5)] private int _shooterPlatformActiveCount = 3;
    [Tooltip("Shooter container grid width (columns). Should match platform count.")]
    [SerializeField][Range(1, 5)] private int _shooterGridWidth = 5;
    [Tooltip("Shooter container grid depth (rows). Row 0 = platforms, row 1+ = tray.")]
    [SerializeField][Range(1, 10)] private int _shooterGridDepth = 2;
    [Tooltip("Optional explicit list of shooter color entries for this level. If empty, colors are inferred from blocks used in the grid.")]
    [SerializeField] private ShooterColorEntry[] _shooterColorEntries;

    public bool IsHardLevel => _isHardLevel;
    /// <summary>Grid width (columns).</summary>
    public int Width => _width;

    /// <summary>Grid height (rows). Row 0 = bottom line.</summary>
    public int Height => _height;

    /// <summary>Number of shooters to spawn for this level.</summary>
    public int ShooterCount => _shooterCount;

    /// <summary>Number of shooter platforms to enable for this level (1–5).</summary>
    public int ShooterPlatformActiveCount => _shooterPlatformActiveCount;

    /// <summary>Shooter container grid width (columns) for this level.</summary>
    public int ShooterGridWidth => _shooterGridWidth;

    /// <summary>Shooter container grid depth (rows) for this level.</summary>
    public int ShooterGridDepth => _shooterGridDepth;

    /// <summary>Explicit shooter colors for this level, or an empty list if none assigned (distinct set, order not guaranteed).</summary>
    public List<BlockColorData> ShooterColors
    {
        get
        {
            if (_shooterColorEntries == null || _shooterColorEntries.Length == 0)
                return new List<BlockColorData>();

            var list = new List<BlockColorData>(_shooterColorEntries.Length);
            for (int i = 0; i < _shooterColorEntries.Length; i++)
            {
                var color = _shooterColorEntries[i].Color;
                if (color != null && !list.Contains(color))
                    list.Add(color);
            }
            return list;
        }
    }

    /// <summary>Raw shooter color entries (one per shooter) including hidden/linked flags.</summary>
    public IReadOnlyList<ShooterColorEntry> ShooterColorEntries => _shooterColorEntries ?? System.Array.Empty<ShooterColorEntry>();

    /// <summary>
    /// Shooter colors sequence (one entry per shooter) in authoring order.
    /// Length is equal to ShooterCount (enforced in OnValidate).
    /// May contain duplicates and nulls.
    /// </summary>
    public IReadOnlyList<BlockColorData> ShooterColorSequence
    {
        get
        {
            if (_shooterColorEntries == null || _shooterColorEntries.Length == 0)
                return System.Array.Empty<BlockColorData>();

            var sequence = new List<BlockColorData>(_shooterColorEntries.Length);
            for (int i = 0; i < _shooterColorEntries.Length; i++)
            {
                sequence.Add(_shooterColorEntries[i].Color);
            }

            return sequence;
        }
    }

    /// <summary>Color at (column, row), or null if empty or out of bounds.</summary>
    public BlockColorData GetColorAt(int column, int row)
    {
        if (column < 0 || column >= _width || row < 0 || row >= _height)
            return null;
        int index = column + row * _width;
        if (_cells == null || index >= _cells.Length)
            return null;
        return _cells[index].Color;
    }

    /// <summary>Tier at (column, row): number of cubes stacked (1 = one at y=0, 2 = two at y=0 and y=1). Returns 1 if out of bounds or not set.</summary>
    public int GetTierAt(int column, int row)
    {
        if (column < 0 || column >= _width || row < 0 || row >= _height)
            return 1;
        int index = column + row * _width;
        if (_cells == null || index >= _cells.Length)
            return 1;
        return _cells[index].Tier;
    }

    /// <summary>Expected array length for current width/height.</summary>
    public int ExpectedLength => _width * _height;

    /// <summary>Total number of cubes (blocks) in this level. Grid size × tier per cell, summed over non-empty cells.</summary>
    public int GetBlockCount()
    {
        if (_cells == null) return 0;
        int count = 0;
        for (int r = 0; r < _height; r++)
        {
            for (int c = 0; c < _width; c++)
            {
                if (GetColorAt(c, r) != null)
                    count += GetTierAt(c, r);
            }
        }
        return count;
    }

    /// <summary>Number of blocks (cubes) of the given color in this level. Sum of tier for all cells with that color.</summary>
    public int GetBlockCountForColor(BlockColorData color)
    {
        if (color == null || _cells == null) return 0;
        int count = 0;
        for (int r = 0; r < _height; r++)
        {
            for (int c = 0; c < _width; c++)
            {
                if (GetColorAt(c, r) == color)
                    count += GetTierAt(c, r);
            }
        }
        return count;
    }

    /// <summary>Distinct colors used in this level (cells with non-null color). Order is not guaranteed.</summary>
    public List<BlockColorData> GetColorsUsed()
    {
        var set = new HashSet<BlockColorData>();
        if (_cells == null) return new List<BlockColorData>();
        for (int r = 0; r < _height; r++)
        {
            for (int c = 0; c < _width; c++)
            {
                BlockColorData color = GetColorAt(c, r);
                if (color != null) set.Add(color);
            }
        }
        return new List<BlockColorData>(set);
    }

    /// <summary>Colors to use for shooters in this level. Uses explicit ShooterColors when set; otherwise infers from block colors used in the grid.</summary>
    public List<BlockColorData> GetShooterColorsOrInferred()
    {
        var explicitColors = ShooterColors;
        if (explicitColors != null && explicitColors.Count > 0)
            return explicitColors;

        return GetColorsUsed();
    }

    /// <summary>True if _cells has the correct length for current width and height.</summary>
    public bool IsValid => _cells != null && _cells.Length == ExpectedLength;

    /// <summary>True if block count meets the minimum (120).</summary>
    public bool MeetsMinBlockCount => GetBlockCount() >= MinBlocksPerLevel;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_width < 1) _width = 1;
        if (_height < 1) _height = 1;
        _shooterCount = Mathf.Clamp(_shooterCount, 1, 30);
        _shooterPlatformActiveCount = Mathf.Clamp(_shooterPlatformActiveCount, 1, 5);
        _shooterGridWidth = Mathf.Clamp(_shooterGridWidth, 1, 5);
        _shooterGridDepth = Mathf.Clamp(_shooterGridDepth, 1, 10);
        // Ensure shooter color entries array matches shooter count (one entry per shooter).
        if (_shooterColorEntries == null || _shooterColorEntries.Length != _shooterCount)
        {
            var next = new ShooterColorEntry[_shooterCount];
            int copy = _shooterColorEntries != null ? Mathf.Min(_shooterColorEntries.Length, _shooterCount) : 0;
            for (int i = 0; i < copy; i++)
                next[i] = _shooterColorEntries[i];
            _shooterColorEntries = next;
        }
        // Validate that no shooter entry has a null Color; configuration must be explicit now.
        if (_shooterColorEntries != null)
        {
            for (int i = 0; i < _shooterColorEntries.Length; i++)
            {
                if (_shooterColorEntries[i].Color == null)
                {
                    Debug.LogError($"{name}: Shooter color entry at index {i} has a null Color. Assign a BlockColorData or remove the entry.", this);
                }
            }
        }
        int expected = _width * _height;
        if (_cells == null || _cells.Length != expected)
            ResizeCellsArray(expected);
        if (_cells != null && GetBlockCount() < MinBlocksPerLevel)
            Debug.LogWarning($"{name}: block count {GetBlockCount()} is below minimum {MinBlocksPerLevel}. Add blocks in rows 10+ (Z=10 onwards) or increase tiers.", this);
    }

    private void ResizeCellsArray(int newLength)
    {
        if (_cells == null)
        {
            _cells = new LevelCell[newLength];
            for (int i = 0; i < newLength; i++)
                _cells[i] = LevelCell.Empty;
            return;
        }
        var next = new LevelCell[newLength];
        int copy = Mathf.Min(_cells.Length, newLength);
        for (int i = 0; i < copy; i++)
            next[i] = _cells[i];
        for (int i = copy; i < newLength; i++)
            next[i] = LevelCell.Empty;
        _cells = next;
    }
#endif
}
