using System;
using System.Collections.Generic;
using Godot;

namespace TowerDefense;

// Runtime-описание типа после разбора EntityTypeConfig.
public sealed class EntityType
{
    public EntityTypeConfig Config;
    public string Name;
    public EntityKind Kind;
    public Vector3 BaseCenter;
    public Vector3 BaseSize;
    public float BottomOffset;
    public bool HasBounds;
    public EntityBehavior Behavior;
    public EntityQuery Query;
    public readonly List<Entity> Entities = new();
}

// Над-система: создаёт логические объекты по описаниям типов и связывает
// менеджер запросов, хитбоксы, визуал и снаряды.
public partial class EntityManager : Node3D
{
    // Все типы объектов проекта. Описание — EntityTypeConfig.
    [Export] public Godot.Collections.Array<Resource> Types = new();

    public HitboxManager Hitboxes { get; private set; }
    public QueryManager Queries { get; private set; }
    public PhysicManager Physics { get; private set; }
    public PhysicProjectileManager Projectiles => Physics?.Projectiles;
    public NpcManager Npcs { get; private set; }

    private readonly Dictionary<string, EntityType> _types = new();
    private readonly Dictionary<int, Entity> _entities = new();
    private int _nextOwnerId = 1;

    public override void _Ready()
    {
        Hitboxes = GetNodeOrNull<HitboxManager>("HitboxManager");
        Physics = GetNodeOrNull<PhysicManager>("PhysicManager");
        Queries = GetNodeOrNull<QueryManager>("QueryManager");
        Npcs = GetParent()?.GetNodeOrNull<NpcManager>("NpcManager");

        if (Hitboxes == null || Physics == null || Queries == null || Physics.Projectiles == null)
        {
            GD.PushError("EntityManager: не хватает HitboxManager, PhysicManager или QueryManager.");
            return;
        }

        Queries.Physics = Physics;
        Physics.Queries = Queries;
        Physics.Projectiles.Initialize(this);
        BuildTypes();
        BuildStaticObjects();
    }

    public override void _PhysicsProcess(double delta)
    {
        float frameDelta = (float)delta;
        Queries.SetDelta(frameDelta);
        Projectiles.Update(frameDelta);

        foreach (EntityType type in _types.Values)
        {
            type.Query?.Update(frameDelta);
        }
    }

    public int NextOwnerId() => _nextOwnerId++;

    public Entity GetEntity(int id)
    {
        return _entities.TryGetValue(id, out Entity entity) ? entity : null;
    }

    public EntityType GetType(string name)
    {
        return _types.TryGetValue(name, out EntityType type) ? type : null;
    }

    public IReadOnlyList<Entity> GetEntities(string typeName)
    {
        EntityType type = GetType(typeName);

        if (type == null)
        {
            return Array.Empty<Entity>();
        }

        return type.Entities;
    }

    // Смещение низа визуала относительно его корня: нужно для спавна на поверхности.
    public float GetTypeBottomOffset(string typeName)
    {
        EntityType type = GetType(typeName);
        return type is { HasBounds: true } ? type.BottomOffset : 0f;
    }

