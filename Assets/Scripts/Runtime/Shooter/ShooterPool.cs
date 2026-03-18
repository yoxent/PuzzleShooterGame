using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// Object pool for <see cref="Shooter"/> instances. When a shooter runs out of projectiles it goes offscreen then returns here.
/// </summary>
public class ShooterPool : MonoBehaviour
{
    [SerializeField] private Shooter _shooterPrefab;
    [SerializeField] private Transform _activeParent;
    [SerializeField] private Transform _inactiveParent;
    [Tooltip("Default projectile count assigned when getting a shooter without specifying count.")]
    [SerializeField] private int _defaultProjectileCount = 10;
    [SerializeField] private int _defaultCapacity = 8;
    [SerializeField] private int _maxSize = 16;

    private ObjectPool<Shooter> _pool;

    /// <summary>Whether the pool is initialized and has a valid prefab.</summary>
    public bool IsReady => _pool != null && _shooterPrefab != null;

    private void Awake()
    {
        ServiceLocator.Register(this);
        if (_shooterPrefab == null) return;

        _pool = new ObjectPool<Shooter>(
            createFunc: () =>
            {
                var s = Instantiate(_shooterPrefab, _inactiveParent);
                return s;
            },
            actionOnGet: s =>
            {
                s.transform.SetParent(_activeParent);
                s.gameObject.SetActive(true);
                s.SetPool(this);
            },
            actionOnRelease: s =>
            {
                s.transform.SetParent(_inactiveParent);
                s.transform.localPosition = Vector3.zero;
                s.gameObject.SetActive(false);
            },
            actionOnDestroy: s => { if (s != null) Destroy(s.gameObject); },
            collectionCheck: true,
            defaultCapacity: _defaultCapacity,
            maxSize: _maxSize
        );
    }

    private void OnDestroy()
    {
        ServiceLocator.Unregister<ShooterPool>();
    }

    /// <summary>Get a shooter from the pool with the given projectile count. Returns null if pool or prefab is not set.</summary>
    public Shooter Get(int projectileCount)
    {
        if (_pool == null) return null;
        Shooter s = _pool.Get();
        if (s != null)
            s.SetProjectileCount(projectileCount);
        return s;
    }

    /// <summary>Get a shooter from the pool with projectile count and color entry (only targets blocks of this color).</summary>
    public Shooter Get(int projectileCount, ShooterColorEntry entry)
    {
        Shooter s = Get(projectileCount);
        if (s != null && entry.Color != null)
            s.SetShooterData(entry);
        return s;
    }

    /// <summary>Get a shooter from the pool with the default projectile count.</summary>
    public Shooter Get() => Get(_defaultProjectileCount);

    /// <summary>Return a shooter to the pool.</summary>
    public void Release(Shooter shooter)
    {
        if (shooter != null && _pool != null)
            _pool.Release(shooter);
    }
}
