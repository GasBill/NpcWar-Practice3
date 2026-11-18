using Godot;

namespace TowerDefense;

// Скрипт, который считает движение объекта и отправляет запросы менеджеру.
// Один экземпляр на тип: он обновляет все объекты этого типа.
public abstract partial class EntityQuery : Node
{
    public EntityManager EntityManager { get; private set; }
    public HitboxManager Hitboxes => EntityManager.Hitboxes;
    public QueryManager Queries => EntityManager.Queries;
    public PhysicManager Physics => EntityManager.Physics;
    public PhysicProjectileManager Projectiles => EntityManager.Projectiles;

    public virtual void Initialize(EntityManager entityManager)
    {
        EntityManager = entityManager;
    }

    public abstract void Update(float delta);
}