    public Entity Spawn(string typeName, Vector3 position, Vector3 rotation, Action<EntityData> configure = null)
    {
        if (!_types.TryGetValue(typeName, out EntityType type) || type.Kind != EntityKind.Object)
        {
            return null;
        }

        if (type.Config.DataScript == null)
        {
            GD.PushError($"EntityManager: у типа {typeName} нет DataScript.");
            return null;
        }

        int id = NextOwnerId();
        Entity entity = new()
        {
            Name = $"{typeName}_{id}",
            Id = id,
            Config = type.Config,
        };
        AddChild(entity);

        entity.Data = type.Config.DataScript.New(Array.Empty<Variant>()).As<EntityData>();

        if (entity.Data == null)
        {
            GD.PushError($"EntityManager: не удалось создать данные типа {typeName}.");
            entity.QueueFree();
            return null;
        }

        entity.Data.Entity = entity;
        configure?.Invoke(entity.Data);

        Node3D visual = null;

        if (type.Config.SpawnVisual && type.Config.VisualScene != null)
        {
            visual = Physics.Spawn(id, type.Config.VisualScene, position, rotation);
        }

        HitboxObject mainHitbox = Hitboxes.CreateHitbox(id, typeName, visual, true);

        if (mainHitbox != null)
        {
            entity.Hitboxes.Add(mainHitbox);
        }

        foreach (string additionalName in type.Config.AdditionalHitboxTypeNames)
        {
            HitboxObject additional = Hitboxes.CreateHitbox(id, additionalName, visual);

            if (additional != null)
            {
                entity.Hitboxes.Add(additional);
            }
        }

        Queries.RegisterOwner(id, entity.Hitboxes, visual);
        Queries.SyncFromSource(id);

        type.Entities.Add(entity);
        _entities[id] = entity;
        type.Behavior?.OnSpawn(entity);
        return entity;
    }

    public void Destroy(Entity entity)
    {
        if (entity == null || !entity.IsAlive)
        {
            return;
        }

        entity.IsAlive = false;

        if (_types.TryGetValue(entity.Config.Name, out EntityType type))
        {
            type.Entities.Remove(entity);
            type.Behavior?.OnDeath(entity);
        }

        _entities.Remove(entity.Id);
        Queries.UnregisterOwner(entity.Id);
        Hitboxes.DestroyOwner(entity.Id);
        Physics.Destroy(entity.Id);
        entity.QueueFree();
    }

    private void BuildTypes()
    {
        foreach (Resource resource in Types)
        {
            if (resource is not EntityTypeConfig config || string.IsNullOrWhiteSpace(config.Name))
            {
                GD.PushError("EntityManager: некорректное описание типа.");
                continue;
            }

            EntityType type = new()
            {
                Config = config,
                Name = config.Name,
                Kind = config.Kind,
            };

            if (config.VisualScene != null)
            {
                (Vector3 center, Vector3 size, float minY, bool hasBounds) = ComputeSceneBounds(config.VisualScene);
                type.BaseCenter = center;
                type.BaseSize = size;
                type.BottomOffset = -minY;
                type.HasBounds = hasBounds;
            }

            _types[config.Name] = type;
        }

        foreach (EntityType type in _types.Values)
        {
            EntityTypeConfig config = type.Config;
            type.Behavior = CreateBehavior(config);

            if (type.Behavior == null)
            {
                GD.PushError($"EntityManager: у типа {type.Name} нет поведения.");
                continue;
            }

            if (type.Kind == EntityKind.Object && config.QueryScript != null)
            {
                type.Query = CreateQuery(config);
            }

            HitboxType hitboxType = new()
            {
                Name = type.Name,
                Kind = type.Kind,
                BaseCenter = type.BaseCenter,
                BaseSize = type.BaseSize,
                OptimizePosition = config.OptimizePosition,
                OptimizeChecks = config.OptimizeChecks,
                CheckIntervalFrames = config.CheckIntervalFrames,
                CollisionTypeNames = CopyNames(config.CollisionTypeNames),
                Behavior = type.Behavior,
            };

            Hitboxes.RegisterType(hitboxType);
        }

        foreach (EntityType type in _types.Values)
        {
            if (type.Kind == EntityKind.Projectile && type.Behavior is ProjectileBehavior projectileBehavior)
            {
                Projectiles.Register(type.Config, projectileBehavior);
            }
        }
    }

    private EntityBehavior CreateBehavior(EntityTypeConfig config)
    {
        if (config.BehaviorScript == null)
        {
            return null;
        }

        Node node = config.BehaviorScript.New(Array.Empty<Variant>()).As<Node>();

        if (node is not EntityBehavior behavior)
        {
            node?.Free();
            return null;
        }

        behavior.Initialize(this);
        AddChild(behavior);
        return behavior;
    }

