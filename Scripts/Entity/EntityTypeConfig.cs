using Godot;

namespace TowerDefense;

// Тип объекта в EntityManager: логическая сущность, снаряд или статичный объект сцены.
public enum EntityKind
{
    Object = 0,
    Projectile = 1,
    Static = 2,
}

// Описание типа объекта: имя, визуал, скрипты и параметры хитбокса.
// Группы больше не используются — тип ищется по Name.
[GlobalClass]
public partial class EntityTypeConfig : Resource
{
    [Export] public EntityKind Kind = EntityKind.Object;
    [Export] public string Name = string.Empty;

    // Готовые объекты сцены (стены, пол): для них считаются размеры и поворот.
    [Export] public Godot.Collections.Array<string> SceneObjectPaths = new();

    [Export] public PackedScene VisualScene;

    // Скрипт, который каждый кадр считает движение и шлёт запросы.
    [Export] public CSharpScript QueryScript;

    // Скрипт с условиями обработки пересечений и урона.
    [Export] public CSharpScript BehaviorScript;

    // Класс с данными конкретного объекта (только для Object).
    [Export] public CSharpScript DataScript;

    // Только для Projectile: время жизни в физических кадрах.
    [Export] public int LifetimeFrames;

    // Для служебных hitbox-типов (например NpcDamage) визуал не создаётся.
    [Export] public bool SpawnVisual = true;

    [Export] public bool OptimizePosition = true;
    [Export] public bool OptimizeChecks = true;
    [Export] public int CheckIntervalFrames;

    // Типы, с объектами которых нужно проверять пересечения.
    [Export] public Godot.Collections.Array<string> CollisionTypeNames = new();

    // Дополнительные хитбоксы, создаваемые вместе с основным.
    [Export] public Godot.Collections.Array<string> AdditionalHitboxTypeNames = new();
}
