using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages a 2D grid of shooter slots. Top-left is first (row 0, col 0), bottom-right is last. Row 0 at Z=0, each row below at -spacing.
/// Only the bottom row (front) is viable for placement on ShooterPlatforms; only shooters on platforms can shoot.
/// Place by clicking (next free platform, left to right) or by dragging a shooter onto a platform.
/// </summary>
public class ShooterContainer : MonoBehaviour
{
    private LevelManager _levelManager;
    private GameEventBus _eventBus;
    private ShooterPool _shooterPool;

    [Header("References")]
    [SerializeField] private ShooterPlatforms _shooterPlatforms;
    [Tooltip("Parent for tray shooters when not on a platform. Assign in scene.")]
    [SerializeField] private Transform _trayRoot;
    [Tooltip("Prefab instantiated for each grid cell to draw the grid (X × Z). One instance per slot, parented to Grid Root.")]
    [SerializeField] private GameObject _gridSlotPrefab;
    [Tooltip("Parent for grid slot instances. If null, grid is parented to this transform.")]
    [SerializeField] private Transform _gridRoot;
    [SerializeField] private Transform[] _offscreenTransforms;
    [SerializeField] private float gridScale = 0.5f;

    [Header("Tray Layout")]
    [Tooltip("Distance in world units between tray rows along -Z (e.g. 1.5 gives first row Z=0, second Z=-1.5).")]
    [SerializeField] private float _trayRowOffsetZ = 1.5f;
    [Tooltip("Duration to lerp tray shooters to new positions when one is moved to a platform.")]
    [SerializeField] private float _trayLerpDuration = 0.2f;
    [Tooltip("Duration to lerp a shooter from tray/drop position to the platform center when placed.")]
    [SerializeField] private float _platformLerpDuration = 0.25f;
    [Tooltip("Cooldown after a merge during which no further merges are attempted (seconds).")]
    [SerializeField] private float _mergeSuspendDuration = 1f;

    private int _gridWidth = 5;
    private int _gridDepth = 2;
    private float _nextMergeAllowedTime;
    private int _mergesInProgress;
    private int _shootersLerpingToPlatformCount;
    private readonly Shooter[] _platformSlots = new Shooter[5];
    private readonly List<Shooter> _trayShooters = new List<Shooter>();
    private readonly List<int> _trayShooterSlots = new List<int>();
    private readonly List<GameObject> _gridSlotInstances = new List<GameObject>();
    private readonly List<ShooterLink> _shooterLinks = new List<ShooterLink>();
    private readonly Dictionary<Shooter, int> _offscreenDirectionByShooter = new Dictionary<Shooter, int>();
    private Transform TrayParent => _trayRoot != null ? _trayRoot : transform;

    /// <summary>True while any shooter is still lerping to its platform. GameSession uses this to defer LevelFailed until placement animations finish.</summary>
    public bool IsAnyShooterMovingToPlatform => _shootersLerpingToPlatformCount > 0;
    /// <summary>True while a triple merge animation/combine flow is actively running.</summary>
    public bool IsMergeInProgress => _mergesInProgress > 0;

    private void Awake()
    {
        ServiceLocator.Register(this);
    }

    private void Start()
    {
        _levelManager = ServiceLocator.Resolve<LevelManager>();
        _shooterPool = ServiceLocator.Resolve<ShooterPool>();

        if (_levelManager != null)
        {
            _levelManager.LevelLoaded += OnLevelLoaded;
        }

        OnSubscribeToEvents();
        BuildGrid();
    }

    private void OnDisable()
    {
        if (_levelManager != null)
        {
            _levelManager.LevelLoaded -= OnLevelLoaded;
        }

        OnUnsubscribeToEvents();
    }

    private void OnDestroy()
    {
        if (_levelManager != null)
        {
            _levelManager.LevelLoaded -= OnLevelLoaded;
        }

        OnUnsubscribeToEvents();
        ServiceLocator.Unregister<ShooterContainer>();
    }

    public void OnSubscribeToEvents()
    {
        _eventBus = ServiceLocator.Resolve<GameEventBus>();

        if (_eventBus == null) return;

        _eventBus.ShooterSelected += OnShooterPressed;
    }

    public void OnUnsubscribeToEvents()
    {
        if (_eventBus != null)
        {
            _eventBus.ShooterSelected -= OnShooterPressed;
        }
    }

    private void OnShooterPressed(Shooter shooter)
    {
        if (shooter == null) return;
        if (IsOnPlatform(shooter)) return;

        // If this shooter is part of a linked pair, try to move both together.
        if (TryPlaceLinkedPairOnPlatforms(shooter)) return;

        // Fallback: normal single-shooter placement.
        if (TryPlaceOnNextPlatform(shooter)) return;
    }

    private void OnLevelLoaded(LevelBlockSetup level)
    {
        if (level != null)
        {
            SetGridFromLevel(level.ShooterGridWidth, level.ShooterGridDepth);
            SpawnShootersForLevel(level);
        }
    }

    /// <summary>Return all shooters (on platforms and in tray) to the shooter pool. Call before spawning for a new level.</summary>
    public void ReturnAllShootersToPool()
    {
        if (_shooterPool == null) return;
        for (int i = 0; i < _platformSlots.Length; i++)
        {
            if (_platformSlots[i] != null)
            {
                var shooter = _platformSlots[i].GetComponent<Shooter>();
                if (shooter != null) _shooterPool.Release(shooter);
                _platformSlots[i] = null;
            }
        }
        for (int i = _trayShooters.Count - 1; i >= 0; i--)
        {
            Shooter shooter = _trayShooters[i];
            if (shooter == null) continue;
            _shooterPool.Release(shooter);
            _trayShooters.RemoveAt(i);
            if (i < _trayShooterSlots.Count)
                _trayShooterSlots.RemoveAt(i);
        }

        _offscreenDirectionByShooter.Clear();
    }

