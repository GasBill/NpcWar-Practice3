using System;
using Godot;

namespace TowerDefense;

// Здоровье НПС: урон с учётом защиты, регенерация и смерть.
[GlobalClass]
public partial class HealthComponent : Node, INpcComponent
{
    // Делитель защиты в формуле урона: hp -= int(damage - def / 5).
    private const float DefenseDivisor = 5f;

    // Степень урона в векторе отталкивания.
    private const float KnockbackExponent = 0.5f;

    // Значение здоровья по умолчанию.
    private const float DefaultHealthValue = 300f;

    // Время регенерации по умолчанию.
    private const float DefaultRegenInterval = 1f;

    [Export] public float MaxHealth = DefaultHealthValue;
    [Export] public float Health = DefaultHealthValue;
    [Export] public float Defense;
    [Export] public float RegenAmount;
    [Export] public float RegenInterval = DefaultRegenInterval;
    [Export] public int Weight = INpcComponent.DefaultWeight;

    private Timer _regenTimer;
    private bool _isDead;
    private Npc _npc;

    // Сигнал получают остальные компоненты, например MoveComponent.
    [Signal] public delegate void DamagedEventHandler(Npc attacker);

    public override void _Ready()
    {
        _npc = GetParent<Npc>();
        Health = Mathf.Min(Health, MaxHealth);
        UpdateNpcHealthLabel();

        // Таймер регенерации нужен, только если регенерация вообще есть.
        if (RegenAmount <= 0f)
        {
            return;
        }

        // Циклический таймер регенерации создаётся при инициализации.
        _regenTimer = new Timer
        {
            WaitTime = RegenInterval,
            OneShot = false,
            Autostart = true,
        };
        _regenTimer.Timeout += Regenerate;
        AddChild(_regenTimer);
    }

    // Урон уменьшается защитой.
    // attackerId нужен для снарядов, у которых остался только Id отправителя.
    public void TakeDamage(float damage, Vector3 direction, Npc attacker = null, int attackerId = -1)
    {
        if (_isDead)
        {
            return;
        }

        Health -= (int)(damage - Defense / DefenseDivisor);
        UpdateNpcHealthLabel();
        ApplyHitImpulse(direction, damage);

        int sourceId = attacker != null ? attacker.Id : attackerId;
        Npc source = attacker;

        if ((source == null || !source.IsAlive) && sourceId >= 0 && _npc != null)
        {
            source = _npc.Manager?.GetNpcById(sourceId);
        }

        if (source != null && source.IsAlive && _npc != null && source.Squad != _npc.Squad)
        {
            // Получили урон — переключаемся на того, кто его нанёс.
            _npc.TargetId = source.Id;
            EmitSignal(SignalName.Damaged, source);
        }

        if (Health <= 0f)
        {
            Die();
        }
    }

    public int GetWeight() => Weight;

    public Action<float>[] GetStatSetters()
    {
        return new Action<float>[]
        {
            value => MaxHealth = value,
            value => Health = value,
            value => Defense = value,
            value => RegenAmount = value,
            value => RegenInterval = value,
        };
    }

    // Апгрейд увеличивает здоровье, защиту и регенерацию,
    // а время регенерации уменьшается — она становится чаще.
    public void ApplyUpgrades(int count)
    {
        float multiplier = Mathf.Pow(INpcComponent.UpgradeMultiplier, count);

        MaxHealth *= multiplier;
        Health = MaxHealth;
        Defense *= multiplier;
        RegenAmount *= multiplier;
        RegenInterval /= multiplier;
    }

    // Регенерация до максимального здоровья.
    private void Regenerate()
    {
        if (_isDead)
        {
            return;
        }

        Health = Mathf.Min(Health + RegenAmount, MaxHealth);
        UpdateNpcHealthLabel();
    }

    // Передаёт текущее здоровье НПС, чтобы обновить его Label3D.
    private void UpdateNpcHealthLabel()
    {
        if (_npc != null)
        {
            _npc.UpdateHealthLabel(Health);
        }
    }

    // Вектор отталкивания: direction / max(|x|,|y|,|z|) * damage^0.5.
    private void ApplyHitImpulse(Vector3 direction, float damage)
    {
        if (_npc == null)
        {
            return;
        }

        float maxComponent = Mathf.Max(
            Mathf.Abs(direction.X),
            Mathf.Max(Mathf.Abs(direction.Y), Mathf.Abs(direction.Z)));

        if (maxComponent <= 0f)
        {
            return;
        }

        Vector3 pushVector = direction / maxComponent * Mathf.Pow(damage, KnockbackExponent);
        _npc.ApplyHitVector(pushVector);
    }

    // Смерть: НПС просит менеджера удалить его из массива НПС.
    private void Die()
    {
        _isDead = true;
        Health = 0f;
        UpdateNpcHealthLabel();

        if (_npc == null)
        {
            return;
        }

        _npc.IsAlive = false;

        if (_npc.Manager != null)
        {
            _npc.Manager.RemoveNpc(_npc);
        }
        else
        {
            _npc.QueueFree();
        }
    }
}
