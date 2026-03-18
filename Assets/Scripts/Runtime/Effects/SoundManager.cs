using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Subscribes to game events and plays sounds. Assign clips in the inspector.
/// Add to a GameObject in the scene (e.g. with GameEventBus); resolve event bus in Start.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class SoundManager : MonoBehaviour
{
    [SerializeField] private AudioSource _audioSource;
    [SerializeField][Min(1)] private int _maxSimultaneousSfx = 8;

    [Header("Block")]
    [SerializeField] private AudioClip _blockHitClip;

    [Header("Shooters")]
    [SerializeField] private AudioClip _shooterDeployedClip;
    [SerializeField] private AudioClip _shootersLiftClip;
    [SerializeField] private AudioClip _shootersConvergeClip;
    [SerializeField] private AudioClip _shooterSettleClip;

    [Header("UI")]
    [SerializeField] private AudioClip _winClip;
    [SerializeField] private AudioClip _loseClip;

    private GameEventBus _eventBus;
    private readonly List<AudioSource> _sfxSources = new();

    private void Awake()
    {
        if (_audioSource != null)
        {
            _audioSource.playOnAwake = false;
            _audioSource.loop = false;
            _audioSource.spatialBlend = 0f;
        }
    }

    private void Start()
    {
        OnSubscribeToEvents();
    }

    private void OnDisable() => OnUnsubscribeToEvents();
    private void OnDestroy() => OnUnsubscribeToEvents();

    public void OnSubscribeToEvents()
    {
        _eventBus = ServiceLocator.Resolve<GameEventBus>();

        if (_eventBus != null)
        {
            _eventBus.ShooterDeployed += OnShooterDeployed;
            _eventBus.ShootersMergeLift += OnShootersLift;
            _eventBus.ShootersMergeConverged += OnShootersConverged;
            _eventBus.ShooterMergedSettled += OnShooterSettled;
            _eventBus.BlockHit += OnBlockHit;
            _eventBus.LevelCompleted += OnPlayWinSFX;
            _eventBus.LevelFailed += OnPlayLoseSFX;
        }
    }

    public void OnUnsubscribeToEvents()
    {
        if (_eventBus != null)
        {
            _eventBus.ShooterDeployed -= OnShooterDeployed;
            _eventBus.ShootersMergeLift -= OnShootersLift;
            _eventBus.ShootersMergeConverged -= OnShootersConverged;
            _eventBus.ShooterMergedSettled -= OnShooterSettled;
            _eventBus.BlockHit -= OnBlockHit;
            _eventBus.LevelCompleted -= OnPlayWinSFX;
            _eventBus.LevelFailed -= OnPlayLoseSFX;
        }
    }

    private bool CanPlaySFX(AudioClip clip)
    {
        return clip != null && _audioSource != null;
    }

    /// <summary>
    /// Returns an idle pooled AudioSource for one-shot SFX, creates one when under cap,
    /// and falls back to reusing an existing source when the pool is full.
    /// </summary>
    private AudioSource GetAvailableSfxSource()
    {
        for (int i = 0; i < _sfxSources.Count; i++)
        {
            AudioSource s = _sfxSources[i];
            if (s != null && !s.isPlaying) return s;
        }

        if (_sfxSources.Count < _maxSimultaneousSfx)
        {
            var go = new GameObject($"SFX_{_sfxSources.Count:00}");
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = _audioSource.spatialBlend;
            source.outputAudioMixerGroup = _audioSource.outputAudioMixerGroup;
            source.volume = _audioSource.volume;
            source.pitch = _audioSource.pitch;
            _sfxSources.Add(source);
            return source;
        }

        // If at cap, reuse first source; PlayOneShot still keeps app responsive.
        return _sfxSources.Count > 0 ? _sfxSources[0] : _audioSource;
    }

    private void PlaySfx(AudioClip clip)
    {
        if (!CanPlaySFX(clip)) return;
        AudioSource source = GetAvailableSfxSource();
        if (source == null) return;
        source.PlayOneShot(clip);
    }

    private void OnShooterDeployed()
    {
        PlaySfx(_shooterDeployedClip);
    }

    private void OnShootersLift()
    {
        PlaySfx(_shootersLiftClip);
    }

    private void OnShootersConverged()
    {
        PlaySfx(_shootersConvergeClip);
    }

    private void OnShooterSettled()
    {
        PlaySfx(_shooterSettleClip);
    }

    private void OnBlockHit(Block b)
    {
        PlaySfx(_blockHitClip);
    }

    private void OnPlayWinSFX()
    {
        PlaySfx(_winClip);
    }

    private void OnPlayLoseSFX()
    {
        PlaySfx(_loseClip);
    }
}
