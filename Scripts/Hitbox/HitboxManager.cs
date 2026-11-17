    using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;

namespace TowerDefense;

// Менеджер хитбоксов: создаёт типы из TypesOfObjects, собирает объекты
// из групп с именами типов и проверяет пересечения их прямоугольных хитбоксов.
//
// Инструкция по TypesOfObjects:
// In the “TypesOfObjects” list, specify
// the name of the group in which this object is located,
// the scene,
// the processing script,
// position optimization,
// check optimization,
// the element type number that should trigger the action.
public partial class HitboxManager : Node3D
{
    private const float HalfExtentFactor = 0.5f;
    private const float SphereDiameterFactor = 2f;
    private const string FpsLabelPath = "CanvasLayer/Fps";

    [ExportCategory("Instruction")]
    [Export(PropertyHint.MultilineText, "")]
    public string TypesInstruction =
        "In the “TypesOfObjects” list, specify\n" +
        "the name of the group in which this object is located,\n" +
        "the scene,\n" +
        "the processing script,\n" +
        "position optimization,\n" +
        "check optimization,\n" +
        "the element type number that should trigger the action.";

    [ExportCategory("Types")]
    [Export] public Godot.Collections.Array<Resource> TypesOfObjects = new();

    [ExportCategory("Settings")]
    [Export] public int PhysicsPriority = 100;
    [Export] public Key DebugToggleKey = Key.Z;
    [Export] public Color DebugColor = new Color(1f, 0.35f, 0.65f, 0.35f);

    // Большой публичный список: [ [объекты типа 0], [объекты типа 1], ... ].
    public List<List<HitboxObject>> HitboxesByType { get; } = new();

    // Глобальный FPS проекта, который отображается в Label и используется хитбоксами.
    public float CurrentFps => (float)Engine.GetFramesPerSecond();

    private readonly List<HitboxType> _types = new();
    private readonly Dictionary<ulong, HitboxObject> _hitboxesByNode = new();
    private readonly List<HitboxCheckTask> _checkTasks = new();
    private StandardMaterial3D _debugMaterial;
    private bool _debugVisible;
    private Label _fpsLabel;

    public override void _Ready()
    {
        ProcessPhysicsPriority = PhysicsPriority;
        _fpsLabel = GetNodeOrNull<Label>(FpsLabelPath);
        BuildTypes();
    }

