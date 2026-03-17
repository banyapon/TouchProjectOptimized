using UnityEngine;

public class SwitchCamera : MonoBehaviour
{
    [Header("Camera References")]
    [SerializeField] private GameObject FPS_Camera;
    [SerializeField] private GameObject TPS_Camera;

    [Header("State")]
    [SerializeField] private bool isFPSCameraActive = true;

    private void Start()
    {
        ApplyCameraState();
    }

    public void ToggleCamera()
    {
        isFPSCameraActive = !isFPSCameraActive;
        ApplyCameraState();
    }

    public void SetCameraMode(bool useFPSCamera)
    {
        isFPSCameraActive = useFPSCamera;
        ApplyCameraState();
    }

    private void ApplyCameraState()
    {
        if (FPS_Camera != null)
            FPS_Camera.SetActive(isFPSCameraActive);

        if (TPS_Camera != null)
            TPS_Camera.SetActive(!isFPSCameraActive);
    }
}
