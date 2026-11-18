using Godot;

namespace TowerDefense;

// Обработчик типа снаряда. Снаряд сам не решает, кому наносить урон:
// он каждый кадр проверяет damage-хитбоксы НПС и передаёт пересечение
// их обработчику, чтобы урон и удаление пули оставались в NpcDamageProcessor.
public partial class BulletHitboxProcessor : HitboxProcessor
{
    public override void ProcessHit(HitboxObject self, HitboxObject other, Vector3 intersection)
    {
        other.Type.Processor?.ProcessHit(other, self, intersection);
    }
}