    private EntityQuery CreateQuery(EntityTypeConfig config)
    {
        Node node = config.QueryScript.New(Array.Empty<Variant>()).As<Node>();

        if (node is not EntityQuery query)
        {
            node?.Free();
            return null;
        }

        query.Initialize(this);
        AddChild(query);
        return query;
    }

    private void BuildStaticObjects()
    {
        Node sceneRoot = GetTree().CurrentScene ?? GetTree().Root;

        foreach (EntityType type in _types.Values)
        {
            if (type.Kind != EntityKind.Static)
            {
                continue;
            }

            foreach (string path in type.Config.SceneObjectPaths)
            {
                Node3D node = sceneRoot.GetNodeOrNull<Node3D>(path);

                if (node == null)
                {
                    GD.PushError($"EntityManager: не найден объект сцены {path}.");
                    continue;
                }

                (Vector3 center, Vector3 size, Vector3 rotation) = ComputeNodeBounds(node);
                int ownerId = NextOwnerId();
                HitboxObject hitbox = Hitboxes.CreateHitbox(ownerId, type.Name, node, true);

                if (hitbox == null)
                {
                    continue;
                }

                hitbox.Position = center;
                hitbox.Size = size;
                hitbox.Rotation = rotation;
                hitbox.IsStatic = true;
                hitbox.UpdatedThisFrame = true;
            }
        }
    }

    // Базовые размеры визуальной сцены: центр и размер AABB по мешам.
    private static (Vector3 Center, Vector3 Size, float MinY, bool HasBounds) ComputeSceneBounds(PackedScene scene)
    {
        Node root = scene.Instantiate();

        if (root is not Node3D root3D)
        {
            root?.Free();
            return (Vector3.Zero, Vector3.Zero, 0f, false);
        }

        Aabb bounds = default;
        bool hasBounds = false;

        if (root3D is MeshInstance3D rootMesh)
        {
            AddMeshBounds(rootMesh, Transform3D.Identity, ref bounds, ref hasBounds);
        }

        foreach (Node child in root3D.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (child is not MeshInstance3D meshInstance)
            {
                continue;
            }

            Transform3D relativeTransform = GetRelativeTransform(meshInstance, root3D);
            AddMeshBounds(meshInstance, relativeTransform, ref bounds, ref hasBounds);
        }

        root.Free();

        if (!hasBounds)
        {
            return (Vector3.Zero, Vector3.Zero, 0f, false);
        }

        Vector3 center = bounds.Position + bounds.Size * 0.5f;
        return (center, bounds.Size, bounds.Position.Y, true);
    }

    private static void AddMeshBounds(MeshInstance3D meshInstance, Transform3D relativeTransform, ref Aabb bounds, ref bool hasBounds)
    {
        if (meshInstance.Mesh == null)
        {
            return;
        }

        Aabb meshBounds = GetMeshBounds(meshInstance.Mesh);
        Aabb transformedBounds = TransformAabb(relativeTransform, meshBounds);

        if (!hasBounds)
        {
            bounds = transformedBounds;
            hasBounds = true;
            return;
        }

        bounds = bounds.Merge(transformedBounds);
    }

    private static Aabb GetMeshBounds(Mesh mesh)
    {
        if (mesh is SphereMesh sphere)
        {
            Vector3 size = new(sphere.Radius * 2f, sphere.Height, sphere.Radius * 2f);
            Aabb sphereBounds = mesh.GetAabb();
            Vector3 center = sphereBounds.Position + sphereBounds.Size * 0.5f;
            return new Aabb(center - size * 0.5f, size);
        }

        return mesh.GetAabb();
    }

