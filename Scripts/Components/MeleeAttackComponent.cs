using System;
using Godot;

namespace TowerDefense;

// Ближний бой: проверяет пересечение хитбоксов через HitboxManager
// и бьёт общую цель НПС.
[GlobalClass]
public partial class MeleeAttackComponent : Node, INpcComponent, INpcAttack
{
    private const float DefaultAttackInterval = 1f;
    private const float DefaultDamage = 10f;

    [Export] public float AttackInterval = DefaultAttackInterval;
    [Export] public float Damage = DefaultDamage;
    [Export] public int Weight = INpcComponent.DefaultWeight;

    private Npc _npc;
    private float _timeUntilAttack;

    public override void _Ready()
    {
        _npc = GetParent<Npc>();
        _timeUntilAttack = AttackInterval;
        _npc.AcquireTarget();
    }

    // Вызывается общим таймером команды.
    public void TickAttack(float delta)
    {
        _timeUntilAttack -= delta;

        if (_timeUntilAttack > 0f)
        {
            return;
        }

        _timeUntilAttack = AttackInterval;
        Attack();
    }

    public int GetWeight() => Weight;

    public Action<float>[] GetStatSetters()
    {
        return new Action<float>[]
        {
            value => AttackInterval = value,
            value => Damage = value,
        };
    }

    // Апгрейд увеличивает урон, а интервал уменьшается — атака становится чаще.
    public void ApplyUpgrades(int count)
    {
        float multiplier = Mathf.Pow(INpcComponent.UpgradeMultiplier, count);

        Damage *= multiplier;
        AttackInterval /= multiplier;
    }

    // Компонент только поддерживает актуальную цель.
    // Сам удар выполняет NpcDamageProcessor через damage-хитбокс.
    private void Attack()
    {
        _npc.AcquireTarget();
    }

    // Вызывается damage-хитбоксом раз в его интервал проверки.
    public void DealDamage(Npc target)
    {
        if (_npc == null || target == null || !target.IsAlive || target.Squad == _npc.Squad)
        {
            return;
        }

        HealthComponent health = target.Health;

        if (health == null)
        {
            return;
        }

        Vector3 direction = target.GlobalPosition - _npc.GlobalPosition;

        if (direction.LengthSquared() <= 0f)
        {
            direction = -_npc.GlobalTransform.Basis.Z;
        }

        health.TakeDamage(Damage, direction.Normalized(), _npc);
    }
}
