// Assets/Scripts/Core/TurnManager.cs
//
// Доработка для старта ПОСЛЕ драфта:
// - добавлен флаг autoStartOnConnect (по умолчанию false);
// - публичный серверный метод BeginAfterDraftServer() для запуска ходов после спавна;
// - остальная логика сохранена.
//
// Поставьте autoStartOnConnect = false в инспекторе,
// а UnitDraftManager вызовет BeginAfterDraftServer() когда оба игрока «готовы».

using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

public class TurnManager : NetworkBehaviour
{
    [Header("Turn Settings")]
    [SerializeField] private float turnDuration = 60f;
    public float TurnDuration => turnDuration;

    [Header("Flow")]
    [SerializeField] private bool autoStartOnConnect = false; // ключ: ждём драфт

    [Header("Events (UI can subscribe)")]
    public UnityEvent<ulong, int> OnTurnStarted; // (playerId, turnNumber)
    public UnityEvent<ulong, int> OnTurnEnded;   // (playerId, turnNumber)
    public UnityEvent<Transform> OnPlayerUnitTurnStarted;

    public NetworkVariable<ulong> CurrentPlayerId = new NetworkVariable<ulong>();
    public NetworkVariable<int> TurnNumber = new NetworkVariable<int>(1);

    private bool _gameStarted;
    private Coroutine _timerCoroutine;
    private UnitNetworkBehaviour _firstUnitOfCurrentPlayer;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            if (autoStartOnConnect)
            {
                NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
                if (NetworkManager.Singleton.ConnectedClientsIds.Count >= 2)
                    StartGame();
            }
        }

        CurrentPlayerId.OnValueChanged += (_, pid) => OnTurnStarted?.Invoke(pid, TurnNumber.Value);
        TurnNumber.OnValueChanged += (_, tn) => OnTurnStarted?.Invoke(CurrentPlayerId.Value, tn);
    }

    private void OnDestroy()
    {
        if (IsServer && autoStartOnConnect && NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
    }

    private void OnClientConnected(ulong clientId)
    {
        if (!IsServer || _gameStarted) return;
        if (NetworkManager.Singleton.ConnectedClientsIds.Count >= 2)
            StartGame();
    }

    // Вызвать с сервера ПОСЛЕ драфта/спавна
    public void BeginAfterDraftServer()
    {
        if (!IsServer) return;
        if (_gameStarted) return;

        StartGame();
    }

    private void StartGame()
    {
        _gameStarted = true;
        if (autoStartOnConnect && NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;

        var clients = NetworkManager.Singleton.ConnectedClientsIds;
        if (clients.Count == 0) return;

        CurrentPlayerId.Value = clients[0]; // первый ход — у первого подключившегося (Player A)
        TurnNumber.Value = 1;

        StartTurn();
    }

    private void StartTurn()
    {
        ResetUnitsForPlayer(CurrentPlayerId.Value);

        NetworkObjectReference unitReference = default;

        _firstUnitOfCurrentPlayer = GetFirstUnitForPlayer(CurrentPlayerId.Value);
        if (_firstUnitOfCurrentPlayer != null)
            unitReference = _firstUnitOfCurrentPlayer.NetworkObject;

        StartTurnClientRpc(CurrentPlayerId.Value, TurnNumber.Value, unitReference);

        if (_timerCoroutine != null) StopCoroutine(_timerCoroutine);
        _timerCoroutine = StartCoroutine(TurnTimer());
    }

    private void ResetUnitsForPlayer(ulong playerId)
    {
        Transform firstUnitTransform = null;

        foreach (var kvp in NetworkManager.Singleton.SpawnManager.SpawnedObjects)
        {
            var netObj = kvp.Value;
            if (netObj.OwnerClientId != playerId) continue;
            var unit = netObj.GetComponent<UnitNetworkBehaviour>();
            if (unit != null)
            {
                unit.MovementRemaining.Value = unit.moveSpeed;
                unit._canAttack.Value = true;

                if (firstUnitTransform == null)
                    firstUnitTransform = netObj.transform;
            }
        }

        if (firstUnitTransform != null)
            OnPlayerUnitTurnStarted?.Invoke(firstUnitTransform);
    }

    [ClientRpc]
    private void StartTurnClientRpc(ulong playerId, int turn, NetworkObjectReference unitReference)
    {
        OnTurnStarted?.Invoke(playerId, turn);

        if (unitReference.TryGet(out var networkObject))
            OnPlayerUnitTurnStarted?.Invoke(networkObject.transform);
        else
            Debug.LogWarning("[TurnManager] Юнит для центрирования камеры не найден.");
    }

    public void RequestEndTurn()
    {
        if (IsServer || NetworkManager.Singleton.LocalClientId == CurrentPlayerId.Value)
            EndTurnServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void EndTurnServerRpc(ServerRpcParams rpcParams = default)
    {
        EndTurnClientRpc(CurrentPlayerId.Value, TurnNumber.Value);

        var clients = NetworkManager.Singleton.ConnectedClientsIds;
        ulong next = (clients.Count >= 2 && clients[0] == CurrentPlayerId.Value) ? clients[1] : clients[0];
        CurrentPlayerId.Value = next;
        TurnNumber.Value++;

        StartTurn();
    }

    [ClientRpc]
    private void EndTurnClientRpc(ulong playerId, int turn)
    {
        OnTurnEnded?.Invoke(playerId, turn);
    }

    private IEnumerator TurnTimer()
    {
        float elapsed = 0f;
        while (elapsed < turnDuration)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
        EndTurnServerRpc();
    }

    public UnitNetworkBehaviour GetFirstUnitForPlayer(ulong playerId)
    {
        foreach (var kvp in NetworkManager.Singleton.SpawnManager.SpawnedObjects)
        {
            var netObj = kvp.Value;
            if (netObj.OwnerClientId == playerId)
                return netObj.GetComponent<UnitNetworkBehaviour>();
        }
        return null;
    }
}
