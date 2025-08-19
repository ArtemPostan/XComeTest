// Assets/Scripts/World/ObstacleFieldSpawner.cs
//
// Серверный спавнер групп препятствий (server-driven).
// Добавлены подробные логи: [Spawner] ...  + сводка причин, почему не получилось поставить группу.
//
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class ObstacleFieldSpawner : MonoBehaviour
{
    [Header("Area")]
    [SerializeField, Tooltip("Коллайдер плоскости/террейна, ограничивающий область спавна препятствий")]
    private Collider fieldCollider;

    [Header("Group Prefab (Server-Driven)")]
    [SerializeField, Tooltip("Префаб с NetworkObject + ObstacleGroupGeneratorServerDriven")]
    private GameObject obstacleGroupPrefab;

    [Header("Group Count")]
    [SerializeField, Tooltip("Мин. число групп")]
    private int minGroups = 3;
    [SerializeField, Tooltip("Макс. число групп")]
    private int maxGroups = 6;

    [Header("Group Params")]
    [SerializeField, Tooltip("Диапазон радиусов группы (вокруг центра)")]
    private Vector2 groupRadiusRange = new Vector2(2f, 4f);
    [SerializeField, Tooltip("Диапазон числа элементов в группе")]
    private Vector2Int elementsCountRange = new Vector2Int(3, 7);

    [Header("Placement Constraints")]
    [SerializeField, Tooltip("Минимальная дистанция от центра группы до ближайшего юнита на старте")]
    private float minDistanceToUnits = 2.5f;
    [SerializeField, Tooltip("Дополнительная «подушка» между группами (к сумме их радиусов)")]
    private float groupsPadding = 1.0f;
    [SerializeField, Tooltip("Сколько попыток найти валидные точки")]
    private int maxPlacementAttempts = 200;

    [Header("Raycast")]
    [SerializeField, Tooltip("Лэйеры поверхности (Plane/Terrain)")]
    private LayerMask groundMask = ~0;

    private readonly List<(Vector3 pos, float radius)> _placed = new();

    /// <summary>
    /// Вызывайте на сервере/хосте после спавна юнитов.
    /// </summary>
    public void SpawnObstaclesServer()
    {
        if (!NetworkManager.Singleton || !NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[Spawner] SpawnObstaclesServer должен вызываться на сервере/хосте.");
            return;
        }

        if (fieldCollider == null)
        {
            Debug.LogError("[Spawner] Не задан fieldCollider.");
            return;
        }
        if (obstacleGroupPrefab == null)
        {
            Debug.LogError("[Spawner] Не задан obstacleGroupPrefab.");
            return;
        }

        // Быстрая проверка регистрации префаба в NetworkManager
        if (!PrefabRegistered(obstacleGroupPrefab))
            Debug.LogWarning("[Spawner] ВНИМАНИЕ: obstacleGroupPrefab может быть не зарегистрирован в NetworkManager/NetworkPrefabs. " +
                             "Если так — no.Spawn() не создаст сетевой объект.");

        _placed.Clear();

        var unitPositions = new List<Vector3>();
        foreach (var u in FindObjectsOfType<UnitNetworkBehaviour>())
            unitPositions.Add(u.transform.position);

        int targetGroups = Random.Range(minGroups, Mathf.Max(minGroups, maxGroups + 1));
        int attempts = 0;

        // Счётчики для сводки причин отказов
        int missRay = 0, nearUnit = 0, nearGroup = 0, badPrefab = 0, spawned = 0;

        Debug.Log($"[Spawner] START. targetGroups={targetGroups}, attemptsLimit={maxPlacementAttempts}, " +
                  $"units={unitPositions.Count}, groundMask={groundMask.value}");

        while (_placed.Count < targetGroups && attempts < maxPlacementAttempts)
        {
            attempts++;

            if (!TryPickPointOnField(out var posOnField))
            {
                missRay++;
                continue;
            }

            float radius = Random.Range(groupRadiusRange.x, groupRadiusRange.y);
            int elements = Random.Range(elementsCountRange.x, elementsCountRange.y + 1);

            if (!IsFarFromUnits(posOnField, radius, unitPositions)) { nearUnit++; continue; }
            if (!IsFarFromOtherGroups(posOnField, radius)) { nearGroup++; continue; }

            var go = Instantiate(obstacleGroupPrefab, posOnField, Quaternion.identity);

            if (!go.TryGetComponent<NetworkObject>(out var no) ||
                !go.TryGetComponent<ObstacleGroupGeneratorServerDriven>(out var gen))
            {
                Debug.LogError("[Spawner] Префаб группы должен содержать NetworkObject И ObstacleGroupGeneratorServerDriven.");
                Destroy(go);
                badPrefab++;
                continue;
            }

            gen.ElementsCount.Value = elements;
            gen.Radius.Value = radius;

            Debug.Log($"[Spawner] Instantiate '{go.name}' at {posOnField} (r={radius:F2}, elements={elements})");
            no.Spawn();
            if (no.IsSpawned) spawned++; else Debug.LogWarning($"[Spawner] '{go.name}' не IsSpawned после no.Spawn(). Проверьте NetworkPrefabs.");

            _placed.Add((posOnField, radius));
        }

        Debug.Log($"[Spawner] DONE. PlacedGroups={_placed.Count}/{targetGroups} (attempts={attempts}/{maxPlacementAttempts}). " +
                  $"Summary: rayMiss={missRay}, tooCloseToUnit={nearUnit}, tooCloseToGroup={nearGroup}, badPrefab={badPrefab}, spawnedOk={spawned}");

        if (_placed.Count < targetGroups)
        {
            Debug.LogWarning($"[Spawner] Расставлено {_placed.Count}/{targetGroups} групп (не хватило валидных позиций?).");
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

        Vector3 rayStart = new Vector3(x, b.max.y + 10f, z);
        if (Physics.Raycast(rayStart, Vector3.down, out var hit, b.size.y + 50f, groundMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider == fieldCollider)
            {
                worldPoint = hit.point;
                return true;
            }
            else
            {
                Debug.Log($"[Spawner] Raycast попал в '{hit.collider.name}', а должен в '{fieldCollider.name}'. Проверьте fieldCollider/groundMask.");
            }
        }
        else
        {
            Debug.Log("[Spawner] Raycast НЕ попал в поле. Проверьте высоту запуска/маску слоёв/коллайдер.");
        }
        return false;
    }

    private bool PrefabRegistered(GameObject prefab)
    {
        var nm = NetworkManager.Singleton;
        if (!nm) return false;
        foreach (var p in nm.NetworkConfig.Prefabs.Prefabs)
            if (p != null && p.Prefab == prefab) return true;
        return false;
    }
}
