using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Quick debug helper: click a block to destroy it and trigger board slide.
/// Handy when testing board behavior without shooter logic.
/// </summary>
public class TestShooterClick : MonoBehaviour
{
    [SerializeField] private Camera _camera;
    [SerializeField] private float _rayDistance = 100f;

    private BlockGrid _grid;
    private bool _hasLastHitPoint;
    private Vector3 _lastHitPoint;
    private readonly RaycastHit[] _raycastHits = new RaycastHit[24];

    private void Start()
    {
        if (_camera == null)
        {
            _camera = Camera.main;
        }

        _grid = ServiceLocator.Resolve<BlockGrid>();
    }

    private void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            TryAttackBlockUnderCursor();
        }
    }

    private void TryAttackBlockUnderCursor()
    {
        if (_camera == null || _grid == null)
        {
            return;
        }

        Ray ray = _camera.ScreenPointToRay(Input.mousePosition);
        int hitCount = Physics.RaycastNonAlloc(ray, _raycastHits, _rayDistance);

        if (hitCount <= 0)
        {
            return;
        }

        Block block = null;
        float bestDistance = float.MaxValue;
        Vector3 bestHitPoint = Vector3.zero;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _raycastHits[i];
            Block candidate = ColliderLookup.FindInSelfOrParents<Block>(hit.collider);
            if (candidate == null || !candidate.IsInFrontAndBottomTier() || candidate.IsMoving)
                continue;
            if (hit.distance >= bestDistance)
                continue;

            bestDistance = hit.distance;
            bestHitPoint = hit.point;
            block = candidate;
        }

        if (block == null)
        {
            return;
        }

        _lastHitPoint = bestHitPoint;
        _hasLastHitPoint = true;
        _grid.DestroyBlocksAndSlide(new List<Block> { block });
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!_hasLastHitPoint) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(_lastHitPoint, 0.25f);
    }
#endif
}
