// Assets/Scripts/Core/UnitDraftManager.cs
//
// ДОБАВЛЕНО: вызов генератора препятствий ПОСЛЕ спавна юнитов и ПЕРЕД началом первого хода.
// (ищем ObstacleFieldSpawner на сцене и просим его отработать на сервере).
//
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class UnitDraftManager : NetworkBehaviour
{
    public const int Slots = 5;

    [SerializeField] private GameSessionManager sessionManager;
    [SerializeField] private TurnManager turnManager;

    private readonly Dictionary<ulong, LoadoutPayload> _submitted = new();
    private readonly List<ulong> _connectionOrder = new();

    private bool _draftStarted;
    private bool _unitsSpawned;

    private void Awake()
    {
        if (sessionManager == null) sessionManager = FindObjectOfType<GameSessionManager>();
        if (turnManager == null) turnManager = FindObjectOfType<TurnManager>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            NetworkManager.OnClientConnectedCallback += OnClientConnected;
            CheckAndBeginDraft();
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
            NetworkManager.OnClientConnectedCallback -= OnClientConnected;
    }

    private void OnClientConnected(ulong clientId)
    {
        if (!IsServer) return;

        if (!_connectionOrder.Contains(clientId))
            _connectionOrder.Add(clientId);

        CheckAndBeginDraft();
    }

    private void CheckAndBeginDraft()
    {
        if (_draftStarted) return;

        var clients = NetworkManager.ConnectedClientsIds;
        if (clients.Count < 2) return;

        _draftStarted = true;
        BeginDraftClientRpc();
    }

    [ClientRpc]
    private void BeginDraftClientRpc()
    {
        var ui = FindObjectOfType<UnitDraftUI>(true);
        if (ui != null)
        {
            ui.gameObject.SetActive(true);
            ui.Bind(this);
            ui.SetupSlots(Slots);
            if (sessionManager != null)
                ui.SetCatalog(sessionManager.UnitPrefabs);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void SubmitLoadoutServerRpc(LoadoutPayload payload, ServerRpcParams rpcParams = default)
    {
        if (!IsServer) return;

        var clientId = rpcParams.Receive.SenderClientId;
        if (!ValidatePayload(payload)) return;

        _submitted[clientId] = payload;

        if (_submitted.Count >= 2 && !_unitsSpawned)
        {
            _unitsSpawned = true;
            SpawnChosenUnits();
            // <<< НОВОЕ: сгенерировать препятствия до старта игры
            var obstacleSpawner = FindObjectOfType<ObstacleFieldSpawner>();
            if (obstacleSpawner != null) obstacleSpawner.SpawnObstaclesServer();

            EndDraftClientRpc();
            if (turnManager != null)
                turnManager.BeginAfterDraftServer();
        }
    }

    [ClientRpc]
    private void EndDraftClientRpc()
    {
        var ui = FindObjectOfType<UnitDraftUI>(true);
        if (ui != null) ui.gameObject.SetActive(false);
    }

    private bool ValidatePayload(LoadoutPayload p)
    {
        if (sessionManager == null || sessionManager.UnitPrefabs == null || sessionManager.UnitPrefabs.Count == 0)
            return false;

        int max = sessionManager.UnitPrefabs.Count;
        return InRange(p.s0, max) && InRange(p.s1, max) && InRange(p.s2, max) && InRange(p.s3, max) && InRange(p.s4, max);
        static bool InRange(int v, int limit) => v >= 0 && v < limit;
    }

    private void SpawnChosenUnits()
    {
        if (!IsServer || sessionManager == null) return;

        if (_connectionOrder.Count < 2)
        {
            _connectionOrder.Clear();
            _connectionOrder.AddRange(NetworkManager.ConnectedClientsIds);
        }

        var clients = NetworkManager.ConnectedClientsIds;
        foreach (var clientId in clients)
        {
            bool isPlayerA = _connectionOrder.Count > 0 && _connectionOrder[0] == clientId;
            if (_submitted.TryGetValue(clientId, out var loadout))
            {
                sessionManager.SpawnLoadoutFor(clientId, loadout.ToArray(), isPlayerA);
            }
            else
            {
                var def = new int[Slots];
                for (int i = 0; i < Slots; i++) def[i] = 0;
                sessionManager.SpawnLoadoutFor(clientId, def, isPlayerA);
            }
        }
    }

    // ===== Payload =====
    public struct LoadoutPayload : INetworkSerializable
    {
        public int s0, s1, s2, s3, s4;
        public LoadoutPayload(int[] arr)
        {
            s0 = arr != null && arr.Length > 0 ? arr[0] : 0;
            s1 = arr != null && arr.Length > 1 ? arr[1] : 0;
            s2 = arr != null && arr.Length > 2 ? arr[2] : 0;
            s3 = arr != null && arr.Length > 3 ? arr[3] : 0;
            s4 = arr != null && arr.Length > 4 ? arr[4] : 0;
        }
        public int[] ToArray() => new[] { s0, s1, s2, s3, s4 };
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref s0);
            serializer.SerializeValue(ref s1);
            serializer.SerializeValue(ref s2);
            serializer.SerializeValue(ref s3);
            serializer.SerializeValue(ref s4);
        }
    }
}