    /// <summary>Spawn shooters for the level from ShooterPool. Returns existing shooters to pool first. Shooters are assigned colors from the level; projectiles per shooter are divided by color (block count for that color / shooters with that color).</summary>
    public void SpawnShootersForLevel(LevelBlockSetup level)
    {
        if (level == null) return;
        if (_shooterPool == null || !_shooterPool.IsReady)
        {
            Debug.LogWarning("ShooterContainer: ShooterPool not found or not ready. Assign ShooterPool in the scene.");
            return;
        }

        ReturnAllShootersToPool();

        int shooterCount = Mathf.Max(1, level.ShooterCount);

        // If explicit shooter color entries are defined, spawn shooters in that exact order.
        var shooterColorSequence = level.ShooterColorSequence;
        var shooterEntries = level.ShooterColorEntries;
        if (shooterColorSequence != null && shooterColorSequence.Count > 0 && shooterEntries != null && shooterEntries.Count > 0)
        {
            // Explicit per-shooter colors; no more randomness from null slots.
            var resolvedColors = new List<BlockColorData>(shooterColorSequence.Count);
            for (int i = 0; i < shooterColorSequence.Count; i++)
            {
                BlockColorData explicitColor = shooterColorSequence[i];
                resolvedColors.Add(explicitColor);
            }

            // Precompute per-shooter projectile counts so that, per color, the sum equals the total block count for that color.
            int[] projectileCounts = new int[resolvedColors.Count];

            // Group shooters by color (non-null).
            var shootersByColor = new Dictionary<BlockColorData, List<int>>();
            for (int i = 0; i < resolvedColors.Count; i++)
            {
                BlockColorData color = resolvedColors[i];
                if (color == null) continue;

                if (!shootersByColor.TryGetValue(color, out var list))
                {
                    list = new List<int>();
                    shootersByColor[color] = list;
                }
                list.Add(i);
            }

            // For each color, divide its block count across its shooters (base + remainder) so totals match exactly.
            foreach (var kvp in shootersByColor)
            {
                BlockColorData color = kvp.Key;
                List<int> indices = kvp.Value;
                int totalBlocksForColor = level.GetBlockCountForColor(color);
                int shooterWithColorCount = Mathf.Max(1, indices.Count);

                int basePerShooter = totalBlocksForColor / shooterWithColorCount;
                int remainder = totalBlocksForColor % shooterWithColorCount;

                for (int j = 0; j < indices.Count; j++)
                {
                    int idx = indices[j];
                    int bullets = basePerShooter + (remainder > 0 ? 1 : 0);
                    if (remainder > 0) remainder--;
                    projectileCounts[idx] = Mathf.Max(1, bullets);
                }
            }

            // Shooters with null colors (no color or no grid colors) default to 1 projectile.
            for (int i = 0; i < resolvedColors.Count; i++)
            {
                if (projectileCounts[i] <= 0)
                    projectileCounts[i] = 1;
            }

            // Spawn shooters using the resolved colors and precomputed projectile counts.
            var spawnedShooters = new List<Shooter>(resolvedColors.Count);
            for (int i = 0; i < resolvedColors.Count; i++)
            {
                BlockColorData color = resolvedColors[i];
                int projectilesPerShooter = projectileCounts[i];

                ShooterColorEntry entry = i < shooterEntries.Count ? shooterEntries[i] : default;
                entry.Color = color;

                Shooter s = _shooterPool.Get(projectilesPerShooter);
                if (s != null)
                {
                    // Hidden state and other flags are driven by ShooterColorEntry.
                    s.SetShooterData(entry);
                    s.gameObject.name = $"Shooter_{i:00}";
                    AddToTray(s);
                }
                spawnedShooters.Add(s);
            }
            ConfigureShooterLinks(spawnedShooters, shooterEntries);
        }
        else
        {
            // Fallback: infer colors from blocks and distribute shooters round-robin.
            List<BlockColorData> colorsUsed = level.GetColorsUsed();
            if (colorsUsed == null || colorsUsed.Count == 0)
            {
                Debug.LogWarning("ShooterContainer: Level has no block colors; no shooters spawned.");
                return;
            }

            int colorCount = colorsUsed.Count;

            // Precompute per-shooter projectile counts for the round-robin case.
            int[] projectileCounts = new int[shooterCount];

            // shootersPerColor tells us how many shooters share each color.
            int[] shootersPerColor = new int[colorCount];
            for (int i = 0; i < shooterCount; i++)
                shootersPerColor[i % colorCount]++;

            // For each color, divide its block count across its shooters.
            for (int colorIndex = 0; colorIndex < colorCount; colorIndex++)
            {
                BlockColorData color = colorsUsed[colorIndex];
                int totalBlocksForColor = level.GetBlockCountForColor(color);
                int shooterWithColorCount = Mathf.Max(1, shootersPerColor[colorIndex]);

                int basePerShooter = totalBlocksForColor / shooterWithColorCount;
                int remainder = totalBlocksForColor % shooterWithColorCount;

                // Assign bullets to each shooter that uses this color (round-robin positions).
                for (int shooterIndex = 0, assigned = 0; shooterIndex < shooterCount && assigned < shooterWithColorCount; shooterIndex++)
                {
                    if (shooterIndex % colorCount != colorIndex) continue;

                    int bullets = basePerShooter + (remainder > 0 ? 1 : 0);
                    if (remainder > 0) remainder--;

                    projectileCounts[shooterIndex] = Mathf.Max(1, bullets);
                    assigned++;
                }
            }

            // Default any unassigned shooters to 1 projectile.
            for (int i = 0; i < projectileCounts.Length; i++)
            {
                if (projectileCounts[i] <= 0)
                    projectileCounts[i] = 1;
            }

            // Spawn shooters with precomputed projectile counts.
            for (int i = 0; i < shooterCount; i++)
            {
                BlockColorData color = colorsUsed[i % colorCount];
                int projectilesPerShooter = projectileCounts[i];

                Shooter s = _shooterPool.Get(projectilesPerShooter);
                if (s != null)
                {
                    ShooterColorEntry entry = new ShooterColorEntry
                    {
                        Color = color,
                        IsHidden = false,
                        IsLinked = false,
                        LinkedToIndex = -1
                    };
                    s.SetShooterData(entry);
                    s.gameObject.name = $"Shooter_{i:00}";
                    AddToTray(s);
                }
            }
        }
        ApplyTrayLayout();
    }

