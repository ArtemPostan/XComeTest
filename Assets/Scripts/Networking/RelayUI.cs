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
    [SerializeField] private GameObject waitingPanel;

    private NetworkUtility _netUtil;

    private void Awake()
    {
        // ���� NetworkUtility � ���� �� GameObject
        _netUtil = GetComponent<NetworkUtility>();
        if (_netUtil == null)
        {
            Debug.LogError("[RelayUI] NetworkUtility �� ������!");
            enabled = false;
        }
    }

    private void Start()
    {
        // ����������� ������
        connectButton.onClick.AddListener(OnConnectClicked);
        //�������� �� ����������
        //joinCodeText.gameObject.SetActive(false);
        //joinInput.gameObject.SetActive(false);
        //connectButton.gameObject.SetActive(false);
        waitingPanel.SetActive(false);
        
    }

    private void Update()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        // ����: NetworkManager �������� � ��� ������, � ��� ������
        if (nm.IsHost)
        {           
            string code = _netUtil.JoinCode;
            joinCodeText.text = string.IsNullOrEmpty(code)
                ? "���������� ���..."
                : $"Your join code:\n<size=24><b>{code}</b></size>";

            joinCodeText.gameObject.SetActive(true);
            waitingPanel.SetActive(true);

            joinInput.gameObject.SetActive(false);
            connectButton.gameObject.SetActive(false);
            return;
        }

        // ������ �� �����������
        if (nm.IsClient)
        {            
            joinInput.gameObject.SetActive(true);
            connectButton.gameObject.SetActive(true);

            joinCodeText.gameObject.SetActive(false);
            waitingPanel.SetActive(false);
            return;
        }

        //// ������ ����� �����������
        //if (nm.IsConnectedClient)
        //{
        //    joinCodeText.gameObject.SetActive(false);
        //    joinInput.gameObject.SetActive(false);
        //    connectButton.gameObject.SetActive(false);
        //    waitingPanel.SetActive(false);
        //}
    }

    private void OnConnectClicked()
    {
        string code = joinInput.text.Trim();
        if (string.IsNullOrEmpty(code))
            return;
        Debug.Log($"[RelayUI] Connect clicked with code: '{code}'");
        _netUtil.SetJoinCodeAndConnect(code);
    }
}
