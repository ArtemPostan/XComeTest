using System.Collections;
using UnityEngine;

public class CameraControl : MonoBehaviour
{
    [Header("��������� �����������")]
    [Tooltip("�������� ����� ��� �������� �� �������")]
    public TurnManager turnManager;

    [Header("��������� ��������")]
    [Tooltip("�������� �������� ������")]
    [SerializeField] private float moveSpeed = 10f;
    [Tooltip("������ �� ���� ������, ��� ������� �������� ��������� ������")]
    [SerializeField] private float edgeTolerance = 25f;
    [Tooltip("������ ������ �� ��� Z �� ����� ��� �������������")]
    [SerializeField] private float offsetZ = -5f; 

    [Header("��������� ���������������")]
    [Tooltip("���������������� ��������������� (�������� ����)")]
    [SerializeField] private float zoomSpeed = 5f;
    [Tooltip("����������� �������� ��������")]
    [SerializeField] private float minZoom = 5f;
    [Tooltip("������������ �������� ��������")]
    [SerializeField] private float maxZoom = 25f;

    [Header("����������� ����")]
    [Tooltip("������, �� �������� �������� ����� ���������� �������� ������")]
    public GameObject boundaryObject;

    private Camera mainCamera;
    private Bounds gameBounds;

    void Start()
    {
        mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogError("Main Camera �� ������� � �����. ���������, ��� � ������ ���� ��� 'MainCamera'.");
            return;
        }

        // --- NEW: Subscribe to the turn manager event ---
        if (turnManager != null)
        {
            turnManager.OnPlayerUnitTurnStarted.AddListener(CenterOnUnit);
        }
        else
        {
            Debug.LogWarning("TurnManager is not set. Camera will not automatically center on units.");
        }

