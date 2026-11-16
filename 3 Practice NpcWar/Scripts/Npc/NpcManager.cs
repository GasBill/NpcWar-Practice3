using System;
using System.Collections.Generic;
using Godot;

namespace TowerDefense;

// Менеджер НПС: прикрепляется к Node3D, создаёт НПС через Instantiate.
// НПС из разных команд (Squad) считаются противниками; спавн идёт
// в кольцевых секторах, на которые окружность делят команды.
//
// Инструкция по очкам:
// Difficulty = points;
// interface's upgrade *= 1.2 for every stat;
// npc = 2 points;
// interface = 3 points;
// interface's upgrade = 1 point;
// interface can have max 3 upgrades;
public partial class NpcManager : Node3D
{
    // Полный круг в градусах: используется для секторов и цветов команд.
    private const float FullCircleDegrees = 360f;

    // Количество команд по умолчанию.
    private const int DefaultTeamCount = 2;

    // Один таймер атак на команду, а не на каждого НПС.
    private const float TeamAttackTickInterval = 0.1f;

    // Очки на команду по умолчанию.
    private const int DefaultGameDifficulty = 30;

    // Стоимость в очках.
    private const int BaseNpcCost = 2;
    private const int InterfaceCost = 3;
    private const int UpgradeCost = 1;

    // Папка со сценами и имя сцены НПС.
    private const string SceneUsagePath = "res://SceneUsage";
    private const string NpcSceneName = "Npc.tscn";

    // Папка со скриптами интерфейсов (компонентов).
    private const string ComponentsPath = "res://Scripts/Components";

    // Путь к Label с количеством НПС на CanvasLayer.
    private const string NpcCountLabelPath = "CanvasLayer/Count";

    // Инструкция по очкам, отображается прямо в инспекторе Godot.
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

    // Сколько интерфейсов максимум можно присвоить одному НПС.
    [Export] public int MaxComponentsPerNpc = 3;

    // Минимальное количество интерфейсов у одного НПС.
    [Export] public int MinComponentsPerNpc = 2;

    // Очки на одного НПС: распределяются между интерфейсами и их апгрейдами.
    [Export] public int Difficulty;

    // Очки на каждую команду: пока они есть, команда спавнит НПС.
    [Export] public int GameDifficulty = DefaultGameDifficulty;

    // Количество команд: влияет на сектора спавна и цвета НПС.
    [Export] public int TeamCount = DefaultTeamCount;

    // Внешний радиус кольца, в котором спавнятся НПС.
    [Export] public float SpawnRadius;

    // Внутренний радиус кольца: ближе него НПС не спавнятся.
    [Export] public float InnerRadius;

    // Углы (в градусах) центров секторов спавна команд на окружности.
    // Заполняются равномерно при инициализации.
    [Export] public Godot.Collections.Array<float> SpawnSectorAngles = new();

    // C#-скрипты компонентов, из которых менеджер собирает НПС.
    [Export] public Godot.Collections.Array<CSharpScript> ComponentScripts = new();

    // Сцена НПС, экземпляр которой создаёт менеджер.
    [Export] public PackedScene NpcScene;

    // Массив ссылок на НПС; слоты переиспользуются после смерти.
    private readonly List<Npc> _npcs = new();

    // Универсальный двумерный список: [[Id, link], [Id, link], ...].
    // link — адрес Vector3* в неуправляемой памяти, переданный при спавне.
    private readonly List<List<object>> _npcLinks = new();

    // Скрипты интерфейсов, загруженные из ComponentsPath.
    private readonly List<CSharpScript> _componentScripts = new();

    // Очки, оставшиеся у каждой команды.
    private int[] _teamPoints = System.Array.Empty<int>();

    // Цвет каждой команды, вычисленный при инициализации.
    private Color[] _teamColors = System.Array.Empty<Color>();

    // Label с количеством живых НПС.
    private Label _npcCountLabel;

    private readonly RandomNumberGenerator _random = new();

    public override void _Ready()
    {
        _random.Randomize();
        _npcCountLabel = GetNodeOrNull<Label>(NpcCountLabelPath);

        // Сцена НПС ищется в SceneUsage; поле в инспекторе — запасной вариант.
        PackedScene foundNpcScene = FindScene(NpcSceneName);

        if (foundNpcScene != null)
        {
            NpcScene = foundNpcScene;
        }

        UpdateTeamData();
        InitTeamPoints();
        LoadComponentScripts();
        CreateTeamAttackTimers();

        // Команды начинают тратить очки после построения сцены.
        Callable.From(FillTeams).CallDeferred(System.Array.Empty<Variant>());
    }

