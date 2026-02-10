using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public enum DragnGoMode
{
    TwoFingerRotate,    // 1 นิ้ว ลากเดิน, 2 นิ้ว หมุน (default)
    OneFingerRotate     // 1 นิ้ว ลากหมุน Realtime, Double Tap เดิน
}

public class DragnGo : MonoBehaviour
{
    [Header("References")]
    public Transform player;                 // Player root we move
    public Camera vrCamera;                  // Camera used for raycast direction
    public LineRenderer laserPointer;        // Visual line for the ray
    public LayerMask raycastLayers;          // Layers the ray can hit

    [Header("Mode Settings")]
    public Toggle modeToggle;                // Toggle UI สำหรับสลับโหมด
    public DragnGoMode currentMode = DragnGoMode.TwoFingerRotate;

    [Header("Movement Settings")]
    public Slider speedSlider;               // Slider สำหรับปรับ moveSpeed
    public float moveSpeed = 5f;             // Movement speed toward targets
    public float maxRayDistance = 100f;      // Fallback distance if no hit
    public float maxSwipePixels = 400f;      // Drag distance (pixels) that reaches full ray distance
    public float stopBeforeEnd = 0.15f;      // ระยะที่หยุดก่อนถึงปลาย Ray

    [Header("Rotation Settings")]
    public float rotationSpeed = 5f;         // Two-finger rotation sensitivity
    public bool invertTwoFingerRotation = true;
    public bool allowPitch = false;          // Allow up/down rotation during two-finger gesture
    public float pitchMin = -60f;
    public float pitchMax = 60f;
    public float dragThreshold = 10f;        // ระยะลากขั้นต่ำก่อนเริ่มหมุน

    [Header("Double Tap Settings")]
    public float doubleTapMaxInterval = 0.3f;  // เวลาสูงสุดระหว่าง tap (วินาที)
    public float tapDragThreshold = 20f;       // ระยะ pixel ที่ถือว่าเป็น tap ไม่ใช่ drag

    [Header("Swipe Strafe Settings")]
    public float swipeMoveDistance = 0.6f;       // ระยะเคลื่อนที่ซ้าย/ขวาต่อ swipe
    public float swipeMinPixels = 50f;           // ระยะ pixel ขั้นต่ำ horizontal ที่ถือว่าเป็น swipe
    public float swipeMaxDuration = 0.4f;        // เวลาสูงสุดที่ถือว่าเป็น swipe (วินาที)

    [Header("Hit Circle Settings")]
    public float hitCircleRadius = 0.3f;     // รัศมีของ circle ที่จุดกระทบ

    // UI touch blocking
    private bool touchStartedOnUI = false;

    // Movement state
    private bool isRotating = false;
    private bool isDragging = false;
    private Vector3 originalVEPosition;
    private Vector3 raycastTarget;
    private float currentHitDistance;
    private float dragMaxDistance;
    private bool hasDragLimit = false;
    private bool hasValidHit = false;
    private float touchStartY;
    private Coroutine moveRoutine;

    // Rotation state
    private Vector2 previousTouchMidpoint;
    private Quaternion targetRotation;
    private bool smoothRotation = false;

    // Double Tap Detection (OneFingerRotate mode)
    private float lastTapTime = -1f;
    private Vector2 lastTapPosition;
    private Vector2 touchStartPosition;
    private float touchStartTime;

    // One-finger rotation (OneFingerRotate mode)
    private bool isOneFingerRotating = false;
    private Vector2 previousOneFingerPosition;

    // Hit circle
    private GameObject hitCircle;
    private LineRenderer circleRenderer;

    void Start()
    {
        // เชื่อม Slider กับ moveSpeed
        if (speedSlider != null)
        {
            speedSlider.value = moveSpeed;
            speedSlider.onValueChanged.AddListener(OnSpeedSliderChanged);
        }

        // เชื่อม Toggle กับโหมด
        if (modeToggle != null)
        {
            modeToggle.isOn = (currentMode == DragnGoMode.OneFingerRotate);
            modeToggle.onValueChanged.AddListener(OnModeToggleChanged);
        }

        // ลดความกว้างของ LineRenderer
        if (laserPointer != null)
        {
            Color startColor = laserPointer.startColor;
            Color endColor = laserPointer.endColor;
            startColor.a = 0.5f;
            endColor.a = 0.5f;
            laserPointer.startColor = startColor;
            laserPointer.endColor = endColor;

            laserPointer.startWidth *= 0.5f;
            laserPointer.endWidth *= 0.5f;
        }

        CreateHitCircle();

        if (vrCamera != null)
            targetRotation = vrCamera.transform.localRotation;
    }

    void OnSpeedSliderChanged(float value)
    {
        moveSpeed = value;
    }

