using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

public class UnitNetworkBehaviour : NetworkBehaviour
{
    [field: SerializeField] public float moveSpeed { get; private set; } = 5f;
    [field: SerializeField] public float AttackRadius { get; private set; } = 3f;
    [field: SerializeField] public int attackDamage { get; private set; } = 10;

    [Header("Health")]
    [SerializeField] private int maxHealth = 100;
    public int MaxHealth => maxHealth;

    public NetworkVariable<int> Health = new NetworkVariable<int>(
        100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<float> MovementRemaining = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> _canAttack = new NetworkVariable<bool>(
        true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private Renderer[] _renderers;
    private Color[] _originalColors;
    [SerializeField] private Color _selectedColor = new Color(0.2f, 0.8f, 1f, 1f);

    private NavMeshAgent _agent;

    // === Death ===
    [SerializeField] private GameObject deathVfxPrefab;
    [SerializeField] private float deathDespawnDelay = 1.0f;

    private bool _isDead;

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        if (_agent == null)
        {
            Debug.LogError("NavMeshAgent component not found on UnitNetworkBehaviour. Please add it to the prefab.");
            return;
        }

        _renderers = GetComponentsInChildren<Renderer>(true);
        _originalColors = new Color[_renderers.Length];
        for (int i = 0; i < _renderers.Length; i++)
        {
            _originalColors[i] = _renderers[i].material.HasProperty("_Color")
                ? _renderers[i].material.color
                : Color.white;
        }
    }

    private void Start()
    {
        if (IsServer)
        {
            if (MovementRemaining.Value <= 0f)
                MovementRemaining.Value = moveSpeed;

            if (Health.Value <= 0)
                Health.Value = maxHealth;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            if (MovementRemaining.Value <= 0f)
                MovementRemaining.Value = moveSpeed;

            if (Health.Value <= 0)
                Health.Value = maxHealth;
        }

        MovementRemaining.OnValueChanged += OnMovementRemainingChanged;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        if (IsServer)
        {
            MovementRemaining.OnValueChanged -= OnMovementRemainingChanged;
        }
    }

    private void OnMovementRemainingChanged(float oldVal, float newVal)
    {
        _agent.speed = newVal > 0 ? moveSpeed : 0;
    }

    private void Update()
    {
        if (!IsSpawned) return;

        if (!IsHost && _agent.enabled)
        {
        }
    }

    // === ДВИЖЕНИЕ ===
    public void MoveTo(Vector3 position)
    {
        if (IsOwner || (NetworkManager.IsHost && !IsClient))
        {
            MoveToServerRpc(position);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void MoveToServerRpc(Vector3 requestedPosition, ServerRpcParams rpcParams = default)
    {
        bool isLocalPlay = FindObjectOfType<NetworkUtility>()?.localPlayMode ?? false;

        if (!isLocalPlay && rpcParams.Receive.SenderClientId != OwnerClientId)
        {
            Debug.LogWarning($"[Move] Reject from {rpcParams.Receive.SenderClientId}, owner is {OwnerClientId}");
            return;
        }

        if (MovementRemaining.Value <= 0f)
        {
            Debug.Log($"[Move] No movement points for {name}");
            return;
        }

        _agent.SetDestination(requestedPosition);
        MoveToClientRpc(requestedPosition);
    }

    [ClientRpc]
    private void MoveToClientRpc(Vector3 requestedPosition)
    {
        _agent.SetDestination(requestedPosition);
    }

    [ServerRpc(RequireOwnership = false)]
    public void StopMovementServerRpc(ServerRpcParams rpcParams = default)
    {
        bool isLocalPlay = FindObjectOfType<NetworkUtility>()?.localPlayMode ?? false;
        if (!isLocalPlay && rpcParams.Receive.SenderClientId != OwnerClientId) return;

        if (_agent.enabled)
        {
            _agent.isStopped = true;
            _agent.ResetPath();
        }
    }

    public void StopMovement()
    {
        StopMovementServerRpc();
    }

    // === АТАКА ===
    public bool CanAttack(UnitNetworkBehaviour targetUnit)
    {
        if (targetUnit == null) return false;
        const float epsilon = 0.05f;
        float distance = Vector3.Distance(transform.position, targetUnit.transform.position);
        return distance <= (AttackRadius + epsilon);
    }

    public void AttackTarget(NetworkObject targetNetworkObject)
    {
        if (IsOwner || (NetworkManager.IsHost && !IsClient))
        {
            AttackServerRpc(targetNetworkObject);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void AttackServerRpc(NetworkObjectReference targetRef, ServerRpcParams rpcParams = default)
    {
        bool isLocalPlay = FindObjectOfType<NetworkUtility>()?.localPlayMode ?? false;
        if (!isLocalPlay && rpcParams.Receive.SenderClientId != OwnerClientId)
        {
            Debug.LogWarning($"[Attack] Reject from {rpcParams.Receive.SenderClientId}, owner is {OwnerClientId}");
            return;
        }

        if (!_canAttack.Value)
        {
            Debug.Log($"[Attack] {name} cannot attack this turn");
            return;
        }

        if (!targetRef.TryGet(out var targetNO))
        {
            Debug.LogWarning("[Attack] targetRef invalid");
            return;
        }

        var targetUnit = targetNO.GetComponent<UnitNetworkBehaviour>();
        if (targetUnit == null)
        {
            Debug.LogWarning("[Attack] target has no UnitNetworkBehaviour");
            return;
        }

        const float epsilon = 0.05f;
        float dist = Vector3.Distance(transform.position, targetUnit.transform.position);
        if (dist > AttackRadius + epsilon)
        {
            Debug.Log($"[Attack] Target out of range: {dist:F2} > {AttackRadius + epsilon:F2}");
            return;
        }

        targetUnit.ApplyDamageServerRpc(attackDamage);

        _canAttack.Value = false;

        if (_agent.enabled)
        {
            _agent.isStopped = true;
            _agent.ResetPath();
        }

        Debug.Log($"[Attack] {name} hit {targetUnit.name} for {attackDamage}, target HP={targetUnit.Health.Value}");
    }

    [ServerRpc(RequireOwnership = false)]
    public void ApplyDamageServerRpc(int damage)
    {
        if (!IsServer) return;
        int newHp = Mathf.Max(0, Health.Value - Mathf.Max(0, damage));
        Health.Value = newHp;

        if (Health.Value == 0)
        {
            TryDieServer();
        }
    }

    // === НОВЫЕ МЕТОДЫ ДЛЯ ПРЕДПРОСМОТРА ПУТИ ===

    [ServerRpc(RequireOwnership = false)]
    public void RequestPathPreviewServerRpc(Vector3 destination, ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId) return;

        NavMeshPath path = new NavMeshPath();
        if (_agent != null && NavMesh.CalculatePath(transform.position, destination, NavMesh.AllAreas, path))
        {
            ClientRpcParams clientRpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new ulong[] { rpcParams.Receive.SenderClientId }
                }
            };
            DrawPathPreviewClientRpc(path.corners, MovementRemaining.Value, clientRpcParams);
        }
        else
        {
            ClientRpcParams clientRpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new ulong[] { rpcParams.Receive.SenderClientId }
                }
            };
            HidePathPreviewClientRpc(clientRpcParams);
        }
    }

