// Assets/Scripts/Core/GameSessionManager.cs

using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// ����� ���� ��� NetworkUtility �������� ����� ��� �������,
/// ���� �������� �� ������� (Host) ����� ����������� � ������� ��� ������� ������ �������� ����� ������
/// � ��� ���� (������ ������).
/// </summary>
public class GameSessionManager : MonoBehaviour
{
    [Header("Unit Settings")]
    [Tooltip("������ ����� � NetworkObject � UnitNetworkBehaviour")]
    [SerializeField] private GameObject unitPrefab;
    [Tooltip("������� ������ �������� �� ������")]
    [SerializeField] private int unitsPerPlayer = 5;
    [Tooltip("������ ������ ����� ����, � ������� ������������ �����")]
    [SerializeField] private float spawnRadius = 1.5f;

    [Header("Spawn Zones")]
    [Tooltip("�����, �������� ���� ������ ��� ������ A")]
    [SerializeField] private Transform[] spawnZonesPlayerA;
    [Tooltip("�����, �������� ���� ������ ��� ������ B")]
    [SerializeField] private Transform[] spawnZonesPlayerB;

    // ����� �� �������� ������ ��� ������ ������
    private readonly HashSet<ulong> _spawnedFor = new HashSet<ulong>();
    // ������ ������� ����������� (0-� � ����, 1-� � ������ �������� ������ � �.�.)
    private readonly List<ulong> _connectionOrder = new List<ulong>();

    private void Start()
    {
        // ������������� �� ����������� ��������
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
    }

    private void OnClientConnected(ulong clientId)
    {
        // ������ ������ �������
        if (!NetworkManager.Singleton.IsServer)
            return;

        // �� �����������
        if (_spawnedFor.Contains(clientId))
            return;

        Debug.Log($"[GameSessionManager] ����� �����������: {clientId}");
        _spawnedFor.Add(clientId);
        _connectionOrder.Add(clientId);

        // ����������, ����� ���� ��� ����� ������:
        // ���� ��� ������ � ���� A, ������ � ����� � ���� B
        bool isPlayerA = (_connectionOrder.IndexOf(clientId) == 0);
        var zones = isPlayerA ? spawnZonesPlayerA : spawnZonesPlayerB;

        // ������� unitsPerPlayer ������ � ��������� ������ ������ ������ ����
        SpawnUnitsFor(clientId, zones);

        // (�������������) ����� ��� ������� ClientRpc, ����� ���� ����� ��������, ��� �� ����� ������
    }

    private void SpawnUnitsFor(ulong ownerClientId, Transform[] zones)
    {
        if (zones == null || zones.Length == 0)
        {
            Debug.LogError("[GameSessionManager] ��� ��� ������ ��� " +
                (ownerClientId == _connectionOrder[0] ? "Player A" : "Player B"));
            return;
        }

        for (int i = 0; i < unitsPerPlayer; i++)
        {
            // �������� ���� �� ��� ���������� (���� ��� ������, ��� ������)
            var zone = zones[i % zones.Length];
            // ��������� �������� ������ ����� ������� spawnRadius
            Vector2 offset2D = Random.insideUnitCircle * spawnRadius;
            Vector3 spawnPos = zone.position + new Vector3(offset2D.x, 0, offset2D.y);

            // ��������� �� �������
            var go = Instantiate(unitPrefab, spawnPos, Quaternion.identity);
            if (go.TryGetComponent<NetworkObject>(out var no))
            {
                // ������� �������� ����� �������
                no.SpawnAsPlayerObject(ownerClientId, true);
                Debug.Log($"[GameSessionManager] ����� #{i} ��� {ownerClientId} � {spawnPos}");
            }
            else
            {
                Debug.LogError("[GameSessionManager] unitPrefab �� �������� NetworkObject!");
                Destroy(go);
            }
        }
    }
}
