using Godot;
using System.Collections.Generic;

public partial class RoomController : Node2D
{
	[Signal]
	public delegate void RoomClearedEventHandler();

	// Ссылка на CombatController, чтобы обновить список врагов после спавна.
	[Export]
	public NodePath CombatControllerPath { get; set; } = new NodePath("../CombatController");

	// Задержка перед стартом следующей волны после очистки комнаты.
	[Export(PropertyHint.Range, "0.2,10.0,0.1")]
	public float WaveRespawnDelaySeconds { get; set; } = 1.5f;

	// Количество волн в MVP-цикле.
	[Export(PropertyHint.Range, "1,20,1")]
	public int MaxWaves { get; set; } = 5;

	private CombatController _combatController;
	private bool _roomClearedSent;
	private bool _waitingNextWave;
	private float _waveTimer;
	private int _currentWave;

	private readonly struct PatternVariant
	{
		public PatternVariant(HitType[] hits, string rudimentName)
		{
			Hits = CopyHits(hits);
			RudimentName = rudimentName ?? string.Empty;
		}

		public HitType[] Hits { get; }
		public string RudimentName { get; }

		public PatternVariant Clone()
		{
			return new PatternVariant(Hits, RudimentName);
		}

		private static HitType[] CopyHits(HitType[] hits)
		{
			if (hits == null || hits.Length == 0)
			{
				return new[] { HitType.Left, HitType.Right };
			}

			HitType[] copy = new HitType[hits.Length];
			for (int i = 0; i < hits.Length; i++)
			{
				copy[i] = hits[i];
			}

			return copy;
		}
	}

	public override void _Ready()
	{
		_combatController = GetNodeOrNull<CombatController>(CombatControllerPath);
		StartFirstWaveIfNeeded();

		// Обновляем CombatController после спавна врагов.
		_combatController?.RefreshEnemiesFromRoom();
	}

	public override void _Process(double delta)
	{
		if (_waitingNextWave)
		{
			_waveTimer -= (float)delta;
			if (_waveTimer <= 0.0f)
			{
				_waitingNextWave = false;
				SpawnNextWaveIfAllowed();
				_combatController?.RefreshEnemiesFromRoom();
			}
			return;
		}

		if (_roomClearedSent)
		{
			return;
		}

		List<Enemy> enemies = GetEnemiesInRoom();
		if (enemies.Count == 0)
		{
			return;
		}

		foreach (Enemy enemy in enemies)
		{
			if (enemy.IsAlive)
			{
				return;
			}
		}

		_roomClearedSent = true;
		GD.Print("КОМНАТА ОЧИЩЕНА: все враги побеждены");
		EmitSignal(SignalName.RoomCleared);

		// Формируем следующий цикл комнаты.
		_waitingNextWave = true;
		_waveTimer = WaveRespawnDelaySeconds;
	}

	private void StartFirstWaveIfNeeded()
	{
		List<Enemy> existingEnemies = GetEnemiesInRoom();
		if (existingEnemies.Count > 0)
		{
			_currentWave = 1;
			return;
		}

		_currentWave = 1;
		SpawnWave(_currentWave);
	}

	private void SpawnNextWaveIfAllowed()
	{
		CleanupDefeatedEnemies();

		if (_currentWave >= MaxWaves)
		{
			GD.Print("ЦИКЛ ЗАВЕРШЕН: достигнут лимит волн");
			return;
		}

		_currentWave += 1;
		_roomClearedSent = false;
		SpawnWave(_currentWave);
		GD.Print($"СТАРТ ВОЛНЫ: {_currentWave}");
	}

	private void SpawnWave(int wave)
	{
		// MVP-волна: два врага, которые постепенно усиливаются по HP.
		int hpBonus = (wave - 1) * 2;

		PatternVariant slimePattern = BuildSlimePattern(wave);

		Enemy slime = CreateEnemy(
			name: $"Slime_W{wave}",
			position: new Vector2(-180.0f, -40.0f),
			maxHp: 12 + hpBonus,
			pattern: slimePattern.Hits,
			rudimentName: slimePattern.RudimentName);
		AddChild(slime);
		slime.EnsureInitializedForCombat();

		PatternVariant batPattern = BuildBatPattern(wave);

		Enemy bat = CreateEnemy(
			name: $"Bat_W{wave}",
			position: new Vector2(180.0f, 40.0f),
			maxHp: 10 + hpBonus,
			pattern: batPattern.Hits,
			rudimentName: batPattern.RudimentName);
		AddChild(bat);
		bat.EnsureInitializedForCombat();

		// Начиная со 2-й волны добавляем третьего врага с паттерном Knight.
		if (wave >= 2)
		{
			PatternVariant knightPattern = BuildKnightPattern(wave);
			Enemy knight = CreateEnemy(
				name: $"Knight_W{wave}",
				position: new Vector2(0.0f, -120.0f),
				maxHp: 16 + hpBonus,
				pattern: knightPattern.Hits,
				rudimentName: knightPattern.RudimentName);
			AddChild(knight);
			knight.EnsureInitializedForCombat();
		}
	}

