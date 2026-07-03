using Godot;
using System;
using System.Collections.Generic;

public partial class Enemy : Node2D
{
	[Signal]
	public delegate void DamagedEventHandler(int currentHp, int maxHp);

	[Signal]
	public delegate void DefeatedEventHandler();

	[Signal]
	public delegate void CounterattackedEventHandler(int damage);

	// Максимальное здоровье врага.
	[Export(PropertyHint.Range, "1,999,1")]
	public int MaxHp { get; set; } = 10;

	// Урон контратаки, который враг наносит при ошибке игрока.
	[Export(PropertyHint.Range, "1,99,1")]
	public int CounterattackDamage { get; set; } = 1;

	// Дистанция контактной атаки врага (обычное давление в комнате).
	[Export(PropertyHint.Range, "10,200,1")]
	public float ContactAttackDistance { get; set; } = 44.0f;

	// Радиус наведения мышью для подсветки цели.
	[Export(PropertyHint.Range, "8,80,1")]
	public float HoverRadius { get; set; } = 20.0f;

	// Интервал контактных атак.
	[Export(PropertyHint.Range, "0.1,3.0,0.01")]
	public float ContactAttackIntervalSeconds { get; set; } = 0.45f;

	// Последовательность ударов врага (его ритм-паттерн).
	[Export]
	public Godot.Collections.Array<HitType> Pattern { get; set; } = new Godot.Collections.Array<HitType>();

	[Export]
	public string RudimentName { get; set; } = string.Empty;

	// Ссылка на Player для простого давления в комнате.
	[Export]
	public NodePath PlayerPath { get; set; } = new NodePath("../../Player");

	// Скорость движения врага в комнате.
	[Export(PropertyHint.Range, "10,300,1")]
	public float MoveSpeed { get; set; } = 120.0f;

	// Дистанция, на которой враг останавливается возле игрока.
	[Export(PropertyHint.Range, "10,200,1")]
	public float StopDistance { get; set; } = 22.0f;

	// Радиус, в котором враги отталкиваются друг от друга.
	[Export(PropertyHint.Range, "10,200,1")]
	public float SeparationRadius { get; set; } = 42.0f;

	// Сила разведения врагов, чтобы не слипались в одну точку.
	[Export(PropertyHint.Range, "0.0,2.0,0.01")]
	public float SeparationStrength { get; set; } = 0.65f;

	public int CurrentHp { get; private set; }
	public bool IsAlive => CurrentHp > 0;
	private bool _isInitializedForCombat;
	private Node2D _player;
	private float _contactAttackCooldown;
	private bool _isActiveTarget;
	private bool _isHoveredByCursor;

	public override void _Ready()
	{
		EnsureInitializedForCombat();
		_player = GetNodeOrNull<Node2D>(PlayerPath);
		_player ??= GetNodeOrNull<Node2D>("../../Player");
		QueueRedraw();
	}

	public override void _Process(double delta)
	{
		if (!IsAlive || _player == null)
		{
			return;
		}

		if (_contactAttackCooldown > 0.0f)
		{
			_contactAttackCooldown -= (float)delta;
		}

		Vector2 toPlayer = _player.GlobalPosition - GlobalPosition;
		float distance = toPlayer.Length();
		if (distance <= ContactAttackDistance && _contactAttackCooldown <= 0.0f)
		{
			// Обычная контактная атака врага вне ритм-промаха.
			Counterattack();
			_contactAttackCooldown = ContactAttackIntervalSeconds;
		}

		float effectiveStopDistance = Mathf.Min(StopDistance, ContactAttackDistance * 0.7f);
		if (distance <= effectiveStopDistance || distance <= 0.001f)
		{
			return;
		}

		// Базовое направление: к игроку.
		Vector2 direction = toPlayer / distance;
		// Добавляем простое локальное разведение между соседними врагами.
		direction = (direction + GetSeparationDirection() * SeparationStrength).Normalized();
		GlobalPosition += direction * MoveSpeed * (float)delta;
	}

