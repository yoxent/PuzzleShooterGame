using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Shooter that auto-fires at blocks of the same color. Spawns projectiles from a pool; all hits destroy the block.
/// Has attack speed (fire rate) and a limited projectile count; when it runs out, goes offscreen then returns to ShooterPool.
/// </summary>
public class Shooter : MonoBehaviour
{
    //Classes
    private GameEventBus _eventBus;
    private ShooterPool _shooterPool;
    private ShooterContainer _shooterContainer;
    private BlockColorData _colorData;
    private bool _isColorHidden;
    private ShooterColorEntry _colorEntry;
    private BlockGrid _blockGrid;
    private Projectile _activeProjectile;
    private ProjectilePool _projectilePool;

    [Header("Visuals")]
    [Tooltip("Transform for the shooter model.")]
    [SerializeField] private Transform _modelRoot;
    [SerializeField] private MeshRenderer _renderer;
    [SerializeField] private TextMeshPro _projectileText;
    [SerializeField] private LineRenderer _lineRenderer;

    [Header("Feedback")]
    [Tooltip("How far back the model recoils on each shot.")]
    [SerializeField] private float _recoilDistance = 0.07f;
    [Tooltip("How long the recoil out-and-back lasts.")]
    [SerializeField] private float _recoilDuration = 0.08f;
    [Tooltip("How aggressively the model rotates toward its target on fire (0 = no turn, 1 = snap).")]
    [SerializeField][Range(0f, 1f)] private float _aimSlerpFactor = 0.35f;
    [Tooltip("Minimum yaw angle (degrees) the model can aim to on fire.")]
    [SerializeField] private float _minAimYaw = -28f;
    [Tooltip("Maximum yaw angle (degrees) the model can aim to on fire.")]
    [SerializeField] private float _maxAimYaw = 28f;

    [Header("Settings")]
    [SerializeField] private Transform _projectileSpawnPoint;
    public Transform ProjectileSpawnPoint => _projectileSpawnPoint;
    [SerializeField] private ShooterFireMode _fireMode = ShooterFireMode.Multiple;
    [SerializeField] private ShooterTargetingMode _targetingMode = ShooterTargetingMode.Random;
    [SerializeField] private float _attackSpeed = 0.5f;
    [SerializeField] private float _projectileSpeed = 15f;
    [Tooltip("Duration to move offscreen before returning to pool when out of projectiles.")]
    [SerializeField] private float _offscreenDuration = 2f;

    private Vector3 _modelBaseLocalPosition;
    private Quaternion _modelBaseRotation;
    private float _nextFireTime;
    private int _roundRobinIndex;
    private int _projectileCount;
    private bool _isGoingOffscreen;
    private float _offscreenStartTime;
    private float _offscreenEndTime;
    private Vector3 _offscreenStartPosition;
    private Vector3 _offscreenTargetPosition;
    private Quaternion _offscreenStartRotation;
    // Reused to avoid allocating a new List on every single block hit.
    private readonly List<Block> _singleBlockDestroyBuffer = new(1);
    private bool _isFiringSuspended;
    public bool IsMovingToPlatform { get; set; }

    /// <summary>Current number of projectiles remaining. When 0, shooter will go offscreen and be pooled.</summary>
    public int ProjectileCount => _projectileCount;

    /// <summary>True while moving offscreen before being returned to pool.</summary>
    public bool IsGoingOffscreen => _isGoingOffscreen;

    /// <summary>Color this shooter targets. Only blocks with this color are attacked.</summary>
    public BlockColorData ColorData => _colorData;
    /// <summary>True while the shooter's color should be hidden in the tray (revealed when eligible for platform deployment).</summary>
    public bool IsColorHidden => _isColorHidden;
    /// <summary>Full shooter color entry (color, hidden/linked flags) used to configure this shooter.</summary>
    public ShooterColorEntry ColorEntry => _colorEntry;
    /// <summary>True if this shooter currently has an active link line.</summary>
    public bool HasLinkLine => _lineRenderer != null && _lineRenderer.enabled;