	private static PatternVariant BuildSlimePattern(int wave)
	{
		// Slime: базовые рудименты. Читается как обучение рукам.
		PatternVariant[] variants =
		{
			new PatternVariant(RudimentLibrary.BuildGroove(RudimentId.SingleStroke, mirror: false, slotCount: 8, accentSlots: new[] { 0, 4 }), $"Pulse: {RudimentLibrary.GetDisplayName(RudimentId.SingleStroke)}"),
			new PatternVariant(RudimentLibrary.BuildGroove(RudimentId.SingleStroke, mirror: true, slotCount: 8, accentSlots: new[] { 0, 4 }), $"Pulse: {RudimentLibrary.GetDisplayName(RudimentId.SingleStroke, mirror: true)}"),
			new PatternVariant(RudimentLibrary.BuildGroove(RudimentId.DoubleStroke, mirror: false, slotCount: 8, accentSlots: new[] { 0, 4 }), $"Roll: {RudimentLibrary.GetDisplayName(RudimentId.DoubleStroke)}"),
			new PatternVariant(RudimentLibrary.BuildGroove(RudimentId.SingleStroke, mirror: false, slotCount: 8, accentSlots: new[] { 0, 4 }, restSlots: new[] { 3 }), $"Pocket: {RudimentLibrary.GetDisplayName(RudimentId.SingleStroke)}"),
			new PatternVariant(RudimentLibrary.BuildGroove(RudimentId.AccentGrid, mirror: false, slotCount: 8, accentSlots: new[] { 0, 5 }), $"Dynamics: {RudimentLibrary.GetDisplayName(RudimentId.AccentGrid)}"),
			new PatternVariant(RudimentLibrary.BuildShiftedGroove(RudimentId.SingleStroke, mirror: false, slotCount: 8, rotationSteps: 1, accentSlots: new[] { 2, 6 }, restSlots: new[] { 5 }), $"Offset: {RudimentLibrary.GetDisplayName(RudimentId.SingleStroke)}"),
			new PatternVariant(RudimentLibrary.BuildGroove(RudimentId.SyncopatedTriplet, mirror: false, slotCount: 8, accentSlots: new[] { 0, 3, 6 }, restSlots: new[] { 2 }), $"Triplet: {RudimentLibrary.GetDisplayName(RudimentId.SyncopatedTriplet)}")
		};

		return SelectVariant(variants, wave, salt: 11);
	}

	private static PatternVariant BuildBatPattern(int wave)
	{
		// Bat: paradiddle-семейство, ощущается более "ломаным" и музыкальным.
		PatternVariant[] variants =
		{
			new PatternVariant(RudimentLibrary.BuildGroove(RudimentId.Paradiddle, mirror: false, slotCount: 8, accentSlots: new[] { 0, 4 }), $"Shift: {RudimentLibrary.GetDisplayName(RudimentId.Paradiddle)}"),
			new PatternVariant(RudimentLibrary.BuildGroove(RudimentId.Paradiddle, mirror: true, slotCount: 8, accentSlots: new[] { 0, 4 }), $"Shift: {RudimentLibrary.GetDisplayName(RudimentId.Paradiddle, mirror: true)}"),
			new PatternVariant(RudimentLibrary.BuildGroove(RudimentId.ParadiddleDiddle, mirror: false, slotCount: 8, accentSlots: new[] { 0, 3, 6 }), $"Float: {RudimentLibrary.GetDisplayName(RudimentId.ParadiddleDiddle)}"),
			new PatternVariant(RudimentLibrary.BuildGroove(RudimentId.Paradiddle, mirror: false, slotCount: 8, accentSlots: new[] { 0, 5 }, restSlots: new[] { 3 }), $"Pocket: {RudimentLibrary.GetDisplayName(RudimentId.Paradiddle)}"),
			new PatternVariant(RudimentLibrary.BuildGroove(RudimentId.FlamAccent, mirror: false, slotCount: 8, accentSlots: new[] { 0, 3, 6 }), $"Accent: {RudimentLibrary.GetDisplayName(RudimentId.FlamAccent)}"),
			new PatternVariant(RudimentLibrary.BuildGroove(RudimentId.InvertedParadiddle, mirror: false, slotCount: 8, accentSlots: new[] { 1, 4, 7 }), $"Inside-Out: {RudimentLibrary.GetDisplayName(RudimentId.InvertedParadiddle)}"),
			new PatternVariant(RudimentLibrary.BuildShiftedGroove(RudimentId.ParadiddleDiddle, mirror: true, slotCount: 8, rotationSteps: 2, accentSlots: new[] { 0, 4, 7 }, restSlots: new[] { 3 }), $"Broken Float: {RudimentLibrary.GetDisplayName(RudimentId.ParadiddleDiddle, mirror: true)}")
		};

		return SelectVariant(variants, wave, salt: 23);
	}

