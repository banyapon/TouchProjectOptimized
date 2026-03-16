using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Splines;
using Unity.Mathematics;

[RequireComponent(typeof(Rigidbody))]
public class TouchSpline : MonoBehaviour
{
    [Header("Movement Settings")]
    public float baseSpeed = 6f;
    public float maxSpeed = 18f;
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
    public bool enableTwoFingerRotation = true;
    public float twoFingerRotateSpeed = 0.15f;

    [Header("Spline Switch Animation")]
    [Tooltip("ความเร็วหมุน Tangent ตามทิศ Spline ใหม่ (องศา/วินาที)")]
    public float tangentRotateSpeed = 120f;

    [Header("Branch Mapping (Hub-and-Spoke)")]
    [Tooltip("Index ของ Spline เส้นตรง (เส้น default)")]
    public int straightSplineIndex = 0;
    [Tooltip("Index ของ Spline สาขาซ้าย (-1 = ไม่มี)")]
    public int leftBranchSplineIndex = 1;
    [Tooltip("Index ของ Spline สาขาขวา (-1 = ไม่มี)")]
    public int rightBranchSplineIndex = 2;
    [Tooltip("Index ของ Spline ที่ต่อตรงผ่านแยก (ไม่ต้อง Swipe, auto-transition)  -1 = ไม่มี")]
    public int throughSplineIndex = 3;

    [Header("Junction Zone")]
    [Tooltip("T บน Spline เส้นตรงที่เป็นจุดแยก (0=ต้น, 1=ปลาย)")]
    [Range(0f, 1f)]
    public float junctionT = 1.0f;
    [Tooltip("ระยะ T รอบจุดแยกที่อนุญาตให้ Swipe เปลี่ยน Spline ได้")]
    [Range(0.01f, 0.5f)]
    public float junctionRadius = 0.15f;

    [Header("Spline Rendering (สูงสุด 4 Spline)")]
    public Color[] splineColors = new Color[4]
    {
        new Color(1.00f, 0.25f, 0.25f),   // แดง
        new Color(0.25f, 1.00f, 0.35f),   // เขียว
        new Color(0.25f, 0.55f, 1.00f),   // น้ำเงิน
        new Color(1.00f, 0.95f, 0.25f),   // เหลือง
    };
    [Min(4)]
    public int lineResolution = 64;
    public float lineWidth = 0.15f;

    //Spline state────────────────────────────────────────────────
    private SplineContainer splineContainer;
    private int currentSplineIndex = 0;
    private float currentT = 0f;           // normalized 0-1 ตำแหน่งบน spline

    //Rotation animation──────────────────────────────────────────
    private Quaternion targetRotation;
    private bool isAnimatingRotation;

    //Rigidbody───────────────────────────────────────────────────
    private Rigidbody rb;

    //Line Renderers──────────────────────────────────────────────
    private LineRenderer[] lineRenderers;

    //Gesture state───────────────────────────────────────────────
    enum GestureType { None, Hold, Drag, Swipe }
    private GestureType currentGesture = GestureType.None;

    private Vector2 touchStartPos;
    private float touchStartTime;
    private bool isTouching;
    private bool touchStartedOnUI;
    private float holdTimer;
    private bool isHolding;
    private bool gestureLocked;
    private bool isTwoFingerActive;
    private float yRotationOffset;

    //Debug data──────────────────────────────────────────────────
    private Vector2 dragDistanceCm;
    private float totalDragDistanceCm;
    private float currentVelocityCm;
    private string swipeZone;

    // =================================================================
    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.isKinematic = true;

        splineContainer = FindFirstObjectByType<SplineContainer>();
        if (splineContainer == null)
        {
            Debug.LogError("TouchSpline: ไม่พบ SplineContainer ในฉาก!");
            enabled = false;
            return;
        }

