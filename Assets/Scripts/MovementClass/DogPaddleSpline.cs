using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

[RequireComponent(typeof(Rigidbody))]
public class DogPaddleSpline : MonoBehaviour
{
    [Header("References")]
    public Camera vrCamera;
    public LayerMask raycastLayers;
    public SplineCreator splineCreator;

    [Header("Movement Settings")]
    public Slider speedSlider;
    public float baseSpeed = 5f;
    public float maxSpeed = 15f;

    [Header("Touch Surface Settings")]
    public float fallbackDpi = 160f;

    [Header("Hold Detection")]
    public float holdPixelThreshold = 5f;
    public float holdTimeThreshold = 0.1f;

    [Header("Drag Settings (cm)")]
    public float dragDeadZoneCm = 0.1f;
    public float cmToSpeedScale = 3f;

    [Header("Swipe Detection (cm/s)")]
    public float swipeVelocityThresholdCm = 12f;
    public float swipeMaxDuration = 0.2f;
    public float swipeMinDistanceCm = 2.0f;
    public float swipeMinPixels = 50f;

    [Header("Two-Finger Rotation")]
    public bool enableYRotation = true;
    public float twoFingerRotateSpeed = 0.15f;

    [Header("Lane Settings")]
    public int laneCount = 3;
    public float laneWidth = 1.0f;
    public float laneSwitchSpeed = 8f;
    [Header("Current Branch Highlight")]
    [Range(0f, 1f)] public float activeHighlightStartRatio = 0.2f;
    [Range(0f, 1f)] public float activeHighlightEndRatio = 0.9f;
    [Header("Tangent Turn Animation")]
    public bool smoothTurnToSplineTangent = true;
    public float tangentTurnDuration = 0.2f;
    public AnimationCurve tangentTurnCurve = null;

    // --- Current state ---
    private SplineCreator.BranchType activeBranch = SplineCreator.BranchType.Main;
    private float currentDistance;
    private int currentLane;
    private float yRotationOffset;
    private bool hasReservedSelection;
    private bool isTangentTurnAnimating;
    private float tangentTurnTimer;
    private Quaternion tangentTurnFrom;
    private Quaternion tangentTurnTo;

    // --- Rigidbody ---
    private Rigidbody rb;

    // --- Internal State ---
    enum GestureType { None, Hold, Drag, Swipe, TwoFingerRotate }

    private Vector2 touchStartPos;
    private float touchStartTime;
    private bool isTouching;
    private bool touchStartedOnUI;

    private float holdTimer;
    private bool isHolding;

    private GestureType currentGesture = GestureType.None;
    private bool gestureLocked;

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
        rb.isKinematic = true;

        if (speedSlider != null)
        {
            speedSlider.value = baseSpeed;
            speedSlider.onValueChanged.AddListener(v => baseSpeed = v);
        }

        splineCreator.Build(transform.position);

