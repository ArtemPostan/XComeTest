// Assets/Scripts/Core/NetworkUtility.cs

using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using System.Threading.Tasks;
using Unity.Networking.Transport.Relay;

public class NetworkUtility : MonoBehaviour
{
    public static NetworkUtility Instance { get; private set; }

    [Header("Relay Settings")]
    [Tooltip("Сколько удалённых клиентов (помимо хоста)")]
    [SerializeField] private int maxConnections = 1;
    [Tooltip("Код для подключения (пусто → это хост)")]
    [SerializeField] private string joinCode;

    private bool _initialized;

    private void Awake()
    {
        // Singleton
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }
    }

    private async void Start()
    {
        // 1) Инициализация Unity Services + аутентификация
        if (!_initialized)
        {
            await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            _initialized = true;
        }

        // 2) Запускаем Relay-логику
        await SetupRelayAndStartAsync();
    }

    private async Task SetupRelayAndStartAsync()
    {
        var nm = NetworkManager.Singleton;
        var transport = nm.GetComponent<UnityTransport>();

        bool isWebGL = Application.platform == RuntimePlatform.WebGLPlayer;

        string connectionType;

        // ─── ХОСТ ───
        if (string.IsNullOrWhiteSpace(joinCode))
        {
            // Хост всегда использует UDP/DTLS.
            transport.UseWebSockets = false;

            // Создаем аллокацию для хоста
            var allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
            joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            Debug.Log($"[NetworkUtility] Relay HOST. JoinCode = {joinCode}");

            connectionType = "dtls"; // Хост использует DTLS (Secure UDP)
            transport.SetRelayServerData(new RelayServerData(allocation, connectionType));

            Debug.Log($"[NetworkUtility] Host started using UDP with connection type: {connectionType}");
            nm.StartHost();
            return;
        }

        // ─── КЛИЕНТ ───
        var trimmed = joinCode.Trim();
        var joinAlloc = await RelayService.Instance.JoinAllocationAsync(trimmed);
        Debug.Log($"[NetworkUtility] Relay CLIENT joining with code = {trimmed}");

        // Клиент: если это WebGL, то используем WebSockets, иначе UDP
        connectionType = isWebGL ? "wss" : "dtls";
        transport.UseWebSockets = isWebGL; // Устанавливаем свойство транспорта

        transport.SetRelayServerData(new RelayServerData(joinAlloc, connectionType));

        Debug.Log($"[NetworkUtility] Client started using {connectionType}");
        nm.StartClient();
    }


    /// <summary>
    /// Вызывается из UI-кнопки «Connect» в WebGL. Задаёт joinCode и переподключается.
    /// </summary>
    public void SetJoinCodeAndConnect(string code)
    {
        joinCode = code;
        var nm = NetworkManager.Singleton;
        if (nm.IsClient || nm.IsServer)
        {
            nm.Shutdown();
            Debug.Log("[NetworkUtility] Shutdown before reconnect");
        }
        _ = SetupRelayAndStartAsync();
    }

    /// <summary>
    /// Публичный геттер для UI — текущий join-код (показывать хосту).
    /// </summary>
    public string JoinCode => joinCode;
}