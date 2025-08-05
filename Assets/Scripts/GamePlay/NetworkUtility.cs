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

    private string joinCode;
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

    private async Task InitializeUnityServices()
    {
        if (!_initialized)
        {
            await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            _initialized = true;
        }
    }

    /// <summary>
    /// Вызывается из UI-скрипта для запуска игры в режиме хоста.
    /// </summary>
    public async void StartHost()
    {
        await InitializeUnityServices();

        var nm = NetworkManager.Singleton;
        var transport = nm.GetComponent<UnityTransport>();

        if (nm.IsClient || nm.IsServer)
        {
            nm.Shutdown();
            await Task.Delay(100); // Небольшая задержка для завершения Shutdown
        }

        var allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
        joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
        Debug.Log($"[NetworkUtility] Relay HOST. JoinCode = {joinCode}");

        string connectionType = "dtls";
        transport.UseWebSockets = false;
        transport.SetRelayServerData(new RelayServerData(allocation, connectionType));

        Debug.Log($"[NetworkUtility] Host started using {connectionType}");
        nm.StartHost();
    }

    /// <summary>
    /// Вызывается из UI-скрипта для запуска игры в режиме клиента.
    /// </summary>
    public async void StartClient(string code)
    {
        await InitializeUnityServices();

        var nm = NetworkManager.Singleton;
        var transport = nm.GetComponent<UnityTransport>();

        if (nm.IsClient || nm.IsServer)
        {
            nm.Shutdown();
            await Task.Delay(100);
        }

        joinCode = code.Trim();

        var joinAlloc = await RelayService.Instance.JoinAllocationAsync(joinCode);
        Debug.Log($"[NetworkUtility] Relay CLIENT joining with code = {joinCode}");

        string connectionType = "wss";
        transport.UseWebSockets = true;

        transport.SetRelayServerData(new RelayServerData(joinAlloc, connectionType));

        Debug.Log($"[NetworkUtility] Client started using {connectionType}");
        nm.StartClient();
    }

    /// <summary>
    /// Публичный геттер для UI — текущий join-код (показывать хосту).
    /// </summary>
    public string JoinCode => joinCode;
}