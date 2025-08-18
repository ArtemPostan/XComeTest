// Assets/Scripts/World/ObstacleFieldSpawner.cs
//
// Серверный спавнер групп препятствий (server-driven).
// Ищет точки на поле (Collider) так, чтобы не пересекаться с юнитами и другими группами,
// инстанцирует префаб группы (с NetworkObject + ObstacleGroupGeneratorServerDriven),
// задаёт параметры группы (ElementsCount/Radius) и делает Spawn().
// Внутри группы сервер сам заполнит точную раскладку через NetworkList, клиенты НЕ рандомят.
//
// Вызовите SpawnObstaclesServer() ПОСЛЕ спавна юнитов и ДО начала первого хода.

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
            Debug.LogWarning("[ObstacleFieldSpawner] SpawnObstaclesServer должен вызываться на сервере/хосте.");
            return;
        }

        if (fieldCollider == null)
        {
            Debug.LogError("[ObstacleFieldSpawner] Не задан fieldCollider.");
            return;
        }
        if (obstacleGroupPrefab == null)
        {
            Debug.LogError("[ObstacleFieldSpawner] Не задан obstacleGroupPrefab.");
            return;
        }

        _placed.Clear();

        // Собираем стартовые позиции юнитов на момент генерации
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

            // Создаём группу и задаём параметры до Spawn()
            var go = Instantiate(obstacleGroupPrefab, posOnField, Quaternion.identity);

            if (!go.TryGetComponent<NetworkObject>(out var no) ||
                !go.TryGetComponent<ObstacleGroupGeneratorServerDriven>(out var gen))
            {
                Debug.LogError("[ObstacleFieldSpawner] Префаб группы должен содержать NetworkObject и ObstacleGroupGeneratorServerDriven.");
                Destroy(go);
                continue;
            }

            gen.ElementsCount.Value = elements;
            gen.Radius.Value = radius;

            // Спавним сетевой контейнер группы (дети — локальные, придут из NetworkList)
            no.Spawn();

            _placed.Add((posOnField, radius));
        }

        if (_placed.Count < targetGroups)
        {
            Debug.LogWarning($"[ObstacleFieldSpawner] Расставлено {_placed.Count}/{targetGroups} групп (не хватило валидных позиций?).");
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

        // Луч сверху вниз: учитываем реальный рельеф/плоскость
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
