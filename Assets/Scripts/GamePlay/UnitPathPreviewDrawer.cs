// Assets/Scripts/Units/UnitPathPreviewDrawer.cs

using UnityEngine;

/// <summary>
/// ������ ������ ���� �� ����� � ������� ����� ����� �������:
/// ������ � ��������� ��������� � ������ MovementRemaining,
/// ������� � ���������� ������������ �������.
/// �������� GameObject ��� �� ���� ������ �� ��������.
/// ������������ ����-���������� ������� � ������� � �������� ���������� �� ����/��������� ������.
/// </summary>
public class UnitPathPreviewDrawer : MonoBehaviour
{
    [Header("Appearance")]
    [Tooltip("������� ������� ����� � ������� �������� (������������ ���� ����-������� ��������)")]
    [SerializeField] private float lineWidth = 0.06f;
    [SerializeField] private float yOffset = 0.05f;
    [SerializeField] private Color greenColor = new Color(0.2f, 0.95f, 0.2f, 0.9f);
    [SerializeField] private Color redColor = new Color(0.95f, 0.2f, 0.2f, 0.9f);

    [Header("Screen-space thickness")]
    [Tooltip("������� ���������� ������� ���������� � �������� (������������� ��� ������������� ������)")]
    [SerializeField] private bool autoScaleScreenWidth = true;
    [Tooltip("�������� ������� ����� �� ������ � ��������")]
    [SerializeField] private float desiredScreenWidthPx = 3f;
    [Tooltip("����������� ����������� ������� � ������� ������")]
    [SerializeField] private float minWorldWidth = 0.01f;
    [Tooltip("����������� ������������ ������� � ������� ������")]
    [SerializeField] private float maxWorldWidth = 0.35f;

    [Header("Editor & Depth")]
    [Tooltip("������ �������� ������ �� ��������, ����� �� �� ����� � ���������")]
    [SerializeField] private bool hideRootInHierarchy = true;
    [Tooltip("������������ �������� � ������ ������� (����� ����� ��������� �� ���������)")]
    [SerializeField] private bool useDepthAwareMaterial = false;

    private LineRenderer _green;
    private LineRenderer _red;
    private Material _sharedMat;

    // ��� ��� ������������ ���������� �������
    private bool _greenVisible;
    private bool _redVisible;
    private Vector3 _gA, _gB, _rA, _rB;

    private Camera _cam;

    private void Awake()
    {
        if (hideRootInHierarchy)
            gameObject.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;

        _sharedMat = CreateMaterial(useDepthAwareMaterial);

        _green = CreateChildLine("GreenSegment", greenColor);
        _red = CreateChildLine("RedSegment", redColor);

        // ������ �� ���������
        _cam = Camera.main;

        Hide();
        ApplyBaseWidth();
    }

    private void OnEnable()
    {
        // �� ������, ���� ������ ��������� �����
        if (_cam == null) _cam = Camera.main;
    }

    private void LateUpdate()
    {
        if (!autoScaleScreenWidth) return;

        if (_cam == null)
        {
            _cam = Camera.main;
            if (_cam == null) return;
        }

        // ������������ ������� ��� ������� �������� ��������
        if (_greenVisible)
        {
            float w = ComputeWorldWidthForSegment(_gA, _gB, _cam, desiredScreenWidthPx);
            w = Mathf.Clamp(w, minWorldWidth, maxWorldWidth);
            _green.startWidth = _green.endWidth = w;
        }

        if (_redVisible)
        {
            float w = ComputeWorldWidthForSegment(_rA, _rB, _cam, desiredScreenWidthPx);
            w = Mathf.Clamp(w, minWorldWidth, maxWorldWidth);
            _red.startWidth = _red.endWidth = w;
        }
    }

    private Material CreateMaterial(bool depthAware)
    {
        if (!depthAware)
        {
            // ����� ������ (������ ��� ������ � depth)
            var mat = new Material(Shader.Find("Sprites/Default"));
            return mat;
        }
        else
        {
            // ��������, ����������� �������
            var shader = Shader.Find("Unlit/Color");
            var mat = new Material(shader);
            // ������� �������� ������ �������, ���� ������ ������������
            mat.SetInt("_ZWrite", 1);
            return mat;
        }
    }