	public void EnsureInitializedForCombat()
	{
		if (_isInitializedForCombat)
		{
			return;
		}

		CurrentHp = MaxHp;

		// На случай пустого паттерна задаём минимальный паттерн для MVP.
		if (Pattern.Count == 0)
		{
			Pattern.Add(HitType.Left);
			Pattern.Add(HitType.Right);
		}

		_isInitializedForCombat = true;
	}

	public List<HitType> GetPatternHits()
	{
		List<HitType> hits = new List<HitType>(Pattern.Count);
		foreach (HitType hit in Pattern)
		{
			hits.Add(hit);
		}

		NormalizePatternSlots(hits);
		return hits;
	}

	public void TakeDamage(int damage)
	{
		EnsureInitializedForCombat();

		if (!IsAlive || damage <= 0)
		{
			return;
		}

		CurrentHp -= damage;
		if (CurrentHp < 0)
		{
			CurrentHp = 0;
		}

		EmitSignal(SignalName.Damaged, CurrentHp, MaxHp);
		GD.Print($"Враг {Name}: -{damage} HP, осталось {CurrentHp}");
		QueueRedraw();
		if (!IsAlive)
		{
			EmitSignal(SignalName.Defeated);
			GD.Print($"Враг {Name} побеждён");
		}
	}

	public override void _Draw()
	{
		// Простейшая MVP-визуализация врага: красный круг + индикатор HP.
		Color bodyColor = IsAlive ? new Color(0.9f, 0.25f, 0.25f) : new Color(0.3f, 0.3f, 0.3f);
		if (_isActiveTarget && IsAlive)
		{
			bodyColor = new Color(1.0f, 0.78f, 0.2f);
		}
		DrawCircle(Vector2.Zero, 14.0f, bodyColor);

		// Подсветка врага, на которого наведён курсор.
		if (_isHoveredByCursor && IsAlive)
		{
			DrawArc(Vector2.Zero, 18.0f, 0.0f, Mathf.Tau, 28, new Color(1.0f, 1.0f, 1.0f), 2.0f);
		}

		float hpRatio = MaxHp > 0 ? Mathf.Clamp((float)CurrentHp / MaxHp, 0.0f, 1.0f) : 0.0f;
		Vector2 barStart = new Vector2(-16.0f, -24.0f);
		DrawRect(new Rect2(barStart, new Vector2(32.0f, 4.0f)), new Color(0.15f, 0.15f, 0.15f));
		DrawRect(new Rect2(barStart, new Vector2(32.0f * hpRatio, 4.0f)), new Color(0.25f, 0.95f, 0.35f));

		// Отображаем паттерн врага прямо над ним.
		Font font = ThemeDB.FallbackFont;
		if (font != null)
		{
			string rudimentText = BuildRudimentText();
			if (!string.IsNullOrEmpty(rudimentText))
			{
				Vector2 rudimentSize = font.GetStringSize(rudimentText, HorizontalAlignment.Left, -1, 10);
				Vector2 rudimentPos = new Vector2(-rudimentSize.X * 0.5f, -48.0f);
				DrawString(font, rudimentPos, rudimentText, HorizontalAlignment.Left, -1, 10, new Color(0.72f, 0.9f, 1.0f));
			}

			string patternText = BuildPatternText();
			Vector2 size = font.GetStringSize(patternText, HorizontalAlignment.Left, -1, 14);
			Vector2 textPos = new Vector2(-size.X * 0.5f, -34.0f);
			DrawString(font, textPos, patternText, HorizontalAlignment.Left, -1, 14, Colors.White);
		}
	}

	public void Counterattack()
	{
		EnsureInitializedForCombat();

		if (!IsAlive)
		{
			return;
		}

		EmitSignal(SignalName.Counterattacked, CounterattackDamage);
		GD.Print($"Враг {Name} контратакует и наносит {CounterattackDamage} урона");
	}

	public void SetActiveTarget(bool isActive)
	{
		_isActiveTarget = isActive;
		QueueRedraw();
	}

	public void SetHoveredByCursor(bool isHovered)
	{
		_isHoveredByCursor = isHovered;
		QueueRedraw();
	}

	public bool IsPointHovering(Vector2 worldPoint)
	{
		return IsAlive && GlobalPosition.DistanceTo(worldPoint) <= HoverRadius;
	}