        if (boundaryObject != null)
        {
            Renderer renderer = boundaryObject.GetComponent<Renderer>();
            if (renderer != null)
            {
                gameBounds = renderer.bounds;
                Debug.Log($"Camera bounds set to: {gameBounds}");
            }
            else
            {
                Debug.LogError("Boundary object does not have a Renderer component. Camera movement will not be restricted.");
            }
        }
        else
        {
            Debug.LogWarning("Boundary object is not set. Camera movement will not be restricted.");
        }
    }

    void Update()
    {
        if (mainCamera == null) return;

        HandleKeyboardInput();
        HandleMouseMovement();
        HandleMouseScroll();

        // --- NEW: Apply boundaries based on game object ---
        if (boundaryObject != null)
        {
            ApplyBoundaries();
        }
    }

    /// <summary>
    /// Adjusts the camera's position to keep it within the game bounds.
    /// </summary>
    private void ApplyBoundaries()
    {
        Vector3 currentPosition = transform.position;

        // The camera's viewport is half of its orthographic size on each side
        float camHeight = mainCamera.orthographicSize;
        float camWidth = mainCamera.orthographicSize * mainCamera.aspect;

        // Calculate the camera's new clamped position
        float clampedX = Mathf.Clamp(currentPosition.x, gameBounds.min.x + camWidth, gameBounds.max.x - camWidth);
        float clampedZ = Mathf.Clamp(currentPosition.z, gameBounds.min.z + camHeight, gameBounds.max.z - camHeight);

        // Check if the game world is smaller than the camera view, and center the camera if so.
        if (gameBounds.size.x < camWidth * 2)
        {
            clampedX = gameBounds.center.x;
        }
        if (gameBounds.size.z < camHeight * 2)
        {
            clampedZ = gameBounds.center.z;
        }

        transform.position = new Vector3(clampedX, currentPosition.y, clampedZ);
    }

    /// <summary>
    /// ��������� �������� � ������� ������-�������.
    /// </summary>
    private void HandleKeyboardInput()
    {
        Vector3 moveDirection = Vector3.zero;

        if (Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W))
        {
            moveDirection += Vector3.forward;
        }
        if (Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S))
        {
            moveDirection += Vector3.back;
        }
        if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A))
        {
            moveDirection += Vector3.left;
        }
        if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D))
        {
            moveDirection += Vector3.right;
        }

        // ����������� ������, ����� ������������ �������� �� ���� �������
        moveDirection.Normalize();

        // ���������� ������
        transform.Translate(moveDirection * moveSpeed * Time.deltaTime, Space.World);
    }

    /// <summary>
    /// ��������� �������� � ������� ������� ���� �� ����� ������.
    /// </summary>
    private void HandleMouseMovement()
    {
        Vector3 moveDirection = Vector3.zero;
        Vector2 mousePosition = Input.mousePosition;

        // ���������, ��������� �� ������ � �������� ������
        // Unity ������������� ������������ ������� �������, �� ����� �������� ������ ��� ����� �������
        bool cursorIsInsideScreen = (mousePosition.x >= 0 && mousePosition.x <= Screen.width &&
                                     mousePosition.y >= 0 && mousePosition.y <= Screen.height);

        if (!cursorIsInsideScreen)
        {
            // ���� ������ ����� �� ������� ������, �� ������� ������
            return;
        }

        // �������� �� �����������
        if (mousePosition.x < edgeTolerance)
        {
            moveDirection += Vector3.left;
        }
        else if (mousePosition.x > Screen.width - edgeTolerance)
        {
            moveDirection += Vector3.right;
        }

        // �������� �� ���������
        if (mousePosition.y < edgeTolerance)
        {
            moveDirection += Vector3.back;
        }
        else if (mousePosition.y > Screen.height - edgeTolerance)
        {
            moveDirection += Vector3.forward;
        }

        // ����������� � ����������
        if (moveDirection != Vector3.zero)
        {
            moveDirection.Normalize();
            transform.Translate(moveDirection * moveSpeed * Time.deltaTime, Space.World);
        }
    }

    /// <summary>
    /// ��������� ��������������� � ������� �������� ����.
    /// </summary>
    private void HandleMouseScroll()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");

        // ���������, ��� ���� ��������� � ������ �������
        if (scroll != 0 && mainCamera != null)
        {
            // �������� ������� ������� ������
            Vector3 newPosition = transform.position;

            // ������ ������ ������ (Y-����������) �� ������ ���������
            // scroll > 0 - ��������� ������ (�����������)
            // scroll < 0 - ��������� ����� (���������)
            // �� �����, ����� ��� scroll > 0 Y-���������� �����������, ������� ���������� `-scroll`
            newPosition.y -= scroll * zoomSpeed;

            // ������������ ������ ������
            // ���������� Mathf.Clamp ��� �������������� ������ �� �������
            newPosition.y = Mathf.Clamp(newPosition.y, minZoom, maxZoom);

            // ��������� ����� �������
            transform.position = newPosition;
        }
    }
    public void CenterOnUnit(Transform unitTransform)
    {
        Vector3 targetPosition = unitTransform.position;
        // ��������� ������� Y-���������� ������, ����� ��� �� �������� ��� �������������
        Vector3 newPosition = new Vector3(targetPosition.x, transform.position.y, targetPosition.z + offsetZ);

        // ������� ����������� ������
        // ���������� Vector3.Lerp ��� �������� ������� ���������
        StartCoroutine(MoveCameraSmoothly(newPosition, 1.0f)); // 1.0f - ��� ��������, ����� ������� � ���������
    }

    private IEnumerator MoveCameraSmoothly(Vector3 targetPosition, float duration)
    {
        float elapsedTime = 0;
        Vector3 startingPos = transform.position;

        while (elapsedTime < duration)
        {
            transform.position = Vector3.Lerp(startingPos, targetPosition, (elapsedTime / duration));
            elapsedTime += Time.deltaTime;
            yield return null;
        }
        transform.position = targetPosition; // ��������, ��� ������ ����� ����� �� ����
    }
}