        // Fallback for common 4-spline setup:
        // 0 = straight approach, 1/2 = left/right, 3 = straight through.
        if (!ValidSplineIndex(throughSplineIndex) && splineContainer.Splines.Count > 3)
        {
            throughSplineIndex = 3;
            Debug.Log("TouchSpline: throughSplineIndex not set, auto-using Spline 3.");
        }

        currentSplineIndex = straightSplineIndex;
        SnapToNearestT();
        BuildLineRenderers();
        PlaceOnSpline(forceRotation: true);
    }

    void Update()
    {
        if (splineContainer == null) return;

        HandleTouchInput();

        //หมุน Tangent ไปทิศ Spline ใหม่ค่อยๆ
        if (isAnimatingRotation)
        {
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, targetRotation,
                tangentRotateSpeed * Time.deltaTime);

            if (Quaternion.Angle(transform.rotation, targetRotation) < 0.5f)
            {
                transform.rotation = targetRotation;
                isAnimatingRotation = false;
            }
        }

        PlaceOnSpline(forceRotation: false);
    }

    // =================================================================
    //  SPLINE SNAP & PLACEMENT
    // =================================================================

    void PlaceOnSpline(bool forceRotation)
    {
        if (!ValidSplineIndex(currentSplineIndex)) return;

        var spline = splineContainer.Splines[currentSplineIndex];
        currentT = Mathf.Clamp01(currentT);

        //ตำแหน่ง
        float3 localPos = spline.EvaluatePosition(currentT);
        Vector3 worldPos = splineContainer.transform.TransformPoint((Vector3)localPos);
        worldPos.y = transform.position.y;          // รักษาความสูงเดิม
        rb.MovePosition(worldPos);

        //หมุนตาม Tangent (เฉพาะตอนไม่กำลัง animate)
        if (!isAnimatingRotation || forceRotation)
        {
            Vector3 worldTangent = EvaluateWorldTangent(currentSplineIndex, currentT);
            if (worldTangent.sqrMagnitude > 0.001f)
            {
                Quaternion tangentRot = Quaternion.LookRotation(worldTangent, Vector3.up);
                Quaternion yOffset = Quaternion.AngleAxis(yRotationOffset, Vector3.up);
                transform.rotation = yOffset * tangentRot;
            }
        }
    }

    // หา T ที่ใกล้ position ปัจจุบันที่สุดบน spline ใหม่
    void SnapToNearestT()
    {
        if (!ValidSplineIndex(currentSplineIndex)) return;

        var spline = splineContainer.Splines[currentSplineIndex];
        Vector3 localPos = splineContainer.transform.InverseTransformPoint(transform.position);

        SplineUtility.GetNearestPoint(spline, (float3)localPos, out _, out float t);
        currentT = t;
    }

    // เปลี่ยน Spline พร้อม animate Tangent (Hub-and-Spoke)
    // swipeDir: -1 = ซ้าย, +1 = ขวา
    void SwitchSpline(int swipeDir)
    {
        if (splineContainer == null) return;

        int newIndex;

        bool isAtJunctionEntrance = currentSplineIndex == straightSplineIndex
                                  || currentSplineIndex == throughSplineIndex;

        if (isAtJunctionEntrance)
        {
            // อยู่บนเส้นตรง (approach หรือ through) → เลือก branch ตามทิศ swipe
            if (swipeDir < 0)
                newIndex = leftBranchSplineIndex;
            else
                newIndex = rightBranchSplineIndex;
        }
        else
        {
            // อยู่บน branch ใดๆ → กลับเส้นตรงเสมอ ไม่สนทิศ swipe
            newIndex = straightSplineIndex;
        }

        // ตรวจ index ถูกต้องและไม่ใช่ spline ปัจจุบัน
        if (newIndex < 0 || !ValidSplineIndex(newIndex)) return;
        if (newIndex == currentSplineIndex) return;

        currentSplineIndex = newIndex;
        SnapToNearestT();

        // Animate rotation ไปตาม Tangent ของ Spline ใหม่
        Vector3 worldTangent = EvaluateWorldTangent(currentSplineIndex, currentT);
        if (worldTangent.sqrMagnitude > 0.001f)
        {
            targetRotation = Quaternion.LookRotation(worldTangent, Vector3.up);
            isAnimatingRotation = true;
        }
    }

    Vector3 EvaluateWorldTangent(int splineIndex, float t)
    {
        if (!ValidSplineIndex(splineIndex)) return transform.forward;

        float3 localTangent = splineContainer.Splines[splineIndex].EvaluateTangent(t);
        Vector3 worldTangent = splineContainer.transform.TransformDirection((Vector3)localTangent);
        worldTangent.y = 0f;
        worldTangent.Normalize();
        return worldTangent;
    }

    float GetCurrentSplineLength()
    {
        if (!ValidSplineIndex(currentSplineIndex)) return 1f;
        return SplineUtility.CalculateLength(
            splineContainer.Splines[currentSplineIndex],
            splineContainer.transform.localToWorldMatrix);
    }

    bool ValidSplineIndex(int index)
    {
        return splineContainer != null
            && splineContainer.Splines != null
            && index >= 0
            && index < splineContainer.Splines.Count;
    }

    // =================================================================
    //  LINE RENDERERS (สี 4 สี)
    // =================================================================

    void BuildLineRenderers()
    {
        if (splineContainer == null) return;

        int count = Mathf.Min(splineContainer.Splines.Count, splineColors.Length);
        lineRenderers = new LineRenderer[count];

        for (int i = 0; i < count; i++)
        {
            GameObject go = new GameObject($"SplineLine_{i}");
            go.transform.SetParent(splineContainer.transform);

            LineRenderer lr = go.AddComponent<LineRenderer>();

            // Material
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            Color c = splineColors[i];
            mat.color = c;
            lr.material = mat;
            lr.startColor = c;
            lr.endColor = c;

            lr.startWidth = lineWidth;
            lr.endWidth = lineWidth;
            lr.useWorldSpace = true;
            lr.positionCount = lineResolution;

            // Sample positions
            var spline = splineContainer.Splines[i];
            for (int j = 0; j < lineResolution; j++)
            {
                float t = (float)j / (lineResolution - 1);
                float3 lp = spline.EvaluatePosition(t);
                lr.SetPosition(j, splineContainer.transform.TransformPoint((Vector3)lp));
            }

            lineRenderers[i] = lr;
        }
    }

    // =================================================================
    //  TOUCH INPUT
    // =================================================================

    void HandleTouchInput()
    {
        if (UIDebugClass.Instance != null && UIDebugClass.Instance.IsDebugActive)
            return;

        if (Input.touchCount == 2)
        {
            if (isTouching) ResetState();
            isTwoFingerActive = true;
            HandleTwoFingerRotation();
            return;
        }

        if (Input.touchCount != 1)
        {
            if (isTouching) ResetState();
            if (isTwoFingerActive) isTwoFingerActive = false;
            return;
        }

        if (isTwoFingerActive) { isTwoFingerActive = false; return; }

        Touch touch = Input.GetTouch(0);

        if (touch.phase == TouchPhase.Began)
            touchStartedOnUI = IsTouchOverUI(touch.fingerId);

        if (touchStartedOnUI)
        {
            if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                touchStartedOnUI = false;
            return;
        }

        switch (touch.phase)
        {
            case TouchPhase.Began:       OnBegan(touch);      break;
            case TouchPhase.Moved:       OnMoved(touch);      break;
            case TouchPhase.Stationary:  OnStationary(touch); break;
            case TouchPhase.Ended:
            case TouchPhase.Canceled:    OnEnded(touch);      break;
        }
    }

    void OnBegan(Touch touch)
    {
        touchStartPos  = touch.position;
        touchStartTime = Time.time;
        isTouching     = true;
        isHolding      = false;
        holdTimer      = 0f;
        gestureLocked  = false;
        currentGesture = GestureType.None;
        dragDistanceCm      = Vector2.zero;
        totalDragDistanceCm = 0f;
        currentVelocityCm   = 0f;
        swipeZone = GetScreenZone(touch.position.x);
        UpdateDebugUI();
    }

    void OnMoved(Touch touch)
    {
        Vector2 deltaPx = touch.position - touchStartPos;
        dragDistanceCm      = PixelsToCm(deltaPx);
        totalDragDistanceCm = dragDistanceCm.magnitude;

        float elapsed = Time.time - touchStartTime;
        currentVelocityCm = elapsed > 0f ? totalDragDistanceCm / elapsed : 0f;

        // ปลด lock Hold ถ้า drag ไกลพอ
        if (gestureLocked && currentGesture == GestureType.Hold)
        {
            if (totalDragDistanceCm > dragDeadZoneCm * 2f)
            { isHolding = false; gestureLocked = false; }
            else
            { UpdateDebugUI(); return; }
        }

        // ตรวจ Hold
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

        if (totalDragDistanceCm < dragDeadZoneCm) { UpdateDebugUI(); return; }

        currentGesture = GestureType.Drag;

        //เคลื่อนที่ตาม Spline (ลากแนวตั้ง)
        Vector2 frameDeltaCm = PixelsToCm(touch.deltaPosition);
        if (Mathf.Abs(frameDeltaCm.y) > 0.001f)
        {
            float speedMul  = baseSpeed / 5f;
            float moveSpeed = Mathf.Clamp(Mathf.Abs(frameDeltaCm.y) * cmToSpeedScale * speedMul, 0f, maxSpeed);
            float dir       = frameDeltaCm.y < 0f ? 1f : -1f;

            float length = GetCurrentSplineLength();
            if (length > 0f)
                currentT += dir * moveSpeed * Time.deltaTime / length;

            ApplyTWithJunctionTransition(dir);
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
        float elapsed       = Time.time - touchStartTime;
        Vector2 finalDeltaPx = touch.position - touchStartPos;
        Vector2 finalCm     = PixelsToCm(finalDeltaPx);
        float finalDistCm   = finalCm.magnitude;
        float finalVelocity = elapsed > 0f ? finalDistCm / elapsed : 0f;

        bool isSwipe = finalVelocity   >= swipeVelocityThresholdCm
                    && elapsed          <= swipeMaxDuration
                    && finalDistCm      >= swipeMinDistanceCm
                    && Mathf.Abs(finalDeltaPx.x) >= swipeMinPixels
                    && Mathf.Abs(finalDeltaPx.x)  > Mathf.Abs(finalDeltaPx.y);

        if (isSwipe && IsAtJunction())
        {
            currentGesture = GestureType.Swipe;
            int dir = finalDeltaPx.x > 0f ? 1 : -1;
            SwitchSpline(dir);
        }

        UpdateDebugUI();
        ResetState();
    }

    void HandleTwoFingerRotation()
    {
        if (!enableTwoFingerRotation) return;
        if (Input.touchCount != 2) return;

        Touch t0 = Input.GetTouch(0);
        Touch t1 = Input.GetTouch(1);
        float avgDeltaX = (t0.deltaPosition.x + t1.deltaPosition.x) * 0.5f;
        float rotAngle = -avgDeltaX * twoFingerRotateSpeed;
        yRotationOffset += rotAngle;
    }

    // =================================================================
    //  HELPERS
    // =================================================================

    // จัดการ T หลังเคลื่อนที่ รวมถึง auto-transition ตรงผ่านแยก
    void ApplyTWithJunctionTransition(float moveDir)
    {
        //Straight approach → ผ่าน junctionT ตรงไป
        if (currentSplineIndex == straightSplineIndex && moveDir > 0f && currentT >= junctionT)
        {
            if (ValidSplineIndex(throughSplineIndex))
                AutoTransitionTo(throughSplineIndex, 0f);
            else
                currentT = junctionT; // ไม่มี through → หยุดที่แยก
            return;
        }

        //Through spline → ถอยกลับผ่าน T=0 ← คืนสู่ straight
        if (currentSplineIndex == throughSplineIndex && moveDir < 0f && currentT < 0f)
        {
            AutoTransitionTo(straightSplineIndex, junctionT);
            return;
        }

        currentT = Mathf.Clamp01(currentT);
    }

    // เปลี่ยน spline อัตโนมัติ (ไม่ใช่ swipe) พร้อม animate tangent
    void AutoTransitionTo(int newIndex, float newT)
    {
        currentSplineIndex = newIndex;
        currentT = Mathf.Clamp01(newT);

        Vector3 wt = EvaluateWorldTangent(currentSplineIndex, currentT);
        if (wt.sqrMagnitude > 0.001f)
        {
            Quaternion tangentRot = Quaternion.LookRotation(wt, Vector3.up);
            Quaternion yOffset = Quaternion.AngleAxis(yRotationOffset, Vector3.up);
            targetRotation = yOffset * tangentRot;
            isAnimatingRotation = true;
        }
    }

    // ตรวจว่าอยู่ในระยะจุดแยกพอที่จะ Swipe เปลี่ยน Spline ได้
    bool IsAtJunction()
    {
        if (currentSplineIndex == straightSplineIndex)
            // อยู่บนเส้นตรง: ต้องอยู่ใกล้ junctionT
            return Mathf.Abs(currentT - junctionT) <= junctionRadius;
        else
            // อยู่บน branch: จุดแยกอยู่ที่ต้นสาย (T ≈ 0)
            return currentT <= junctionRadius;
    }

    void ResetState()
    {
        isTouching     = false;
        isHolding      = false;
        holdTimer      = 0f;
        gestureLocked  = false;
        currentGesture = GestureType.None;
    }

    bool IsTouchOverUI(int fingerId)
    {
        if (EventSystem.current == null) return false;
        return EventSystem.current.IsPointerOverGameObject(fingerId);
    }

    Vector2 PixelsToCm(Vector2 pixels)
    {
        float dpi = Screen.dpi > 0 ? Screen.dpi : fallbackDpi;
        return pixels * (2.54f / dpi);
    }

    string GetScreenZone(float screenX)
    {
        float third = Screen.width / 3f;
        if (screenX < third)       return "Left";
        if (screenX < third * 2f)  return "Center";
        return "Right";
    }

    // =================================================================
    //  DEBUG UI
    // =================================================================

    void UpdateDebugUI()
    {
        if (UIDebugClass.Instance == null) return;

        int total = splineContainer != null ? splineContainer.Splines.Count : 0;
        string info = $"=== TouchSpline ===\n";
        info += $"{currentGesture} | Spline: {currentSplineIndex}/{total - 1} | T: {currentT:F3}";
        info += IsAtJunction() ? " | [AT JUNCTION]" : "";
        if (isAnimatingRotation)
            info += " | [Rotating]";
        if (currentGesture != GestureType.None)
        {
            info += $"\nZone: {swipeZone} | Vel: {currentVelocityCm:F1} cm/s";
            info += $"\nDrag: X={dragDistanceCm.x:F2}cm  Y={dragDistanceCm.y:F2}cm";
        }
        UIDebugClass.Instance.SetGestureLog(info);
    }

    void OnDestroy()
    {
        if (lineRenderers == null) return;
        foreach (var lr in lineRenderers)
            if (lr != null) Destroy(lr.gameObject);
    }

    // =================================================================
    //  PUBLIC GETTERS
    // =================================================================

    public int    GetCurrentSplineIndex() => currentSplineIndex;
    public float  GetCurrentT()           => currentT;
    public string GetGestureState()       => currentGesture.ToString();
    public bool   IsHolding()             => isHolding;
}
