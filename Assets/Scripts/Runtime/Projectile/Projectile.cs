using System;
using UnityEngine;

/// <summary>
/// Pooled projectile. Moves in a direction at a set speed until it hits a collider.
/// On hit, notifies the block (if any) for destruction and returns to the pool. No damage value — all hits destroy the block.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class Projectile : MonoBehaviour
{
    private Vector3 _direction;
    private float _speed;
    private bool _launched;
    private bool _lerpMode;
    private Vector3 _lerpFrom;
    private Vector3 _lerpTo;
    private float _lerpDuration;
    private float _lerpElapsed;
    private Block _lerpTargetBlock;
    private Rigidbody _rigidbody;
    private ProjectilePool _pool;
    private Action<Block> _onHitBlock;
    private Action _onStopped;

    private void Awake()
    {
        TryGetComponent(out _rigidbody);
        if (_rigidbody != null)
        {
            _rigidbody.isKinematic = true;
            _rigidbody.useGravity = false;
        }
        if (TryGetComponent(out Collider col))
            col.isTrigger = true;
    }

    /// <summary>Set the pool to return to when stopped. Called by ProjectilePool on Get.</summary>
    public void SetPool(ProjectilePool pool) => _pool = pool;

    /// <summary>Launch in direction at speed. On hit, onHitBlock is invoked with the Block (if any), then onStopped, then this is returned to the pool.</summary>
    public void Launch(Vector3 direction, float speed, Action<Block> onHitBlock, Action onStopped)
    {
        _lerpMode = false;
        _direction = direction.normalized;
        _speed = Mathf.Max(0f, speed);
        _onHitBlock = onHitBlock;
        _onStopped = onStopped;
        _launched = true;
    }

    /// <summary>Launch and lerp from start to end over duration. On completion, onHitBlock(targetBlock) and onStopped are invoked, then returned to pool.</summary>
    public void LaunchToward(Vector3 from, Vector3 to, Block targetBlock, float duration, Action<Block> onHitBlock, Action onStopped)
    {
        _lerpMode = true;
        _lerpFrom = from;
        _lerpTo = to;
        _lerpDuration = Mathf.Max(0.001f, duration);
        _lerpElapsed = 0f;
        _lerpTargetBlock = targetBlock;
        _onHitBlock = onHitBlock;
        _onStopped = onStopped;
        _launched = true;
        if (_rigidbody != null)
            _rigidbody.position = from;
        else
            transform.position = from;
    }

    private void FixedUpdate()
    {
        if (!_launched) return;

        if (_lerpMode)
        {
            _lerpElapsed += Time.fixedDeltaTime;
            float t = Mathf.Clamp01(_lerpElapsed / _lerpDuration);
            Vector3 pos = Vector3.Lerp(_lerpFrom, _lerpTo, t);
            if (_rigidbody != null)
                _rigidbody.MovePosition(pos);
            else
                transform.position = pos;
            if (t >= 1f)
            {
                _onHitBlock?.Invoke(_lerpTargetBlock);
                StopAndReturnToPool();
            }
            return;
        }

        if (_speed <= 0f) return;
        if (_rigidbody != null)
            _rigidbody.MovePosition(_rigidbody.position + _direction * (_speed * Time.fixedDeltaTime));
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!_launched || _lerpMode) return;
        Block block = other.GetComponent<Block>() ?? other.GetComponentInParent<Block>();
        if (block != null)
            _onHitBlock?.Invoke(block);
        StopAndReturnToPool();
    }

    private void OnCollisionEnter(Collision _)
    {
        if (!_launched) return;
        StopAndReturnToPool();
    }

    private void StopAndReturnToPool()
    {
        _launched = false;
        _lerpMode = false;
        _lerpTargetBlock = null;
        _onHitBlock = null;
        _onStopped?.Invoke();
        _onStopped = null;
        if (_pool != null)
            _pool.Release(this);
        else
            gameObject.SetActive(false);
    }
}