    private void Start()
    {
        _eventBus = ServiceLocator.Resolve<GameEventBus>();
        _blockGrid = ServiceLocator.Resolve<BlockGrid>();
        _shooterContainer = ServiceLocator.Resolve<ShooterContainer>();
        _projectilePool = ServiceLocator.Resolve<ProjectilePool>();

        if (_modelRoot == null)
            _modelRoot = transform;
        _modelBaseLocalPosition = _modelRoot.localPosition;
        _modelBaseRotation = _modelRoot.rotation;
    }

    private void OnEnable()
    {
        if (!_isGoingOffscreen)
        {
            _projectileSpawnPoint.gameObject.SetActive(true);
        }
    }

    private void Update()
    {
        if (_isGoingOffscreen)
        {
            if (_offscreenDuration <= 0f)
            {
                transform.position = _offscreenTargetPosition;
                transform.rotation = Quaternion.identity;
            }
            else
            {
                float elapsed = Time.time - _offscreenStartTime;
                float t = Helper.SmoothStep01(elapsed / _offscreenDuration);
                transform.position = Vector3.Lerp(_offscreenStartPosition, _offscreenTargetPosition, t);

                // Only Y (yaw) from movement direction; X and Z stay at start rotation.
                // Right → 90, Left → -90, Up (+Z) → 0, Down (-Z) → 180.
                Vector3 e = _offscreenStartRotation.eulerAngles;
                Vector3 moveDir = (_offscreenTargetPosition - _offscreenStartPosition).normalized;
                if (moveDir.sqrMagnitude > 0.01f)
                {
                    float yaw = Mathf.Atan2(moveDir.x, moveDir.z) * Mathf.Rad2Deg;
                    transform.rotation = Quaternion.Euler(e.x, yaw, e.z);
                }
                else
                {
                    transform.rotation = _offscreenStartRotation;
                }
            }

            if (Time.time >= _offscreenEndTime)
            {
                transform.position = _offscreenTargetPosition;
                transform.rotation = Quaternion.identity;
                _isGoingOffscreen = false;
                _shooterContainer?.OnShooterReleased(this);
                _shooterContainer?.RemoveFromPlatform(this);
                _shooterContainer?.RemoveFromTray(this);
                _shooterPool?.Release(this);
            }
            return;
        }

        // Do not fire while any shooter is still lerping to a platform.
        if (IsMovingToPlatform)
        {
            return;
        }

        // Temporarily paused (e.g. during merges).
        if (_isFiringSuspended)
        {
            ReturnModelToBaseRotation();
            return;
        }

        if (_projectileCount <= 0)
        {
            if (_shooterContainer != null && _shooterContainer.IsOnPlatform(this))
            {
                _eventBus?.RaiseRequestGameOverCheck();
            }
            ReturnModelToBaseRotation();
            // For linked shooters, only start offscreen when both members of the pair are out of ammo.
            if (_shooterContainer == null || _shooterContainer.ShouldShooterStartOffscreen(this))
            {
                StartOffscreen();
            }
            return;
        }

        if (_fireMode == ShooterFireMode.Single && _activeProjectile != null) return;
        if (_shooterContainer != null && !_shooterContainer.IsOnPlatform(this)) return;
        if (_projectilePool == null || !_projectilePool.IsReady) return;
        if (_blockGrid == null || _colorData == null) return;
        if (Time.time < _nextFireTime) return;

        // Global priority: special front-row target (if present) overrides color targeting.
        Block target = _blockGrid.GetPrioritySpecialTarget();
        if (target == null)
        {
            IReadOnlyList<Block> targets = _blockGrid.GetAttackableBlocksMatching(_colorData);
            if (targets == null || targets.Count == 0)
            {
                ReturnModelToBaseRotation();
                return;
            }

            target = GetTargetFromList(targets, _projectileSpawnPoint.position);
        }

        if (target == null)
        {
            ReturnModelToBaseRotation();
            return;
        }

        // No need to normalize just to check if target is basically at the muzzle.
        Vector3 toTarget = target.transform.position - _projectileSpawnPoint.position;
        if (toTarget.sqrMagnitude <= 0.0001f)
        {
            ReturnModelToBaseRotation();
            return;
        }

        Fire(target, _projectileSpawnPoint.position);
    }

