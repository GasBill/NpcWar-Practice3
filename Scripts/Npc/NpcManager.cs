using System;
using System.Collections.Generic;
using Godot;

namespace TowerDefense;

// Менеджер НПС: очки команд, спавн в кольцевых секторах и список НПС.
// Сами НПС — логические Entity, которыми управляют NpcQuery и NpcBehavior.
public partial class NpcManager : Node3D
{
    private const float FullCircleDegrees = 360f;
    private const int DefaultTeamCount = 2;
    private const int DefaultGameDifficulty = 30;

    // Стоимость в очках.
    private const int BaseNpcCost = 2;
    private const int InterfaceCost = 3;
    private const int UpgradeCost = 1;

    // Базовые статы НПС до апгрейдов.
    private const float BaseHealth = 300f;
    private const float BaseDefense = 0f;
    private const float BaseRegenAmount = 0f;
    private const float BaseRegenInterval = 1f;
    private const float BaseSpeed = 5f;
    private const float BaseDamage = 10f;
    private const float BaseAttackInterval = 1f;
    private const float BaseShootRadius = 10f;
    private const float UpgradeMultiplier = 1.2f;
    private const int MaxUpgradesPerInterface = 3;
    private const float MinimumRegenInterval = 0.1f;

    // Луч для поиска поверхности спавна.
    private const float RaycastHeight = 100f;
    private const uint SpawnSurfaceMask = 1;

    private const string NpcCountLabelPath = "CanvasLayer/Count";

    [ExportCategory("Инструкция по очкам")]
    [Export(PropertyHint.MultilineText, "")]
    public string PointsInstruction =
        "Difficulty = points;\n" +
        "interface's upgrade *= 1.2 for every stat;\n" +
        "npc = 2 points;\n" +
        "interface = 3 points;\n" +
        "interface's upgrade = 1 point;\n" +
        "interface can have max 3 upgrades;";

    [ExportCategory("Параметры")]
    [Export] public int MaxComponentsPerNpc = 3;
    [Export] public int MinComponentsPerNpc = 2;
    [Export] public int Difficulty;
    [Export] public int GameDifficulty = DefaultGameDifficulty;
    [Export] public int TeamCount = DefaultTeamCount;
    [Export] public float SpawnRadius;
    [Export] public float InnerRadius;
    [Export] public float SpawnHeightAboveSurface = 0.02f;
    [Export] public Godot.Collections.Array<float> SpawnSectorAngles = new();

    public EntityManager Entities { get; private set; }

    // Массив НПС: слот = номер НПС, после смерти слот освобождается.
    public List<NpcData> Npcs { get; } = new();

    private int[] _teamPoints = Array.Empty<int>();
    private Color[] _teamColors = Array.Empty<Color>();
    private Label _npcCountLabel;
    private readonly RandomNumberGenerator _random = new();

    public override void _Ready()
    {
        _random.Randomize();
        Entities = GetParent()?.GetNodeOrNull<EntityManager>("EntityManager");
        _npcCountLabel = GetNodeOrNull<Label>(NpcCountLabelPath);
        UpdateTeamData();
        InitTeamPoints();
        Callable.From(FillTeams).CallDeferred();
    }

    public override void _Process(double delta)
    {
        if (_npcCountLabel == null)
        {
            return;
        }

        int nonNullCount = 0;

        foreach (NpcData data in Npcs)
        {
            if (data != null)
            {
                nonNullCount++;
            }
        }

        _npcCountLabel.Text = $"Npcs: {nonNullCount}";
    }

    public NpcData GetNpcBySlot(int slot)
    {
        if (slot < 0 || slot >= Npcs.Count)
        {
            return null;
        }

        return Npcs[slot];
    }

    public Vector3 GetPosition(NpcData data)
    {
        return data?.Entity != null ? Entities.Queries.GetPosition(data.Entity.Id) : Vector3.Zero;
    }

    public bool HasValidTarget(NpcData data)
    {
        NpcData target = GetNpcBySlot(data.TargetSlot);
        return target != null && target.IsAlive && target.Squad != data.Squad;
    }

    // Ближайший враг: позиции берутся из менеджера запросов, а не из объектов.
    public int GetNearestEnemySlot(NpcData data)
    {
        int nearestSlot = -1;
        float nearestDistance = float.MaxValue;
        Vector3 position = GetPosition(data);

        for (int slot = 0; slot < Npcs.Count; slot++)
        {
            NpcData other = Npcs[slot];

            if (other == null || other == data || !other.IsAlive || other.Squad == data.Squad)
            {
                continue;
            }

            float distance = position.DistanceSquaredTo(GetPosition(other));

            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestSlot = slot;
            }
        }

