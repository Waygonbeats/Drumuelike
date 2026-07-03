using Godot;
using System;
using System.Collections.Generic;

public enum RhythmSystemMode
{
	LegacyPattern = 0,
	DawTimelineExperiment = 1
}

public enum DawTimelineFailReason
{
	None = 0,
	NoActivePattern = 1,
	TimingMiss = 2,
	SequenceMiss = 3,
	DuplicateInputIgnored = 4
}

public enum DawTimelineNoteState
{
	Pending = 0,
	Hit = 1,
	Missed = 2
}

public sealed class DawTimelineSettings
{
	public double PerfectWindowSeconds { get; init; } = 0.06;
	public double GoodWindowSeconds { get; init; } = 0.12;
	public double InputLatencyOffsetMs { get; init; } = 0.0;
	public int PerfectBaseDamage { get; init; } = 2;
	public int GoodBaseDamage { get; init; } = 1;
	public int StreakStepN { get; init; } = 4;
	public int PerfectStreakGain { get; init; } = 2;
	public int GoodStreakGain { get; init; } = 1;
	public int AccentDamageBonus { get; init; } = 1;
	public int AccentStreakBonus { get; init; } = 1;
	public double AccentPerfectWindowSeconds { get; init; } = 0.045;
	public bool UseAccentLogic { get; init; }
}

public readonly struct DawTimelineInputResult
{
	public static DawTimelineInputResult NoActivePattern => new DawTimelineInputResult(
		consumed: false,
		success: false,
		hitResult: HitResult.Miss,
		failReason: DawTimelineFailReason.NoActivePattern,
		expectedHit: HitType.Left,
		offsetSeconds: null,
		patternCompleted: false,
		damageApplied: 0,
		damageTarget: null,
		wasAccent: false,
		streakAfterHit: 0,
		multiplierAfterHit: 1,
		streakContinued: false);

	public DawTimelineInputResult(
		bool consumed,
		bool success,
		HitResult hitResult,
		DawTimelineFailReason failReason,
		HitType expectedHit,
		double? offsetSeconds,
		bool patternCompleted,
		int damageApplied,
		Enemy damageTarget,
		bool wasAccent,
		int streakAfterHit,
		int multiplierAfterHit,
		bool streakContinued)
	{
		Consumed = consumed;
		Success = success;
		HitResult = hitResult;
		FailReason = failReason;
		ExpectedHit = expectedHit;
		OffsetSeconds = offsetSeconds;
		PatternCompleted = patternCompleted;
		DamageApplied = damageApplied;
		DamageTarget = damageTarget;
		WasAccent = wasAccent;
		StreakAfterHit = streakAfterHit;
		MultiplierAfterHit = multiplierAfterHit;
		StreakContinued = streakContinued;
	}

	public bool Consumed { get; }
	public bool Success { get; }
	public HitResult HitResult { get; }
	public DawTimelineFailReason FailReason { get; }
	public HitType ExpectedHit { get; }
	public double? OffsetSeconds { get; }
	public bool PatternCompleted { get; }
	public int DamageApplied { get; }
	public Enemy DamageTarget { get; }
	public bool WasAccent { get; }
	public int StreakAfterHit { get; }
	public int MultiplierAfterHit { get; }
	public bool StreakContinued { get; }
}

public readonly struct DawTimelineAdvanceResult
{
	public static DawTimelineAdvanceResult None => new DawTimelineAdvanceResult(
		missedRequiredHit: false,
		missedHit: HitType.Left,
		patternCompleted: false,
		damageApplied: 0,
		damageTarget: null);

	public DawTimelineAdvanceResult(
		bool missedRequiredHit,
		HitType missedHit,
		bool patternCompleted,
		int damageApplied,
		Enemy damageTarget)
	{
		MissedRequiredHit = missedRequiredHit;
		MissedHit = missedHit;
		PatternCompleted = patternCompleted;
		DamageApplied = damageApplied;
		DamageTarget = damageTarget;
	}

	public bool MissedRequiredHit { get; }
	public HitType MissedHit { get; }
	public bool PatternCompleted { get; }
	public int DamageApplied { get; }
	public Enemy DamageTarget { get; }
}

