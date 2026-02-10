using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Android;
using System;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public enum DogPaddleMode
{
    TwoFingerRotate,// 1 นิ้ว ลากเดิน, 2 นิ้ว หมุน (default)
    OneFingerRotate // 1 นิ้ว ลากหมุน Realtime, Double Tap เดิน
}

public class DogPaddle : MonoBehaviour
{
    [Header("References")]
    public Camera vrCamera;     // กล้องสำหรับ raycast
    public LayerMask raycastLayers; // Layer ที่ raycast ชนได้

    [Header("Mode Settings")]
    public Toggle modeToggle;   // Toggle UI สำหรับสลับโหมด
    public DogPaddleMode currentMode = DogPaddleMode.TwoFingerRotate;

    [Header("Movement Settings")]
    public Slider speedSlider;   // Slider สำหรับปรับ baseSpeed
    public float baseSpeed = 5f;// ความเร็วพื้นฐาน
    public float maxSpeed = 15f;// ความเร็วสูงสุด (เมื่อ touch อยู่ล่างสุด)
    public float maxRayDistance = 100f; // ระยะ ray สูงสุด
    public float arriveDistance = 0.1f; // ระยะถือว่าถึงแล้ว

    [Header("Rotation Settings")]
    public float rotationSpeed = 5f;// ความเร็วหมุน
    public bool invertTwoFingerRotation = true; // กลับทิศหมุน
    public bool allowPitch = false; // อนุญาตให้เงย/ก้ม
    public float pitchMin = -60f;
    public float pitchMax = 60f;
    public float dragThreshold = 10f;   // ระยะลากขั้นต่ำก่อนเริ่มหมุน

    [Header("Double Tap Settings")]
    public float doubleTapMaxInterval = 0.3f;   // เวลาสูงสุดระหว่าง tap (วินาที)
    public float tapDragThreshold = 20f;// ระยะ pixel ที่ถือว่าเป็น tap ไม่ใช่ drag

    [Header("Swipe Strafe Settings")]
    public float swipeMoveDistance = 0.6f;   // ระยะเคลื่อนที่ซ้าย/ขวาต่อ swipe
    public float swipeMinPixels = 50f;   // ระยะ pixel ขั้นต่ำ horizontal ที่ถือว่าเป็น swipe

    [Header("Touch Surface Settings")]
    public float fallbackDpi = 160f;// DPI สำรองกรณี Screen.dpi คืนค่า 0
    public float holdThreshold = 5f; // ระยะ pixel ที่ถือว่า "กดค้าง" ไม่ใช่ drag
    public float holdTimeThreshold = 0.2f;   // เวลา (วินาที) ที่ต้องนิ่งถึงจะถือว่ากดค้าง

    [Header("Swipe Detection (cm/s - DPI-based)")]
    public float swipeVelocityThresholdCm = 12f;  // ความเร็วขั้นต่ำที่ถือว่าเป็น Swipe (cm/sec)
    public float swipeMaxDuration = 0.4f; // เวลาสูงสุดที่ถือว่าเป็น Swipe (วินาที)
    public float swipeMinDistanceCm = 1.5f;   // ระยะลากขั้นต่ำที่ถือว่าเป็น Swipe (cm)

// Movement state
    private Vector3 originalPosition;
    private Vector3 targetPosition;
    private float targetDistance;
    private bool isDragging = false;
    private bool hasValidTarget = false;
    private Coroutine moveRoutine;
    private float touchStartY;

// Touch distance tracking
    private Vector2 touchStartPosition;
    private Vector2 totalDragPixels;
    private float totalDragDistanceCm;
    private Vector2 dragDistanceCm;

// Hold detection
    private bool isHolding = false;
    private float holdTimer = 0f;
    private Vector2 lastMovePosition;

// Swipe/Drag detection (1 finger)
    private float touchStartTime;
    private float currentTouchVelocityCm;
    private string oneFingerGestureState = "None";
    private Vector2 swipeDirection;
    private string oneFingerSwipeZone = "None";

// Swipe/Drag detection (2 fingers)
    private float twoFingerStartTime;
    private Vector2 twoFingerStartMidpoint;
    private float twoFingerVelocityCm;
    private string twoFingerGestureState = "None";
    private Vector2 twoFingerSwipeDirection;
    private string twoFingerSwipeZone = "None";

// UI touch blocking
    private bool touchStartedOnUI = false;

// Rotation state
    private bool isRotating = false;
    private Vector2 previousTouchMidpoint;

// Double Tap Detection (OneFingerRotate mode)
    private float lastTapTime = -1f;
    private Vector2 lastTapPosition;

// One-finger rotation (OneFingerRotate mode)
    private bool isOneFingerRotating = false;
    private Vector2 previousOneFingerPosition;

