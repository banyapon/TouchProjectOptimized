using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneManageClass : MonoBehaviour
{
    public static SceneManageClass Instance { get; private set; }

    private GameObject loadingUIPrefab;
    private GameObject loadingUIInstance;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); 
            
            loadingUIPrefab = Resources.Load<GameObject>("ui/loading");
            if (loadingUIPrefab == null)
            {
                Debug.LogError("Loading UI prefab not found at 'Resources/ui/loading'.");
            }
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }
    }

    private void OnEnable()
    {
        // Subscribe to the sceneLoaded event
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        // Unsubscribe from the sceneLoaded event
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    /// <summary>
    /// This method is called every time a scene finishes loading.
    /// </summary>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Destroy the loading UI instance as the first action when a new scene loads.
        if (loadingUIInstance != null)
        {
            Destroy(loadingUIInstance);
        }

        // Check if the loaded scene is the "Title" scene
        if (scene.name == "Title")
        {
            // Find the GameObject named "CanvasMainUI"
            GameObject mainUICanvas = GameObject.Find("CanvasMainUI");
            if (mainUICanvas != null)
            {
                // Destroy the GameObject if it was found
                Destroy(mainUICanvas);
            }
        }
    }

    public void LoadSceneAsync(string sceneName)
    {
        if (loadingUIPrefab != null)
        {
            StartCoroutine(LoadSceneCoroutine(sceneName));
        }
        else
        {
            Debug.LogError("Cannot load scene because the loading UI is not available.");
        }
    }

    private IEnumerator LoadSceneCoroutine(string sceneName)
    {
        // Instantiate the loading UI
        loadingUIInstance = Instantiate(loadingUIPrefab);
        DontDestroyOnLoad(loadingUIInstance); // Ensure it persists during scene change

        // Start loading the scene asynchronously
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);

        // Don't activate the new scene immediately
        asyncLoad.allowSceneActivation = false;

        // Wait until the scene is almost fully loaded
        while (asyncLoad.progress < 0.9f)
        {
            yield return null;
        }

        // The scene is loaded, now allow activation.
        // The loading screen will become hidden by the new scene, then destroyed.
        asyncLoad.allowSceneActivation = true;

        // The rest of the cleanup is now handled by OnSceneLoaded
        yield return null;
    }
}
