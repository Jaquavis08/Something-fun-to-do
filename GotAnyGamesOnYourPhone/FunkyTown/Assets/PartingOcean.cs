using System.Collections.Generic;
using UnityEngine;

public class PartingOcean : MonoBehaviour
{
    [Header("Source (required)")]
    [Tooltip("MeshFilter of the water plane to split. Must be a static planar mesh (not GPU-generated).")]
    public MeshFilter sourceMeshFilter;

    [Tooltip("Optional: use a world-space Transform X as the center of the split. If null, the source's local X = 0 is used.")]
    public Transform splitCenterReference;

    [Header("Parting settings")]
    [Tooltip("Maximum total separation distance (meters).")]
    public float maxGap = 8f;

    [Tooltip("How fast the gap opens/closes (units per second).")]
    public float speed = 4f;

    [Tooltip("If true the part animates when calling Open/Close or toggled by key.")]
    public bool animate = true;

    [Header("Controls")]
    [Tooltip("Keyboard key to toggle open/close in Play mode.")]
    public KeyCode toggleKey = KeyCode.Space;

    // Runtime
    Mesh leftMesh;
    Mesh rightMesh;
    GameObject leftGO;
    GameObject rightGO;
    MeshRenderer sourceRenderer;
    float currentGap = 0f;
    bool isOpen = false;

    void Start()
    {
        Initialize();
    }

    void OnValidate()
    {
        // Try to keep editor-time feedback
        // Avoid expensive rebuilds constantly in the editor; only rebuild when things are set.
        if (sourceMeshFilter != null)
            Initialize();
    }

    void Initialize()
    {
        if (sourceMeshFilter == null)
            return;

        // Ensure source renderer exists
        sourceRenderer = sourceMeshFilter.GetComponent<MeshRenderer>();
        if (sourceRenderer == null)
        {
            Debug.LogWarning("PartingOcean: sourceMeshFilter has no MeshRenderer.");
            return;
        }

        // Create or find child GOs
        if (leftGO == null || rightGO == null)
        {
            // If existing from previous run, destroy to rebuild
            if (leftGO != null) { DestroyImmediate(leftGO); }
            if (rightGO != null) { DestroyImmediate(rightGO); }

            leftGO = new GameObject("Water_Left");
            rightGO = new GameObject("Water_Right");

            leftGO.transform.SetParent(transform, false);
            rightGO.transform.SetParent(transform, false);

            leftGO.hideFlags = HideFlags.DontSave;
            rightGO.hideFlags = HideFlags.DontSave;

            leftGO.AddComponent<MeshFilter>();
            leftGO.AddComponent<MeshRenderer>();

            rightGO.AddComponent<MeshFilter>();
            rightGO.AddComponent<MeshRenderer>();
        }

        // Disable the original renderer to avoid double-render
        sourceRenderer.enabled = false;

        // Build split meshes
        BuildSplitMeshes();
    }

