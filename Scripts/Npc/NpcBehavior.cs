using System.Collections.Generic;
using Godot;

namespace TowerDefense;

// Поведение НПС: раздвигание, ближний бой, получение урона и смерть.
public partial class NpcBehavior : EntityBehavior
{
    // Делитель защиты в формуле урона: hp -= int(damage - def / 5).
    private const float DefenseDivisor = 5f;

    // Степень урона в векторе отталкивания.
    private const float KnockbackExponent = 0.5f;

    // Один материал на цвет команды, а не на каждого НПС.
    private static readonly Dictionary<Color, StandardMaterial3D> Materials = new();

    public override void OnSpawn(Entity entity)
    {
        if (entity?.Data is not NpcData data)
        {
            return;
        }

        ApplyColor(entity, data.Color);
        UpdateHealthLabel(EntityManager, entity, data.Health);
    }

    public override void OnIntersect(HitboxObject self, HitboxObject other, Vector3 intersection)
    {
        if (self.Type.Name == EntityTypeNames.Npc && other.Type.Name == EntityTypeNames.Npc)
        {
            Separate(self, other, intersection);
            return;
        }

        if (self.Type.Name == EntityTypeNames.NpcDamage && other.Type.Name == EntityTypeNames.Npc)
        {
            Melee(self, other);
        }
    }

    public override void OnProjectileHit(HitboxObject self, HitboxObject projectileHitbox)
    {
        NpcData target = GetData(self.OwnerId);
        Projectile projectile = EntityManager.Projectiles.Get(projectileHitbox.OwnerId);

        if (target == null || projectile == null || !target.IsAlive)
        {
            return;
        }

        if (projectile.ShooterSquad == target.Squad)
        {
            return;
        }

        ApplyDamage(target, projectile.Damage, projectile.Direction, projectile.ShooterSquad, projectile.ShooterSlot);
        EntityManager.Projectiles.Destroy(projectile.Id);
    }

    public static void UpdateHealthLabel(EntityManager entityManager, Entity entity, float health)
    {
        Node3D visual = entityManager.Physics.GetVisual(entity.Id);
        Label3D label = visual?.GetNodeOrNull<Label3D>("Hp");

        if (label != null)
        {
            label.Text = Mathf.Max(Mathf.CeilToInt(health), 0).ToString();
        }
    }

    private void Separate(HitboxObject self, HitboxObject other, Vector3 intersection)
    {
        if (self.OwnerId > other.OwnerId)
        {
            return;
        }

        Vector3 halfIntersection = intersection * 0.5f;
        halfIntersection.Y = 0f;

        Queries.AddDisplacement(self.OwnerId, -halfIntersection);
        Queries.AddDisplacement(other.OwnerId, halfIntersection);
    }

    private void Melee(HitboxObject self, HitboxObject other)
    {
        NpcData attacker = GetData(self.OwnerId);
        NpcData target = GetData(other.OwnerId);

        if (attacker == null || target == null || !attacker.CanMelee || !target.IsAlive)
        {
            return;
        }

        if (attacker.TargetSlot != target.Slot || attacker.Squad == target.Squad)
        {
            return;
        }

        Vector3 direction = Queries.GetPosition(target.Entity.Id) - Queries.GetPosition(attacker.Entity.Id);
        ApplyDamage(target, attacker.Damage, direction, attacker.Squad, attacker.Slot);
    }

    private void ApplyDamage(NpcData target, float damage, Vector3 direction, int attackerSquad, int attackerSlot)
    {
        if (!target.IsAlive)
        {
            return;
        }

        target.Health -= (int)(damage - target.Defense / DefenseDivisor);
        UpdateHealthLabel(EntityManager, target.Entity, target.Health);

        float maxComponent = Mathf.Max(
            Mathf.Abs(direction.X),
            Mathf.Max(Mathf.Abs(direction.Y), Mathf.Abs(direction.Z)));

        if (maxComponent > 0f)
        {
            Vector3 pushVector = direction / maxComponent * Mathf.Pow(damage, KnockbackExponent);
            pushVector.Y = 0f;
            Queries.AddDisplacement(target.Entity.Id, pushVector);
        }

        if (attackerSquad != target.Squad)
        {
            target.TargetSlot = attackerSlot;
        }

        if (target.Health <= 0f)
        {
            EntityManager.Npcs?.RemoveNpc(target);
        }
    }

    private NpcData GetData(int ownerId)
    {
        return EntityManager.GetEntity(ownerId)?.Data as NpcData;
    }

    private void ApplyColor(Entity entity, Color color)
    {
        Node3D visual = Physics.GetVisual(entity.Id);

        if (visual == null)
        {
            return;
        }

        if (!Materials.TryGetValue(color, out StandardMaterial3D material))
        {
            material = new StandardMaterial3D
            {
                AlbedoColor = color,
            };
            Materials[color] = material;
        }

        foreach (Node child in visual.FindChildren("*", "GeometryInstance3D", true, false))
        {
            if (child is GeometryInstance3D geometry && child is not Label3D)
            {
                geometry.MaterialOverride = material;
            }
        }

        Label3D label = visual.GetNodeOrNull<Label3D>("Hp");

        if (label != null)
        {
            label.Modulate = color;
        }
    }
}