    void Start()
    {
// เชื่อม Slider กับ baseSpeed
        if (speedSlider != null)
        {
            speedSlider.value = baseSpeed;
            speedSlider.onValueChanged.AddListener(OnSpeedSliderChanged);
        }

// เชื่อม Toggle กับโหมด
        if (modeToggle != null)
        {
            modeToggle.isOn = (currentMode == DogPaddleMode.OneFingerRotate);
            modeToggle.onValueChanged.AddListener(OnModeToggleChanged);
        }
    }

    void OnSpeedSliderChanged(float value)
    {
        baseSpeed = value;
    }

    void OnModeToggleChanged(bool isOn)
    {
        currentMode = isOn ? DogPaddleMode.OneFingerRotate : DogPaddleMode.TwoFingerRotate;
// Reset state เมื่อสลับโหมด
        isOneFingerRotating = false;
        isRotating = false;
        isDragging = false;
        lastTapTime = -1f;
    }

    void Update()
    {
        HandleTouchInput();
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
                LogDataClass.Instance.LogTouch("dogpaddle", touch);

            if (currentMode == DogPaddleMode.TwoFingerRotate)
            {
// โหมดเดิม: 1 นิ้ว ลากเดิน
                switch (touch.phase)
                {
                    case TouchPhase.Began:
                        OnOneTouchBegan(touch);
                        break;
                    case TouchPhase.Moved:
                        OnOneTouchMoved(touch);
                        break;
                    case TouchPhase.Stationary:
                        OnOneTouchStationary(touch);
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
                        OnOneTouchMoved_OneFingerRotateMode(touch);
                        break;
                    case TouchPhase.Stationary:
                        break;
                    case TouchPhase.Ended:
                    case TouchPhase.Canceled:
                        OnOneTouchEnded_OneFingerRotateMode(touch);
                        break;
                }
            }
        }
// === 2 นิ้ว: หมุน (เฉพาะ TwoFingerRotate mode) ===
        else if (Input.touchCount == 2 && currentMode == DogPaddleMode.TwoFingerRotate)
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
        originalPosition = transform.position;
        touchStartY = touch.position.y;
        touchStartPosition = touch.position;
        lastMovePosition = touch.position;
        touchStartTime = Time.time;
        isDragging = true;
        hasValidTarget = false;

// Reset touch tracking
        totalDragPixels = Vector2.zero;
        totalDragDistanceCm = 0f;
        dragDistanceCm = Vector2.zero;
        isHolding = false;
        holdTimer = 0f;
        currentTouchVelocityCm = 0f;
        oneFingerGestureState = "Touching";
        oneFingerSwipeZone = GetScreenZone(touch.position.x);

        StopMoveRoutine();
        UpdateRaycastTarget();
    }

// === 1 Finger: Moved ===
    void OnOneTouchMoved(Touch touch)
    {
// คำนวณระยะ drag เป็น cm
        Vector2 currentDragPixels = touch.position - touchStartPosition;
        totalDragPixels = currentDragPixels;

        dragDistanceCm = PixelsToCm(currentDragPixels);
        totalDragDistanceCm = dragDistanceCm.magnitude;

// คำนวณ Velocity (cm/sec)
        float elapsed = Time.time - touchStartTime;
        currentTouchVelocityCm = elapsed > 0f ? totalDragDistanceCm / elapsed : 0f;

// ตรวจสอบ Hold
        float moveDelta = touch.deltaPosition.magnitude;
        if (moveDelta < holdThreshold)
        {
            holdTimer += Time.deltaTime;
            if (holdTimer >= holdTimeThreshold)
            {
                isHolding = true;
                oneFingerGestureState = "Hold";
            }
        }
        else
        {
            holdTimer = 0f;
            isHolding = false;
            lastMovePosition = touch.position;

            if (currentTouchVelocityCm >= swipeVelocityThresholdCm)
                oneFingerGestureState = "Swipe";
            else
                oneFingerGestureState = "Drag";
        }

        UpdateGestureDebugUI();

// ถ้ากดค้าง ไม่เคลื่อนที่
        if (isHolding)
        {
            if (LogDataClass.Instance != null)
                LogDataClass.Instance.LogMovement("dogpaddle", transform.position, 0f, "Hold");
            return;
        }

// ถ้า drag แนว horizontal มากกว่า vertical ให้ ไม่เดินหน้า/ถอยหลัง (รอ swipe strafe ตอน ended)
        Vector2 overallDelta = touch.position - touchStartPosition;
        if (Mathf.Abs(overallDelta.x) > Mathf.Abs(overallDelta.y))
            return;

        if (isDragging && hasValidTarget)
        {
            float deltaY = touch.position.y - touchStartY;
            float absDeltaY = Mathf.Abs(deltaY);
            float maxDeltaY = Screen.height * 0.5f;
            float speedPercent = Mathf.Clamp01(absDeltaY / maxDeltaY);
            float currentSpeed = Mathf.Lerp(baseSpeed, maxSpeed, speedPercent);

            Vector3 forwardDirection = (targetPosition - originalPosition).normalized;

            if (deltaY < 0)
            {
                float movePercent = Mathf.Clamp01(absDeltaY / maxDeltaY);
                Vector3 newPosition = originalPosition + forwardDirection * (targetDistance * movePercent);
                newPosition.y = originalPosition.y;

                transform.position = Vector3.MoveTowards(
                    transform.position,
                    newPosition,
                    currentSpeed * Time.deltaTime
                );
            }
            else
            {
                transform.Translate(-forwardDirection * currentSpeed * Time.deltaTime, Space.World);
            }

// Log movement
            if (LogDataClass.Instance != null)
                LogDataClass.Instance.LogMovement("dogpaddle", transform.position, currentSpeed, oneFingerGestureState);
        }
    }

