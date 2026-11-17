using Godot;

namespace TowerDefense;

// Свободный полёт: WASD — по направлению камеры (с учётом наклона),
// мышь — обзор без ограничения наклона, Shift — ускорение.
public partial class Player : CharacterBody3D
{
    // Скорость полёта, как у свободной камеры редактора Godot.
    private const float DefaultFlySpeed = 10f;

    // Ускорение Shift, как в редакторе.
    private const float FastSpeedMultiplier = 3f;

    // Чувствительность мыши.
    private const float MouseSensitivity = 0.002f;

    [Export] public float FlySpeed = DefaultFlySpeed;

    private Camera3D _camera;
    private float _pitch;

    public override void _Ready()
    {
        // Камера должна быть дочерним узлом игрока.
        _camera = GetNodeOrNull<Camera3D>("Camera3D");

        if (_camera != null)
        {
            _camera.Current = true;
            _camera.Rotation = Vector3.Zero;
        }

        // Корпус вращается только вокруг Y; наклон хранится у камеры.
        Rotation = new Vector3(0f, Rotation.Y, 0f);

        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Мышь вращает корпус по горизонтали и камеру по вертикали.
        if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            RotateY(-motion.Relative.X * MouseSensitivity);

            // Наклон не ограничиваем: камера может смотреть в любую сторону.
            _pitch -= motion.Relative.Y * MouseSensitivity;

            if (_camera != null)
            {
                _camera.Rotation = new Vector3(_pitch, 0f, 0f);
            }
        }

        // Escape отпускает мышь, клик — снова захватывает.
        if (@event.IsActionPressed("ui_cancel"))
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
        else if (@event is InputEventMouseButton button && button.Pressed &&
                 Input.MouseMode == Input.MouseModeEnum.Visible)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        // Полёт идёт ровно туда, куда смотрит камера, вместе с наклоном.
        Basis basis = _camera != null ? _camera.GlobalTransform.Basis : Transform.Basis;

        Vector3 forward = -basis.Z;
        Vector3 right = basis.X;

        Vector3 direction = Vector3.Zero;

        if (Input.IsKeyPressed(Key.W)) direction += forward;
        if (Input.IsKeyPressed(Key.S)) direction -= forward;
        if (Input.IsKeyPressed(Key.D)) direction += right;
        if (Input.IsKeyPressed(Key.A)) direction -= right;

        if (direction == Vector3.Zero)
        {
            return;
        }

        float speed = Input.IsKeyPressed(Key.Shift) ? FlySpeed * FastSpeedMultiplier : FlySpeed;
        GlobalPosition += direction.Normalized() * speed * (float)delta;
    }
}
