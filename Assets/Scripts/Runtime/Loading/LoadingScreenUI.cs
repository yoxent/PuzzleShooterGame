using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Utilities
{
    /// <summary>
    /// Controls a loading screen with a progress bar, background/logo, and rotating hint text.
    /// Expects LoadingScreenContext.NextSceneName to be set before this scene is loaded.
    /// </summary>
    public class LoadingScreenUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Image _progressFillImage;

        [Header("Behaviour")]
        [SerializeField] private float _minLoadingDuration = 2f;

        private void Start()
        {
            var targetScene = LoadingScreenContext.NextSceneName;
            if (string.IsNullOrEmpty(targetScene))
            {
                Debug.LogError("LoadingScreenUI: No target scene set in LoadingScreenContext.NextSceneName.");
                return;
            }

            _ = LoadSceneAsync(targetScene);
        }

        private async Awaitable LoadSceneAsync(string targetScene)
        {
            float elapsed = 0f;

            if (_progressFillImage != null)
            {
                _progressFillImage.fillAmount = 0f;
            }

            var op = SceneManager.LoadSceneAsync(targetScene, LoadSceneMode.Single);
            op.allowSceneActivation = false;

            while (!op.isDone)
            {
                elapsed += Time.unscaledDeltaTime;

                // Update progress: Unity reports up to 0.9f before activation.
                float rawProgress = Mathf.Clamp01(op.progress / 0.9f);

                // Time-based smoothing: use ~90% of the minimum duration to reach 100%.
                float durationForBar = _minLoadingDuration * 0.9f;
                float timeProgress = durationForBar > 0f
                    ? Mathf.Clamp01(elapsed / durationForBar)
                    : 1f;

                // Don't visually advance beyond the actual async progress.
                float visualProgress = Mathf.Min(rawProgress, timeProgress);

                if (_progressFillImage != null)
                {
                    _progressFillImage.fillAmount = visualProgress;
                }

                // When loading has reached 90% and minimum duration has passed, activate.
                if (op.progress >= 0.9f && elapsed >= _minLoadingDuration)
                {
                    op.allowSceneActivation = true;
                }

                await Awaitable.EndOfFrameAsync();
            }
        }
    }
}

