// Assets/Scripts/Core/GameSessionManager.cs
//
// Обновлено под драфт:
// - вместо одного unitPrefab теперь список UnitPrefabs (каталог);
// - добавлен метод SpawnLoadoutFor(ulong, int[], bool isPlayerA), который спавнит 5 выбранных юнитов;
// - фиксим владение: SpawnWithOwnership (НЕ SpawnAsPlayerObject!) для обычных юнитов.

using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class GameSessionManager : MonoBehaviour
{
    [Header("Unit Catalog")]
    [Tooltip("Список всех типов юнитов, доступных в драфте (зарегистрированы в NetworkPrefabs).")]
    [SerializeField] private List<GameObject> unitPrefabs = new List<GameObject>();
    public List<GameObject> UnitPrefabs => unitPrefabs;

    [Header("Spawn Settings")]
    [Tooltip("Сколько юнитов по слоту (обычно 5)")]
    [SerializeField] private int unitsPerPlayer = 5;
    [SerializeField] private float spawnRadius = 1.5f;

    [Header("Spawn Zones")]
    [SerializeField] private Transform[] spawnZonesPlayerA;
    [SerializeField] private Transform[] spawnZonesPlayerB;

    // Старый автоспавн по подключению больше не используем — драфт зовёт вручную.

    public void SpawnLoadoutFor(ulong ownerClientId, int[] selectionIndices, bool isPlayerA)
    {
        if (!NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[GameSessionManager] SpawnLoadoutFor должен вызываться на сервере.");
            return;
        }

        if (selectionIndices == null || selectionIndices.Length == 0)
        {
            Debug.LogError("[GameSessionManager] Пустой лоад-аут.");
            return;
        }

        var zones = isPlayerA ? spawnZonesPlayerA : spawnZonesPlayerB;
        if (zones == null || zones.Length == 0)
        {
            Debug.LogError("[GameSessionManager] Нет зон спавна для " + (isPlayerA ? "Player A" : "Player B"));
            return;
        }

        for (int i = 0; i < selectionIndices.Length && i < unitsPerPlayer; i++)
        {
            int prefabIndex = Mathf.Clamp(selectionIndices[i], 0, unitPrefabs.Count - 1);
            var prefab = unitPrefabs[prefabIndex];
            if (prefab == null)
            {
                Debug.LogError($"[GameSessionManager] Unit prefab по индексу {prefabIndex} не задан.");
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
                Debug.LogError("[GameSessionManager] Префаб не содержит NetworkObject!");
                Destroy(go);
            }
        }
    }
}
