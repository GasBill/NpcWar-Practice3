using Godot;

namespace TowerDefense;

// Обработчик типа снаряда. У снаряда нет собственных целей для проверки:
// пересечение с NPC обрабатывает NpcHitboxProcessor.
public partial class BulletHitboxProcessor : HitboxProcessor
{
    public override void ProcessHit(HitboxObject self, HitboxObject other, Vector3 intersection)
    {
    }
}
