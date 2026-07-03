using Godot;

public partial class Player : CharacterBody2D
{
	[Signal]
	public delegate void DamagedEventHandler(int currentHp, int maxHp);

	[Signal]
	public delegate void DefeatedEventHandler();

	private const string MoveLeftAction = "move_left";
	private const string MoveRightAction = "move_right";
	private const string MoveUpAction = "move_up";
	private const string MoveDownAction = "move_down";

	// Ссылка на CombatController, куда отправляются боевые input-команды игрока.
	[Export]
	public NodePath CombatControllerPath { get; set; } = new NodePath();

	// Базовая скорость перемещения игрока.
	[Export(PropertyHint.Range, "20,600,1")]
	public float MoveSpeed { get; set; } = 180.0f;

	// Максимальное HP игрока.
	[Export(PropertyHint.Range, "1,999,1")]
	public int MaxHp { get; set; } = 20;

	// Ускорение при наборе скорости.
	[Export(PropertyHint.Range, "50,3000,1")]
	public float Acceleration { get; set; } = 1200.0f;

	// Торможение при отпускании клавиш.
	[Export(PropertyHint.Range, "50,4000,1")]
	public float Deceleration { get; set; } = 1600.0f;

	// Скорость рывка (dodge).
	[Export(PropertyHint.Range, "40,1200,1")]
	public float DodgeSpeed { get; set; } = 420.0f;

	// Длительность рывка в секундах.
	[Export(PropertyHint.Range, "0.05,0.6,0.01")]
	public float DodgeDurationSeconds { get; set; } = 0.16f;

	// Кулдаун рывка в секундах.
	[Export(PropertyHint.Range, "0.1,2.0,0.01")]
	public float DodgeCooldownSeconds { get; set; } = 0.5f;

	private CombatController _combatController;
	private Vector2 _lastMoveDirection = Vector2.Right;
	private Vector2 _dodgeDirection = Vector2.Zero;
	private float _dodgeRemainingSeconds;
	private float _dodgeCooldownRemainingSeconds;
	private int _currentHp;

	public int CurrentHp => _currentHp;

	public override void _Ready()
	{
		EnsureInputActions();

		_combatController = GetNodeOrNull<CombatController>(CombatControllerPath);
		_combatController ??= GetNodeOrNull<CombatController>("../CombatController");
		_currentHp = MaxHp;

		QueueRedraw();
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;

		if (_dodgeCooldownRemainingSeconds > 0.0f)
		{
			_dodgeCooldownRemainingSeconds -= dt;
		}

		if (_dodgeRemainingSeconds > 0.0f)
		{
			_dodgeRemainingSeconds -= dt;
			Velocity = _dodgeDirection * DodgeSpeed;
			MoveAndSlide();
			return;
		}

		// Движение игрока в этой фазе идёт строго через WASD.
		Vector2 movementInput = GetMovementInput();
		Vector2 desiredDirection = movementInput;

		// Экранно-интуитивное управление: вправо = вправо на экране.
		if (desiredDirection != Vector2.Zero)
		{
			desiredDirection = desiredDirection.Normalized();
			_lastMoveDirection = desiredDirection;
		}

		Vector2 targetVelocity = desiredDirection * MoveSpeed;
		float moveStep = desiredDirection == Vector2.Zero ? Deceleration * dt : Acceleration * dt;
		Velocity = Velocity.MoveToward(targetVelocity, moveStep);
		MoveAndSlide();
		QueueRedraw();
	}

	public override void _Draw()
	{
		// Простейшая MVP-визуализация игрока: ромб + маркер направления взгляда.
		Vector2[] bodyPoints = new Vector2[]
		{
			new Vector2(0, -14),
			new Vector2(12, 0),
			new Vector2(0, 14),
			new Vector2(-12, 0)
		};
		DrawColoredPolygon(bodyPoints, new Color(0.35f, 0.75f, 1.0f));

		Vector2 lookDir = _lastMoveDirection == Vector2.Zero ? Vector2.Right : _lastMoveDirection.Normalized();
		DrawLine(Vector2.Zero, lookDir * 16.0f, new Color(1.0f, 1.0f, 1.0f), 2.0f);

		// Полоса HP игрока.
		float hpRatio = MaxHp > 0 ? Mathf.Clamp((float)_currentHp / MaxHp, 0.0f, 1.0f) : 0.0f;
		Vector2 barStart = new Vector2(-18.0f, -24.0f);
		DrawRect(new Rect2(barStart, new Vector2(36.0f, 4.0f)), new Color(0.15f, 0.15f, 0.15f));
		DrawRect(new Rect2(barStart, new Vector2(36.0f * hpRatio, 4.0f)), new Color(0.3f, 0.9f, 1.0f));
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseButton && mouseButton.Pressed)
		{
			// По ТЗ: ЛКМ и ПКМ — это L/R input для ритм-удара.
			if (mouseButton.ButtonIndex == MouseButton.Left)
			{
				_combatController?.OnPlayerHitRequested(HitType.Left);
			}
			else if (mouseButton.ButtonIndex == MouseButton.Right)
			{
				_combatController?.OnPlayerHitRequested(HitType.Right);
			}
		}

