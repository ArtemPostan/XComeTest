using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Серверный спавнер групп препятствий (server-driven) с поддержкой нескольких типов групп.
/// Вызывать ПОСЛЕ спавна юнитов и ДО первого хода.
/// ПАТЧИ: подробные логи, проверка NetworkPrefabs, сводка причин отказов.
/// </summary>
public class ObstacleFieldSpawnerMulti : MonoBehaviour
{
    [Header("Area")]
    [SerializeField, Tooltip("Коллайдер плоскости/террейна, ограничивающий область спавна препятствий")]
    private Collider fieldCollider;

    [Header("Modes")]
    [SerializeField, Tooltip("true = размещаем по квотам каждого типа; false = смешанный пул по весам")]
    private bool usePerArchetypeQuotas = true;

    [Header("Global Group Count (used when quotas=false)")]
    [SerializeField, Tooltip("Мин. число групп (глобально)")]
    private int globalMinGroups = 6;
    [SerializeField, Tooltip("Макс. число групп (глобально)")]
    private int globalMaxGroups = 12;

    [Header("Global Placement Constraints (fallbacks)")]
    [SerializeField, Tooltip("Мин. дистанция от центра группы до ближайшего юнита (если не переопределено в типе)")]
    private float minDistanceToUnits = 2.5f;
    [SerializeField, Tooltip("Подушка между группами (к сумме радиусов), если не переопределено в типе")]
    private float groupsPadding = 1.0f;
    [SerializeField, Tooltip("Сколько попыток найти валидные точки (на весь процесс)")]
    private int maxPlacementAttempts = 600;

    [Header("Raycast")]
    [SerializeField, Tooltip("Лэйеры поверхности (Plane/Terrain)")]
    private LayerMask groundMask = ~0;

    [Header("Archetypes")]
    [SerializeField, Tooltip("Набор типов групп с собственными параметрами")]
    private List<GroupArchetype> archetypes = new();

    // Для проверки пересечений между группами (круги по радиусу)
    private readonly List<(Vector3 pos, float radius, float padding)> _placed = new();

    [Serializable]
    public class GroupArchetype
    {
        [Header("Prefab (Group Container)")]
        [Tooltip("Префаб группы: ДОЛЖЕН содержать NetworkObject + ObstacleGroupGeneratorServerDriven")]
        public GameObject groupPrefab;

        [Header("Per-Type Count (used when quotas=true)")]
        [Tooltip("Мин. число групп этого типа")]
        public int minGroups = 0;
        [Tooltip("Макс. число групп этого типа")]
        public int maxGroups = 0;

        [Header("Group Params for this Type")]
        [Tooltip("Диапазон радиуса группы (fallback, если в префабе группы нет BoxCollider-рамки)")]
        public Vector2 groupRadiusRange = new Vector2(2f, 4f);
        [Tooltip("Диапазон числа элементов в группе")]
        public Vector2Int elementsCountRange = new Vector2Int(3, 7);

        [Header("Placement Overrides (optional)")]
        [Tooltip("Переопределение minDistanceToUnits (если <0 — используется глобальное значение)")]
        public float minDistanceToUnitsOverride = -1f;
        [Tooltip("Переопределение groupsPadding (если <0 — используется глобальное значение)")]
        public float groupsPaddingOverride = -1f;

        [Header("Mixing (used when quotas=false)")]
        [Tooltip("Вес типа при смешанном размещении (чем больше, тем чаще)")]
        public float weight = 1f;
    }

