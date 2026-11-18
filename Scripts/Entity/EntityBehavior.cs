using Godot;

namespace TowerDefense;

// Абстрактный обработчик типа: программист пишет только условия реакций
// на пересечения, урон и смерть. Геометрию и проверки делает HitboxManager.
public abstract partial class EntityBehavior : Node
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

    public virtual void OnSpawn(Entity entity) { }

    public virtual void OnIntersect(HitboxObject self, HitboxObject other, Vector3 intersection) { }

    // Вызывается, когда снаряд попал в этот хитбокс.
    public virtual void OnProjectileHit(HitboxObject self, HitboxObject projectile) { }

    public virtual void OnDeath(Entity entity) { }
}
