using Godot;

public partial class RhythmTestRoomSetup : Node2D
{
	// Ссылка на CombatController, чтобы обновить список врагов после спавна.
	[Export]
	public NodePath CombatControllerPath { get; set; } = new NodePath("../CombatController");

	private CombatController _combatController;
	private int _spawnedEnemies;

	public override void _Ready()
	{
		_combatController = GetNodeOrNull<CombatController>(CombatControllerPath);
		RemoveExistingTestEnemies();
		SpawnTestEnemies();
		GD.Print($"RhythmTestRoomSetup: заспавнено врагов = {_spawnedEnemies}");
		_combatController?.RefreshEnemiesFromRoom();
	}

	private void SpawnTestEnemies()
	{
		_spawnedEnemies = 0;

		// Вариация: чередование (классический LRLR).
		SpawnEnemy("Alt_4", new Vector2(180, 170), 14,
			HitType.Left, HitType.Right, HitType.Left, HitType.Right);

		// Вариация: повторы одной стороны.
		SpawnEnemy("RepeatL_4", new Vector2(350, 170), 14,
			HitType.Left, HitType.Left, HitType.Left, HitType.Left);

		// Вариация: повторы с правой стороны.
		SpawnEnemy("RepeatR_4", new Vector2(520, 170), 14,
			HitType.Right, HitType.Right, HitType.Right, HitType.Right);

		// Вариация: паузы в коротком паттерне.
		SpawnEnemy("Pauses_4", new Vector2(690, 170), 14,
			HitType.Left, HitType.Right, HitType.Left, HitType.Right);

		// Вариация: акценты (усложнённые удары).
		SpawnEnemy("Accents_4", new Vector2(860, 170), 14,
			HitType.AccentLeft, HitType.Right, HitType.AccentRight, HitType.Left);

		// Вариация: синкопа в 8 слотах (удары с паузами между долями).
		SpawnEnemy("Syncop_8", new Vector2(250, 340), 14,
			HitType.AccentLeft, HitType.Right, HitType.Left, HitType.AccentRight,
			HitType.Left, HitType.Right, HitType.Left, HitType.Right);

		// Вариация: фраза 8 слотов (смешанный паттерн).
		SpawnEnemy("Phrase_8", new Vector2(450, 340), 14,
			HitType.Left, HitType.Right, HitType.Left, HitType.AccentRight,
			HitType.Right, HitType.Left, HitType.AccentLeft, HitType.Right);

		// Вариация: нечётная длина 9 (проверка нормализации до полного бара).
		SpawnEnemy("Odd_9", new Vector2(650, 340), 14,
			HitType.Left, HitType.Right, HitType.Left, HitType.Right, HitType.Left,
			HitType.Right, HitType.AccentLeft, HitType.Left, HitType.AccentRight);

		// Вариация: длинная фраза 16 слотов (2 бара по 8).
		SpawnEnemy("Long_16", new Vector2(850, 340), 16,
			HitType.AccentLeft, HitType.Right, HitType.Left, HitType.AccentRight,
			HitType.Left, HitType.Right, HitType.Left, HitType.Right,
			HitType.Left, HitType.Right, HitType.Left, HitType.AccentRight,
			HitType.Right, HitType.Left, HitType.AccentLeft, HitType.Right);
	}

	private void SpawnEnemy(string name, Vector2 position, int maxHp, params HitType[] pattern)
	{
		Enemy enemy = new Enemy
		{
			Name = name,
			Position = position,
			MaxHp = maxHp,
			CounterattackDamage = 1,
			MoveSpeed = 0.0f
		};

		foreach (HitType hit in pattern)
		{
			enemy.Pattern.Add(hit);
		}

		AddChild(enemy);
		// Для тестовой сцены враги полностью статичны: без преследования и контактных атак.
		enemy.SetProcess(false);
		enemy.EnsureInitializedForCombat();
		_spawnedEnemies++;
	}

	private void RemoveExistingTestEnemies()
	{
		foreach (Node child in GetChildren())
		{
			if (child is Enemy)
			{
				child.QueueFree();
			}
		}
	}
}
