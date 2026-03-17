using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public enum StreetViewMode
{
    TwoFingerRotate,    // 1 นิ้ว Tap เดิน, 2 นิ้ว หมุน (default)
    OneFingerRotate     // 1 นิ้ว ลากหมุน Realtime, Double Tap เดิน
}

public class StreetView : MonoBehaviour
{
    [Header("References")]
    public Transform player;                    // ตำแหน่งผู้เล่น (rig/root)
    public Camera vrCamera;                     // กล้องสำหรับ raycast
    public LayerMask groundLayer;               // Layer ของพื้น (Ground)
    [Header("Mode Settings")]
    public Toggle modeToggle;                   // Toggle UI สำหรับสลับโหมด
    public StreetViewMode currentMode = StreetViewMode.TwoFingerRotate;

    [Header("Movement Settings")]
    public Slider speedSlider;                   // Slider สำหรับปรับ moveDuration
    public float moveDuration = 1f;             // เวลาเคลื่อนที่คงที่ (วินาที) - ไกลหรือใกล้ใช้เวลาเท่ากัน
    public float maxRayDistance = 100f;         // ระยะ ray สูงสุด
    public float noHitMoveDistance = 10f;       // ระยะเคลื่อนที่เมื่อไม่ชน Ground
    public float arriveDistance = 0.1f;         // ระยะถือว่าถึงแล้ว

    [Header("Rotation Settings")]
    public float rotationSpeed = 5f;            // ความเร็วหมุน
    public bool invertRotation = false;         // กลับทิศหมุน (World Rotate style)
    public bool allowPitch = true;              // อนุญาตให้เงย/ก้ม
    public float pitchMin = -60f;               // มุมก้มต่ำสุด
    public float pitchMax = 60f;                // มุมเงยสูงสุด
    public float dragThreshold = 10f;           // ระยะลากขั้นต่ำก่อนเริ่มหมุน

    [Header("Double Tap Settings")]
    public float doubleTapMaxInterval = 0.3f;   // เวลาสูงสุดระหว่าง tap สอง tap (วินาที)

    [Header("Swipe Strafe Settings")]
    public float swipeMoveDistance = 0.6f;       // ระยะเคลื่อนที่ซ้าย/ขวาต่อ swipe
    public float swipeMinPixels = 50f;           // ระยะ pixel ขั้นต่ำ horizontal ที่ถือว่าเป็น swipe
    public float swipeMaxDuration = 0.4f;        // เวลาสูงสุดที่ถือว่าเป็น swipe (วินาที)

    [Header("Walkable Grounding")]
    public string walkableLayerName = "Walkable";
    public float groundProbeHeight = 0.5f;
    public float groundProbeDistance = 1.5f;
    public float footOffset = 0.02f;

    [Header("Cursor Settings")]
    public bool showCursor = true;
    public float cursorRadius = 0.3f;
    public Color cursorColor = Color.cyan;
    public float cursorLineWidth = 0.05f;
    public float cursorForwardOffset = 0.15f;
    public float cursorDirectionLength = 0.35f;

    // Cursor
    private GameObject cursorCircle;
    private LineRenderer cursorRenderer;
    private LineRenderer cursorDirectionRenderer;

    // Raycast hit info
    private Vector3 currentHitPoint;
    private Vector3 currentHitNormal;
    private bool hasValidHit = false;
    private float currentHitDistance = 0f;
    private Vector3 currentCursorForward = Vector3.forward;
    private Vector3 lastValidCursorPoint;
    private Vector3 lastValidCursorNormal = Vector3.up;
    private bool hasLastValidCursor;

    // Movement
    private Coroutine moveRoutine;
    private float currentMoveSpeed;
    private Vector3 moveTargetPosition;
    private bool hideCursorUntilPointerActivity = false;

    // Touch
    private Vector2 touchStartPosition;
    private float touchStartTime;

    // Single Tap Detection (Street View style)
    private float tapDragThreshold = 20f;

    // UI touch blocking
    private bool touchStartedOnUI = false;

    // Rotation (2-finger drag)
    private bool isRotating = false;
    private Vector2 previousTouchMidpoint;
    private Vector2 initialTouchMidpoint;
    private bool isDraggingToRotate = false;

