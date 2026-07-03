using Godot;
using System;
using System.Collections.Generic;

public partial class PatternExecutor : Node
{
	// Базовый урон за идеальный удар до умножения.
	[Export(PropertyHint.Range, "1,99,1")]
	public int PerfectBaseDamage { get; set; } = 2;

	// Базовый урон за хороший удар до умножения.
	[Export(PropertyHint.Range, "1,99,1")]
	public int GoodBaseDamage { get; set; } = 1;

	// Шаг роста множителя: multiplier = 1 + (streak / N).
	[Export(PropertyHint.Range, "1,16,1")]
	public int StreakStepN { get; set; } = 4;

	// Рост силы streak за Perfect.
	[Export(PropertyHint.Range, "1,4,1")]
	public int PerfectStreakGain { get; set; } = 2;

	// Рост силы streak за Good.
	[Export(PropertyHint.Range, "1,3,1")]
	public int GoodStreakGain { get; set; } = 1;

	// Дополнительный урон за акцентный удар.
	[Export(PropertyHint.Range, "0,5,1")]
	public int AccentDamageBonus { get; set; } = 1;

	// Дополнительный рост streak за акцентный удар.
	[Export(PropertyHint.Range, "0,4,1")]
	public int AccentStreakBonus { get; set; } = 1;

	// Более узкое окно акцентного удара.
	[Export(PropertyHint.Range, "0.005,0.12,0.005")]
	public double AccentPerfectWindowSeconds { get; set; } = 0.045;

	// Временный переключатель: если выключен, акценты работают как обычные L/R.
	[Export]
	public bool UseAccentLogic { get; set; } = false;

	// Максимальный размер активной боевой линии. Чистится только на безопасной границе цикла.
	[Export(PropertyHint.Range, "8,128,8")]
	public int MaxActiveTimelineSlots { get; set; } = 32;

	public int Streak { get; private set; }
	public int StreakPower { get; private set; }
	public int Multiplier => 1 + (StreakPower / Math.Max(1, StreakStepN));
	public Enemy ActiveEnemy => GetCurrentSlotTarget() ?? _activeEnemy;
	public bool HasActivePattern => _activeTimelineSlots.Count > 0;
	public int ActiveTimelineSlotCount => _activeTimelineSlots.Count;
	public int NextExpectedStepIndex => _nextExpectedStepIndex;
	public bool IsAtTimelineLoopStart => _activeTimelineSlots.Count == 0 || _nextHitIndex == 0;
	public bool HasAliveTargetInTimeline
	{
		get
		{
			foreach (PatternTimelineSlot slot in _activeTimelineSlots)
			{
				if (slot.Target != null && slot.Target.IsAlive)
				{
					return true;
				}
			}

			return false;
		}
	}

	private readonly List<PatternTimelineSlot> _activeTimelineSlots = new List<PatternTimelineSlot>();
	private Enemy _activeEnemy;
	private int _nextHitIndex;
	private int _lastSuccessfulStepIndex = -1;
	private int _lastAdvancedStepIndex = -1;
	private int _lastProcessedInputStepIndex = -1;
	private int _nextExpectedStepIndex = -1;
	private readonly Dictionary<Enemy, int> _pendingPhraseDamage = new Dictionary<Enemy, int>();

	private readonly struct PatternTimelineSlot
	{
		public PatternTimelineSlot(HitType hit, Enemy target)
		{
			Hit = hit;
			Target = target;
		}

		public HitType Hit { get; }
		public Enemy Target { get; }
	}

	public void SetActivePattern(RhythmPattern pattern)
	{
		SetActivePattern(pattern, startStepIndex: 0, leadInSteps: 0);
	}

	public void SetActivePattern(RhythmPattern pattern, int startStepIndex, int leadInSteps)
	{
		_activeTimelineSlots.Clear();
		_nextHitIndex = 0;
		_activeEnemy = null;
		_lastSuccessfulStepIndex = -1;
		_lastAdvancedStepIndex = -1;
		_lastProcessedInputStepIndex = -1;
		_nextExpectedStepIndex = -1;
		_pendingPhraseDamage.Clear();

		if (pattern == null || pattern.OwnerEnemy == null || pattern.Hits.Count == 0)
		{
			return;
		}

		_activeEnemy = pattern.OwnerEnemy;
		foreach (HitType hit in pattern.Hits)
		{
			_activeTimelineSlots.Add(new PatternTimelineSlot(hit, pattern.OwnerEnemy));
		}
		_nextExpectedStepIndex = Math.Max(0, startStepIndex + Math.Max(0, leadInSteps));
		_lastAdvancedStepIndex = _nextExpectedStepIndex - 1;
	}

