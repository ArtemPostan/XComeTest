// В файле UnitPathPreviewDrawer.cs

using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class UnitPathPreviewDrawer : MonoBehaviour
{
    private LineRenderer _lineRenderer;

    // Цвета для доступной и недоступной части пути
    [SerializeField] private Color reachableColor = new Color(0.2f, 0.8f, 1f, 1f);
    [SerializeField] private Color unreachableColor = new Color(1f, 0.2f, 0.2f, 0.5f);

    private void Awake()
    {
        _lineRenderer = GetComponent<LineRenderer>();
        if (_lineRenderer == null)
        {
            _lineRenderer = gameObject.AddComponent<LineRenderer>();
        }
        SetupLineRenderer();
    }

    private void SetupLineRenderer()
    {
        _lineRenderer.enabled = false;
        _lineRenderer.positionCount = 0;
        _lineRenderer.startWidth = 1f;
        _lineRenderer.endWidth = 1f;
        _lineRenderer.material = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply"));
        _lineRenderer.textureMode = LineTextureMode.Tile;
    }

    public void Draw(Vector3[] corners, float remainingMovement)
    {
        if (corners == null || corners.Length < 2)
        {
            Hide();
            return;
        }

        _lineRenderer.enabled = true;
        _lineRenderer.positionCount = corners.Length;
        _lineRenderer.SetPositions(corners);

        // Настраиваем градиент для окрашивания пути
        var gradient = new Gradient();
        float totalPathLength = 0f;
        for (int i = 1; i < corners.Length; i++)
        {
            totalPathLength += Vector3.Distance(corners[i - 1], corners[i]);
        }

        if (totalPathLength > 0)
        {
            float reachableRatio = Mathf.Clamp01(remainingMovement / totalPathLength);

            gradient.SetKeys(
                new GradientColorKey[] {
                    new GradientColorKey(reachableColor, 0.0f),
                    new GradientColorKey(reachableColor, reachableRatio),
                    new GradientColorKey(unreachableColor, reachableRatio + 0.001f), // Резкий переход цвета
                    new GradientColorKey(unreachableColor, 1.0f)
                },
                new GradientAlphaKey[] {
                    new GradientAlphaKey(reachableColor.a, 0.0f),
                    new GradientAlphaKey(unreachableColor.a, 1.0f)
                }
            );
        }
        else
        {
            // Если пути нет, просто ставим один цвет
            gradient.SetKeys(
                new GradientColorKey[] { new GradientColorKey(reachableColor, 0.0f), new GradientColorKey(reachableColor, 1.0f) },
                new GradientAlphaKey[] { new GradientAlphaKey(reachableColor.a, 0.0f), new GradientAlphaKey(reachableColor.a, 1.0f) }
            );
        }

        _lineRenderer.colorGradient = gradient;
    }

    public void Hide()
    {
        _lineRenderer.enabled = false;
    }
}