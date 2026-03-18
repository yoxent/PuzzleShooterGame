using UnityEngine;

/// <summary>
/// Holds references to up to 5 platform transforms for shooters. Enables a set number at start and spaces them evenly along X (Y and Z stay 0).
/// </summary>
public class ShooterPlatforms : MonoBehaviour
{
    private const int PlatformCount = 5;

    [Header("Platforms")]
    [Tooltip("Platform transforms for shooters (order determines which are enabled first).")]
    [SerializeField] private Transform[] _platforms = new Transform[PlatformCount];

    [Header("Active count")]
    [Tooltip("How many platforms are enabled at start (1–5). Remaining are disabled.")]
    [SerializeField][Range(1, PlatformCount)] private int _activeCount = 3;

    private LevelManager _levelManager;

    /// <summary>Number of platforms currently enabled (1–5).</summary>
    public int ActiveCount => Mathf.Clamp(_activeCount, 0, _platforms != null ? _platforms.Length : 0);

    /// <summary>Platform transform at index (0 = leftmost). Returns null if out of range.</summary>
    public Transform GetPlatform(int index)
    {
        if (_platforms == null || index < 0 || index >= _platforms.Length) return null;
        return _platforms[index];
    }

    /// <summary>Set how many platforms are enabled (1–5) and reapply spacing. Call when loading a level.</summary>
    public void SetActiveCount(int count)
    {
        _activeCount = Mathf.Clamp(count, 1, PlatformCount);
        ApplyActiveCountAndSpacing();
    }

    private void Awake()
    {
        ServiceLocator.Register(this);
    }

    private void Start()
    {
        _levelManager = ServiceLocator.Resolve<LevelManager>();

        if (_levelManager != null)
        {
            _levelManager.LevelLoaded += OnLevelLoaded;
        }

        ApplyActiveCountAndSpacing();
    }

    private void OnDisable()
    {
        if (_levelManager != null)
        {
            _levelManager.LevelLoaded -= OnLevelLoaded;
        }
    }

    private void OnDestroy()
    {
        if (_levelManager != null)
        {
            _levelManager.LevelLoaded -= OnLevelLoaded;
        }

        ServiceLocator.Unregister<ShooterPlatforms>();
    }

    private void OnLevelLoaded(LevelBlockSetup level)
    {
        if (level != null)
            SetActiveCount(level.ShooterPlatformActiveCount);
    }

    /// <summary>Enable the first activeCount platforms, disable the rest, and space active ones along X using fixed step positions.</summary>
    public void ApplyActiveCountAndSpacing()
    {
        if (_platforms == null) return;

        int count = Mathf.Clamp(_activeCount, 0, _platforms.Length);
        int activeIndex = 0;

        for (int i = 0; i < _platforms.Length; i++)
        {
            Transform t = _platforms[i];
            if (t == null) continue;

            bool enable = activeIndex < count;
            t.gameObject.SetActive(enable);

            if (enable)
            {
                const float step = 2f;
                float x = Helper.ComputeSymmetricStepX(activeIndex, count, step);
                Vector3 pos = t.localPosition;
                pos.x = x;
                pos.y = 0f;
                pos.z = 0f;
                t.localPosition = pos;
                activeIndex++;
            }
        }
    }
}