// === 1 Finger: Stationary ===
    void OnOneTouchStationary(Touch touch)
    {
        holdTimer += Time.deltaTime;
        isHolding = true;
        oneFingerGestureState = "Hold";
        UpdateGestureDebugUI();
    }

// === 1 Finger: Ended / Canceled ===
    void OnOneTouchEnded(Touch touch)
    {
        float totalElapsed = Time.time - touchStartTime;
        Vector2 finalDragCm = PixelsToCm(touch.position - touchStartPosition);
        float finalDistanceCm = finalDragCm.magnitude;
        float finalVelocityCm = totalElapsed > 0f ? finalDistanceCm / totalElapsed : 0f;
        swipeDirection = (touch.position - touchStartPosition).normalized;

        if (finalVelocityCm >= swipeVelocityThresholdCm)
        {
            oneFingerGestureState = "Swipe";
// ตรวจ swipe ซ้าย/ขวา ให้ เคลื่อนที่ด้านข้าง
            TrySwipeStrafe(touch.position);
        }
        else if (isHolding)
        {
            oneFingerGestureState = "Hold";
        }
        else
        {
            oneFingerGestureState = "Drag";
        }

        isDragging = false;
        isHolding = false;
        holdTimer = 0f;

        UpdateGestureDebugUI();
    }