		if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
		{
			if (keyEvent.Keycode == Key.Space)
			{
				TryStartDodge();
			}
			else if (keyEvent.Keycode == Key.Q)
			{
				// Клавиатурный дубль L-удара для удобного теста без мыши.
				_combatController?.OnPlayerHitRequested(HitType.Left);
			}
			else if (keyEvent.Keycode == Key.E)
			{
				// Клавиатурный дубль R-удара для удобного теста без мыши.
				_combatController?.OnPlayerHitRequested(HitType.Right);
			}
			else if (keyEvent.Keycode == Key.Tab)
			{
				// Для MVP переключаем активную цель отдельной клавишей.
				_combatController?.OnSelectNextTargetRequested();
			}
		}
	}

	private void TryStartDodge()
	{
		if (_dodgeCooldownRemainingSeconds > 0.0f || _dodgeRemainingSeconds > 0.0f)
		{
			return;
		}

		Vector2 dodgeDirection = _lastMoveDirection;
		if (dodgeDirection == Vector2.Zero)
		{
			dodgeDirection = Vector2.Right;
		}

		_dodgeDirection = dodgeDirection.Normalized();
		_dodgeRemainingSeconds = DodgeDurationSeconds;
		_dodgeCooldownRemainingSeconds = DodgeCooldownSeconds;
		QueueRedraw();
	}

	private static Vector2 GetMovementInput()
	{
		// Базовый ввод через InputMap (если действия уже корректно настроены).
		Vector2 actionInput = Input.GetVector(MoveLeftAction, MoveRightAction, MoveUpAction, MoveDownAction);

		// Надёжный fallback по физическим клавишам, независимый от раскладки.
		Vector2 keyInput = Vector2.Zero;
		if (Input.IsPhysicalKeyPressed(Key.A))
		{
			keyInput.X -= 1.0f;
		}
		if (Input.IsPhysicalKeyPressed(Key.D))
		{
			keyInput.X += 1.0f;
		}
		if (Input.IsPhysicalKeyPressed(Key.W))
		{
			keyInput.Y -= 1.0f;
		}
		if (Input.IsPhysicalKeyPressed(Key.S))
		{
			keyInput.Y += 1.0f;
		}

		Vector2 combined = actionInput + keyInput;
		if (combined.LengthSquared() > 1.0f)
		{
			combined = combined.Normalized();
		}

		return combined;
	}

	private static void EnsureInputActions()
	{
		// Создаём отдельные действия движения, чтобы не зависеть от ui_left/ui_right (стрелок).
		EnsureActionKey(MoveLeftAction, Key.A);
		EnsureActionKey(MoveRightAction, Key.D);
		EnsureActionKey(MoveUpAction, Key.W);
		EnsureActionKey(MoveDownAction, Key.S);
	}

	private static void EnsureActionKey(string actionName, Key key)
	{
		if (!InputMap.HasAction(actionName))
		{
			InputMap.AddAction(actionName);
		}

		foreach (InputEvent inputEvent in InputMap.ActionGetEvents(actionName))
		{
			if (inputEvent is InputEventKey keyEvent &&
				(keyEvent.Keycode == key || keyEvent.PhysicalKeycode == key))
			{
				return;
			}
		}

		InputEventKey newKey = new InputEventKey
		{
			Keycode = key,
			PhysicalKeycode = key
		};
		InputMap.ActionAddEvent(actionName, newKey);
	}

	public void TakeDamage(int damage)
	{
		if (damage <= 0 || _currentHp <= 0)
		{
			return;
		}

		_currentHp -= damage;
		if (_currentHp < 0)
		{
			_currentHp = 0;
		}

		EmitSignal(SignalName.Damaged, _currentHp, MaxHp);
		GD.Print($"Игрок получил {damage} урона. HP: {_currentHp}/{MaxHp}");
		QueueRedraw();

		if (_currentHp == 0)
		{
			EmitSignal(SignalName.Defeated);
			GD.Print("Игрок побеждён");
		}
	}
}
