using Godot;

namespace TowerDefense;

// Пуля: попадает в damage-хитбокс НПС и передаёт удар его поведению.
// О стену пуля просто разбивается.
public partial class BulletBehavior : ProjectileBehavior
{
    public override void OnHit(HitboxObject self, HitboxObject other)
    {
        if (other.Type.Kind == EntityKind.Static)
        {
            Projectiles.Destroy(self.OwnerId);
            return;
        }

        if (other.Type.Name == EntityTypeNames.NpcDamage)
        {
            other.Type.Behavior?.OnProjectileHit(other, self);
        }
    }
}