// ============================================================
// === OneFingerRotate Mode: 1 Finger handlers (ใหม่) ===
// ============================================================

    void OnOneTouchBegan_OneFingerRotateMode(Touch touch)
    {
        touchStartPosition = touch.position;
        previousOneFingerPosition = touch.position;
        isOneFingerRotating = false;
        touchStartTime = Time.time;

// ยัง track gesture สำหรับ debug
        oneFingerGestureState = "Touching";
        oneFingerSwipeZone = GetScreenZone(touch.position.x);
        totalDragPixels = Vector2.zero;
        totalDragDistanceCm = 0f;
        dragDistanceCm = Vector2.zero;
        currentTouchVelocityCm = 0f;

        UpdateGestureDebugUI();
    }

    void OnOneTouchMoved_OneFingerRotateMode(Touch touch)
    {
// คำนวณ gesture tracking (ไม่เคลื่อนที่)
        Vector2 currentDragPixels = touch.position - touchStartPosition;
        totalDragPixels = currentDragPixels;
        dragDistanceCm = PixelsToCm(currentDragPixels);
        totalDragDistanceCm = dragDistanceCm.magnitude;

        float elapsed = Time.time - touchStartTime;
        currentTouchVelocityCm = elapsed > 0f ? totalDragDistanceCm / elapsed : 0f;

        if (currentTouchVelocityCm >= swipeVelocityThresholdCm)
            oneFingerGestureState = "Swipe";
        else
            oneFingerGestureState = "Drag";

        UpdateGestureDebugUI();

// ตรวจว่าลากเกิน threshold ให้ เริ่มหมุน (ไม่เคลื่อนที่)
        float dragDistance = Vector2.Distance(touch.position, touchStartPosition);
        if (dragDistance > dragThreshold || isOneFingerRotating)
        {
            isOneFingerRotating = true;

            Vector2 delta = touch.position - previousOneFingerPosition;

// หมุนซ้าย-ขวา (Yaw)
            float rotationSign = invertTwoFingerRotation ? -1f : 1f;
            float yawDelta = delta.x * rotationSpeed * 0.05f * rotationSign;
            transform.Rotate(Vector3.up, yawDelta, Space.World);

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
// Gesture tracking
        float totalElapsed = Time.time - touchStartTime;
        Vector2 finalDragCm = PixelsToCm(touch.position - touchStartPosition);
        float finalDistanceCm = finalDragCm.magnitude;
        float finalVelocityCm = totalElapsed > 0f ? finalDistanceCm / totalElapsed : 0f;

        if (finalVelocityCm >= swipeVelocityThresholdCm)
            oneFingerGestureState = "Swipe";
        else if (!isOneFingerRotating)
            oneFingerGestureState = "Tap";
        else
            oneFingerGestureState = "Drag";

        UpdateGestureDebugUI();

// ถ้าไม่ได้ลาก (tap) ให้ ตรวจ Double Tap
        float dragDistance = Vector2.Distance(touch.position, touchStartPosition);
        if (dragDistance <= tapDragThreshold && !isOneFingerRotating)
        {
            float currentTime = Time.unscaledTime;
            if (lastTapTime > 0f && (currentTime - lastTapTime) <= doubleTapMaxInterval)
            {
// Double Tap ให้ เดินไปข้างหน้า
                originalPosition = transform.position;
                UpdateRaycastTarget();
                if (hasValidTarget)
                {
                    StopMoveRoutine();
                    moveRoutine = StartCoroutine(MoveToTargetRoutine());

                    if (LogDataClass.Instance != null)
                        LogDataClass.Instance.LogMovement("dogpaddle", targetPosition, baseSpeed, "DoubleTap");
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
// ลากหมุน/swipe ให้ ตรวจ swipe ซ้าย/ขวา
            TrySwipeStrafe(touch.position);
            lastTapTime = -1f;
        }

        isOneFingerRotating = false;
    }

// === Swipe Strafe: ตรวจ swipe ซ้าย/ขวา แล้วเคลื่อนที่ด้านข้าง ===
    bool TrySwipeStrafe(Vector2 touchEndPosition)
    {
        float duration = Time.time - touchStartTime;
        if (duration > swipeMaxDuration) return false;

        Vector2 delta = touchEndPosition - touchStartPosition;
        if (Mathf.Abs(delta.x) < swipeMinPixels) return false;
        if (Mathf.Abs(delta.x) <= Mathf.Abs(delta.y)) return false; // ไม่ใช่ horizontal

        float dir = delta.x > 0 ? 1f : -1f;
        transform.position += transform.right * dir * swipeMoveDistance;

        if (LogDataClass.Instance != null)
            LogDataClass.Instance.LogMovement("dogpaddle", transform.position, 0f, dir > 0 ? "SwipeRight" : "SwipeLeft");

        return true;
    }

    IEnumerator MoveToTargetRoutine()
    {
        while (Vector3.Distance(transform.position, targetPosition) > arriveDistance)
        {
            transform.position = Vector3.MoveTowards(
                transform.position,
                targetPosition,
                baseSpeed * Time.deltaTime
            );
            yield return null;
        }
        transform.position = targetPosition;
        moveRoutine = null;
    }

// === 2 Fingers: Rotation (โครงสร้างเดียวกับ DragnGo, StreetView) ===
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
            twoFingerStartMidpoint = currentMidpoint;
            twoFingerStartTime = Time.time;
            twoFingerVelocityCm = 0f;
            twoFingerGestureState = "Touching";
            twoFingerSwipeZone = GetScreenZone(currentMidpoint.x);
        }
        else if (touch0.phase == TouchPhase.Ended || touch1.phase == TouchPhase.Ended
              || touch0.phase == TouchPhase.Canceled || touch1.phase == TouchPhase.Canceled)
        {
            float elapsed2F = Time.time - twoFingerStartTime;
            Vector2 dist2FCm = PixelsToCm(currentMidpoint - twoFingerStartMidpoint);
            float distanceCm = dist2FCm.magnitude;
            float vel2FCm = elapsed2F > 0f ? distanceCm / elapsed2F : 0f;
            twoFingerSwipeDirection = (currentMidpoint - twoFingerStartMidpoint).normalized;

            twoFingerGestureState = vel2FCm >= swipeVelocityThresholdCm ? "Swipe" : "Drag";
            UpdateGestureDebugUI();
        }
        else
        {
            Vector2 deltaMidpoint = currentMidpoint - previousTouchMidpoint;

// คำนวณ Velocity 2 นิ้ว
            float elapsed2F = Time.time - twoFingerStartTime;
            Vector2 dist2FCm = PixelsToCm(currentMidpoint - twoFingerStartMidpoint);
            twoFingerVelocityCm = elapsed2F > 0f ? dist2FCm.magnitude / elapsed2F : 0f;

            twoFingerGestureState = twoFingerVelocityCm >= swipeVelocityThresholdCm ? "Swipe" : "Drag";

// หมุนซ้าย-ขวา (Yaw)
            float rotationSign = invertTwoFingerRotation ? -1f : 1f;
            float yawDelta = deltaMidpoint.x * rotationSpeed * 0.05f * rotationSign;
            transform.Rotate(Vector3.up, yawDelta, Space.World);

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
            UpdateGestureDebugUI();
        }
    }

    void UpdateRaycastTarget()
    {
        if (vrCamera == null)
        {
            targetPosition = transform.position + transform.forward * maxRayDistance;
            targetDistance = maxRayDistance;
            hasValidTarget = true;
            return;
        }

        Ray ray = new Ray(vrCamera.transform.position, vrCamera.transform.forward);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, maxRayDistance, raycastLayers))
        {
            targetPosition = hit.point;
            targetPosition.y = originalPosition.y;
            targetDistance = Vector3.Distance(originalPosition, targetPosition);
            hasValidTarget = true;
        }
        else
        {
            Vector3 direction = vrCamera.transform.forward;
            direction.y = 0f;
            direction.Normalize();

            targetPosition = originalPosition + direction * maxRayDistance;
            targetDistance = maxRayDistance;
            hasValidTarget = true;
        }
    }

    void StopMoveRoutine()
    {
        if (moveRoutine != null)
        {
            StopCoroutine(moveRoutine);
            moveRoutine = null;
        }
    }