	public void AppendPattern(RhythmPattern pattern)
	{
		if (pattern == null || pattern.OwnerEnemy == null || pattern.Hits.Count == 0)
		{
			return;
		}

		if (_activeEnemy == null)
		{
			_activeEnemy = pattern.OwnerEnemy;
		}

		foreach (HitType hit in pattern.Hits)
		{
			_activeTimelineSlots.Add(new PatternTimelineSlot(hit, pattern.OwnerEnemy));
		}
	}

	public void SetRestBar(int startStepIndex, int leadInSteps, int slotCount)
	{
		_activeTimelineSlots.Clear();
		_nextHitIndex = 0;
		_activeEnemy = null;
		_lastSuccessfulStepIndex = -1;
		_lastAdvancedStepIndex = -1;
		_lastProcessedInputStepIndex = -1;
		_nextExpectedStepIndex = Math.Max(0, startStepIndex + Math.Max(0, leadInSteps));
		_lastAdvancedStepIndex = _nextExpectedStepIndex - 1;
		AppendRestBar(slotCount);
	}

	public void AppendRestBar(int slotCount)
	{
		int count = Math.Max(1, slotCount);
		for (int i = 0; i < count; i++)
		{
			_activeTimelineSlots.Add(new PatternTimelineSlot(HitType.Rest, null));
		}
	}

	public bool TryCleanupTimelineAtLoopStart()
	{
		if (_activeTimelineSlots.Count == 0 || _nextHitIndex != 0)
		{
			return false;
		}

		int beforeCount = _activeTimelineSlots.Count;
		for (int i = _activeTimelineSlots.Count - 1; i >= 0; i--)
		{
			Enemy target = _activeTimelineSlots[i].Target;
			if (target == null || !target.IsAlive)
			{
				_activeTimelineSlots.RemoveAt(i);
			}
		}

		int maxSlots = Math.Max(8, MaxActiveTimelineSlots);
		if (_activeTimelineSlots.Count > maxSlots)
		{
			int removeCount = _activeTimelineSlots.Count - maxSlots;
			_activeTimelineSlots.RemoveRange(0, removeCount);
		}

		if (_activeTimelineSlots.Count == 0)
		{
			_activeEnemy = null;
			_nextHitIndex = 0;
			_lastSuccessfulStepIndex = -1;
			return beforeCount > 0;
		}

		_activeEnemy = GetFirstAliveTargetInTimeline();
		return _activeTimelineSlots.Count != beforeCount;
	}

	public bool RemoveTargetFromTimeline(Enemy target)
	{
		if (target == null || _activeTimelineSlots.Count == 0)
		{
			return false;
		}

		int beforeCount = _activeTimelineSlots.Count;
		for (int i = _activeTimelineSlots.Count - 1; i >= 0; i--)
		{
			if (_activeTimelineSlots[i].Target != target)
			{
				continue;
			}

			_activeTimelineSlots.RemoveAt(i);
			if (i < _nextHitIndex)
			{
				_nextHitIndex--;
			}
		}

		if (_activeTimelineSlots.Count == beforeCount)
		{
			return false;
		}

		ClearPhraseDamage(target);
		if (_activeTimelineSlots.Count == 0)
		{
			_activeEnemy = null;
			_nextHitIndex = 0;
			_nextExpectedStepIndex = -1;
			_lastSuccessfulStepIndex = -1;
			_lastProcessedInputStepIndex = -1;
			return true;
		}

		_nextHitIndex = Mathf.Clamp(_nextHitIndex, 0, _activeTimelineSlots.Count - 1);
		_activeEnemy = GetCurrentSlotTarget() ?? GetFirstAliveTargetInTimeline();
		return true;
	}

	public bool TryGetExpectedHit(out HitType expectedHit)
	{
		if (!HasActivePattern)
		{
			expectedHit = HitType.Left;
			return false;
		}

		expectedHit = GetCurrentSlotHit();
		return true;
	}