    private void ConfigureShooterLinks(IReadOnlyList<Shooter> shooters, IReadOnlyList<ShooterColorEntry> entries)
    {
        _shooterLinks.Clear();
        if (shooters == null || entries == null) return;

        int count = Mathf.Min(shooters.Count, entries.Count);

        // First, disable any existing link lines on these shooters.
        for (int i = 0; i < count; i++)
        {
            Shooter s = shooters[i];
            if (s != null && s.HasLinkLine)
            {
                s.DisableLinkLine();
            }
        }

        for (int i = 0; i < count; i++)
        {
            if (!entries[i].IsLinked)
                continue;

            int linkedIndex = entries[i].LinkedToIndex;
            if (linkedIndex < 0 || linkedIndex >= count)
                continue;

            int ownerIndex = Mathf.Min(i, linkedIndex);
            int targetIndex = Mathf.Max(i, linkedIndex);

            // Ensure we only configure the pair once (on the closest-to-zero index).
            if (i != ownerIndex)
                continue;

            Shooter owner = shooters[ownerIndex];
            Shooter linked = shooters[targetIndex];
            if (owner == null || linked == null)
                continue;

            owner.ConfigureLinkLine(owner.ColorData, linked.ColorData);
            _shooterLinks.Add(new ShooterLink { Owner = owner, Linked = linked });
        }
    }

    private void LateUpdate()
    {
        if (_shooterLinks.Count == 0) return;

        for (int i = 0; i < _shooterLinks.Count; i++)
        {
            var link = _shooterLinks[i];
            if (link.Owner == null || link.Linked == null || !link.Owner.HasLinkLine)
                continue;

            link.Owner.UpdateLinkLinePositions(link.Owner.transform.position, link.Linked.transform.position);
        }
    }

    /// <summary>Set grid size from level and rebuild the grid. Call when loading a level. Destroys existing grid slot instances.</summary>
    public void SetGridFromLevel(int width, int depth)
    {
        _gridWidth = Mathf.Clamp(width, 1, _platformSlots.Length);
        _gridDepth = Mathf.Clamp(depth, 1, 5);
        for (int i = 0; i < _gridSlotInstances.Count; i++)
        {
            if (_gridSlotInstances[i] != null)
                Destroy(_gridSlotInstances[i]);
        }
        _gridSlotInstances.Clear();
        BuildGrid();
    }

    /// <summary>Instantiate grid slot prefabs. Top-left is first (row 0, col 0), bottom-right is last. Row 0 at Z=0, each row below at -spacing.</summary>
    private void BuildGrid()
    {
        if (_gridSlotPrefab == null) return;
        Transform root = _gridRoot != null ? _gridRoot : transform;
        for (int row = 0; row < _gridDepth; row++)
        {
            for (int col = 0; col < _gridWidth; col++)
            {
                var go = Instantiate(_gridSlotPrefab, root);
                go.name = $"GridSlot_{col}_{row}";

                float px = ComputeTrayXPosition(col);
                float pz = -row * _trayRowOffsetZ;
                go.transform.localPosition = new Vector3(px, 0f, pz);
                go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                go.transform.localScale = new Vector3(gridScale, gridScale, 1f);
                _gridSlotInstances.Add(go);
            }
        }
    }

    /// <summary>Grid width (columns).</summary>
    public int GridWidth => _gridWidth;

    /// <summary>Grid depth (rows). Row 0 = top, bottom row = front (platforms).</summary>
    public int GridDepth => _gridDepth;

    /// <summary>True if the shooter is currently on a platform (front row Z=0).</summary>
    public bool IsOnPlatform(Shooter shooter)
    {
        return GetPlatformIndex(shooter) >= 0;
    }

    /// <summary>Platform index (0 = left) the shooter is on, or -1 if not on a platform.</summary>
    public int GetPlatformIndex(Shooter shooter)
    {
        if (shooter == null) return -1;
        int count = _shooterPlatforms != null ? _shooterPlatforms.ActiveCount : 0;
        for (int i = 0; i < count && i < _platformSlots.Length; i++)
        {
            if (_platformSlots[i] == shooter) return i;
        }
        return -1;
    }

    /// <summary>Shooter on the platform at index, or null.</summary>
    public Shooter GetShooterAtPlatform(int platformIndex)
    {
        if (platformIndex < 0 || platformIndex >= _platformSlots.Length) return null;
        return _platformSlots[platformIndex];
    }

    /// <summary>True if the platform at index has a shooter.</summary>
    public bool IsPlatformOccupied(int platformIndex)
    {
        return GetShooterAtPlatform(platformIndex) != null;
    }

    /// <summary>True if at least one platform slot (0..ActiveCount-1) has no shooter. GameSession uses this to avoid failing while the player can still deploy.</summary>
    public bool HasEmptyPlatformSlot()
    {
        if (_shooterPlatforms == null) return false;
        int count = _shooterPlatforms.ActiveCount;
        for (int i = 0; i < count && i < _platformSlots.Length; i++)
        {
            if (_platformSlots[i] == null) return true;
        }
        return false;
    }

    /// <summary>True if at least one shooter in the tray is in the front row and can be placed on a platform. GameSession uses this to avoid failing while the player can still deploy.</summary>
    public bool HasPlaceableShooterInTray()
    {
        for (int i = 0; i < _trayShooters.Count; i++)
        {
            Shooter shooter = _trayShooters[i];
            if (shooter != null && IsShooterAllowedOnPlatform(shooter)) return true;
        }
        return false;
    }

    /// <summary>True if there is an empty platform slot and at least one shooter in the tray that can be placed there. GameSession uses this to not fail while the game is still playable.</summary>
    public bool CanStillPlaceShootersFromTray()
    {
        int emptySlotCount = GetEmptyPlatformSlotCount();
        if (emptySlotCount <= 0) return false;

        for (int i = 0; i < _trayShooters.Count; i++)
        {
            Shooter shooter = _trayShooters[i];
            if (shooter == null) continue;
            if (!IsShooterAllowedOnPlatform(shooter)) continue;
            if (CanPlaceShooterWithCurrentPlatformSpace(shooter, emptySlotCount)) return true;
        }

        return false;
    }

    private bool CanPlaceShooterWithCurrentPlatformSpace(Shooter shooter, int emptySlotCount)
    {
        if (shooter == null) return false;
        if (emptySlotCount <= 0) return false;

        Shooter partner = GetLinkedPartner(shooter);
        bool isLinkedPair = partner != null && !IsOnPlatform(partner);
        if (!isLinkedPair)
            return true; // Single shooter can use one empty slot.

        // Linked pair needs two empty platform slots and the same eligibility
        // rules used by TryPlaceLinkedPairOnPlatforms(...).
        if (emptySlotCount < 2) return false;

        bool thisAllowed = IsShooterAllowedOnPlatform(shooter);   // row 0
        bool partnerAllowed = IsShooterAllowedOnPlatform(partner); // row 0
        bool bothFront = thisAllowed && partnerAllowed;

        bool willBecomeFront = false;
        if (thisAllowed && !partnerAllowed)
        {
            if (TryGetTrayRowCol(shooter, out int shooterRow, out int shooterCol) &&
                TryGetTrayRowCol(partner, out int partnerRow, out int partnerCol))
            {
                // Partner is directly behind in the same column and can move with the front-row shooter.
                willBecomeFront = shooterRow == 0 && partnerRow == 1 && shooterCol == partnerCol;
            }
        }

        return bothFront || willBecomeFront;
    }

