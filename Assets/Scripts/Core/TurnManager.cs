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
                // ��������� ����: �� ��� ������� �������
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

        // ������������ ����� � ����
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
        // �������� � ������� ��������������� �������
        CurrentPlayerId.Value = clients[0];
        TurnNumber.Value = 1;

        StartTurn();
    }

    private void StartTurn()
    {
        // ����� ������ �������� � ����� � ���� ������ �������� ������
        ResetUnitsForPlayer(CurrentPlayerId.Value);

        // ������� ������ �� "�������" ����� �������� ������ (��� ������������� ������)
        NetworkObjectReference unitReference = new NetworkObjectReference(); // ������ �� ���������
        _firstUnitOfCurrentPlayer = GetFirstUnitForPlayer(CurrentPlayerId.Value);
        if (_firstUnitOfCurrentPlayer != null)
        {
            unitReference = _firstUnitOfCurrentPlayer.NetworkObject;
        }

        // ��������� �������� � ������ ����, ������� ������ �� ����� ��� ������
        StartTurnClientRpc(CurrentPlayerId.Value, TurnNumber.Value, unitReference);

        // ��������� ������ ���� �� �������
        if (_timerCoroutine != null) StopCoroutine(_timerCoroutine);
        _timerCoroutine = StartCoroutine(TurnTimer());
    }

    /// <summary>
    /// ���������� MovementRemaining � CanAttack � ���� ������ ��������� ������.
    /// </summary>
    private void ResetUnitsForPlayer(ulong playerId)
    {
        // ��� ����������� ������������� ������ �� ������� (�����������)
        Transform firstUnitTransform = null;

        foreach (var kvp in NetworkManager.Singleton.SpawnManager.SpawnedObjects)
        {
            var netObj = kvp.Value;
            if (netObj == null) continue;
            if (netObj.OwnerClientId != playerId) continue;

            var unit = netObj.GetComponent<UnitNetworkBehaviour>();
            if (unit != null)
            {
                // ���������� ����� ������������ � ����������� �����
                unit.MovementRemaining.Value = unit.moveSpeed;

                // ��������: ���� ���������� _canAttack (��������), ��. UnitNetworkBehaviour
                unit._canAttack.Value = true;

                Debug.Log($"[TurnManager] Reset unit {netObj.NetworkObjectId} for player {playerId}");

                // ��������� ������ ���������� ���� ��� ������ ������ (��������� �����)
                if (firstUnitTransform == null)
                {
                    firstUnitTransform = netObj.transform;
                }
            }
        }

        // ��������� (���������) ������� � ���� ������ ������� �� �����/�����
        if (firstUnitTransform != null)
        {
            OnPlayerUnitTurnStarted?.Invoke(firstUnitTransform);
        }
    }

    [ClientRpc]
    private void StartTurnClientRpc(ulong playerId, int turn, NetworkObjectReference unitReference)
    {
        // ���������� ������� ��� UI
        OnTurnStarted?.Invoke(playerId, turn);

        // ���� ������ �� ���� ������� � ����� ��� Transform ���������� (��������, ������)
        if (unitReference.TryGet(out var networkObject) && networkObject != null)
        {
            OnPlayerUnitTurnStarted?.Invoke(networkObject.transform);
        }
        else
        {
            Debug.LogWarning("[TurnManager] ���� ��� ������������� ������ �� ������.");
        }
       
    }

    /// <summary>
    /// ������ ��� ������ ������ ��������� ���.
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
        // ���������� ���� � ����� ����
        EndTurnClientRpc(CurrentPlayerId.Value, TurnNumber.Value);

        // ����� ������ � ������ ����
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
        // �� ��������� ������� � ��������� ���
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
