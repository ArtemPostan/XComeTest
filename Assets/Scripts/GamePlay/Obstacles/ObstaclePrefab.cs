using System;
using UnityEngine;

[Serializable]
public class ObstaclePrefab
{
    public GameObject prefab;                 // БЕЗ NetworkObject
    [Header("Footprint (on XZ)")]
    public bool useCircle = true;             // true = круг, false = прямоуг.
    public float radius = 0.5f;               // если useCircle
    public Vector2 halfExtents = new(0.5f, 0.5f); // если прямоуг.

    [Header("Scale (uniform)")]
    public Vector2 scaleRange = new(0.9f, 1.2f); // случайный масштаб в пределах
    [Header("Weights")]
    [Min(0f)] public float weight = 1f;       // вероятность выбора
}