    private bool TryGetTrayRowCol(Shooter shooter, out int row, out int col)
    {
        row = -1;
        col = -1;
        if (shooter == null) return false;

        int trayIndex = _trayShooters.IndexOf(shooter);
        if (trayIndex < 0) return false;

        int slot = trayIndex < _trayShooterSlots.Count ? _trayShooterSlots[trayIndex] : trayIndex;
        if (_gridWidth <= 0) return false;

        col = slot % _gridWidth;
        row = slot / _gridWidth;
        return true;
    }

    private int GetEmptyPlatformSlotCount()
    {
        int count = 0;
        int active = _shooterPlatforms != null ? _shooterPlatforms.ActiveCount : 0;
        for (int i = 0; i < active && i < _platformSlots.Length; i++)
        {
            if (_platformSlots[i] == null) count++;
        }
        return count;
    }

    private Shooter GetLinkedPartner(Shooter shooter)
    {
        if (shooter == null) return null;
        for (int i = 0; i < _shooterLinks.Count; i++)
        {
            var link = _shooterLinks[i];
            if (link.Owner == shooter) return link.Linked;
            if (link.Linked == shooter) return link.Owner;
        }
        return null;
    }

    private bool TryPlaceLinkedPairOnPlatforms(Shooter shooter)
    {
        Shooter partner = GetLinkedPartner(shooter);
        if (partner == null || IsOnPlatform(partner)) return false;

        // Eligibility rules:
        // - If both shooters are already in the front row, they can move together.
        // - If only one is in the front row, they can still move together when the other
        //   is directly behind in the same column (row 1 in that column), since it will
        //   slide into the front row when the first leaves.
        bool thisAllowed = IsShooterAllowedOnPlatform(shooter);   // row 0
        bool partnerAllowed = IsShooterAllowedOnPlatform(partner); // row 0

        bool bothFront = thisAllowed && partnerAllowed;

        bool willBecomeFront = false;
        if (thisAllowed && !partnerAllowed)
        {
            if (TryGetTrayRowCol(shooter, out int shooterRow, out int shooterCol) &&
                TryGetTrayRowCol(partner, out int partnerRow, out int partnerCol))
            {
                // Partner is "at the back of the front row": directly behind in same column.
                if (shooterRow == 0 && partnerRow == 1 && shooterCol == partnerCol)
                    willBecomeFront = true;
            }
        }

        if (!(bothFront || willBecomeFront))
        {
            // One half of the pair is not eligible yet; do not move either shooter.
            Debug.LogWarning("Linked shooters: both members of the pair must be in or immediately behind the front row before they can move to platforms.");
            return true; // handled: suppress normal placement.
        }

        // Need at least two empty platform slots to move the pair together.
        // If we don't have that, treat the click as handled so we DON'T fall back
        // to single-shooter placement and break the linked behavior.
        if (GetEmptyPlatformSlotCount() < 2)
            return true;

        // Place the clicked shooter first, then the partner.
        if (!TryPlaceOnNextPlatform(shooter))
            return false;

        // After tray reorder, partner may or may not still be front-row; if not, we leave it in tray.
        // This still respects the "must be eligible" rule at click time.
        TryPlaceOnNextPlatform(partner);
        return true;
    }

    public bool ShouldShooterStartOffscreen(Shooter shooter)
    {
        if (shooter == null) return true;

        Shooter partner = GetLinkedPartner(shooter);
        if (partner == null)
            return true;

        // Only allow this shooter to go offscreen when both linked shooters are out of ammo.
        return partner.ProjectileCount <= 0;
    }

    public void OnShooterReleased(Shooter shooter)
    {
        if (shooter == null) return;

        for (int i = _shooterLinks.Count - 1; i >= 0; i--)
        {
            var link = _shooterLinks[i];
            if (link.Owner == shooter || link.Linked == shooter)
            {
                if (link.Owner != null)
                    link.Owner.DeactivateLinkLineObject();
                if (link.Linked != null)
                    link.Linked.DeactivateLinkLineObject();

                _shooterLinks.RemoveAt(i);
            }
        }

        _offscreenDirectionByShooter.Remove(shooter);
    }

    /// <summary>
    /// For linked shooters, assign and remember a common offscreen direction (left/right)
    /// so both members of the pair leave the screen on the same side. Returns 0 for
    /// non-linked shooters or when no offscreen points are configured.
    /// </summary>
    public int GetLinkedOffscreenDirection(Shooter shooter, Vector3 startPosition, Transform[] offscreenPoints)
    {
        if (shooter == null || offscreenPoints == null || offscreenPoints.Length == 0)
            return 0;

        // If this shooter already has an assigned direction, reuse it.
        if (_offscreenDirectionByShooter.TryGetValue(shooter, out int existingDir))
            return existingDir;

        Shooter partner = GetLinkedPartner(shooter);
        if (partner == null)
            return 0;

        // If the partner already chose a direction, mirror that.
        if (_offscreenDirectionByShooter.TryGetValue(partner, out int partnerDir))
        {
            _offscreenDirectionByShooter[shooter] = partnerDir;
            return partnerDir;
        }

        // First time this pair is going offscreen: pick the closest offscreen point
        // and use its X sign as the pair's shared direction.
        float bestSqr = float.MaxValue;
        int chosenDir = 0;
        for (int i = 0; i < offscreenPoints.Length; i++)
        {
            Transform point = offscreenPoints[i];
            if (point == null) continue;

            float sqr = (point.position - startPosition).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                float signX = Mathf.Sign(point.position.x);
                if (Mathf.Approximately(signX, 0f))
                    signX = 1f;
                chosenDir = signX > 0 ? 1 : -1;
            }
        }

        if (chosenDir == 0)
            return 0;