    // Double Tap Detection (OneFingerRotate mode)
    private float lastTapTime = -1f;
    private Vector2 lastTapPosition;

    // One-finger rotation (OneFingerRotate mode)
    private bool isOneFingerRotating = false;
    private Vector2 previousOneFingerPosition;

    // Desktop mouse
    private bool isMouseDragging = false;
    private bool mouseDownStartedOnUI = false;
    private bool suppressCursorUntilMouseMove = false;
    private Vector2 mouseDownPosition;
    private Vector2 previousMousePosition;
    private CharacterController playerCharacterController;
    private CapsuleCollider playerCapsuleCollider;

    void Start()
    {
        // เชื่อม Slider กับ baseSpeed
        if (speedSlider != null)
        {
            speedSlider.value = moveDuration;
            speedSlider.onValueChanged.AddListener(OnSpeedSliderChanged);
        }

        // เชื่อม Toggle กับโหมด
        if (modeToggle != null)
        {
            modeToggle.isOn = (currentMode == StreetViewMode.OneFingerRotate);
            modeToggle.onValueChanged.AddListener(OnModeToggleChanged);
        }

        if (player != null)
        {
            playerCharacterController = player.GetComponent<CharacterController>();
            playerCapsuleCollider = player.GetComponent<CapsuleCollider>();
        }

        SnapPlayerToWalkable();
        previousMousePosition = Input.mousePosition;
        CreateCursor();
        hideCursorUntilPointerActivity = true;
    }

    void OnSpeedSliderChanged(float value)
    {
        moveDuration = Mathf.Max(0.1f, value); // ขั้นต่ำ 0.1 วินาที
    }

    void OnModeToggleChanged(bool isOn)
    {
        currentMode = isOn ? StreetViewMode.OneFingerRotate : StreetViewMode.TwoFingerRotate;
        // Reset state เมื่อสลับโหมด
        isOneFingerRotating = false;
        isRotating = false;
        isDraggingToRotate = false;
        lastTapTime = -1f;
    }

    void Update()
    {
        HandleTouchInput();
        DetectPointerActivity();
        UpdateCursorTracking();
        UpdateCursor();
    }

    void DetectPointerActivity()
    {
        if (!hideCursorUntilPointerActivity)
            return;

        bool hasTouchActivity = false;
        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch touch = Input.GetTouch(i);
            if (touch.phase == TouchPhase.Began)
            {
                hasTouchActivity = true;
                break;
            }
        }

        Vector2 mousePosition = Input.mousePosition;
        bool hasMouseMove =
            float.IsFinite(mousePosition.x) &&
            float.IsFinite(mousePosition.y) &&
            mousePosition != previousMousePosition;
        bool hasMouseActivity = Input.GetMouseButtonDown(0) || hasMouseMove;

        if (hasTouchActivity || hasMouseActivity)
            hideCursorUntilPointerActivity = false;

