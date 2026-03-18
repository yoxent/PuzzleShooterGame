using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime board grid on the XZ plane.
/// Handles spawn, destroy, and slide logic (first Y compact inside a cell, then Z compact by column).
/// </summary>
public class BlockGrid : MonoBehaviour
{
    [Header("Grid")]
    [Tooltip("Set height >= 12 for levels with 120+ blocks so rows 10+ (Z=10 onwards) are in bounds.")]
    [SerializeField] private int _width = 10;
    [SerializeField] private int _height = 12;
    [Tooltip("Origin added to computed position (e.g. grid transform).")]
    [SerializeField] private Vector3 _originOffset = Vector3.zero;

    [Header("Animation")]
    [Tooltip("Duration to shrink blocks to 0.3 scale before releasing to pool.")]
    [SerializeField] private float _shrinkDuration = 0.15f;
    [Tooltip("Duration to lerp blocks to new position when sliding.")]
    [SerializeField] private float _moveDuration = 0.15f;

    private BlockPool _blockPool;
    private GameEventBus _eventBus;
    private LevelManager _levelManager;
    private readonly List<Block> _activeBlocks = new();
    private readonly HashSet<Block> _targetedByProjectile = new();
    private readonly Dictionary<BlockColorData, int> _sharedRoundRobinIndexByColor = new();
    private int _boardResolutionsInProgress;
    private Transform _transform;

    /// <summary>Grid width (columns).</summary>
    public int Width => _width;

    /// <summary>Grid height (rows). Row 0 = bottom line (Z=0).</summary>
    public int Height => _height;

    private void Awake()
    {
        _transform = transform;
        ServiceLocator.Register(this);

        _levelManager = ServiceLocator.Resolve<LevelManager>();

        if (_levelManager != null)
            _levelManager.LevelLoaded += OnLevelLoaded;

        _blockPool = ServiceLocator.Resolve<BlockPool>();
        _eventBus = ServiceLocator.Resolve<GameEventBus>();
    }

    //private void Start()
    //{
    //    if (_levelManager == null) _levelManager = ServiceLocator.Resolve<LevelManager>();
    //    if (_levelManager != null)
    //        _levelManager.LevelLoaded += OnLevelLoaded;

    //    _blockPool = ServiceLocator.Resolve<BlockPool>();
    //    _eventBus = ServiceLocator.Resolve<IGameEventBus>();
    //}

    private void OnDisable()
    {
        if (_levelManager != null)
            _levelManager.LevelLoaded -= OnLevelLoaded;
    }

    private void OnDestroy()
    {
        if (_levelManager != null)
            _levelManager.LevelLoaded -= OnLevelLoaded;
        ServiceLocator.Unregister<BlockGrid>();
    }

    private void OnLevelLoaded(LevelBlockSetup level)
    {
        if (level != null)
            LoadLevel(level);
    }

    /// <summary>World position for cell (column, row). Origin = transform.position + _originOffset. Y=0 for base.</summary>
    public Vector3 GetWorldPosition(int column, int row)
    {
        return GetWorldPosition(column, row, 0);
    }

    /// <summary>World position for cell (column, row) and tier level. Tier level 0 = y=0, 1 = y=1, etc.</summary>
    public Vector3 GetWorldPosition(int column, int row, int tierLevel)
    {
        float x = -(_width - 1) / 2f + column;
        float z = row;
        float y = tierLevel;
        return _transform.position + _originOffset + new Vector3(x, y, z);
    }

    /// <summary>True if (column, row) is inside the grid.</summary>
    public bool IsInBounds(int column, int row)
    {
        return column >= 0 && column < _width && row >= 0 && row < _height;
    }

    /// <summary>Get attackable blocks for a color (front row, bottom tier, idle, and not reserved).</summary>
    public IReadOnlyList<Block> GetAttackableBlocksMatching(BlockColorData color)
    {
        var result = new List<Block>();
        if (color == null) return result;

        for (int i = 0; i < _activeBlocks.Count; i++)
        {
            Block block = _activeBlocks[i];
            if (block == null) continue;
            if (!block.IsInFrontAndBottomTier()) continue;
            if (block.IsMoving) continue;
            if (_targetedByProjectile.Contains(block)) continue;
            if (block.ColorData != color) continue;
            result.Add(block);
        }

        return result;
    }

