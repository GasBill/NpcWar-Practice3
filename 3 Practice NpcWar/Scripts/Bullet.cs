using Godot;

namespace TowerDefense;

// Пуля без физики: летит к выбранной цели и проверяет только её.
public partial class Bullet : Node3D
{
    private const float DefaultSpeed = 20f;
    private const float DefaultDamage = 10f;
    private const float DefaultHitRadius = 0.35f;
    private const int DefaultLifetimeFrames = 90;

    [Export] public float Speed = DefaultSpeed;
    [Export] public float Damage = DefaultDamage;
    [Export] public float HitRadius = DefaultHitRadius;

    // Время жизни пули в физических кадрах.
    [Export] public int LifetimeFrames = DefaultLifetimeFrames;

    private Npc _shooter;
    private Npc _target;
    private Vector3 _direction;
    private int _framesLeft;
    private bool _launched;

    // Вызывается ShootComponent после установки позиции.
    public void Launch(Npc shooter, Npc target, Vector3 direction)
    {
        _shooter = shooter;
        _target = target;
        _direction = direction.LengthSquared() > 0f ? direction.Normalized() : -GlobalTransform.Basis.Z;
        _framesLeft = LifetimeFrames;
        _launched = true;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_launched)
        {
            return;
        }

        GlobalPosition += _direction * Speed * (float)delta;
        _framesLeft--;

        if (_framesLeft <= 0)
        {
            QueueFree();
            return;
        }

        if (_target == null || !_target.IsAlive)
        {
            return;
        }

        float hitRadius = HitRadius + _target.HitBoxRadius;

        if (GlobalPosition.DistanceSquaredTo(_target.GlobalPosition) > hitRadius * hitRadius)
        {
            return;
        }

        HealthComponent health = _target.Health;

        if (health != null)
        {
            Npc attacker = _shooter != null && _shooter.IsAlive ? _shooter : null;
            health.TakeDamage(Damage, _direction, attacker);
        }

        QueueFree();
    }
}