	public bool TryGetCurrentAndNextHits(out HitType currentHit, out HitType nextHit, out bool hasNextHit)
	{
		if (!HasActivePattern)
		{
			currentHit = HitType.Left;
			nextHit = HitType.Left;
			hasNextHit = false;
			return false;
		}

		currentHit = GetCurrentSlotHit();
		if (_activeTimelineSlots.Count <= 1)
		{
			nextHit = currentHit;
			hasNextHit = false;
			return true;
		}

		int nextIndex = (_nextHitIndex + 1) % _activeTimelineSlots.Count;
		nextHit = _activeTimelineSlots[nextIndex].Hit;
		hasNextHit = true;
		return true;
	}

	public bool TryGetTimeline(out List<HitType> notes, out int currentIndex)
	{
		notes = new List<HitType>();
		currentIndex = 0;

		if (!HasActivePattern)
		{
			return false;
		}

		foreach (PatternTimelineSlot slot in _activeTimelineSlots)
		{
			notes.Add(slot.Hit);
		}
		currentIndex = Mathf.Clamp(_nextHitIndex, 0, Math.Max(0, _activeTimelineSlots.Count - 1));
		return notes.Count > 0;
	}

	public PatternStepResult ProcessHit(HitType inputHit, HitResult hitResult, int stepIndex, double timingOffsetAbsSeconds)
	{
		if (_activeTimelineSlots.Count == 0)
		{
			return PatternStepResult.NoActivePattern;
		}

		Enemy targetEnemy = GetCurrentSlotTarget();

		if (_nextExpectedStepIndex >= 0 && stepIndex >= 0 && stepIndex < _nextExpectedStepIndex)
		{
			return new PatternStepResult(
				success: false,
				patternCompleted: false,
				damageApplied: 0,
				streakAfterHit: Streak,
				multiplierAfterHit: Multiplier,
				failReason: PatternFailReason.InputBeforeExpectedStepIgnored,
				expectedHit: GetCurrentSlotHit(),
				streakContinued: true,
				wasAccent: false,
				damageTarget: null);
		}

		// Анти-спам: в рамках одного ритм-шага учитываем только одну попытку ввода.
		if (stepIndex >= 0 && stepIndex == _lastProcessedInputStepIndex)
		{
			return new PatternStepResult(
				success: false,
				patternCompleted: false,
				damageApplied: 0,
				streakAfterHit: Streak,
				multiplierAfterHit: Multiplier,
				failReason: PatternFailReason.DuplicateInputIgnored,
				expectedHit: GetCurrentSlotHit(),
				streakContinued: true,
				wasAccent: false,
				damageTarget: null);
		}

		HitType expectedHit = GetCurrentSlotHit();
		bool expectedAccent = IsAccent(expectedHit);
		bool timingInvalid = hitResult == HitResult.Miss
			|| (UseAccentLogic && expectedAccent && timingOffsetAbsSeconds > AccentPerfectWindowSeconds);
		bool pauseInput = expectedHit == HitType.Rest;
		bool sequenceInvalid = !IsInputMatchingExpected(inputHit, expectedHit);

		if (pauseInput || timingInvalid || sequenceInvalid)
		{
			PatternFailReason failReason = pauseInput
				? PatternFailReason.PauseInput
				: (timingInvalid ? PatternFailReason.TimingMiss : PatternFailReason.SequenceMiss);
			return new PatternStepResult(
				success: false,
				patternCompleted: false,
				damageApplied: 0,
				streakAfterHit: Streak,
				multiplierAfterHit: Multiplier,
				failReason: failReason,
				expectedHit: expectedHit,
				streakContinued: true,
				wasAccent: false,
				damageTarget: null);
		}

		// Если партия не идёт непрерывно по битам, streak сбрасывается перед новым ударом.
		if (stepIndex >= 0)
		{
			_lastProcessedInputStepIndex = stepIndex;
		}

		bool streakContinued = IsContinuousStepChain(stepIndex);
		if (!streakContinued)
		{
			ResetStreak();
		}

		int baseDamage = hitResult == HitResult.Perfect ? PerfectBaseDamage : GoodBaseDamage;
		if (expectedAccent)
		{
			baseDamage += AccentDamageBonus;
		}
		int appliedDamage = baseDamage * Multiplier;
		if (targetEnemy != null && targetEnemy.IsAlive)
		{
			QueuePhraseDamage(targetEnemy, appliedDamage);
		}
		else
		{
			appliedDamage = 0;
		}

		// Улучшенный streak: качество удара влияет на скорость роста множителя.
		Streak += 1;
		int streakGain = hitResult == HitResult.Perfect ? PerfectStreakGain : GoodStreakGain;
		if (expectedAccent)
		{
			streakGain += AccentStreakBonus;
		}
		StreakPower += streakGain;
		_lastSuccessfulStepIndex = stepIndex;
		Enemy previousTarget = targetEnemy;
		bool patternCompleted = AdvanceExpectedSlot();
		Enemy nextTarget = GetCurrentSlotTarget();
		int damageAppliedNow = 0;
		Enemy damageTarget = null;
		if (previousTarget != null && (patternCompleted || nextTarget != previousTarget))
		{
			damageAppliedNow = FlushPhraseDamage(previousTarget);
			if (damageAppliedNow > 0)
			{
				damageTarget = previousTarget;
			}
		}

		return new PatternStepResult(
			success: true,
			patternCompleted: patternCompleted,
			damageApplied: damageAppliedNow,
			streakAfterHit: Streak,
			multiplierAfterHit: Multiplier,
			failReason: PatternFailReason.None,
			expectedHit: expectedHit,
			streakContinued: streakContinued,
			wasAccent: expectedAccent,
			damageTarget: damageTarget);
	}