    void OnModeToggleChanged(bool isOn)
    {
        currentMode = isOn ? DragnGoMode.OneFingerRotate : DragnGoMode.TwoFingerRotate;
        // Reset state เมื่อสลับโหมด
        isOneFingerRotating = false;
        isRotating = false;
        isDragging = false;
        lastTapTime = -1f;
    }

    void CreateHitCircle()
    {
        hitCircle = new GameObject("HitCircle");
        circleRenderer = hitCircle.AddComponent<LineRenderer>();

        circleRenderer.material = new Material(Shader.Find("Sprites/Default"));
        circleRenderer.startColor = Color.red;
        circleRenderer.endColor = Color.red;
        circleRenderer.startWidth = 0.05f;
        circleRenderer.endWidth = 0.05f;
        circleRenderer.positionCount = 37;
        circleRenderer.useWorldSpace = true;
        circleRenderer.loop = true;

        hitCircle.SetActive(false);
    }

    void Update()
    {
        HandleTouchInput();
        UpdateLaserPointer();

        // Smoothly approach the target camera rotation
        if (smoothRotation && vrCamera != null)
        {
            vrCamera.transform.localRotation = Quaternion.Slerp(
                vrCamera.transform.localRotation,
                targetRotation,
                rotationSpeed * Time.deltaTime);
        }
    }

    private bool IsTouchOverUI(int fingerId)
    {
        if (EventSystem.current == null) return false;
        return EventSystem.current.IsPointerOverGameObject(fingerId);
    }

    void HandleTouchInput()
    {
        // บล็อค touch ทั้งหมดเมื่อ debug panel เปิด
        if (UIDebugClass.Instance != null && UIDebugClass.Instance.IsDebugActive)
            return;

        // === 1 นิ้ว ===
        if (Input.touchCount == 1 && !isRotating)
        {
            Touch touch = Input.GetTouch(0);

            // ไม่ทำงานเมื่อแตะบน UI (track ตลอด touch sequence)
            if (touch.phase == TouchPhase.Began)
                touchStartedOnUI = IsTouchOverUI(touch.fingerId);

            if (touchStartedOnUI)
            {
                if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                    touchStartedOnUI = false;
                return;
            }

            // Log ผ่าน LogDataClass
            if (LogDataClass.Instance != null)
                LogDataClass.Instance.LogTouch("dragngo", touch);

            if (currentMode == DragnGoMode.TwoFingerRotate)
            {
                // โหมดเดิม: 1 นิ้ว ลากเดิน
                switch (touch.phase)
                {
                    case TouchPhase.Began:
                        OnOneTouchBegan(touch);
                        break;
                    case TouchPhase.Moved:
                    case TouchPhase.Stationary:
                        OnOneTouchMovedOrStationary(touch);
                        break;
                    case TouchPhase.Ended:
                    case TouchPhase.Canceled:
                        OnOneTouchEnded(touch);
                        break;
                }
            }
            else
            {
                // โหมดใหม่: 1 นิ้ว ลากหมุน + Double Tap เดิน (ไม่เคลื่อนที่ขณะ swipe)
                switch (touch.phase)
                {
                    case TouchPhase.Began:
                        OnOneTouchBegan_OneFingerRotateMode(touch);
                        break;
                    case TouchPhase.Moved:
                    case TouchPhase.Stationary:
                        OnOneTouchMoved_OneFingerRotateMode(touch);
                        break;
                    case TouchPhase.Ended:
                    case TouchPhase.Canceled:
                        OnOneTouchEnded_OneFingerRotateMode(touch);
                        break;
                }
            }
        }
        // === 2 นิ้ว: หมุน (เฉพาะ TwoFingerRotate mode) ===
        else if (Input.touchCount == 2 && currentMode == DragnGoMode.TwoFingerRotate)
        {
            HandleTwoFingerRotation();
        }
        else
        {
            isRotating = false;
            isOneFingerRotating = false;
        }
    }

    // === 1 Finger: Began ===
    void OnOneTouchBegan(Touch touch)
    {
        originalVEPosition = player.position;
        touchStartY = touch.position.y;
        isDragging = true;
        hasDragLimit = false;
    }

    // === 1 Finger: Moved / Stationary ===
    void OnOneTouchMovedOrStationary(Touch touch)
    {
        if (!isDragging) return;

        if (UpdateDragRaycast())
        {
            if (!hasDragLimit)
            {
                dragMaxDistance = currentHitDistance;
                hasDragLimit = true;
            }

            float effectiveDistance = Mathf.Max(0f, dragMaxDistance - stopBeforeEnd);
            Vector3 direction = (raycastTarget - originalVEPosition).normalized;
            float movePercent = GetDragPercentFromScreen(touch.position.y);
            Vector3 targetPosition = originalVEPosition + direction * (effectiveDistance * movePercent);

            targetPosition.y = originalVEPosition.y;
            StopMoveRoutine();
            player.position = targetPosition;

            // Log movement
            if (LogDataClass.Instance != null)
                LogDataClass.Instance.LogMovement("dragngo", player.position, moveSpeed, "Drag");
        }
    }

