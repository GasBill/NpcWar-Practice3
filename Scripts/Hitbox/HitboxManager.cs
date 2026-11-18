using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace TowerDefense;

// Менеджер хитбоксов: хранит хитбоксы типов, находит их пересечения
// и передаёт результат в EntityBehavior. Позиции и размеры приходят
// из QueryManager, поэтому здесь осталась только математика проверок.
public partial class HitboxManager : Node3D
{
    [Export] public int PhysicsPriority = 100;
    [Export] public Key DebugToggleKey = Key.Z;
    [Export] public Color DebugColor = new Color(1f, 0.35f, 0.65f, 0.35f);

    private readonly Dictionary<string, HitboxType> _types = new();
    private readonly Dictionary<int, List<HitboxObject>> _hitboxesByOwner = new();
    private readonly Dictionary<int, HitboxObject> _mainByOwner = new();
    private readonly List<HitboxCheckTask> _checkTasks = new();
    private StandardMaterial3D _debugMaterial;
    private bool _debugVisible;

    public override void _Ready()
    {
        ProcessPhysicsPriority = PhysicsPriority;
    }

    public void RegisterType(HitboxType type)
    {
        if (string.IsNullOrWhiteSpace(type.Name))
        {
            GD.PushError("HitboxManager: у типа нет имени.");
            return;
        }

        if (_types.ContainsKey(type.Name))
        {
            GD.PushError($"HitboxManager: тип {type.Name} уже зарегистрирован.");
            return;
        }

        _types[type.Name] = type;
    }

    public HitboxObject CreateHitbox(int ownerId, string typeName, Node3D source, bool isMain = false)
    {
        if (!_types.TryGetValue(typeName, out HitboxType type))
        {
            GD.PushError($"HitboxManager: неизвестный тип {typeName}.");
            return null;
        }

        HitboxObject hitbox = new(ownerId, type, source, type.BaseSize);
        type.Objects.Add(hitbox);

        if (!_hitboxesByOwner.TryGetValue(ownerId, out List<HitboxObject> hitboxes))
        {
            hitboxes = new List<HitboxObject>();
            _hitboxesByOwner[ownerId] = hitboxes;
        }

        hitboxes.Add(hitbox);

        if (isMain)
        {
            _mainByOwner[ownerId] = hitbox;
        }

        return hitbox;
    }

    public void DestroyOwner(int ownerId)
    {
        if (_hitboxesByOwner.Remove(ownerId, out List<HitboxObject> hitboxes))
        {
            foreach (HitboxObject hitbox in hitboxes)
            {
                hitbox.IsAlive = false;
                hitbox.Type.Objects.Remove(hitbox);
                RemoveDebugMesh(hitbox);
            }
        }

        _mainByOwner.Remove(ownerId);
    }

    public HitboxObject GetMainHitbox(int ownerId)
    {
        return _mainByOwner.TryGetValue(ownerId, out HitboxObject hitbox) ? hitbox : null;
    }

    public Vector3 GetPosition(int ownerId)
    {
        HitboxObject hitbox = GetMainHitbox(ownerId);
        return hitbox != null ? hitbox.Position : Vector3.Zero;
    }

    public bool Intersects(int firstOwnerId, int secondOwnerId)
    {
        HitboxObject first = GetMainHitbox(firstOwnerId);
        HitboxObject second = GetMainHitbox(secondOwnerId);

        return first != null && second != null
            && first.IsAlive && second.IsAlive
            && HitboxProcessor.Intersects(first, second);
    }

