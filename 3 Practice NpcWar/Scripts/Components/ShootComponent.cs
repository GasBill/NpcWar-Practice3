using System;
using Godot;

namespace TowerDefense;

// Стрельба одной пулей по ближайшему противнику в радиусе.
[GlobalClass]
public partial class ShootComponent : Node, INpcComponent, INpcAttack
{
    private const float DefaultFireInterval = 1f;
    private const float DefaultShootRadius = 10f;

    // Папка со сценами и имя сцены пули.
    private const string SceneUsagePath = "res://SceneUsage";
    private const string BulletSceneName = "Bullet.tscn";

    // Сцена пули, которую создаёт и запускает компонент.
    [Export] public PackedScene BulletScene;
    [Export] public float FireInterval = DefaultFireInterval;
    [Export] public float ShootRadius = DefaultShootRadius;
    [Export] public int Weight = INpcComponent.DefaultWeight;

    private Npc _npc;
    private float _timeUntilShot;

    public override void _Ready()
    {
        _npc = GetParent<Npc>();

        // Сцена пули ищется в SceneUsage; поле в инспекторе — запасной вариант.
        PackedScene foundBulletScene = FindScene(BulletSceneName);

        if (foundBulletScene != null)
        {
            BulletScene = foundBulletScene;
        }

        _timeUntilShot = FireInterval;
        _npc.AcquireTarget();
    }

    // Вызывается общим таймером команды.
    public void TickAttack(float delta)
    {
        _timeUntilShot -= delta;

        if (_timeUntilShot > 0f)
        {
            return;
        }

        _timeUntilShot = FireInterval;
        Shoot();
    }

    public int GetWeight() => Weight;

    public Action<float>[] GetStatSetters()
    {
        return new Action<float>[]
        {
            value => FireInterval = value,
            value => ShootRadius = value,
        };
    }

    // Апгрейд увеличивает радиус стрельбы и уменьшает интервал —
    // стрельба становится чаще.
    public void ApplyUpgrades(int count)
    {
        float multiplier = Mathf.Pow(INpcComponent.UpgradeMultiplier, count);

        ShootRadius *= multiplier;
        FireInterval /= multiplier;
    }

    private void Shoot()
    {
        _npc.AcquireTarget();

        Npc target = _npc.Manager?.GetNpcById(_npc.TargetId);

        if (target == null || !target.IsAlive || target.Squad == _npc.Squad || BulletScene == null)
        {
            return;
        }

        if (_npc.GlobalPosition.DistanceSquaredTo(target.GlobalPosition) > ShootRadius * ShootRadius)
        {
            return;
        }

        Node bulletNode = BulletScene.Instantiate();

        if (bulletNode is not Node3D bullet)
        {
            bulletNode?.Free();
            return;
        }

        Node scene = GetTree().CurrentScene ?? GetTree().Root;
        scene.AddChild(bullet);

        bullet.GlobalPosition = _npc.GlobalPosition;

        // Задаём направление напрямую, без LookAt: пуля — сфера,
        // а LookAt падает на совпадающих/строго вертикальных направлениях.
        Vector3 direction = target.GlobalPosition - bullet.GlobalPosition;

        if (direction.LengthSquared() <= 0f)
        {
            direction = -_npc.GlobalTransform.Basis.Z;
        }

        if (bullet is Bullet bulletScript)
        {
            bulletScript.Launch(_npc, target, direction);
        }
    }

    // Ищет сцену по имени в папке SceneUsage.
    private static PackedScene FindScene(string fileName)
    {
        if (!DirAccess.DirExistsAbsolute(SceneUsagePath))
        {
            return null;
        }

        foreach (string file in DirAccess.GetFilesAt(SceneUsagePath))
        {
            if (file == fileName)
            {
                return GD.Load<PackedScene>($"{SceneUsagePath}/{file}");
            }
        }

        return null;
    }
}