    // Создаёт по одному таймеру атак на каждую команду.
    private void CreateTeamAttackTimers()
    {
        for (int squad = 0; squad < TeamCount; squad++)
        {
            int squadIndex = squad;
            Timer timer = new()
            {
                Name = $"TeamAttackTimer{squadIndex}",
                WaitTime = TeamAttackTickInterval,
                OneShot = false,
                Autostart = true,
            };
            timer.Timeout += () => TickTeamAttacks(squadIndex);
            AddChild(timer);
        }
    }

    // Тикает атаки только НПС указанной команды.
    private void TickTeamAttacks(int squad)
    {
        // Индексный обход: атака может убить НПС и изменить _npcs[id].
        for (int i = 0; i < _npcs.Count; i++)
        {
            Npc npc = _npcs[i];

            if (npc == null || !npc.IsAlive || npc.Squad != squad)
            {
                continue;
            }

            npc.TickAttacks(TeamAttackTickInterval);
        }
    }

    // Каждый кадр считает живых НПС и выводит число в Label на CanvasLayer.
    public override void _Process(double delta)
    {
        PrintNpcLinks();

        if (_npcCountLabel == null)
        {
            return;
        }

        int aliveCount = 0;

        foreach (Npc npc in _npcs)
        {
            if (npc != null && npc.IsAlive)
            {
                aliveCount++;
            }
        }

        _npcCountLabel.Text = $"Npcs: {aliveCount}";
    }

    // Выводит двумерный список [[Id, link], ...] в консоль каждый кадр.
    private void PrintNpcLinks()
    {
        System.Text.StringBuilder builder = new();
        builder.Append('[');
        bool first = true;

        for (int i = 0; i < _npcLinks.Count; i++)
        {
            List<object> pair = _npcLinks[i];

            if (pair == null)
            {
                continue;
            }

            if (!first)
            {
                builder.Append(", ");
            }

            first = false;
            IntPtr address = (IntPtr)pair[1];
            builder.Append($"[{pair[0]}, 0x{address.ToInt64():X}]");
        }

        builder.Append(']');
        GD.Print(builder.ToString());
    }

    // Getter адреса памяти, где лежит позиция противника.
    public bool TryGetNpcPositionLink(int id, int squad, out IntPtr link)
    {
        link = IntPtr.Zero;

        if (id < 0 || id >= _npcs.Count || id >= _npcLinks.Count)
        {
            return false;
        }

        Npc npc = _npcs[id];

        if (npc == null || !npc.IsAlive || npc.Squad == squad)
        {
            return false;
        }

        List<object> pair = _npcLinks[id];

        if (pair == null)
        {
            return false;
        }

        link = pair[1] is IntPtr address ? address : IntPtr.Zero;
        return link != IntPtr.Zero;
    }

    // Регистрирует [Id, адрес памяти] при спавне НПС.
    private unsafe void RegisterNpcPositionLink(Npc npc)
    {
        while (_npcLinks.Count <= npc.Id)
        {
            _npcLinks.Add(null);
        }

        _npcLinks[npc.Id] = new List<object> { npc.Id, (IntPtr)npc.Position };
    }

    // Убирает запись [Id, link] при смерти НПС.
    private void UnregisterNpcPositionLink(int id)
    {
        if (id >= 0 && id < _npcLinks.Count)
        {
            _npcLinks[id] = null;
        }
    }

    // Ближайший враг по id: только математика, без списков и ссылок.
    public int GetNearestEnemyId(Npc npc)
    {
        int nearestId = -1;
        float nearestDistance = float.MaxValue;

        for (int i = 0; i < _npcs.Count; i++)
        {
            Npc other = _npcs[i];

            if (other == null || other == npc || !other.IsAlive || other.Squad == npc.Squad)
            {
                continue;
            }

            float distance = npc.GlobalPosition.DistanceSquaredTo(other.GlobalPosition);

            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestId = i;
            }
        }

