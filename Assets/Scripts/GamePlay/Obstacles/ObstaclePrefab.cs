using System;
using UnityEngine;

[Serializable]
public class ObstaclePrefab
{
    public GameObject prefab;                 // ��� NetworkObject
    [Header("Footprint (on XZ)")]
    public bool useCircle = true;             // true = ����, false = �������.
    public float radius = 0.5f;               // ���� useCircle
    public Vector2 halfExtents = new(0.5f, 0.5f); // ���� �������.

    [Header("Scale (uniform)")]
    public Vector2 scaleRange = new(0.9f, 1.2f); // ��������� ������� � ��������
    [Header("Weights")]
    [Min(0f)] public float weight = 1f;       // ����������� ������
}
