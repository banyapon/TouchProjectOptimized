using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

[RequireComponent(typeof(Rigidbody))]
public class DogPaddle : MonoBehaviour
{
    [Header("References")]
    public Camera vrCamera;
    public LayerMask raycastLayers;

    [Header("Movement Settings")]
    public Slider speedSlider;
    public float baseSpeed = 5f;
    public float maxSpeed = 15f;

    [Header("Touch Surface Settings")]
    public float fallbackDpi = 160f;

    [Header("Hold Detection")]
    public float holdPixelThreshold = 5f;   // delta ต่ำกว่านี้ = นิ้วนิ่ง
    public float holdTimeThreshold = 0.1f;  // ต้องนิ่งนานเท่านี้ (วินาที) ถึงจะนับว่า Hold

    [Header("Drag Settings (cm)")]
    public float dragDeadZoneCm = 0.1f;    // dead zone ก่อนเริ่มเคลื่อนที่
    public float cmToSpeedScale = 3f;       // 1cm drag = speed เท่าไหร่

    [Header("Swipe Detection (cm/s)")]
    public float swipeVelocityThresholdCm = 12f;
    public float swipeMaxDuration = 0.2f;
    public float swipeMinDistanceCm = 2.0f;
    public float swipeMinPixels = 50f;
    public float swipeMoveDistance = 0.6f;

    [Header("Two-Finger Rotation")]
    public bool enableYRotation = true;         // toggle เปิด/ปิดหมุน Y
    public float twoFingerRotateSpeed = 0.15f;  // ความเร็วการหมุน (องศา/px)

    // --- Rigidbody ---
    private Rigidbody rb;
    private Vector3 desiredVelocity;
    private bool hasDragInput;

    // --- Internal State ---
    enum GestureType { None, Hold, Drag, Swipe, TwoFingerRotate }

    private Vector2 touchStartPos;
    private float touchStartTime;
    private bool isTouching;
    private bool touchStartedOnUI;

    // Hold
    private float holdTimer;
    private bool isHolding;

    // Gesture classification
    private GestureType currentGesture = GestureType.None;
    private bool gestureLocked; // เมื่อ lock แล้วจะไม่เปลี่ยน gesture จนกว่าจะปล่อยนิ้ว

    // Two-finger rotation
    private bool isTwoFingerActive;

