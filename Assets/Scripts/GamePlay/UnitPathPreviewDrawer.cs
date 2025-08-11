// Assets/Scripts/Units/UnitPathPreviewDrawer.cs

using UnityEngine;

/// <summary>
/// Рисует превью пути от юнита к целевой точке двумя линиями:
/// зелёная — доступная дистанция в рамках MovementRemaining,
/// красная — оставшийся «недоступный» отрезок.
/// Корневой GameObject сам по себе ничего не рендерит.
/// Поддерживает авто-подстройку толщины к толщине в пикселях независимо от угла/дистанции камеры.
/// </summary>
public class UnitPathPreviewDrawer : MonoBehaviour
{
    [Header("Appearance")]
    [Tooltip("Базовая толщина линии в мировых единицах (используется если авто-масштаб отключён)")]
    [SerializeField] private float lineWidth = 0.06f;
    [SerializeField] private float yOffset = 0.05f;
    [SerializeField] private Color greenColor = new Color(0.2f, 0.95f, 0.2f, 0.9f);
    [SerializeField] private Color redColor = new Color(0.95f, 0.2f, 0.2f, 0.9f);

    [Header("Screen-space thickness")]
    [Tooltip("Держать визуальную толщину стабильной в пикселях (рекомендуется для перспективной камеры)")]
    [SerializeField] private bool autoScaleScreenWidth = true;
    [Tooltip("Желаемая толщина линии на экране в пикселях")]
    [SerializeField] private float desiredScreenWidthPx = 3f;
    [Tooltip("Ограничение минимальной толщины в мировых метрах")]
    [SerializeField] private float minWorldWidth = 0.01f;
    [Tooltip("Ограничение максимальной толщины в мировых метрах")]
    [SerializeField] private float maxWorldWidth = 0.35f;

    [Header("Editor & Depth")]
    [Tooltip("Скрыть корневой объект из иерархии, чтобы он не мешал в редакторе")]
    [SerializeField] private bool hideRootInHierarchy = true;
    [Tooltip("Использовать материал с учётом глубины (линии будут прятаться за объектами)")]
    [SerializeField] private bool useDepthAwareMaterial = false;

    private LineRenderer _green;
    private LineRenderer _red;
    private Material _sharedMat;

    // кэш для динамической подстройки толщины
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

        // Камера по умолчанию
        _cam = Camera.main;

        Hide();
        ApplyBaseWidth();
    }

    private void OnEnable()
    {
        // На случай, если камера появилась позже
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

        // Подстраиваем толщину для каждого видимого сегмента
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
            // Видно поверх (обычно без записи в depth)
            var mat = new Material(Shader.Find("Sprites/Default"));
            return mat;
        }
        else
        {
            // Материал, учитывающий глубину
            var shader = Shader.Find("Unlit/Color");
            var mat = new Material(shader);
            // Попытка включить запись глубины, если шейдер поддерживает
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
        lr.alignment = LineAlignment.View; // Billboard к камере — корректно выглядит под разными углами
        lr.numCapVertices = 8;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;

        return lr;
    }

    private void ApplyBaseWidth()
    {
        if (autoScaleScreenWidth) return; // в этом режиме ширина задаётся в LateUpdate
        if (_green != null) _green.startWidth = _green.endWidth = lineWidth;
        if (_red != null) _red.startWidth = _red.endWidth = lineWidth;
    }

    /// <summary>
    /// Обновляет линии. 
    /// start — позиция юнита; target — наведённая точка;
    /// maxReach — сколько юнит может пройти в этом ходу.
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
            // Нет хода — всё красным
            SetLine(_green, false, Vector3.zero, Vector3.zero);
            SetLine(_red, true, start, target);
            CacheSegment(ref _redVisible, ref _rA, ref _rB, true, start, target);
            CacheSegment(ref _greenVisible, ref _gA, ref _gB, false, Vector3.zero, Vector3.zero);
            return;
        }

        if (maxReach >= totalDist)
        {
            // Хватает полностью — только зелёная
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
    /// Вычисляет требуемую толщину линии в мировых метрах, чтобы она выглядела как desiredPx пикселей.
    /// Формула учитывает тип камеры (перспективная/ортографическая) и расстояние.
    /// </summary>
    private static float ComputeWorldWidthForSegment(Vector3 a, Vector3 b, Camera cam, float desiredPx)
    {
        if (cam == null || desiredPx <= 0f) return 0.01f;

        // Берём середину сегмента как репрезентативную точку
        Vector3 mid = (a + b) * 0.5f;

        if (cam.orthographic)
        {
            // В ортографической: worldPerPixel = (orthoSize*2) / screenHeight
            float worldPerPixel = (cam.orthographicSize * 2f) / Mathf.Max(1, cam.pixelHeight);
            return desiredPx * worldPerPixel;
        }
        else
        {
            // В перспективной: worldPerPixel = 2 * d * tan(fov/2) / screenHeight
            float dist = Vector3.Distance(cam.transform.position, mid);
            float worldPerPixel = 2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, cam.pixelHeight);
            return desiredPx * worldPerPixel;
        }
    }
}
