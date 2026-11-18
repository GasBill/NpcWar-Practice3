using System.Collections.Generic;
using Godot;

namespace TowerDefense;

// Владелец хитбоксов: логическая сущность или снаряд, его визуал и последнее
// состояние, снятое с этого визуала.
public sealed class HitboxOwner
{
    public int Id;
    public readonly List<HitboxObject> Hitboxes = new();
    public Node3D Visual;
    public Vector3 Position;
    public Vector3 Rotation;
    public bool HasPosition;
}

// Менеджер запросов: принимает векторы движения и смещения, обновляет
// хитбоксы по фактическому трансформу объекта сцены и передаёт состояние
// поведениям. Оптимизация выборки хитбоксов живёт здесь.
public partial class QueryManager : Node
{
    private const float MinimumSpeedForOptimization = 1f;
    private const string FpsLabelPrefix = "+/- Fps: ";

    // Минимальная толщина хитбокса цели и её максимальная скорость.
    // По ним выбирается максимальный интервал между обновлениями хитбокса.
    [Export] public float TargetMinHitboxThickness = 2f;
    [Export] public float TargetMaxSpeed = 10f;

    private readonly Dictionary<int, HitboxOwner> _owners = new();
    private Label _fpsLabel;
    private float _delta;

    public float CurrentFps => (float)Engine.GetFramesPerSecond();

    public PhysicManager Physics { get; set; }

