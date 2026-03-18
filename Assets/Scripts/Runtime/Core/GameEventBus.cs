using System;
using UnityEngine;

/// <summary>
/// Concrete event bus with named events. Add to a GameObject in the scene (e.g. a bootstrap or Managers object);
/// registers with ServiceLocator in Awake. Resolve via ServiceLocator.Resolve&lt;GameEventBus&gt;().
/// </summary>
public class GameEventBus : MonoBehaviour
{
    private event Action<int> LevelLoadedInvoked;
    private event Action<Shooter> ShooterSelectedInvoked;
    private event Action<Shooter> ShooterAttackedInvoked;
    private event Action ShooterPlacedOnPlatformInvoked;
    private event Action SlideCompletedInvoked;
    private event Action RequestGameOverCheckInvoked;
    private event Action ShooterDeployedInvoked;
    private event Action ShootersMergeLiftInvoked;
    private event Action ShootersMergeConvergedInvoked;
    private event Action ShooterMergedSettledInvoked;
    private event Action<Block> BlockHitInvoked;
    private event Action<Block> BlockDestroyedInvoked;
    private event Action LevelCompletedInvoked;
    private event Action LevelFailedInvoked;


    public event Action<int> LevelLoaded
    {
        add => LevelLoadedInvoked += value;
        remove => LevelLoadedInvoked -= value;
    }
    public event Action<Shooter> ShooterSelected
    {
        add => ShooterSelectedInvoked += value;
        remove => ShooterSelectedInvoked -= value;
    }
    public event Action<Shooter> ShooterAttacked
    {
        add => ShooterAttackedInvoked += value;
        remove => ShooterAttackedInvoked -= value;
    }
    public event Action ShooterPlacedOnPlatform
    {
        add => ShooterPlacedOnPlatformInvoked += value;
        remove => ShooterPlacedOnPlatformInvoked -= value;
    }
    public event Action SlideCompleted
    {
        add => SlideCompletedInvoked += value;
        remove => SlideCompletedInvoked -= value;
    }
    public event Action RequestGameOverCheck
    {
        add => RequestGameOverCheckInvoked += value;
        remove => RequestGameOverCheckInvoked -= value;
    }
    public event Action ShooterDeployed
    {
        add => ShooterDeployedInvoked += value;
        remove => ShooterDeployedInvoked -= value;
    }
    public event Action ShootersMergeLift
    {
        add => ShootersMergeLiftInvoked += value;
        remove => ShootersMergeLiftInvoked -= value;
    }
    public event Action ShootersMergeConverged
    {
        add => ShootersMergeConvergedInvoked += value;
        remove => ShootersMergeConvergedInvoked -= value;
    }
    public event Action ShooterMergedSettled
    {
        add => ShooterMergedSettledInvoked += value;
        remove => ShooterMergedSettledInvoked -= value;
    }
    public event Action<Block> BlockHit
    {
        add => BlockHitInvoked += value;
        remove => BlockHitInvoked -= value;
    }
    public event Action<Block> BlockDestroyed
    {
        add => BlockDestroyedInvoked += value;
        remove => BlockDestroyedInvoked -= value;
    }
    public event Action LevelCompleted
    {
        add => LevelCompletedInvoked += value;
        remove => LevelCompletedInvoked -= value;
    }
    public event Action LevelFailed
    {
        add => LevelFailedInvoked += value;
        remove => LevelFailedInvoked -= value;
    }

    public void RaiseLevelLoaded(int levelIndex) => LevelLoadedInvoked?.Invoke(levelIndex);
    public void RaiseShooterSelected(Shooter shooter) => ShooterSelectedInvoked?.Invoke(shooter);
    public void RaiseShooterAttacked(Shooter shooter) => ShooterAttackedInvoked?.Invoke(shooter);
    public void RaiseShooterPlacedOnPlatform() => ShooterPlacedOnPlatformInvoked?.Invoke();
    public void RaiseSlideCompleted() => SlideCompletedInvoked?.Invoke();
    public void RaiseRequestGameOverCheck() => RequestGameOverCheckInvoked?.Invoke();

    public void RaiseShooterDeployed() => ShooterDeployedInvoked?.Invoke();
    public void RaiseShootersMergeLift() => ShootersMergeLiftInvoked?.Invoke();
    public void RaiseShootersMergeConverged() => ShootersMergeConvergedInvoked?.Invoke();
    public void RaiseShooterMergedSettled() => ShooterMergedSettledInvoked?.Invoke();
    public void RaiseBlockHit(Block block) => BlockHitInvoked?.Invoke(block);
    public void RaiseBlockDestroyed(Block block) => BlockDestroyedInvoked?.Invoke(block);
    public void RaiseLevelCompleted() => LevelCompletedInvoked?.Invoke();
    public void RaiseLevelFailed() => LevelFailedInvoked?.Invoke();

    private void Awake()
    {
        ServiceLocator.Register(this);
    }

    private void OnDestroy()
    {
        LevelLoadedInvoked = null;
        ShooterSelectedInvoked = null;
        ShooterAttackedInvoked = null;
        ShooterPlacedOnPlatformInvoked = null;
        SlideCompletedInvoked = null;
        RequestGameOverCheckInvoked = null;
        ShooterDeployedInvoked = null;
        ShootersMergeLiftInvoked = null;
        ShootersMergeConvergedInvoked = null;
        ShooterMergedSettledInvoked = null;
        BlockHitInvoked = null;
        BlockDestroyedInvoked = null;
        LevelCompletedInvoked = null;
        LevelFailedInvoked = null;
        ServiceLocator.Unregister<GameEventBus>();
    }
}
