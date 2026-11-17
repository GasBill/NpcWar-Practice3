using Godot;

namespace TowerDefense;

// Обработчик общего NPC-хитбокса: только раздвигание НПС.
// Урон вынесен в NpcDamageProcessor и отдельный damage-хитбокс.
public partial class NpcHitboxProcessor : HitboxProcessor
{
    public override void ProcessHit(HitboxObject self, HitboxObject other, Vector3 intersection)
    {
        if (self.Node is Npc selfNpc && other.Node is Npc otherNpc)
        {
            ResolveNpcOverlap(selfNpc, otherNpc, intersection);
        }
    }

    // Раздвигает пару НПС на половину минимального вектора пересечения.
    // Пару обрабатывает только НПС с меньшим InstanceId.
    private static void ResolveNpcOverlap(Npc self, Npc other, Vector3 intersection)
    {
        if (self == null || other == null || self == other)
        {
            return;
        }

        if (self.GetInstanceId() > other.GetInstanceId())
        {
            return;
        }

        if (!self.IsAlive || !other.IsAlive)
        {
            return;
        }

        Vector3 halfIntersection = intersection * 0.5f;
        self.ApplySeparationVector(-halfIntersection);
        other.ApplySeparationVector(halfIntersection);
    }
}