    /// <summary>Set the color entry this shooter uses (color + hidden/linked flags). Call when spawning or placing (e.g. from pool).</summary>
    public void SetShooterData(ShooterColorEntry entry)
    {
        _colorEntry = entry;
        _colorData = entry.Color;
        _isColorHidden = entry.IsHidden;
        ToggleText(!entry.IsHidden);
    }

    public void ConfigureLinkLine(BlockColorData selfColor, BlockColorData linkedColor)
    {
        if (_lineRenderer == null) return;
        _lineRenderer.gameObject.SetActive(true);
        _lineRenderer.enabled = true;
        _lineRenderer.positionCount = 2;
        _lineRenderer.startColor = selfColor != null ? selfColor.ActiveShooterColor : Color.white;
        _lineRenderer.endColor = linkedColor != null ? linkedColor.ActiveShooterColor : Color.white;
    }

    public void DisableLinkLine()
    {
        if (_lineRenderer == null) return;
        _lineRenderer.enabled = false;
    }

    public void DeactivateLinkLineObject()
    {
        if (_lineRenderer == null) return;
        _lineRenderer.gameObject.SetActive(false);
        _lineRenderer.enabled = false;
    }

    public void UpdateLinkLinePositions(Vector3 a, Vector3 b)
    {
        if (_lineRenderer == null || !_lineRenderer.enabled) return;
        _lineRenderer.SetPosition(0, a);
        _lineRenderer.SetPosition(1, b);
    }

    public void ToggleText(bool isVisible)
    {
        if (_projectileText == null) return;

        // Keep the label visible; hidden mode shows '?' instead of disabling the text.
        _projectileText.gameObject.SetActive(true);
        _projectileText.text = isVisible ? _projectileCount.ToString() : "?";
    }

    /// <summary>Set the pool to return to when out of projectiles. Called by ShooterPool on Get.</summary>
    public void SetPool(ShooterPool pool) => _shooterPool = pool;

    /// <summary>Set initial projectile count (ammo). Call after getting from pool.</summary>
    public void SetProjectileCount(int count)
    {
        _projectileCount = Mathf.Max(0, count);
        UpdateProjectileTextCount(_projectileCount);
    }

    /// <summary>Reset fire cooldown so the shooter can fire on the next frame. Call when placed on a platform.</summary>
    public void ResetFireCooldown() => _nextFireTime = 0f;

    /// <summary>Temporarily stop this shooter from firing (e.g. during merges).</summary>
    public void SuspendFiring()
    {
        _isFiringSuspended = true;
        ReturnModelToBaseRotation();
    }

    /// <summary>Resume firing after a suspension and allow Update to schedule shots again.</summary>
    public void ResumeFiring()
    {
        _isFiringSuspended = false;
        ResetFireCooldown();
    }

    private Block GetTargetFromList(IReadOnlyList<Block> targets, Vector3 origin)
    {
        if (targets == null || targets.Count == 0) return null;
        int index;

        switch (_targetingMode)
        {
            case ShooterTargetingMode.RoundRobin:
                index = _roundRobinIndex % targets.Count;
                _roundRobinIndex++;
                return targets[index];

            case ShooterTargetingMode.SharedRoundRobin:
                index = _blockGrid.GetAndAdvanceSharedRoundRobinIndex(_colorData, targets.Count);
                return targets[index % targets.Count];

            case ShooterTargetingMode.First:
                return targets[0];

            case ShooterTargetingMode.ClosestFirst:
                return GetClosestTarget(targets, origin);

            case ShooterTargetingMode.Random:

            default:
                return targets[Random.Range(0, targets.Count)];
        }
    }

