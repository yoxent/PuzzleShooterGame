using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Resolves the current unlockable feature from the catalog based on level and exposes it for UI.
/// </summary>
public class Unlockables : MonoBehaviour
{
    private GameEventBus _gameEventBus;

    [SerializeField] private UnlockablesCatalog _unlockables;
    [SerializeField] private UnlockablesHighlightOverlay _overlay;

    [Header("UI")]
    [SerializeField] private GameObject _unlockablesPanel;
    [SerializeField] private TextMeshProUGUI _flavtorText;
    [SerializeField] private List<Image> _featureImages = new List<Image>();

    private UnlockableFeature _currentUnlockable;
    private bool _hasCurrentUnlockable;

    private void Start()
    {
        ServiceLocator.Register(this);
        _gameEventBus = ServiceLocator.Resolve<GameEventBus>();

        if (_gameEventBus != null)
        {
            _gameEventBus.LevelLoaded += UpdateCurrentLevel;
        }
    }

    private void OnDisable()
    {
        if (_gameEventBus != null)
        {
            _gameEventBus.LevelLoaded -= UpdateCurrentLevel;
        }
    }

    private void OnDestroy()
    {
        ServiceLocator.Unregister<Unlockables>();
    }

    private void UpdateCurrentLevel(int level)
    {
        _hasCurrentUnlockable = false;
        _currentUnlockable = default;

        if (_unlockables == null || _unlockables.Entries == null)
        {
            return;
        }

        foreach (UnlockableFeature entry in _unlockables.Entries)
        {
            if (level >= entry.LevelUnlockFeatureStart && (level <= entry.LevelUnlockFeatureEnd || level == entry.LevelFeatureShowcase))
            {
                _currentUnlockable = entry;
                _hasCurrentUnlockable = true;

                foreach (var img in _featureImages)
                {
                    if (img != null)
                    {
                        img.sprite = _currentUnlockable.UnlockableImage;
                    }
                }
                break;
            }
        }

        if (_hasCurrentUnlockable && level == _currentUnlockable.LevelFeatureShowcase)
        {
            _flavtorText.text = _currentUnlockable.FlavorText;
            _unlockablesPanel.SetActive(true);

            if (_overlay != null)
            {
                _overlay.SetVisible(true);
                if (_currentUnlockable.OverlaySettings.SpriteOverlay != null)
                    _overlay.SetMaskSprite(_currentUnlockable.OverlaySettings.SpriteOverlay);
                _overlay.ApplyOverlaySettings(_currentUnlockable.OverlaySettings);
            }
        }
    }

    /// <summary>True if the current level falls within an unlock range.</summary>
    public bool TryGetCurrentUnlockable(out UnlockableFeature feature)
    {
        feature = _currentUnlockable;
        return _hasCurrentUnlockable;
    }
}
