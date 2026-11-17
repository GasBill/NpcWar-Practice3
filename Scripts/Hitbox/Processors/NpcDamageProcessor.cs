using Godot;

namespace TowerDefense;

// Обработчик отдельного damage-хитбокса NPC.
// Только он наносит урон — и от снарядов, и в ближнем бою.
// Общий хитбокс NPC занимается только раздвиганием.
public partial class NpcDamageProcessor : HitboxProcessor
{
    public override void ProcessHit(HitboxObject self, HitboxObject other, Vector3 intersection)
    {
        if (!self.IsAlive || self.Node.GetParent() is not Npc npc)
        {
            return;
        }

        if (other.Node is Bullet bullet)
        {
            ProcessBullet(npc, bullet, other);
            return;
        }

        if (other.Node is Npc target)
        {
            ProcessMelee(npc, target);
        }
    }

    private static void ProcessBullet(Npc npc, Bullet bullet, HitboxObject bulletHitbox)
    {
        if (!npc.IsAlive || !bullet.IsAlive || bullet.HasHit)
        {
            return;
        }

        Npc shooter = bullet.Shooter;

        if ((shooter == null || !shooter.IsAlive) && bullet.ShooterId >= 0)
        {
            shooter = npc.Manager?.GetNpcById(bullet.ShooterId);
        }

        // Свой и дружественный снаряд не наносит урон.
        if (shooter != null && shooter.Squad == npc.Squad)
        {
            return;
        }

        HealthComponent health = npc.Health;

        if (health == null)
        {
            return;
        }

        Vector3 direction = bullet.Direction;

        if (direction.LengthSquared() <= 0f)
        {
            direction = npc.GlobalPosition - bullet.GlobalPosition;
        }

        Npc attacker = shooter != null && shooter.IsAlive ? shooter : null;

        health.TakeDamage(bullet.Damage, direction, attacker, bullet.ShooterId);
        bullet.Consume();
        bulletHitbox.IsAlive = false;
    }

    private static void ProcessMelee(Npc npc, Npc target)
    {
        if (!npc.IsAlive || !target.IsAlive || npc == target || target.Squad == npc.Squad)
        {
            return;
        }

        // Ближний бой бьёт только выбранную цель, а не всех, кто попал в хитбокс.
        if (npc.TargetId != target.Id)
        {
            return;
        }

        MeleeAttackComponent melee = npc.GetComponent<MeleeAttackComponent>();

        if (melee == null)
        {
            return;
        }

        melee.DealDamage(target);
    }
}
