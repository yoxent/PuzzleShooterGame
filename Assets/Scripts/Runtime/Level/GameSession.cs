using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns win/lose rules. Win when no blocks remain. Lose only when platforms are full and no platform shooter can fire.
/// Fail checks are deferred until projectiles/slides/platform-lerps settle so we evaluate stable board state.
/// </summary>
public class GameSession : MonoBehaviour
{
    private BlockGrid _blockGrid;
    private ShooterContainer _shooterContainer;
    private ShooterPlatforms _shooterPlatforms;
    private GameEventBus _eventBus;
    private bool _deferredFailPending;

    private GameState gameState = GameState.None;

    private void Start()
    {
        _blockGrid = ServiceLocator.Resolve<BlockGrid>();
        _shooterContainer = ServiceLocator.Resolve<ShooterContainer>();
        _shooterPlatforms = ServiceLocator.Resolve<ShooterPlatforms>();

        OnSubscribeToEvents();
    }

    private void OnDisable() => OnUnsubscribeToEvents();
    private void OnDestroy() => OnUnsubscribeToEvents();

    public void OnSubscribeToEvents()
    {
        _eventBus = ServiceLocator.Resolve<GameEventBus>();

        if (_eventBus != null)
        {
            _eventBus.ShooterPlacedOnPlatform += OnShooterPlacedOnPlatform;
            _eventBus.SlideCompleted += OnSlideCompleted;
            _eventBus.RequestGameOverCheck += OnRequestGameOverCheck;
        }
    }

    public void OnUnsubscribeToEvents()
    {
        if (_eventBus != null)
        {
            _eventBus.ShooterPlacedOnPlatform -= OnShooterPlacedOnPlatform;
            _eventBus.SlideCompleted -= OnSlideCompleted;
            _eventBus.RequestGameOverCheck -= OnRequestGameOverCheck;
        }
    }

    /// <summary>Fail only triggers when all platforms are full and no platform shooter can fire.</summary>
    private void OnShooterPlacedOnPlatform()
    {
        TryEvaluateFail();
    }

    /// <summary>On slide completed: full win + fail evaluation.</summary>
    private void OnSlideCompleted()
    {
        if (gameState != GameState.None) return;
        if (TryEvaluateWin()) return;
        TryEvaluateFail();
    }

    /// <summary>On request game-over check (e.g. shooter ran out): full win + fail evaluation.</summary>
    private void OnRequestGameOverCheck()
    {
        if (gameState != GameState.None) return;
        if (TryEvaluateWin()) return;
        TryEvaluateFail();
    }

    /// <summary>Returns true if level was won (0 blocks, raised LevelCompleted).</summary>
    private bool TryEvaluateWin()
    {
        if (_blockGrid.ActiveBlockCount != 0) return false;

        _eventBus?.RaiseLevelCompleted();
        Debug.Log("[GameSession] Level completed raised."); // Testing only; remove later.
        gameState = GameState.Win;
        return true;
    }

    /// <summary>Fail evaluation: all platforms are full, nothing is still settling, and no platform shooter can currently fire.</summary>
    private void TryEvaluateFail()
    {
        if (_blockGrid == null || _shooterContainer == null || _shooterPlatforms == null)
        {
            return;
        }

        if (_blockGrid.ActiveBlockCount == 0)
        {
            return;
        }

        if (_shooterContainer.CanStillPlaceShootersFromTray())
        {
            return;
        }

        int platformCount = _shooterPlatforms.ActiveCount;
        // All active platforms must be occupied; otherwise this isn't a full-platform loss state.
        for (int i = 0; i < platformCount; i++)
        {
            if (_shooterContainer.GetShooterAtPlatform(i) == null) return;
        }

        // Defer fail while transient work is still happening.
        if (_blockGrid.HasProjectilesInFlight ||
            _blockGrid.HasBlocksMoving ||
            _blockGrid.IsResolvingBoardState ||
            _shooterContainer.IsAnyShooterMovingToPlatform ||
            _shooterContainer.IsMergeInProgress)
        {
            if (!_deferredFailPending)
            {
                _deferredFailPending = true;
                StartCoroutine(DeferredLevelFailed());
            }
            return;
        }

        // Full platforms can still be playable if a valid triple merge is pending.
        if (_shooterContainer.HasPendingMergeCandidate())
        {
            return;
        }

        if (IsAnyPlatformShooterAbleToFire(platformCount))
        {
            return;
        }

        gameState = GameState.Lose;
        _eventBus?.RaiseLevelFailed();
        Debug.Log("[GameSession] Level failed raised."); // Testing only; remove later.
    }

    private IEnumerator DeferredLevelFailed()
    {
        while (_blockGrid != null &&
               (_blockGrid.HasProjectilesInFlight ||
                _blockGrid.HasBlocksMoving ||
                _blockGrid.IsResolvingBoardState ||
                (_shooterContainer != null && (_shooterContainer.IsAnyShooterMovingToPlatform || _shooterContainer.IsMergeInProgress))))
        {
            yield return null;
        }

        _deferredFailPending = false;

        if (this == null || !this || _blockGrid == null) yield break;
        if (_shooterContainer == null || _shooterPlatforms == null) yield break;
        if (gameState != GameState.None) yield break;

        // Re-check after settling: player may have gained a legal placement or already won.
        if (_shooterContainer.CanStillPlaceShootersFromTray() || _blockGrid.ActiveBlockCount == 0) yield break;

        int platformCount = _shooterPlatforms.ActiveCount;
        for (int i = 0; i < platformCount; i++)
        {
            if (_shooterContainer.GetShooterAtPlatform(i) == null) yield break;
        }

        if (_shooterContainer.HasPendingMergeCandidate()) yield break;
        if (IsAnyPlatformShooterAbleToFire(platformCount)) yield break;

        gameState = GameState.Lose;
        _eventBus?.RaiseLevelFailed();
        Debug.Log("[GameSession] Level failed raised."); // Testing only; remove later.
    }

    private bool IsAnyPlatformShooterAbleToFire(int platformCount)
    {
        if (_blockGrid != null && _blockGrid.GetPrioritySpecialTarget() != null)
            return true;

        for (int i = 0; i < platformCount; i++)
        {
            Shooter shooter = _shooterContainer.GetShooterAtPlatform(i);
            if (shooter == null) continue;
            if (shooter.ProjectileCount <= 0 || shooter.ColorData == null) continue;
            if (shooter.IsGoingOffscreen || shooter.IsMovingToPlatform) continue;

            IReadOnlyList<Block> targets = _blockGrid.GetAttackableBlocksMatching(shooter.ColorData);
            if (targets != null && targets.Count > 0) return true;
        }

        return false;
    }
}
