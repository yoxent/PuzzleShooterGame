using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared gameplay math and interpolation helpers (spacing, lerps, etc.).
/// Keep domain-agnostic math here so multiple systems can reuse it.
/// </summary>
public static class Helper
{
    /// <summary>
    /// Standard smoothstep easing for t in [0..1].
    /// Keeps animation math consistent across systems.
    /// </summary>
    public static float SmoothStep01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    /// <summary>
    /// Compute a symmetric X position with fixed step for a given index/count.
    /// Odd count: centered on 0 (e.g. 1 -> [0], 3 -> [-2,0,2], 5 -> [-4,-2,0,2,4]).
    /// Even count: skips 0 and mirrors around it (e.g. 2 -> [-2,2], 4 -> [-4,-2,2,4]).
    /// </summary>
    public static float ComputeSymmetricStepX(int index, int count, float step)
    {
        if (count <= 0) return 0f;
        if (count == 1) return 0f;

        if (count % 2 == 1)
        {
            int mid = count / 2;
            float j = index - mid;
            return j * step;
        }
        else
        {
            int half = count / 2;
            float j = index < half ? index - half : index - half + 1;
            return j * step;
        }
    }

    /// <summary>
    /// Lerp a transform's local position from start to end over duration using smoothstep easing.
    /// </summary>
    public static async Awaitable LerpLocalPositionSmoothStepAsync(
        Transform transform,
        Vector3 startLocal,
        Vector3 endLocal,
        float duration,
        Func<bool> shouldCancel)
    {
        if (transform == null) return;
        if (duration <= 0f)
        {
            if (transform != null)
                transform.localPosition = endLocal;
            return;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (shouldCancel != null && shouldCancel())
                return;

            elapsed += Time.deltaTime;
            float t = SmoothStep01(elapsed / duration);
            if (transform != null)
                transform.localPosition = Vector3.Lerp(startLocal, endLocal, t);
            await Awaitable.NextFrameAsync();
        }

        if (transform != null)
            transform.localPosition = endLocal;
    }

    /// <summary>
    /// Lerp only the local Z component for a set of transforms from their start positions to target positions over duration.
    /// Keeps X/Y from the cached starts and applies linear interpolation on Z.
    /// </summary>
    public static async Awaitable LerpLocalZLinearAsync(
        IReadOnlyList<Transform> transforms,
        IReadOnlyList<Vector3> starts,
        IReadOnlyList<Vector3> targets,
        float duration,
        Func<bool> shouldCancel)
    {
        if (transforms == null) return;
        int count = transforms.Count;
        if (count == 0) return;
        if (starts == null || targets == null) return;
        if (starts.Count != count || targets.Count != count) return;

        if (duration <= 0f)
        {
            for (int i = 0; i < count; i++)
            {
                Transform t = transforms[i];
                if (t == null) continue;
                t.localPosition = targets[i];
                t.localRotation = Quaternion.identity;
            }
            return;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (shouldCancel != null && shouldCancel())
                return;

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            for (int i = 0; i < count; i++)
            {
                Transform tr = transforms[i];
                if (tr == null) continue;
                Vector3 start = starts[i];
                Vector3 target = targets[i];
                tr.localPosition = new Vector3(start.x, start.y, Mathf.Lerp(start.z, target.z, t));
            }
            await Awaitable.NextFrameAsync();
        }

        for (int i = 0; i < count; i++)
        {
            Transform tr = transforms[i];
            if (tr == null) continue;
            tr.localPosition = targets[i];
            tr.localRotation = Quaternion.identity;
        }
    }
}