        activeBranch = SplineCreator.BranchType.Main;
        currentLane = GetDefaultLaneIndex();
        currentDistance = 0f;
        hasReservedSelection = false;
        if (tangentTurnCurve == null || tangentTurnCurve.length == 0)
            tangentTurnCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        RefreshSplineHighlight();
        PlaceOnSpline();
    }

    void Update()
    {
        HandleTouchInput();
        PlaceOnSpline();
    }

    void PlaceOnSpline()
    {
        RefreshSplineHighlight();

        float length = splineCreator.GetPathLength(activeBranch);
        currentDistance = Mathf.Clamp(currentDistance, 0f, length);

        Vector3 pos = splineCreator.SamplePosition(activeBranch, currentDistance);
        Vector3 fwd = splineCreator.SampleForward(activeBranch, currentDistance);
        Vector3 finalPos = pos;
        finalPos.y = transform.position.y;

        rb.MovePosition(finalPos);

        if (fwd.sqrMagnitude > 0.001f)
        {
            Quaternion splineRot = Quaternion.LookRotation(fwd, Vector3.up);
            Quaternion yOffset = Quaternion.AngleAxis(yRotationOffset, Vector3.up);
            Quaternion desiredRotation = yOffset * splineRot;

            if (isTangentTurnAnimating)
            {
                tangentTurnTimer += Time.deltaTime;
                float duration = Mathf.Max(0.0001f, tangentTurnDuration);
                float t = Mathf.Clamp01(tangentTurnTimer / duration);
                float eased = tangentTurnCurve != null ? tangentTurnCurve.Evaluate(t) : t;
                transform.rotation = Quaternion.Slerp(tangentTurnFrom, tangentTurnTo, eased);
                if (t >= 1f)
                {
                    isTangentTurnAnimating = false;
                    transform.rotation = desiredRotation;
                }
            }
            else
            {
                transform.rotation = desiredRotation;
            }
        }
    }

    void CheckForkTransition()
    {
        float mainLength = splineCreator.MainLength;

        if (activeBranch == SplineCreator.BranchType.Main && currentDistance >= mainLength)
        {
            float overflow = currentDistance - mainLength;
            activeBranch = GetReservedBranch();

            currentDistance = overflow;
            yRotationOffset = 0f;

            if (smoothTurnToSplineTangent)
            {
                Vector3 branchFwd = splineCreator.SampleForward(activeBranch, currentDistance);
                StartTangentTurn(branchFwd);
            }

            hasReservedSelection = false;
            RefreshSplineHighlight();
        }

        else if (activeBranch != SplineCreator.BranchType.Main && currentDistance < 0f)
        {
            activeBranch = SplineCreator.BranchType.Main;
            currentDistance = mainLength;
            RefreshSplineHighlight();
        }
    }

    SplineCreator.BranchType GetReservedBranch()
    {
        if (laneCount <= 2)
            return currentLane <= 0 ? SplineCreator.BranchType.Left : SplineCreator.BranchType.Right;

        if (currentLane <= 0)
            return SplineCreator.BranchType.Left;
        if (currentLane >= laneCount - 1)
            return SplineCreator.BranchType.Right;
        return SplineCreator.BranchType.Straight;
    }

    int GetDefaultLaneIndex()
    {
        return laneCount <= 2 ? 0 : laneCount / 2;
    }

    void RefreshSplineHighlight()
    {
        if (splineCreator == null)
            return;

        SplineCreator.BranchType reservedBranch = hasReservedSelection ? GetReservedBranch() : activeBranch;
        splineCreator.HighlightBranches(activeBranch, reservedBranch, true);
    }

    SplineCreator.BranchType GetDefaultReservedBranch()
    {
        return laneCount <= 2 ? SplineCreator.BranchType.Left : SplineCreator.BranchType.Straight;
    }

    bool ShouldHighlightActiveBranch()
    {
        float pathLength = splineCreator.GetPathLength(activeBranch);
        if (pathLength <= 0f)
            return false;

        float startRatio = Mathf.Min(activeHighlightStartRatio, activeHighlightEndRatio);
        float endRatio = Mathf.Max(activeHighlightStartRatio, activeHighlightEndRatio);
        float normalizedDistance = Mathf.Clamp01(currentDistance / pathLength);
        return normalizedDistance >= startRatio && normalizedDistance <= endRatio;
    }

    // ===================================================================
    //  TOUCH INPUT
    // ===================================================================

    void HandleTouchInput()
    {
        if (UIDebugClass.Instance != null && UIDebugClass.Instance.IsDebugActive)
            return;

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
            LogDataClass.Instance.LogTouch("dogpaddle_spline", touch);

        switch (touch.phase)
        {
            case TouchPhase.Began:      OnBegan(touch);      break;
            case TouchPhase.Moved:      OnMoved(touch);      break;
            case TouchPhase.Stationary: OnStationary(touch); break;
            case TouchPhase.Ended:
            case TouchPhase.Canceled:   OnEnded(touch);      break;
        }
    }

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
        Vector2 deltaPx = touch.position - touchStartPos;
        dragDistanceCm = PixelsToCm(deltaPx);
        totalDragDistanceCm = dragDistanceCm.magnitude;

        float elapsed = Time.time - touchStartTime;
        currentVelocityCm = elapsed > 0f ? totalDragDistanceCm / elapsed : 0f;

        if (gestureLocked && currentGesture == GestureType.Hold)
        {
            if (totalDragDistanceCm > dragDeadZoneCm * 2f)
            {
                isHolding = false;
                gestureLocked = false;
            }
            else
            {
                UpdateDebugUI();
                return;
            }
        }

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

        if (totalDragDistanceCm < dragDeadZoneCm)
        {
            UpdateDebugUI();
            return;
        }

        currentGesture = GestureType.Drag;

        Vector2 frameDeltaCm = PixelsToCm(touch.deltaPosition);
        float speedMultiplier = baseSpeed / 5f;

        if (Mathf.Abs(frameDeltaCm.y) > 0.001f)
        {
            float moveSpeed = Mathf.Clamp(Mathf.Abs(frameDeltaCm.y) * cmToSpeedScale * speedMultiplier, 0f, maxSpeed);
            float moveDir = frameDeltaCm.y < 0f ? 1f : -1f;
            // กลับทิศตามที่อวตารหัน — ถ้าหมุนเกิน 90° ทิศจะกลับ
            float facingSign = Mathf.Cos(yRotationOffset * Mathf.Deg2Rad) >= 0f ? 1f : -1f;
            currentDistance += moveDir * facingSign * moveSpeed * Time.deltaTime;

            CheckForkTransition();
        }

        if (LogDataClass.Instance != null)
            LogDataClass.Instance.LogMovement("dogpaddle_spline", transform.position, baseSpeed, "Drag");

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

        bool isSwipe = finalVelocity >= swipeVelocityThresholdCm
                    && elapsed <= swipeMaxDuration
                    && finalDistCm >= swipeMinDistanceCm
                    && Mathf.Abs(finalDeltaPx.x) >= swipeMinPixels
                    && Mathf.Abs(finalDeltaPx.x) > Mathf.Abs(finalDeltaPx.y);

        if (isSwipe)
        {
            currentGesture = GestureType.Swipe;
            // กลับทิศ swipe ตามที่อวตารหัน
            float swipeFacingSign = Mathf.Cos(yRotationOffset * Mathf.Deg2Rad) >= 0f ? 1f : -1f;
            int dir = (finalDeltaPx.x > 0f ? 1 : -1) * (int)swipeFacingSign;
            if (activeBranch == SplineCreator.BranchType.Main)
            {
                int newLane = Mathf.Clamp(currentLane + dir, 0, laneCount - 1);

                if (newLane != currentLane)
                {
                    currentLane = newLane;
                    hasReservedSelection = true;
                    RefreshSplineHighlight();

                    if (LogDataClass.Instance != null)
                        LogDataClass.Instance.LogMovement("dogpaddle_spline", transform.position, 0f,
                            dir > 0 ? "SwipeRight_ReserveLane" + currentLane : "SwipeLeft_ReserveLane" + currentLane);
                }
            }
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

        float avgDeltaX = (t0.deltaPosition.x + t1.deltaPosition.x) * 0.5f;
        float rotAngle = -avgDeltaX * twoFingerRotateSpeed;   // สลับทิศแบบ World-scale เหมือน DogPaddle

        yRotationOffset += rotAngle;

        if (LogDataClass.Instance != null)
            LogDataClass.Instance.LogMovement("dogpaddle_spline", transform.position, Mathf.Abs(rotAngle), "TwoFingerRotate");

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

    void StartTangentTurn(Vector3 tangentForward)
    {
        if (tangentForward.sqrMagnitude < 0.001f)
            return;

        Quaternion splineRot = Quaternion.LookRotation(tangentForward.normalized, Vector3.up);
        Quaternion yOffset = Quaternion.AngleAxis(yRotationOffset, Vector3.up);
        tangentTurnFrom = transform.rotation;
        tangentTurnTo = yOffset * splineRot;
        tangentTurnTimer = 0f;
        isTangentTurnAnimating = true;
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
        float pathLen = splineCreator.GetPathLength(activeBranch);
        string branchName = activeBranch.ToString();

        string info = $"=== DogPaddleSpline (DPI: {dpi:F0}) ===\n";
        info += $"[{(isTwoFingerActive ? "2F" : "1F")}] {currentGesture}";
        info += $" | Speed: {baseSpeed:F1}";
        info += $"\nBranch: {branchName} | Reserved: {GetReservedBranch()} ({currentLane}/{laneCount - 1})";
        info += $" | Dist: {currentDistance:F1}/{pathLen:F1}";
        if (currentGesture == GestureType.TwoFingerRotate)
        {
            info += $" | RotY: {(enableYRotation ? "ON" : "OFF")}";
        }
        else if (currentGesture != GestureType.None)
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
    public int GetCurrentLane() => currentLane;
    public string GetActiveBranch() => activeBranch.ToString();
    public float GetSplineProgress()
    {
        float mainLen = splineCreator.MainLength;
        float branchLen = splineCreator.StraightLength;
        if (activeBranch == SplineCreator.BranchType.Left) branchLen = splineCreator.LeftLength;
        else if (activeBranch == SplineCreator.BranchType.Right) branchLen = splineCreator.RightLength;

        float total = mainLen + branchLen;
        float progress = (activeBranch == SplineCreator.BranchType.Main) ? currentDistance : mainLen + currentDistance;
        return total > 0f ? progress / total : 0f;
    }
}