    private static Aabb TransformAabb(Transform3D transform, Aabb aabb)
    {
        Vector3 minimum = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 maximum = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

        for (int x = 0; x <= 1; x++)
        {
            for (int y = 0; y <= 1; y++)
            {
                for (int z = 0; z <= 1; z++)
                {
                    Vector3 corner = aabb.Position + new Vector3(
                        x == 0 ? 0f : aabb.Size.X,
                        y == 0 ? 0f : aabb.Size.Y,
                        z == 0 ? 0f : aabb.Size.Z);
                    Vector3 worldCorner = transform * corner;
                    minimum = new Vector3(
                        Mathf.Min(minimum.X, worldCorner.X),
                        Mathf.Min(minimum.Y, worldCorner.Y),
                        Mathf.Min(minimum.Z, worldCorner.Z));
                    maximum = new Vector3(
                        Mathf.Max(maximum.X, worldCorner.X),
                        Mathf.Max(maximum.Y, worldCorner.Y),
                        Mathf.Max(maximum.Z, worldCorner.Z));
                }
            }
        }

        return new Aabb(minimum, maximum - minimum);
    }

    private static Transform3D GetRelativeTransform(Node3D node, Node3D root)
    {
        Transform3D transform = Transform3D.Identity;
        Node current = node;

        while (current != null && current != root)
        {
            if (current is Node3D node3D)
            {
                transform = node3D.Transform * transform;
            }

            current = current.GetParent();
        }

        return transform;
    }

    // Размеры готового объекта сцены (стены, пол) по его форме или мешу.
    private static (Vector3 Center, Vector3 Size, Vector3 Rotation) ComputeNodeBounds(Node3D node)
    {
        if (node is CollisionShape3D shape && shape.Shape != null)
        {
            Aabb local = GetShapeAabb(shape.Shape);
            Transform3D transform = shape.GlobalTransform;
            Vector3 size = local.Size * transform.Basis.Scale.Abs();
            Vector3 center = transform * (local.Position + local.Size * 0.5f);
            return (center, size, transform.Basis.Orthonormalized().GetEuler());
        }

        if (node is MeshInstance3D mesh && mesh.Mesh != null)
        {
            Aabb local = GetMeshBounds(mesh.Mesh);
            Transform3D transform = mesh.GlobalTransform;
            Vector3 size = local.Size * transform.Basis.Scale.Abs();
            Vector3 center = transform * (local.Position + local.Size * 0.5f);
            return (center, size, transform.Basis.Orthonormalized().GetEuler());
        }

        return (node.GlobalPosition, Vector3.One, node.GlobalRotation);
    }

    private static Aabb GetShapeAabb(Shape3D shape)
    {
        switch (shape)
        {
            case BoxShape3D box:
                return new Aabb(-box.Size / 2f, box.Size);

            case SphereShape3D sphere:
            {
                float radius = sphere.Radius;
                return new Aabb(
                    new Vector3(-radius, -radius, -radius),
                    new Vector3(radius * 2f, radius * 2f, radius * 2f));
            }

            case CapsuleShape3D capsule:
            {
                float radius = capsule.Radius;
                float height = capsule.Height;
                return new Aabb(
                    new Vector3(-radius, -height / 2f, -radius),
                    new Vector3(radius * 2f, height, radius * 2f));
            }

            case CylinderShape3D cylinder:
            {
                float radius = cylinder.Radius;
                float height = cylinder.Height;
                return new Aabb(
                    new Vector3(-radius, -height / 2f, -radius),
                    new Vector3(radius * 2f, height, radius * 2f));
            }

            default:
            {
                Mesh debugMesh = shape.GetDebugMesh();
                return debugMesh != null ? debugMesh.GetAabb() : new Aabb();
            }
        }
    }

    private static string[] CopyNames(Godot.Collections.Array<string> source)
    {
        if (source == null)
        {
            return Array.Empty<string>();
        }

        string[] result = new string[source.Count];

        for (int i = 0; i < source.Count; i++)
        {
            result[i] = source[i];
        }

        return result;
    }
}