    private Block GetClosestTarget(IReadOnlyList<Block> targets, Vector3 origin)
    {
        if (targets == null || targets.Count == 0) return null;
        Block closest = null;
        float sqrMin = float.MaxValue;

        for (int i = 0; i < targets.Count; i++)
        {
            Block b = targets[i];
            if (b == null) continue;
            float sqr = (b.transform.position - origin).sqrMagnitude;
            if (sqr < sqrMin)
            {
                sqrMin = sqr;
                closest = b;
            }
        }

        return closest;
    }

    private void Fire(Block targetBlock, Vector3 spawnPos)
    {
        bool shouldReserveTarget = targetBlock == null || !targetBlock.IsSpecialTarget;
        if (shouldReserveTarget && _blockGrid != null && !_blockGrid.TryReserveTarget(targetBlock)) return;

        Projectile projectile = _projectilePool.Get();
        if (projectile == null)
        {
            if (shouldReserveTarget)
                _blockGrid?.UnreserveTarget(targetBlock);
            return;
        }

        _nextFireTime = Time.time + _attackSpeed;
        if (targetBlock == null || !targetBlock.IsSpecialTarget)
        {
            _projectileCount--;
            UpdateProjectileTextCount(_projectileCount);
        }

        if (_fireMode == ShooterFireMode.Single) _activeProjectile = projectile;

        Vector3 targetPos = targetBlock.transform.position;

        // Aim the visual model toward the target with damped rotation (Y-up, flattening vertical component),
        // clamped between the configured min/max yaw angles.
        if (_modelRoot != null)
        {
            Vector3 aimDir = targetPos - _modelRoot.position;
            aimDir.y = 0f;

            if (aimDir.sqrMagnitude > 0.0001f)
            {
                Quaternion current = _modelRoot.rotation;
                Quaternion targetRot = Quaternion.LookRotation(aimDir.normalized, Vector3.up);

                // Clamp yaw around world forward (0 degrees) using signed delta.
                float rawYaw = targetRot.eulerAngles.y;
                float deltaYaw = Mathf.DeltaAngle(0f, rawYaw);
                float clampedDelta = Mathf.Clamp(deltaYaw, _minAimYaw, _maxAimYaw);
                targetRot = Quaternion.Euler(0f, clampedDelta, 0f);

                float factor = Mathf.Clamp01(_aimSlerpFactor);
                _modelRoot.rotation = Quaternion.Slerp(current, targetRot, factor);
            }
        }

        float distance = Vector3.Distance(spawnPos, targetPos);
        float duration = distance / Mathf.Max(0.01f, _projectileSpeed);

        projectile.transform.SetPositionAndRotation(spawnPos, Quaternion.LookRotation(targetPos - spawnPos));
        projectile.LaunchToward(
            spawnPos,
            targetPos,
            targetBlock,
            duration,
            OnProjectileHitBlock,
            () => OnProjectileStoppedForBlock(targetBlock, projectile));

        PlayFireRecoil();

        _eventBus?.RaiseShooterAttacked(this);
    }

    private void OnProjectileHitBlock(Block block)
    {
        if (_fireMode == ShooterFireMode.Single)
            _activeProjectile = null;

        _eventBus?.RaiseBlockHit(block);

        if (_blockGrid != null)
        {
            if (block != null && block.IsSpecialTarget)
            {
                _blockGrid.PlayHitReactions(block);
            }
            else
            {
                _blockGrid.PlayNeighborHitReactions(block);
            }

            if (block == null || !block.IsSpecialTarget)
                _blockGrid.UnreserveTarget(block);

            if (block != null)
            {
                bool shouldDestroy = block.ApplyHitAndShouldDestroy();
                if (!shouldDestroy) return;

                // Reuse a tiny list here so rapid-fire hits don't keep creating garbage.
                _singleBlockDestroyBuffer.Clear();
                _singleBlockDestroyBuffer.Add(block);
                _blockGrid.DestroyBlocksAndSlide(_singleBlockDestroyBuffer);
            }
        }
    }

    private void OnProjectileStoppedForBlock(Block block, Projectile projectile)
    {
        if (_fireMode == ShooterFireMode.Single && _activeProjectile == projectile)
            _activeProjectile = null;

        if (block != null && _blockGrid != null && !block.IsSpecialTarget)
            _blockGrid.UnreserveTarget(block);
    }

