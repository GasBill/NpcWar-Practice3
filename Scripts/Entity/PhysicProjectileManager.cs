using System.Collections.Generic;
using Godot;

namespace TowerDefense;

// Состояние одного снаряда: данные хранятся в менеджере, а не в Entity.
public sealed class Projectile
{
    public int Id;
    public ProjectileType Type;
    public Vector3 Position;
    public Vector3 Rotation;
    public Vector3 Direction;
    public float Speed;
    public float Damage;
    public int FramesLeft;
    public int ShooterSquad;
    public int ShooterSlot;
    public bool IsAlive = true;
}

public sealed class ProjectileType
{
    public EntityTypeConfig Config;
    public ProjectileBehavior Behavior;
    public PackedScene VisualScene;
}

// Подинструмент PhysicManager: описывает поведение снаряда до создания
// визуала, двигает снаряды, считает время жизни и удаляет их.
public partial class PhysicProjectileManager : Node
{
    private const float VerticalDirectionThreshold = 0.99f;

    private readonly Dictionary<string, ProjectileType> _types = new();
    private readonly List<Projectile> _projectiles = new();
    private readonly Dictionary<int, Projectile> _byId = new();
    private EntityManager _entityManager;

    public void Initialize(EntityManager entityManager)
    {
        _entityManager = entityManager;
    }

    public void Register(EntityTypeConfig config, ProjectileBehavior behavior)
    {
        _types[config.Name] = new ProjectileType
        {
            Config = config,
            Behavior = behavior,
            VisualScene = config.VisualScene,
        };
    }

    public Projectile Spawn(string typeName, Vector3 position, Vector3 direction, int shooterSquad, int shooterSlot)
    {
        if (_entityManager == null || !_types.TryGetValue(typeName, out ProjectileType type))
        {
            return null;
        }

        Vector3 normalized = direction.LengthSquared() > 0f ? direction.Normalized() : Vector3.Forward;
        Vector3 up = Mathf.Abs(normalized.Dot(Vector3.Up)) > VerticalDirectionThreshold
            ? Vector3.Right
            : Vector3.Up;
        Vector3 rotation = Basis.LookingAt(normalized, up).GetEuler();

        int id = _entityManager.NextOwnerId();
        Node3D visual = _entityManager.Physics.Spawn(id, type.VisualScene, position, rotation);
        HitboxObject hitbox = _entityManager.Hitboxes.CreateHitbox(id, type.Config.Name, visual, true);

        if (hitbox == null)
        {
            _entityManager.Physics.Destroy(id);
            return null;
        }

        Projectile projectile = new()
        {
            Id = id,
            Type = type,
            Position = position,
            Rotation = rotation,
            Direction = normalized,
            Speed = type.Behavior.Speed,
            Damage = type.Behavior.Damage,
            FramesLeft = Mathf.Max(type.Config.LifetimeFrames, 1),
            ShooterSquad = shooterSquad,
            ShooterSlot = shooterSlot,
        };

        _entityManager.Queries.RegisterOwner(id, new[] { hitbox }, visual);
        _entityManager.Queries.SyncFromSource(id);

        _projectiles.Add(projectile);
        _byId[id] = projectile;
        return projectile;
    }

    public Projectile Get(int id)
    {
        return _byId.TryGetValue(id, out Projectile projectile) ? projectile : null;
    }

    public void Update(float delta)
    {
        if (_entityManager == null)
        {
            return;
        }

        for (int i = _projectiles.Count - 1; i >= 0; i--)
        {
            Projectile projectile = _projectiles[i];

            if (!projectile.IsAlive)
            {
                _projectiles.RemoveAt(i);
                continue;
            }

            projectile.Position += projectile.Direction * projectile.Speed * delta;

            Node3D visual = _entityManager.Physics.GetVisual(projectile.Id);

            if (visual != null)
            {
                visual.GlobalPosition = projectile.Position;
                visual.GlobalRotation = projectile.Rotation;
            }

            _entityManager.Queries.SyncFromSource(projectile.Id);
            projectile.FramesLeft--;

            if (projectile.FramesLeft <= 0)
            {
                Destroy(projectile.Id);
            }
        }
    }

    public void Destroy(int id)
    {
        if (!_byId.TryGetValue(id, out Projectile projectile) || !projectile.IsAlive)
        {
            return;
        }

        projectile.IsAlive = false;
        _byId.Remove(id);
        _projectiles.Remove(projectile);
        _entityManager.Queries.UnregisterOwner(id);
        _entityManager.Hitboxes.DestroyOwner(id);
        _entityManager.Physics.Destroy(id);
    }
}