        return nearestId;
    }

    // Спавнит НПС в командах, пока у них есть очки и свободные слоты.
    private void FillTeams()
    {
        if (NpcScene == null)
        {
            GD.PushError("NpcManager: не задана сцена НПС (NpcScene).");
            return;
        }

        for (int squad = 0; squad < TeamCount; squad++)
        {
            while (SpawnNpc(squad) != null)
            {
            }
        }

        // После заполнения всех команд один раз раздаём цели.
        foreach (Npc npc in _npcs)
        {
            if (npc != null && npc.IsAlive)
            {
                npc.AcquireTarget();
            }
        }
    }

    // Создаёт НПС команды squad, если команда может оплатить его очки.
    public Npc SpawnNpc(int squad)
    {
        if (NpcScene == null)
        {
            GD.PushError("NpcManager: не задана сцена НПС (NpcScene).");
            return null;
        }

        if (squad < 0 || squad >= TeamCount)
        {
            GD.PushError($"NpcManager: нет команды с номером {squad}.");
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

        int npcId = FindFreeNpcSlot();

        if (npcId < 0)
        {
            return null;
        }

        List<INpcComponent> components = BuildComponents(budget);

        if (components == null)
        {
            return null;
        }

        Npc npc = NpcScene.Instantiate<Npc>();

        // Стоимость НПС — выбранное количество очков; при смерти они вернутся.
        npc.Cost = budget;
        _teamPoints[squad] -= budget;
        _npcs[npcId] = npc;

        Vector2 spawnPositionXz = GetRandomSpawnPosition(squad);
        npc.Initialize(squad, npcId, this, spawnPositionXz, _teamColors[squad], components);

        AddChild(npc);
        RegisterNpcPositionLink(npc);

        return npc;
    }

    // Возвращает НПС по его номеру (id равен индексу в массиве).
    public Npc GetNpcById(int id)
    {
        if (id < 0 || id >= _npcs.Count)
        {
            return null;
        }

        return _npcs[id];
    }

    // Убирает НПС из массива, возвращает команде его очки
    // и пытается создать замену.
    public void RemoveNpc(Npc npc)
    {
        if (npc == null)
        {
            return;
        }

        npc.IsAlive = false;
        EnsureTeamPoints();

        if (npc.Id >= 0 && npc.Id < _npcs.Count && _npcs[npc.Id] == npc)
        {
            _npcs[npc.Id] = null;
            UnregisterNpcPositionLink(npc.Id);
        }

        if (npc.Squad >= 0 && npc.Squad < _teamPoints.Length)
        {
            _teamPoints[npc.Squad] += npc.Cost;
        }

        int squad = npc.Squad;
        npc.QueueFree();

        // Замена создаётся в конце кадра: массив нельзя менять прямо
        // во время физики или срабатывания таймера.
        Callable.From(() =>
        {
            SpawnNpc(squad);
        }).CallDeferred(System.Array.Empty<Variant>());
    }

    // Создаёт по одному экземпляру из каждого C#-скрипта интерфейса.
    private List<INpcComponent> CreateComponentCandidates()
    {
        if (_componentScripts.Count == 0)
        {
            LoadComponentScripts();
        }

        List<INpcComponent> candidates = new();

        foreach (CSharpScript componentScript in _componentScripts)
        {
            if (componentScript == null)
            {
                continue;
            }

            // У интерфейса (INpcComponent.cs) нет класса, поэтому он пропускается.
            if (string.IsNullOrEmpty(componentScript.GetInstanceBaseType().ToString()))
            {
                continue;
            }

            Node componentNode = componentScript.New(System.Array.Empty<Variant>()).As<Node>();

            if (componentNode is INpcComponent component)
            {
                candidates.Add(component);
            }
            else
            {
                componentNode?.Free();
            }
        }

        return candidates;
    }

    // Загружает все C#-скрипты интерфейсов из папки Components.
    private void LoadComponentScripts()
    {
        _componentScripts.Clear();

        if (DirAccess.DirExistsAbsolute(ComponentsPath))
        {
            foreach (string file in DirAccess.GetFilesAt(ComponentsPath))
            {
                if (!file.EndsWith(".cs"))
                {
                    continue;
                }

                CSharpScript script = GD.Load<CSharpScript>($"{ComponentsPath}/{file}");

                if (script != null)
                {
                    _componentScripts.Add(script);
                }
            }
        }

        // Если папка пуста — используем массив, назначенный в инспекторе.
        if (_componentScripts.Count == 0)
        {
            foreach (CSharpScript script in ComponentScripts)
            {
                if (script != null)
                {
                    _componentScripts.Add(script);
                }
            }
        }
    }

    // Собирает НПС в рамках бюджета: здоровье и атака обязательны,
    // остальные интерфейсы и апгрейды покупаются на оставшиеся очки.
    private List<INpcComponent> BuildComponents(int budget)
    {
        List<INpcComponent> candidates = CreateComponentCandidates();
        List<INpcComponent> selected = new();
        int cost = BaseNpcCost;

        // 1. Здоровье обязательно.
        int healthIndex = candidates.FindIndex(candidate => candidate is HealthComponent);

        if (healthIndex < 0 || cost + InterfaceCost > budget)
        {
            FreeComponents(candidates);
            return null;
        }

        selected.Add(candidates[healthIndex]);
        candidates.RemoveAt(healthIndex);
        cost += InterfaceCost;

        // 2. Любая атака обязательна: ближний бой или стрельба.
        int attackIndex = candidates.FindIndex(candidate =>
            candidate is MeleeAttackComponent || candidate is ShootComponent);

        if (attackIndex < 0 || cost + InterfaceCost > budget)
        {
            FreeComponents(candidates);
            FreeComponents(selected);
            return null;
        }

        selected.Add(candidates[attackIndex]);
        candidates.RemoveAt(attackIndex);
        cost += InterfaceCost;

        // 3. Остальные интерфейсы — по весам, пока хватает очков.
        while (selected.Count < MaxComponentsPerNpc && candidates.Count > 0 &&
               cost + InterfaceCost <= budget)
        {
            int index = PickWeightedIndex(candidates);
            selected.Add(candidates[index]);
            candidates.RemoveAt(index);
            cost += InterfaceCost;
        }

        // 4. Добираем до минимума, если очки позволяют.
        while (selected.Count < MinComponentsPerNpc && candidates.Count > 0 &&
               cost + InterfaceCost <= budget)
        {
            selected.Add(candidates[0]);
            candidates.RemoveAt(0);
            cost += InterfaceCost;
        }

        // 5. Оставшиеся очки тратим на апгрейды интерфейсов.
        int[] upgrades = new int[selected.Count];
        bool upgraded = true;

        while (upgraded && cost + UpgradeCost <= budget)
        {
            upgraded = false;

            for (int i = 0; i < selected.Count && cost + UpgradeCost <= budget; i++)
            {
                if (upgrades[i] >= INpcComponent.MaxUpgrades)
                {
                    continue;
                }

                upgrades[i]++;
                cost += UpgradeCost;
                upgraded = true;
            }
        }

        for (int i = 0; i < selected.Count; i++)
        {
            selected[i].ApplyUpgrades(upgrades[i]);
        }

        FreeComponents(candidates);
        return selected;
    }

    // Индекс компонента, выбранного пропорционально весам.
    private int PickWeightedIndex(List<INpcComponent> candidates)
    {
        int totalWeight = 0;

        foreach (INpcComponent candidate in candidates)
        {
            totalWeight += candidate.GetWeight();
        }

        if (totalWeight <= 0)
        {
            return 0;
        }

        int roll = _random.RandiRange(0, totalWeight - 1);
        int index = 0;

        while (index < candidates.Count - 1 && roll >= candidates[index].GetWeight())
        {
            roll -= candidates[index].GetWeight();
            index++;
        }

        return index;
    }

    // Освобождает неиспользованные компоненты.
    private static void FreeComponents(List<INpcComponent> components)
    {
        foreach (INpcComponent component in components)
        {
            if (component is Node node)
            {
                node.Free();
            }
        }
    }

    // Очки, которые команда может выделить на следующего НПС.
    // Минимум — половина Difficulty, но не меньше обязательных интерфейсов.
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

    // Первый свободный слот массива; новый слот добавляется по необходимости.
    private int FindFreeNpcSlot()
    {
        for (int i = 0; i < _npcs.Count; i++)
        {
            if (_npcs[i] == null)
            {
                return i;
            }
        }

        _npcs.Add(null);
        return _npcs.Count - 1;
    }

    // Очки команд инициализируются сложностью игры.
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

    // Команды всегда делят окружность на равные сектора,
    // поэтому цвета идут по цветовому кругу: красный, зелёный, синий...
    private void UpdateTeamData()
    {
        SpawnSectorAngles.Clear();

        if (TeamCount <= 0)
        {
            _teamColors = System.Array.Empty<Color>();
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

    // Случайная точка в кольцевом секторе команды: угол от центра сектора
    // в пределах его половины, радиус — между InnerRadius и SpawnRadius.
    private Vector2 GetRandomSpawnPosition(int squad)
    {
        float centerAngle = Mathf.DegToRad(SpawnSectorAngles[squad]);
        float halfSector = Mathf.DegToRad(FullCircleDegrees / TeamCount / 2f);

        float angle = centerAngle + _random.RandfRange(-halfSector, halfSector);

        float inner = Mathf.Min(InnerRadius, SpawnRadius);
        float outer = Mathf.Max(InnerRadius, SpawnRadius);

        // sqrt даёт равномерное распределение по площади кольца.
        float radius = Mathf.Sqrt(_random.RandfRange(inner * inner, outer * outer));

        return new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
    }
}
