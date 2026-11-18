using Godot;

namespace TowerDefense;

// Базовый контейнер данных объекта. Наследник описывает поля конкретного типа:
// массивы — если важен номер элемента, списки — если элементы часто добавляются.
public abstract partial class EntityData : RefCounted
{
    public Entity Entity;
}
