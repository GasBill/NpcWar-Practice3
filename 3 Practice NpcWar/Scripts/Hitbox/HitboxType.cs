using System.Collections.Generic;
using Godot;

namespace TowerDefense;

// Тип хитбоксов, собранный из HitboxTypeConfig при инициализации менеджера.
// Хранит вычисленные по сцене базовые размеры и список объектов этого типа.
public sealed class HitboxType
{
    public string GroupName;
    public PackedScene Scene;
    public bool OptimizePosition;
    public bool OptimizeChecks;
    public int CheckIntervalFrames;
    public int[] CollisionTypeNumbers;
    public Vector3 BaseCenter;
    public Vector3 BaseSize;
    public HitboxProcessor Processor;
    public bool IsValid;

    // Список объектов типа; он же лежит в HitboxManager.HitboxesByType.
    public readonly List<HitboxObject> Objects = new();
}
