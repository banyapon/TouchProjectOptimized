using System.Collections.Generic;
using UnityEngine;

public class SplineCreator : MonoBehaviour
{
    public enum BranchType { Main, Straight, Left, Right }

    [Header("Spline Settings")]
    public float splineLength = 20f;
    public Vector3 splineDirection = Vector3.forward;
    public int segmentsPerUnit = 3;

    [Header("Fork Settings")]
    [Range(0.1f, 0.9f)]
    public float forkPointRatio = 0.4f;
    public bool forcePlusIntersection = true;
    public float leftBranchAngle = 90f;
    public float leftBranchLength = 12f;
    public float rightBranchAngle = 90f;
    public float rightBranchLength = 12f;

    [Header("Road Visual")]
    public float roadWidth = 3f;
    public Material roadMaterial;
    public Color defaultRoadColor = Color.red;
    public Color reservedRoadColor = Color.white;

    // --- Path data ---
    private Vector3[] mainPoints;
    private float[] mainDists;
    private float mainLength;

    private Vector3[] straightPoints;
    private float[] straightDists;
    private float straightLength;

    private Vector3[] leftPoints;
    private float[] leftDists;
    private float leftLength;
    private Vector3[] rightPoints;
    private float[] rightDists;
    private float rightLength;

    private Vector3 mainRight;

    // --- Fork ---
    private float forkDistance;
    private Vector3 forkPosition;
    private Vector3 forkForward;

    // --- Road Mesh ---
    private GameObject roadObject;
    private MeshRenderer roadRenderer;
    private Material mainRoadMaterial;
    private Material straightRoadMaterial;
    private Material leftRoadMaterial;
    private Material rightRoadMaterial;

    // --- Public accessors ---
    public float MainLength => mainLength;
    public float StraightLength => straightLength;
    public float LeftLength => leftLength;
    public float RightLength => rightLength;
    public float ForkDistance => forkDistance;
    public Vector3 ForkPosition => forkPosition;
    public Vector3 MainRight => mainRight;

    public void Build(Vector3 origin)
    {
        BuildAllPaths(origin);
        BuildRoadMesh();
    }

    // ===================================================================
    //  PATH CONSTRUCTION
    // ===================================================================

    void BuildAllPaths(Vector3 origin)
    {
        Vector3 dir = splineDirection.normalized;
        mainRight = Vector3.Cross(Vector3.up, dir);
        if (mainRight.sqrMagnitude < 0.001f) mainRight = Vector3.right;
        mainRight.Normalize();

        // --- Main path (start → fork) ---
        float mainDist = splineLength * forkPointRatio;
        mainPoints = BuildStraightSegment(origin, dir, mainDist, out mainDists);
        mainLength = mainDist;
        forkDistance = mainLength;
        forkPosition = mainPoints[mainPoints.Length - 1];
        forkForward = dir;

        // --- Straight branch (fork → forward) ---
        float straightDist = splineLength - mainDist;
        straightPoints = BuildStraightSegment(forkPosition, dir, straightDist, out straightDists);
        straightLength = straightDist;

        // --- Left branch (fork → angled left) ---
        Vector3 leftDir;
        if (forcePlusIntersection)
            leftDir = -mainRight;
        else
        {
            Quaternion leftRot = Quaternion.AngleAxis(-leftBranchAngle, Vector3.up);
            leftDir = leftRot * dir;
        }
        leftPoints = BuildStraightSegment(forkPosition, leftDir, leftBranchLength, out leftDists);
        leftLength = leftDists[leftDists.Length - 1];

        Vector3 rightDir;
        if (forcePlusIntersection)
            rightDir = mainRight;
        else
        {
            Quaternion rightRot = Quaternion.AngleAxis(rightBranchAngle, Vector3.up);
            rightDir = rightRot * dir;
        }
        rightPoints = BuildStraightSegment(forkPosition, rightDir, rightBranchLength, out rightDists);
        rightLength = rightDists[rightDists.Length - 1];
    }

    Vector3[] BuildStraightSegment(Vector3 start, Vector3 dir, float length, out float[] dists)
    {
        int segs = Mathf.Max(2, Mathf.RoundToInt(length * segmentsPerUnit));
        int count = segs + 1;
        Vector3[] pts = new Vector3[count];
        dists = new float[count];

        float segLen = length / segs;
        pts[0] = start;
        dists[0] = 0f;

        for (int i = 1; i < count; i++)
        {
            pts[i] = start + dir * (segLen * i);
            dists[i] = segLen * i;
        }
        return pts;
    }

