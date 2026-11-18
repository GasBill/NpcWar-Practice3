using Godot;

namespace TowerDefense;

// Один хитбокс: чистая математика (положение, размеры, поворот).
// Он не привязан к Node3D — трансформ приходит из QueryManager.
public sealed class HitboxObject
{
    public int OwnerId;
    public HitboxType Type;

    // Фактический объект сцены, с которого снимаются положение и поворот.
    public Node3D Source;

    public Vector3 Position;
    public Vector3 Size;
    public Vector3 Rotation;
    public Vector3 BaseSize;

    // Центр источника на прошлом обновлении: по нему строится swept-хитбокс.
    public Vector3 LastSourceCenter;
    public float Speed;
    public int UpdatePeriod;
    public int FramesUntilUpdate;
    public int FramesUntilCheck;
    public bool UpdatedThisFrame;
    public bool IsStatic;
    public bool IsAlive = true;
    public bool HasPosition;
    public MeshInstance3D DebugMesh;

    public HitboxObject(int ownerId, HitboxType type, Node3D source, Vector3 baseSize)
    {
        OwnerId = ownerId;
        Type = type;
        Source = source;
        BaseSize = baseSize;
        Size = baseSize;
        HasPosition = false;

        if (type.CheckIntervalFrames > 0)
        {
            FramesUntilCheck = (int)((ulong)ownerId % (ulong)type.CheckIntervalFrames);
        }
    }
}
