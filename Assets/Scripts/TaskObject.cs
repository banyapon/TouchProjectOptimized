using System.Collections;
using TMPro;
using UnityEngine;

public class TaskObject : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private TMP_Text countdownText;
    [SerializeField] private Transform player;
    [SerializeField] private string playerTag = "Player";

    [Header("Center Check")]
    [SerializeField] private float centerRadius = 0.35f;
    [SerializeField] private float centerHeightTolerance = 1.0f;

    [Header("Visual")]
    [SerializeField] private Color idleColor = new Color(0.2f, 1f, 0.2f, 0.35f);
    [SerializeField] private Color activeColor = new Color(0.3f, 1f, 1f, 0.45f);

    [Header("Timing")]
    [SerializeField] private float countdownDuration = 1.0f;
    [SerializeField] private float doneVisibleDuration = 1.0f;
    [SerializeField] private bool disableAfterDone = true;

    private Material runtimeMaterial;
    private bool completed;
    private float remainingTime;

    private void Awake()
    {
        if (targetRenderer == null)
            targetRenderer = GetComponent<Renderer>();

        if (targetRenderer != null)
        {
            runtimeMaterial = targetRenderer.material;
            SetupTransparent(runtimeMaterial);
            SetMaterialColor(idleColor);
        }

        if (countdownText != null)
        {
            countdownText.text = string.Empty;
            countdownText.color = activeColor;
        }

        remainingTime = Mathf.Max(0.01f, countdownDuration);
    }

    private void Start()
    {
        if (player == null && !string.IsNullOrEmpty(playerTag))
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag(playerTag);
            if (playerObj != null) player = playerObj.transform;
        }
    }

    private bool IsPlayerCentered()
    {
        if (player == null) return false;
        Vector3 local = transform.InverseTransformPoint(player.position);
        float horizontal = new Vector2(local.x, local.z).magnitude;
        return horizontal <= centerRadius && Mathf.Abs(local.y) <= centerHeightTolerance;
    }

    private bool IsPlayerCollider(Collider other)
    {
        if (other == null) return false;
        if (!string.IsNullOrEmpty(playerTag) && other.CompareTag(playerTag)) return true;
        if (player == null) return false;
        return other.transform == player || other.transform.IsChildOf(player);
    }

    private void OnTriggerStay(Collider other)
    {
        if (completed || !IsPlayerCollider(other)) return;
        if (player == null) player = other.transform;

        if (!IsPlayerCentered())
        {
            SetMaterialColor(idleColor);
            return;
        }

        SetMaterialColor(activeColor);
        remainingTime -= Time.fixedDeltaTime;
        if (countdownText != null)
        {
            countdownText.color = activeColor;
            countdownText.text = $"{Mathf.Max(0f, remainingTime):0.0}s";
        }

        if (remainingTime <= 0f)
            CompleteTask();
    }

    private void OnTriggerExit(Collider other)
    {
        if (completed || !IsPlayerCollider(other)) return;
        SetMaterialColor(idleColor);
    }

    private void CompleteTask()
    {
        if (completed) return;
        completed = true;

        if (countdownText != null)
        {
            countdownText.color = activeColor;
            countdownText.text = "Done";
        }

        StartCoroutine(FinalizeAfterDoneDelay());
    }

    private IEnumerator FinalizeAfterDoneDelay()
    {
        yield return new WaitForSeconds(Mathf.Max(0f, doneVisibleDuration));

        if (disableAfterDone)
        {
            gameObject.SetActive(false);
        }
        else
        {
            if (targetRenderer != null) targetRenderer.enabled = false;
            if (countdownText != null) countdownText.enabled = false;
        }
    }

    private void SetMaterialColor(Color c)
    {
        if (runtimeMaterial != null) runtimeMaterial.color = c;
    }

    private static void SetupTransparent(Material mat)
    {
        if (mat == null) return;

        if (mat.HasProperty("_Surface"))
        {
            mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return;
        }

        if (mat.HasProperty("_Mode"))
        {
            mat.SetFloat("_Mode", 3f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
    }
    
}
