using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Authoring data for one level's block grid.
/// Cells are stored flat (`index = column + row * width`), with optional color + tier per cell.
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

    /// <summary>Color at a cell, or null if out of range/empty.</summary>
    public BlockColorData GetColorAt(int column, int row)
    {
        return TryGetCell(column, row, out LevelCell cell) ? cell.Color : null;
    }

    /// <summary>Tier at a cell (1 by default when missing/out of bounds).</summary>
    public int GetTierAt(int column, int row)
    {
        return TryGetCell(column, row, out LevelCell cell) ? cell.Tier : 1;
    }

    /// <summary>Expected array length for current width/height.</summary>
    public int ExpectedLength => _width * _height;

    /// <summary>Total number of cubes in this level (sum of tiers for all non-empty cells).</summary>
    public int GetBlockCount()
    {
        if (_cells == null) return 0;

        int count = 0;
        int limit = Mathf.Min(_cells.Length, ExpectedLength);
        for (int i = 0; i < limit; i++)
        {
            LevelCell cell = _cells[i];
            if (cell.Color == null) continue;
            count += cell.Tier;
        }

        return count;
    }

    /// <summary>Count cubes for one color (again summing by tier).</summary>
    public int GetBlockCountForColor(BlockColorData color)
    {
        if (color == null || _cells == null) return 0;

        int count = 0;
        int limit = Mathf.Min(_cells.Length, ExpectedLength);
        for (int i = 0; i < limit; i++)
        {
            LevelCell cell = _cells[i];
            if (cell.Color == color)
                count += cell.Tier;
        }

        return count;
    }

    /// <summary>Distinct colors used in non-empty cells.</summary>
    public List<BlockColorData> GetColorsUsed()
    {
        var set = new HashSet<BlockColorData>();
        if (_cells == null) return new List<BlockColorData>();

        int limit = Mathf.Min(_cells.Length, ExpectedLength);
        for (int i = 0; i < limit; i++)
        {
            BlockColorData color = _cells[i].Color;
            if (color != null)
                set.Add(color);
        }

        return new List<BlockColorData>(set);
    }

    /// <summary>Use explicit shooter colors if provided; otherwise infer them from blocks in the grid.</summary>
    public List<BlockColorData> GetShooterColorsOrInferred()
    {
        var explicitColors = ShooterColors;
        if (explicitColors != null && explicitColors.Count > 0)
            return explicitColors;

        return GetColorsUsed();
    }

    /// <summary>True if cell array length matches width * height.</summary>
    public bool IsValid => _cells != null && _cells.Length == ExpectedLength;

    /// <summary>True when this level meets the minimum block count.</summary>
    public bool MeetsMinBlockCount => GetBlockCount() >= MinBlocksPerLevel;

    private bool TryGetCell(int column, int row, out LevelCell cell)
    {
        cell = LevelCell.Empty;
        if (column < 0 || column >= _width || row < 0 || row >= _height)
            return false;
        if (_cells == null)
            return false;

        int index = column + row * _width;
        if (index < 0 || index >= _cells.Length)
            return false;

        cell = _cells[index];
        return true;
    }

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
