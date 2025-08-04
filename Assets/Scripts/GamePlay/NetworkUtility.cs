using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using System.Collections;

public class NetworkUtility : MonoBehaviour
{
    public static NetworkUtility Instance { get; private set; }

    [SerializeField] private float connectionTimeout = 2f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        StartCoroutine(InitializeNetwork());
    }

    private IEnumerator InitializeNetwork()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("NetworkManager не найден в сцене!");
            yield break;
        }

        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport == null)
        {
            Debug.LogError("UnityTransport не найден на NetworkManager!");
            yield break;
        }

        // Пытаемся подключиться как клиент
        NetworkManager.Singleton.StartClient();

        float timer = 0f;
        while (timer < connectionTimeout)
        {
            if (NetworkManager.Singleton.IsConnectedClient)
            {
                Debug.Log("Подключились как Client");
                yield break;
            }

            timer += Time.deltaTime;
            yield return null;
        }

        // Если не смогли подключиться → становимся Host
        NetworkManager.Singleton.Shutdown();
        yield return null;

        if (NetworkManager.Singleton.StartHost())
        {
            Debug.Log("Стартанули как Host (первый игрок)");
        }
        else
        {
            Debug.LogError("Не удалось запустить Host");
        }
    }

    public void StopSession()
    {
        if (NetworkManager.Singleton != null &&
            (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsClient))
        {
            NetworkManager.Singleton.Shutdown();
            Debug.Log("Сессия остановлена");
        }
    }

    public void ResetTransportPort()
    {
        if (NetworkManager.Singleton != null)
        {
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport != null)
            {
                transport.ConnectionData.Port = 0;
                Debug.Log("Порт сброшен (будет выбран автоматически при следующем старте)");
            }
        }
    }

    private void OnApplicationQuit()
    {
        StopSession();
        ResetTransportPort();
    }
}
