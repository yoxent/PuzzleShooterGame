using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// Object pool for <see cref="Block"/> instances. Assign Active and Inactive transforms in the hierarchy for clear separation.
/// </summary>
public class BlockPool : MonoBehaviour
{
    [SerializeField] private Block _blockPrefab;
    [SerializeField] private Transform _activeParent;
    [SerializeField] private Transform _inactiveParent;
    [Tooltip("Initial number of instances to create when the pool is first used.")]
    [SerializeField] private int _defaultCapacity = 64;
    [Tooltip("Maximum instances to keep when released. Excess are destroyed.")]
    [SerializeField] private int _maxSize = 128;

    private ObjectPool<Block> _pool;

    /// <summary>Whether the pool is initialized and has a valid prefab.</summary>
    public bool IsReady => _pool != null && _blockPrefab != null;

    private void Awake()
    {
        ServiceLocator.Register(this);
        if (_blockPrefab == null) return;

        _pool = new ObjectPool<Block>(
            createFunc: () =>
            {
                var block = Instantiate(_blockPrefab, _inactiveParent);
                return block;
            },
            actionOnGet: b =>
            {
                b.transform.SetParent(_activeParent);
                b.gameObject.SetActive(true);
            },
            actionOnRelease: b =>
            {
                b.transform.SetParent(_inactiveParent);
                b.transform.localPosition = Vector3.zero;
                b.gameObject.SetActive(false);
            },
            actionOnDestroy: b => { if (b != null) Destroy(b.gameObject); },
            collectionCheck: true,
            defaultCapacity: _defaultCapacity,
            maxSize: _maxSize
        );
    }

    private void OnDestroy()
    {
        ServiceLocator.Unregister<BlockPool>();
    }

    /// <summary>Get a block from the pool. Returns null if pool or prefab is not set.</summary>
    public Block Get()
    {
        return _pool != null ? _pool.Get() : null;
    }

    /// <summary>Return a block to the pool.</summary>
    public void Release(Block block)
    {
        if (block != null && _pool != null)
            _pool.Release(block);
    }
}
