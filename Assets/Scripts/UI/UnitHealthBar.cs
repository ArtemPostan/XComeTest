// Assets/Scripts/Units/UnitHealthBar.cs
//
// ВЕШАЕМ НА ДОЧЕРНИЙ ОБЪЕКТ ПРЕФАБА ЮНИТА С Canvas (Render Mode = World Space)
// и Image внутри (type = Filled, Fill Method = Horizontal).
// Скрипт читает здоровье из UnitNetworkBehaviour.Health и обновляет полоску.
// Полоска всегда смотрит на камеру, масштабируется под расстояние и может скрываться при полном HP.

using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

[DefaultExecutionOrder(210)]
[RequireComponent(typeof(Canvas))]
public class UnitHealthBar : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Юнит, здоровье которого показываем. Если не задан, найдётся в родителях.")]
    [SerializeField] private UnitNetworkBehaviour unit;

    [Tooltip("Image c типом Filled (Horizontal). Если не задан — возьмём первый в детях.")]
    [SerializeField] private Image fillImage;
    

    [Header("Appearance")]
    [Tooltip("Скрывать полоску, когда здоровье полно")]
    [SerializeField] private bool hideWhenFull = true;

    [Tooltip("Градиент цвета (от красного к зелёному)")]
    [SerializeField] private Gradient colorByHealth = DefaultGradient();

    [Header("Facing & Scale")]
    [Tooltip("Поворачивать бар к камере")]
    [SerializeField] private bool billboardToCamera = true;

   

    private Canvas _canvas;
    private RectTransform _rect;
    private Camera _cam;

    private void Awake()
    {
        _canvas = GetComponent<Canvas>();
        _rect = GetComponent<RectTransform>();
        if (_canvas.renderMode != RenderMode.WorldSpace)
        {
            Debug.LogWarning("[UnitHealthBar] Canvas должен быть World Space. Меняю автоматически.");
            _canvas.renderMode = RenderMode.WorldSpace;
        }

        if (unit == null)
            unit = GetComponentInParent<UnitNetworkBehaviour>();

        if (fillImage == null)
            fillImage = GetComponentInChildren<Image>(true);

        _cam = Camera.main;
    }

    private void OnEnable()
    {
        if (unit != null)
        {
            unit.Health.OnValueChanged += OnHealthChanged;
            // Принудительное обновление
            OnHealthChanged(unit.Health.Value, unit.Health.Value);
        }
    }

    private void OnDisable()
    {
        if (unit != null)
            unit.Health.OnValueChanged -= OnHealthChanged;
    }

    private void LateUpdate()
    {
        if (unit == null || fillImage == null) return;

        if (_cam == null) _cam = Camera.main;        

        // ориентация
        if (billboardToCamera && _cam != null)
        {
            var fwd = _cam.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
        }       
        
    }

    private void OnHealthChanged(int oldValue, int newValue)
    {
        if (unit == null || fillImage == null) return;

        float maxHP = Mathf.Max(1, unit.MaxHealth);
        float t = Mathf.Clamp01(newValue / maxHP);

        fillImage.fillAmount = t;
        fillImage.color = colorByHealth.Evaluate(t);

        //// Прятать при полном здоровье (опционально)
        //if (hideWhenFull)
        //    _canvas.enabled = t < 0.999f;
        //else
        //    _canvas.enabled = true;
    }

    private static float ComputeWorldSizeForPixels(Camera cam, float pixels, Vector3 worldPos)
    {
        if (cam.orthographic)
        {
            float worldPerPixel = (cam.orthographicSize * 2f) / Mathf.Max(1, cam.pixelHeight);
            return pixels * worldPerPixel;
        }
        else
        {
            float dist = Vector3.Distance(cam.transform.position, worldPos);
            float worldPerPixel = 2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, cam.pixelHeight);
            return pixels * worldPerPixel;
        }
    }

    private static Gradient DefaultGradient()
    {
        var g = new Gradient();
        g.colorKeys = new[]
        {
            new GradientColorKey(new Color(0.9f, 0.1f, 0.1f), 0f),
            new GradientColorKey(new Color(1.0f, 0.9f, 0.1f), 0.5f),
            new GradientColorKey(new Color(0.2f, 0.95f, 0.2f), 1f),
        };
        g.alphaKeys = new[]
        {
            new GradientAlphaKey(1f, 0f),
            new GradientAlphaKey(1f, 1f)
        };
        return g;
    }
}