    Vector3[] BuildQuadraticBezier(Vector3 p0, Vector3 p1, Vector3 p2, out float[] dists)
    {
        float approxLen = Vector3.Distance(p0, p1) + Vector3.Distance(p1, p2);
        int segs = Mathf.Max(4, Mathf.RoundToInt(approxLen * segmentsPerUnit));
        int count = segs + 1;
        Vector3[] pts = new Vector3[count];
        dists = new float[count];

        pts[0] = p0;
        dists[0] = 0f;

        for (int i = 1; i < count; i++)
        {
            float t = (float)i / segs;
            float u = 1f - t;
            pts[i] = u * u * p0 + 2f * u * t * p1 + t * t * p2;
            dists[i] = dists[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
        }
        return pts;
    }

    // ===================================================================
    //  ROAD MESH
    // ===================================================================

    void BuildRoadMesh()
    {
        if (roadObject != null)
            Destroy(roadObject);

        roadObject = new GameObject("SplineRoad");
        roadObject.transform.SetParent(transform);
        roadObject.transform.position = Vector3.zero;
        roadObject.transform.rotation = Quaternion.identity;

        MeshFilter mf = roadObject.AddComponent<MeshFilter>();
        roadRenderer = roadObject.AddComponent<MeshRenderer>();

        Mesh mesh = new Mesh { name = "ForkRoadMesh" };

        List<Vector3> allVerts = new List<Vector3>();
        List<Vector2> allUVs = new List<Vector2>();
        List<int> trisMain = new List<int>();
        List<int> trisStraight = new List<int>();
        List<int> trisLeft = new List<int>();
        List<int> trisRight = new List<int>();

        // Main path
        int offset = allVerts.Count;
        AddRoadStrip(mainPoints, mainRight, roadWidth, allVerts, allUVs, trisMain, offset);

        // Straight branch
        offset = allVerts.Count;
        AddRoadStrip(straightPoints, mainRight, roadWidth, allVerts, allUVs, trisStraight, offset);

        // Left branch
        offset = allVerts.Count;
        AddRoadStripCurved(leftPoints, roadWidth, allVerts, allUVs, trisLeft, offset);

        // Right branch
        offset = allVerts.Count;
        AddRoadStripCurved(rightPoints, roadWidth, allVerts, allUVs, trisRight, offset);

        mesh.SetVertices(allVerts);
        mesh.SetUVs(0, allUVs);
        mesh.subMeshCount = 4;
        mesh.SetTriangles(trisMain, 0);
        mesh.SetTriangles(trisStraight, 1);
        mesh.SetTriangles(trisLeft, 2);
        mesh.SetTriangles(trisRight, 3);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        mf.mesh = mesh;

        mainRoadMaterial = CreateOrUseMaterial(defaultRoadColor);
        straightRoadMaterial = CreateOrUseMaterial(defaultRoadColor);
        leftRoadMaterial = CreateOrUseMaterial(defaultRoadColor);
        rightRoadMaterial = CreateOrUseMaterial(defaultRoadColor);
        roadRenderer.materials = new Material[] { mainRoadMaterial, straightRoadMaterial, leftRoadMaterial, rightRoadMaterial };
        HighlightBranches(BranchType.Main);
    }

    void AddRoadStrip(Vector3[] pts, Vector3 right, float width,
        List<Vector3> verts, List<Vector2> uvs, List<int> tris, int vOffset)
    {
        float hw = width * 0.5f;
        int count = pts.Length;

        for (int i = 0; i < count; i++)
        {
            verts.Add(pts[i] - right * hw);
            verts.Add(pts[i] + right * hw);

            float v = (float)i / (count - 1);
            uvs.Add(new Vector2(0f, v));
            uvs.Add(new Vector2(1f, v));
        }

        for (int i = 0; i < count - 1; i++)
        {
            int bl = vOffset + i * 2;
            int br = vOffset + i * 2 + 1;
            int tl = vOffset + (i + 1) * 2;
            int tr = vOffset + (i + 1) * 2 + 1;

            tris.Add(bl); tris.Add(tl); tris.Add(br);
            tris.Add(br); tris.Add(tl); tris.Add(tr);
        }
    }

    void AddRoadStripCurved(Vector3[] pts, float width,
        List<Vector3> verts, List<Vector2> uvs, List<int> tris, int vOffset)
    {
        float hw = width * 0.5f;
        int count = pts.Length;

        for (int i = 0; i < count; i++)
        {
            Vector3 fwd;
            if (i < count - 1)
                fwd = (pts[i + 1] - pts[i]).normalized;
            else
                fwd = (pts[i] - pts[i - 1]).normalized;

            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

            verts.Add(pts[i] - right * hw);
            verts.Add(pts[i] + right * hw);

            float v = (float)i / (count - 1);
            uvs.Add(new Vector2(0f, v));
            uvs.Add(new Vector2(1f, v));
        }

        for (int i = 0; i < count - 1; i++)
        {
            int bl = vOffset + i * 2;
            int br = vOffset + i * 2 + 1;
            int tl = vOffset + (i + 1) * 2;
            int tr = vOffset + (i + 1) * 2 + 1;

            tris.Add(bl); tris.Add(tl); tris.Add(br);
            tris.Add(br); tris.Add(tl); tris.Add(tr);
        }
    }

    Material CreateOrUseMaterial(Color color)
    {
        if (roadMaterial != null)
        {
            Material m = new Material(roadMaterial);
            m.color = color;
            return m;
        }
        Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.color = color;
        return mat;
    }

    public void HighlightBranches(BranchType activeBranch, BranchType reservedBranch = BranchType.Main)
    {
        ApplyMaterialColor(mainRoadMaterial, defaultRoadColor);
        ApplyMaterialColor(straightRoadMaterial, defaultRoadColor);
        ApplyMaterialColor(leftRoadMaterial, defaultRoadColor);
        ApplyMaterialColor(rightRoadMaterial, defaultRoadColor);

        HighlightBranch(activeBranch);

        if (reservedBranch != activeBranch)
            HighlightBranch(reservedBranch);
    }

    void HighlightBranch(BranchType branch)
    {
        switch (branch)
        {
            case BranchType.Main:
                ApplyMaterialColor(mainRoadMaterial, reservedRoadColor);
                break;
            case BranchType.Left:
                ApplyMaterialColor(leftRoadMaterial, reservedRoadColor);
                break;
            case BranchType.Right:
                ApplyMaterialColor(rightRoadMaterial, reservedRoadColor);
                break;
            case BranchType.Straight:
                ApplyMaterialColor(straightRoadMaterial, reservedRoadColor);
                break;
        }
    }

    void ApplyMaterialColor(Material material, Color color)
    {
        if (material == null)
            return;

        material.color = color;
    }

    // ===================================================================
    //  SPLINE QUERY (public methods)
    // ===================================================================

    public void GetPathData(BranchType branch, out Vector3[] pts, out float[] dists, out float length)
    {
        switch (branch)
        {
            case BranchType.Left:
                pts = leftPoints; dists = leftDists; length = leftLength; return;
            case BranchType.Right:
                pts = rightPoints; dists = rightDists; length = rightLength; return;
            case BranchType.Straight:
                pts = straightPoints; dists = straightDists; length = straightLength; return;
            default:
                pts = mainPoints; dists = mainDists; length = mainLength; return;
        }
    }

    public float GetPathLength(BranchType branch)
    {
        switch (branch)
        {
            case BranchType.Left: return leftLength;
            case BranchType.Right: return rightLength;
            case BranchType.Straight: return straightLength;
            default: return mainLength;
        }
    }

    public Vector3 SamplePosition(BranchType branch, float distance)
    {
        Vector3[] pts; float[] dists; float length;
        GetPathData(branch, out pts, out dists, out length);
        return SamplePositionFromPoints(pts, dists, distance);
    }

    public Vector3 SampleTangent(BranchType branch, float distance)
    {
        Vector3[] pts; float[] dists; float length;
        GetPathData(branch, out pts, out dists, out length);
        return SampleTangentFromPoints(pts, dists, distance);
    }

    public Vector3 SampleForward(BranchType branch, float distance)
    {
        return SampleTangent(branch, distance);
    }

    public Vector3 SampleRight(BranchType branch, float distance)
    {
        Vector3 fwd = SampleForward(branch, distance);
        Vector3 r = Vector3.Cross(Vector3.up, fwd).normalized;
        if (r.sqrMagnitude < 0.001f) return mainRight;
        return r;
    }

    Vector3 SampleTangentFromPoints(Vector3[] pts, float[] dists, float distance)
    {
        if (pts.Length < 2) return splineDirection.normalized;

        float total = dists[dists.Length - 1];
        distance = Mathf.Clamp(distance, 0f, total);

        // หา segment ที่ distance ตกอยู่
        for (int i = 1; i < pts.Length; i++)
        {
            if (dists[i] >= distance)
            {
                Vector3 tangent = (pts[i] - pts[i - 1]).normalized;
                if (tangent.sqrMagnitude > 0.001f) return tangent;
                break;
            }
        }

        // fallback: ใช้ segment สุดท้าย
        Vector3 last = (pts[pts.Length - 1] - pts[pts.Length - 2]).normalized;
        if (last.sqrMagnitude > 0.001f) return last;
        return splineDirection.normalized;
    }

    Vector3 SamplePositionFromPoints(Vector3[] pts, float[] dists, float distance)
    {
        if (distance <= 0f) return pts[0];
        float total = dists[dists.Length - 1];
        if (distance >= total) return pts[pts.Length - 1];

        for (int i = 1; i < pts.Length; i++)
        {
            if (dists[i] >= distance)
            {
                float t = (distance - dists[i - 1]) / (dists[i] - dists[i - 1]);
                return Vector3.Lerp(pts[i - 1], pts[i], t);
            }
        }
        return pts[pts.Length - 1];
    }

    void OnDestroy()
    {
        if (roadObject != null)
            Destroy(roadObject);
    }
}