    /// <summary>Вызывайте на сервере/хосте после спавна юнитов.</summary>
    public void SpawnObstaclesServer()
    {
        if (!NetworkManager.Singleton || !NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[Spawner] SpawnObstaclesServer должен вызываться на сервере/хосте.");
            return;
        }

        if (!fieldCollider)
        {
            Debug.LogError("[Spawner] Не задан fieldCollider.");
            return;
        }
        if (archetypes == null || archetypes.Count == 0)
        {
            Debug.LogError("[Spawner] Не задан список Archetypes.");
            return;
        }

        // Проверим корректность каждого архетипа и регистрацию префаба в сетевой конфигурации
        foreach (var a in archetypes)
        {
            if (!a.groupPrefab)
            {
                Debug.LogError("[Spawner] Archetype без groupPrefab.");
                return;
            }
            if (!a.groupPrefab.GetComponent<NetworkObject>() ||
                !a.groupPrefab.GetComponent<ObstacleGroupGeneratorServerDriven>())
            {
                Debug.LogError($"[Spawner] Group Prefab '{a.groupPrefab.name}' должен содержать NetworkObject + ObstacleGroupGeneratorServerDriven.");
                return;
            }
            if (!PrefabRegistered(a.groupPrefab))
            {
                Debug.LogError($"[Spawner] Group Prefab '{a.groupPrefab.name}' НЕ зарегистрирован в NetworkManager → NetworkPrefabs. Добавьте его туда.");
                return;
            }
        }

        _placed.Clear();

        // Собираем стартовые позиции юнитов
        var unitPositions = new List<Vector3>();
        foreach (var u in FindObjectsOfType<UnitNetworkBehaviour>())
            unitPositions.Add(u.transform.position);

        int attemptsLeft = Mathf.Max(1, maxPlacementAttempts);

        Debug.Log($"[Spawner] START. archetypes={archetypes.Count}, useQuotas={usePerArchetypeQuotas}, " +
                  $"globalGroups=[{globalMinGroups}-{globalMaxGroups}], attempts={attemptsLeft}, units={unitPositions.Count}, " +
                  $"groundMask={groundMask.value}, field='{fieldCollider.name}'");

        if (usePerArchetypeQuotas)
        {
            for (int i = 0; i < archetypes.Count; i++)
            {
                var a = archetypes[i];
                int want = UnityEngine.Random.Range(
                    Mathf.Min(a.minGroups, a.maxGroups),
                    Mathf.Max(a.minGroups, a.maxGroups) + 1);

                Debug.Log($"[Spawner] Archetype '{a.groupPrefab.name}' want={want}");
                TryPlaceForArchetype(a, want, unitPositions, ref attemptsLeft);
                if (attemptsLeft <= 0)
                {
                    Debug.LogWarning("[Spawner] attemptsLeft исчерпаны.");
                    break;
                }
            }
        }
        else
        {
            int targetTotal = UnityEngine.Random.Range(
                Mathf.Min(globalMinGroups, globalMaxGroups),
                Mathf.Max(globalMinGroups, globalMaxGroups) + 1);

            float[] cdf = BuildCdf(archetypes);
            int safety = 100000;

            Debug.Log($"[Spawner] Mixed mode. targetTotal={targetTotal}");

            while (targetTotal > 0 && attemptsLeft > 0 && safety-- > 0)
            {
                int idx = SampleIndexByCdf(cdf);
                var a = archetypes[idx];
                if (TryPlaceOne(a, unitPositions))
                {
                    targetTotal--;
                }
                attemptsLeft--;
            }

            if (targetTotal > 0)
                Debug.LogWarning($"[Spawner] Недорезервировано групп: осталось {targetTotal} (не хватило валидных позиций?).");
        }

        Debug.Log($"[Spawner] DONE. Placed groups total: {_placed.Count}");
    }

    // ---------- Размещение по квоте типа ----------
    private void TryPlaceForArchetype(GroupArchetype a, int want, List<Vector3> unitPositions, ref int attemptsLeft)
    {
        int placed = 0;
        int safety = 100000;

        // сводка отказов по типу
        int missRay = 0, nearUnit = 0, nearGroup = 0;

        while (placed < want && attemptsLeft > 0 && safety-- > 0)
        {
            if (!TryPickPointOnField(out var posOnField)) { missRay++; attemptsLeft--; continue; }

            float radius = UnityEngine.Random.Range(a.groupRadiusRange.x, a.groupRadiusRange.y);
            int elements = UnityEngine.Random.Range(a.elementsCountRange.x, a.elementsCountRange.y + 1);

            float mdUnits = a.minDistanceToUnitsOverride >= 0f ? a.minDistanceToUnitsOverride : minDistanceToUnits;
            float pad = a.groupsPaddingOverride >= 0f ? a.groupsPaddingOverride : groupsPadding;

            if (!IsFarFromUnits(posOnField, radius, mdUnits, unitPositions)) { nearUnit++; attemptsLeft--; continue; }
            if (!IsFarFromOtherGroups(posOnField, radius, pad)) { nearGroup++; attemptsLeft--; continue; }

            if (PlaceOneInstance(a, posOnField, radius, elements, pad))
            {
                placed++;
            }

            attemptsLeft--;
        }

        Debug.Log($"[Spawner] Archetype '{a.groupPrefab.name}' placed {placed}/{want} (attemptsLeft={attemptsLeft}). " +
                  $"Summary: rayMiss={missRay}, tooCloseToUnit={nearUnit}, tooCloseToGroup={nearGroup}");

        if (placed < want)
            Debug.LogWarning($"[Spawner] '{a.groupPrefab.name}': размещено {placed}/{want} (не хватило валидных позиций?).");
    }