        return nearestSlot;
    }

    public void RemoveNpc(NpcData data)
    {
        if (data == null || !data.IsAlive)
        {
            return;
        }

        data.IsAlive = false;

        if (data.Slot >= 0 && data.Slot < Npcs.Count && Npcs[data.Slot] == data)
        {
            Npcs[data.Slot] = null;
        }

        if (data.Squad >= 0 && data.Squad < _teamPoints.Length)
        {
            _teamPoints[data.Squad] += data.Cost;
        }

        int squad = data.Squad;
        Entity entity = data.Entity;

        if (entity != null && Entities != null)
        {
            Entities.Destroy(entity);
        }

        Callable.From(() => SpawnNpc(squad)).CallDeferred();
    }

    private void FillTeams()
    {
        for (int squad = 0; squad < TeamCount; squad++)
        {
            while (SpawnNpc(squad) != null)
            {
            }
        }
    }

    // Создаёт НПС команды squad, если команда может оплатить его очки.
    public NpcData SpawnNpc(int squad)
    {
        if (Entities == null || squad < 0 || squad >= TeamCount)
        {
            return null;
        }

        EnsureTeamPoints();

        if (_teamColors.Length != TeamCount)
        {
            UpdateTeamData();
        }

        int budget = GetRandomBudget(squad);

        if (budget < 0)
        {
            return null;
        }

        int slot = FindFreeSlot();

        if (slot < 0)
        {
            return null;
        }

        Vector2 spawnXz = GetRandomSpawnPosition(squad);
        float surfaceY = ResolveSurfaceY(spawnXz.X, spawnXz.Y);
        float bottomOffset = Entities.GetTypeBottomOffset(EntityTypeNames.Npc);
        Vector3 position = new(spawnXz.X, surfaceY + bottomOffset + SpawnHeightAboveSurface, spawnXz.Y);

        Entity entity = Entities.Spawn(
            EntityTypeNames.Npc,
            position,
            Vector3.Zero,
            data => ConfigureNpc((NpcData)data, budget, slot, squad));

        if (entity?.Data is not NpcData spawned)
        {
            Npcs[slot] = null;
            return null;
        }

        Npcs[slot] = spawned;
        _teamPoints[squad] -= budget;
        return spawned;
    }

    // Данные НПС готовятся до OnSpawn, чтобы визуал сразу получил цвет и здоровье.
    private void ConfigureNpc(NpcData data, int budget, int slot, int squad)
    {
        FillNpcData(data, budget);
        data.Slot = slot;
        data.Squad = squad;
        data.Cost = budget;
        data.Color = _teamColors[squad];
        data.IsAlive = true;
        data.Health = data.MaxHealth;
        data.AttackTimer = data.AttackInterval;
        data.TargetSlot = -1;
    }

    // Заполняет данные НПС статами, купленными на бюджет.
    // Интерфейсы — это бывшие компоненты: здоровье, движение, стрельба, ближний бой.
    private void FillNpcData(NpcData data, int budget)
    {
        int cost = BaseNpcCost;
        List<NpcInterface> interfaces = new();

        interfaces.Add(NpcInterface.Health);
        cost += InterfaceCost;

        NpcInterface attack = _random.Randf() < 0.5f ? NpcInterface.Shoot : NpcInterface.Melee;
        interfaces.Add(attack);
        cost += InterfaceCost;

        NpcInterface alternate = attack == NpcInterface.Shoot ? NpcInterface.Melee : NpcInterface.Shoot;
        NpcInterface[] extras = { NpcInterface.Move, alternate };

        while (interfaces.Count < MaxComponentsPerNpc && cost + InterfaceCost <= budget)
        {
            NpcInterface pick = extras[_random.RandiRange(0, extras.Length - 1)];

            if (interfaces.Contains(pick))
            {
                break;
            }

            interfaces.Add(pick);
            cost += InterfaceCost;
        }

        while (interfaces.Count < MinComponentsPerNpc && cost + InterfaceCost <= budget)
        {
            if (!interfaces.Contains(NpcInterface.Move))
            {
                interfaces.Add(NpcInterface.Move);
                cost += InterfaceCost;
            }
            else
            {
                break;
            }
        }

        int[] upgrades = new int[interfaces.Count];
        bool upgraded = true;

        while (upgraded && cost + UpgradeCost <= budget)
        {
            upgraded = false;

            for (int i = 0; i < interfaces.Count && cost + UpgradeCost <= budget; i++)
            {
                if (upgrades[i] >= MaxUpgradesPerInterface)
                {
                    continue;
                }

                upgrades[i]++;
                cost += UpgradeCost;
                upgraded = true;
            }
        }

        data.MaxHealth = BaseHealth;
        data.Health = BaseHealth;
        data.Defense = BaseDefense;
        data.RegenAmount = BaseRegenAmount;
        data.RegenInterval = BaseRegenInterval;
        data.Speed = BaseSpeed;
        data.Damage = BaseDamage;
        data.AttackInterval = BaseAttackInterval;
        data.ShootRadius = BaseShootRadius;
        data.CanShoot = interfaces.Contains(NpcInterface.Shoot);
        data.CanMelee = interfaces.Contains(NpcInterface.Melee);

        for (int i = 0; i < interfaces.Count; i++)
        {
            float multiplier = Mathf.Pow(UpgradeMultiplier, upgrades[i]);

            switch (interfaces[i])
            {
                case NpcInterface.Health:
                    data.MaxHealth *= multiplier;
                    data.Defense *= multiplier;
                    data.RegenAmount *= multiplier;
                    data.RegenInterval = Mathf.Max(data.RegenInterval / multiplier, MinimumRegenInterval);
                    break;

                case NpcInterface.Move:
                    data.Speed *= multiplier;
                    break;

                case NpcInterface.Shoot:
                    data.Damage *= multiplier;
                    data.AttackInterval /= multiplier;
                    data.ShootRadius *= multiplier;
                    break;

                case NpcInterface.Melee:
                    data.Damage *= multiplier;
                    data.AttackInterval /= multiplier;
                    break;
            }
        }

        data.Health = data.MaxHealth;
        data.Interfaces = interfaces.ToArray();
        data.Upgrades = upgrades;
    }

    // Очки, которые команда может выделить на следующего НПС.
    private int GetRandomBudget(int squad)
    {
        int minInterfaces = Mathf.Max(MinComponentsPerNpc, 2);
        int minBudget = BaseNpcCost + minInterfaces * InterfaceCost;
        minBudget = Mathf.Max(minBudget, Difficulty / 2);

        int maxBudget = Mathf.Max(Difficulty, minBudget);
        int available = _teamPoints[squad];

        if (available < minBudget)
        {
            return -1;
        }

        return _random.RandiRange(minBudget, Mathf.Min(maxBudget, available));
    }

    private int FindFreeSlot()
    {
        for (int i = 0; i < Npcs.Count; i++)
        {
            if (Npcs[i] == null)
            {
                return i;
            }
        }

        Npcs.Add(null);
        return Npcs.Count - 1;
    }

    private void InitTeamPoints()
    {
        int teams = Mathf.Max(TeamCount, 0);
        _teamPoints = new int[teams];

        for (int i = 0; i < teams; i++)
        {
            _teamPoints[i] = GameDifficulty;
        }
    }

    private void EnsureTeamPoints()
    {
        if (_teamPoints.Length != Mathf.Max(TeamCount, 0))
        {
            InitTeamPoints();
        }
    }

    // Команды делят окружность на равные сектора, цвета идут по цветовому кругу.
    private void UpdateTeamData()
    {
        SpawnSectorAngles.Clear();

        if (TeamCount <= 0)
        {
            _teamColors = Array.Empty<Color>();
            return;
        }

        float sectorAngle = FullCircleDegrees / TeamCount;

        for (int i = 0; i < TeamCount; i++)
        {
            SpawnSectorAngles.Add(i * sectorAngle);
        }

        _teamColors = new Color[TeamCount];

        for (int i = 0; i < TeamCount; i++)
        {
            float hue = Mathf.PosMod(SpawnSectorAngles[i], FullCircleDegrees) / FullCircleDegrees;
            _teamColors[i] = Color.FromHsv(hue, 1f, 1f, 1f);
        }
    }

    // Случайная точка в кольцевом секторе команды.
    private Vector2 GetRandomSpawnPosition(int squad)
    {
        float centerAngle = Mathf.DegToRad(SpawnSectorAngles[squad]);
        float halfSector = Mathf.DegToRad(FullCircleDegrees / TeamCount / 2f);
        float angle = centerAngle + _random.RandfRange(-halfSector, halfSector);

        float inner = Mathf.Min(InnerRadius, SpawnRadius);
        float outer = Mathf.Max(InnerRadius, SpawnRadius);
        float radius = Mathf.Sqrt(_random.RandfRange(inner * inner, outer * outer));

        return new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
    }

    // Верх пола под точкой спавна: теперь это луч физики, а не группа сцены.
    private float ResolveSurfaceY(float x, float z)
    {
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(
            new Vector3(x, RaycastHeight, z),
            new Vector3(x, -RaycastHeight, z),
            SpawnSurfaceMask);
        Godot.Collections.Dictionary hit = space.IntersectRay(query);

        if (hit != null && hit.Count > 0 && hit.ContainsKey("position"))
        {
            return hit["position"].AsVector3().Y;
        }

        return 0f;
    }
}