	public PatternTimelineResult AdvanceTimeline(int stepIndex)
	{
		if (_activeTimelineSlots.Count == 0 || stepIndex < 0)
		{
			return PatternTimelineResult.None;
		}

		bool restAdvanced = false;
		bool patternCompleted = false;
		bool missedRequiredHit = false;
		HitType missedHit = HitType.Left;

		while (_nextExpectedStepIndex >= 0 && _nextExpectedStepIndex < stepIndex)
		{
			HitType expectedHit = GetCurrentSlotHit();
			Enemy targetEnemy = GetCurrentSlotTarget();
			if (expectedHit == HitType.Rest)
			{
				restAdvanced = true;
				patternCompleted |= AdvanceExpectedSlot();
			}
			else
			{
				missedRequiredHit = true;
				missedHit = expectedHit;
				ResetStreak();
				ClearPhraseDamage(targetEnemy);
				_lastSuccessfulStepIndex = -1;
				if (targetEnemy != null && targetEnemy.IsAlive)
				{
					targetEnemy.Counterattack();
				}
				patternCompleted |= AdvanceExpectedSlot();
			}
		}

		return new PatternTimelineResult(restAdvanced, patternCompleted, missedRequiredHit, missedHit);
	}

	private void ResetStreak()
	{
		Streak = 0;
		StreakPower = 0;
	}

	private void QueuePhraseDamage(Enemy enemy, int damage)
	{
		if (enemy == null || damage <= 0)
		{
			return;
		}

		_pendingPhraseDamage.TryGetValue(enemy, out int currentDamage);
		_pendingPhraseDamage[enemy] = currentDamage + damage;
	}

	private int FlushPhraseDamage(Enemy enemy)
	{
		if (enemy == null || !_pendingPhraseDamage.TryGetValue(enemy, out int damage))
		{
			return 0;
		}

		_pendingPhraseDamage.Remove(enemy);
		if (damage <= 0 || !enemy.IsAlive)
		{
			return 0;
		}

		enemy.TakeDamage(damage);
		return damage;
	}

	private void ClearPhraseDamage(Enemy enemy)
	{
		if (enemy == null)
		{
			return;
		}

		_pendingPhraseDamage.Remove(enemy);
	}

	private bool IsContinuousStepChain(int currentStepIndex)
	{
		if (currentStepIndex < 0)
		{
			return true;
		}

		if (_lastSuccessfulStepIndex < 0)
		{
			return true;
		}

		return currentStepIndex == _lastSuccessfulStepIndex + 1;
	}