    /// <summary>
    /// Returns a front-row special target if one exists and is currently attackable.
    /// Special targets are globally prioritized by shooters.
    /// </summary>
    public Block GetPrioritySpecialTarget()
    {
        for (int i = 0; i < _activeBlocks.Count; i++)
        {
            Block b = _activeBlocks[i];
            if (b == null || !b.IsInFrontAndBottomTier() || b.IsMoving) continue;
            if (b.IsSpecialTarget) return b;
        }

        return null;
    }

    /// <summary>Distinct colors that are currently attackable on the front row.</summary>
    public IReadOnlyList<BlockColorData> GetAttackableBlockColors()
    {
        var result = new List<BlockColorData>();
        var seen = new HashSet<BlockColorData>();
        for (int i = 0; i < _activeBlocks.Count; i++)
        {
            Block b = _activeBlocks[i];
            if (b == null || !b.IsInFrontAndBottomTier() || b.IsMoving) continue;
            BlockColorData color = b.ColorData;
            if (color == null) continue;
            if (seen.Add(color))
                result.Add(color);
        }
        return result;
    }

    /// <summary>Reserve a block so only one projectile can target it. Returns true if reserved, false if already reserved by another projectile.</summary>
    public bool TryReserveTarget(Block block)
    {
        if (block == null) return false;
        return _targetedByProjectile.Add(block);
    }

    /// <summary>Release a block from reservation so it can be targeted again. Call when a projectile hits or is stopped without hitting.</summary>
    public void UnreserveTarget(Block block)
    {
        if (block == null) return;
        _targetedByProjectile.Remove(block);
    }

    /// <summary>True if any block is currently reserved by a projectile in flight. GameSession uses this to avoid declaring failure while hits are pending.</summary>
    public bool HasProjectilesInFlight => _targetedByProjectile.Count > 0;

