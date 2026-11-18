using System.Collections.Generic;
using Godot;

namespace TowerDefense;

// Тип хитбоксов, собранный EntityManager из описания объекта.
// Хранит базовые размеры, настройки оптимизации и обработчик поведения.
public sealed class HitboxType
{
    public string Name;
    public EntityKind Kind;
    public Vector3 BaseCenter;
    public Vector3 BaseSize;
    public bool OptimizePosition;
    public bool OptimizeChecks;
    public int CheckIntervalFrames;
    public string[] CollisionTypeNames = System.Array.Empty<string>();
    public EntityBehavior Behavior;
    public readonly List<HitboxObject> Objects = new();
}