        _offscreenDirectionByShooter[shooter] = chosenDir;
        _offscreenDirectionByShooter[partner] = chosenDir;
        return chosenDir;
    }

    /// <summary>Place shooter on the next available platform (left to right). Returns true if placed. Only shooters in the tray front row (row 0) can be placed.</summary>
    public bool TryPlaceOnNextPlatform(Shooter shooter)
    {
        if (shooter == null || _shooterPlatforms == null) return false;
        if (!IsShooterAllowedOnPlatform(shooter)) return false;
        int count = _shooterPlatforms.ActiveCount;
        for (int i = 0; i < count && i < _platformSlots.Length; i++)
        {
            if (_platformSlots[i] == null)
            {
                PlaceOnPlatform(shooter, i);
                return true;
            }
        }
        return false;
    }

    /// <summary>True if this shooter can be placed on a platform: must be in the tray front row (row 0). Shooters already on a platform cannot be moved to another platform.</summary>
    private bool IsShooterAllowedOnPlatform(Shooter shooter)
    {
        if (shooter == null) return false;
        if (IsOnPlatform(shooter)) return false;
        int trayIndex = _trayShooters.IndexOf(shooter);
        if (trayIndex < 0) return false;
        int slot = trayIndex < _trayShooterSlots.Count ? _trayShooterSlots[trayIndex] : trayIndex;
        int row = slot / _gridWidth;
        return row == 0;
    }

    /// <summary>Place shooter on the platform at index. Only tray front-row shooters can be placed; once on a platform, the shooter stays there until it runs out of projectiles (no moving between platforms).</summary>
    public bool PlaceOnPlatform(Shooter shooter, int platformIndex)
    {
        if (shooter == null || _shooterPlatforms == null) return false;
        if (platformIndex < 0 || platformIndex >= _platformSlots.Length) return false;
        if (platformIndex >= _shooterPlatforms.ActiveCount) return false;

        Transform shooterTransform = shooter.transform;
        if (shooterTransform == null) return false;
        if (!IsShooterAllowedOnPlatform(shooter)) return false;

        int trayIndex = _trayShooters.IndexOf(shooter);
        int vacatedSlot = (trayIndex >= 0 && trayIndex < _trayShooterSlots.Count) ? _trayShooterSlots[trayIndex] : trayIndex;
        RemoveFromPlatformOrTray(shooter);
        if (trayIndex >= 0)
        {
            int vacatedCol = vacatedSlot % _gridWidth;
            ReorderTrayAfterVacating(vacatedCol);
        }
        Transform platform = _shooterPlatforms.GetPlatform(platformIndex);
        if (platform == null) return false;

        Vector3 worldPosBefore = shooterTransform.position;
        _platformSlots[platformIndex] = shooter;
        shooterTransform.SetParent(platform);
        shooterTransform.localPosition = platform.InverseTransformPoint(worldPosBefore);
        shooterTransform.localRotation = Quaternion.identity;

        var shooterComponent = shooterTransform.GetComponent<Shooter>();
        if (shooterComponent != null)
        {
            // Platform placement should always use active/revealed visuals.
            // Linked partners can skip the usual row-0 tray visual pass and go straight here.
            if (shooterComponent.IsColorHidden)
            {
                var entry = shooterComponent.ColorEntry;
                entry.IsHidden = false;
                shooterComponent.SetShooterData(entry);
            }

            shooterComponent.ApplyActiveColor(shooterComponent.ColorData);
            shooterComponent.ToggleText(true);
            shooterComponent.ResetFireCooldown();
        }
        LerpTrayLayoutToTargets();

        if (_platformLerpDuration > 0f)
        {
            LerpShooterToPlatformCenterAsync(shooter, platform);
        }
        else
        {
            SnapShooterToPlatform(shooter);
            TryMergeSameColorTriples();
        }

        // Raise after movement/snap kickoff so fail checks see transient placement state
        // (e.g. IsAnyShooterMovingToPlatform) and wait for settle before evaluating loss.
        _eventBus?.RaiseShooterPlacedOnPlatform();

        return true;
    }

    /// <summary>
    /// If any three platforms have shooters with the same color, merge them into one:
    /// - Sort shooters of the same color by platform index and take the first three.
    /// - The shooter on the middle platform stays and gets combined ammo.
    /// - The two shooters on the sides are returned to the pool and removed from platforms.
    /// - All three suspend firing during the merge; the middle shooter resumes afterward.
    /// Only one merge is processed per call.
    /// </summary>
    private void TryMergeSameColorTriples()
    {
        if (_shooterPlatforms == null || _shooterPool == null || _levelManager.CurrentLevelIndex < 4) return;

        int activeCount = _shooterPlatforms.ActiveCount;
        if (activeCount < 3) return;

        // Global merge cooldown to leave room for future merge effects.
        if (Time.time < _nextMergeAllowedTime) return;

        // Group shooters on platforms by color.
        var shootersByColor = new Dictionary<BlockColorData, List<(int index, Shooter shooter)>>();

        for (int i = 0; i < activeCount && i < _platformSlots.Length; i++)
        {
            Shooter shooter = _platformSlots[i];
            if (shooter == null || shooter.ColorData == null) continue;

            // Linked shooters are excluded from triple-merge logic.
            if (shooter.ColorEntry.IsLinked)
                continue;

            if (!shootersByColor.TryGetValue(shooter.ColorData, out var list))
            {
                list = new List<(int index, Shooter shooter)>();
                shootersByColor[shooter.ColorData] = list;
            }

            list.Add((i, shooter));
        }

        foreach (var kvp in shootersByColor)
        {
            List<(int index, Shooter shooter)> list = kvp.Value;
            if (list.Count < 3) continue;

            // Sort by platform index and take the first three.
            list.Sort((a, b) => a.index.CompareTo(b.index));
            var a = list[0];
            var b = list[1];
            var c = list[2];

            // Safety: never merge any shooter that is part of a linked pair.
            if ((a.shooter != null && a.shooter.ColorEntry.IsLinked) ||
                (b.shooter != null && b.shooter.ColorEntry.IsLinked) ||
                (c.shooter != null && c.shooter.ColorEntry.IsLinked))
            {
                continue;
            }

            MergeTriple(a.index, a.shooter, b.index, b.shooter, c.index, c.shooter);
            return;
        }
    }

    private async void MergeTriple(int indexA, Shooter shooterA, int indexB, Shooter shooterB, int indexC, Shooter shooterC)
    {
        _mergesInProgress++;
        try
        {
            // Set global cooldown for future merges; this also serves as "merge in progress" duration.
            _nextMergeAllowedTime = Time.time + _mergeSuspendDuration;

            // Order the three shooters by their platform indices so we can identify left/middle/right.
            var entries = new List<(int index, Shooter shooter)>
            {
                (indexA, shooterA),
                (indexB, shooterB),
                (indexC, shooterC)
            };
            entries.Sort((x, y) => x.index.CompareTo(y.index));

            var leftEntry = entries[0];
            var middleEntry = entries[1];
            var rightEntry = entries[2];

            Shooter left = leftEntry.shooter;
            Shooter middle = middleEntry.shooter;
            Shooter right = rightEntry.shooter;

            // Stop firing first.
            left?.SuspendFiring();
            middle?.SuspendFiring();
            right?.SuspendFiring();

            // Play phases 1..3 (lift/fan-out/converge).
            await AnimateTripleMergeAsync(left, middle, right);

            if (this == null || _shooterPool == null || left == null || middle == null || right == null)
            {
                // If merge was interrupted after phase-4 visual hide, restore side shooters.
                if (left != null) left.gameObject.SetActive(true);
                if (right != null) right.gameObject.SetActive(true);
                left?.ResumeFiring();
                middle?.ResumeFiring();
                right?.ResumeFiring();
                return;
            }

            // Ensure all three shooters are still on platforms; if not (e.g. one was pooled), abort merge safely.
            int leftIndexNow = GetPlatformIndex(left);
            int middleIndexNow = GetPlatformIndex(middle);
            int rightIndexNow = GetPlatformIndex(right);
            if (leftIndexNow < 0 || middleIndexNow < 0 || rightIndexNow < 0)
            {
                // If merge was interrupted after phase-4 visual hide, restore side shooters.
                left.gameObject.SetActive(true);
                right.gameObject.SetActive(true);
                left?.ResumeFiring();
                middle?.ResumeFiring();
                right?.ResumeFiring();
                return;
            }

            // Combine remaining ammo.
            int combinedAmmo = left.ProjectileCount + middle.ProjectileCount + right.ProjectileCount;

            // Phase 3 is the "merge commit" moment: middle gets the combined ammo now.
            middle.SetProjectileCount(combinedAmmo);

            // Hide side shooters right after the merge commit so frontend reads as one merged shooter.
            left.gameObject.SetActive(false);
            right.gameObject.SetActive(false);

            // Return side shooters to the pool and clear their platform slots.
            if (leftIndexNow >= 0 && leftIndexNow < _platformSlots.Length)
            {
                _platformSlots[leftIndexNow] = null;
                try
                {
                    _shooterPool.Release(left);
                }
                catch (System.InvalidOperationException)
                {
                    // Already released; ignore.
                }
            }

            if (rightIndexNow >= 0 && rightIndexNow < _platformSlots.Length)
            {
                _platformSlots[rightIndexNow] = null;
                try
                {
                    _shooterPool.Release(right);
                }
                catch (System.InvalidOperationException)
                {
                    // Already released; ignore.
                }
            }

            // Final settle: merged shooter drops back to platform height.
            await AnimateMergedShooterSettleAsync(middle);

            // Resume firing on the merged shooter.
            middle.ResumeFiring();
        }
        finally
        {
            _mergesInProgress = Mathf.Max(0, _mergesInProgress - 1);
        }
    }

    /// <summary>
    /// True when current platform occupants contain a valid same-color triple
    /// (non-linked shooters only), so a merge can free slots and keep the run playable.
    /// </summary>
    public bool HasPendingMergeCandidate()
    {
        if (_shooterPlatforms == null || _levelManager == null || _levelManager.CurrentLevelIndex < 4)
            return false;

        int activeCount = _shooterPlatforms.ActiveCount;
        if (activeCount < 3) return false;

        var counts = new Dictionary<BlockColorData, int>();
        for (int i = 0; i < activeCount && i < _platformSlots.Length; i++)
        {
            Shooter shooter = _platformSlots[i];
            if (shooter == null || shooter.ColorData == null) continue;
            if (shooter.ColorEntry.IsLinked) continue;

            if (!counts.TryGetValue(shooter.ColorData, out int value))
                value = 0;

            value++;
            if (value >= 3) return true;
            counts[shooter.ColorData] = value;
        }

        return false;
    }

    /// <summary>
    /// Animate a triple-merge: all shooters lift on Y, then side shooters fan out on X,
    /// then both converge horizontally to the middle shooter's X.
    /// Each phase lasts 0.25s (total ~0.75s).
    /// </summary>
    private async Awaitable AnimateTripleMergeAsync(Shooter left, Shooter middle, Shooter right)
    {
        const float phaseDuration = 0.25f;
        if (left == null || middle == null || right == null)
            return;

        Transform lt = left.transform;
        Transform mt = middle.transform;
        Transform rt = right.transform;
        if (lt == null || mt == null || rt == null)
            return;

        // Cache starting positions (local to their platform parents).
        Vector3 leftStart = lt.localPosition;
        Vector3 middleStart = mt.localPosition;
        Vector3 rightStart = rt.localPosition;

        // Phase 1: lift all shooters to Y = 1.
        {
            Vector3 leftTarget = new Vector3(leftStart.x, 1f, leftStart.z);
            Vector3 middleTarget = new Vector3(middleStart.x, 1f, middleStart.z);
            Vector3 rightTarget = new Vector3(rightStart.x, 1f, rightStart.z);

            float elapsed = 0f;
            while (elapsed < phaseDuration)
            {
                if (this == null || lt == null || mt == null || rt == null)
                    return;

                elapsed += Time.deltaTime;
                float t = Helper.SmoothStep01(elapsed / phaseDuration);

                lt.localPosition = Vector3.Lerp(leftStart, leftTarget, t);
                mt.localPosition = Vector3.Lerp(middleStart, middleTarget, t);
                rt.localPosition = Vector3.Lerp(rightStart, rightTarget, t);

                await Awaitable.NextFrameAsync();
            }

            leftStart = leftTarget;
            middleStart = middleTarget;
            rightStart = rightTarget;

            _eventBus?.RaiseShootersMergeLift();
        }

        // Phase 2: fan out side shooters on X (left -= 0.15, right += 0.15).
        {
            Vector3 leftTarget = new Vector3(leftStart.x - 0.15f, leftStart.y, leftStart.z);
            Vector3 middleTarget = middleStart; // stays centered
            Vector3 rightTarget = new Vector3(rightStart.x + 0.15f, rightStart.y, rightStart.z);

            float elapsed = 0f;
            while (elapsed < phaseDuration)
            {
                if (this == null || lt == null || mt == null || rt == null)
                    return;

                elapsed += Time.deltaTime;
                float t = Helper.SmoothStep01(elapsed / phaseDuration);

                lt.localPosition = Vector3.Lerp(leftStart, leftTarget, t);
                mt.localPosition = Vector3.Lerp(middleStart, middleTarget, t);
                rt.localPosition = Vector3.Lerp(rightStart, rightTarget, t);

                await Awaitable.NextFrameAsync();
            }

            leftStart = leftTarget;
            middleStart = middleTarget;
            rightStart = rightTarget;
        }

        // Phase 3: converge side shooters' X toward the middle shooter's X (aligning in world space).
        {
            // Use world positions here because each shooter is parented under its own platform.
            Vector3 leftWorldStart = lt.position;
            Vector3 rightWorldStart = rt.position;
            float middleWorldX = mt.position.x;
            Vector3 leftWorldTarget = new Vector3(middleWorldX, leftWorldStart.y, leftWorldStart.z);
            Vector3 rightWorldTarget = new Vector3(middleWorldX, rightWorldStart.y, rightWorldStart.z);

            float elapsed = 0f;
            while (elapsed < phaseDuration)
            {
                if (this == null || lt == null || mt == null || rt == null)
                    return;

                elapsed += Time.deltaTime;
                float t = Helper.SmoothStep01(elapsed / phaseDuration);

                lt.position = Vector3.Lerp(leftWorldStart, leftWorldTarget, t);
                rt.position = Vector3.Lerp(rightWorldStart, rightWorldTarget, t);
                // Middle stays where it is in this phase.

                await Awaitable.NextFrameAsync();
            }

            _eventBus?.RaiseShootersMergeConverged();
        }

    }

    private async Awaitable AnimateMergedShooterSettleAsync(Shooter middle)
    {
        const float phaseDuration = 0.25f;
        if (middle == null) return;

        Transform mt = middle.transform;
        if (mt == null) return;

        Vector3 middleLocalStart = mt.localPosition;
        Vector3 middleLocalTarget = new Vector3(middleLocalStart.x, 0f, middleLocalStart.z);

        float elapsed = 0f;
        while (elapsed < phaseDuration)
        {
            if (this == null || mt == null)
                return;

            elapsed += Time.deltaTime;
            float t = Helper.SmoothStep01(elapsed / phaseDuration);
            mt.localPosition = Vector3.Lerp(middleLocalStart, middleLocalTarget, t);

            await Awaitable.NextFrameAsync();
        }

        if (mt != null)
            mt.localPosition = middleLocalTarget;

        _eventBus?.RaiseShooterMergedSettled();
    }

    private void SnapShooterToPlatform(Shooter shooter)
    {
        if (shooter == null) return;
        shooter.transform.localPosition = Vector3.zero;
        shooter.transform.localRotation = Quaternion.identity;
    }

    private async void LerpShooterToPlatformCenterAsync(Shooter shooter, Transform platform)
    {
        if (shooter == null || platform == null) return;

        shooter.IsMovingToPlatform = true;
        _shootersLerpingToPlatformCount++;

        try
        {
            Vector3 startLocal = shooter.transform.localPosition;
            Vector3 targetLocal = Vector3.zero;
            float duration = _platformLerpDuration;

            await Helper.LerpLocalPositionSmoothStepAsync(
                shooter.transform,
                startLocal,
                targetLocal,
                duration,
                () => this == null || shooter == null);

            if (shooter != null)
            {
                shooter.IsMovingToPlatform = false;
                shooter.transform.localPosition = targetLocal;
                shooter.transform.localRotation = Quaternion.identity;

                TryMergeSameColorTriples();
            }
        }
        finally
        {
            _shootersLerpingToPlatformCount--;
            _eventBus?.RaiseShooterDeployed();
        }
    }

    /// <summary>Remove shooter from its platform and return it to the tray (parented to _trayRoot or this transform).</summary>
    public void RemoveFromPlatform(Shooter shooter)
    {
        if (shooter == null) return;

        Transform shooterTransform = shooter.transform;
        if (shooterTransform == null) return;
        int idx = GetPlatformIndex(shooter);
        if (idx < 0) return;
        _platformSlots[idx] = null;
        _trayShooters.Add(shooter);
        _trayShooterSlots.Add(_trayShooters.Count - 1);
        if (_trayRoot != null)
        {
            shooterTransform.SetParent(_trayRoot);
            shooterTransform.localPosition = Vector3.zero;
            shooterTransform.localRotation = Quaternion.identity;
        }
        else
        {
            shooterTransform.SetParent(transform);
            shooterTransform.localPosition = Vector3.zero;
            shooterTransform.localRotation = Quaternion.identity;
        }
        ApplyTrayLayout();
    }

    /// <summary>
    /// Frees only the platform slot for this shooter (if any) without moving/reparenting the shooter.
    /// Used when a shooter starts going offscreen so a new shooter can be deployed immediately.
    /// </summary>
    public void VacatePlatformSlot(Shooter shooter)
    {
        if (shooter == null || _shooterPlatforms == null) return;
        int platformIndex = GetPlatformIndex(shooter);
        if (platformIndex >= 0)
        {
            _platformSlots[platformIndex] = null;
        }
    }

    /// <summary>Remove shooter from the tray lists only (e.g. before releasing to pool). Does not reparent.</summary>
    public void RemoveFromTray(Shooter shooter)
    {
        if (shooter == null) return;
        int trayIdx = _trayShooters.IndexOf(shooter);
        if (trayIdx >= 0)
        {
            _trayShooters.RemoveAt(trayIdx);
            if (trayIdx < _trayShooterSlots.Count)
                _trayShooterSlots.RemoveAt(trayIdx);
        }
    }

    private void RemoveFromPlatformOrTray(Shooter shooter)
    {
        int idx = GetPlatformIndex(shooter);
        if (idx >= 0)
            _platformSlots[idx] = null;
        int trayIdx = _trayShooters.IndexOf(shooter);
        if (trayIdx >= 0)
        {
            _trayShooters.RemoveAt(trayIdx);
            if (trayIdx < _trayShooterSlots.Count)
                _trayShooterSlots.RemoveAt(trayIdx);
        }
    }

    /// <summary>Register a shooter as being in the tray (e.g. after spawn). Parents it to _trayRoot or this transform and applies layout.</summary>
    public void AddToTray(Shooter shooter)
    {
        if (shooter == null) return;

        Transform shooterTransform = shooter.transform;
        if (shooterTransform == null) return;
        if (IsOnPlatform(shooter)) return;
        if (_trayShooters.Contains(shooter)) return;
        _trayShooters.Add(shooter);
        int slotIndex = _trayShooters.Count - 1;
        _trayShooterSlots.Add(slotIndex);
        UpdateShooterTrayState(shooter, slotIndex);
        shooterTransform.SetParent(TrayParent);
        ApplyTrayLayout();
    }

    /// <summary>Position tray shooters by their assigned slot index. Uses _trayRoot or this transform as parent.</summary>
    public void ApplyTrayLayout()
    {
        Transform parent = TrayParent;
        for (int i = 0; i < _trayShooters.Count; i++)
        {
            Shooter shooter = _trayShooters[i];
            if (shooter == null) continue;
            Transform s = shooter.transform;
            if (s == null) continue;
            int slot = i < _trayShooterSlots.Count ? _trayShooterSlots[i] : i;
            UpdateShooterTrayState(shooter, slot);
            Vector3 target = GetTraySlotLocalPosition(slot);
            s.SetParent(parent);
            s.localPosition = target;
            s.localRotation = Quaternion.identity;
        }
    }

    /// <summary>World position of a tray grid cell given its column and row.</summary>
    public Vector3 GetTraySlotWorldPosition(int col, int row)
    {
        int slotIndex = col + row * _gridWidth;
        Vector3 local = GetTraySlotLocalPosition(slotIndex);
        return TrayParent.TransformPoint(local);
    }

    /// <summary>Target local position for tray slot index (same parent as layout).</summary>
    private Vector3 GetTraySlotLocalPosition(int slotIndex)
    {
        int col = slotIndex % _gridWidth;
        int row = slotIndex / _gridWidth;

        float px = ComputeTrayXPosition(col);
        float pz = -row * _trayRowOffsetZ;
        return new Vector3(px, 0f, pz);
    }

    private float ComputeTrayXPosition(int col)
    {
        const float step = 2f;
        bool useCenteredSpacing = _gridWidth == 4 || (_levelManager != null && _levelManager.CurrentLevelIndex == 16);
        if (useCenteredSpacing)
        {
            // With width=4 and step=2 this yields exactly: -3, -1, 1, 3.
            return (col - (_gridWidth - 1) * 0.5f) * step;
        }

        return Helper.ComputeSymmetricStepX(col, _gridWidth, step);
    }

    /// <summary>
    /// Update a shooter's tray visuals (color + text) based on its current slot index.
    /// Hidden-color shooters stay neutral in back rows and are revealed when they reach the front row.
    /// </summary>
    private void UpdateShooterTrayState(Shooter shooter, int slotIndex)
    {
        if (shooter == null) return;

        int row = _gridWidth > 0 ? slotIndex / _gridWidth : 0;
        if (row == 0)
        {
            // Front row: shooter is eligible for platform placement.
            if (shooter.IsColorHidden)
            {
                // Reveal color and text once when reaching the front row, using the active visual.
                var entry = shooter.ColorEntry;
                entry.IsHidden = false;
                shooter.ApplyActiveColor(entry.Color);
                shooter.SetShooterData(entry);
            }
            else
            {
                shooter.ApplyActiveColor(shooter.ColorData);
            }
        }
        else
        {
            // Back rows: hide color/text for hidden shooters; show inactive visuals for fixed-color shooters.
            if (shooter.IsColorHidden)
            {
                shooter.ToggleText(false);
            }
            else
            {
                shooter.ApplyInactiveColor(shooter.ColorData);
                shooter.ToggleText(true);
            }
        }
    }

    /// <summary>After vacating a slot, compact the affected column like BlockGrid.ApplyZSlideAsync: group shooters in that column by current row, order by row (front first), assign consecutive target rows 0,1,2,... so back moves up toward Z=0. Other columns unchanged.</summary>
    private void ReorderTrayAfterVacating(int vacatedCol)
    {
        if (_trayShooters.Count == 0 || _trayShooterSlots.Count != _trayShooters.Count) return;
        var inColumn = new List<(int index, int row)>();
        for (int i = 0; i < _trayShooters.Count; i++)
        {
            int slot = _trayShooterSlots[i];
            int col = slot % _gridWidth;
            int row = slot / _gridWidth;
            if (col == vacatedCol)
                inColumn.Add((i, row));
        }
        inColumn.Sort((a, b) => a.row.CompareTo(b.row));
        int newRow = 0;
        foreach (var (index, _) in inColumn)
        {
            int slot = vacatedCol + newRow * _gridWidth;
            _trayShooterSlots[index] = slot;

            // Shooter tray visuals (including hidden/revealed state) are updated in ApplyTrayLayout / UpdateShooterTrayState.

            newRow++;
        }
    }

    /// <summary>When a shooter is moved to platform, lerp remaining tray shooters to their new front positions.</summary>
    private void LerpTrayLayoutToTargets()
    {
        if (_trayShooters.Count == 0 || _trayLerpDuration <= 0f)
        {
            ApplyTrayLayout();
            return;
        }
        LerpTrayLayoutToTargetsAsync();
    }

    private async void LerpTrayLayoutToTargetsAsync()
    {
        try
        {
            await ApplyTrayLayoutLerpAsync(_trayLerpDuration);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
        }
    }

    private async Awaitable ApplyTrayLayoutLerpAsync(float duration)
    {
        if (this == null) return;
        int count = _trayShooters.Count;
        if (count == 0) return;

        Transform parent = TrayParent;
        var transforms = new List<Transform>(count);
        var starts = new List<Vector3>(count);
        var targets = new List<Vector3>(count);

        for (int i = 0; i < count; i++)
        {
            Shooter shooter = _trayShooters[i];
            if (shooter == null) continue;
            Transform s = shooter.transform;
            if (s == null) continue;
            int slot = i < _trayShooterSlots.Count ? _trayShooterSlots[i] : i;
            s.SetParent(parent);
            transforms.Add(s);
            starts.Add(s.localPosition);
            targets.Add(GetTraySlotLocalPosition(slot));
        }

        await Helper.LerpLocalZLinearAsync(
            transforms,
            starts,
            targets,
            duration,
            () => this == null);

        // After the positional lerp completes, snap visuals/text to the final tray state.
        // This ensures hidden shooters reveal correctly when they end up in the front row.
        if (this != null)
        {
            ApplyTrayLayout();
        }
    }
    public Transform[] GetOffscreenTransforms()
    {
        return _offscreenTransforms;
    }
}