    void BuildSplitMeshes()
    {
        var srcMesh = sourceMeshFilter.sharedMesh;
        if (srcMesh == null)
        {
            Debug.LogWarning("PartingOcean: sourceMeshFilter has no mesh assigned.");
            return;
        }

        // Convert vertices to local to source (mesh vertices are already in mesh local)
        Vector3[] vertices = srcMesh.vertices;
        int[] tris = srcMesh.triangles;
        Vector3[] normals = srcMesh.normals;
        Vector2[] uvs = srcMesh.uv;

        // We'll use the source transform to interpret positions in world/local correctly
        // Calculate the split plane in the source's local space.
        float splitXLocal = 0f;
        if (splitCenterReference != null)
        {
            // world position of reference -> convert to local of source
            Vector3 worldRef = splitCenterReference.position;
            splitXLocal = sourceMeshFilter.transform.InverseTransformPoint(worldRef).x;
        }
        else
        {
            // default center at local X=0 of source's transform
            splitXLocal = 0f;
        }

        // Mappings
        var leftVerts = new List<Vector3>();
        var leftNormals = new List<Vector3>();
        var leftUVs = new List<Vector2>();
        var leftTris = new List<int>();
        var rightVerts = new List<Vector3>();
        var rightNormals = new List<Vector3>();
        var rightUVs = new List<Vector2>();
        var rightTris = new List<int>();

        var leftIndexMap = new Dictionary<int, int>();
        var rightIndexMap = new Dictionary<int, int>();

        // Decide side per vertex
        var vertexSides = new bool[vertices.Length]; // true = right, false = left
        for (int i = 0; i < vertices.Length; i++)
        {
            vertexSides[i] = vertices[i].x > splitXLocal;
        }

        // Helper to add a vertex to a mesh list and return new index
        int AddVertexToSide(int originalIndex, bool toRight)
        {
            if (toRight)
            {
                if (rightIndexMap.TryGetValue(originalIndex, out int idx)) return idx;
                int newIdx = rightVerts.Count;
                rightIndexMap[originalIndex] = newIdx;
                rightVerts.Add(vertices[originalIndex]);
                if (normals != null && normals.Length > 0) rightNormals.Add(normals[originalIndex]);
                if (uvs != null && uvs.Length > 0) rightUVs.Add(uvs[originalIndex]);
                return newIdx;
            }
            else
            {
                if (leftIndexMap.TryGetValue(originalIndex, out int idx)) return idx;
                int newIdx = leftVerts.Count;
                leftIndexMap[originalIndex] = newIdx;
                leftVerts.Add(vertices[originalIndex]);
                if (normals != null && normals.Length > 0) leftNormals.Add(normals[originalIndex]);
                if (uvs != null && uvs.Length > 0) leftUVs.Add(uvs[originalIndex]);
                return newIdx;
            }
        }

        // Iterate triangles and assign to left or right if all vertices on same side.
        for (int t = 0; t < tris.Length; t += 3)
        {
            int i0 = tris[t];
            int i1 = tris[t + 1];
            int i2 = tris[t + 2];

            bool s0 = vertexSides[i0];
            bool s1 = vertexSides[i1];
            bool s2 = vertexSides[i2];

            // All on right
            if (s0 && s1 && s2)
            {
                int ni0 = AddVertexToSide(i0, true);
                int ni1 = AddVertexToSide(i1, true);
                int ni2 = AddVertexToSide(i2, true);
                rightTris.Add(ni0);
                rightTris.Add(ni1);
                rightTris.Add(ni2);
            }
            // All on left
            else if (!s0 && !s1 && !s2)
            {
                int ni0 = AddVertexToSide(i0, false);
                int ni1 = AddVertexToSide(i1, false);
                int ni2 = AddVertexToSide(i2, false);
                leftTris.Add(ni0);
                leftTris.Add(ni1);
                leftTris.Add(ni2);
            }
            else
            {
                // Triangles that cross the split plane are dropped to create a clean seam.
                // A more advanced approach would re-triangulate and cap the hole.
            }
        }

        // Create Meshes
        if (leftMesh != null) { DestroyImmediate(leftMesh); leftMesh = null; }
        if (rightMesh != null) { DestroyImmediate(rightMesh); rightMesh = null; }

        leftMesh = new Mesh { name = srcMesh.name + "_Left" };
        rightMesh = new Mesh { name = srcMesh.name + "_Right" };

        leftMesh.SetVertices(leftVerts);
        leftMesh.SetTriangles(leftTris, 0);
        if (leftNormals.Count == leftVerts.Count) leftMesh.SetNormals(leftNormals);
        if (leftUVs.Count == leftVerts.Count) leftMesh.SetUVs(0, leftUVs);
        leftMesh.RecalculateBounds();
        if (leftNormals.Count != leftVerts.Count) leftMesh.RecalculateNormals();

        rightMesh.SetVertices(rightVerts);
        rightMesh.SetTriangles(rightTris, 0);
        if (rightNormals.Count == rightVerts.Count) rightMesh.SetNormals(rightNormals);
        if (rightUVs.Count == rightVerts.Count) rightMesh.SetUVs(0, rightUVs);
        rightMesh.RecalculateBounds();
        if (rightNormals.Count != rightVerts.Count) rightMesh.RecalculateNormals();

        // Assign meshes and materials to our created GO's
        var leftFilter = leftGO.GetComponent<MeshFilter>();
        var rightFilter = rightGO.GetComponent<MeshFilter>();
        var leftRenderer = leftGO.GetComponent<MeshRenderer>();
        var rightRenderer = rightGO.GetComponent<MeshRenderer>();

        leftFilter.sharedMesh = leftMesh;
        rightFilter.sharedMesh = rightMesh;

        // Copy renderer settings/material
        leftRenderer.sharedMaterials = sourceRenderer.sharedMaterials;
        rightRenderer.sharedMaterials = sourceRenderer.sharedMaterials;

        // Align transforms with source
        leftGO.transform.localPosition = Vector3.zero;
        leftGO.transform.localRotation = Quaternion.identity;
        leftGO.transform.localScale = Vector3.one;
        rightGO.transform.localPosition = Vector3.zero;
        rightGO.transform.localRotation = Quaternion.identity;
        rightGO.transform.localScale = Vector3.one;

        // Reset gap state
        currentGap = 0f;
        ApplyGapImmediate();
    }

    void Update()
    {
        if (Application.isPlaying)
        {
            if (Input.GetKeyDown(toggleKey))
                Toggle();

            float target = isOpen ? maxGap : 0f;
            if (animate)
            {
                currentGap = Mathf.MoveTowards(currentGap, target, speed * Time.deltaTime);
                ApplyGapImmediate();
            }
            else
            {
                if (Mathf.Approximately(currentGap, target) == false)
                {
                    currentGap = target;
                    ApplyGapImmediate();
                }
            }
        }
        else
        {
            // Editor preview: keep meshes aligned but don't animate
            // (no automatic input handling)
            if (leftMesh == null || rightMesh == null)
            {
                if (sourceMeshFilter != null)
                    Initialize();
            }
        }
    }

    void ApplyGapImmediate()
    {
        if (leftGO == null || rightGO == null) return;

        float half = currentGap * 0.5f;

        // Move left and right GameObjects locally on X
        leftGO.transform.localPosition = new Vector3(-half, 0f, 0f);
        rightGO.transform.localPosition = new Vector3(+half, 0f, 0f);
    }

    /// <summary>
    /// Open the sea (part)
    /// </summary>
    public void Open()
    {
        isOpen = true;
        if (!animate) { currentGap = maxGap; ApplyGapImmediate(); }
    }

    /// <summary>
    /// Close the sea (bring back together)
    /// </summary>
    public void Close()
    {
        isOpen = false;
        if (!animate) { currentGap = 0f; ApplyGapImmediate(); }
    }

    /// <summary>
    /// Toggle open/close
    /// </summary>
    public void Toggle()
    {
        isOpen = !isOpen;
    }

    void OnDestroy()
    {
        if (leftMesh != null) { DestroyImmediate(leftMesh); leftMesh = null; }
        if (rightMesh != null) { DestroyImmediate(rightMesh); rightMesh = null; }
    }
}
