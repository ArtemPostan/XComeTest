// Assets/Scripts/Units/UnitNetworkBehaviour.cs

using System.Linq;
using Unity.Netcode;
using UnityEngine;

public class UnitNetworkBehaviour : NetworkBehaviour
{
    [Header("Stats")]
    [Tooltip("ћаксимальное рассто€ние перемещени€ за один ход")]
    public float moveSpeed = 5f;

    [Header("Smoothing")]
    [Tooltip("—корость сглаживани€ движени€ на клиентах")]
    [SerializeField] private float smoothingSpeed = 8f;

    [Header("Selection Visuals")]
    [SerializeField] private Renderer[] renderers;
    [SerializeField] private Color highlightColor = Color.yellow;

    public NetworkVariable<Vector3> NetPosition = new NetworkVariable<Vector3>();
    public NetworkVariable<float> MovementRemaining = new NetworkVariable<float>();
    public NetworkVariable<bool> CanAttack = new NetworkVariable<bool>(true);

    private Vector3 _targetPosition;
    private Color[][] _originalColors;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            NetPosition.Value = transform.position;
            MovementRemaining.Value = moveSpeed;
        }

        if (IsClient)
        {
            _targetPosition = NetPosition.Value;
            transform.position = _targetPosition;
            NetPosition.OnValueChanged += (_, newPos) => _targetPosition = newPos;
        }

        _originalColors = renderers
            .Select(r => r.materials.Select(m => m.color).ToArray())
            .ToArray();
    }

    private void Update()
    {
        if (IsClient)
        {
            transform.position = Vector3.Lerp(
                transform.position,
                _targetPosition,
                Time.deltaTime * smoothingSpeed
            );
        }
    }

    [ServerRpc(RequireOwnership = true)]
    public void MoveServerRpc(Vector3 target, ServerRpcParams rpcParams = default)
    {
        // ѕровер€ем, чей сейчас ход
        var sender = rpcParams.Receive.SenderClientId;
        var tm = FindObjectOfType<TurnManager>();
        if (tm == null || tm.CurrentPlayerId.Value != sender)
            return;

        float left = MovementRemaining.Value;
        Vector3 currentPos = NetPosition.Value;
        Vector3 direction = (target - currentPos);
        float requestedDist = direction.magnitude;

        if (requestedDist <= 0f || left <= 0f)
            return;

        Vector3 newPos;
        float usedDistance;
        if (requestedDist <= left)
        {
            // можем дойти до цели
            newPos = target;
            usedDistance = requestedDist;
        }
        else
        {
            // идЄм в направлении, пока не закончитс€ движение
            newPos = currentPos + direction.normalized * left;
            usedDistance = left;
        }

        NetPosition.Value = newPos;
        MovementRemaining.Value = Mathf.Max(0f, left - usedDistance);

        Debug.Log($"[Unit {NetworkObjectId}] Moved towards {target}, " +
                  $"used {usedDistance:F2}, remaining {MovementRemaining.Value:F2}");
    }

    [ServerRpc(RequireOwnership = true)]
    public void AttackServerRpc(ulong targetId, ServerRpcParams rpcParams = default)
    {
        var sender = rpcParams.Receive.SenderClientId;
        var tm = FindObjectOfType<TurnManager>();
        if (tm == null || tm.CurrentPlayerId.Value != sender)
            return;

        if (!CanAttack.Value) return;
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetId, out var obj))
            obj.Despawn();
        CanAttack.Value = false;
    }

    public void SetSelected(bool selected)
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            var mats = renderers[i].materials;
            for (int j = 0; j < mats.Length; j++)
                mats[j].color = selected ? highlightColor : _originalColors[i][j];
        }
    }
}
