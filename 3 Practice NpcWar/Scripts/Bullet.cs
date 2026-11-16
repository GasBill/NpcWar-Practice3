using Godot;

namespace TowerDefense;

// Пуля без физики: летит по заданному направлению и живёт ограниченное число
// физических кадров. Попадание проверяет не она, а хитбокс NPC.
public partial class Bullet : Node3D, IHitboxSpeedProvider
{
    public const string HitboxGroupName = "Bullet";

    private const float DefaultSpeed = 20f;
    private const float DefaultDamage = 10f;
    private const int DefaultLifetimeFrames = 90;

    [Export] public float Speed = DefaultSpeed;
    [Export] public float Damage = DefaultDamage;

    // Время жизни пули в физических кадрах.
    [Export] public int LifetimeFrames = DefaultLifetimeFrames;

    private const float VerticalDirectionThreshold = 0.99f;

    public Npc Shooter { get; private set; }
    public int ShooterId { get; private set; } = -1;
    public Vector3 Direction { get; private set; }
    public bool HasHit { get; private set; }
    public float HitboxSpeed => Speed;
    public bool IsAlive => !HasHit && GodotObject.IsInstanceValid(this) && IsInsideTree();

    private int _framesLeft;
    private bool _launched;

    public override void _EnterTree()
    {
        AddToGroup(HitboxGroupName);
    }

    // Вызывается ShootComponent после установки позиции.
    public void Launch(Npc shooter, Vector3 direction)
    {
        Shooter = shooter;
        ShooterId = shooter != null ? shooter.Id : -1;
        Direction = direction.LengthSquared() > 0f ? direction.Normalized() : -GlobalTransform.Basis.Z;

        // Разворачиваем пулю так, чтобы её -Z смотрел по направлению движения.
        Vector3 up = Mathf.Abs(Direction.Dot(Vector3.Up)) > VerticalDirectionThreshold
            ? Vector3.Right
            : Vector3.Up;
        Basis orientation = Basis.LookingAt(Direction, up);
        GlobalTransform = new Transform3D(orientation, GlobalPosition);

        _framesLeft = LifetimeFrames;
        _launched = true;
    }

    // Останавливает пулю после попадания; дальнейшие проверки её игнорируют.
    public void Consume()
    {
        if (HasHit)
        {
            return;
        }

        HasHit = true;
        QueueFree();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_launched || HasHit)
        {
            return;
        }

        GlobalPosition += Direction * Speed * (float)delta;
        _framesLeft--;

        if (_framesLeft <= 0)
        {
            QueueFree();
        }
    }
}
