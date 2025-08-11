// Assets/Scripts/Units/UnitCommandController.cs

using System.Collections;
using UnityEngine;
using Unity.Netcode;

public class UnitCommandController : MonoBehaviour
{
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private GameObject moveMarkerPrefab;
    [SerializeField] private float markerYOffset = 0.1f;
    [SerializeField] private float markerLifetime = 2f;

    private Camera _cam;
    private UnitSelectionManager _selMgr;
    private bool _gameActive;

    private void Start()
    {
        _cam = Camera.main;
        _selMgr = FindObjectOfType<UnitSelectionManager>();

        var tm = FindObjectOfType<TurnManager>();
        if (tm != null)
        {
            tm.OnTurnStarted.AddListener((pid, turn) =>
                _gameActive = (NetworkManager.Singleton.LocalClientId == pid));
            tm.OnTurnEnded.AddListener((pid, turn) =>
                _gameActive = false);
        }
    }

    private void Update()
    {
        if (!_gameActive) return;
        if (Input.GetMouseButtonDown(1))
            TryMove();
    }

    private void TryMove()
    {
        var sel = _selMgr.SelectedUnits;
        if (sel.Count == 0) return;

        Ray ray = _cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out var hit, Mathf.Infinity, groundLayer))
        {
            Vector3 tgt = hit.point;
            if (moveMarkerPrefab)
            {
                var m = Instantiate(moveMarkerPrefab, tgt + Vector3.up * markerYOffset, Quaternion.identity);
                StartCoroutine(AnimateAndDestroyMarker(m));
            }
            foreach (var u in sel)
                u.MoveTo(tgt); // <-- Здесь вызываем публичный метод MoveTo
        }
    }

    private IEnumerator AnimateAndDestroyMarker(GameObject marker)
    {
        float elapsed = 0f;
        Vector3 initialScale = marker.transform.localScale;

        while (elapsed < markerLifetime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / markerLifetime);
            marker.transform.localScale = initialScale * (1f - t);
            yield return null;
        }

        Destroy(marker);
    }
}
