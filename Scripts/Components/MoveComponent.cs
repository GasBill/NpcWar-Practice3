using System;
using Godot;

namespace TowerDefense;

// Движение НПС к общей цели, которую выбирает Npc.
[GlobalClass]
public partial class MoveComponent : Node, INpcComponent
{
    private const float DefaultSpeed = 5f;

    [Export] public float Speed = DefaultSpeed;
    [Export] public int Weight = INpcComponent.DefaultWeight;

    private Npc _npc;

    public override void _Ready()
    {
        _npc = GetParent<Npc>();
        _npc.AcquireTarget();
    }

    public override unsafe void _PhysicsProcess(double delta)
    {
        if (_npc == null || _npc.Manager == null)
        {
            return;
        }

        // В списке лежит адрес памяти, а не копия позиции.
        if (!_npc.Manager.TryGetNpcPositionLink(_npc.TargetId, _npc.Squad, out IntPtr targetAddress))
        {
            if (_npc.TargetId >= 0)
            {
                _npc.AcquireTarget();
            }

            if (!_npc.Manager.TryGetNpcPositionLink(_npc.TargetId, _npc.Squad, out targetAddress))
            {
                StopHorizontal();
                return;
            }
        }

        Npc targetNpc = _npc.Manager.GetNpcById(_npc.TargetId);

        if (targetNpc == null || !targetNpc.IsAlive || targetNpc.Squad == _npc.Squad)
        {
            _npc.AcquireTarget();

            if (!_npc.Manager.TryGetNpcPositionLink(_npc.TargetId, _npc.Squad, out targetAddress))
            {
                StopHorizontal();
                return;
            }

            targetNpc = _npc.Manager.GetNpcById(_npc.TargetId);

            if (targetNpc == null || !targetNpc.IsAlive || targetNpc.Squad == _npc.Squad)
            {
                StopHorizontal();
                return;
            }
        }

        // Хитбоксы уже пересеклись — НПС больше не идёт в сторону цели.
        HitboxManager hitboxManager = _npc.Manager.HitboxSystem;

        if (hitboxManager != null && hitboxManager.Intersects(_npc, targetNpc))
        {
            StopHorizontal();
            return;
        }

        // Разыменовываем адрес: читаем именно переменную по ссылке.
        Vector3* target = (Vector3*)targetAddress;

        // Двигаемся только по горизонтали и через физику.
        Vector3 direction = *target - _npc.GlobalPosition;
        direction.Y = 0f;

        Vector3 horizontalVelocity = direction.Normalized() * Speed;
        Vector3 velocity = _npc.Velocity;
        velocity.X = horizontalVelocity.X;
        velocity.Z = horizontalVelocity.Z;
        _npc.Velocity = velocity;
        _npc.MoveAndSlide();

        if (_npc.Position != null)
        {
            *_npc.Position = _npc.GlobalPosition;
        }
    }

    // Гасит горизонтальное движение, сохраняя вертикальную скорость гравитации.
    private unsafe void StopHorizontal()
    {
        Vector3 velocity = _npc.Velocity;
        velocity.X = 0f;
        velocity.Z = 0f;
        _npc.Velocity = velocity;
        _npc.MoveAndSlide();

        if (_npc.Position != null)
        {
            *_npc.Position = _npc.GlobalPosition;
        }
    }

    public int GetWeight() => Weight;

    public Action<float>[] GetStatSetters()
    {
        return new Action<float>[]
        {
            value => Speed = value,
        };
    }

    // Апгрейд увеличивает скорость передвижения.
    public void ApplyUpgrades(int count)
    {
        Speed *= Mathf.Pow(INpcComponent.UpgradeMultiplier, count);
    }
}