	private static PatternVariant BuildKnightPattern(int wave)
	{
		// Knight: составные рудименты с акцентами. Это уже "мини-партия".
		PatternVariant[] variants =
		{
			new PatternVariant(RudimentLibrary.ComposeGroove(16, new[] { 0, 4, 8, 12 }, new[] { 7 }, (RudimentId.FlamAccent, false), (RudimentId.SingleStroke, true)), $"Charge: {RudimentLibrary.GetPhraseName((RudimentId.FlamAccent, false), (RudimentId.SingleStroke, true))}"),
			new PatternVariant(RudimentLibrary.ComposeGroove(16, new[] { 0, 6, 10, 14 }, new[] { 4 }, (RudimentId.Paradiddle, false), (RudimentId.DoubleStroke, true)), $"Control: {RudimentLibrary.GetPhraseName((RudimentId.Paradiddle, false), (RudimentId.DoubleStroke, true))}"),
			new PatternVariant(RudimentLibrary.ComposeGroove(16, new[] { 0, 2, 8, 13 }, new[] { 6 }, (RudimentId.DragLike, false), (RudimentId.ParadiddleDiddle, true)), $"Drag Chain: {RudimentLibrary.GetPhraseName((RudimentId.DragLike, false), (RudimentId.ParadiddleDiddle, true))}"),
			new PatternVariant(RudimentLibrary.ComposeGroove(16, new[] { 0, 5, 8, 13 }, new[] { 6, 11 }, (RudimentId.Paradiddle, false), (RudimentId.FlamAccent, true)), $"Pocket: {RudimentLibrary.GetPhraseName((RudimentId.Paradiddle, false), (RudimentId.FlamAccent, true))}"),
			new PatternVariant(RudimentLibrary.ComposeGroove(16, new[] { 0, 4, 8, 12 }, null, (RudimentId.AccentGrid, false), (RudimentId.Paradiddle, true)), $"Finale: {RudimentLibrary.GetPhraseName((RudimentId.AccentGrid, false), (RudimentId.Paradiddle, true))}"),
			new PatternVariant(RudimentLibrary.BuildCompositePhrase(16, new[] { 0, 3, 8, 11, 14 }, new[] { 5, 12 }, (RudimentId.InvertedParadiddle, false, 1), (RudimentId.HertaLike, true, 2)), $"Pressure: {RudimentLibrary.GetDisplayName(RudimentId.InvertedParadiddle)} + {RudimentLibrary.GetDisplayName(RudimentId.HertaLike, true)}"),
			new PatternVariant(RudimentLibrary.BuildCompositePhrase(16, new[] { 0, 4, 6, 10, 15 }, new[] { 3, 11 }, (RudimentId.SyncopatedTriplet, false, 0), (RudimentId.FlamAccent, true, 1), (RudimentId.DoubleStroke, false, 0)), $"Tripwire: {RudimentLibrary.GetDisplayName(RudimentId.SyncopatedTriplet)} + {RudimentLibrary.GetDisplayName(RudimentId.FlamAccent, true)}"),
			new PatternVariant(RudimentLibrary.BuildCompositePhrase(16, new[] { 1, 5, 9, 13 }, new[] { 7 }, (RudimentId.HertaLike, false, 0), (RudimentId.ParadiddleDiddle, false, 3), (RudimentId.SingleStroke, true, 0)), $"Surge: {RudimentLibrary.GetDisplayName(RudimentId.HertaLike)} + {RudimentLibrary.GetDisplayName(RudimentId.ParadiddleDiddle)}")
		};

		return SelectVariant(variants, wave, salt: 37);
	}

	private static PatternVariant SelectVariant(PatternVariant[] variants, int wave, int salt)
	{
		if (variants == null || variants.Length == 0)
		{
			return new PatternVariant(new[] { HitType.Left, HitType.Right }, RudimentLibrary.GetDisplayName(RudimentId.SingleStroke));
		}

		// Детерминированная ротация по волнам: разнообразно, но воспроизводимо.
		int index = Mathf.PosMod(wave * 3 + salt, variants.Length);
		return variants[index].Clone();
	}

	private Enemy CreateEnemy(string name, Vector2 position, int maxHp, HitType[] pattern, string rudimentName = "")
	{
		Enemy enemy = new Enemy
		{
			Name = name,
			Position = position,
			MaxHp = maxHp,
			CounterattackDamage = 1,
			RudimentName = rudimentName
		};

		foreach (HitType hit in pattern)
		{
			enemy.Pattern.Add(hit);
		}

		return enemy;
	}

	private void CleanupDefeatedEnemies()
	{
		List<Enemy> enemies = GetEnemiesInRoom();
		foreach (Enemy enemy in enemies)
		{
			if (!enemy.IsAlive)
			{
				enemy.QueueFree();
			}
		}
	}

	private List<Enemy> GetEnemiesInRoom()
	{
		List<Enemy> enemies = new List<Enemy>();
		CollectEnemiesRecursive(this, enemies);
		return enemies;
	}

	private static void CollectEnemiesRecursive(Node node, List<Enemy> buffer)
	{
		if (node is Enemy enemy)
		{
			buffer.Add(enemy);
		}

		foreach (Node child in node.GetChildren())
		{
			CollectEnemiesRecursive(child, buffer);
		}
	}
}