public sealed class DawTimelineEngine
{
	private sealed class TimelineSlot
	{
		public TimelineSlot(HitType hit, int relativeStepIndex)
		{
			Hit = hit;
			RelativeStepIndex = relativeStepIndex;
			State = DawTimelineNoteState.Pending;
		}

		public HitType Hit { get; }
		public int RelativeStepIndex { get; }
		public DawTimelineNoteState State { get; set; }
		public double? OffsetSeconds { get; set; }
		public bool IsPlayable => Hit != HitType.Rest;
		public bool IsAccent => Hit == HitType.AccentLeft || Hit == HitType.AccentRight;
	}

	private readonly List<TimelineSlot> _slots = new List<TimelineSlot>();
	private readonly List<HitType> _patternHits = new List<HitType>();
	private RhythmManager _rhythmManager;
	private DawTimelineSettings _settings = new DawTimelineSettings();
	private Enemy _activeEnemy;
	private int _cycleStartStepIndex;
	private int _clipLengthSteps;
	private int _lastProcessedInputStepIndex = -1;
	private int _lastSuccessfulTimelineStepIndex = -1;
	private int _pendingPhraseDamage;
	private bool _phraseDamageFlushedThisCycle;

	public Enemy ActiveEnemy => _activeEnemy;
	public bool HasActivePattern => _activeEnemy != null && _slots.Count > 0;
	public int ActiveTimelineSlotCount => _slots.Count;
	public int Streak { get; private set; }
	public int StreakPower { get; private set; }
	public int Multiplier => 1 + (StreakPower / Math.Max(1, _settings.StreakStepN));

	public void Initialize(RhythmManager rhythmManager, DawTimelineSettings settings)
	{
		_rhythmManager = rhythmManager;
		_settings = settings ?? new DawTimelineSettings();
	}

	public void Clear()
	{
		_slots.Clear();
		_patternHits.Clear();
		_activeEnemy = null;
		_cycleStartStepIndex = 0;
		_clipLengthSteps = 0;
		_lastProcessedInputStepIndex = -1;
		_lastSuccessfulTimelineStepIndex = -1;
		_pendingPhraseDamage = 0;
		_phraseDamageFlushedThisCycle = false;
		ResetStreak();
	}

	public void SetActivePattern(Enemy enemy, IReadOnlyList<HitType> hits, int currentStepIndex, int leadInSteps)
	{
		Clear();
		if (_rhythmManager == null || enemy == null || hits == null || hits.Count == 0)
		{
			return;
		}

		_activeEnemy = enemy;
		_cycleStartStepIndex = Math.Max(0, currentStepIndex + Math.Max(0, leadInSteps));
		_clipLengthSteps = Math.Max(1, hits.Count);
		foreach (HitType hit in hits)
		{
			_patternHits.Add(hit);
		}

		RebuildSlots();
	}

