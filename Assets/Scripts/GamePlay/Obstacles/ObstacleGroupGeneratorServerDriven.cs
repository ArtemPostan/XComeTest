// Assets/Scripts/World/ObstacleGroupGeneratorServerDriven.cs
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class ObstacleGroupGeneratorServerDriven : NetworkBehaviour
{
    [Header("Source Prefabs (statics, no NetworkObject)")]
    [Tooltip("Набор статических префабов (камни, деревья, колонны и т.п.). БЕЗ NetworkObject!")]
    [SerializeField] private List<GameObject> elementPrefabs = new List<GameObject>();

    [Header("Local Visual Tweaks")]
    [SerializeField] private float yOffset = 0f;

    // Параметры группы задаёт сервер ДО Spawn()
    public NetworkVariable<int> ElementsCount = new NetworkVariable<int>(
        5, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<float> Radius = new NetworkVariable<float>(
        2.5f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Точная раскладка, которую рассылает сервер
    private NetworkList<ElementLayout> _layout;

    // Локально созданные дети (не сетевые)
    private readonly List<GameObject> _spawnedChildren = new();

    private void Awake()
    {
        _layout = new NetworkList<ElementLayout>(
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server
        );
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _layout.OnListChanged += OnLayoutChanged;

        if (IsServer)
        {
            // Сервер один раз наполняет список
            if (_layout.Count == 0)
                ServerBuildLayout();
            // На сервере тоже собираем визуал (он такой же, как у клиентов)
            RebuildFromLayout();
        }
        else
        {
            // Клиент: если снапшот уже пришёл — соберём сразу
            if (_layout.Count > 0)
                RebuildFromLayout();
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        if (_layout != null) _layout.OnListChanged -= OnLayoutChanged;

        // подчистим локальные объекты
        ClearChildren();
    }

    private void OnLayoutChanged(NetworkListEvent<ElementLayout> _)
    {
        // ВАЖНО: НЕ добавляем по одному! Каждый раз пересобираем целиком по текущему списку.
        RebuildFromLayout();
    }

    private void RebuildFromLayout()
    {
        ClearChildren();

        if (_layout == null || _layout.Count == 0 || elementPrefabs.Count == 0)
            return;

        foreach (var el in _layout)
        {
            int idx = Mathf.Clamp(el.prefabIndex, 0, elementPrefabs.Count - 1);
            var prefab = elementPrefabs[idx];
            if (prefab == null) continue;

            var go = Instantiate(prefab, transform);
            go.transform.localPosition = el.localPos + new Vector3(0f, yOffset, 0f);
            go.transform.localRotation = Quaternion.Euler(0f, el.rotY, 0f);
            go.transform.localScale = el.scale;

            // Гарантируем чисто локальные статики
            var no = go.GetComponent<NetworkObject>(); if (no) Destroy(no);
            var rb = go.GetComponent<Rigidbody>(); if (rb) Destroy(rb);

            _spawnedChildren.Add(go);
        }
    }

    private void ClearChildren()
    {
        for (int i = 0; i < _spawnedChildren.Count; i++)
            if (_spawnedChildren[i] != null) Destroy(_spawnedChildren[i]);
        _spawnedChildren.Clear();
    }

    /// <summary>Сервер генерирует точную раскладку и пишет её в NetworkList.</summary>
    private void ServerBuildLayout()
    {
        if (!IsServer || elementPrefabs == null || elementPrefabs.Count == 0) return;

        int count = Mathf.Max(1, ElementsCount.Value);
        float r = Mathf.Max(0.1f, Radius.Value);

        _layout.Clear();

        for (int i = 0; i < count; i++)
        {
            int prefabIdx = UnityEngine.Random.Range(0, elementPrefabs.Count);

            // случайная точка в круге радиуса r (sqrt — равномернее)
            float ang = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            float rad = r * Mathf.Sqrt(UnityEngine.Random.Range(0f, 1f));
            Vector3 localPos = new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);

            float rotY = UnityEngine.Random.Range(0f, 360f);

            float uni = UnityEngine.Random.Range(0.85f, 1.2f);
            Vector3 scl = new Vector3(uni, UnityEngine.Random.Range(0.9f, 1.25f), uni);

            _layout.Add(new ElementLayout(prefabIdx, localPos, rotY, scl));
        }
    }

    [Serializable]
    public struct ElementLayout : INetworkSerializable, IEquatable<ElementLayout>
    {
        public int prefabIndex;
        public Vector3 localPos;
        public float rotY;
        public Vector3 scale;

        public ElementLayout(int idx, Vector3 pos, float y, Vector3 scl)
        {
            prefabIndex = idx;
            localPos = pos;
            rotY = y;
            scale = scl;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref prefabIndex);
            serializer.SerializeValue(ref localPos);
            serializer.SerializeValue(ref rotY);
            serializer.SerializeValue(ref scale);
        }

        public bool Equals(ElementLayout other)
        {
            return prefabIndex == other.prefabIndex
                   && localPos.Equals(other.localPos)
                   && Mathf.Approximately(rotY, other.rotY)
                   && scale.Equals(other.scale);
        }

        public override bool Equals(object obj) => obj is ElementLayout other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + prefabIndex;
                hash = hash * 31 + localPos.GetHashCode();
                hash = hash * 31 + rotY.GetHashCode();
                hash = hash * 31 + scale.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(ElementLayout a, ElementLayout b) => a.Equals(b);
        public static bool operator !=(ElementLayout a, ElementLayout b) => !a.Equals(b);
    }
}
