using System.Collections.Generic;
using Godot;

namespace TowerDefense;

// Скрипт запросов НПС: выбор цели, движение, регенерация и выстрелы.
// НПС не двигает сам себя — он считает новое положение и шлёт его в QueryManager.
public partial class NpcQuery : EntityQuery
{
    private const float ProjectileSpawnHeight = 1f;
    private const float MovementEpsilon = 0.0001f;

    public override void Update(float delta)
    {
        NpcManager manager = EntityManager.Npcs;

        if (manager == null)
        {
            return;
        }

        List<NpcData> npcs = manager.Npcs;

        for (int slot = 0; slot < npcs.Count; slot++)
        {
            NpcData data = npcs[slot];

            if (data == null || !data.IsAlive || data.Entity == null || !data.Entity.IsAlive)
            {
                continue;
            }

            UpdateRegen(data, delta);
            UpdateCombat(data, manager, delta);
        }
    }

    private void UpdateRegen(NpcData data, float delta)
    {
        if (data.RegenAmount <= 0f || data.RegenInterval <= 0f)
        {
            return;
        }

        data.RegenTimer += delta;

        if (data.RegenTimer < data.RegenInterval)
        {
            return;
        }

        data.RegenTimer -= data.RegenInterval;
        data.Health = Mathf.Min(data.Health + data.RegenAmount, data.MaxHealth);
        NpcBehavior.UpdateHealthLabel(EntityManager, data.Entity, data.Health);
    }

    private void UpdateCombat(NpcData data, NpcManager manager, float delta)
    {
        Entity entity = data.Entity;
        data.AttackTimer -= delta;

        if (!manager.HasValidTarget(data))
        {
            data.TargetSlot = manager.GetNearestEnemySlot(data);
        }

        Vector3 position = Queries.GetPosition(entity.Id);
        Vector3 rotation = Queries.GetRotation(entity.Id);
        Vector3 velocity = Vector3.Zero;

        if (manager.HasValidTarget(data))
        {
            NpcData target = manager.GetNpcBySlot(data.TargetSlot);
            Vector3 targetPosition = Queries.GetPosition(target.Entity.Id);
            Vector3 direction = targetPosition - position;
            direction.Y = 0f;

            if (direction.LengthSquared() > MovementEpsilon)
            {
                Vector3 normalized = direction.Normalized();

                if (!Hitboxes.Intersects(entity.Id, target.Entity.Id))
                {
                    velocity = normalized * data.Speed;
                }

                rotation = new Vector3(0f, Mathf.Atan2(-normalized.X, -normalized.Z), 0f);
            }

            float distance = position.DistanceTo(targetPosition);

            if (data.CanShoot && data.AttackTimer <= 0f && distance <= data.ShootRadius)
            {
                Shoot(data, targetPosition);
                data.AttackTimer = data.AttackInterval;
            }
        }

        // Движение уходит вектором скорости: тело двигает физика, а не запрос координат.
        Queries.RequestMovement(entity.Id, velocity, rotation);
    }

    private void Shoot(NpcData data, Vector3 targetPosition)
    {
        Vector3 origin = Queries.GetPosition(data.Entity.Id) + Vector3.Up * ProjectileSpawnHeight;
        Vector3 aimPoint = targetPosition + Vector3.Up * ProjectileSpawnHeight;
        Projectiles.Spawn(EntityTypeNames.Bullet, origin, aimPoint - origin, data.Squad, data.Slot);
    }
}
