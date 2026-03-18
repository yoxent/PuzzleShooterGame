using System;
using System.Collections.Generic;

/// <summary>
/// Central service locator for resolving cross-system dependencies without direct references.
/// Register services in Awake (e.g. on a bootstrap or manager); resolve from any class by type or interface.
/// Use interfaces (e.g. ILevelManager) when registering so callers depend on contracts, not concrete types.
/// Does not create instances — registration is explicit.
/// </summary>
public static class ServiceLocator
{
    private static readonly Dictionary<Type, object> _services = new();

    /// <summary>Register a service by type T (use T = interface to decouple callers). Overwrites existing registration for that type.</summary>
    public static void Register<T>(T instance) where T : class
    {
        _services[typeof(T)] = instance ?? throw new ArgumentNullException(nameof(instance));
    }

    /// <summary>Unregister a service by type. No-op if not registered.</summary>
    public static void Unregister<T>() where T : class
    {
        _services.Remove(typeof(T));
    }

    /// <summary>Resolve a service. Returns null if not registered.</summary>
    public static T Resolve<T>() where T : class
    {
        return _services.TryGetValue(typeof(T), out var obj) ? obj as T : null;
    }

    /// <summary>Resolve a service. Returns true if registered and assigns the instance.</summary>
    public static bool TryResolve<T>(out T instance) where T : class
    {
        instance = Resolve<T>();
        return instance != null;
    }

    /// <summary>Remove all registrations. Use in tests or scene teardown to avoid stale references.</summary>
    public static void Clear()
    {
        _services.Clear();
    }
}