// แปลง pixels เป็น cm โดยใช้ Screen.dpi
    Vector2 PixelsToCm(Vector2 pixels)
    {
        float dpi = Screen.dpi > 0 ? Screen.dpi : fallbackDpi;
        float cmPerPixel = 2.54f / dpi;
        return new Vector2(pixels.x * cmPerPixel, pixels.y * cmPerPixel);
    }

// แบ่งหน้าจอเป็น 3 โซน
    string GetScreenZone(float screenX)
    {
        float oneThird = Screen.width / 3f;
        if (screenX < oneThird) return "Left";
        else if (screenX < oneThird * 2f) return "Center";
        else return "Right";
    }

// อัพเดท Gesture Debug UI ผ่าน UIDebugClass
    void UpdateGestureDebugUI()
    {
        if (UIDebugClass.Instance == null) return;

        float dpi = Screen.dpi > 0 ? Screen.dpi : fallbackDpi;
        string info = $"=== DogPaddle (DPI: {dpi:F0}) ===\n";
        info += $"\n[1F] {oneFingerGestureState}";
        if (oneFingerGestureState != "None")
        {
            info += $" | Zone: {oneFingerSwipeZone}";
            info += $" | Vel: {currentTouchVelocityCm:F1}cm/s";
            info += $"\nDrag: X={dragDistanceCm.x:F2}cm Y={dragDistanceCm.y:F2}cm Total={totalDragDistanceCm:F2}cm";
        }
        info += $"\n[2F] {twoFingerGestureState}";
        if (twoFingerGestureState != "None")
        {
            info += $" | Zone: {twoFingerSwipeZone}";
            info += $" | Vel: {twoFingerVelocityCm:F1}cm/s";
        }

        UIDebugClass.Instance.SetGestureLog(info);
    }

// Public getters
    public Vector2 GetDragDistanceCm() => dragDistanceCm;
    public float GetTotalDragDistanceCm() => totalDragDistanceCm;
    public bool IsHolding() => isHolding;
    public string GetOneFingerGesture() => oneFingerGestureState;
    public string GetTwoFingerGesture() => twoFingerGestureState;
    public float GetOneFingerVelocityCm() => currentTouchVelocityCm;
    public float GetTwoFingerVelocityCm() => twoFingerVelocityCm;
    public string GetOneFingerZone() => oneFingerSwipeZone;
    public string GetTwoFingerZone() => twoFingerSwipeZone;
}
