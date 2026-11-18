using Godot;

namespace TowerDefense;

// Базовое поведение снаряда: свойства и реакция на попадание.
// Скорость и урон задаются прямо в скрипте-наследнике.
public abstract partial class ProjectileBehavior : EntityBehavior
{
    [Export] public float Speed = 20f;
    [Export] public float Damage = 10f;

    public override void OnIntersect(HitboxObject self, HitboxObject other, Vector3 intersection)
    {
        OnHit(self, other);
    }

    public virtual void OnHit(HitboxObject self, HitboxObject other) { }
}
