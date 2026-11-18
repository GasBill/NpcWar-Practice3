using Godot;

namespace TowerDefense;

// Интерфейсы (бывшие компоненты), которые покупаются за очки.
public enum NpcInterface
{
    Health = 0,
    Move = 1,
    Shoot = 2,
    Melee = 3,
}

// Данные одного НПС: здоровье, движение и атака как свойства,
// которые используют скрипты поведения и запросов.
public sealed partial class NpcData : EntityData
{
    public int Slot;
    public int Squad;
    public int Cost;
    public bool IsAlive = true;
    public Color Color;

    public float MaxHealth;
    public float Health;
    public float Defense;
    public float RegenAmount;
    public float RegenInterval;
    public float RegenTimer;

    public float Speed;

    public float Damage;
    public float AttackInterval;
    public float AttackTimer;
    public float ShootRadius;
    public bool CanShoot;
    public bool CanMelee;

    public int TargetSlot = -1;

    // Купленные интерфейсы и их апгрейды: важен номер элемента.
    public NpcInterface[] Interfaces = System.Array.Empty<NpcInterface>();
    public int[] Upgrades = System.Array.Empty<int>();
}
