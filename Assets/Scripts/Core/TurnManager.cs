// Assets/Scripts/Core/TurnManager.cs

using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Управляет очередностью ходов и сбрасывает запас движения юнитов в начале хода.
/// </summary>
public class TurnManager : NetworkBehaviour
{
    [Header("Turn Settings")]
    [SerializeField, Tooltip("Длительность хода в секундах")]
    private float turnDuration = 60f;
    public float TurnDuration => turnDuration;

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
            // Ждём второго игрока
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            if (NetworkManager.Singleton.ConnectedClientsIds.Count >= 2)
                StartGame();
        }

        // Подписываем UI на изменения
        CurrentPlayerId.OnValueChanged += (_, pid) => OnTurnStarted?.Invoke(pid, TurnNumber.Value);
        TurnNumber.OnValueChanged += (_, tn) => OnTurnStarted?.Invoke(CurrentPlayerId.Value, tn);
    }

    private void OnClientConnected(ulong clientId)
    {
        if (!IsServer || _gameStarted) return;
        if (NetworkManager.Singleton.ConnectedClientsIds.Count >= 2)
            StartGame();
    }

    private void StartGame()
    {
        _gameStarted = true;
        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;

        var clients = NetworkManager.Singleton.ConnectedClientsIds;
        CurrentPlayerId.Value = clients[0];
        TurnNumber.Value = 1;

        StartTurn();
    }

    private void StartTurn()
    {
        // Сброс запаса движения и атаки у всех юнитов текущего игрока
        ResetUnitsForPlayer(CurrentPlayerId.Value);

        NetworkObjectReference unitReference = new NetworkObjectReference(); // Создаем пустую ссылку по умолчанию

        // Получаем первый юнит текущего игрока
        _firstUnitOfCurrentPlayer = GetFirstUnitForPlayer(CurrentPlayerId.Value);

        // Если юнит найден, присваиваем его ссылку
        if (_firstUnitOfCurrentPlayer != null)
        {
            unitReference = _firstUnitOfCurrentPlayer.NetworkObject;
        }

        // Уведомляем клиентов, передавая созданную ссылку
        StartTurnClientRpc(CurrentPlayerId.Value, TurnNumber.Value, unitReference);

        // Запускаем таймер хода
        if (_timerCoroutine != null) StopCoroutine(_timerCoroutine);
        _timerCoroutine = StartCoroutine(TurnTimer());
    }

    /// <summary>
    /// Сбрасывает MovementRemaining и CanAttack у всех юнитов заданного игрока.
    /// </summary>
    private void ResetUnitsForPlayer(ulong playerId)
    {
        // NEW: Variable to hold the first unit's transform to send to the camera
        Transform firstUnitTransform = null;

        foreach (var kvp in NetworkManager.Singleton.SpawnManager.SpawnedObjects)
        {
            var netObj = kvp.Value;
            if (netObj.OwnerClientId != playerId) continue;
            var unit = netObj.GetComponent<UnitNetworkBehaviour>();
            if (unit != null)
            {
                unit.MovementRemaining.Value = unit.moveSpeed;
                unit.CanAttack.Value = true;
                Debug.Log($"[TurnManager] Reset unit {netObj.NetworkObjectId} for player {playerId}");

                // NEW: Capture the transform of the first unit
                if (firstUnitTransform == null)
                {
                    firstUnitTransform = netObj.transform;
                }
            }
        }

        // NEW: If a unit was found, notify the camera to focus on it
        if (firstUnitTransform != null)
        {
            OnPlayerUnitTurnStarted?.Invoke(firstUnitTransform);
        }
    }

    [ClientRpc] 
    private void StartTurnClientRpc(ulong playerId, int turn, NetworkObjectReference unitReference)
    {
        OnTurnStarted?.Invoke(playerId, turn);

        // Если ссылка на юнит действительна, передаем его Transform в событие
        if (unitReference.TryGet(out var networkObject))
        {
            OnPlayerUnitTurnStarted?.Invoke(networkObject.transform);
        }
        else
        {
            // Иначе, просто выводим предупреждение
            Debug.LogWarning("[TurnManager] Юнит для центрирования камеры не найден.");
        }
    }

    /// <summary>
    /// Клиент или сервер просит завершить ход.
    /// </summary>
    public void RequestEndTurn()
    {
        Debug.Log("LocalClientId " + NetworkManager.Singleton.LocalClientId + "and CurrentPlayerId " + CurrentPlayerId.Value);
        if (IsServer || NetworkManager.Singleton.LocalClientId == CurrentPlayerId.Value)
            EndTurnServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void EndTurnServerRpc(ServerRpcParams rpcParams = default)
    {
        // Оповестить о конце хода
        EndTurnClientRpc(CurrentPlayerId.Value, TurnNumber.Value);

        // Смена игрока и номера хода
        var clients = NetworkManager.Singleton.ConnectedClientsIds;
        ulong next = (clients.Count >= 2 && clients[0] == CurrentPlayerId.Value)
            ? clients[1]
            : clients[0];
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
            {
                return netObj.GetComponent<UnitNetworkBehaviour>();
            }
        }
        return null;
    }
}
