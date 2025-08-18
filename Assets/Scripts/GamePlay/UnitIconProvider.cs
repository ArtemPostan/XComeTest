// Assets/Scripts/Units/UnitIconProvider.cs
//
// Вешаем на ПРЕФАБ юнита. Здесь храним иконку и красивое имя для UI драфта.
// Заполни поля в инспекторе на каждом префабе.

using UnityEngine;

public class UnitIconProvider : MonoBehaviour
{
    [Header("Draft UI")]
    [Tooltip("Отображаемое имя юнита в списках/драфте")]
    public string DisplayName = "Unit";

    [Tooltip("Иконка для UI драфта (Sprite)")]
    public Sprite Icon;
}
