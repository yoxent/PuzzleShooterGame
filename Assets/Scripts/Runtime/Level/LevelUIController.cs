using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LevelUIController : MonoBehaviour
{
    private GameEventBus _eventBus;
    private LevelManager _levelManager;
    private SceneLoader _sceneLoader;
    private EnvironmentManager _environmentManager;

    [SerializeField] private List<TextMeshProUGUI> _levelTexts = new List<TextMeshProUGUI>();
    [SerializeField] private Image _levelProgressFill;
    [SerializeField] private GameObject _winPanel;
    [SerializeField] private GameObject _losePanel;
    [SerializeField] private Image _unlockProgress;
    [SerializeField] private TextMeshProUGUI _unlockProgressText;
    [SerializeField] private List<Image> _featureImages = new List<Image>();
    [SerializeField] private GameObject _hardLevelPanel;
    [SerializeField] private GameObject _nextFeaturePanel;
    [SerializeField] private GameObject _unlockedPanel;

    [Header("Special Target")]
    [SerializeField] private GameObject _specialTargetPanel;
    [SerializeField] private TextMeshProUGUI _specialTargetText;
    [SerializeField] private GameObject _specialTargetClearedImage;

    private int _level;
    private float _destroyedBlocks = 0;
    private float _totalBlocks = 120;
    private int _specialTargetsRemaining;
    private void Start()
    {
        _levelManager = ServiceLocator.Resolve<LevelManager>();
        _eventBus = ServiceLocator.Resolve<GameEventBus>();
        _sceneLoader = ServiceLocator.Resolve<SceneLoader>();
        _environmentManager = ServiceLocator.Resolve<EnvironmentManager>();

        SubscribeToEvents();
        SyncLevelFromManager();
        ResetFillProgress();
    }

    /// <summary>Apply current level state from LevelManager so UI is correct even if we subscribed after LevelLoaded fired.</summary>
    private void SyncLevelFromManager()
    {
        if (_levelManager == null) return;

        int levelIndex = _levelManager.CurrentLevelIndex;
        LevelBlockSetup setup = _levelManager.GetLevelSetup(levelIndex);

        if (setup != null)
            UpdateLevelData(setup);
        UpdateLevelDisplay(levelIndex);
    }
    private void OnDisable() => UnsubscribeToEvents();
    private void OnDestroy() => UnsubscribeToEvents();

    private void SubscribeToEvents()
    {
        if (_levelManager != null)
        {
            _levelManager.LevelLoaded += UpdateLevelData;
        }

        if (_eventBus != null)
        {
            _eventBus.LevelLoaded += UpdateLevelDisplay;
            _eventBus.BlockDestroyed += UpdateLevelProgress;
            _eventBus.LevelCompleted += ShowWinPanel;
            _eventBus.LevelFailed += ShowLosePanel;
        }
    }

    private void UnsubscribeToEvents()
    {
        if (_levelManager != null)
        {
            _levelManager.LevelLoaded -= UpdateLevelData;
        }

        if (_eventBus != null)
        {
            _eventBus.LevelLoaded -= UpdateLevelDisplay;
            _eventBus.BlockDestroyed -= UpdateLevelProgress;
            _eventBus.LevelCompleted -= ShowWinPanel;
            _eventBus.LevelFailed -= ShowLosePanel;
        }
    }

    public void OnPressedHome()
    {

    }

    /// <summary>Reload the game scene. LevelManager already advanced and saved the next level on LevelCompleted, so the new session will load it.</summary>
    public void OnPressedContinueNextLevel()
    {
        _sceneLoader?.LoadSceneWithLoading("Game");
    }

    public void OnPressedRetry()
    {
        _sceneLoader?.LoadSceneWithLoading("Game");
    }

    private void UpdateLevelDisplay(int levelIndex)
    {
        _level = levelIndex;

        if (_levelTexts != null)
        {
            foreach (var levelText in _levelTexts)
            {
                levelText.text = $"Level {levelIndex}";
            }
        }

        ApplyCurrentUnlockableSprites();
    }

    private void ApplyCurrentUnlockableSprites()
    {
        var unlockables = ServiceLocator.Resolve<Unlockables>();
        if (unlockables == null || _featureImages == null) return;

        if (!unlockables.TryGetCurrentUnlockable(out UnlockableFeature feature) || feature.UnlockableImage == null)
            return;

        foreach (Image img in _featureImages)
        {
            if (img != null)
                img.sprite = feature.UnlockableImage;
        }
    }

    private void UpdateLevelData(LevelBlockSetup setup)
    {
        _totalBlocks = setup.GetBlockCount();
        _specialTargetsRemaining = GetSpecialTargetCount(setup);
        UpdateSpecialTargetUI();

        _hardLevelPanel?.SetActive(setup.IsHardLevel);
        _environmentManager?.UpdateMaterial(setup.IsHardLevel);
    }

    private void UpdateLevelProgress(Block b)
    {
        if (_levelProgressFill == null) return;

        _levelProgressFill.fillAmount = Mathf.Clamp(++_destroyedBlocks / _totalBlocks, 0f, 1f);

        if (b != null && b.IsSpecialTarget)
        {
            _specialTargetsRemaining = Mathf.Max(0, _specialTargetsRemaining - 1);
            UpdateSpecialTargetUI();
        }
    }

    private void UpdateUnlockProgress()
    {
        AnimateUnlockProgress();
    }

    private void ResetFillProgress()
    {
        _destroyedBlocks = 0f;
        if (_levelProgressFill != null)
        {
            _levelProgressFill.fillAmount = 0f;
        }

        if (_unlockProgress != null)
        {
            _unlockProgress.fillAmount = 0f;
            _unlockProgress.transform.localScale = Vector3.one;
        }

        if (_unlockProgressText != null)
            _unlockProgressText.gameObject.SetActive(true);
    }

    private int GetSpecialTargetCount(LevelBlockSetup setup)
    {
        if (setup == null) return 0;

        int count = 0;
        for (int r = 0; r < setup.Height; r++)
        {
            for (int c = 0; c < setup.Width; c++)
            {
                BlockColorData color = setup.GetColorAt(c, r);
                if (color == null || !color.IsSpecialTarget) continue;
                count += setup.GetTierAt(c, r);
            }
        }

        return count;
    }

    private void UpdateSpecialTargetUI()
    {
        bool hasTargets = _specialTargetsRemaining > 0;

        if (_specialTargetText != null)
        {
            _specialTargetText.gameObject.SetActive(hasTargets);
            _specialTargetText.text = _specialTargetsRemaining.ToString();
        }

        if (_specialTargetPanel == null) return;

        // Once shown, keep panel visible; only swap between "count" and "cleared" visuals.
        if (_specialTargetPanel.activeInHierarchy)
        {
            if (_specialTargetClearedImage != null)
                _specialTargetClearedImage.SetActive(!hasTargets);
        }
        else
        {
            if (_specialTargetClearedImage != null)
                _specialTargetClearedImage.SetActive(false);
            _specialTargetPanel.SetActive(hasTargets);
        }
    }

    private void ShowWinPanel()
    {
        if (_winPanel == null) return;

        _winPanel.SetActive(true);

        UpdateUnlockProgress();
    }

    private void ShowLosePanel()
    {
        if (_losePanel == null) return;

        _losePanel.SetActive(true);
    }

    private void SetUnlockText(float fill)
    {
        if (_unlockProgressText == null) return;
        _unlockProgressText.text = $"{fill * 100f:0}%";
    }

    private async void AnimateUnlockProgress()
    {
        _nextFeaturePanel.SetActive(true);
        float targetFill = 0f;
        var unlockables = ServiceLocator.Resolve<Unlockables>();
        if (unlockables != null && unlockables.TryGetCurrentUnlockable(out UnlockableFeature feature))
        {
            int start = feature.LevelUnlockFeatureStart;
            int end = feature.LevelUnlockFeatureEnd;
            int span = Mathf.Max(1, end - start + 1);

            // Map level progress within the unlock range [start..end] instead of [0..end].
            // Example: start=8, end=15 => level 8 is ~12.5% (1/8), not ~53%.
            targetFill = Mathf.Clamp01((float)(_level - start + 1) / span);

            const float duration = 0.8f;

            float startFill = Mathf.Clamp01((float)(_level - start) / span);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                if (this == null) return;

                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float currentFill = Mathf.Lerp(startFill, targetFill, t);

                if (_unlockProgress != null)
                {
                    _unlockProgress.fillAmount = currentFill;
                }

                SetUnlockText(currentFill);

                await Awaitable.NextFrameAsync();
            }

            if (_unlockProgress != null)
            {
                _unlockProgress.fillAmount = targetFill;
            }

            SetUnlockText(targetFill);

            if (targetFill >= 1f)
            {
                if (_unlockProgressText != null)
                    _unlockProgressText.gameObject.SetActive(false);

                await AnimateUnlockProgressCompleteScaleAsync();
            }
            else if (_unlockProgressText != null)
            {
                _unlockProgressText.gameObject.SetActive(true);
            }
        }
    }

    private async Awaitable AnimateUnlockProgressCompleteScaleAsync()
    {
        if (_unlockProgress == null) return;

        const float duration = 0.2f;
        Vector3 startScale = _unlockProgress.transform.localScale;
        Vector3 targetScale = Vector3.one * 1.25f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (this == null || _unlockProgress == null) return;

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            _unlockProgress.transform.localScale = Vector3.Lerp(startScale, targetScale, t);
            await Awaitable.NextFrameAsync();
        }

        _nextFeaturePanel.SetActive(false);
        _unlockedPanel.SetActive(true);

        if (_unlockProgress != null)
            _unlockProgress.transform.localScale = targetScale;
    }
}