	public DawTimelineInputResult ProcessInput(HitType inputHit)
	{
		if (!HasActivePattern || _rhythmManager == null)
		{
			return DawTimelineInputResult.NoActivePattern;
		}

		int currentStepIndex = _rhythmManager.CurrentStepIndex;
		double adjustedSongTime = _rhythmManager.SongTimeSeconds + _settings.InputLatencyOffsetMs * 0.001;
		if (currentStepIndex >= 0 && currentStepIndex == _lastProcessedInputStepIndex)
		{
			return new DawTimelineInputResult(
				consumed: false,
				success: false,
				hitResult: HitResult.Miss,
				failReason: DawTimelineFailReason.DuplicateInputIgnored,
				expectedHit: GetClosestExpectedHit(),
				offsetSeconds: null,
				patternCompleted: false,
				damageApplied: 0,
				damageTarget: null,
				wasAccent: false,
				streakAfterHit: Streak,
				multiplierAfterHit: Multiplier,
				streakContinued: true);
		}

		TimelineSlot bestMatch = null;
		double bestOffsetSeconds = 0.0;
		double bestDistance = double.MaxValue;
		TimelineSlot nearestNote = null;
		double nearestDistance = double.MaxValue;

		foreach (TimelineSlot slot in _slots)
		{
			if (!slot.IsPlayable || slot.State != DawTimelineNoteState.Pending)
			{
				continue;
			}

			double noteTime = GetSlotTimeSeconds(slot);
			double signedOffset = adjustedSongTime - noteTime;
			double absoluteOffset = Math.Abs(signedOffset);
			if (absoluteOffset < nearestDistance)
			{
				nearestDistance = absoluteOffset;
				nearestNote = slot;
			}

			if (!IsInputMatchingExpected(inputHit, slot.Hit) || absoluteOffset > _settings.GoodWindowSeconds)
			{
				continue;
			}

			if (absoluteOffset < bestDistance)
			{
				bestDistance = absoluteOffset;
				bestOffsetSeconds = signedOffset;
				bestMatch = slot;
			}
		}

		if (bestMatch == null)
		{
			DawTimelineFailReason failReason = nearestNote != null && nearestDistance <= _settings.GoodWindowSeconds
				? DawTimelineFailReason.SequenceMiss
				: DawTimelineFailReason.TimingMiss;
			return new DawTimelineInputResult(
				consumed: true,
				success: false,
				hitResult: HitResult.Miss,
				failReason: failReason,
				expectedHit: nearestNote?.Hit ?? GetClosestExpectedHit(),
				offsetSeconds: nearestNote != null ? adjustedSongTime - GetSlotTimeSeconds(nearestNote) : null,
				patternCompleted: false,
				damageApplied: 0,
				damageTarget: null,
				wasAccent: false,
				streakAfterHit: Streak,
				multiplierAfterHit: Multiplier,
				streakContinued: true);
		}

		HitResult hitResult = JudgeOffset(bestOffsetSeconds, bestMatch.IsAccent);
		if (hitResult == HitResult.Miss)
		{
			return new DawTimelineInputResult(
				consumed: true,
				success: false,
				hitResult: HitResult.Miss,
				failReason: DawTimelineFailReason.TimingMiss,
				expectedHit: bestMatch.Hit,
				offsetSeconds: bestOffsetSeconds,
				patternCompleted: false,
				damageApplied: 0,
				damageTarget: null,
				wasAccent: false,
				streakAfterHit: Streak,
				multiplierAfterHit: Multiplier,
				streakContinued: true);
		}

		_lastProcessedInputStepIndex = currentStepIndex;
		int absoluteNoteStepIndex = _cycleStartStepIndex + bestMatch.RelativeStepIndex;
		bool streakContinued = IsContinuousStepChain(absoluteNoteStepIndex);
		if (!streakContinued)
		{
			ResetStreak();
		}

		bestMatch.State = DawTimelineNoteState.Hit;
		bestMatch.OffsetSeconds = bestOffsetSeconds;

		int baseDamage = hitResult == HitResult.Perfect ? _settings.PerfectBaseDamage : _settings.GoodBaseDamage;
		if (bestMatch.IsAccent)
		{
			baseDamage += _settings.AccentDamageBonus;
		}

		Streak += 1;
		int streakGain = hitResult == HitResult.Perfect ? _settings.PerfectStreakGain : _settings.GoodStreakGain;
		if (bestMatch.IsAccent)
		{
			streakGain += _settings.AccentStreakBonus;
		}
		StreakPower += streakGain;
		_lastSuccessfulTimelineStepIndex = absoluteNoteStepIndex;
		_pendingPhraseDamage += baseDamage * Multiplier;

		bool patternCompleted = false;
		int damageApplied = 0;
		Enemy damageTarget = null;
		if (!_phraseDamageFlushedThisCycle && AreAllPlayableNotesResolved())
		{
			_phraseDamageFlushedThisCycle = true;
			damageApplied = FlushPendingPhraseDamage();
			if (damageApplied > 0)
			{
				damageTarget = _activeEnemy;
			}
			patternCompleted = true;
		}

		return new DawTimelineInputResult(
			consumed: true,
			success: true,
			hitResult: hitResult,
			failReason: DawTimelineFailReason.None,
			expectedHit: bestMatch.Hit,
			offsetSeconds: bestOffsetSeconds,
			patternCompleted: patternCompleted,
			damageApplied: damageApplied,
			damageTarget: damageTarget,
			wasAccent: bestMatch.IsAccent,
			streakAfterHit: Streak,
			multiplierAfterHit: Multiplier,
			streakContinued: streakContinued);
	}