    // === 1 Finger: Ended / Canceled ===
    void OnOneTouchEnded(Touch touch)
    {
        isDragging = false;
    }

    // ============================================================
    // === OneFingerRotate Mode: 1 Finger handlers (ใหม่) ===
    // ============================================================

    void OnOneTouchBegan_OneFingerRotateMode(Touch touch)
    {
        touchStartPosition = touch.position;
        touchStartTime = Time.unscaledTime;
        previousOneFingerPosition = touch.position;
        isOneFingerRotating = false;
    }

    void OnOneTouchMoved_OneFingerRotateMode(Touch touch)
    {
        // ตรวจว่าลากเกิน threshold → เริ่มหมุน (ไม่เคลื่อนที่เลย)
        float dragDistance = Vector2.Distance(touch.position, touchStartPosition);
        if (dragDistance > dragThreshold || isOneFingerRotating)
        {
            isOneFingerRotating = true;

            Vector2 delta = touch.position - previousOneFingerPosition;

            // หมุนซ้าย-ขวา (Yaw)
            float rotationSign = invertTwoFingerRotation ? -1f : 1f;
            float yawDelta = delta.x * rotationSpeed * 0.05f * rotationSign;
            player.Rotate(Vector3.up, yawDelta, Space.World);

            // หมุนเงย-ก้ม (Pitch)
            if (allowPitch && vrCamera != null)
            {
                float pitchDelta = -delta.y * rotationSpeed * 0.05f;
                Vector3 currentRotation = vrCamera.transform.localEulerAngles;
                float newPitch = currentRotation.x + pitchDelta;
                if (newPitch > 180f) newPitch -= 360f;
                newPitch = Mathf.Clamp(newPitch, pitchMin, pitchMax);
                vrCamera.transform.localEulerAngles = new Vector3(newPitch, currentRotation.y, currentRotation.z);
            }

            previousOneFingerPosition = touch.position;
        }
    }

    void OnOneTouchEnded_OneFingerRotateMode(Touch touch)
    {
        // ถ้าไม่ได้ลาก (tap) → ตรวจ Double Tap
        float dragDistance = Vector2.Distance(touch.position, touchStartPosition);
        if (dragDistance <= tapDragThreshold && !isOneFingerRotating)
        {
            float currentTime = Time.unscaledTime;
            if (lastTapTime > 0f && (currentTime - lastTapTime) <= doubleTapMaxInterval)
            {
                // Double Tap → เดินไปตาม raycast
                originalVEPosition = player.position;
                if (UpdateDragRaycast())
                {
                    float effectiveDistance = Mathf.Max(0f, currentHitDistance - stopBeforeEnd);
                    Vector3 direction = (raycastTarget - originalVEPosition).normalized;
                    Vector3 targetPos = originalVEPosition + direction * effectiveDistance;
                    targetPos.y = originalVEPosition.y;

                    StartMoveToPosition(targetPos);

                    if (LogDataClass.Instance != null)
                        LogDataClass.Instance.LogMovement("dragngo", targetPos, moveSpeed, "DoubleTap");
                }
                lastTapTime = -1f;
            }
            else
            {
                lastTapTime = currentTime;
                lastTapPosition = touch.position;
            }
        }
        else
        {
            // ลากหมุน/swipe → ตรวจ swipe ซ้าย/ขวา
            TrySwipeStrafe(touch.position);
            lastTapTime = -1f;
        }

        isOneFingerRotating = false;
        isDragging = false;
    }

    // === Swipe Strafe: ตรวจ swipe ซ้าย/ขวา แล้วเคลื่อนที่ด้านข้าง ===
    bool TrySwipeStrafe(Vector2 touchEndPosition)
    {
        float duration = Time.unscaledTime - touchStartTime;
        if (duration > swipeMaxDuration) return false;

        Vector2 delta = touchEndPosition - touchStartPosition;
        if (Mathf.Abs(delta.x) < swipeMinPixels) return false;
        if (Mathf.Abs(delta.x) <= Mathf.Abs(delta.y)) return false; // ไม่ใช่ horizontal

        float dir = delta.x > 0 ? 1f : -1f;
        player.position += player.right * dir * swipeMoveDistance;

        if (LogDataClass.Instance != null)
            LogDataClass.Instance.LogMovement("dragngo", player.position, 0f, dir > 0 ? "SwipeRight" : "SwipeLeft");

        return true;
    }

