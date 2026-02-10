using UnityEngine;
using UnityEngine.SceneManagement;
public class SceneLoadClass : MonoBehaviour
{
    public string sceneName;
    public void LoadScene(string sceneName)
    {
        SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
    }
}