    public override void _PhysicsProcess(double delta)
    {
        UpdateFpsLabel();
        RefreshObjects();
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

    // Проверка пересечения двух объектов по их хитбоксам.
    // Используется, например, ближним боем: геометрия остаётся в HitboxProcessor.
    public bool Intersects(Node3D first, Node3D second)
    {
        if (first == null || second == null)
        {
            return false;
        }

        if (!_hitboxesByNode.TryGetValue(first.GetInstanceId(), out HitboxObject firstHitbox) ||
            !_hitboxesByNode.TryGetValue(second.GetInstanceId(), out HitboxObject secondHitbox))
        {
            return false;
        }

        return firstHitbox.IsAlive
            && secondHitbox.IsAlive
            && HitboxProcessor.Intersects(firstHitbox, secondHitbox);
    }

    private void BuildTypes()
    {
        _types.Clear();
        HitboxesByType.Clear();
        _hitboxesByNode.Clear();

        if (TypesOfObjects == null || TypesOfObjects.Count == 0)
        {
            GD.PushError("HitboxManager: TypesOfObjects is empty.");
            return;
        }

        for (int i = 0; i < TypesOfObjects.Count; i++)
        {
            HitboxType type = new();
            _types.Add(type);
            HitboxesByType.Add(type.Objects);

            if (TypesOfObjects[i] is not HitboxTypeConfig config)
            {
                GD.PushError($"HitboxManager: TypesOfObjects[{i}] is not HitboxTypeConfig.");
                continue;
            }

            type.Scene = config.Scene;
            type.OptimizePosition = config.OptimizePosition;
            type.OptimizeChecks = config.OptimizeChecks;
            type.CheckIntervalFrames = config.CheckIntervalFrames;
            type.CollisionTypeNumbers = CopyCollisionTypes(config.CollisionTypeNumbers);
            type.GroupName = config.GetGroupNameOrDefault();

            if (config.Scene == null)
            {
                GD.PushError($"HitboxManager: TypesOfObjects[{i}] has no scene.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(type.GroupName))
            {
                GD.PushError($"HitboxManager: TypesOfObjects[{i}] has no group name.");
                continue;
            }

            (Vector3 center, Vector3 size) = GetSceneBounds(config.Scene);

            if (!HasVolume(size))
            {
                GD.PushError($"HitboxManager: TypesOfObjects[{i}] has no mesh bounds.");
                continue;
            }

            type.BaseCenter = center;
            type.BaseSize = size;
            type.IsValid = true;

            if (config.ProcessorScript != null)
            {
                Node processorNode = config.ProcessorScript.New(Array.Empty<Variant>()).As<Node>();

                if (processorNode is HitboxProcessor processor)
                {
                    processor.Name = $"HitboxProcessor{i}";
                    AddChild(processor);
                    type.Processor = processor;
                }
                else
                {
                    processorNode?.Free();
                    GD.PushError($"HitboxManager: TypesOfObjects[{i}] processor is not HitboxProcessor.");
                }
            }
        }
    }

    private void UpdateFpsLabel()
    {
        if (_fpsLabel == null)
        {
            return;
        }

        int fps = Mathf.RoundToInt(CurrentFps);
        _fpsLabel.Text = $"+/- Fps: {fps}";
    }

    // Каждый кадр сканирует группы с именами типов и добавляет новые объекты
    // в соответствующий список. Уже известные объекты не дублируются.
    private void RefreshObjects()
    {
        for (int typeIndex = 0; typeIndex < _types.Count; typeIndex++)
        {
            HitboxType type = _types[typeIndex];

            for (int i = type.Objects.Count - 1; i >= 0; i--)
            {
                HitboxObject hitbox = type.Objects[i];

                if (IsHitboxValid(hitbox))
                {
                    continue;
                }

                hitbox.IsAlive = false;
                RemoveDebugMesh(hitbox);
                _hitboxesByNode.Remove(hitbox.InstanceId);
                type.Objects.RemoveAt(i);
            }

            if (!type.IsValid)
            {
                continue;
            }

            foreach (Node node in GetTree().GetNodesInGroup(type.GroupName))
            {
                if (node is not Node3D node3D || !node3D.IsInsideTree())
                {
                    continue;
                }

                ulong instanceId = node3D.GetInstanceId();

                if (_hitboxesByNode.ContainsKey(instanceId))
                {
                    continue;
                }

                HitboxObject hitbox = new(node3D, type, typeIndex, type.BaseCenter, type.BaseSize);
                type.Objects.Add(hitbox);
                _hitboxesByNode[instanceId] = hitbox;
            }
        }
    }

    private void BuildCheckTasks()
    {
        _checkTasks.Clear();

        for (int typeIndex = 0; typeIndex < _types.Count; typeIndex++)
        {
            HitboxType type = _types[typeIndex];

            if (!type.IsValid)
            {
                continue;
            }

            foreach (HitboxObject hitbox in type.Objects)
            {
                if (!hitbox.IsAlive)
                {
                    continue;
                }

                bool updated = UpdateHitbox(hitbox);

                if (type.CheckIntervalFrames > 0)
                {
                    if (hitbox.FramesUntilCheck > 0)
                    {
                        hitbox.FramesUntilCheck--;
                        continue;
                    }

                    hitbox.FramesUntilCheck = Mathf.Max(type.CheckIntervalFrames - 1, 0);
                }
                else if (type.OptimizeChecks && !updated)
                {
                    continue;
                }

                foreach (int targetTypeIndex in hitbox.CollisionTypeNumbers)
                {
                    if (targetTypeIndex < 0 || targetTypeIndex >= _types.Count || !_types[targetTypeIndex].IsValid)
                    {
                        continue;
                    }

                    if (_types[targetTypeIndex].Objects.Count == 0)
                    {
                        continue;
                    }

                    _checkTasks.Add(new HitboxCheckTask(hitbox, targetTypeIndex));
                }
            }
        }
    }

    // Обновляет позицию хитбокса по правилам п. 5.1/5.2.
    // Пока не истёк период, положение и скорость не пересчитываются.
    private bool UpdateHitbox(HitboxObject hitbox)
    {
        if (!hitbox.Type.OptimizePosition)
        {
            hitbox.UpdateFromNode(CurrentFps);
            return true;
        }

        if (hitbox.FramesUntilUpdate > 0)
        {
            hitbox.FramesUntilUpdate--;
            return false;
        }

        hitbox.UpdateFromNode(CurrentFps);
        return true;
    }

    // Проверки распределяются по объектам и целевым типам через Parallel.For.
    // Внутри потоков читаются только снимки хитбоксов; изменение сцены
    // выполняется после этого на главном потоке.
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
            List<HitboxObject> targets = HitboxesByType[task.TargetTypeIndex];

            for (int i = 0; i < targets.Count; i++)
            {
                HitboxObject other = targets[i];

                if (!task.Self.IsAlive || !other.IsAlive || ReferenceEquals(task.Self, other))
                {
                    continue;
                }

                // Пары одного типа проверяются один раз: от меньшего InstanceId.
                if (task.TargetTypeIndex == task.Self.TypeIndex && other.InstanceId <= task.Self.InstanceId)
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

            hit.Self.Type.Processor?.ProcessHit(hit.Self, hit.Other, hit.Intersection);
        }
    }

    private void UpdateDebugMeshes()
    {
        if (!_debugVisible)
        {
            return;
        }

        foreach (HitboxType type in _types)
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
        foreach (HitboxType type in _types)
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
        if (!HasVolume(hitbox.Size))
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

        // Позиция, поворот и размеры хитбокса целиком переносятся в Transform.
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

    private static bool IsHitboxValid(HitboxObject hitbox)
    {
        return hitbox.Node != null
            && GodotObject.IsInstanceValid(hitbox.Node)
            && hitbox.Node.IsInsideTree();
    }

    private static bool HasVolume(Vector3 size)
    {
        return size.X > 0f && size.Y > 0f && size.Z > 0f;
    }

    private static int[] CopyCollisionTypes(Godot.Collections.Array<int> source)
    {
        if (source == null)
        {
            return Array.Empty<int>();
        }

        int[] result = new int[source.Count];

        for (int i = 0; i < source.Count; i++)
        {
            result[i] = source[i];
        }

        return result;
    }

    // Базовые размеры объекта определяются по MeshInstance3D из его сцены.
    // Для сферы берутся радиус и высота, для остальных мешей — их AABB.
    private static (Vector3 Center, Vector3 Size) GetSceneBounds(PackedScene scene)
    {
        Node root = scene.Instantiate();

        if (root is not Node3D root3D)
        {
            root?.Free();
            return (Vector3.Zero, Vector3.Zero);
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
            return (Vector3.Zero, Vector3.Zero);
        }

        Vector3 center = bounds.Position + bounds.Size * HalfExtentFactor;
        return (center, bounds.Size);
    }

    private static void AddMeshBounds(
        MeshInstance3D meshInstance,
        Transform3D relativeTransform,
        ref Aabb bounds,
        ref bool hasBounds)
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
            // Полный размер сферы: диаметр по X/Z и height по Y.
            Vector3 size = new(
                sphere.Radius * SphereDiameterFactor,
                sphere.Height,
                sphere.Radius * SphereDiameterFactor);

            Aabb sphereBounds = mesh.GetAabb();
            Vector3 center = sphereBounds.Position + sphereBounds.Size * HalfExtentFactor;
            return new Aabb(center - size * HalfExtentFactor, size);
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

    // Локальная трансформация меша относительно корня сцены.
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

    private readonly struct HitboxCheckTask
    {
        public readonly HitboxObject Self;
        public readonly int TargetTypeIndex;

        public HitboxCheckTask(HitboxObject self, int targetTypeIndex)
        {
            Self = self;
            TargetTypeIndex = targetTypeIndex;
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
