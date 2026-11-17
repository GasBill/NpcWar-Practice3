using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;

namespace TowerDefense;

// НПС (башня/юнит), которого менеджер создаёт через Instantiate.
public unsafe partial class Npc : CharacterBody3D, IHitboxSpeedProvider
{
    // Группа, из которой HitboxManager собирает объекты типа NPC.
    public const string HitboxGroupName = "Npc";

    // Группа отдельного damage-хитбокса NPC.
    public const string DamageHitboxGroupName = "NpcDamage";

    // Группа объектов, по коллизиям которых определяется поверхность спавна.
    private const string SpawnSurfaceGroup = "ObjectsToSpawnOn";

    // Имя Label3D, в которую выводится текущее здоровье.
    private const string HealthLabelName = "Hp";

    // Номер команды: НПС из разных команд считаются противниками.
    public int Squad;

    // Номер НПС; равен индексу в массиве менеджера.
    public int Id;

    // Очки, которые команда заплатила за НПС; возвращаются при смерти.
    public int Cost;

    // Числовые значения параметров, которые требуют компоненты НПС.
    public Godot.Collections.Array<float> TowerStats = new();

    // Менеджер, создавший НПС: через него компоненты получают список НПС.
    public NpcManager Manager;

    // Живой ли НПС. После смерти C#-объект уже может быть освобождён,
    // поэтому вместо обращения к движку проверяем это поле.
    public bool IsAlive = true;

    // Скорость для расчёта периода обновления хитбокса.
    public float HitboxSpeed => Velocity.Length();

    // Кэш компонентов атаки: тикают от общего таймера команды.
    private readonly List<INpcAttack> _attacks = new();

    // Кэш компонента здоровья и id текущей цели.
    // Id вместо ссылки: движение берёт позицию через getter менеджера.
    public HealthComponent Health;
    public int TargetId = -1;

    // Сырой указатель на Vector3 в неуправляемой памяти.
    // Адрес выделяется один раз при спавне и передаётся менеджеру.
    public new Vector3* Position;

    // Насколько нижняя точка физического отрезка поднята над поверхностью спавна.
    [Export] public float SpawnHeightAboveSurface = 0.02f;

    // Гравитация НПС; движение и приземление выполняет Godot-физика.
    [Export] public float Gravity = 9.8f;

    // Радиус HitBox по умолчанию, если форму не удалось прочитать.
    private const float DefaultHitBoxRadius = 0.8f;

    // Точка спавна только по XZ; Y нормализуется по поверхности и коллизии.
    private Vector2 _spawnPositionXz;

    // Цвет команды, переданный менеджером при спавне.
    private Color _squadColor;

    // Label3D, в которую выводится здоровье.
    private Label3D _healthLabel;

    // Обновление текста HP не чаще раза в 0.5 с: Label3D дорого перестраивать.
    private const float HealthLabelInterval = 0.5f;
    private float _healthLabelTimer;
    private float _pendingHealth;
    private bool _healthLabelDirty;
    private bool _healthLabelInitialized;

    // HitBox-маркер: хранит форму для радиуса, физики не имеет.
    public Node3D HitBox;

    // Радиус HitBox'а — используется для melee на дистанции.
    public float HitBoxRadius = DefaultHitBoxRadius;

    // Один материал на цвет команды, а не на каждого НПС.
    private static readonly Dictionary<Color, StandardMaterial3D> SquadMaterials = new();

    // Если MoveComponent нет, физику двигает сам НПС.
    private bool _hasMoveComponent;

    // Накопленный вектор отталкивания от полученного урона.
    private Vector3 _pendingHitVector;

    // Вызывается менеджером до добавления НПС на сцену.
    public void Initialize(
        int squad,
        int id,
        NpcManager manager,
        Vector2 spawnPositionXz,
        Color squadColor,
        IReadOnlyList<INpcComponent> components)
    {
        Squad = squad;
        Id = id;
        Manager = manager;
        _spawnPositionXz = spawnPositionXz;
        _squadColor = squadColor;

        foreach (INpcComponent component in components)
        {
            // Компоненты используют собственные значения из [Export],
            // а не принудительное число из менеджера.
            if (component is Node componentNode)
            {
                AddChild(componentNode);
            }
        }

        Node3D damageHitbox = new()
        {
            Name = "DamageHitbox",
        };
        AddChild(damageHitbox);
        damageHitbox.AddToGroup(DamageHitboxGroupName);

        _hasMoveComponent = GetComponent<MoveComponent>() != null;

        // При передаче данных из менеджера сразу показываем стартовое здоровье.
        Health = GetComponent<HealthComponent>();

        if (Health != null)
        {
            UpdateHealthLabel(Health.Health);
        }
    }

