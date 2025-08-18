// Assets/Scripts/Units/UnitNetworkBehaviour.cs
//
// Исправления:
// - CanAttack использует допуск epsilon, чтобы не промахиваться по float.
// - Добавлен StopMovement(), который мгновенно останавливает юнита (сбрасывает TargetPosition).
//   Вызывается после успешной атаки, чтобы не было "сдвига" после атаки.

using Unity.Netcode;
using UnityEngine;

public class UnitNetworkBehaviour : NetworkBehaviour
{   
    [field: SerializeField] public float moveSpeed { get; private set; } = 5f;
    [field: SerializeField] public float AttackRadius { get; private set; } = 3f;

    [Header("Health")]
    [SerializeField] private int maxHealth = 100;
    public int MaxHealth => maxHealth;

    public NetworkVariable<int> Health = new NetworkVariable<int>(
        100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<float> MovementRemaining = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<Vector3> TargetPosition = new NetworkVariable<Vector3>(
        Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> _canAttack = new NetworkVariable<bool>(
        true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private Renderer[] _renderers;
    private Color[] _originalColors;
    [SerializeField] private Color _selectedColor = new Color(0.2f, 0.8f, 1f, 1f);

    // === Death ===
    [SerializeField] private GameObject deathVfxPrefab; // опционально: эффект смерти
    [SerializeField] private float deathDespawnDelay = 1.0f;

    private bool _isDead;

    private void Awake()
    {
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

            TargetPosition.Value = transform.position;

            if (Health.Value <= 0)
                Health.Value = maxHealth;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            TargetPosition.Value = transform.position;
            if (MovementRemaining.Value <= 0f)
                MovementRemaining.Value = moveSpeed;

            if (Health.Value <= 0)
                Health.Value = maxHealth;
        }
    }

    private void Update()
    {
        if (!IsSpawned) return;

        if (TargetPosition.Value != transform.position)
        {
            transform.position = Vector3.MoveTowards(
                transform.position,
                TargetPosition.Value,
                moveSpeed * Time.deltaTime
            );
        }
    }

    // === ДВИЖЕНИЕ ===
    public void MoveTo(Vector3 position)
    {
        MoveToServerRpc(position);
    }

    [ServerRpc(RequireOwnership = false)]
    private void MoveToServerRpc(Vector3 requestedPosition, ServerRpcParams rpcParams = default)
    {
        bool isLocalPlay = NetworkUtility.Instance?.localPlayMode ?? false;

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

        Vector3 current = transform.position;
        float distanceToRequested = Vector3.Distance(current, requestedPosition);
        if (distanceToRequested <= 0.001f)
            return;

        if (MovementRemaining.Value >= distanceToRequested)
        {
            MovementRemaining.Value -= distanceToRequested;
            TargetPosition.Value = requestedPosition;
        }
        else
        {
            Vector3 dir = (requestedPosition - current).normalized;
            Vector3 partial = current + dir * MovementRemaining.Value;

            TargetPosition.Value = partial;
            MovementRemaining.Value = 0f;
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void StopMovementServerRpc(ServerRpcParams rpcParams = default)
    {
        bool isLocalPlay = NetworkUtility.Instance?.localPlayMode ?? false;
        if (!isLocalPlay && rpcParams.Receive.SenderClientId != OwnerClientId) return;

        TargetPosition.Value = transform.position;
    }

    public void StopMovement()
    {
        // Клиент просит сервер остановить движение
        StopMovementServerRpc();
    }

    // === АТАКА ===
    public bool CanAttack(UnitNetworkBehaviour targetUnit)
    {
        if (targetUnit == null) return false;
        const float epsilon = 0.05f; // небольшой допуск
        float distance = Vector3.Distance(transform.position, targetUnit.transform.position);
        return distance <= (AttackRadius + epsilon);
    }

    public void AttackTarget(NetworkObject targetNetworkObject, int damage)
    {
        AttackServerRpc(targetNetworkObject, damage);
    }

    [ServerRpc(RequireOwnership = false)]
    private void AttackServerRpc(NetworkObjectReference targetRef, int damage, ServerRpcParams rpcParams = default)
    {
        bool isLocalPlay = NetworkUtility.Instance?.localPlayMode ?? false;
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

        // Проверка радиуса с тем же epsilon, что и в CanAttack
        const float epsilon = 0.05f;
        float dist = Vector3.Distance(transform.position, targetUnit.transform.position);
        if (dist > AttackRadius + epsilon)
        {
            Debug.Log($"[Attack] Target out of range: {dist:F2} > {AttackRadius + epsilon:F2}");
            return;
        }

        // Урон
        targetUnit.ApplyDamageServerRpc(damage);

        // Снимаем возможность атаковать до конца хода
        _canAttack.Value = false;

        // ЖЁСТКО останавливаем движение после атаки
        TargetPosition.Value = transform.position;

        Debug.Log($"[Attack] {name} hit {targetUnit.name} for {damage}, target HP={targetUnit.Health.Value}");
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

        // Клиентам — показать анимацию/вfx, скрыть визуал/коллайдеры
        PlayDeathClientRpc(transform.position);

        // Через задержку — убрать объект из сети
        StartCoroutine(DespawnAfterDelay());
    }

    [ClientRpc]
    private void PlayDeathClientRpc(Vector3 at)
    {
        // Визуально "выключаем" юнит: прячем рендереры/коллайдеры
        foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = false;

        // Спавним vfx (если задан)
        if (deathVfxPrefab != null)
        {
            var vfx = Instantiate(deathVfxPrefab, at, Quaternion.identity);
            Destroy(vfx, 2f);
        }
    }

    private System.Collections.IEnumerator DespawnAfterDelay()
    {
        yield return new WaitForSeconds(deathDespawnDelay);

        // Безопасно убираем объект из сети (только на сервере)
        if (this != null && IsServer && TryGetComponent<NetworkObject>(out var no) && no.IsSpawned)
        {
            no.Despawn(true); // true — также уничтожит GameObject у клиента
        }
    }
}
