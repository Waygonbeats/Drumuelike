using Godot;

public partial class RhythmTestRoomStaticSetup : Node2D
{
	// Ссылка на CombatController, чтобы он перечитал список врагов после настройки.
	[Export]
	public NodePath CombatControllerPath { get; set; } = new NodePath("../CombatController");

	private CombatController _combatController;

	public override void _Ready()
	{
		_combatController = GetNodeOrNull<CombatController>(CombatControllerPath);
		SetupStaticEnemies();
		_combatController?.RefreshEnemiesFromRoom();
	}

	private void SetupStaticEnemies()
	{
		foreach (Node child in GetChildren())
		{
			if (child is not Enemy enemy)
			{
				continue;
			}

			// В тестовой сцене враги — полностью статичная "витрина".
			enemy.MoveSpeed = 0.0f;
			enemy.StopDistance = 0.0f;
			enemy.ContactAttackDistance = 0.0f;
			enemy.CounterattackDamage = 0;
			enemy.SetProcess(false);

			ApplyPatternByName(enemy);
			enemy.EnsureInitializedForCombat();
			enemy.QueueRedraw();
		}
	}

	private static void ApplyPatternByName(Enemy enemy)
	{
		enemy.Pattern.Clear();

		switch (enemy.Name)
		{
			case "Alt_4":
				Push(enemy, HitType.Left, HitType.Right, HitType.Left, HitType.Right);
				break;
			case "RepeatL_4":
				Push(enemy, HitType.Left, HitType.Left, HitType.Left, HitType.Left);
				break;
			case "RepeatR_4":
				Push(enemy, HitType.Right, HitType.Right, HitType.Right, HitType.Right);
				break;
			case "Pauses_4":
				Push(enemy, HitType.Left, HitType.Right, HitType.Left, HitType.Right);
				break;
			case "Accents_4":
				Push(enemy, HitType.AccentLeft, HitType.Right, HitType.AccentRight, HitType.Left);
				break;
			case "Syncop_8":
				Push(enemy, HitType.AccentLeft, HitType.Right, HitType.Rest, HitType.AccentRight, HitType.Left, HitType.Right, HitType.Rest, HitType.Right); // sync gaps
				break;
			case "Phrase_8":
				Push(enemy, HitType.Left, HitType.Right, HitType.Left, HitType.AccentRight, HitType.Rest, HitType.Left, HitType.AccentLeft, HitType.Right); // shifted gap
				break;
			case "Odd_9":
				Push(enemy, HitType.Left, HitType.AccentRight, HitType.Right, HitType.Left, HitType.Right, HitType.AccentLeft, HitType.Left, HitType.Right, HitType.Left); // offbeat accents
				break;
			case "Long_16":
				Push(enemy,
					HitType.AccentLeft, HitType.Right, HitType.Left, HitType.AccentRight, HitType.Left, HitType.Right, HitType.AccentLeft, HitType.Right,
					HitType.Left, HitType.AccentRight, HitType.Right, HitType.Left, HitType.Right, HitType.AccentLeft, HitType.Left, HitType.Right); // alternating sync accents
				break;
			default:
				// Защита от пустого узла: минимальный паттерн.
				Push(enemy, HitType.Left, HitType.Right, HitType.Left, HitType.Right);
				break;
		}
	}

	private static void Push(Enemy enemy, params HitType[] hits)
	{
		foreach (HitType hit in hits)
		{
			enemy.Pattern.Add(hit);
		}
	}
}