	public DawTimelineAdvanceResult AdvanceTimeline()
	{
		if (!HasActivePattern || _rhythmManager == null)
		{
			return DawTimelineAdvanceResult.None;
		}

		double adjustedSongTime = _rhythmManager.SongTimeSeconds + _settings.InputLatencyOffsetMs * 0.001;
		bool missedRequiredHit = false;
		HitType missedHit = HitType.Left;
		foreach (TimelineSlot slot in _slots)
		{
			if (!slot.IsPlayable || slot.State != DawTimelineNoteState.Pending)
			{
				continue;
			}

			double noteTime = GetSlotTimeSeconds(slot);
			if (adjustedSongTime <= noteTime + _settings.GoodWindowSeconds)
			{
				continue;
			}

			slot.State = DawTimelineNoteState.Missed;
			slot.OffsetSeconds = adjustedSongTime - noteTime;
			missedRequiredHit = true;
			missedHit = slot.Hit;
			_pendingPhraseDamage = 0;
			_phraseDamageFlushedThisCycle = true;
			ResetStreak();
			if (_activeEnemy != null && _activeEnemy.IsAlive)
			{
				_activeEnemy.Counterattack();
			}
		}

		bool cycleEnded = adjustedSongTime >= GetCycleEndTimeSeconds();
		if (!cycleEnded)
		{
			return missedRequiredHit
				? new DawTimelineAdvanceResult(missedRequiredHit, missedHit, false, 0, null)
				: DawTimelineAdvanceResult.None;
		}

		bool patternCompleted = _phraseDamageFlushedThisCycle;
		AdvanceToNextCycle();
		return new DawTimelineAdvanceResult(
			missedRequiredHit: missedRequiredHit,
			missedHit: missedHit,
			patternCompleted: patternCompleted,
			damageApplied: 0,
			damageTarget: null);
	}

	public bool TryGetTimeline(out List<HitType> notes, out int currentIndex)
	{
		notes = new List<HitType>(_patternHits);
		currentIndex = 0;
		if (!HasActivePattern || notes.Count == 0 || _rhythmManager == null)
		{
			return false;
		}

		int relativeIndex = _rhythmManager.CurrentStepIndex - _cycleStartStepIndex;
		if (relativeIndex < 0)
		{
			currentIndex = 0;
		}
		else
		{
			currentIndex = Mathf.Clamp(relativeIndex, 0, Math.Max(0, notes.Count - 1));
		}

		return true;
	}

	public bool TryGetCurrentAndNextHits(out HitType currentHit, out HitType nextHit, out bool hasNextHit)
	{
		currentHit = HitType.Left;
		nextHit = HitType.Left;
		hasNextHit = false;
		if (!HasActivePattern || _patternHits.Count == 0 || _rhythmManager == null)
		{
			return false;
		}

		int currentIndex = Mathf.Clamp(_rhythmManager.CurrentStepIndex - _cycleStartStepIndex, 0, _patternHits.Count - 1);
		currentHit = _patternHits[currentIndex];
		for (int i = currentIndex + 1; i < _patternHits.Count; i++)
		{
			if (_patternHits[i] == HitType.Rest)
			{
				continue;
			}

			nextHit = _patternHits[i];
			hasNextHit = true;
			return true;
		}

		nextHit = currentHit;
		return true;
	}

