using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// Object pool for <see cref="Projectile"/> instances. Assign Active and Inactive transforms for clear separation. Instance names get an index suffix (e.g. Projectile_0) for debugging.
/// </summary>
public class ProjectilePool : MonoBehaviour
{
    [SerializeField] private Projectile _projectilePrefab;
    [SerializeField] private Transform _activeParent;
    [SerializeField] private Transform _inactiveParent;
    [SerializeField] private int _defaultCapacity = 32;
    [SerializeField] private int _maxSize = 64;
    [Tooltip("Number of projectiles to create and return to the pool at startup.")]
    [SerializeField] private int _prewarmCount = 16;

    private ObjectPool<Projectile> _pool;
    private int _createIndex;

    /// <summary>Whether the pool is initialized and has a valid prefab.</summary>
    public bool IsReady => _pool != null && _projectilePrefab != null;

    private void Awake()
    {
        ServiceLocator.Register(this);
        if (_projectilePrefab == null) return;

        _pool = new ObjectPool<Projectile>(
            createFunc: () =>
            {
                var p = Instantiate(_projectilePrefab, _inactiveParent);
                p.gameObject.name = $"{_projectilePrefab.name}_{_createIndex++}";
                return p;
            },
            actionOnGet: p =>
            {
                p.transform.SetParent(_activeParent);
                p.gameObject.SetActive(true);
                p.SetPool(this);
            },
            actionOnRelease: p =>
            {
                p.transform.SetParent(_inactiveParent);
                p.transform.localPosition = Vector3.zero;
                p.transform.localRotation = Quaternion.identity;
                p.gameObject.SetActive(false);
            },
            actionOnDestroy: p => { if (p != null) Destroy(p.gameObject); },
            collectionCheck: true,
            defaultCapacity: _defaultCapacity,
            maxSize: _maxSize
        );
        Prewarm();
    }

    private void Prewarm()
    {
        if (_pool == null || _prewarmCount <= 0) return;
        int count = Mathf.Min(_prewarmCount, _maxSize);
        var temp = new Projectile[count];
        for (int i = 0; i < count; i++)
            temp[i] = _pool.Get();
        for (int i = 0; i < count; i++)
            _pool.Release(temp[i]);
    }

    private void OnDestroy()
    {
        ServiceLocator.Unregister<ProjectilePool>();
    }

    /// <summary>Get a projectile from the pool. Returns null if pool or prefab is not set.</summary>
    public Projectile Get() => _pool != null ? _pool.Get() : null;

    /// <summary>Return a projectile to the pool.</summary>
    public void Release(Projectile projectile)
    {
        if (projectile != null && _pool != null)
            _pool.Release(projectile);
    }
}