    // ---------- Размещение одной группы ----------
    private bool TryPlaceOne(GroupArchetype a, List<Vector3> unitPositions)
    {
        if (!TryPickPointOnField(out var posOnField)) return false;

        float radius = UnityEngine.Random.Range(a.groupRadiusRange.x, a.groupRadiusRange.y);
        int elements = UnityEngine.Random.Range(a.elementsCountRange.x, a.elementsCountRange.y + 1);

        float mdUnits = a.minDistanceToUnitsOverride >= 0f ? a.minDistanceToUnitsOverride : minDistanceToUnits;
        float pad = a.groupsPaddingOverride >= 0f ? a.groupsPaddingOverride : groupsPadding;

        if (!IsFarFromUnits(posOnField, radius, mdUnits, unitPositions)) return false;
        if (!IsFarFromOtherGroups(posOnField, radius, pad)) return false;

        return PlaceOneInstance(a, posOnField, radius, elements, pad);
    }

    private bool PlaceOneInstance(GroupArchetype a, Vector3 posOnField, float radius, int elements, float pad)
    {
        var go = Instantiate(a.groupPrefab, posOnField, Quaternion.identity);

        // складываем в отдельный корневой контейнер для наглядности
        var root = GameObject.Find("Obstacles (Server)") ?? new GameObject("Obstacles (Server)");
        go.transform.SetParent(root.transform, worldPositionStays: true);

        if (!go.TryGetComponent<NetworkObject>(out var no) ||
            !go.TryGetComponent<ObstacleGroupGeneratorServerDriven>(out var gen))
        {
            Debug.LogError("[Spawner] Префаб группы должен содержать NetworkObject и ObstacleGroupGeneratorServerDriven.");
            Destroy(go);
            return false;
        }

        gen.ElementsCount.Value = elements;
        gen.Radius.Value = radius;

        Debug.Log($"[Spawner] Instantiate '{go.name}' at {posOnField} (r={radius:F2}, elements={elements})");
        no.Spawn();

        // проверим статус спавна на следующий кадр
        StartCoroutine(CheckSpawn(no, go));

        _placed.Add((posOnField, radius, pad));
        return true;
    }

    private IEnumerator CheckSpawn(NetworkObject no, GameObject inst)
    {
        yield return null; // подождать кадр
        Debug.Log($"[Spawner] Spawn result for '{inst.name}': IsSpawned={(no && no.IsSpawned)}, active={(inst && inst.activeInHierarchy)}");
    }

    // ---------- Проверки дистанций ----------
    private bool IsFarFromUnits(Vector3 p, float radius, float minDistToUnits, List<Vector3> units)
    {
        float need = radius + minDistToUnits;
        float needSqr = need * need;
        for (int i = 0; i < units.Count; i++)
        {
            if ((units[i] - p).sqrMagnitude < needSqr)
            {
                Debug.Log($"[Spawner] Rejected: too close to unit at {units[i]} (need>={need:F2})");
                return false;
            }
        }
        return true;
    }

    private bool IsFarFromOtherGroups(Vector3 p, float radius, float pad)
    {
        for (int i = 0; i < _placed.Count; i++)
        {
            float need = radius + _placed[i].radius + Mathf.Max(0f, (pad + _placed[i].padding));
            if ((p - _placed[i].pos).sqrMagnitude < need * need)
            {
                Debug.Log($"[Spawner] Rejected: too close to group at {_placed[i].pos} (need>={need:F2})");
                return false;
            }
        }
        return true;
    }

    // ---------- Выбор точки на поле ----------
    private bool TryPickPointOnField(out Vector3 worldPoint)
    {
        worldPoint = Vector3.zero;

        var b = fieldCollider.bounds;
        float x = UnityEngine.Random.Range(b.min.x, b.max.x);
        float z = UnityEngine.Random.Range(b.min.z, b.max.z);

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
                Debug.Log($"[Spawner] Raycast hit '{hit.collider.name}', ожидался '{fieldCollider.name}'. Проверьте fieldCollider/groundMask.");
            }
        }
        else
        {
            Debug.Log("[Spawner] Raycast miss — не нашли поверхность поля. Проверьте groundMask/высоту/коллайдер поля.");
        }
        return false;
    }

    // ---------- Вспомогательные: веса и регистрация ----------
    private static float[] BuildCdf(List<GroupArchetype> list)
    {
        float sum = 0f;
        for (int i = 0; i < list.Count; i++) sum += Mathf.Max(0f, list[i].weight);
        if (sum <= 0f) sum = 1f;

        float acc = 0f;
        var cdf = new float[list.Count];
        for (int i = 0; i < list.Count; i++)
        {
            acc += Mathf.Max(0f, list[i].weight) / sum;
            cdf[i] = acc;
        }
        if (cdf.Length > 0) cdf[cdf.Length - 1] = 1f;
        return cdf;
    }

    private static int SampleIndexByCdf(float[] cdf)
    {
        float u = UnityEngine.Random.value;
        int lo = 0, hi = cdf.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (u <= cdf[mid]) hi = mid; else lo = mid + 1;
        }
        return lo;
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
