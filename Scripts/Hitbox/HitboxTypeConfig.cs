using System.IO;
using Godot;

namespace TowerDefense;

// Описание одного типа хитбоксов из списка TypesOfObjects.
// Порядок полей соответствует инструкции в HitboxManager:
// имя группы, сцена, скрипт с классом обработки, оптимизация позиции,
// оптимизация проверок и номера типов, с которыми нужно проверять столкновения.
[GlobalClass]
public partial class HitboxTypeConfig : Resource
{
    // Если имя не задано, менеджер возьмёт имя сцены без расширения.
    [Export] public string GroupName = string.Empty;

    // Сцена, у которой при инициализации считаются базовые размеры хитбокса.
    [Export] public PackedScene Scene;

    // Скрипт с классом, унаследованным от HitboxProcessor.
    [Export] public CSharpScript ProcessorScript;

    // 1 — обновлять положение хитбокса раз в несколько кадров
    // в зависимости от скорости объекта; 0 — каждый кадр.
    [Export] public bool OptimizePosition;

    // 1 — выполнять проверки только в момент обновления позиции;
    // 0 — выполнять проверки каждый кадр.
    [Export] public bool OptimizeChecks;

    // Если больше 0, проверки этого типа выполняются раз в указанное число кадров.
    [Export] public int CheckIntervalFrames;

    // Номера типов, с объектами которых нужно проверять пересечения.
    [Export] public Godot.Collections.Array<int> CollisionTypeNumbers = new();

    public string GetGroupNameOrDefault()
    {
        if (!string.IsNullOrWhiteSpace(GroupName))
        {
            return GroupName;
        }

        return Scene == null ? string.Empty : Path.GetFileNameWithoutExtension(Scene.ResourcePath);
    }
}
