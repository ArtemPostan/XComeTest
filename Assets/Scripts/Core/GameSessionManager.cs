// Assets/Scripts/Core/GameSessionManager.cs
//
// ��������� ��� �����:
// - ������ ������ unitPrefab ������ ������ UnitPrefabs (�������);
// - �������� ����� SpawnLoadoutFor(ulong, int[], bool isPlayerA), ������� ������� 5 ��������� ������;
// - ������ ��������: SpawnWithOwnership (�� SpawnAsPlayerObject!) ��� ������� ������.

using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class GameSessionManager : MonoBehaviour
{
    [Header("Unit Catalog")]
    [Tooltip("������ ���� ����� ������, ��������� � ������ (���������������� � NetworkPrefabs).")]
    [SerializeField] private List<GameObject> unitPrefabs = new List<GameObject>();
    public List<GameObject> UnitPrefabs => unitPrefabs;

    [Header("Spawn Settings")]
    [Tooltip("������� ������ �� ����� (������ 5)")]
    [SerializeField] private int unitsPerPlayer = 5;
    [SerializeField] private float spawnRadius = 1.5f;

    [Header("Spawn Zones")]
    [SerializeField] private Transform[] spawnZonesPlayerA;
    [SerializeField] private Transform[] spawnZonesPlayerB;

    // ������ ��������� �� ����������� ������ �� ���������� � ����� ���� �������.

    public void SpawnLoadoutFor(ulong ownerClientId, int[] selectionIndices, bool isPlayerA)
    {
        if (!NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[GameSessionManager] SpawnLoadoutFor ������ ���������� �� �������.");
            return;
        }

        if (selectionIndices == null || selectionIndices.Length == 0)
        {
            Debug.LogError("[GameSessionManager] ������ ����-���.");
            return;
        }

        var zones = isPlayerA ? spawnZonesPlayerA : spawnZonesPlayerB;
        if (zones == null || zones.Length == 0)
        {
            Debug.LogError("[GameSessionManager] ��� ��� ������ ��� " + (isPlayerA ? "Player A" : "Player B"));
            return;
        }

        for (int i = 0; i < selectionIndices.Length && i < unitsPerPlayer; i++)
        {
            int prefabIndex = Mathf.Clamp(selectionIndices[i], 0, unitPrefabs.Count - 1);
            var prefab = unitPrefabs[prefabIndex];
            if (prefab == null)
            {
                Debug.LogError($"[GameSessionManager] Unit prefab �� ������� {prefabIndex} �� �����.");
                continue;
            }

            var zone = zones[i % zones.Length];
            Vector2 offset2D = Random.insideUnitCircle * spawnRadius;
            Vector3 spawnPos = zone.position + new Vector3(offset2D.x, 0, offset2D.y);

            var go = Instantiate(prefab, spawnPos, Quaternion.identity);
            if (go.TryGetComponent<NetworkObject>(out var no))
            {
                no.SpawnWithOwnership(ownerClientId, true);
                Debug.Log($"[GameSessionManager] Spawn {prefab.name} for {ownerClientId} at {spawnPos}");
            }
            else
            {
                Debug.LogError("[GameSessionManager] ������ �� �������� NetworkObject!");
                Destroy(go);
            }
        }
    }
}
