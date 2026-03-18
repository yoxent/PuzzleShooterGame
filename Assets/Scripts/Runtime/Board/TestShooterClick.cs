using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Test only: mouse click raycasts and "attacks" the block under the cursor, destroying it and triggering grid slide.
/// Simulates shooter attack for testing. Block prefabs must have a Collider for raycast to hit.
/// </summary>
public class TestShooterClick : MonoBehaviour
{
    [SerializeField] private Camera _camera;
    [SerializeField] private float _rayDistance = 100f;

    private BlockGrid _grid;
    private bool _hasLastHitPoint;
    private Vector3 _lastHitPoint;

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
        RaycastHit[] hits = Physics.RaycastAll(ray, _rayDistance);

        if (hits.Length == 0)
        {
            return;
        }

        Block block = null;
        RaycastHit hit = default;
        for (int i = 0; i < hits.Length; i++)
        {
            hit = hits[i];
            Block b = hit.collider.GetComponent<Block>() ?? hit.collider.GetComponentInParent<Block>();
            if (b != null && b.IsInFrontAndBottomTier() && !b.IsMoving)
            {
                block = b;
                break;
            }
        }

        _lastHitPoint = hit.point;
        _hasLastHitPoint = true;

        if (block == null)
        {
            return;
        }

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