    public override void _Ready()
    {
        CanvasLayer layer = new();
        AddChild(layer);

        _fpsLabel = new Label
        {
            Text = $"{FpsLabelPrefix}0",
            Scale = new Vector2(2f, 2f),
        };
        layer.AddChild(_fpsLabel);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_fpsLabel != null)
        {
            _fpsLabel.Text = $"{FpsLabelPrefix}{Mathf.RoundToInt(CurrentFps)}";
        }
    }

    public void SetDelta(float delta) => _delta = delta;

    public void RegisterOwner(int ownerId, IEnumerable<HitboxObject> hitboxes, Node3D visual)
    {
        HitboxOwner owner = new()
        {
            Id = ownerId,
            Visual = visual,
        };

        owner.Hitboxes.AddRange(hitboxes);
        _owners[ownerId] = owner;
    }

    public void UnregisterOwner(int ownerId)
    {
        _owners.Remove(ownerId);
    }

    // Фактическое положение объекта сцены, а не расчётная точка.
    public Vector3 GetPosition(int ownerId)
    {
        if (_owners.TryGetValue(ownerId, out HitboxOwner owner))
        {
            if (owner.Visual != null && GodotObject.IsInstanceValid(owner.Visual))
            {
                return owner.Visual.GlobalPosition;
            }

            return owner.Position;
        }

        return Vector3.Zero;
    }

    public Vector3 GetRotation(int ownerId)
    {
        if (_owners.TryGetValue(ownerId, out HitboxOwner owner))
        {
            if (owner.Visual != null && GodotObject.IsInstanceValid(owner.Visual))
            {
                return owner.Visual.GlobalRotation;
            }

            return owner.Rotation;
        }

        return Vector3.Zero;
    }

    // Запрос движения: горизонтальная скорость и поворот корпуса.
    public void RequestMovement(int ownerId, Vector3 velocity, Vector3 rotation)
    {
        Physics?.SetMovement(ownerId, velocity, rotation);
    }

    // Запрос смещения: раздвигание или отталкивание. Применяется физикой
    // через MoveAndCollide, поэтому стены и пол учитываются.
    public void AddDisplacement(int ownerId, Vector3 displacement)
    {
        Physics?.AddDisplacement(ownerId, displacement);
    }

    // Снимает фактический трансформ объекта сцены и обновляет хитбоксы.
    public void SyncFromSource(int ownerId)
    {
        if (!_owners.TryGetValue(ownerId, out HitboxOwner owner))
        {
            return;
        }

        Vector3 position = owner.Position;
        Vector3 rotation = owner.Rotation;

        if (owner.Visual != null && GodotObject.IsInstanceValid(owner.Visual))
        {
            position = owner.Visual.GlobalPosition;
            rotation = owner.Visual.GlobalRotation;
        }

        float speed = 0f;

        if (owner.Visual is CharacterBody3D body)
        {
            speed = body.Velocity.Length();
        }
        else if (owner.HasPosition && _delta > 0f)
        {
            speed = (position - owner.Position).Length() / _delta;
        }

        owner.Position = position;
        owner.Rotation = rotation;
        owner.HasPosition = true;

        foreach (HitboxObject hitbox in owner.Hitboxes)
        {
            UpdateHitbox(hitbox, speed);
        }
    }

    private void UpdateHitbox(HitboxObject hitbox, float speed)
    {
        hitbox.UpdatedThisFrame = false;

        if (hitbox.Source == null || !GodotObject.IsInstanceValid(hitbox.Source))
        {
            return;
        }

        if (!hitbox.Type.OptimizePosition)
        {
            ApplySourceTransform(hitbox, speed, 1);
            return;
        }

        if (hitbox.FramesUntilUpdate > 0)
        {
            hitbox.FramesUntilUpdate--;
            return;
        }

        int period = CalculateUpdatePeriod(speed);
        ApplySourceTransform(hitbox, speed, period);
        hitbox.FramesUntilUpdate = Mathf.Max(period - 1, 0);
    }

    private void ApplySourceTransform(HitboxObject hitbox, float speed, int period)
    {
        Transform3D transform = hitbox.Source.GlobalTransform;
        Basis rotationBasis = transform.Basis.Orthonormalized();
        Vector3 sourceCenter = transform * hitbox.Type.BaseCenter;
        Vector3 size = hitbox.BaseSize * transform.Basis.Scale.Abs();
        Vector3 position = sourceCenter;

        // Swept-хитбокс: между обновлениями объект проходит отрезок от прошлого
        // центра до текущего. Ставим хитбокс в середину отрезка и растягиваем
        // его на длину отрезка — тогда весь путь попадает внутрь одного OBB.
        if (hitbox.HasPosition && period > 1)
        {
            Vector3 movement = sourceCenter - hitbox.LastSourceCenter;
            float distance = movement.Length();

            if (distance > 0f)
            {
                position = hitbox.LastSourceCenter + movement * 0.5f;
                Vector3 localMovement = rotationBasis.Inverse() * movement;
                float maxComponent = Mathf.Max(
                    Mathf.Abs(localMovement.X),
                    Mathf.Max(Mathf.Abs(localMovement.Y), Mathf.Abs(localMovement.Z)));

                if (maxComponent > 0f)
                {
                    Vector3 direction = localMovement / maxComponent;
                    size += new Vector3(
                        Mathf.Abs(direction.X),
                        Mathf.Abs(direction.Y),
                        Mathf.Abs(direction.Z)) * distance;
                }
            }
        }

        hitbox.LastSourceCenter = sourceCenter;
        hitbox.Position = position;
        hitbox.Rotation = rotationBasis.GetEuler();
        hitbox.Size = size;
        hitbox.Speed = speed;
        hitbox.UpdatePeriod = period;
        hitbox.HasPosition = true;
        hitbox.UpdatedThisFrame = true;
    }

    private int CalculateUpdatePeriod(float speed)
    {
        if (speed < MinimumSpeedForOptimization)
        {
            return 1;
        }

        // Чтобы цель, пересекавшая путь, не успела уйти дальше половины своей
        // минимальной толщины, интервал не должен превышать это время.
        float maxInterval = TargetMinHitboxThickness / (2f * Mathf.Max(TargetMaxSpeed, 0.001f));
        float fps = Mathf.Max(CurrentFps, 1f);
        return Mathf.Max(Mathf.RoundToInt(fps * maxInterval), 1);
    }
}