    public override void _EnterTree()
    {
        _healthLabel = GetNodeOrNull<Label3D>(HealthLabelName);
        HitBox = GetNodeOrNull<Node3D>("HitBox");
        Health ??= GetComponent<HealthComponent>();

        if (Health != null)
        {
            _pendingHealth = Health.Health;
            ApplyHealthLabel();
        }

        ConfigureHitBox();

        // Тело сталкивается только с миром (слой 1), но не с другими НПС.
        CollisionLayer = 0;
        CollisionMask = 1;

        // НПС, расставленный в редакторе без Initialize, не трогаем.
        if (Manager == null)
        {
            return;
        }

        AddToGroup(HitboxGroupName);
        Rotation = new Vector3(0f, Rotation.Y, 0f);

        // Позиция и цвет ставятся до _Ready дочерних компонентов,
        // чтобы они сразу видели правильное положение НПС.
        PlaceOnSpawnSurface();

        if (Position == null)
        {
            Position = (Vector3*)NativeMemory.Alloc((nuint)sizeof(Vector3));
        }

        *Position = GlobalPosition;
        ApplySquadColor();
    }

    public override void _ExitTree()
    {
        // Освобождаем неуправляемую память, адрес которой был у менеджера.
        if (Position != null)
        {
            NativeMemory.Free(Position);
            Position = null;
        }

        // Даже если НПС удалили не через смерть, он больше не цель.
        IsAlive = false;
    }

    public override void _Ready()
    {
        foreach (Node child in GetChildren())
        {
            if (child is INpcAttack attack)
            {
                _attacks.Add(attack);
            }
        }
    }

    // Вызывается общим таймером команды.
    public void TickAttacks(float delta)
    {
        foreach (INpcAttack attack in _attacks)
        {
            attack.TickAttack(delta);
        }
    }

    // Возвращает компонент указанного типа среди дочерних узлов НПС.
    public T GetComponent<T>() where T : class
    {
        foreach (Node child in GetChildren())
        {
            if (child is T component)
            {
                return component;
            }
        }

        return null;
    }

    // Показывает текущее здоровье НПС в Label3D "Hp".
    public void UpdateHealthLabel(float health)
    {
        _pendingHealth = health;
        _healthLabelDirty = true;

        if (!_healthLabelInitialized)
        {
            ApplyHealthLabel();
        }
    }

    public override void _Process(double delta)
    {
        if (!_healthLabelDirty)
        {
            return;
        }

        _healthLabelTimer += (float)delta;

        if (_healthLabelTimer >= HealthLabelInterval)
        {
            ApplyHealthLabel();
        }
    }

    private void ApplyHealthLabel()
    {
        if (_healthLabel == null)
        {
            return;
        }

        _healthLabel.Text = Mathf.Max(Mathf.CeilToInt(_pendingHealth), 0).ToString();
        _healthLabelDirty = false;
        _healthLabelTimer = 0f;
        _healthLabelInitialized = true;
    }

    // Отталкивание от удара; MoveAndCollide применяет его через физику Godot.
    public void ApplyHitVector(Vector3 vector)
    {
        _pendingHitVector += vector;
    }