        previousMousePosition = mousePosition;
    }

    void UpdateCursorTracking()
    {
        if (!showCursor || vrCamera == null) return;
        if (hideCursorUntilPointerActivity || moveRoutine != null) return;

        Vector2 pointerPos;

        if (Input.touchCount > 0)
        {
            pointerPos = Input.GetTouch(0).position;
        }
        else
        {
            Vector2 mouse = Input.mousePosition;
            // ข้ามถ้า mouse อยู่นอกหน้าจอ หรือ เป็น Infinity/NaN
            if (!float.IsFinite(mouse.x) || !float.IsFinite(mouse.y)) return;
            if (mouse.x < 0 || mouse.x > Screen.width || mouse.y < 0 || mouse.y > Screen.height) return;
            pointerPos = mouse;
        }

        PerformRaycastFromScreenPosition(pointerPos);
    }

    void CreateCursor()
    {
        cursorCircle = new GameObject("GroundCursor");
        cursorRenderer = cursorCircle.AddComponent<LineRenderer>();

        cursorRenderer.material = new Material(Shader.Find("Sprites/Default"));
        cursorRenderer.startColor = cursorColor;
        cursorRenderer.endColor = cursorColor;
        cursorRenderer.startWidth = cursorLineWidth;
        cursorRenderer.endWidth = cursorLineWidth;
        cursorRenderer.positionCount = 37;
        cursorRenderer.useWorldSpace = true;
        cursorRenderer.loop = true;

        cursorCircle.SetActive(false);
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
                LogDataClass.Instance.LogTouch("streetview", touch);

            if (currentMode == StreetViewMode.TwoFingerRotate)
            {
                // โหมดเดิม: 1 นิ้ว Tap เดิน
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
                        OnOneTouchEnded_TwoFingerMode(touch);
                        break;
                    case TouchPhase.Canceled:
                        break;
                }
            }
            else
            {
                // โหมดใหม่: 1 นิ้ว ลากหมุน + Double Tap เดิน
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
                        OnOneTouchEnded_OneFingerRotateMode(touch);
                        break;
                    case TouchPhase.Canceled:
                        isOneFingerRotating = false;
                        break;
                }
            }
        }
        // === 2 นิ้ว: หมุน (เฉพาะ TwoFingerRotate mode) ===
        else if (Input.touchCount == 2 && currentMode == StreetViewMode.TwoFingerRotate)
        {
            HandleTwoFingerRotation();
        }
        else
        {
            isRotating = false;
            isDraggingToRotate = false;
            isOneFingerRotating = false;
        }
    }

    // ============================================================
    // === TwoFingerRotate Mode: 1 Finger handlers (เดิม) ===
    // ============================================================

    void OnOneTouchBegan(Touch touch)
    {
        touchStartPosition = touch.position;
        touchStartTime = Time.unscaledTime;
        isRotating = false;

        PerformRaycastFromScreenPosition(touch.position);
    }

    void OnOneTouchMovedOrStationary(Touch touch)
    {
        PerformRaycastFromScreenPosition(touch.position);
    }

    void OnOneTouchEnded_TwoFingerMode(Touch touch)
    {
        // Street View style: Single Tap เคลื่อนที่ไปจุดที่แตะ
        float dragDistance = Vector2.Distance(touch.position, touchStartPosition);
        if (dragDistance <= tapDragThreshold)
        {
            StartMovement(touch.position);
        }
        else
        {
            // ตรวจ swipe ซ้าย/ขวา → เคลื่อนที่ด้านข้าง
            TrySwipeStrafe(touch.position);
        }
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

        PerformRaycastFromScreenPosition(touch.position);
    }

    void OnOneTouchMoved_OneFingerRotateMode(Touch touch)
    {
        // ตรวจว่าลากเกิน threshold → เริ่มหมุน
        float dragDistance = Vector2.Distance(touch.position, touchStartPosition);
        if (dragDistance > dragThreshold || isOneFingerRotating)
        {
            isOneFingerRotating = true;

            Vector2 delta = touch.position - previousOneFingerPosition;

            // หมุนซ้าย-ขวา (Yaw)
            float rotationSign = invertRotation ? -1f : 1f;
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

        PerformRaycastFromScreenPosition(touch.position);
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
                // Double Tap → เดิน
                StartMovement(touch.position);
                lastTapTime = -1f; // reset เพื่อไม่ให้ triple tap trigger อีก
            }
            else
            {
                // บันทึก tap แรก
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
    }

    // === 2 Fingers: Rotation (โครงสร้างเดียวกับ DogPaddle, DragnGo) ===
    void HandleTwoFingerRotation()
    {
        isRotating = true;

        Touch touch0 = Input.GetTouch(0);
        Touch touch1 = Input.GetTouch(1);

        Vector2 currentMidpoint = (touch0.position + touch1.position) / 2f;

        if (touch0.phase == TouchPhase.Began || touch1.phase == TouchPhase.Began)
        {
            initialTouchMidpoint = currentMidpoint;
            previousTouchMidpoint = currentMidpoint;
            isDraggingToRotate = false;
        }
        else if (touch0.phase == TouchPhase.Ended || touch1.phase == TouchPhase.Ended
              || touch0.phase == TouchPhase.Canceled || touch1.phase == TouchPhase.Canceled)
        {
            // จบการหมุน
        }
        else
        {
            // ตรวจสอบว่าลากเกิน threshold หรือยัง
            float dragDistance = Vector2.Distance(currentMidpoint, initialTouchMidpoint);
            if (dragDistance > dragThreshold || isDraggingToRotate)
            {
                isDraggingToRotate = true;

                Vector2 deltaMidpoint = currentMidpoint - previousTouchMidpoint;

                // หมุนซ้าย-ขวา (Yaw)
                float rotationSign = invertRotation ? -1f : 1f;
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
        bool moved = MovePlayerKeepingFeetOnGround(player.position + player.right * dir * swipeMoveDistance);

        if (moved && LogDataClass.Instance != null)
            LogDataClass.Instance.LogMovement("streetview", player.position, 0f, dir > 0 ? "SwipeRight" : "SwipeLeft");

        return moved;
    }

    public void MoveForwardDefault()
    {
        if (vrCamera == null || player == null) return;

        Vector3 forward = vrCamera.transform.forward;
        forward.y = 0f;
        forward.Normalize();

        Vector3 targetPos = player.position + forward * noHitMoveDistance;
        if (!TryGetWalkableHit(targetPos, out _, out _))
            return;

        moveTargetPosition = targetPos;

        float distance = Vector3.Distance(player.position, moveTargetPosition);
        currentMoveSpeed = distance / Mathf.Max(0.1f, moveDuration);
        hideCursorUntilPointerActivity = true;

        if (moveRoutine != null) StopCoroutine(moveRoutine);
        moveRoutine = StartCoroutine(MoveToTarget());

        LogDataClass.Instance?.LogMovement("streetview", moveTargetPosition, currentMoveSpeed, "ArrowTap");
    }

    void PerformRaycastFromScreenPosition(Vector2 screenPosition)
    {
        if (vrCamera == null) return;

        Ray ray = vrCamera.ScreenPointToRay(screenPosition);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, maxRayDistance, groundLayer))
        {
            hasValidHit = true;
            currentHitPoint = hit.point;
            currentHitNormal = hit.normal;
            currentHitDistance = hit.distance;
            lastValidCursorPoint = hit.point;
            lastValidCursorNormal = hit.normal;
            hasLastValidCursor = true;
        }
        else
        {
            hasValidHit = false;
            currentHitDistance = 0f;
            currentHitPoint = ray.origin + ray.direction * noHitMoveDistance;
            currentHitNormal = Vector3.up;
        }
    }

    void StartMovement(Vector2 screenPosition)
    {
        if (vrCamera == null || player == null) return;

        Ray ray = vrCamera.ScreenPointToRay(screenPosition);
        RaycastHit hit;

        Vector3 targetPosition;

        if (Physics.Raycast(ray, out hit, maxRayDistance, groundLayer))
        {
            targetPosition = hit.point;
        }
        else
        {
            Vector3 direction = ray.direction;
            direction.y = 0f;
            direction.Normalize();

            targetPosition = player.position + direction * noHitMoveDistance;
        }

        if (!TryGetWalkableHit(targetPosition, out _, out _))
            return;

        moveTargetPosition = targetPosition;

        // คำนวณ speed จาก distance / duration → ไกลหรือใกล้ใช้เวลาเท่ากัน
        float distance = Vector3.Distance(player.position, moveTargetPosition);
        currentMoveSpeed = distance / Mathf.Max(0.1f, moveDuration);
        hideCursorUntilPointerActivity = true;

        if (moveRoutine != null)
        {
            StopCoroutine(moveRoutine);
        }
        moveRoutine = StartCoroutine(MoveToTarget());

        // Log movement
        if (LogDataClass.Instance != null)
            LogDataClass.Instance.LogMovement("streetview", moveTargetPosition, currentMoveSpeed, "Tap");
    }

    IEnumerator MoveToTarget()
    {
        // ซ่อน cursor ขณะเคลื่อนที่
        Vector3 startPosition = player.position;
        float duration = Mathf.Max(0.1f, moveDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float easedT = Mathf.SmoothStep(0f, 1f, t);
            Vector3 nextPosition = Vector3.Lerp(startPosition, moveTargetPosition, easedT);
            if (!MovePlayerKeepingFeetOnGround(nextPosition))
                break;
            yield return null;
        }

        MovePlayerKeepingFeetOnGround(moveTargetPosition);
        moveRoutine = null;

        // แสดง cursor กลับมาเมื่อถึงจุดหมาย
    }

    void UpdateCursor()
    {
        // ซ่อน cursor ขณะเคลื่อนที่
        if (!showCursor || cursorRenderer == null || moveRoutine != null || hideCursorUntilPointerActivity || Input.touchCount <= 0)
        {
            if (cursorCircle != null) cursorCircle.SetActive(false);
            return;
        }

        if (!hasValidHit && !hasLastValidCursor)
        {
            if (cursorCircle != null) cursorCircle.SetActive(false);
            return;
        }

        cursorCircle.SetActive(true);
        Vector3 drawPoint = hasValidHit ? currentHitPoint : lastValidCursorPoint;
        Vector3 drawNormal = hasValidHit ? currentHitNormal : lastValidCursorNormal;
        DrawCursor(drawPoint, drawNormal, hasValidHit || hasLastValidCursor);
    }

    void DrawCursor(Vector3 point, Vector3 normal, bool isValidHit)
    {
        Color color = isValidHit ? cursorColor : Color.red;
        cursorRenderer.startColor = color;
        cursorRenderer.endColor = color;

        Quaternion rotation = Quaternion.FromToRotation(Vector3.up, normal);

        int segments = 36;
        float angleStep = 360f / segments;

        for (int i = 0; i <= segments; i++)
        {
            float angle = i * angleStep * Mathf.Deg2Rad;
            Vector3 localPos = new Vector3(
                Mathf.Cos(angle) * cursorRadius,
                0f,
                Mathf.Sin(angle) * cursorRadius
            );
            Vector3 worldPos = point + rotation * localPos;
            cursorRenderer.SetPosition(i, worldPos);
        }
    }

    // Public methods
    public void StopMovement()
    {
        if (moveRoutine != null)
        {
            StopCoroutine(moveRoutine);
            moveRoutine = null;
        }
    }

    bool MovePlayerKeepingFeetOnGround(Vector3 targetPosition)
    {
        if (player == null)
            return false;

        if (TryGetWalkableHit(targetPosition, out RaycastHit hit, out float footToPivot))
        {
            player.position = hit.point + hit.normal * (footToPivot + footOffset);
            AlignPlayerToGround(hit.normal);
            return true;
        }

        return false;
    }

    void SnapPlayerToWalkable()
    {
        if (player == null)
            return;

        if (TryGetWalkableHit(player.position, out RaycastHit hit, out float footToPivot))
        {
            player.position = hit.point + hit.normal * (footToPivot + footOffset);
            AlignPlayerToGround(hit.normal);
        }
    }

    bool TryGetWalkableHit(Vector3 pivotPosition, out RaycastHit hit, out float footToPivot)
    {
        footToPivot = GetFootToPivotDistance();
        int walkableLayer = LayerMask.NameToLayer(walkableLayerName);
        int mask = walkableLayer >= 0 ? (1 << walkableLayer) : groundLayer.value;
        Vector3 rayOrigin = pivotPosition + Vector3.up * groundProbeHeight;
        float rayDistance = groundProbeHeight + footToPivot + groundProbeDistance;
        return Physics.Raycast(rayOrigin, Vector3.down, out hit, rayDistance, mask, QueryTriggerInteraction.Ignore);
    }

    float GetFootToPivotDistance()
    {
        if (playerCharacterController != null)
            return Mathf.Max(0.01f, (playerCharacterController.height * 0.5f) - playerCharacterController.center.y);

        if (playerCapsuleCollider != null)
            return Mathf.Max(0.01f, (playerCapsuleCollider.height * 0.5f) - playerCapsuleCollider.center.y);

        return 0.5f;
    }

    void AlignPlayerToGround(Vector3 groundNormal)
    {
        Vector3 euler = player.eulerAngles;
        player.rotation = Quaternion.Euler(0f, euler.y, 0f);
    }
}
