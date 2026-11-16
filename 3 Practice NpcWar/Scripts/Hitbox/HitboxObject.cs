using Godot;

namespace TowerDefense;

// Свойства одного хитбокса: положение, размеры, поворот и номера типов,
// с которыми этот объект должен проверять пересечения.
// Геометрия хранится отдельно от визуальной и физической частей объекта
// и обновляется по правилам оптимизации из HitboxManager.
public sealed class HitboxObject
{
    // Скорость меньше этой величины обрабатывается каждый кадр (п. 5.2.1).
    private const float MinimumSpeedForOptimization = 1f;

    // Множитель FPS в формуле периода: fps * 4 / speed.
    private const float SpeedMultiplierForUpdatePeriod = 4f;

    public Node3D Node { get; }
    public ulong InstanceId { get; }
    public HitboxType Type { get; }
    public int TypeIndex { get; }
    public int[] CollisionTypeNumbers { get; }
    public MeshInstance3D DebugMesh;

    public Vector3 Position;
    public Vector3 Size;
    public Vector3 Rotation;
    public float Speed;
    public int FramesUntilUpdate;
    public int UpdatePeriod;
    public int CheckIntervalFrames;
    public int FramesUntilCheck;

    // Флаг читается из параллельных проверок, поэтому это обычное поле.
    public bool IsAlive = true;

    private readonly Vector3 _localCenter;
    private readonly Vector3 _baseSize;

    public HitboxObject(Node3D node, HitboxType type, int typeIndex, Vector3 localCenter, Vector3 baseSize)
    {
        Node = node;
        InstanceId = node.GetInstanceId();
        Type = type;
        TypeIndex = typeIndex;
        CollisionTypeNumbers = type.CollisionTypeNumbers;
        CheckIntervalFrames = type.CheckIntervalFrames;
        FramesUntilCheck = CheckIntervalFrames > 0
            ? (int)(InstanceId % (ulong)CheckIntervalFrames)
            : 0;
        _localCenter = localCenter;
        _baseSize = baseSize;
    }

    // Считывает положение и поворот объекта и пересчитывает размеры.
    // Скорость берётся только здесь, то есть после того, как прошли кадры,
    // рассчитанные по предыдущему периоду (п. 5.2.2).
    public void UpdateFromNode(float framesPerSecond)
    {
        Transform3D transform = Node.GlobalTransform;

        Position = transform * _localCenter;
        Rotation = transform.Basis.Orthonormalized().GetEuler();
        Size = _baseSize * transform.Basis.Scale.Abs();
        Speed = GetSpeed(Node);

        UpdatePeriod = CalculateUpdatePeriod(framesPerSecond, Speed);
        FramesUntilUpdate = UpdatePeriod - 1 < 0 ? 0 : UpdatePeriod - 1;
    }

    private int CalculateUpdatePeriod(float framesPerSecond, float speed)
    {
        if (!Type.OptimizePosition)
        {
            return 1;
        }

        // При скорости меньше 1 хитбокс обновляется каждый кадр,
        // то есть с частотой глобального FPS проекта (п. 5.2.1).
        if (speed < MinimumSpeedForOptimization)
        {
            return 1;
        }

        float fps = Mathf.Max(framesPerSecond, 1f);
        return Mathf.Max(Mathf.RoundToInt(fps * SpeedMultiplierForUpdatePeriod / speed), 1);
    }

    private static float GetSpeed(Node3D node)
    {
        if (node is IHitboxSpeedProvider provider)
        {
            return provider.HitboxSpeed;
        }

        if (node is CharacterBody3D body)
        {
            return body.Velocity.Length();
        }

        if (node is RigidBody3D rigidBody)
        {
            return rigidBody.LinearVelocity.Length();
        }

        return 0f;
    }
}