    // Раздвигание пересекшихся хитбоксов; вызывается только HitboxManager.
    public void ApplySeparationVector(Vector3 vector)
    {
        if (vector.LengthSquared() <= 0f)
        {
            return;
        }

        MoveAndCollide(vector);

        if (Position != null)
        {
            *Position = GlobalPosition;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        // Повороты X и Z заблокированы: НПС только поворачивается вокруг Y.
        Vector3 rotation = Rotation;

        if (!Mathf.IsZeroApprox(rotation.X) || !Mathf.IsZeroApprox(rotation.Z))
        {
            Rotation = new Vector3(0f, rotation.Y, 0f);
        }

        if (_pendingHitVector.LengthSquared() > 0f)
        {
            MoveAndCollide(_pendingHitVector);
            _pendingHitVector = Vector3.Zero;
        }

        Vector3 velocity = Velocity;

        if (IsOnFloor())
        {
            if (velocity.Y < 0f)
            {
                velocity.Y = 0f;
            }
        }
        else
        {
            velocity.Y -= Gravity * (float)delta;
        }

        Velocity = velocity;

        // Если MoveComponent нет, физику двигает сам НПС.
        if (!_hasMoveComponent)
        {
            MoveAndSlide();
        }

        // Менеджер и движение читают позицию по адресу Position.
        if (Position != null)
        {
            *Position = GlobalPosition;
        }
    }

    // Ближайший живой противник из массива менеджера.
    public int FindNearestEnemyId()
    {
        if (Manager == null)
        {
            return -1;
        }

        return Manager.GetNearestEnemyId(this);
    }

    // Ищет цель только если её нет или она умерла.
    public void AcquireTarget()
    {
        if (HasValidTarget())
        {
            return;
        }

        TargetId = FindNearestEnemyId();
    }

    // Валидна ли цель: адрес позиции живого противника.
    public bool HasValidTarget()
    {
        return Manager != null && Manager.TryGetNpcPositionLink(TargetId, Squad, out _);
    }

    // HitBox больше не физическая Area: из формы берём только радиус,
    // а саму форму отключаем.
    private void ConfigureHitBox()
    {
        if (HitBox == null)
        {
            return;
        }

        CollisionShape3D shapeNode = HitBox.GetNodeOrNull<CollisionShape3D>("CollisionShape3D");

        if (shapeNode?.Shape is CapsuleShape3D capsule)
        {
            HitBoxRadius = capsule.Radius;
        }
        else if (shapeNode?.Shape is SphereShape3D sphere)
        {
            HitBoxRadius = sphere.Radius;
        }
    }

    // Ставит НПС на самую высокую точку коллизий группы ObjectsToSpawnOn
    // под переданными XZ. Верхняя точка поверхности и нижняя точка коллизии
    // нормализуются, а SpawnHeightAboveSurface задаёт зазор над поверхностью.
    private void PlaceOnSpawnSurface()
    {
        float surfaceY = GetHighestSurfaceY(_spawnPositionXz.X, _spawnPositionXz.Y);
        float bottomOffset = GetOwnBottomOffset();

        GlobalPosition = new Vector3(
            _spawnPositionXz.X,
            surfaceY - bottomOffset + SpawnHeightAboveSurface,
            _spawnPositionXz.Y);
    }

    // Ищет максимальную верхнюю точку AABB коллизий группы под точкой XZ.
    private float GetHighestSurfaceY(float x, float z)
    {
        float highestY = float.NegativeInfinity;

        foreach (Node node in GetTree().GetNodesInGroup(SpawnSurfaceGroup))
        {
            if (node is CollisionShape3D shape)
            {
                ConsiderShape(shape, x, z, ref highestY);
            }

            foreach (Node child in node.FindChildren("*", "CollisionShape3D", true, false))
            {
                if (child is CollisionShape3D childShape)
                {
                    ConsiderShape(childShape, x, z, ref highestY);
                }
            }
        }

        return float.IsNegativeInfinity(highestY) ? 0f : highestY;
    }

    // Учитывает верхнюю грань коллизии, если XZ попадает в её AABB.
    private static void ConsiderShape(CollisionShape3D shape, float x, float z, ref float highestY)
    {
        if (shape.Shape == null)
        {
            return;
        }

        Aabb worldAabb = shape.GlobalTransform * GetShapeAabb(shape.Shape);

        bool insideX = x >= worldAabb.Position.X && x <= worldAabb.End.X;
        bool insideZ = z >= worldAabb.Position.Z && z <= worldAabb.End.Z;

        if (insideX && insideZ)
        {
            highestY = Mathf.Max(highestY, worldAabb.End.Y);
        }
    }

    // AABB формы без генерации debug-меша (он дорогой при каждом спавне).
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

            case SeparationRayShape3D ray:
            {
                // Отрезок начинается в локальном нуле и уходит вдоль +Y.
                return new Aabb(Vector3.Zero, new Vector3(0f, ray.Length, 0f));
            }

            default:
            {
                Mesh debugMesh = shape.GetDebugMesh();
                return debugMesh != null ? debugMesh.GetAabb() : new Aabb();
            }
        }
    }

    // Смещение самой нижней точки включённых коллизий относительно корня НПС.
    // Отрицательное значение означает, что коллизия находится ниже корня.
    private float GetOwnBottomOffset()
    {
        float bottomOffset = 0f;
        bool found = false;

        foreach (Node child in FindChildren("*", "CollisionShape3D", true, false))
        {
            if (child is not CollisionShape3D shape || shape.Shape == null || shape.Disabled)
            {
                continue;
            }

            Aabb worldAabb = GetWorldTransform(shape) * GetShapeAabb(shape.Shape);
            float offset = worldAabb.Position.Y - GlobalPosition.Y;

            if (!found || offset < bottomOffset)
            {
                bottomOffset = offset;
                found = true;
            }
        }

        return found ? bottomOffset : 0f;
    }

    // Мировая трансформация узла без требования is_inside_tree():
    // складываем локальные Transform от узла до НПС.
    private Transform3D GetWorldTransform(Node3D node)
    {
        Transform3D transform = Transform3D.Identity;
        Node current = node;

        while (current != null && current != this)
        {
            if (current is Node3D node3D)
            {
                transform = node3D.Transform * transform;
            }

            current = current.GetParent();
        }

        return GlobalTransform * transform;
    }

    // Красит все GeometryInstance3D НПС цветом команды:
    // Material Override -> Albedo -> Color.
    private void ApplySquadColor()
    {
        if (!SquadMaterials.TryGetValue(_squadColor, out StandardMaterial3D material))
        {
            material = new StandardMaterial3D
            {
                AlbedoColor = _squadColor,
            };
            SquadMaterials[_squadColor] = material;
        }

        foreach (Node child in FindChildren("*", "GeometryInstance3D", true, false))
        {
            // Label3D тоже GeometryInstance3D, но её материал трогать нельзя.
            if (child is GeometryInstance3D geometry && child is not Label3D)
            {
                geometry.MaterialOverride = material;
            }
        }

        // Текст здоровья красится в тот же цвет, что и модель.
        if (_healthLabel != null)
        {
            _healthLabel.Modulate = _squadColor;
        }
    }
}
