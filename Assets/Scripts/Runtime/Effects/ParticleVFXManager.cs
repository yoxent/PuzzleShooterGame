using System;
using UnityEngine;

public class ParticleVFXManager : MonoBehaviour
{
    [Header("Event-Driven Effects")]
    [SerializeField] private ParticleEffectConfig _levelCompletedEffect;
    [SerializeField] private ParticleEffectConfig _shooterAttackededEffect;

    private GameEventBus _eventBus;

    private void Start()
    {
        _eventBus = ServiceLocator.Resolve<GameEventBus>();
        SubscribeToEvents();
    }

    private void OnDisable() => UnsubscribeFromEvents();
    private void OnDestroy() => UnsubscribeFromEvents();

    private void SubscribeToEvents()
    {
        if (_eventBus == null) return;

        _eventBus.LevelCompleted += OnLevelCompleted;
        _eventBus.ShooterAttacked += OnShooterAttacked;
    }

    private void UnsubscribeFromEvents()
    {
        if (_eventBus == null) return;

        _eventBus.LevelCompleted -= OnLevelCompleted;
        _eventBus.ShooterAttacked -= OnShooterAttacked;
    }

    private void OnLevelCompleted()
    {
        SpawnEffect(_levelCompletedEffect, _levelCompletedEffect.SpawnPoint);
    }

    private void OnShooterAttacked(Shooter shooter)
    {
        SpawnEffect(_shooterAttackededEffect, shooter.ProjectileSpawnPoint);
    }

    private void SpawnEffect(ParticleEffectConfig config, Transform spawnPoint)
    {
        if (config.Prefab == null) return;

        ParticleSystem particle = Instantiate(config.Prefab, spawnPoint);

        float lifetime = particle.main.duration;
        if (lifetime <= 0f)
        {
            var main = particle.main;
            lifetime = main.duration + main.startLifetime.constantMax;
        }

        Destroy(particle.gameObject, Mathf.Max(0.01f, lifetime));
    }
}

