// Assets/Scripts/GamePlay/UnitCombatUIAndInput.cs
//
// ПКМ по врагу:
//  - если враг в радиусе атаки -> ТОЛЬКО атака (движение НЕ запускается);
//  - если не в радиусе -> движение к врагу (с частичным шагом делает UnitNetworkBehaviour).
//
// Также меняет курсор (если валидные текстуры) либо показывает fallback-иконку над врагом.

using UnityEngine;
using Unity.Netcode;

[DefaultExecutionOrder(205)]
public class UnitCombatUIAndInput : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UnitSelectionManager selectionManager;

    [Header("Cursor Icons (source)")]
    [SerializeField] private Texture2D cursorDefault;
    [SerializeField] private Texture2D cursorAttack;
    [SerializeField] private Vector2 cursorHotspot = new Vector2(8, 8);
    [SerializeField] private CursorMode cursorMode = CursorMode.Auto;

    [Header("Fallback Sprite (if cursor textures invalid/unreadable)")]
    [SerializeField] private Sprite attackFallbackSprite;
    [SerializeField] private Vector3 iconLocalOffset = new Vector3(0f, 1.6f, 0f);
    [SerializeField] private float iconBillboardScale = 1.0f;
    [SerializeField] private int iconSortingOrder = 5000;

    [Header("Raycast")]
    [SerializeField] private LayerMask unitMask = ~0;

    [Header("Gameplay")]
    [SerializeField] private int attackDamage = 10;

    private Camera _cam;
    private UnitNetworkBehaviour _hoverEnemy;
    private UnitNetworkBehaviour _selectedUnit;
    private bool _activeTurn;
    private bool _isLocalPlay;

    private Texture2D _cursorDefaultSafe;
    private Texture2D _cursorAttackSafe;

    private GameObject _iconGO;
    private SpriteRenderer _iconSR;

    private void Awake()
    {
        _cam = Camera.main;
        if (selectionManager == null)
            selectionManager = FindObjectOfType<UnitSelectionManager>();

        _isLocalPlay = FindObjectOfType<NetworkUtility>()?.localPlayMode ?? false;

        var tm = FindObjectOfType<TurnManager>();
        if (tm != null)
        {
            tm.OnTurnStarted.AddListener((pid, turn) => _activeTurn = (NetworkManager.Singleton.LocalClientId == pid));
            tm.OnTurnEnded.AddListener((pid, turn) => _activeTurn = false);
        }

        _cursorDefaultSafe = MakeCursorSafe(cursorDefault);
        _cursorAttackSafe = MakeCursorSafe(cursorAttack);

        _iconGO = new GameObject("AttackIcon_Fallback");
        _iconGO.transform.SetParent(transform, false);
        _iconSR = _iconGO.AddComponent<SpriteRenderer>();
        _iconSR.sprite = attackFallbackSprite;
        _iconSR.sortingOrder = iconSortingOrder;
        _iconSR.enabled = false;
        var shader = Shader.Find("Sprites/Default");
        if (shader != null) _iconSR.material = new Material(shader);

        SetDefaultPointerVisual();
    }

    private void OnDisable()
    {
        SetDefaultPointerVisual();
        if (_iconSR != null) _iconSR.enabled = false;
    }

    private void Update()
    {
        if (selectionManager == null) return;

        _selectedUnit = null;
        if (selectionManager.SelectedUnits.Count == 1)
        {
            var u = selectionManager.SelectedUnits[0];
            if (u != null && (_isLocalPlay || u.IsOwner))
                _selectedUnit = u;
        }

        if (!_isLocalPlay && !_activeTurn)
        {
            SetDefaultPointerVisual();
            return;
        }

        if (_selectedUnit == null)
        {
            SetDefaultPointerVisual();
            return;
        }

        _hoverEnemy = RaycastEnemyUnderMouse();
        UpdatePointerVisual();

        if (Input.GetMouseButtonDown(1) && _hoverEnemy != null)
        {
            bool canAttackNow = _selectedUnit._canAttack.Value && _selectedUnit.CanAttack(_hoverEnemy);

            if (canAttackNow)
            {
                // 1) Атакуем
                _selectedUnit.AttackTarget(_hoverEnemy.NetworkObject, attackDamage);

                // 2) ЖЁСТКО останавливаем любое запущенное движение (на всякий случай)
                _selectedUnit.StopMovement();

                // 3) НИЧЕГО не двигаем после атаки
                return;
            }

            // Если не достаём — двигаемся к врагу (частичный шаг внутри MoveToServerRpc)
            if (_selectedUnit.MovementRemaining.Value > 0f)
            {
                _selectedUnit.MoveTo(_hoverEnemy.transform.position);
            }
        }
    }

    private UnitNetworkBehaviour RaycastEnemyUnderMouse()
    {
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return null;

        Ray ray = _cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out var hit, 1000f, unitMask, QueryTriggerInteraction.Ignore))
        {
            var unit = hit.collider.GetComponentInParent<UnitNetworkBehaviour>();
            if (unit != null)
            {
                if (_isLocalPlay || unit.OwnerClientId != NetworkManager.Singleton.LocalClientId)
                    return unit;
            }
        }
        return null;
    }

    private void UpdatePointerVisual()
    {
        bool canAttackNow = (_hoverEnemy != null) &&
                            _selectedUnit._canAttack.Value &&
                            _selectedUnit.CanAttack(_hoverEnemy);

        if (_cursorDefaultSafe != null && _cursorAttackSafe != null)
        {
            Cursor.SetCursor(canAttackNow ? _cursorAttackSafe : _cursorDefaultSafe,
                             ClampHotspot(canAttackNow ? _cursorAttackSafe : _cursorDefaultSafe, cursorHotspot),
                             cursorMode);
            if (_iconSR.enabled) _iconSR.enabled = false;
        }
        else
        {
            if (_hoverEnemy != null && attackFallbackSprite != null)
            {
                _iconGO.transform.position = _hoverEnemy.transform.position + iconLocalOffset;
                if (_cam != null)
                {
                    var fwd = _cam.transform.forward; fwd.y = 0f;
                    if (fwd.sqrMagnitude > 0.0001f) _iconGO.transform.rotation = Quaternion.LookRotation(fwd);
                }
                _iconGO.transform.localScale = Vector3.one * iconBillboardScale;
                _iconSR.color = canAttackNow ? Color.green : Color.red;
                _iconSR.enabled = true;
            }
            else
            {
                if (_iconSR.enabled) _iconSR.enabled = false;
            }
        }
    }

    private void SetDefaultPointerVisual()
    {
        if (_cursorDefaultSafe != null)
            Cursor.SetCursor(_cursorDefaultSafe, ClampHotspot(_cursorDefaultSafe, cursorHotspot), cursorMode);
        if (_iconSR != null) _iconSR.enabled = false;
    }

    private static Texture2D MakeCursorSafe(Texture2D src)
    {
        if (src == null) return null;

        bool isRGBA32 = src.format == TextureFormat.RGBA32 || src.format == TextureFormat.ARGB32;
        bool noMips = src.mipmapCount <= 1;
        bool readable = false;
        try { readable = src.isReadable; } catch { readable = true; }

        if (isRGBA32 && noMips && readable)
            return src;

        if (!readable) return null;

        var copy = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false, false)
        {
            name = src.name + "_RGBA32_NoMips",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        copy.SetPixels32(src.GetPixels32());
        copy.Apply(false, false);
        return copy;
    }

    private static Vector2 ClampHotspot(Texture2D tex, Vector2 hotspot)
    {
        if (tex == null) return hotspot;
        float x = Mathf.Clamp(hotspot.x, 0, tex.width - 1);
        float y = Mathf.Clamp(hotspot.y, 0, tex.height - 1);
        return new Vector2(x, y);
    }
}