    /// <summary>True when at least one block is mid-slide animation.</summary>
    public bool HasBlocksMoving
    {
        get
        {
            for (int i = 0; i < _activeBlocks.Count; i++)
            {
                Block block = _activeBlocks[i];
                if (block != null && block.IsMoving)
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// True while the board is resolving a destroy/slide cycle (including shrink, return-to-pool, and slide).
    /// This covers short windows where no projectile is in flight yet the board is still not in a stable state.
    /// </summary>
    public bool IsResolvingBoardState => _boardResolutionsInProgress > 0;

    /// <summary>Return the current shared round-robin index for this color and advance it. Used by shooters with SharedRoundRobin targeting so same-color shooters share one sequence. Resets when the level is cleared.</summary>
    public int GetAndAdvanceSharedRoundRobinIndex(BlockColorData color, int targetCount)
    {
        if (color == null || targetCount <= 0) return 0;
        if (!_sharedRoundRobinIndexByColor.TryGetValue(color, out int index))
            index = 0;
        int result = index % targetCount;
        _sharedRoundRobinIndexByColor[color] = index + 1;
        return result;
    }

    /// <summary>Spawn a block at the given cell and tier level (0 = bottom, y=0). Returns null if out of bounds or pool not ready.</summary>
    public Block SpawnBlock(int column, int row, int tierLevel, BlockColorData colorData)
    {
        if (!IsInBounds(column, row))
            return null;

        if (_blockPool == null)
        {
            Debug.LogError("BlockGrid.SpawnBlock: BlockPool not found. Add a BlockPool component to the scene and ensure it has a Block prefab assigned.");
            return null;
        }

        Block block = _blockPool.Get();
        if (block == null) return null;

        Vector3 worldPos = GetWorldPosition(column, row, tierLevel);
        block.PlaceAt(this, column, row, tierLevel, worldPos, colorData);
        _activeBlocks.Add(block);
        return block;
    }

    /// <summary>Return a block to the pool. Called by Block.ReturnToPool().</summary>
    public void ReturnBlock(Block block)
    {
        if (block == null) return;
        _activeBlocks.Remove(block);
        _blockPool?.Release(block);
    }

    /// <summary>Return all active blocks to the pool.</summary>
    public void Clear()
    {
        _targetedByProjectile.Clear();
        _sharedRoundRobinIndexByColor.Clear();
        for (int i = _activeBlocks.Count - 1; i >= 0; i--)
            ReturnBlock(_activeBlocks[i]);
    }

    /// <summary>Clear the grid and spawn blocks from a level setup. Each cell spawns tier-many cubes at y=0, y=1, ..., y=tier-1. Grid size is set from the level.</summary>
    public void LoadLevel(LevelBlockSetup level)
    {
        if (level == null) return;

        if (_blockPool == null)
        {
            Debug.LogError("BlockGrid.LoadLevel: BlockPool not found. Add a BlockPool component to the scene and ensure it has a Block prefab assigned.");
            return;
        }

        _width = level.Width;
        _height = level.Height;
        Clear();
        int spawned = 0;
        for (int r = 0; r < level.Height; r++)
        {
            for (int c = 0; c < level.Width; c++)
            {
                if (!IsInBounds(c, r)) continue;
                BlockColorData colorData = level.GetColorAt(c, r);
                if (colorData == null) continue;
                int tier = level.GetTierAt(c, r);
                for (int t = 0; t < tier; t++)
                {
                    if (SpawnBlock(c, r, t, colorData) != null)
                        spawned++;
                }
            }
        }
        if (level.GetBlockCount() > 0 && spawned == 0)
            Debug.LogError("BlockGrid.LoadLevel: Level has blocks but none spawned. Check that BlockPool has a Block prefab assigned.");
    }

    /// <summary>Number of blocks (cubes) currently on the grid. Used by GameSession for win/lose evaluation.</summary>
    public int ActiveBlockCount => _activeBlocks.Count;

    /// <summary>Destroy the given blocks (shrink then return to pool), then apply slide with position lerp. Y first, then Z. Bails if this grid is destroyed (e.g. exiting play mode).</summary>
    public async void DestroyBlocksAndSlide(IReadOnlyList<Block> toDestroy)
    {
        if (toDestroy == null || toDestroy.Count == 0) return;
        if (this == null) return;

        _boardResolutionsInProgress++;
        try
        {
            var distinct = new HashSet<Block>(toDestroy);

            if (_eventBus == null)
            {
                Debug.LogError("BlockGrid.DestroyBlocksAndSlide: EventBus not found. Add a GameEventBus component to the scene.");
                return;
            }

            var affectedCells = new HashSet<(int c, int r)>();
            var affectedColumns = new HashSet<int>();
            foreach (Block b in distinct)
            {
                if (b == null) continue;
                affectedCells.Add((b.Column, b.Row));
                affectedColumns.Add(b.Column);
            }

            var shrinkAwaitables = new List<Awaitable>();

            foreach (Block b in distinct)
            {
                if (b == null) continue;
                if (!_activeBlocks.Remove(b)) continue;
                _eventBus?.RaiseBlockDestroyed(b);
                shrinkAwaitables.Add(b.PlayShrinkThenReturnAsync(this, _shrinkDuration));
            }

            foreach (Awaitable a in shrinkAwaitables)
            {
                await a;
                if (this == null) return;
            }

            await ApplyYSlideAsync(affectedCells);
            if (this == null) return;
            await ApplyZSlideAsync(affectedColumns);
            if (this == null) return;

            if (_eventBus == null)
            {
                Debug.LogError("BlockGrid.DestroyBlocksAndSlide: EventBus not found. Add a GameEventBus component to the scene.");
                return;
            }

            _eventBus?.RaiseSlideCompleted();
        }
        finally
        {
            _boardResolutionsInProgress = Mathf.Max(0, _boardResolutionsInProgress - 1);
        }
    }

    /// <summary>Play a side-hit wobble on left/right neighbors in the same row.</summary>
    public void PlayNeighborHitReactions(Block center)
    {
        if (center == null) return;

        int row = center.Row;
        int leftCol = center.Column - 1;
        int rightCol = center.Column + 1;

        Block leftNeighbor = FindLowestTierBlock(leftCol, row);
        if (leftNeighbor != null)
        {
            float jitterX = Random.Range(-4f, 4f);
            float jitterZ = Random.Range(-4f, 4f);
            Vector3 offset = new Vector3(12f + jitterX, 0f, 12f + jitterZ);
            _ = leftNeighbor.PlaySideHitReactionAsync(offset);
        }

        Block rightNeighbor = FindLowestTierBlock(rightCol, row);
        if (rightNeighbor != null)
        {
            float jitterX = Random.Range(-4f, 4f);
            float jitterZ = Random.Range(-4f, 4f);
            Vector3 offset = new Vector3(12f + jitterX, 0f, -12f + jitterZ);
            _ = rightNeighbor.PlaySideHitReactionAsync(offset);
        }
    }

    /// <summary>Play center hit feedback + neighbor wobble.</summary>
    public void PlayHitReactions(Block center)
    {
        if (center == null) return;

        center.ApplyHitScale(0.02f);
        PlayNeighborHitReactions(center);
    }

    /// <summary>First pass: compact each affected cell by tier (Y).</summary>
    private async Awaitable ApplyYSlideAsync(HashSet<(int c, int r)> affectedCells)
    {
        if (this == null) return;
        var awaitables = new List<Awaitable>();
        foreach ((int c, int r) in affectedCells)
        {
            if (this == null) return;
            List<Block> inCell = CollectBlocksInCellSortedByTier(c, r);
            for (int i = 0; i < inCell.Count; i++)
            {
                Block b = inCell[i];
                int newTier = i;
                if (b.TierLevel == newTier) continue;
                Vector3 pos = GetWorldPosition(c, r, newTier);
                awaitables.Add(b.MoveToAnimatedAsync(c, r, newTier, pos, _moveDuration));
            }
        }
        foreach (Awaitable a in awaitables)
        {
            await a;
            if (this == null) return;
        }
    }

    /// <summary>Second pass: compact affected columns by row (Z), pulling back blocks toward the front.</summary>
    private async Awaitable ApplyZSlideAsync(HashSet<int> affectedColumns)
    {
        if (this == null) return;
        var awaitables = new List<Awaitable>();
        foreach (int c in affectedColumns)
        {
            if (this == null) return;
            var byRow = CollectBlocksByRowForColumn(c);
            var rows = new List<int>(byRow.Keys);
            rows.Sort();
            int newRow = 0;
            for (int rowIdx = 0; rowIdx < rows.Count; rowIdx++)
            {
                int sourceRow = rows[rowIdx];
                List<Block> group = byRow[sourceRow];
                int targetRow = newRow++;
                for (int i = 0; i < group.Count; i++)
                {
                    Block b = group[i];
                    if (b.Row == targetRow) continue;
                    Vector3 pos = GetWorldPosition(c, targetRow, b.TierLevel);
                    awaitables.Add(b.MoveToAnimatedAsync(c, targetRow, b.TierLevel, pos, _moveDuration));
                }
            }
        }
        foreach (Awaitable a in awaitables)
        {
            await a;
            if (this == null) return;
        }
    }

    private Block FindLowestTierBlock(int column, int row)
    {
        Block found = null;
        int bestTier = int.MaxValue;
        for (int i = 0; i < _activeBlocks.Count; i++)
        {
            Block candidate = _activeBlocks[i];
            if (candidate == null) continue;
            if (candidate.Column != column || candidate.Row != row) continue;
            if (candidate.TierLevel >= bestTier) continue;
            found = candidate;
            bestTier = candidate.TierLevel;
        }

        return found;
    }

    private List<Block> CollectBlocksInCellSortedByTier(int column, int row)
    {
        var inCell = new List<Block>();
        for (int i = 0; i < _activeBlocks.Count; i++)
        {
            Block block = _activeBlocks[i];
            if (block == null) continue;
            if (block.Column == column && block.Row == row)
                inCell.Add(block);
        }

        inCell.Sort((a, b) => a.TierLevel.CompareTo(b.TierLevel));
        return inCell;
    }

    private Dictionary<int, List<Block>> CollectBlocksByRowForColumn(int column)
    {
        var byRow = new Dictionary<int, List<Block>>();
        for (int i = 0; i < _activeBlocks.Count; i++)
        {
            Block block = _activeBlocks[i];
            if (block == null || block.Column != column) continue;

            if (!byRow.TryGetValue(block.Row, out List<Block> rowList))
            {
                rowList = new List<Block>();
                byRow[block.Row] = rowList;
            }

            rowList.Add(block);
        }

        return byRow;
    }
}
