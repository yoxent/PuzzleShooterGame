using System;
using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Holds the level catalog and current level index (1-based). Persists index via PlayerPrefs. Owns the load flow: gets setup and raises LevelLoaded(setup) so subscribers (e.g. BlockGrid) receive the level without depending on LevelManager.
/// Register with ServiceLocator so UI and BlockGrid can resolve from here.
/// </summary>
public class LevelManager : MonoBehaviour
{
    private const string PrefsKeyLevel = "Demo_CurrentLevel";

    /// <summary>Raised when a level is loaded. Carries the LevelBlockSetup so subscribers can apply it. LevelManager acts first (persists index, then raises).</summary>
    public event Action<LevelBlockSetup> LevelLoaded;

    [Header("Levels")]
    [Tooltip("If true, the level will be loaded from PlayerPrefs. If false, the level will be loaded from the _levelOverride level.")]
    [SerializeField] private bool _usePlayerPrefsForLevels = true;
    [Tooltip("If usePlayerPrefsForLevels is false, this level will be loaded instead of the PlayerPrefs level.")]
    [SerializeField] private int _levelOverride = 1;
    [SerializeField] private LevelBlockSetup[] _levels;

    private GameEventBus _eventBus;

    /// <summary>Current level index (1-based). Clamped to 1..TotalLevelCount.</summary>
    public int CurrentLevelIndex { get; private set; }

    /// <summary>Total number of levels. When using test level: 1 if assigned else 0. Otherwise: length of Levels list.</summary>
    public int TotalLevelCount => _levels != null ? _levels.Length : 0;

    private bool HasCatalogLevels => _levels != null && _levels.Length > 0;
    private int MaxProgressLevelIndex => _levels != null && _levels.Length > 0 ? _levels.Length : 1;

    private void Awake()
    {
        if (_usePlayerPrefsForLevels)
        {
            CurrentLevelIndex = Mathf.Max(1, PlayerPrefs.GetInt(PrefsKeyLevel, 1));
        }
        else
        {
            CurrentLevelIndex = Mathf.Max(1, _levelOverride);
        }

        ServiceLocator.Register(this);
    }
    private void Start()
    {
        _eventBus = ServiceLocator.Resolve<GameEventBus>();
        if (_eventBus != null)
            _eventBus.LevelCompleted += OnLevelCompleted;
        StartCoroutine(LoadLevelNextFrame());
    }

    private IEnumerator LoadLevelNextFrame()
    {
        yield return null;
        LoadLevel(CurrentLevelIndex);
    }

    private void OnDestroy()
    {
        if (_eventBus != null)
            _eventBus.LevelCompleted -= OnLevelCompleted;
        LevelLoaded = null;
        ServiceLocator.Unregister<LevelManager>();
    }

    private void OnLevelCompleted()
    {
        int next = Mathf.Min(CurrentLevelIndex + 1, Mathf.Max(1, MaxProgressLevelIndex));
        if (_usePlayerPrefsForLevels)
        {
            PlayerPrefs.SetInt(PrefsKeyLevel, next);
            PlayerPrefs.Save();
        }
    }

    /// <summary>Returns the LevelBlockSetup for the given 1-based level index, or null if out of range or not assigned. When Use Test Level is true, always returns _testLevel (index is ignored).</summary>
    public LevelBlockSetup GetLevelSetup(int levelIndex)
    {
        if (_levels != null && _levels.Length > 0)
        {
            int i = Mathf.Clamp(levelIndex, 1, _levels.Length) - 1;
            return _levels[i];
        }

        return null;
    }

    /// <summary>Request to load level <paramref name="levelIndex"/> (1-based). Resolves the LevelBlockSetup, then raises LevelLoaded(setup).</summary>
    public void LoadLevel(int levelIndex)
    {
        int clampedLevel = Mathf.Clamp(levelIndex, 1, Mathf.Max(1, TotalLevelCount));
        CurrentLevelIndex = clampedLevel;

        LevelBlockSetup setup = GetLevelSetup(clampedLevel);
        LevelLoaded?.Invoke(setup);

        _eventBus?.RaiseLevelLoaded(clampedLevel);
    }

    /// <summary>Advance to the next level (or stay at max) and load it.</summary>
    public void LoadNextLevel()
    {
        int next = Mathf.Min(CurrentLevelIndex + 1, TotalLevelCount);
        if (next < 1) next = 1;
        LoadLevel(next);
    }
}