	private void RebuildSlots()
	{
		_slots.Clear();
		for (int i = 0; i < _patternHits.Count; i++)
		{
			_slots.Add(new TimelineSlot(_patternHits[i], i));
		}
	}

	private double GetSlotTimeSeconds(TimelineSlot slot)
	{
		return _rhythmManager.GetStepTimeSeconds(_cycleStartStepIndex + slot.RelativeStepIndex);
	}

	private double GetCycleEndTimeSeconds()
	{
		return _rhythmManager.GetStepTimeSeconds(_cycleStartStepIndex + _clipLengthSteps);
	}

	private void AdvanceToNextCycle()
	{
		_cycleStartStepIndex += Math.Max(1, _clipLengthSteps);
		_phraseDamageFlushedThisCycle = false;
		_pendingPhraseDamage = 0;
		foreach (TimelineSlot slot in _slots)
		{
			slot.State = DawTimelineNoteState.Pending;
			slot.OffsetSeconds = null;
		}
	}

	private int FlushPendingPhraseDamage()
	{
		if (_activeEnemy == null || !_activeEnemy.IsAlive || _pendingPhraseDamage <= 0)
		{
			_pendingPhraseDamage = 0;
			return 0;
		}

		int damage = _pendingPhraseDamage;
		_pendingPhraseDamage = 0;
		_activeEnemy.TakeDamage(damage);
		return damage;
	}

	private bool AreAllPlayableNotesResolved()
	{
		bool hasPlayableNotes = false;
		foreach (TimelineSlot slot in _slots)
		{
			if (!slot.IsPlayable)
			{
				continue;
			}

			hasPlayableNotes = true;
			if (slot.State == DawTimelineNoteState.Pending)
			{
				return false;
			}
		}

		return hasPlayableNotes;
	}

	private void ResetStreak()
	{
		Streak = 0;
		StreakPower = 0;
	}

	private bool IsContinuousStepChain(int currentTimelineStepIndex)
	{
		if (_lastSuccessfulTimelineStepIndex < 0)
		{
			return true;
		}

		return currentTimelineStepIndex == _lastSuccessfulTimelineStepIndex + 1;
	}

	private HitResult JudgeOffset(double offsetSeconds, bool isAccent)
	{
		double absoluteOffset = Math.Abs(offsetSeconds);
		double perfectWindow = Math.Min(_settings.PerfectWindowSeconds, _settings.GoodWindowSeconds);
		double goodWindow = _settings.GoodWindowSeconds;
		if (_settings.UseAccentLogic && isAccent)
		{
			perfectWindow = Math.Min(perfectWindow, _settings.AccentPerfectWindowSeconds);
		}

		if (absoluteOffset <= perfectWindow + 1e-6)
		{
			return HitResult.Perfect;
		}

		if (absoluteOffset <= goodWindow + 1e-6)
		{
			return HitResult.Good;
		}

		return HitResult.Miss;
	}

	private HitType GetClosestExpectedHit()
	{
		if (!HasActivePattern || _rhythmManager == null)
		{
			return HitType.Left;
		}

		TimelineSlot best = null;
		double nearestDistance = double.MaxValue;
		double adjustedSongTime = _rhythmManager.SongTimeSeconds + _settings.InputLatencyOffsetMs * 0.001;
		foreach (TimelineSlot slot in _slots)
		{
			if (!slot.IsPlayable || slot.State != DawTimelineNoteState.Pending)
			{
				continue;
			}

			double absoluteOffset = Math.Abs(adjustedSongTime - GetSlotTimeSeconds(slot));
			if (absoluteOffset < nearestDistance)
			{
				nearestDistance = absoluteOffset;
				best = slot;
			}
		}

		return best?.Hit ?? HitType.Left;
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
