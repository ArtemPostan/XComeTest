// Assets/Scripts/Units/UnitIconProvider.cs
//
// ������ �� ������ �����. ����� ������ ������ � �������� ��� ��� UI ������.
// ������� ���� � ���������� �� ������ �������.

using UnityEngine;

public class UnitIconProvider : MonoBehaviour
{
    [Header("Draft UI")]
    [Tooltip("������������ ��� ����� � �������/������")]
    public string DisplayName = "Unit";

    [Tooltip("������ ��� UI ������ (Sprite)")]
    public Sprite Icon;
}
