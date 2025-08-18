// Assets/Scripts/UI/UnitDraftUI.cs
//
// ќбновлено: теперь берЄм Sprite и красивое им€ с префаба через UnitIconProvider
// и кладЄм в iconImages[i] / nameLabels[i].

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UnitDraftUI : MonoBehaviour
{
    [Header("UI Refs per slot (size must be 5)")]
    public Button[] upButtons;
    public Button[] downButtons;
    public TMP_Text[] nameLabels;
    public Image[] iconImages;

    [Header("Controls")]
    public Button readyButton;

    [Header("Fallbacks")]
    [Tooltip("»конка по умолчанию, если у префаба не задана.")]
    public Sprite defaultIcon;

    private UnitDraftManager _manager;
    private List<GameObject> _catalog;
    private int _slots = 5;
    private int[] _selection;

    public void Bind(UnitDraftManager manager)
    {
        _manager = manager;
    }

    public void SetCatalog(List<GameObject> unitPrefabs)
    {
        _catalog = unitPrefabs ?? new List<GameObject>();
        RefreshAll();
    }

    public void SetupSlots(int slots)
    {
        _slots = Mathf.Max(1, slots);
        _selection = new int[_slots];
        for (int i = 0; i < _selection.Length; i++) _selection[i] = 0;

        for (int i = 0; i < _slots; i++)
        {
            int idx = i;
            if (upButtons != null && i < upButtons.Length && upButtons[i] != null)
                upButtons[i].onClick.AddListener(() => Change(idx, +1));
            if (downButtons != null && i < downButtons.Length && downButtons[i] != null)
                downButtons[i].onClick.AddListener(() => Change(idx, -1));
        }

        if (readyButton != null)
        {
            readyButton.onClick.RemoveAllListeners();
            readyButton.onClick.AddListener(OnReadyClicked);
        }

        RefreshAll();
    }

    private void Change(int slot, int delta)
    {
        if (_catalog == null || _catalog.Count == 0) return;

        int max = _catalog.Count;
        int cur = _selection[slot];
        int next = (cur + delta) % max;
        if (next < 0) next += max;

        _selection[slot] = next;
        RefreshSlot(slot);
    }

    private void RefreshAll()
    {
        if (_catalog == null) return;
        for (int i = 0; i < _slots; i++) RefreshSlot(i);
    }

    private void RefreshSlot(int i)
    {
        if (_catalog == null || _catalog.Count == 0) return;
        int idx = Mathf.Clamp(_selection[i], 0, _catalog.Count - 1);
        var prefab = _catalog[idx];

        // »м€
        string displayName = prefab != null ? prefab.name : $"Unit {idx}";
        var provider = prefab != null ? prefab.GetComponent<UnitIconProvider>() : null;
        if (provider != null && !string.IsNullOrWhiteSpace(provider.DisplayName))
            displayName = provider.DisplayName;

        if (nameLabels != null && i < nameLabels.Length && nameLabels[i] != null)
            nameLabels[i].text = displayName;

        // »конка
        Sprite icon = provider != null && provider.Icon != null ? provider.Icon : defaultIcon;
        if (iconImages != null && i < iconImages.Length && iconImages[i] != null)
        {
            iconImages[i].sprite = icon;
            iconImages[i].enabled = (icon != null);
            // дл€ красоты можно подогнать Preserve Aspect
            iconImages[i].preserveAspect = true;
        }
    }

    private void OnReadyClicked()
    {
        if (_manager == null) return;

        var payload = new UnitDraftManager.LoadoutPayload(_selection);
        _manager.SubmitLoadoutServerRpc(payload);

        if (readyButton != null) readyButton.interactable = false;
        SetButtonsInteractable(false);
    }

    private void SetButtonsInteractable(bool on)
    {
        if (upButtons != null) foreach (var b in upButtons) if (b != null) b.interactable = on;
        if (downButtons != null) foreach (var b in downButtons) if (b != null) b.interactable = on;
    }
}