	private string BuildPatternText()
	{
		List<HitType> hits = GetPatternHits();
		if (hits.Count == 0)
		{
			return "-";
		}

		System.Text.StringBuilder sb = new System.Text.StringBuilder(hits.Count * 2);
		for (int i = 0; i < hits.Count; i++)
		{
			sb.Append(FormatHitType(hits[i]));
			if (i < hits.Count - 1)
			{
				sb.Append(' ');
			}
		}

		return sb.ToString();
	}

	private string BuildRudimentText()
	{
		if (string.IsNullOrWhiteSpace(RudimentName))
		{
			return string.Empty;
		}

		string text = RudimentName.Trim();
		const int maxLength = 28;
		if (text.Length <= maxLength)
		{
			return text;
		}

		return text.Substring(0, maxLength - 1) + ".";
	}

	private Vector2 GetSeparationDirection()
	{
		if (SeparationRadius <= 0.0f || GetParent() == null)
		{
			return Vector2.Zero;
		}

		Vector2 push = Vector2.Zero;
		foreach (Node child in GetParent().GetChildren())
		{
			if (child == this || child is not Enemy other || !other.IsAlive)
			{
				continue;
			}

			Vector2 delta = GlobalPosition - other.GlobalPosition;
			float dist = delta.Length();
			if (dist <= 0.001f || dist >= SeparationRadius)
			{
				continue;
			}

			// Чем ближе сосед, тем сильнее отталкивание.
			float weight = 1.0f - (dist / SeparationRadius);
			push += (delta / dist) * weight;
		}

		if (push == Vector2.Zero)
		{
			return Vector2.Zero;
		}

		return push.Normalized();
	}

	private static void NormalizePatternSlots(List<HitType> hits)
	{
		if (hits == null)
		{
			return;
		}

		NormalizeRestDensity(hits);
		if (hits.Count == 0)
		{
			hits.Add(HitType.Left);
			hits.Add(HitType.Right);
		}

		int targetSlots = GetNormalizedSlotCount(hits.Count);
		List<HitType> sourceHits = new List<HitType>(hits);
		int sourceIndex = 0;
		while (hits.Count < targetSlots)
		{
			HitType nextHit = sourceHits[sourceIndex % sourceHits.Count];
			if (CanAppendNormalizedHit(hits, nextHit, targetSlots))
			{
				hits.Add(nextHit);
			}
			sourceIndex++;
		}
	}

	private static void NormalizeRestDensity(List<HitType> hits)
	{
		for (int i = hits.Count - 1; i >= 0; i--)
		{
			bool edgeRest = hits[i] == HitType.Rest && (i == 0 || i == hits.Count - 1);
			bool doubleRest = hits[i] == HitType.Rest && i > 0 && hits[i - 1] == HitType.Rest;
			if (edgeRest || doubleRest)
			{
				hits.RemoveAt(i);
			}
		}
	}

	private static bool CanAppendNormalizedHit(List<HitType> hits, HitType nextHit, int targetSlots)
	{
		if (nextHit != HitType.Rest)
		{
			return true;
		}

		if (hits.Count == 0 || hits[hits.Count - 1] == HitType.Rest)
		{
			return false;
		}

		int restCount = 0;
		foreach (HitType hit in hits)
		{
			if (hit == HitType.Rest)
			{
				restCount++;
			}
		}

		int maxRestCount = Math.Max(1, targetSlots / 4);
		return restCount < maxRestCount;
	}

	private static int GetNormalizedSlotCount(int count)
	{
		// Rhythm-партия должна занимать полный 8-slot бар; короткие фразы повторяются до бара.
		if (count <= 8)
		{
			return 8;
		}

		// Для длинных паттернов выравниваем до ближайного полного бара по 8 слотов.
		int bars = (count + 7) / 8;
		return bars * 8;
	}

	private static string FormatHitType(HitType hitType)
	{
		if (hitType == HitType.Left)
		{
			return "l";
		}

		if (hitType == HitType.Right)
		{
			return "r";
		}

		if (hitType == HitType.AccentLeft)
		{
			return "L";
		}

		if (hitType == HitType.AccentRight)
		{
			return "R";
		}

		return "_";
	}
}