    private async void PlayFireRecoil()
    {
        if (_modelRoot == null) return;

        Vector3 start = _modelBaseLocalPosition;
        Vector3 back = start + Vector3.back * _recoilDistance;

        float elapsed = 0f;
        while (elapsed < _recoilDuration)
        {
            if (this == null || _modelRoot == null) return;
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / _recoilDuration);
            // Out-and-back curve over 0..1
            float phase = t <= 0.5f ? t / 0.5f : (1f - t) / 0.5f;
            _modelRoot.localPosition = Vector3.Lerp(start, back, phase);
            await Awaitable.NextFrameAsync();
        }

        if (this != null && _modelRoot != null)
            _modelRoot.localPosition = _modelBaseLocalPosition;
    }

    private void ReturnModelToBaseRotation()
    {
        if (_modelRoot == null) return;
        // Smoothly return toward the original rotation using the same damping factor.
        float factor = Mathf.Clamp01(_aimSlerpFactor);
        _modelRoot.rotation = Quaternion.Slerp(_modelRoot.rotation, _modelBaseRotation, factor);
    }

    private void StartOffscreen()
    {
        if (_isGoingOffscreen) return;

        _projectileSpawnPoint.gameObject.SetActive(false);
        // Free the platform right away so the next shooter can be placed
        // while this one is still playing its offscreen animation.
        _shooterContainer?.VacatePlatformSlot(this);
        _offscreenStartPosition = transform.position;
        _offscreenStartRotation = transform.rotation;
        _offscreenTargetPosition = _offscreenStartPosition + Vector3.down * 20f;

        Transform[] offscreenPoints = _shooterContainer != null ? _shooterContainer.GetOffscreenTransforms() : null;
        int linkedDir = 0;
        if (_shooterContainer != null && offscreenPoints != null && offscreenPoints.Length > 0)
        {
            linkedDir = _shooterContainer.GetLinkedOffscreenDirection(this, _offscreenStartPosition, offscreenPoints);
        }

        if (offscreenPoints != null && offscreenPoints.Length > 0)
        {
            float bestSqr = float.MaxValue;
            for (int i = 0; i < offscreenPoints.Length; i++)
            {
                Transform point = offscreenPoints[i];
                if (point == null) continue;

                if (linkedDir != 0)
                {
                    float signX = Mathf.Sign(point.position.x);
                    if (Mathf.Approximately(signX, 0f))
                        signX = 1f;
                    int dir = signX > 0 ? 1 : -1;
                    if (dir != linkedDir) continue;
                }

                float sqr = (point.position - _offscreenStartPosition).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    _offscreenTargetPosition = point.position;
                }
            }
        }

        _isGoingOffscreen = true;
        _offscreenStartTime = Time.time;
        _offscreenEndTime = Time.time + _offscreenDuration;
    }

    private void UpdateProjectileTextCount(int count)
    {
        if (_projectileText != null)
            _projectileText.text = count.ToString();
    }

    public void ApplyInactiveColor(BlockColorData colorData)
    {
        if (colorData == null) return;
        if (_renderer != null)
        {
            if (colorData.InactiveShooterMaterial != null)
            {
                _renderer.sharedMaterial = colorData.InactiveShooterMaterial;
            }

            MaterialPropertyBlock _propertyBlock = new MaterialPropertyBlock();
            colorData.ApplyInactiveShooterColor(_propertyBlock);
            _renderer.SetPropertyBlock(_propertyBlock);
        }
    }

    public void ApplyActiveColor(BlockColorData colorData)
    {
        if (colorData == null) return;
        if (_renderer != null)
        {
            if (colorData.ActiveShooterMaterial != null)
            {
                _renderer.sharedMaterial = colorData.ActiveShooterMaterial;
            }

            MaterialPropertyBlock _propertyBlock = new MaterialPropertyBlock();
            colorData.ApplyActiveShooterColor(_propertyBlock);
            _renderer.SetPropertyBlock(_propertyBlock);
        }
    }
}
