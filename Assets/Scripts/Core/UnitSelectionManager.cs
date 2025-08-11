// Assets/Scripts/Units/UnitSelectionManager.cs
//
// Важно: HandleRightMouse НЕ двигает по земле, если курсор над юнитом.
// Это предотвращает «движение поверх атаки», когда ПКМ нажимается по врагу.

using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class UnitSelectionManager : MonoBehaviour
{
    [Header("Selection Settings")]
    [SerializeField] private float dragThreshold = 10f;
    [SerializeField] private Color boxFill = new Color(0.8f, 0.8f, 0.95f, 0.25f);
    [SerializeField] private Color boxBorder = new Color(0.8f, 0.8f, 0.95f);

    [Header("Raycast")]
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private LayerMask unitMask = ~0;

    private GameObject _radiusDrawerObject;
    private UnitAttackRadiusDrawer _radiusDrawerComponent;

    private GameObject _pathPreviewObject;
    private UnitPathPreviewDrawer _pathPreview;

    private Camera _cam;
    private bool _isDragging;
    private Vector2 _dragStart;
    private bool _gameActive;

    private UnitNetworkBehaviour _selectedUnit;

    private readonly List<UnitNetworkBehaviour> _selected = new List<UnitNetworkBehaviour>();

    void Start()
    {
        _cam = Camera.main;
        var tm = FindObjectOfType<TurnManager>();

        bool isLocalPlay = FindObjectOfType<NetworkUtility>()?.localPlayMode ?? false;

        _radiusDrawerObject = new GameObject("AttackRadiusDrawer");
        _radiusDrawerComponent = _radiusDrawerObject.AddComponent<UnitAttackRadiusDrawer>();
        _radiusDrawerObject.transform.SetParent(transform);

        _pathPreviewObject = new GameObject("PathPreviewDrawer");
        _pathPreview = _pathPreviewObject.AddComponent<UnitPathPreviewDrawer>();
        _pathPreviewObject.transform.SetParent(transform);

        if (isLocalPlay)
        {
            _gameActive = true;
        }
        else if (tm != null)
        {
            tm.OnTurnStarted.AddListener((pid, turn) =>
                _gameActive = (NetworkManager.Singleton.LocalClientId == pid));
            tm.OnTurnEnded.AddListener((pid, turn) =>
                _gameActive = false);
        }
    }

    void Update()
    {
        if (!_gameActive) return;

        HandleLeftMouse();
        HandleRightMouse();

        UpdateAttackRadiusDrawing();
        UpdatePathPreview();
    }

    void OnGUI()
    {
        if (!_gameActive || !_isDragging) return;
        var rect = GetScreenRect(_dragStart, Input.mousePosition);
        DrawScreenRect(rect, boxFill);
        DrawScreenRectBorder(rect, 2, boxBorder);
    }

    private void HandleLeftMouse()
    {
        if (Input.GetMouseButtonDown(0))
        {
            _dragStart = Input.mousePosition;
            _isDragging = true;
        }
        if (Input.GetMouseButtonUp(0) && _isDragging)
        {
            var end = Input.mousePosition;
            if (Vector2.Distance(_dragStart, end) > dragThreshold)
                SelectUnitsInRect(_dragStart, end);
            else
                SelectUnitUnderMouse();
            _isDragging = false;
        }
    }

    private bool IsPointerOverUnit()
    {
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return false;
        Ray ray = _cam.ScreenPointToRay(Input.mousePosition);
        return Physics.Raycast(ray, out _, 1000f, unitMask, QueryTriggerInteraction.Ignore);
    }

    private void HandleRightMouse()
    {
        if (!Input.GetMouseButtonDown(1)) return;
        if (_selected.Count == 0) return;

        // Если под курсором юнит — НИЧЕГО не делаем здесь.
        // Пусть UnitCombatUIAndInput решит: атаковать или подойти.
        if (IsPointerOverUnit()) return;

        Ray ray = _cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out var hit, 1000f, groundMask, QueryTriggerInteraction.Ignore))
        {
            Vector3 destination = hit.point;

            if (_selected.Count == 1)
            {
                _selected[0].MoveTo(destination);
            }
            else
            {
                MoveGroupTo(destination);
            }
        }
    }

    private void SelectUnitUnderMouse()
    {
        DeselectAll();
        Ray ray = _cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out var hit, 1000f, ~0, QueryTriggerInteraction.Ignore))
        {
            var unit = hit.collider.GetComponentInParent<UnitNetworkBehaviour>();
            bool isLocalPlay = FindObjectOfType<NetworkUtility>()?.localPlayMode ?? false;

            if (unit != null && (isLocalPlay || unit.IsOwner))
            {
                SelectSingleUnit(unit);
            }
        }
    }

    private void SelectUnitsInRect(Vector2 p1, Vector2 p2)
    {
        DeselectAll();
        Rect r = GetScreenRect(p1, p2);

        bool isLocalPlay = FindObjectOfType<NetworkUtility>()?.localPlayMode ?? false;

        foreach (var unit in FindObjectsOfType<UnitNetworkBehaviour>())
        {
            if (!(isLocalPlay || unit.IsOwner)) continue;

            var sp = _cam.WorldToScreenPoint(unit.transform.position);
            var gp = new Vector2(sp.x, Screen.height - sp.y);
            if (r.Contains(gp))
            {
                _selected.Add(unit);
                unit.SetSelected(true);
            }
        }

        if (_selected.Count > 1)
        {
            ClearSingleSelection();
        }
    }

    private void SelectSingleUnit(UnitNetworkBehaviour u)
    {
        DeselectAll();
        _selectedUnit = u;

        _radiusDrawerComponent.SetRadius(_selectedUnit.AttackRadius);

        _selected.Add(u);
        u.SetSelected(true);
    }

    private void MoveGroupTo(Vector3 destination)
    {
        float radius = 2.0f;
        for (int i = 0; i < _selected.Count; i++)
        {
            float angle = i * Mathf.PI * 2f / _selected.Count;
            Vector3 offset = new Vector3(Mathf.Sin(angle) * radius, 0, Mathf.Cos(angle) * radius);
            Vector3 unitDestination = destination + offset;

            _selected[i].MoveTo(unitDestination);
        }
    }

    private void ClearSingleSelection()
    {
        if (_selectedUnit != null)
        {
            _radiusDrawerComponent.HideRadius();
            _selectedUnit = null;
        }
        _pathPreview.Hide();
    }

    private void DeselectAll()
    {
        foreach (var u in _selected) u.SetSelected(false);
        _selected.Clear();
        ClearSingleSelection();
    }

    private void UpdateAttackRadiusDrawing()
    {
        if (_selected.Count == 1 && _selected[0] != null)
        {
            var unit = _selected[0];
            _selectedUnit = unit;

            _radiusDrawerComponent.SetRadius(unit.AttackRadius);

            bool hasMovesLeft = unit.MovementRemaining.Value > 0;
            Vector3 drawPos;

            if (hasMovesLeft && TryGetGroundPointUnderMouse(out var ground))
            {
                drawPos = ground;
            }
            else
            {
                drawPos = unit.transform.position;
            }

            _radiusDrawerObject.transform.position = drawPos;
            _radiusDrawerComponent.ShowRadius();
        }
        else
        {
            _radiusDrawerComponent.HideRadius();
            _selectedUnit = null;
        }
    }

    private void UpdatePathPreview()
    {
        if (_selected.Count == 1 && _selected[0] != null && TryGetGroundPointUnderMouse(out var ground))
        {
            var unit = _selected[0];
            Vector3 start = unit.transform.position;
            Vector3 target = ground;
            float maxReach = Mathf.Max(0f, unit.MovementRemaining.Value);

            _pathPreview.Draw(start, target, maxReach);
        }
        else
        {
            _pathPreview.Hide();
        }
    }

    private bool TryGetGroundPointUnderMouse(out Vector3 point)
    {
        point = default;
        var ray = _cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out var hit, 1000f, groundMask, QueryTriggerInteraction.Ignore))
        {
            point = hit.point;
            return true;
        }
        return false;
    }

    private static Rect GetScreenRect(Vector2 a, Vector2 b)
    {
        Vector2 p1 = new Vector2(a.x, Screen.height - a.y);
        Vector2 p2 = new Vector2(b.x, Screen.height - b.y);
        Vector2 tl = Vector2.Min(p1, p2);
        Vector2 br = Vector2.Max(p1, p2);
        return Rect.MinMaxRect(tl.x, tl.y, br.x, br.y);
    }

    private static void DrawScreenRect(Rect r, Color c)
    {
        GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = Color.white;
    }

    private static void DrawScreenRectBorder(Rect r, float t, Color c)
    {
        DrawScreenRect(new Rect(r.xMin, r.yMin, r.width, t), c);
        DrawScreenRect(new Rect(r.xMin, r.yMin, t, r.height), c);
        DrawScreenRect(new Rect(r.xMax - t, r.yMin, t, r.height), c);
        DrawScreenRect(new Rect(r.xMin, r.yMax - t, r.width, t), c);
    }

    public IReadOnlyList<UnitNetworkBehaviour> SelectedUnits => _selected;
}
