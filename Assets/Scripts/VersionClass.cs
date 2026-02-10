using UnityEngine;
using TMPro;

/// <summary>
/// Updates a TextMeshPro component to display the application's build version.
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public class VersionClass : MonoBehaviour
{
    private TMP_Text versionText;

    void Start()
    {
        // Get the TMP_Text component attached to this GameObject
        versionText = GetComponent<TMP_Text>();
        
        // Set the text to display the application version
        // Application.version can be set in Project Settings > Player > Version
        if (versionText != null)
        {
            versionText.text = "version: " + Application.version;
        }
        else
        {
            Debug.LogError("VersionClass requires a TMP_Text component on the same GameObject.");
        }
    }
}
