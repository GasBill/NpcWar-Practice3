using System.Collections.Generic;
using Godot;

namespace TowerDefense;

// Логический объект EntityManager. Это обычный Node: он не двигается сам,
// а хранит данные и получает запросы от своего query-скрипта.
public sealed partial class Entity : Node
{
    public int Id;
    public EntityTypeConfig Config;
    public EntityData Data;
    public readonly List<HitboxObject> Hitboxes = new();
    public bool IsAlive = true;
}
