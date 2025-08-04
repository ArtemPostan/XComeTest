// Assets/Scripts/UI/TurnUIManager.cs

using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using TMPro;

public class TurnUIManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private TextMeshProUGUI turnInfoText;
    [SerializeField] private Button endTurnButton;
    [SerializeField] private GameObject waitingPanel;
    [SerializeField] private TextMeshProUGUI waitingPanelText;

    [Header("Unit Info")]
    [SerializeField] private TextMeshProUGUI movementRemainingText;

    private TurnManager _turnManager;
    private UnitSelectionManager _selectionManager;
    private float _remainingTime;
    private bool _turnActive;

    private void Start()
    {
        _turnManager = FindObjectOfType<TurnManager>();
        if (_turnManager == null)
        {
            Debug.LogError("[TurnUIManager] TurnManager не найден!");
            return;
        }

        // Находим менеджер выделения
        _selectionManager = FindObjectOfType<UnitSelectionManager>();

        // Настраиваем панель ожидания
        waitingPanel.SetActive(true);
        if (waitingPanelText != null)
            waitingPanelText.text = "Ожидаем второго игрока";

        timerText.text = "--";
        turnInfoText.text = "";
        endTurnButton.interactable = false;
        movementRemainingText.text = "";

        // Подписываемся на события ходов
        _turnManager.OnTurnStarted.AddListener(OnTurnStarted);
        _turnManager.OnTurnEnded.AddListener(OnTurnEnded);

        // Кнопка «Передать ход»
        endTurnButton.onClick.AddListener(_turnManager.RequestEndTurn);
    }

    private void Update()
    {
        if (_turnActive)
        {
            _remainingTime -= Time.deltaTime;
            timerText.text = $"{_remainingTime:F1}s";
        }

        UpdateMovementRemainingUI();
    }

    private void OnTurnStarted(ulong playerId, int turnNumber)
    {
        // Скрываем панель ожидания при старте первого хода
        waitingPanel.SetActive(false);

        _turnActive = true;
        _remainingTime = _turnManager.TurnDuration; // Сделайте этот геттер публичным в TurnManager
        turnInfoText.text = $"Ход {turnNumber} — Игрок {playerId}";

        bool isMyTurn = (NetworkManager.Singleton.LocalClientId == playerId);
        endTurnButton.interactable = isMyTurn;
        movementRemainingText.gameObject.SetActive(isMyTurn);
        UpdateMovementRemainingUI();
    }

    private void OnTurnEnded(ulong playerId, int turnNumber)
    {
        _turnActive = false;
        endTurnButton.interactable = false;
        movementRemainingText.text = "";
        movementRemainingText.gameObject.SetActive(false);
    }

    private void UpdateMovementRemainingUI()
    {
        if (!_turnActive || _selectionManager == null)
            return;

        var selected = _selectionManager.SelectedUnits;
        if (selected != null && selected.Count > 0)
        {
            var unit = selected[0];
            movementRemainingText.text = $"Осталось хода: {unit.MovementRemaining.Value:F1}";
        }
        else
        {
            movementRemainingText.text = "";
        }
    }
}