    private LineRenderer CreateChildLine(string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();

        lr.material = _sharedMat;
        lr.positionCount = 0;
        lr.startWidth = lr.endWidth = lineWidth;
        lr.startColor = lr.endColor = color;
        lr.textureMode = LineTextureMode.Stretch;
        lr.alignment = LineAlignment.View; // Billboard � ������ � ��������� �������� ��� ������� ������
        lr.numCapVertices = 8;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;

        return lr;
    }

    private void ApplyBaseWidth()
    {
        if (autoScaleScreenWidth) return; // � ���� ������ ������ ������� � LateUpdate
        if (_green != null) _green.startWidth = _green.endWidth = lineWidth;
        if (_red != null) _red.startWidth = _red.endWidth = lineWidth;
    }

    /// <summary>
    /// ��������� �����. 
    /// start � ������� �����; target � ��������� �����;
    /// maxReach � ������� ���� ����� ������ � ���� ����.
    /// </summary>
    public void Draw(Vector3 start, Vector3 target, float maxReach)
    {
        start.y += yOffset;
        target.y += yOffset;

        Vector3 toTarget = target - start;
        float totalDist = toTarget.magnitude;

        if (totalDist <= 0.001f)
        {
            SetLine(_green, false, Vector3.zero, Vector3.zero);
            SetLine(_red, false, Vector3.zero, Vector3.zero);
            return;
        }

        if (maxReach <= 0f)
        {
            // ��� ���� � �� �������
            SetLine(_green, false, Vector3.zero, Vector3.zero);
            SetLine(_red, true, start, target);
            CacheSegment(ref _redVisible, ref _rA, ref _rB, true, start, target);
            CacheSegment(ref _greenVisible, ref _gA, ref _gB, false, Vector3.zero, Vector3.zero);
            return;
        }

        if (maxReach >= totalDist)
        {
            // ������� ��������� � ������ ������
            SetLine(_green, true, start, target);
            SetLine(_red, false, Vector3.zero, Vector3.zero);
            CacheSegment(ref _greenVisible, ref _gA, ref _gB, true, start, target);
            CacheSegment(ref _redVisible, ref _rA, ref _rB, false, Vector3.zero, Vector3.zero);
        }
        else
        {
            Vector3 dir = toTarget / totalDist;
            Vector3 splitPoint = start + dir * maxReach;

            SetLine(_green, true, start, splitPoint);
            SetLine(_red, true, splitPoint, target);

            CacheSegment(ref _greenVisible, ref _gA, ref _gB, true, start, splitPoint);
            CacheSegment(ref _redVisible, ref _rA, ref _rB, true, splitPoint, target);
        }
    }

    public void Hide()
    {
        SetLine(_green, false, Vector3.zero, Vector3.zero);
        SetLine(_red, false, Vector3.zero, Vector3.zero);
        _greenVisible = _redVisible = false;
    }

    private void SetLine(LineRenderer lr, bool enabled, Vector3 a, Vector3 b)
    {
        if (lr == null) return;
        lr.enabled = enabled;
        if (!enabled)
        {
            lr.positionCount = 0;
            return;
        }
        lr.positionCount = 2;
        lr.SetPosition(0, a);
        lr.SetPosition(1, b);
    }

    private void CacheSegment(ref bool visible, ref Vector3 A, ref Vector3 B, bool vis, Vector3 a, Vector3 b)
    {
        visible = vis;
        A = a; B = b;
    }

    /// <summary>
    /// ��������� ��������� ������� ����� � ������� ������, ����� ��� ��������� ��� desiredPx ��������.
    /// ������� ��������� ��� ������ (�������������/���������������) � ����������.
    /// </summary>
    private static float ComputeWorldWidthForSegment(Vector3 a, Vector3 b, Camera cam, float desiredPx)
    {
        if (cam == null || desiredPx <= 0f) return 0.01f;

        // ���� �������� �������� ��� ���������������� �����
        Vector3 mid = (a + b) * 0.5f;

        if (cam.orthographic)
        {
            // � ���������������: worldPerPixel = (orthoSize*2) / screenHeight
            float worldPerPixel = (cam.orthographicSize * 2f) / Mathf.Max(1, cam.pixelHeight);
            return desiredPx * worldPerPixel;
        }
        else
        {
            // � �������������: worldPerPixel = 2 * d * tan(fov/2) / screenHeight
            float dist = Vector3.Distance(cam.transform.position, mid);
            float worldPerPixel = 2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, cam.pixelHeight);
            return desiredPx * worldPerPixel;
        }
    }
}
