using System.Collections.Generic;
using Godot;

namespace TowerDefense;

// Менеджер визуальной и физической части объекта: создаёт сцену, применяет
// запросы движения и двигает CharacterBody3D штатной физикой Godot.
public partial class PhysicManager : Node3D
{
    private sealed class PhysicBody
    {
        public CharacterBody3D Body;
        public Vector3 Velocity;
        public Vector3 Rotation;
        public Vector3 Displacement;
    }

    [Export] public float Gravity = 9.8f;
    [Export] public int PhysicsPriority = 50;

    private readonly Dictionary<int, Node3D> _visuals = new();
    private readonly Dictionary<int, PhysicBody> _bodies = new();
    private Node3D _visualRoot;

    // Подинструмент, который отвечает за снаряды: их поведение и визуал.
    public PhysicProjectileManager Projectiles { get; private set; }

    public QueryManager Queries { get; set; }

    public override void _Ready()
    {
        ProcessPhysicsPriority = PhysicsPriority;

        _visualRoot = new Node3D
        {
            Name = "Visuals",
        };
        AddChild(_visualRoot);

        Projectiles = GetNodeOrNull<PhysicProjectileManager>("PhysicProjectileManager");

        if (Projectiles == null)
        {
            GD.PushError("PhysicManager: нет дочернего PhysicProjectileManager.");
        }
    }

    public Node3D Spawn(int ownerId, PackedScene scene, Vector3 position, Vector3 rotation)
    {
        if (scene == null)
        {
            return null;
        }

        Node node = scene.Instantiate();

        if (node is not Node3D visual)
        {
            node?.Free();
            return null;
        }

        _visualRoot.AddChild(visual);
        visual.GlobalPosition = position;
        visual.GlobalRotation = rotation;
        _visuals[ownerId] = visual;

        if (visual is CharacterBody3D body)
        {
            _bodies[ownerId] = new PhysicBody
            {
                Body = body,
            };
        }

        return visual;
    }

    public Node3D GetVisual(int ownerId)
    {
        return _visuals.TryGetValue(ownerId, out Node3D visual) ? visual : null;
    }

    public void Destroy(int ownerId)
    {
        _bodies.Remove(ownerId);

        if (_visuals.Remove(ownerId, out Node3D visual) && GodotObject.IsInstanceValid(visual))
        {
            visual.QueueFree();
        }
    }

    // Запрос движения: горизонтальная скорость и поворот корпуса.
    public void SetMovement(int ownerId, Vector3 velocity, Vector3 rotation)
    {
        if (_bodies.TryGetValue(ownerId, out PhysicBody state))
        {
            state.Velocity = velocity;
            state.Rotation = rotation;
        }
    }

    // Запрос смещения: применится через MoveAndCollide после MoveAndSlide.
    public void AddDisplacement(int ownerId, Vector3 displacement)
    {
        if (_bodies.TryGetValue(ownerId, out PhysicBody state))
        {
            state.Displacement += displacement;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        float frameDelta = (float)delta;

        foreach (KeyValuePair<int, PhysicBody> pair in _bodies)
        {
            PhysicBody state = pair.Value;
            CharacterBody3D body = state.Body;

            if (!GodotObject.IsInstanceValid(body))
            {
                continue;
            }

            Vector3 velocity = body.Velocity;

            if (body.IsOnFloor())
            {
                if (velocity.Y < 0f)
                {
                    velocity.Y = 0f;
                }
            }
            else
            {
                velocity.Y -= Gravity * frameDelta;
            }

            velocity.X = state.Velocity.X;
            velocity.Z = state.Velocity.Z;
            body.Velocity = velocity;
            body.Rotation = new Vector3(0f, state.Rotation.Y, 0f);
            body.MoveAndSlide();

            if (state.Displacement.LengthSquared() > 0f)
            {
                body.MoveAndCollide(state.Displacement);
                state.Displacement = Vector3.Zero;
            }

            Queries?.SyncFromSource(pair.Key);
        }
    }
}