    // Debug data
    private Vector2 dragDistanceCm;
    private float totalDragDistanceCm;
    private float currentVelocityCm;
    private string swipeZone;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        if (speedSlider != null)
        {
            speedSlider.value = baseSpeed;
            speedSlider.onValueChanged.AddListener(v => baseSpeed = v);
        }
    }

    void Update()
    {
        HandleTouchInput();
    }

    void FixedUpdate()
    {
        if (hasDragInput)
        {
            rb.linearVelocity = desiredVelocity;
            hasDragInput = false;
        }
        else
        {
            rb.linearVelocity = Vector3.zero;
        }
        desiredVelocity = Vector3.zero;
    }

    // ===== Main Touch Handler =====

    void HandleTouchInput()
    {
        if (UIDebugClass.Instance != null && UIDebugClass.Instance.IsDebugActive)
            return;

        // === Two-finger rotation ===
        if (Input.touchCount == 2)
        {
            if (isTouching) ResetState();
            HandleTwoFingerRotation();
            return;
        }

        if (Input.touchCount != 1)
        {
            if (isTouching) ResetState();
            if (isTwoFingerActive) isTwoFingerActive = false;
            return;
        }

        // ปล่อยสองนิ้วแล้วกลับมานิ้วเดียว → reset
        if (isTwoFingerActive)
        {
            isTwoFingerActive = false;
            return;
        }

        Touch touch = Input.GetTouch(0);

        if (touch.phase == TouchPhase.Began)
            touchStartedOnUI = IsTouchOverUI(touch.fingerId);

        if (touchStartedOnUI)
        {
            if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                touchStartedOnUI = false;
            return;
        }

        if (LogDataClass.Instance != null)
            LogDataClass.Instance.LogTouch("dogpaddle", touch);

        switch (touch.phase)
        {
            case TouchPhase.Began:     OnBegan(touch);      break;
            case TouchPhase.Moved:     OnMoved(touch);      break;
            case TouchPhase.Stationary: OnStationary(touch); break;
            case TouchPhase.Ended:
            case TouchPhase.Canceled:  OnEnded(touch);      break;
        }
    }

    // ===== Touch Phases =====

    void OnBegan(Touch touch)
    {
        touchStartPos = touch.position;
        touchStartTime = Time.time;
        isTouching = true;

        isHolding = false;
        holdTimer = 0f;
        gestureLocked = false;
        currentGesture = GestureType.None;

        dragDistanceCm = Vector2.zero;
        totalDragDistanceCm = 0f;
        currentVelocityCm = 0f;
        swipeZone = GetScreenZone(touch.position.x);

        UpdateDebugUI();
    }

    void OnMoved(Touch touch)
    {
        // คำนวณ drag cm จากจุดเริ่ม
        Vector2 deltaPx = touch.position - touchStartPos;
        dragDistanceCm = PixelsToCm(deltaPx);
        totalDragDistanceCm = dragDistanceCm.magnitude;

        float elapsed = Time.time - touchStartTime;
        currentVelocityCm = elapsed > 0f ? totalDragDistanceCm / elapsed : 0f;

        // ถ้า gesture ถูก lock เป็น Hold แล้ว → ต้อง drag เกิน dead zone ถึงจะปลด
        if (gestureLocked && currentGesture == GestureType.Hold)
        {
            if (totalDragDistanceCm > dragDeadZoneCm * 2f)
            {
                // ออกจาก hold, เริ่ม drag
                isHolding = false;
                gestureLocked = false;
            }
            else
            {
                UpdateDebugUI();
                return;
            }
        }

        // ตรวจ hold: นิ้วแทบไม่ขยับ
        float frameDelta = touch.deltaPosition.magnitude;
        if (frameDelta < holdPixelThreshold)
        {
            holdTimer += Time.deltaTime;
            if (holdTimer >= holdTimeThreshold && totalDragDistanceCm < dragDeadZoneCm)
            {
                isHolding = true;
                currentGesture = GestureType.Hold;
                gestureLocked = true;
                UpdateDebugUI();
                return;
            }
        }
        else
        {
            holdTimer = 0f;
            isHolding = false;
        }

        // ยังอยู่ใน dead zone → ไม่ทำอะไร
        if (totalDragDistanceCm < dragDeadZoneCm)
        {
            UpdateDebugUI();
            return;
        }

        // ===== เริ่ม Drag ทันทีหลังพ้น dead zone (ถ้าปล่อยเร็วพอจะตัดสินเป็น Swipe ใน OnEnded) =====
        currentGesture = GestureType.Drag;

        Vector2 frameDeltaCm = PixelsToCm(touch.deltaPosition);
        float speedMultiplier = baseSpeed / 5f; // normalize จาก default baseSpeed=5
        Vector3 movement = Vector3.zero;

        // แนวตั้ง: ลากลง (deltaY-) = ไปหน้า, ลากขึ้น (deltaY+) = ถอยหลัง
        if (Mathf.Abs(frameDeltaCm.y) > 0.001f)
        {
            float fwdSpeed = Mathf.Clamp(Mathf.Abs(frameDeltaCm.y) * cmToSpeedScale * speedMultiplier, 0f, maxSpeed);
            float fwdDir = frameDeltaCm.y < 0f ? 1f : -1f;
            movement += GetHorizontalForward() * fwdDir * fwdSpeed;
        }

        // แนวนอน: ลากขวา (deltaX+) = ไปขวา, ลากซ้าย (deltaX-) = ไปซ้าย
        if (Mathf.Abs(frameDeltaCm.x) > 0.001f)
        {
            float strafeSpeed = Mathf.Clamp(Mathf.Abs(frameDeltaCm.x) * cmToSpeedScale * speedMultiplier, 0f, maxSpeed);
            float strafeDir = frameDeltaCm.x > 0f ? -1f : 1f;
            movement += GetHorizontalRight() * strafeDir * strafeSpeed;
        }

        if (movement.sqrMagnitude > 0f)
        {
            desiredVelocity = movement / Time.deltaTime;
            hasDragInput = true;

            if (LogDataClass.Instance != null)
                LogDataClass.Instance.LogMovement("dogpaddle", transform.position, desiredVelocity.magnitude, "Drag");
        }

        UpdateDebugUI();
    }

    void OnStationary(Touch touch)
    {
        holdTimer += Time.deltaTime;
        isHolding = true;
        currentGesture = GestureType.Hold;
        gestureLocked = true;
        UpdateDebugUI();
    }

    void OnEnded(Touch touch)
    {
        float elapsed = Time.time - touchStartTime;
        Vector2 finalDeltaPx = touch.position - touchStartPos;
        Vector2 finalCm = PixelsToCm(finalDeltaPx);
        float finalDistCm = finalCm.magnitude;
        float finalVelocity = elapsed > 0f ? finalDistCm / elapsed : 0f;

        // ===== Swipe Detection (ตอนปล่อยนิ้ว) =====
        // ต้อง: velocity สูง + ระยะเวลาสั้น + drag ไกลพอ + แนว horizontal เด่น
        bool isSwipe = finalVelocity >= swipeVelocityThresholdCm
                    && elapsed <= swipeMaxDuration
                    && finalDistCm >= swipeMinDistanceCm
                    && Mathf.Abs(finalDeltaPx.x) >= swipeMinPixels
                    && Mathf.Abs(finalDeltaPx.x) > Mathf.Abs(finalDeltaPx.y);

        if (isSwipe)
        {
            currentGesture = GestureType.Swipe;
            float dir = finalDeltaPx.x > 0f ? 1f : -1f;
            Vector3 swipeDir = GetHorizontalRight() * dir;
            float dist = swipeMoveDistance;

            // SweepTest: ตรวจ collider ก่อนเลื่อน ถ้าชนให้หยุดก่อนถึง
            RaycastHit hit;
            if (rb.SweepTest(swipeDir, out hit, dist))
            {
                dist = Mathf.Max(0f, hit.distance - 0.01f);
            }

            if (dist > 0f)
                rb.MovePosition(rb.position + swipeDir * dist);

            if (LogDataClass.Instance != null)
                LogDataClass.Instance.LogMovement("dogpaddle", transform.position, 0f,
                    dir > 0f ? "SwipeRight" : "SwipeLeft");
        }

        UpdateDebugUI();
        ResetState();
    }

    // ===== Two-Finger Rotation =====

    void HandleTwoFingerRotation()
    {
        if (!enableYRotation) return;

        isTwoFingerActive = true;
        currentGesture = GestureType.TwoFingerRotate;

        Touch t0 = Input.GetTouch(0);
        Touch t1 = Input.GetTouch(1);

        // ใช้ค่าเฉลี่ย deltaX ของสองนิ้ว → drag ซ้าย/ขวา = หมุน Y
        float avgDeltaX = (t0.deltaPosition.x + t1.deltaPosition.x) * 0.5f;
        float rotAngle = -avgDeltaX * twoFingerRotateSpeed;

        transform.Rotate(0f, rotAngle, 0f, Space.World);

        if (LogDataClass.Instance != null)
            LogDataClass.Instance.LogMovement("dogpaddle", transform.position, Mathf.Abs(rotAngle), "TwoFingerRotate");

        UpdateDebugUI();
    }

    // ===== Helpers =====

    void ResetState()
    {
        isTouching = false;
        isHolding = false;
        holdTimer = 0f;
        gestureLocked = false;
        currentGesture = GestureType.None;
    }

    bool IsTouchOverUI(int fingerId)
    {
        if (EventSystem.current == null) return false;
        return EventSystem.current.IsPointerOverGameObject(fingerId);
    }

    Vector3 GetHorizontalForward()
    {
        Vector3 fwd = transform.forward;
        fwd.y = 0f;
        fwd.Normalize();
        if (fwd.sqrMagnitude < 0.001f) return Vector3.forward;
        return fwd;
    }

    Vector3 GetHorizontalRight()
    {
        Vector3 right = transform.right;
        right.y = 0f;
        right.Normalize();
        if (right.sqrMagnitude < 0.001f) return Vector3.right;
        return right;
    }

    Vector2 PixelsToCm(Vector2 pixels)
    {
        float dpi = Screen.dpi > 0 ? Screen.dpi : fallbackDpi;
        float cmPerPx = 2.54f / dpi;
        return new Vector2(pixels.x * cmPerPx, pixels.y * cmPerPx);
    }

    string GetScreenZone(float screenX)
    {
        float third = Screen.width / 3f;
        if (screenX < third) return "Left";
        if (screenX < third * 2f) return "Center";
        return "Right";
    }

    // ===== Debug UI =====

    void UpdateDebugUI()
    {
        if (UIDebugClass.Instance == null) return;

        float dpi = Screen.dpi > 0 ? Screen.dpi : fallbackDpi;
        string info = $"=== DogPaddle (DPI: {dpi:F0}) ===\n";
        info += $"[{(isTwoFingerActive ? "2F" : "1F")}] {currentGesture}";
        info += $" | Speed: {baseSpeed:F1}";
        if (currentGesture == GestureType.TwoFingerRotate)
        {
            info += $" | RotY: {(enableYRotation ? "ON" : "OFF")}";
        }
        else if (currentGesture != GestureType.None)
        {
            info += $" | Zone: {swipeZone}";
            info += $" | Vel: {currentVelocityCm:F1}cm/s (SwipeThr: {swipeVelocityThresholdCm:F0})";
            info += $"\nDrag: X={dragDistanceCm.x:F2}cm Y={dragDistanceCm.y:F2}cm Total={totalDragDistanceCm:F2}cm";
        }
        UIDebugClass.Instance.SetGestureLog(info);
    }

    // ===== Public Getters =====

    public Vector2 GetDragDistanceCm() => dragDistanceCm;
    public float GetTotalDragDistanceCm() => totalDragDistanceCm;
    public bool IsHolding() => isHolding;
    public string GetGestureState() => currentGesture.ToString();
    public float GetVelocityCm() => currentVelocityCm;
    public string GetSwipeZone() => swipeZone;
}
