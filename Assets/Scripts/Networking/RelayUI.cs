// Assets/Scripts/UI/RelayUI.cs

using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

[RequireComponent(typeof(NetworkUtility))]
public class RelayUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI joinCodeText;
    [SerializeField] private TMP_InputField joinInput;
    [SerializeField] private Button connectButton;
    [SerializeField] private Button hostButton;
  

    private NetworkUtility _netUtil;

    private void Awake()
    {
        _netUtil = GetComponent<NetworkUtility>();
        if (_netUtil == null)
        {
            Debug.LogError("[RelayUI] NetworkUtility не найден!");
            enabled = false;
        }
    }

    private void Start()
    {
        // Настраиваем кнопки
        connectButton.onClick.AddListener(OnConnectClicked);
        hostButton.onClick.AddListener(OnHostClicked);

        // Изначально скрываем все, кроме кнопок
        UpdateUI();
    }

    private void Update()
    {
        UpdateUI();
    }

    private void UpdateUI()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        // Если хост запущен, показываем код и панель ожидания
        if (nm.IsHost)
        {
            string code = _netUtil.JoinCode;
            joinCodeText.text = string.IsNullOrEmpty(code)
                ? "Генерируем код..."
                : $"Ваш код для подключения:\n<size=24><b>{code}</b></size>";

            joinCodeText.gameObject.SetActive(true);
           
            joinInput.gameObject.SetActive(false);
            connectButton.gameObject.SetActive(false);
            hostButton.gameObject.SetActive(false);
            return;
        }

        // Если клиент подключился, скрываем всё, кроме панели ожидания
        if (nm.IsClient)
        {
            joinCodeText.gameObject.SetActive(false);
            joinInput.gameObject.SetActive(false);
            connectButton.gameObject.SetActive(false);
            hostButton.gameObject.SetActive(false);
           
            return;
        }

        // Если игра не запущена (начальное состояние), показываем UI для ввода кода
        joinCodeText.gameObject.SetActive(false);
        
        joinInput.gameObject.SetActive(true);
        connectButton.gameObject.SetActive(true);
        hostButton.gameObject.SetActive(true);
    }

    private void OnHostClicked()
    {
        Debug.Log("[RelayUI] Host button clicked.");
        _netUtil.StartHost();
    }

    private void OnConnectClicked()
    {
        string code = joinInput.text.Trim();
        if (string.IsNullOrEmpty(code))
            return;
        Debug.Log($"[RelayUI] Connect clicked with code: '{code}'");
        _netUtil.StartClient(code);
    }
}