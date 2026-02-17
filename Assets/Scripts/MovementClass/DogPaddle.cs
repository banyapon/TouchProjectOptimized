using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

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
    public float holdTimeThreshold = 0.2f;  // ต้องนิ่งนานเท่านี้ (วินาที) ถึงจะนับว่า Hold

    [Header("Drag Settings (cm)")]
    public float dragDeadZoneCm = 0.15f;    // dead zone ก่อนเริ่มเคลื่อนที่
    public float cmToSpeedScale = 3f;       // 1cm drag = speed เท่าไหร่

    [Header("Swipe Detection (cm/s)")]
    public float swipeVelocityThresholdCm = 12f;
    public float swipeMaxDuration = 0.4f;
    public float swipeMinDistanceCm = 1.5f;
    public float swipeMinPixels = 50f;
    public float swipeMoveDistance = 0.6f;

    // --- Internal State ---
    enum GestureType { None, Hold, Drag, Swipe }

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

    // Debug data
    private Vector2 dragDistanceCm;
    private float totalDragDistanceCm;
    private float currentVelocityCm;
    private string swipeZone;

    void Start()
    {
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

    // ===== Main Touch Handler =====

    void HandleTouchInput()
    {
        if (UIDebugClass.Instance != null && UIDebugClass.Instance.IsDebugActive)
            return;

        if (Input.touchCount != 1)
        {
            if (isTouching) ResetState();
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

        // ===== Drag Realtime Movement (ใช้ per-frame delta → สลับทิศทันที) =====
        currentGesture = GestureType.Drag;

        Vector2 frameDeltaCm = PixelsToCm(touch.deltaPosition);
        Vector3 movement = Vector3.zero;

        // แนวตั้ง: ลากลง (deltaY-) = ไปหน้า, ลากขึ้น (deltaY+) = ถอยหลัง
        if (Mathf.Abs(frameDeltaCm.y) > 0.001f)
        {
            float fwdSpeed = Mathf.Clamp(Mathf.Abs(frameDeltaCm.y) * cmToSpeedScale, 0f, maxSpeed);
            float fwdDir = frameDeltaCm.y < 0f ? 1f : -1f;
            movement += GetHorizontalForward() * fwdDir * fwdSpeed;
        }

        // แนวนอน: ลากขวา (deltaX+) = ไปขวา, ลากซ้าย (deltaX-) = ไปซ้าย
        if (Mathf.Abs(frameDeltaCm.x) > 0.001f)
        {
            float strafeSpeed = Mathf.Clamp(Mathf.Abs(frameDeltaCm.x) * cmToSpeedScale, 0f, maxSpeed);
            float strafeDir = frameDeltaCm.x > 0f ? 1f : -1f;
            movement += GetHorizontalRight() * strafeDir * strafeSpeed;
        }

        if (movement.sqrMagnitude > 0f)
        {
            transform.position += movement;

            if (LogDataClass.Instance != null)
                LogDataClass.Instance.LogMovement("dogpaddle", transform.position, movement.magnitude / Time.deltaTime, "Drag");
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
            transform.position += GetHorizontalRight() * dir * swipeMoveDistance;

            if (LogDataClass.Instance != null)
                LogDataClass.Instance.LogMovement("dogpaddle", transform.position, 0f,
                    dir > 0f ? "SwipeRight" : "SwipeLeft");
        }

        UpdateDebugUI();
        ResetState();
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
        if (vrCamera != null)
        {
            Vector3 fwd = vrCamera.transform.forward;
            fwd.y = 0f;
            fwd.Normalize();
            if (fwd.sqrMagnitude < 0.001f) return transform.forward;
            return fwd;
        }
        return transform.forward;
    }

    Vector3 GetHorizontalRight()
    {
        if (vrCamera != null)
        {
            Vector3 right = vrCamera.transform.right;
            right.y = 0f;
            right.Normalize();
            if (right.sqrMagnitude < 0.001f) return transform.right;
            return right;
        }
        return transform.right;
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
        info += $"[1F] {currentGesture}";
        if (currentGesture != GestureType.None)
        {
            info += $" | Zone: {swipeZone}";
            info += $" | Vel: {currentVelocityCm:F1}cm/s";
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
