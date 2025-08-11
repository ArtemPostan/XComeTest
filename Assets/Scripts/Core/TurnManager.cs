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
    public UnityEvent<ulong, int> OnTurnStarted = new UnityEvent<ulong, int>(); // (playerId, turnNumber)
    public UnityEvent<ulong, int> OnTurnEnded = new UnityEvent<ulong, int>(); // (playerId, turnNumber)
    public UnityEvent<Transform> OnPlayerUnitTurnStarted = new UnityEvent<Transform>();

    public NetworkVariable<ulong> CurrentPlayerId = new NetworkVariable<ulong>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<int> TurnNumber = new NetworkVariable<int>(
        1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private bool _gameStarted;
    private Coroutine _timerCoroutine;

    private UnitNetworkBehaviour _firstUnitOfCurrentPlayer;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        bool isLocalPlay = FindObjectOfType<NetworkUtility>()?.localPlayMode ?? false;

        if (IsServer)
        {
            if (isLocalPlay)
            {
                // Локальная игра: не ждём второго клиента
                StartGameSinglePlayer();
            }
            else
            {
                NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
                if (NetworkManager.Singleton.ConnectedClientsIds.Count >= 2)
                    StartGame();
            }
        }

        CurrentPlayerId.OnValueChanged += (_, pid) => OnTurnStarted?.Invoke(pid, TurnNumber.Value);
        TurnNumber.OnValueChanged += (_, tn) => OnTurnStarted?.Invoke(CurrentPlayerId.Value, tn);
    }

    private void StartGameSinglePlayer()
    {
        _gameStarted = true;
        var nm = NetworkManager.Singleton;
        if (nm != null)
            nm.OnClientConnectedCallback -= OnClientConnected;

        // единственный игрок — хост
        CurrentPlayerId.Value = NetworkManager.Singleton.LocalClientId;
        TurnNumber.Value = 1;
        StartTurn();
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
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
        // Стартуем с первого подключившегося клиента
        CurrentPlayerId.Value = clients[0];
        TurnNumber.Value = 1;

        StartTurn();
    }

    private void StartTurn()
    {
        // Сброс запаса движения и атаки у всех юнитов текущего игрока
        ResetUnitsForPlayer(CurrentPlayerId.Value);

        // Готовим ссылку на "первого" юнита текущего игрока (для центрирования камеры)
        NetworkObjectReference unitReference = new NetworkObjectReference(); // пустая по умолчанию
        _firstUnitOfCurrentPlayer = GetFirstUnitForPlayer(CurrentPlayerId.Value);
        if (_firstUnitOfCurrentPlayer != null)
        {
            unitReference = _firstUnitOfCurrentPlayer.NetworkObject;
        }

        // Оповещаем клиентов о старте хода, передаём ссылку на юнита для камеры
        StartTurnClientRpc(CurrentPlayerId.Value, TurnNumber.Value, unitReference);

        // Запускаем таймер хода на сервере
        if (_timerCoroutine != null) StopCoroutine(_timerCoroutine);
        _timerCoroutine = StartCoroutine(TurnTimer());
    }

    /// <summary>
    /// Сбрасывает MovementRemaining и CanAttack у всех юнитов заданного игрока.
    /// </summary>
    private void ResetUnitsForPlayer(ulong playerId)
    {
        // Для мгновенного центрирования камеры на сервере (опционально)
        Transform firstUnitTransform = null;

        foreach (var kvp in NetworkManager.Singleton.SpawnManager.SpawnedObjects)
        {
            var netObj = kvp.Value;
            if (netObj == null) continue;
            if (netObj.OwnerClientId != playerId) continue;

            var unit = netObj.GetComponent<UnitNetworkBehaviour>();
            if (unit != null)
            {
                // Сбрасываем запас передвижения и возможность атаки
                unit.MovementRemaining.Value = unit.moveSpeed;

                // ВНИМАНИЕ: поле называется _canAttack (латиница), см. UnitNetworkBehaviour
                unit._canAttack.Value = true;

                Debug.Log($"[TurnManager] Reset unit {netObj.NetworkObjectId} for player {playerId}");

                // Сохраняем первый попавшийся юнит для фокуса камеры (серверная часть)
                if (firstUnitTransform == null)
                {
                    firstUnitTransform = netObj.transform;
                }
            }
        }

        // Локальное (серверное) событие — если камера слушает на хосте/серве
        if (firstUnitTransform != null)
        {
            OnPlayerUnitTurnStarted?.Invoke(firstUnitTransform);
        }
    }

    [ClientRpc]
    private void StartTurnClientRpc(ulong playerId, int turn, NetworkObjectReference unitReference)
    {
        // Клиентское событие для UI
        OnTurnStarted?.Invoke(playerId, turn);

        // Если ссылка на юнит валидна — отдаём его Transform слушателям (например, камера)
        if (unitReference.TryGet(out var networkObject) && networkObject != null)
        {
            OnPlayerUnitTurnStarted?.Invoke(networkObject.transform);
        }
        else
        {
            Debug.LogWarning("[TurnManager] Юнит для центрирования камеры не найден.");
        }
       
    }

    /// <summary>
    /// Клиент или сервер просит завершить ход.
    /// </summary>
    public void RequestEndTurn()
    {
        Debug.Log($"[TurnManager] RequestEndTurn by {NetworkManager.Singleton.LocalClientId}, current is {CurrentPlayerId.Value}");
        if (IsServer || NetworkManager.Singleton.LocalClientId == CurrentPlayerId.Value)
            EndTurnServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void EndTurnServerRpc(ServerRpcParams rpcParams = default)
    {
        // Оповестить всех о конце хода
        EndTurnClientRpc(CurrentPlayerId.Value, TurnNumber.Value);

        // Смена игрока и номера хода
        var clients = NetworkManager.Singleton.ConnectedClientsIds;
        if (clients.Count == 0) return;

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
        // По истечении времени — завершаем ход
        EndTurnServerRpc();
    }

    public UnitNetworkBehaviour GetFirstUnitForPlayer(ulong playerId)
    {
        foreach (var kvp in NetworkManager.Singleton.SpawnManager.SpawnedObjects)
        {
            var netObj = kvp.Value;
            if (netObj == null) continue;
            if (netObj.OwnerClientId == playerId)
            {
                return netObj.GetComponent<UnitNetworkBehaviour>();
            }
        }
        return null;
    }
}
