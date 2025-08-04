using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

public class HostOrClient : MonoBehaviour
{
    private void Awake()
    {
        // Если ещё не подключены
        if (!NetworkManager.Singleton.IsServer && !NetworkManager.Singleton.IsClient)
        {
            if (NetworkManager.Singleton.StartHost())
            {
                Debug.Log("Первый игрок → Host (авто)");
            }
            else
            {
                Debug.LogError("Не удалось стартовать Host");
            }
        }
        else
        {
            if (NetworkManager.Singleton.StartClient())
            {
                Debug.Log("Следующий игрок → Client (авто)");
            }
            else
            {
                Debug.LogError("Не удалось подключить Client");
            }
        }
    }   
}
