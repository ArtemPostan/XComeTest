// Assets/Scripts/Core/TurnManager.cs

using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// ��������� ������������ ����� � ���������� ����� �������� ������ � ������ ����.
/// </summary>
public class TurnManager : NetworkBehaviour
{
    [Header("Turn Settings")]
    [SerializeField, Tooltip("������������ ���� � ��������")]
    private float turnDuration = 60f;
    public float TurnDuration => turnDuration;

    [Header("Events (UI can subscribe)")]
    public UnityEvent<ulong, int> OnTurnStarted; // (playerId, turnNumber)
    public UnityEvent<ulong, int> OnTurnEnded;   // (playerId, turnNumber)

    public NetworkVariable<ulong> CurrentPlayerId = new NetworkVariable<ulong>();
    public NetworkVariable<int> TurnNumber = new NetworkVariable<int>(1);

    private bool _gameStarted;
    private Coroutine _timerCoroutine;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            // ��� ������� ������
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            if (NetworkManager.Singleton.ConnectedClientsIds.Count >= 2)
                StartGame();
        }

        // ����������� UI �� ���������
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
        // ����� ������ �������� � ����� � ���� ������ �������� ������
        ResetUnitsForPlayer(CurrentPlayerId.Value);

        // ���������� ��������
        StartTurnClientRpc(CurrentPlayerId.Value, TurnNumber.Value);

        // ��������� ������ ����
        if (_timerCoroutine != null) StopCoroutine(_timerCoroutine);
        _timerCoroutine = StartCoroutine(TurnTimer());
    }

    /// <summary>
    /// ���������� MovementRemaining � CanAttack � ���� ������ ��������� ������.
    /// </summary>
    private void ResetUnitsForPlayer(ulong playerId)
    {
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
            }
        }
    }

    [ClientRpc]
    private void StartTurnClientRpc(ulong playerId, int turn)
    {
        OnTurnStarted?.Invoke(playerId, turn);
    }

    /// <summary>
    /// ������ ��� ������ ������ ��������� ���.
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
        // ���������� � ����� ����
        EndTurnClientRpc(CurrentPlayerId.Value, TurnNumber.Value);

        // ����� ������ � ������ ����
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
}
