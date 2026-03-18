using UnityEngine;

/// <summary>
/// Moves in a direction until it hits a collider (block or boundary), then notifies and stops.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class ProjectileController : MonoBehaviour
{
    private Vector3 _direction;
    private float _speed;
    private bool _launched;
    private Rigidbody _rigidbody;

    /// <summary>Raised when this projectile hits something and stops.</summary>
    public event System.Action<ProjectileController> OnStopped;

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

    /// <summary>Start moving in the given direction at the given speed.</summary>
    public void Launch(Vector3 direction, float speed)
    {
        _direction = direction.normalized;
        _speed = Mathf.Max(0f, speed);
        _launched = true;
    }

    private void FixedUpdate()
    {
        if (!_launched || _speed <= 0f) return;
        _rigidbody.MovePosition(_rigidbody.position + _direction * (_speed * Time.fixedDeltaTime));
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!_launched) return;
        StopProjectile();
    }

    private void OnCollisionEnter(Collision _)
    {
        if (!_launched) return;
        StopProjectile();
    }

    private void StopProjectile()
    {
        _launched = false;
        OnStopped?.Invoke(this);
        gameObject.SetActive(false);
    }
}