    // === 2 Fingers: Rotation (โครงสร้างเดียวกับ DogPaddle, StreetView) ===
    void HandleTwoFingerRotation()
    {
        isDragging = false;
        isRotating = true;

        Touch touch0 = Input.GetTouch(0);
        Touch touch1 = Input.GetTouch(1);

        Vector2 currentMidpoint = (touch0.position + touch1.position) / 2f;

        if (touch0.phase == TouchPhase.Began || touch1.phase == TouchPhase.Began)
        {
            previousTouchMidpoint = currentMidpoint;
        }
        else if (touch0.phase == TouchPhase.Ended || touch1.phase == TouchPhase.Ended
              || touch0.phase == TouchPhase.Canceled || touch1.phase == TouchPhase.Canceled)
        {
            // จบการหมุน
        }
        else
        {
            Vector2 deltaMidpoint = currentMidpoint - previousTouchMidpoint;

            // หมุนซ้าย-ขวา (Yaw)
            float rotationSign = invertTwoFingerRotation ? -1f : 1f;
            float yawDelta = deltaMidpoint.x * rotationSpeed * 0.05f * rotationSign;
            player.Rotate(Vector3.up, yawDelta, Space.World);

            // หมุนเงย-ก้ม (Pitch)
            if (allowPitch && vrCamera != null)
            {
                float pitchDelta = -deltaMidpoint.y * rotationSpeed * 0.05f;
                Vector3 currentRotation = vrCamera.transform.localEulerAngles;
                float newPitch = currentRotation.x + pitchDelta;
                if (newPitch > 180f) newPitch -= 360f;
                newPitch = Mathf.Clamp(newPitch, pitchMin, pitchMax);
                vrCamera.transform.localEulerAngles = new Vector3(newPitch, currentRotation.y, currentRotation.z);
            }

            previousTouchMidpoint = currentMidpoint;
        }
    }

    private bool UpdateDragRaycast()
    {
        RaycastHit hit;
        Vector3 laserStart = player.position + Vector3.up * 1.5f;
        Vector3 laserDirection = vrCamera.transform.forward;

        if (Physics.Raycast(laserStart, laserDirection, out hit, Mathf.Infinity, raycastLayers))
        {
            raycastTarget = hit.point;
            raycastTarget.y = originalVEPosition.y;
            currentHitDistance = Vector3.Distance(originalVEPosition, raycastTarget);
            hasValidHit = true;
        }
        else
        {
            hasValidHit = false;
        }

        return hasValidHit;
    }

    private float GetDragPercentFromScreen(float screenY)
    {
        if (Screen.height <= 0f) return 0f;
        return Mathf.Clamp01(1f - (screenY / Screen.height));
    }

    void UpdateLaserPointer()
    {
        if (laserPointer == null || vrCamera == null) return;

        Vector3 laserStart = player.position;
        Vector3 laserDirection = vrCamera.transform.forward;

        laserPointer.SetPosition(0, laserStart);

        RaycastHit hit;
        if (Physics.Raycast(laserStart, laserDirection, out hit, Mathf.Infinity, raycastLayers))
        {
            laserPointer.SetPosition(1, hit.point);

            Vector3 stopPoint = laserStart + laserDirection * Mathf.Max(0f, hit.distance - stopBeforeEnd);
            DrawHitCircle(stopPoint, hit.normal);
        }
        else
        {
            laserPointer.SetPosition(1, laserStart + laserDirection * 50f);

            if (hitCircle != null)
                hitCircle.SetActive(false);
        }
    }

    void DrawHitCircle(Vector3 hitPoint, Vector3 hitNormal)
    {
        if (circleRenderer == null) return;

        hitCircle.SetActive(true);

        Quaternion rotation = Quaternion.LookRotation(hitNormal);
        int segments = 36;
        float angleStep = 360f / segments;

        for (int i = 0; i <= segments; i++)
        {
            float angle = i * angleStep * Mathf.Deg2Rad;
            Vector3 localPos = new Vector3(
                Mathf.Cos(angle) * hitCircleRadius,
                Mathf.Sin(angle) * hitCircleRadius,
                0f
            );
            Vector3 worldPos = hitPoint + rotation * localPos;
            circleRenderer.SetPosition(i, worldPos);
        }
    }

    private void StopMoveRoutine()
    {
        if (moveRoutine != null)
        {
            StopCoroutine(moveRoutine);
            moveRoutine = null;
        }
    }

    private void StartMoveToPosition(Vector3 targetPosition)
    {
        StopMoveRoutine();
        moveRoutine = StartCoroutine(MoveToPosition(targetPosition));
    }

    private IEnumerator MoveToPosition(Vector3 targetPosition)
    {
        while (Vector3.Distance(player.position, targetPosition) > 0.16f)
        {
            player.position = Vector3.MoveTowards(player.position, targetPosition, moveSpeed * Time.deltaTime);
            yield return null;
        }

        player.position = targetPosition;
        moveRoutine = null;
    }
}
