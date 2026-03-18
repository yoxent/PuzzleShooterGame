using UnityEngine;

/// <summary>
/// Tiny helper for collider->component lookups.
/// Checks the collider first, then walks up parents.
/// </summary>
public static class ColliderLookup
{
    public static T FindInSelfOrParents<T>(Collider collider) where T : Component
    {
        if (collider == null)
            return null;

        if (collider.TryGetComponent(out T component))
            return component;

        return collider.GetComponentInParent<T>();
    }
}