	private bool AdvanceExpectedSlot()
	{
		if (_nextExpectedStepIndex >= 0)
		{
			_lastAdvancedStepIndex = Math.Max(_lastAdvancedStepIndex, _nextExpectedStepIndex);
			_nextExpectedStepIndex++;
		}

		_nextHitIndex++;
		if (_nextHitIndex < _activeTimelineSlots.Count)
		{
			return false;
		}

		_nextHitIndex = 0;
		return true;
	}

	private HitType GetCurrentSlotHit()
	{
		if (_activeTimelineSlots.Count == 0)
		{
			return HitType.Left;
		}

		int index = Mathf.Clamp(_nextHitIndex, 0, _activeTimelineSlots.Count - 1);
		return _activeTimelineSlots[index].Hit;
	}

	private Enemy GetCurrentSlotTarget()
	{
		if (_activeTimelineSlots.Count == 0)
		{
			return null;
		}

		int index = Mathf.Clamp(_nextHitIndex, 0, _activeTimelineSlots.Count - 1);
		return _activeTimelineSlots[index].Target;
	}

	private Enemy GetFirstAliveTargetInTimeline()
	{
		foreach (PatternTimelineSlot slot in _activeTimelineSlots)
		{
			if (slot.Target != null && slot.Target.IsAlive)
			{
				return slot.Target;
			}
		}

		return null;
	}

	private static bool IsAccent(HitType hitType)
	{
		return hitType == HitType.AccentLeft || hitType == HitType.AccentRight;
	}

	private static bool IsInputMatchingExpected(HitType inputHit, HitType expectedHit)
	{
		if (expectedHit == HitType.Left || expectedHit == HitType.AccentLeft)
		{
			return inputHit == HitType.Left;
		}

		if (expectedHit == HitType.Right || expectedHit == HitType.AccentRight)
		{
			return inputHit == HitType.Right;
		}

		return inputHit == expectedHit;
	}
}

public readonly struct PatternStepResult
{
	public static PatternStepResult NoActivePattern => new PatternStepResult(
		success: false,
		patternCompleted: false,
		damageApplied: 0,
		streakAfterHit: 0,
		multiplierAfterHit: 1,
		failReason: PatternFailReason.NoActivePattern,
		expectedHit: HitType.Left,
		streakContinued: false,
		wasAccent: false,
		damageTarget: null);

	public PatternStepResult(
		bool success,
		bool patternCompleted,
		int damageApplied,
		int streakAfterHit,
		int multiplierAfterHit,
		PatternFailReason failReason,
		HitType expectedHit,
		bool streakContinued,
		bool wasAccent,
		Enemy damageTarget)
	{
		Success = success;
		PatternCompleted = patternCompleted;
		DamageApplied = damageApplied;
		StreakAfterHit = streakAfterHit;
		MultiplierAfterHit = multiplierAfterHit;
		FailReason = failReason;
		ExpectedHit = expectedHit;
		StreakContinued = streakContinued;
		WasAccent = wasAccent;
		DamageTarget = damageTarget;
	}

	public bool Success { get; }
	public bool PatternCompleted { get; }
	public int DamageApplied { get; }
	public int StreakAfterHit { get; }
	public int MultiplierAfterHit { get; }
	public PatternFailReason FailReason { get; }
	public HitType ExpectedHit { get; }
	public bool StreakContinued { get; }
	public bool WasAccent { get; }
	public Enemy DamageTarget { get; }
}

public enum PatternFailReason
{
	None = 0,
	NoActivePattern = 1,
	TimingMiss = 2,
	SequenceMiss = 3,
	DuplicateInputIgnored = 4,
	PauseInput = 5,
	MissedRequiredHit = 6,
	InputBeforeExpectedStepIgnored = 7
}

public readonly struct PatternTimelineResult
{
	public static PatternTimelineResult None => new PatternTimelineResult(false, false, false, HitType.Left);

	public PatternTimelineResult(bool restAdvanced, bool patternCompleted, bool missedRequiredHit, HitType missedHit)
	{
		RestAdvanced = restAdvanced;
		PatternCompleted = patternCompleted;
		MissedRequiredHit = missedRequiredHit;
		MissedHit = missedHit;
	}

	public bool RestAdvanced { get; }
	public bool PatternCompleted { get; }
	public bool MissedRequiredHit { get; }
	public HitType MissedHit { get; }
}
