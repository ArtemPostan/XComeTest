// Assets/Scripts/Core/GameSessionManager.cs

using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// После того как NetworkUtility подтянет хоста или клиента,
/// этот менеджер на сервере (Host) ловит подключения и спавнит для каждого игрока заданное число юнитов
/// в его зоне (группа спавна).
/// </summary>
public class GameSessionManager : MonoBehaviour
{
    [Header("Unit Settings")]
    [Tooltip("Префаб юнита с NetworkObject и UnitNetworkBehaviour")]
    [SerializeField] private GameObject unitPrefab;
    [Tooltip("Сколько юнитов спавнить на игрока")]
    [SerializeField] private int unitsPerPlayer = 5;
    [Tooltip("Радиус вокруг точки зоны, в котором разбросаются юниты")]
    [SerializeField] private float spawnRadius = 1.5f;

    [Header("Spawn Zones")]
    [Tooltip("Точки, задающие зону спавна для игрока A")]
    [SerializeField] private Transform[] spawnZonesPlayerA;
    [Tooltip("Точки, задающие зону спавна для игрока B")]
    [SerializeField] private Transform[] spawnZonesPlayerB;

    // Чтобы не спавнить дважды для одного игрока
    private readonly HashSet<ulong> _spawnedFor = new HashSet<ulong>();
    // Список порядка подключений (0-й — хост, 1-й — первый удалённый клиент и т.д.)
    private readonly List<ulong> _connectionOrder = new List<ulong>();

    private void Start()
    {
        // Подписываемся на подключение клиентов
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
    }

    private void OnClientConnected(ulong clientId)
    {
        // Только сервер спавнит
        if (!NetworkManager.Singleton.IsServer)
            return;

        // Не повторяемся
        if (_spawnedFor.Contains(clientId))
            return;

        Debug.Log($"[GameSessionManager] Игрок подключился: {clientId}");
        _spawnedFor.Add(clientId);
        _connectionOrder.Add(clientId);

        // Определяем, какую зону даём этому игроку:
        // если это первый — зона A, второй и далее — зона B
        bool isPlayerA = (_connectionOrder.IndexOf(clientId) == 0);
        var zones = isPlayerA ? spawnZonesPlayerA : spawnZonesPlayerB;

        // Спавним unitsPerPlayer юнитов в случайных точках внутри каждой зоны
        SpawnUnitsFor(clientId, zones);

        // (Необязательно) можно тут сделать ClientRpc, чтобы дать знать клиентам, что их армия готова
    }

    private void SpawnUnitsFor(ulong ownerClientId, Transform[] zones)
    {
        if (zones == null || zones.Length == 0)
        {
            Debug.LogError("[GameSessionManager] Нет зон спавна для " +
                (ownerClientId == _connectionOrder[0] ? "Player A" : "Player B"));
            return;
        }

        for (int i = 0; i < unitsPerPlayer; i++)
        {
            // Выбираем одну из зон циклически (если зон меньше, чем юнитов)
            var zone = zones[i % zones.Length];
            // Случайное смещение внутри круга радиуса spawnRadius
            Vector2 offset2D = Random.insideUnitCircle * spawnRadius;
            Vector3 spawnPos = zone.position + new Vector3(offset2D.x, 0, offset2D.y);

            // Инстантим на сервере
            var go = Instantiate(unitPrefab, spawnPos, Quaternion.identity);
            if (go.TryGetComponent<NetworkObject>(out var no))
            {
                // Передаём владение этому клиенту
                no.SpawnAsPlayerObject(ownerClientId, true);
                Debug.Log($"[GameSessionManager] Спавн #{i} для {ownerClientId} в {spawnPos}");
            }
            else
            {
                Debug.LogError("[GameSessionManager] unitPrefab не содержит NetworkObject!");
                Destroy(go);
            }
        }
    }
}
