using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class UnitAttackRadiusDrawer : MonoBehaviour
{
    // Количество сегментов для отрисовки круга
    [SerializeField] private int segments = 50;

    private LineRenderer _lineRenderer;
    private float _radius;

    private void Awake()
    {
        _lineRenderer = GetComponent<LineRenderer>();
        // Это ключевое исправление: LineRenderer должен использовать локальные координаты
        _lineRenderer.useWorldSpace = false;
        _lineRenderer.enabled = false;
    }

    public void SetRadius(float radius)
    {
        _radius = radius;
        DrawCircle();
    }

    public void ShowRadius()
    {
        _lineRenderer.enabled = true;
    }

    public void HideRadius()
    {
        _lineRenderer.enabled = false;
    }

    private void DrawCircle()
    {
        _lineRenderer.positionCount = segments + 1;
        _lineRenderer.startWidth = 0.1f;
        _lineRenderer.endWidth = 0.1f;

        float angle = 0f;
        for (int i = 0; i < segments + 1; i++)
        {
            float x = Mathf.Sin(Mathf.Deg2Rad * angle) * _radius;
            float z = Mathf.Cos(Mathf.Deg2Rad * angle) * _radius;
            _lineRenderer.SetPosition(i, new Vector3(x, 0.1f, z)); // Немного приподнимаем, чтобы не конфликтовало с землёй
            angle += (360f / segments);
        }
    }
}