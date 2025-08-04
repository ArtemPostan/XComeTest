// Assets/Scripts/Units/UnitSelectionManager.cs

using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class UnitSelectionManager : MonoBehaviour
{
    [Header("Selection Settings")]
    [SerializeField] private float dragThreshold = 10f;
    [SerializeField] private Color boxFill = new Color(0.8f, 0.8f, 0.95f, 0.25f);
    [SerializeField] private Color boxBorder = new Color(0.8f, 0.8f, 0.95f);

    private Camera _cam;
    private bool _isDragging;
    private Vector2 _dragStart;
    private bool _gameActive;

    private readonly List<UnitNetworkBehaviour> _selected = new List<UnitNetworkBehaviour>();

    void Start()
    {
        _cam = Camera.main;
        var tm = FindObjectOfType<TurnManager>();
        if (tm != null)
        {
            // активируем выбор только в свой ход
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

    private void SelectUnitUnderMouse()
    {
        DeselectAll();
        Ray ray = _cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out var hit))
        {
            var unit = hit.collider.GetComponentInParent<UnitNetworkBehaviour>();
            if (unit != null && unit.IsOwner)
                AddToSelection(unit);
        }
    }

    private void SelectUnitsInRect(Vector2 p1, Vector2 p2)
    {
        DeselectAll();
        Rect r = GetScreenRect(p1, p2);
        foreach (var unit in FindObjectsOfType<UnitNetworkBehaviour>())
        {
            if (!unit.IsOwner) continue;
            var sp = _cam.WorldToScreenPoint(unit.transform.position);
            var gp = new Vector2(sp.x, Screen.height - sp.y);
            if (r.Contains(gp))
                AddToSelection(unit);
        }
    }

    private void AddToSelection(UnitNetworkBehaviour u)
    {
        _selected.Add(u);
        u.SetSelected(true);
    }

    private void DeselectAll()
    {
        foreach (var u in _selected) u.SetSelected(false);
        _selected.Clear();
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