    [ClientRpc]
    private void DrawPathPreviewClientRpc(Vector3[] corners, float movementRemaining, ClientRpcParams clientRpcParams = default)
    {
        var manager = FindObjectOfType<UnitSelectionManager>();
        if (manager != null)
        {
            manager.DrawPath(corners, movementRemaining);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void HidePathPreviewServerRpc(ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId) return;

        ClientRpcParams clientRpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new ulong[] { rpcParams.Receive.SenderClientId }
            }
        };
        HidePathPreviewClientRpc(clientRpcParams);
    }

    [ClientRpc]
    private void HidePathPreviewClientRpc(ClientRpcParams clientRpcParams = default)
    {
        var manager = FindObjectOfType<UnitSelectionManager>();
        if (manager != null)
        {
            manager.HidePath();
        }
    }

    // === ВИЗУАЛЬНОЕ ВЫДЕЛЕНИЕ ===
    public void SetSelected(bool isSelected)
    {
        for (int i = 0; i < _renderers.Length; i++)
        {
            if (_renderers[i] == null) continue;
            if (_renderers[i].material.HasProperty("_Color"))
                _renderers[i].material.color = isSelected ? _selectedColor : _originalColors[i];
        }
    }

    private void TryDieServer()
    {
        if (!IsServer || _isDead) return;
        if (Health.Value > 0) return;

        _isDead = true;

        PlayDeathClientRpc(transform.position);

        StartCoroutine(DespawnAfterDelay());
    }

    [ClientRpc]
    private void PlayDeathClientRpc(Vector3 at)
    {
        foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = false;

        if (_agent != null) _agent.enabled = false;

        if (deathVfxPrefab != null)
        {
            var vfx = Instantiate(deathVfxPrefab, at, Quaternion.identity);
            Destroy(vfx, 2f);
        }
    }

    private System.Collections.IEnumerator DespawnAfterDelay()
    {
        yield return new WaitForSeconds(deathDespawnDelay);

        if (this != null && IsServer && TryGetComponent<NetworkObject>(out var no) && no.IsSpawned)
        {
            no.Despawn(true);
        }
    }
}