    public override void _PhysicsProcess(double delta)
    {
        BuildCheckTasks();
        RunChecks();
        UpdateDebugMeshes();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo &&
            (key.Keycode == DebugToggleKey || key.PhysicalKeycode == DebugToggleKey))
        {
            _debugVisible = !_debugVisible;
            SetDebugVisible(_debugVisible);
            GetViewport().SetInputAsHandled();
        }
    }

    private void BuildCheckTasks()
    {
        _checkTasks.Clear();

        foreach (HitboxType type in _types.Values)
        {
            foreach (HitboxObject hitbox in type.Objects)
            {
                if (!hitbox.IsAlive)
                {
                    continue;
                }

                // Ограничение частоты проверок: у damage-хитбокса это 50 кадров.
                if (type.CheckIntervalFrames > 0)
                {
                    if (hitbox.FramesUntilCheck > 0)
                    {
                        hitbox.FramesUntilCheck--;
                        continue;
                    }

                    hitbox.FramesUntilCheck = Mathf.Max(type.CheckIntervalFrames - 1, 0);
                }
                else if (type.OptimizeChecks && !hitbox.UpdatedThisFrame)
                {
                    continue;
                }

                foreach (string targetName in type.CollisionTypeNames)
                {
                    if (!_types.TryGetValue(targetName, out HitboxType target) || target.Objects.Count == 0)
                    {
                        continue;
                    }

                    _checkTasks.Add(new HitboxCheckTask(hitbox, target));
                }
            }
        }
    }

    private void RunChecks()
    {
        if (_checkTasks.Count == 0)
        {
            return;
        }

        ConcurrentQueue<HitboxHit> hits = new();

        Parallel.For(0, _checkTasks.Count, index =>
        {
            HitboxCheckTask task = _checkTasks[index];

            for (int i = 0; i < task.Target.Objects.Count; i++)
            {
                HitboxObject other = task.Target.Objects[i];

                if (!task.Self.IsAlive || !other.IsAlive || ReferenceEquals(task.Self, other))
                {
                    continue;
                }

                // Пары одного типа проверяются один раз.
                if (ReferenceEquals(task.Target, task.Self.Type) && other.OwnerId <= task.Self.OwnerId)
                {
                    continue;
                }

                if (HitboxProcessor.TryGetIntersection(task.Self, other, out Vector3 intersection))
                {
                    hits.Enqueue(new HitboxHit(task.Self, other, intersection));
                }
            }
        });

        while (hits.TryDequeue(out HitboxHit hit))
        {
            if (!hit.Self.IsAlive || !hit.Other.IsAlive)
            {
                continue;
            }

            hit.Self.Type.Behavior?.OnIntersect(hit.Self, hit.Other, hit.Intersection);
        }
    }

    private void UpdateDebugMeshes()
    {
        if (!_debugVisible)
        {
            return;
        }

        foreach (HitboxType type in _types.Values)
        {
            foreach (HitboxObject hitbox in type.Objects)
            {
                if (!hitbox.IsAlive)
                {
                    RemoveDebugMesh(hitbox);
                    continue;
                }

                if (hitbox.DebugMesh == null)
                {
                    CreateDebugMesh(hitbox);
                }
                else
                {
                    UpdateDebugMesh(hitbox);
                }
            }
        }
    }

    private void SetDebugVisible(bool visible)
    {
        foreach (HitboxType type in _types.Values)
        {
            foreach (HitboxObject hitbox in type.Objects)
            {
                if (visible && hitbox.IsAlive)
                {
                    if (hitbox.DebugMesh == null)
                    {
                        CreateDebugMesh(hitbox);
                    }
                }
                else
                {
                    RemoveDebugMesh(hitbox);
                }
            }
        }
    }

    private void CreateDebugMesh(HitboxObject hitbox)
    {
        if (hitbox.Size.X <= 0f || hitbox.Size.Y <= 0f || hitbox.Size.Z <= 0f)
        {
            return;
        }

        MeshInstance3D meshInstance = new()
        {
            Mesh = new BoxMesh(),
            MaterialOverride = GetDebugMaterial(),
        };

        AddChild(meshInstance);
        hitbox.DebugMesh = meshInstance;
        UpdateDebugMesh(hitbox);
    }

    private void UpdateDebugMesh(HitboxObject hitbox)
    {
        if (hitbox.DebugMesh == null)
        {
            return;
        }

        hitbox.DebugMesh.GlobalPosition = hitbox.Position;
        hitbox.DebugMesh.GlobalRotation = hitbox.Rotation;
        hitbox.DebugMesh.Scale = hitbox.Size;
    }

    private static void RemoveDebugMesh(HitboxObject hitbox)
    {
        if (hitbox.DebugMesh == null)
        {
            return;
        }

        hitbox.DebugMesh.QueueFree();
        hitbox.DebugMesh = null;
    }

    private StandardMaterial3D GetDebugMaterial()
    {
        if (_debugMaterial == null)
        {
            _debugMaterial = new StandardMaterial3D
            {
                AlbedoColor = DebugColor,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            };
        }

        return _debugMaterial;
    }

    private readonly struct HitboxCheckTask
    {
        public readonly HitboxObject Self;
        public readonly HitboxType Target;

        public HitboxCheckTask(HitboxObject self, HitboxType target)
        {
            Self = self;
            Target = target;
        }
    }

    private readonly struct HitboxHit
    {
        public readonly HitboxObject Self;
        public readonly HitboxObject Other;
        public readonly Vector3 Intersection;

        public HitboxHit(HitboxObject self, HitboxObject other, Vector3 intersection)
        {
            Self = self;
            Other = other;
            Intersection = intersection;
        }
    }
}
