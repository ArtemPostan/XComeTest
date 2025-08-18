// Assets/Scripts/World/ObstacleFieldSpawner.cs
//
// ��������� ������� ����� ����������� (server-driven).
// ���� ����� �� ���� (Collider) ���, ����� �� ������������ � ������� � ������� ��������,
// ������������ ������ ������ (� NetworkObject + ObstacleGroupGeneratorServerDriven),
// ����� ��������� ������ (ElementsCount/Radius) � ������ Spawn().
// ������ ������ ������ ��� �������� ������ ��������� ����� NetworkList, ������� �� ��������.
//
// �������� SpawnObstaclesServer() ����� ������ ������ � �� ������ ������� ����.

using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class ObstacleFieldSpawner : MonoBehaviour
{
    [Header("Area")]
    [SerializeField, Tooltip("��������� ���������/��������, �������������� ������� ������ �����������")]
    private Collider fieldCollider;

    [Header("Group Prefab (Server-Driven)")]
    [SerializeField, Tooltip("������ � NetworkObject + ObstacleGroupGeneratorServerDriven")]
    private GameObject obstacleGroupPrefab;

    [Header("Group Count")]
    [SerializeField, Tooltip("���. ����� �����")]
    private int minGroups = 3;
    [SerializeField, Tooltip("����. ����� �����")]
    private int maxGroups = 6;

    [Header("Group Params")]
    [SerializeField, Tooltip("�������� �������� ������ (������ ������)")]
    private Vector2 groupRadiusRange = new Vector2(2f, 4f);
    [SerializeField, Tooltip("�������� ����� ��������� � ������")]
    private Vector2Int elementsCountRange = new Vector2Int(3, 7);

    [Header("Placement Constraints")]
    [SerializeField, Tooltip("����������� ��������� �� ������ ������ �� ���������� ����� �� ������")]
    private float minDistanceToUnits = 2.5f;
    [SerializeField, Tooltip("�������������� �������� ����� �������� (� ����� �� ��������)")]
    private float groupsPadding = 1.0f;
    [SerializeField, Tooltip("������� ������� ����� �������� �����")]
    private int maxPlacementAttempts = 200;

    [Header("Raycast")]
    [SerializeField, Tooltip("������ ����������� (Plane/Terrain)")]
    private LayerMask groundMask = ~0;

    private readonly List<(Vector3 pos, float radius)> _placed = new();

    /// <summary>
    /// ��������� �� �������/����� ����� ������ ������.
    /// </summary>
    public void SpawnObstaclesServer()
    {
        if (!NetworkManager.Singleton || !NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[ObstacleFieldSpawner] SpawnObstaclesServer ������ ���������� �� �������/�����.");
            return;
        }

        if (fieldCollider == null)
        {
            Debug.LogError("[ObstacleFieldSpawner] �� ����� fieldCollider.");
            return;
        }
        if (obstacleGroupPrefab == null)
        {
            Debug.LogError("[ObstacleFieldSpawner] �� ����� obstacleGroupPrefab.");
            return;
        }

        _placed.Clear();

        // �������� ��������� ������� ������ �� ������ ���������
        var unitPositions = new List<Vector3>();
        foreach (var u in FindObjectsOfType<UnitNetworkBehaviour>())
            unitPositions.Add(u.transform.position);

        int targetGroups = Random.Range(minGroups, Mathf.Max(minGroups, maxGroups + 1));
        int attempts = 0;

        while (_placed.Count < targetGroups && attempts < maxPlacementAttempts)
        {
            attempts++;

            if (!TryPickPointOnField(out var posOnField))
                continue;

            float radius = Random.Range(groupRadiusRange.x, groupRadiusRange.y);
            int elements = Random.Range(elementsCountRange.x, elementsCountRange.y + 1);

            if (!IsFarFromUnits(posOnField, radius, unitPositions)) continue;
            if (!IsFarFromOtherGroups(posOnField, radius)) continue;

            // ������ ������ � ����� ��������� �� Spawn()
            var go = Instantiate(obstacleGroupPrefab, posOnField, Quaternion.identity);

            if (!go.TryGetComponent<NetworkObject>(out var no) ||
                !go.TryGetComponent<ObstacleGroupGeneratorServerDriven>(out var gen))
            {
                Debug.LogError("[ObstacleFieldSpawner] ������ ������ ������ ��������� NetworkObject � ObstacleGroupGeneratorServerDriven.");
                Destroy(go);
                continue;
            }

            gen.ElementsCount.Value = elements;
            gen.Radius.Value = radius;

            // ������� ������� ��������� ������ (���� � ���������, ������ �� NetworkList)
            no.Spawn();

            _placed.Add((posOnField, radius));
        }

        if (_placed.Count < targetGroups)
        {
            Debug.LogWarning($"[ObstacleFieldSpawner] ����������� {_placed.Count}/{targetGroups} ����� (�� ������� �������� �������?).");
        }
    }

    private bool IsFarFromUnits(Vector3 p, float radius, List<Vector3> units)
    {
        float need = radius + minDistanceToUnits;
        float needSqr = need * need;
        for (int i = 0; i < units.Count; i++)
        {
            if ((units[i] - p).sqrMagnitude < needSqr)
                return false;
        }
        return true;
    }

    private bool IsFarFromOtherGroups(Vector3 p, float radius)
    {
        for (int i = 0; i < _placed.Count; i++)
        {
            float need = radius + _placed[i].radius + groupsPadding;
            if ((p - _placed[i].pos).sqrMagnitude < need * need)
                return false;
        }
        return true;
    }

    private bool TryPickPointOnField(out Vector3 worldPoint)
    {
        worldPoint = Vector3.zero;

        var b = fieldCollider.bounds;
        float x = Random.Range(b.min.x, b.max.x);
        float z = Random.Range(b.min.z, b.max.z);

        // ��� ������ ����: ��������� �������� ������/���������
        Vector3 rayStart = new Vector3(x, b.max.y + 10f, z);
        if (Physics.Raycast(rayStart, Vector3.down, out var hit, b.size.y + 50f, groundMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider == fieldCollider)
            {
                worldPoint = hit.point;
                return true;
            }
        }
        return false;
    }
}
