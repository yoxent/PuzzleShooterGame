using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Simple wrapper around Unity's async scene loading APIs.
/// Register this in the scene and resolve via ServiceLocator when needed.
/// </summary>
public class SceneLoader : MonoBehaviour
{
    private void Awake()
    {
        ServiceLocator.Register(this);
    }

    private void OnDestroy()
    {
        ServiceLocator.Unregister<SceneLoader>();
    }

    public void LoadScene(string sceneName) => _ = LoadSceneAsync(sceneName);

    public void LoadSceneWithLoading(string nextScene) => _ = LoadSceneWithLoadingAsync(nextScene);

    public async Awaitable LoadSceneAsync(string sceneName, LoadSceneMode mode = LoadSceneMode.Single)
    {
        if (string.IsNullOrEmpty(sceneName)) return;

        await SceneManager.LoadSceneAsync(sceneName, mode);
    }

    public async Awaitable LoadSceneWithLoadingAsync(string nextScene, LoadSceneMode mode = LoadSceneMode.Single)
    {
        LoadingScreenContext.NextSceneName = nextScene;
        await LoadSceneAsync("Loading", mode);
    }
}