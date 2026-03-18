using UnityEngine;

/// <summary>
/// Subscribes to LevelManager and activates tutorial sequences for configured levels.
/// </summary>
public class TutorialManager : MonoBehaviour
{
    private LevelManager _levelManager;

    [SerializeField] private TutorialCatalog _levelTutorials;
    [SerializeField] private TutorialUIController _tutorialUI;
    [SerializeField] private ShooterContainer _shooterContainer;

    private void Start()
    {
        _levelManager = ServiceLocator.Resolve<LevelManager>();

        if (_levelManager != null)
        {
            _levelManager.LevelLoaded += OnLevelLoaded;
        }
    }

    private void OnDisable()
    {
        if (_levelManager != null)
        {
            _levelManager.LevelLoaded -= OnLevelLoaded;
        }
    }

    private void OnLevelLoaded(LevelBlockSetup setup)
    {
        if (setup == null || _levelTutorials == null || _levelTutorials.Entries == null || _levelTutorials.Entries.Length == 0)
            return;

        var levelManager = _levelManager ?? ServiceLocator.Resolve<LevelManager>();
        if (levelManager == null)
            return;

        int currentIndex = levelManager.CurrentLevelIndex;
        var entries = _levelTutorials.Entries;

        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            if (entry.LevelIndex == currentIndex)
            {
                ActivateTutorial(entry.Tutorial);
                break;
            }
        }
    }

    private void ActivateTutorial(TutorialData data)
    {
        if (_tutorialUI == null) return;

        _tutorialUI.DisplayTutorial(data);
    }
}
