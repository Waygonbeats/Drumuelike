using Godot;
using System;
using System.Collections.Generic;

public partial class CombatController : Node
{
	// Ссылка на RhythmManager для запуска и остановки ритма.
	[Export]
	public NodePath RhythmManagerPath { get; set; } = new NodePath();

	// Ссылка на InputJudge для оценки попаданий.
	[Export]
	public NodePath InputJudgePath { get; set; } = new NodePath();

	// Ссылка на Label в UI для вывода результата попадания.
	[Export]
	public NodePath RhythmFeedbackLabelPath { get; set; } = new NodePath();

	// Ссылка на view-компонент RhythmLine (только отображение).
	[Export]
	public NodePath RhythmLineViewPath { get; set; } = new NodePath("../UI/RhythmLine");

	// Ссылка на view-компонент ComboMeter (только отображение).
	[Export]
	public NodePath ComboMeterViewPath { get; set; } = new NodePath("../UI/ComboMeter");

	// Ссылка на PatternExecutor для проверки паттерна и расчёта урона.
	[Export]
	public NodePath PatternExecutorPath { get; set; } = new NodePath();

	// Ссылка на Room, внутри которого ищем врагов.
	[Export]
	public NodePath RoomPath { get; set; } = new NodePath();

	// Ссылка на Player для получения урона от врагов.
	[Export]
	public NodePath PlayerPath { get; set; } = new NodePath("../Player");

	// Плеер звука успешного удара.
	[Export]
	public NodePath HitSfxPlayerPath { get; set; } = new NodePath("../HitSfxPlayer");

	// Плеер звука промаха/ошибки.
	[Export]
	public NodePath MissSfxPlayerPath { get; set; } = new NodePath("../MissSfxPlayer");

	// Если включено, активная цель сразу следует за наведением курсора.
	[Export]
	public bool ImmediateHoverRetarget { get; set; } = false;

	// Задержка автопереключения цели при удержании курсора на враге (если режим не мгновенный).
	[Export(PropertyHint.Range, "0.0,1.0,0.01")]
	public float HoverRetargetDelaySeconds { get; set; } = 0.12f;

	// Диапазон темпа для UI-регулятора.
	[Export(PropertyHint.Range, "30,240,1")]
	public double TempoMinBpm { get; set; } = 70.0;

	[Export(PropertyHint.Range, "30,260,1")]
	public double TempoMaxBpm { get; set; } = 180.0;

	// Размер музыкального бара в slot-единицах. Должен совпадать с UI rhythm lane.
	[Export(PropertyHint.Range, "4,16,1")]
	public int BarSlotCount { get; set; } = 8;

	// Максимальная длина активной rhythm-линии перед cleanup.
	[Export(PropertyHint.Range, "8,128,8")]
	public int MaxActiveTimelineSlots { get; set; } = 32;

	// С этого размера линия считается плотной, и перед новой фразой можно вставить breath bar.
	[Export(PropertyHint.Range, "8,128,8")]
	public int DenseTimelineSlotThreshold { get; set; } = 24;

	[Export(PropertyHint.Range, "0.5,2.0,0.01")]
	public float PerfectHitPitch { get; set; } = 1.22f;

	[Export(PropertyHint.Range, "0.5,2.0,0.01")]
	public float GoodHitPitch { get; set; } = 1.0f;

	[Export(PropertyHint.Range, "0.5,2.0,0.01")]
	public float MissPitch { get; set; } = 1.28f;

	[Export(PropertyHint.Range, "-24,6,0.5")]
	public float PerfectHitVolumeDb { get; set; } = -5.0f;

	[Export(PropertyHint.Range, "-24,6,0.5")]
	public float GoodHitVolumeDb { get; set; } = -8.0f;

	[Export(PropertyHint.Range, "-24,6,0.5")]
	public float MissVolumeDb { get; set; } = -18.0f;

	[Export(PropertyHint.Range, "0.02,0.30,0.005")]
	public double MissSfxMaxDurationSeconds { get; set; } = 0.065;

	[Export(PropertyHint.Range, "0,0.08,0.005")]
	public double PerfectHitStopSeconds { get; set; } = 0.025;

	private RhythmManager _rhythmManager;
	private InputJudge _inputJudge;
	private Label _feedbackLabel;
	private RhythmLineView _rhythmLineView;
	private ComboMeterView _comboMeterView;
	private PatternExecutor _patternExecutor;
	private Node _roomNode;
	private Player _player;
	private AudioStreamPlayer _hitSfxPlayer;
	private AudioStreamPlayer _missSfxPlayer;
	private bool _combatActive;
	private readonly List<Enemy> _enemies = new List<Enemy>();
	private readonly List<Enemy> _subscribedEnemies = new List<Enemy>();
	private int _activeEnemyIndex = -1;
	private Enemy _hoveredEnemy;
	private Enemy _lastHoverCandidate;
	private float _hoverCandidateTime;
	private HitResult? _lastUiHitResult;
	private double? _lastUiTimingOffsetSeconds;
	private float _lastUiHitResultSeconds;
	private bool _hitStopActive;
	private int _missSfxPlayVersion;
	private readonly Queue<List<HitType>> _memoryPatternQueue = new Queue<List<HitType>>();
	private readonly HashSet<Enemy> _queuedDefeatedEnemies = new HashSet<Enemy>();
	private readonly Queue<PendingPatternSwitch> _pendingPatternSwitches = new Queue<PendingPatternSwitch>();
	private int _lastMemoryAppendBarIndex = -1;
	private int _lastQueuedSwitchBarIndex = -1;
	private int _activePatternStartStepIndex = -1;
	private double _activePatternStartSongTimeSeconds = -1.0;

	private readonly struct PendingPatternSwitch
	{
		public PendingPatternSwitch(Enemy enemy, int enemyIndex, int targetBarIndex, bool insertBreathBefore)
		{
			Enemy = enemy;
			EnemyIndex = enemyIndex;
			TargetBarIndex = targetBarIndex;
			InsertBreathBefore = insertBreathBefore;
		}

		public Enemy Enemy { get; }
		public int EnemyIndex { get; }
		public int TargetBarIndex { get; }
		public bool InsertBreathBefore { get; }
	}

	public override void _Ready()
	{
		_rhythmManager = GetNodeOrNull<RhythmManager>(RhythmManagerPath);
		_inputJudge = GetNodeOrNull<InputJudge>(InputJudgePath);
		_feedbackLabel = GetNodeOrNull<Label>(RhythmFeedbackLabelPath);
		_patternExecutor = GetNodeOrNull<PatternExecutor>(PatternExecutorPath);
		_roomNode = GetNodeOrNull<Node>(RoomPath);
		_player = GetNodeOrNull<Player>(PlayerPath);
		_hitSfxPlayer = GetNodeOrNull<AudioStreamPlayer>(HitSfxPlayerPath);
		_missSfxPlayer = GetNodeOrNull<AudioStreamPlayer>(MissSfxPlayerPath);
		_rhythmLineView = GetNodeOrNull<RhythmLineView>(RhythmLineViewPath);
		_comboMeterView = GetNodeOrNull<ComboMeterView>(ComboMeterViewPath);

		// Подхватываем стандартные узлы по именам, если экспортные пути не заданы или сбились.
		_rhythmManager ??= GetNodeOrNull<RhythmManager>("../RhythmManager");
		_inputJudge ??= GetNodeOrNull<InputJudge>("../InputJudge");
		_patternExecutor ??= GetNodeOrNull<PatternExecutor>("../PatternExecutor");
		_roomNode ??= GetNodeOrNull<Node>("../Room");
		_player ??= GetNodeOrNull<Player>("../Player");
		_hitSfxPlayer ??= GetNodeOrNull<AudioStreamPlayer>("../HitSfxPlayer");
		_missSfxPlayer ??= GetNodeOrNull<AudioStreamPlayer>("../MissSfxPlayer");
		_feedbackLabel ??= GetNodeOrNull<Label>("../UI/RhythmLine/Panel/VBoxContainer/CurrentHitLabel");
		_rhythmLineView ??= GetNodeOrNull<RhythmLineView>("../UI/RhythmLine");
		_comboMeterView ??= GetNodeOrNull<ComboMeterView>("../UI/ComboMeter");
		if (_rhythmLineView != null)
		{
			_rhythmLineView.TempoChangedRequested += OnTempoChangedRequested;
		}

		// Отложенная инициализация нужна, чтобы RoomController успел заспавнить врагов.
		CallDeferred(nameof(InitializeCombatDeferred));
	}

	public void RefreshEnemiesFromRoom()
	{
		CollectEnemiesFromRoom();
		ReconnectEnemySignals();
		SetHoveredEnemy(null);

		// После смены волны старый таргет/индекс больше невалиден.
		_activeEnemyIndex = -1;
		_activePatternStartStepIndex = -1;
		_activePatternStartSongTimeSeconds = -1.0;
		_patternExecutor?.SetActivePattern(null);
		ClearPendingPatternSwitch();
		_queuedDefeatedEnemies.Clear();
		RefreshUiState();

		if (_combatActive)
		{
			if (!EnsureActiveEnemy())
			{
				StopCombat();
				UpdateFeedback("КОМНАТА ОЧИЩЕНА");
			}
			return;
		}

		StartCombat();
	}

	private void InitializeCombatDeferred()
	{
		CollectEnemiesFromRoom();
		ReconnectEnemySignals();
		GD.Print($"CombatController Ready | Rhythm={_rhythmManager != null} Judge={_inputJudge != null} Executor={_patternExecutor != null} Room={_roomNode != null} Enemies={_enemies.Count}");
		if (_rhythmManager != null && _rhythmLineView != null)
		{
			_rhythmLineView.SetTempoRange(TempoMinBpm, TempoMaxBpm);
			_rhythmLineView.SetTempo(_rhythmManager.Bpm);
			_rhythmLineView.BarSlotCount = GetBarSlotCount();
		}
		SyncRhythmBalanceSettings();
		if (_rhythmManager == null || _inputJudge == null || _patternExecutor == null || _roomNode == null)
		{
			GD.PushError("CombatController: не найдены обязательные ссылки на узлы.");
			UpdateFeedback("ОШИБКА ССЫЛОК БОЯ");
			return;
		}

		StartCombat();
	}

	public override void _Process(double delta)
	{
		UpdatePatternTimeline();
		TryCleanupActiveTimeline();
		TryApplyPendingPatternSwitch();
		TryAppendQueuedMemoryPhrase();
		CleanupInvalidEnemyReferences();
		UpdateHoveredEnemyByMouse();
		if (ImmediateHoverRetarget)
		{
			TryImmediateRetargetByHover();
		}

		// Playhead и lane идут по step-сетке от непрерывного времени старта паттерна.
		if (_rhythmManager != null && _rhythmLineView != null)
		{
			_rhythmLineView.UpdateLaneTimingProfile(
				subdivision: _rhythmManager.Subdivision,
				useSwing: _rhythmManager.TimingGroove == RhythmManager.GrooveMode.Swing,
				swingRatio: (float)_rhythmManager.SwingRatio,
				stepIntervalSeconds: (float)_rhythmManager.StepIntervalSeconds);
			_rhythmLineView.BarSlotCount = GetBarSlotCount();
			SyncRhythmBalanceSettings();

			int localPatternTick;
			float localPatternMotion;
			if (_activePatternStartSongTimeSeconds >= 0.0 && _rhythmManager.StepIntervalSeconds > 0.0)
			{
				double elapsed = Math.Max(0.0, _rhythmManager.SongTimeSeconds - _activePatternStartSongTimeSeconds);
				double motion = elapsed / _rhythmManager.StepIntervalSeconds;
				localPatternMotion = (float)elapsed;
				localPatternTick = Mathf.Max(0, (int)Math.Floor(motion));
			}
			else
			{
				int playheadTickIndex = _rhythmManager.CurrentStepIndex;
				localPatternTick = playheadTickIndex;
				if (_activePatternStartStepIndex >= 0)
				{
					localPatternTick = Mathf.Max(0, playheadTickIndex - _activePatternStartStepIndex);
				}
				localPatternMotion = localPatternTick;
			}

			_rhythmLineView.UpdateTimelinePlayhead(localPatternTick);
			_rhythmLineView.UpdateTimelineMotion(localPatternMotion);
			_rhythmLineView.UpdateMemoryPlayhead(_rhythmManager.CurrentStepIndex);
		}

		if (_lastUiHitResultSeconds > 0.0f)
		{
			_lastUiHitResultSeconds -= (float)delta;
			if (_lastUiHitResultSeconds <= 0.0f)
			{
				_lastUiHitResult = null;
				_lastUiTimingOffsetSeconds = null;
				RefreshUiState();
			}
		}
	}

	public void OnPlayerHitRequested(HitType hitType)
	{
		if (!_combatActive)
		{
			return;
		}

		HandlePlayerHit(hitType);
	}

	public void OnSelectNextTargetRequested()
	{
		if (!_combatActive)
		{
			return;
		}

		SelectNextEnemy();
	}

	public void StartCombat()
	{
		if (_rhythmManager == null || _inputJudge == null || _patternExecutor == null)
		{
			UpdateFeedback("ОШИБКА ССЫЛОК БОЯ");
			return;
		}

		if (!EnsureActiveEnemy())
		{
			UpdateFeedback("НЕТ ВРАГОВ ДЛЯ БОЯ");
			return;
		}

		_combatActive = true;
		_rhythmManager.StartRhythm();
		// После старта ритма таймер обнуляется, поэтому фиксируем старт playhead по step заново.
		_activePatternStartStepIndex = _rhythmManager.CurrentStepIndex;
		_activePatternStartSongTimeSeconds = _rhythmManager.SongTimeSeconds;
		UpdateFeedback($"БОЙ ЗАПУЩЕН | ЦЕЛЬ: {FormatEnemyLabel(_patternExecutor.ActiveEnemy)}");
		RefreshUiState();
	}

	public void StopCombat()
	{
		if (_rhythmManager != null)
		{
			_rhythmManager.StopRhythm();
		}

		_combatActive = false;
		UpdateFeedback("БОЙ ОСТАНОВЛЕН");
		RefreshUiState();
	}

	private void HandlePlayerHit(HitType hitType)
	{
		if (_inputJudge == null || _patternExecutor == null)
		{
			return;
		}

		if (!EnsureActiveEnemy())
		{
			UpdateFeedback("ВСЕ ВРАГИ ПОБЕЖДЕНЫ");
			RefreshUiState();
			return;
		}

		HitResult result = _patternExecutor.NextExpectedStepIndex >= 0
			? _inputJudge.JudgeInputForStepDetailed(_patternExecutor.NextExpectedStepIndex, out int judgedStepIndex, out double signedOffsetSeconds)
			: _inputJudge.JudgeCurrentInputDetailed(out judgedStepIndex, out signedOffsetSeconds);

		double timingOffsetAbs = Math.Abs(signedOffsetSeconds);
		PatternStepResult step = _patternExecutor.ProcessHit(hitType, result, judgedStepIndex, timingOffsetAbs);
		if (!step.Success && step.FailReason == PatternFailReason.DuplicateInputIgnored)
		{
			// Повторный ввод в тот же шаг просто игнорируем, чтобы не было ложных промахов.
			return;
		}

		if (!step.Success && step.FailReason == PatternFailReason.InputBeforeExpectedStepIgnored)
		{
			return;
		}

		if (!step.Success && step.FailReason == PatternFailReason.TimingMiss)
		{
			UpdateFeedback($"{FormatTimingInputError(signedOffsetSeconds)} | цель: {FormatEnemyLabel(_patternExecutor.ActiveEnemy)}");
			RefreshUiState();
			return;
		}

		if (!step.Success)
		{
			if (step.FailReason == PatternFailReason.SequenceMiss)
			{
				UpdateFeedback($"НЕ ТА РУКА: ожидался {step.ExpectedHit}, введён {hitType}");
				RefreshUiState();
				return;
			}

			if (step.FailReason == PatternFailReason.PauseInput)
			{
				UpdateFeedback($"ПАУЗА | не нажимай {hitType}");
				RefreshUiState();
				return;
			}

			if (step.FailReason == PatternFailReason.NoActivePattern)
			{
				PlayMissSfx();
				SetLastUiResult(HitResult.Miss, signedOffsetSeconds);
				UpdateFeedback("НЕТ АКТИВНОГО ПАТТЕРНА");
				RefreshUiState();
				return;
			}

			PlayMissSfx();
			SetLastUiResult(HitResult.Miss, signedOffsetSeconds);
			UpdateFeedback($"ОШИБКА ПАТТЕРНА | ЦЕЛЬ: {FormatEnemyLabel(_patternExecutor.ActiveEnemy)}");
			RefreshUiState();
			return;
		}

		Enemy defeatedByPhrase = step.DamageTarget;
		if (defeatedByPhrase != null && !defeatedByPhrase.IsAlive)
		{
			bool queuedMemoryPhrase = EnqueueDefeatedEnemyPattern(defeatedByPhrase);
			_patternExecutor.RemoveTargetFromTimeline(defeatedByPhrase);
			if (queuedMemoryPhrase)
			{
				UpdateFeedback($"ВРАГ ПОБЕЖДЕН: {FormatEnemyLabel(defeatedByPhrase)} | ФРАЗА В ОЧЕРЕДИ");
			}

			if (!EnsureActiveEnemy())
			{
				StopCombat();
				UpdateFeedback("КОМНАТА ОЧИЩЕНА");
				RefreshUiState();
				return;
			}
		}

		string damageText = step.DamageApplied > 0 ? step.DamageApplied.ToString() : "копится";
		string message = $"Ввод: {hitType}, Оценка: {result}, Урон: {damageText}, x{step.MultiplierAfterHit}";
		if (!step.StreakContinued && step.StreakAfterHit == 1)
		{
			message += " | ЦЕПОЧКА СБРОШЕНА (РАЗРЫВ ПАРТИИ)";
		}
		if (step.PatternCompleted)
		{
			message += " | ПАТТЕРН ЗАВЕРШЁН";
		}
		if (HasPendingPatternSwitch())
		{
			PendingPatternSwitch nextSwitch = _pendingPatternSwitches.Peek();
			if (nextSwitch.Enemy != null && IsInstanceValid(nextSwitch.Enemy))
			{
				message += $" | СЛЕД. БАР: {FormatEnemyLabel(nextSwitch.Enemy)}";
			}
		}
		if (_memoryPatternQueue.Count > 0)
		{
			message += $" | QUEUE: {_memoryPatternQueue.Count}";
		}
		PlayHitSfx(result, step.WasAccent);
		TryApplyPerfectHitStop(result);
		SetLastUiResult(result, signedOffsetSeconds);
		GD.Print(message);
		UpdateFeedback(message);
		RefreshUiState();
	}

	private void CollectEnemiesFromRoom()
	{
		_enemies.Clear();
		_activeEnemyIndex = -1;

		if (_roomNode == null)
		{
			return;
		}

		CollectEnemiesRecursive(_roomNode);
	}

	private void CollectEnemiesRecursive(Node node)
	{
		if (node is Enemy enemy)
		{
			if (!IsInstanceValid(enemy))
			{
				return;
			}

			// Инициализируем врага сразу, чтобы не зависеть от порядка _Ready между узлами.
			enemy.EnsureInitializedForCombat();
			_enemies.Add(enemy);
		}

		foreach (Node child in node.GetChildren())
		{
			CollectEnemiesRecursive(child);
		}
	}

	private bool EnsureActiveEnemy()
	{
		if (_patternExecutor == null)
		{
			return false;
		}

		if (_patternExecutor.ActiveEnemy != null && _patternExecutor.ActiveEnemy.IsAlive)
		{
			return true;
		}

		if (_patternExecutor.HasAliveTargetInTimeline)
		{
			return true;
		}

		if (HasPendingPatternSwitch())
		{
			TryApplyPendingPatternSwitch(forceWhenTimelineEmpty: true);
			if (_patternExecutor.HasActivePattern || HasPendingPatternSwitch())
			{
				return true;
			}
		}

		for (int i = 0; i < _enemies.Count; i++)
		{
			_enemies[i].EnsureInitializedForCombat();
			if (_enemies[i].IsAlive)
			{
				SelectEnemyByIndex(i);
				return true;
			}
		}

		// Если живых врагов нет, сбрасываем подсветку.
		foreach (Enemy enemy in _enemies)
		{
			enemy.SetActiveTarget(false);
		}

		return false;
	}

	private void SelectNextEnemy()
	{
		if (_enemies.Count == 0 || _patternExecutor == null)
		{
			return;
		}

		// На каждой попытке пересчитываем базовый индекс от текущей реальной цели.
		int currentIndex = -1;
		if (_patternExecutor.ActiveEnemy != null)
		{
			currentIndex = _enemies.IndexOf(_patternExecutor.ActiveEnemy);
		}

		if (currentIndex < 0)
		{
			currentIndex = _activeEnemyIndex;
		}

		for (int offset = 1; offset <= _enemies.Count; offset++)
		{
			int candidateIndex = (currentIndex + offset + _enemies.Count) % _enemies.Count;
			if (_enemies[candidateIndex].IsAlive)
			{
				SelectEnemyByIndex(candidateIndex);
				if (!HasPendingPatternSwitch())
				{
					UpdateFeedback($"НОВАЯ ЦЕЛЬ: {FormatEnemyLabel(_patternExecutor.ActiveEnemy)}");
				}
				RefreshUiState();
				return;
			}
		}
	}

	private void SelectEnemyByIndex(int enemyIndex)
	{
		if (_patternExecutor == null || enemyIndex < 0 || enemyIndex >= _enemies.Count)
		{
			return;
		}

		Enemy enemy = _enemies[enemyIndex];
		SelectEnemy(enemy, enemyIndex);
	}

	private void SelectEnemy(Enemy enemy, int enemyIndex = -1)
	{
		if (_patternExecutor == null || enemy == null || !enemy.IsAlive)
		{
			return;
		}

		if (_combatActive && _patternExecutor.HasActivePattern)
		{
			QueuePatternSwitch(enemy, enemyIndex);
			return;
		}

		ApplyPatternSwitch(enemy, enemyIndex);
	}

	private void QueuePatternSwitch(Enemy enemy, int enemyIndex)
	{
		int targetBarIndex = GetNextQueuedSwitchBarIndex();
		bool insertBreathBefore = ShouldInsertBreathBeforePendingSwitch();
		_pendingPatternSwitches.Enqueue(new PendingPatternSwitch(enemy, enemyIndex, targetBarIndex, insertBreathBefore));
		_lastQueuedSwitchBarIndex = targetBarIndex;
		UpdatePendingPatternPreview();
		string breathText = insertBreathBefore ? " + BREATH" : string.Empty;
		UpdateFeedback($"БАР {targetBarIndex + 1}: {FormatEnemyLabel(enemy)}{breathText}");
	}

	private void TryApplyPendingPatternSwitch(bool forceWhenTimelineEmpty = false)
	{
		if (!_combatActive || !HasPendingPatternSwitch() || _rhythmManager == null)
		{
			return;
		}

		while (HasPendingPatternSwitch())
		{
			PendingPatternSwitch pendingCandidate = _pendingPatternSwitches.Peek();
			if (pendingCandidate.Enemy != null && IsInstanceValid(pendingCandidate.Enemy) && pendingCandidate.Enemy.IsAlive)
			{
				break;
			}

			_pendingPatternSwitches.Dequeue();
		}

		if (!HasPendingPatternSwitch())
		{
			UpdatePendingPatternPreview();
			return;
		}

		PendingPatternSwitch pending = _pendingPatternSwitches.Peek();
		int currentStep = _rhythmManager.CurrentStepIndex;
		int leadInSteps = GetLaneLeadInSteps();
		int targetStep = pending.TargetBarIndex * GetBarSlotCount();
		int stepsUntilTargetBar = Math.Max(0, targetStep - currentStep);
		bool timelineEmpty = !_patternExecutor.HasActivePattern;
		if (!timelineEmpty && stepsUntilTargetBar > leadInSteps)
		{
			return;
		}

		if (timelineEmpty && !forceWhenTimelineEmpty && stepsUntilTargetBar > leadInSteps)
		{
			return;
		}

		bool appendToTimeline = !timelineEmpty;
		bool insertBreathBefore = appendToTimeline && pending.InsertBreathBefore;
		ApplyPatternSwitch(pending.Enemy, pending.EnemyIndex, stepsUntilTargetBar, appendToTimeline: appendToTimeline, insertBreathBefore: insertBreathBefore);
		_pendingPatternSwitches.Dequeue();
		UpdatePendingPatternPreview();
		UpdateFeedback($"ГОТОВИТСЯ БАР | ЦЕЛЬ: {FormatEnemyLabel(_patternExecutor.ActiveEnemy)}");
		RefreshUiState();
	}

	private void ApplyPatternSwitch(Enemy enemy, int enemyIndex, int? leadInOverrideSteps = null, bool appendToTimeline = false, bool insertBreathBefore = false)
	{
		foreach (Enemy otherEnemy in _enemies)
		{
			otherEnemy.SetActiveTarget(false);
		}
		enemy.SetActiveTarget(true);
		RhythmPattern pattern = new RhythmPattern(enemy.GetPatternHits(), enemy);
		int currentStep = _rhythmManager != null ? _rhythmManager.CurrentStepIndex : 0;
		int leadInSteps = leadInOverrideSteps ?? GetLaneLeadInSteps();
		if (appendToTimeline && _patternExecutor.HasActivePattern)
		{
			if (insertBreathBefore)
			{
				_patternExecutor.AppendRestBar(GetBarSlotCount());
			}
			_patternExecutor.AppendPattern(pattern);
		}
		else
		{
			_patternExecutor.SetActivePattern(pattern, currentStep, leadInSteps);
			_activePatternStartStepIndex = currentStep;
			_activePatternStartSongTimeSeconds = _rhythmManager != null ? _rhythmManager.SongTimeSeconds : 0.0;
		}
		_activeEnemyIndex = enemyIndex >= 0 ? enemyIndex : _enemies.IndexOf(enemy);
	}

	private void ClearPendingPatternSwitch()
	{
		_pendingPatternSwitches.Clear();
		_lastQueuedSwitchBarIndex = -1;
		_rhythmLineView?.ClearUpcomingPhrasePreview();
	}

	private int GetLaneLeadInSteps()
	{
		return _rhythmLineView != null ? Math.Max(0, _rhythmLineView.LaneLeadInSteps) : 2;
	}

	private bool HasPendingPatternSwitch()
	{
		return _pendingPatternSwitches.Count > 0;
	}

	private int GetNextQueuedSwitchBarIndex()
	{
		int currentStep = _rhythmManager != null ? _rhythmManager.CurrentStepIndex : 0;
		int nextBarIndex = GetNextBarIndex(currentStep);
		if (!HasPendingPatternSwitch())
		{
			return nextBarIndex;
		}

		return Math.Max(nextBarIndex, _lastQueuedSwitchBarIndex + 1);
	}

	private void UpdatePendingPatternPreview()
	{
		if (!HasPendingPatternSwitch())
		{
			_rhythmLineView?.ClearUpcomingPhrasePreview();
			return;
		}

		PendingPatternSwitch pending = _pendingPatternSwitches.Peek();
		if (pending.Enemy == null || !IsInstanceValid(pending.Enemy) || !pending.Enemy.IsAlive)
		{
			_rhythmLineView?.ClearUpcomingPhrasePreview();
			return;
		}

		_rhythmLineView?.SetUpcomingPhrasePreview(BuildPendingPreviewPhrase(pending));
	}

	private List<HitType> BuildPendingPreviewPhrase(PendingPatternSwitch pending)
	{
		List<HitType> preview = new List<HitType>();
		if (pending.InsertBreathBefore)
		{
			for (int i = 0; i < GetBarSlotCount(); i++)
			{
				preview.Add(HitType.Rest);
			}
		}

		if (pending.Enemy != null && IsInstanceValid(pending.Enemy))
		{
			preview.AddRange(pending.Enemy.GetPatternHits());
		}

		return preview;
	}

	private bool ShouldInsertBreathBeforePendingSwitch()
	{
		return _patternExecutor != null
			&& _patternExecutor.ActiveTimelineSlotCount >= GetDenseTimelineSlotThreshold()
			&& !HasPendingPatternSwitch();
	}

	private int GetNextBarIndex(int stepIndex)
	{
		if (stepIndex < 0)
		{
			return 1;
		}

		int currentBar = stepIndex / GetBarSlotCount();
		return currentBar + 1;
	}

	private int GetBarSlotCount()
	{
		return Math.Max(1, BarSlotCount);
	}

	private int GetMaxActiveTimelineSlots()
	{
		return Math.Max(GetBarSlotCount(), MaxActiveTimelineSlots);
	}

	private int GetDenseTimelineSlotThreshold()
	{
		return Math.Clamp(DenseTimelineSlotThreshold, GetBarSlotCount(), GetMaxActiveTimelineSlots());
	}

	private void SyncRhythmBalanceSettings()
	{
		if (_patternExecutor == null)
		{
			return;
		}

		_patternExecutor.MaxActiveTimelineSlots = GetMaxActiveTimelineSlots();
	}

	private static string FormatEnemyLabel(Enemy enemy)
	{
		if (enemy == null)
		{
			return "-";
		}

		string name = enemy.Name.ToString();
		if (string.IsNullOrWhiteSpace(enemy.RudimentName))
		{
			return name;
		}

		return $"{name} | {enemy.RudimentName}";
	}

	private void UpdateFeedback(string text)
	{
		GD.Print($"[CombatFeedback] {text}");
		if (_feedbackLabel != null)
		{
			_feedbackLabel.Text = text;
		}

		_rhythmLineView?.SetFeedbackText(text);
	}

	private static string FormatTimingInputError(double signedOffsetSeconds)
	{
		int ms = (int)Math.Round(signedOffsetSeconds * 1000.0);
		if (signedOffsetSeconds < 0.0)
		{
			return $"РАНО {ms}ms";
		}

		return $"ПОЗДНО +{ms}ms";
	}

	private void RefreshUiState()
	{
		if (_patternExecutor == null)
		{
			return;
		}

		_comboMeterView?.SetValues(_patternExecutor.Streak, _patternExecutor.Multiplier);
		if (_player != null)
		{
			_comboMeterView?.SetPlayerHp(_player.CurrentHp, _player.MaxHp);
		}

		if (_rhythmLineView == null)
		{
			return;
		}

		if (_patternExecutor.TryGetCurrentAndNextHits(out HitType currentHit, out HitType nextHit, out bool hasNextHit))
		{
			_rhythmLineView.SetPatternPreview(currentHit, hasNextHit, nextHit);
		}
		else
		{
			_rhythmLineView.ClearPatternPreview();
		}

		if (_patternExecutor.TryGetTimeline(out List<HitType> notes, out int currentIndex))
		{
			_rhythmLineView.SetTimeline(notes, currentIndex, _lastUiHitResult, _lastUiTimingOffsetSeconds);
		}
		else
		{
			_rhythmLineView.ClearTimeline();
		}

		if (_rhythmManager != null)
		{
			_rhythmLineView.UpdateMemoryPlayhead(_rhythmManager.CurrentStepIndex);
		}
	}

	private void UpdatePatternTimeline()
	{
		if (!_combatActive || _patternExecutor == null || _rhythmManager == null)
		{
			return;
		}

		PatternTimelineResult timeline = _patternExecutor.AdvanceTimeline(_rhythmManager.CurrentStepIndex);
		if (!timeline.RestAdvanced && !timeline.MissedRequiredHit)
		{
			return;
		}

		if (timeline.MissedRequiredHit)
		{
			PlayMissSfx();
			SetLastUiResult(HitResult.Miss, null);
			UpdateFeedback($"MISS | ПРОПУЩЕНА НОТА: {timeline.MissedHit}");
			RefreshUiState();
			return;
		}

		if (timeline.PatternCompleted)
		{
			UpdateFeedback("ПАУЗА СЫГРАНА | ПАТТЕРН ЗАВЕРШЁН");
		}

		RefreshUiState();
	}

	private void TryCleanupActiveTimeline()
	{
		if (!_combatActive || _patternExecutor == null || !_patternExecutor.IsAtTimelineLoopStart)
		{
			return;
		}

		if (!_patternExecutor.TryCleanupTimelineAtLoopStart())
		{
			return;
		}

		UpdateFeedback($"TIMELINE CLEANUP | SLOTS: {_patternExecutor.ActiveTimelineSlotCount}");
		if (_patternExecutor.ActiveTimelineSlotCount == 0 && HasPendingPatternSwitch())
		{
			TryApplyPendingPatternSwitch(forceWhenTimelineEmpty: true);
			RefreshUiState();
			return;
		}

		if (_patternExecutor.ActiveTimelineSlotCount == 0 && TryStartBreathBar())
		{
			QueueNextAliveEnemyIfNeeded();
		}
		RefreshUiState();
	}

	private bool TryStartBreathBar()
	{
		if (_patternExecutor == null || _rhythmManager == null || HasPendingPatternSwitch())
		{
			return false;
		}

		if (!HasAnyAliveEnemy())
		{
			return false;
		}

		_patternExecutor.SetRestBar(_rhythmManager.CurrentStepIndex, leadInSteps: 0, slotCount: GetBarSlotCount());
		_activePatternStartStepIndex = _rhythmManager.CurrentStepIndex;
		_activePatternStartSongTimeSeconds = _rhythmManager.SongTimeSeconds;
		UpdateFeedback("BREATH BAR");
		return true;
	}

	private void QueueNextAliveEnemyIfNeeded()
	{
		if (_patternExecutor == null || HasPendingPatternSwitch())
		{
			return;
		}

		for (int i = 0; i < _enemies.Count; i++)
		{
			Enemy enemy = _enemies[i];
			if (enemy != null && IsInstanceValid(enemy) && enemy.IsAlive)
			{
				QueuePatternSwitch(enemy, i);
				return;
			}
		}
	}

	private bool HasAnyAliveEnemy()
	{
		foreach (Enemy enemy in _enemies)
		{
			if (enemy != null && IsInstanceValid(enemy) && enemy.IsAlive)
			{
				return true;
			}
		}

		return false;
	}

	private void ReconnectEnemySignals()
	{
		foreach (Enemy enemy in _subscribedEnemies)
		{
			enemy.Counterattacked -= OnEnemyCounterattacked;
		}
		_subscribedEnemies.Clear();

		foreach (Enemy enemy in _enemies)
		{
			enemy.Counterattacked += OnEnemyCounterattacked;
			_subscribedEnemies.Add(enemy);
		}
	}

	private void UpdateHoveredEnemyByMouse()
	{
		if (_enemies.Count == 0)
		{
			SetHoveredEnemy(null);
			return;
		}

		Vector2 worldMouse = GetMouseWorldPosition();
		Enemy hovered = null;
		float nearestDistance = float.MaxValue;

		foreach (Enemy enemy in _enemies)
		{
			if (!enemy.IsPointHovering(worldMouse))
			{
				continue;
			}

			float dist = enemy.GlobalPosition.DistanceTo(worldMouse);
			if (dist < nearestDistance)
			{
				nearestDistance = dist;
				hovered = enemy;
			}
		}

		SetHoveredEnemy(hovered);
	}

	private void SetHoveredEnemy(Enemy enemy)
	{
		if (_hoveredEnemy == enemy)
		{
			return;
		}

		if (_hoveredEnemy != null)
		{
			_hoveredEnemy.SetHoveredByCursor(false);
		}

		_hoveredEnemy = enemy;
		if (_hoveredEnemy != null)
		{
			_hoveredEnemy.SetHoveredByCursor(true);
		}
	}

	private Vector2 GetMouseWorldPosition()
	{
		if (_player != null)
		{
			return _player.GetGlobalMousePosition();
		}

		Vector2 mouseViewport = GetViewport().GetMousePosition();
		Transform2D invCanvas = GetViewport().GetCanvasTransform().AffineInverse();
		return invCanvas * mouseViewport;
	}

	private void OnEnemyCounterattacked(int damage)
	{
		if (!_combatActive || _player == null)
		{
			return;
		}

		_player.TakeDamage(damage);
		UpdateFeedback($"ИГРОК ПОЛУЧИЛ УРОН: -{damage} HP ({_player.CurrentHp}/{_player.MaxHp})");
		RefreshUiState();

		if (_player.CurrentHp <= 0)
		{
			StopCombat();
			UpdateFeedback("ИГРОК ПОБЕЖДЕН");
			RefreshUiState();
		}
	}

	private void TrySnapTargetToHoveredEnemy()
	{
		if (_hoveredEnemy == null || !_hoveredEnemy.IsAlive || _patternExecutor == null)
		{
			return;
		}

		if (_patternExecutor.ActiveEnemy == _hoveredEnemy)
		{
			return;
		}

		SelectEnemy(_hoveredEnemy);
		_activeEnemyIndex = _enemies.IndexOf(_hoveredEnemy);
		RefreshUiState();
	}

	private void TryAutoRetargetByHover(float delta)
	{
		if (!_combatActive || _patternExecutor == null)
		{
			_lastHoverCandidate = null;
			_hoverCandidateTime = 0.0f;
			return;
		}

		if (_hoveredEnemy == null || !_hoveredEnemy.IsAlive)
		{
			_lastHoverCandidate = null;
			_hoverCandidateTime = 0.0f;
			return;
		}

		if (_patternExecutor.ActiveEnemy == _hoveredEnemy)
		{
			_lastHoverCandidate = _hoveredEnemy;
			_hoverCandidateTime = 0.0f;
			return;
		}

		if (_lastHoverCandidate != _hoveredEnemy)
		{
			_lastHoverCandidate = _hoveredEnemy;
			_hoverCandidateTime = 0.0f;
			return;
		}

		_hoverCandidateTime += delta;
		if (_hoverCandidateTime < HoverRetargetDelaySeconds)
		{
			return;
		}

		// Надёжный автоперевод цели на врага, над которым стабильно держим курсор.
		SelectEnemy(_hoveredEnemy);
		_activeEnemyIndex = _enemies.IndexOf(_hoveredEnemy);
		_hoverCandidateTime = 0.0f;
		RefreshUiState();
	}

	private void TryImmediateRetargetByHover()
	{
		if (!_combatActive || _patternExecutor == null || _hoveredEnemy == null || !_hoveredEnemy.IsAlive)
		{
			return;
		}

		if (_patternExecutor.ActiveEnemy == _hoveredEnemy)
		{
			return;
		}

		// Мгновенное переключение активной цели по наведению.
		SelectEnemy(_hoveredEnemy);
		_activeEnemyIndex = _enemies.IndexOf(_hoveredEnemy);
		RefreshUiState();
	}

	private void CleanupInvalidEnemyReferences()
	{
		for (int i = _enemies.Count - 1; i >= 0; i--)
		{
			if (!IsInstanceValid(_enemies[i]))
			{
				_enemies.RemoveAt(i);
			}
		}

		for (int i = _subscribedEnemies.Count - 1; i >= 0; i--)
		{
			if (!IsInstanceValid(_subscribedEnemies[i]))
			{
				_subscribedEnemies.RemoveAt(i);
			}
		}

		if (_hoveredEnemy != null && !IsInstanceValid(_hoveredEnemy))
		{
			_hoveredEnemy = null;
		}
	}

	private void PlayHitSfx(HitResult result, bool wasAccent)
	{
		if (_hitSfxPlayer == null)
		{
			return;
		}

		float pitch = result == HitResult.Perfect ? PerfectHitPitch : GoodHitPitch;
		if (wasAccent)
		{
			pitch += 0.12f;
		}

		_hitSfxPlayer.PitchScale = pitch;
		_hitSfxPlayer.VolumeDb = result == HitResult.Perfect ? PerfectHitVolumeDb : GoodHitVolumeDb;
		_hitSfxPlayer.Play();
	}

	private async void PlayMissSfx()
	{
		if (_missSfxPlayer == null)
		{
			return;
		}

		_missSfxPlayVersion++;
		int playVersion = _missSfxPlayVersion;
		_missSfxPlayer.PitchScale = MissPitch;
		_missSfxPlayer.VolumeDb = MissVolumeDb;
		_missSfxPlayer.Play();

		if (MissSfxMaxDurationSeconds <= 0.0)
		{
			return;
		}

		SceneTree tree = GetTree();
		if (tree == null)
		{
			return;
		}

		await ToSignal(tree.CreateTimer(MissSfxMaxDurationSeconds, processAlways: true), SceneTreeTimer.SignalName.Timeout);
		if (playVersion == _missSfxPlayVersion && IsInstanceValid(_missSfxPlayer) && _missSfxPlayer.Playing)
		{
			_missSfxPlayer.Stop();
		}
	}

	private async void TryApplyPerfectHitStop(HitResult result)
	{
		if (result != HitResult.Perfect || PerfectHitStopSeconds <= 0.0 || _hitStopActive)
		{
			return;
		}

		SceneTree tree = GetTree();
		if (tree == null)
		{
			return;
		}

		_hitStopActive = true;
		bool wasPaused = tree.Paused;
		try
		{
			tree.Paused = true;
			await ToSignal(tree.CreateTimer(PerfectHitStopSeconds, processAlways: true), SceneTreeTimer.SignalName.Timeout);
			if (IsInstanceValid(tree))
			{
				tree.Paused = wasPaused;
			}
		}
		finally
		{
			_hitStopActive = false;
		}
	}

	private void OnTempoChangedRequested(double bpm)
	{
		if (_rhythmManager == null)
		{
			return;
		}

		double clamped = Math.Clamp(bpm, TempoMinBpm, TempoMaxBpm);
		_rhythmManager.Bpm = clamped;
		_rhythmLineView?.SetTempo(clamped);
	}

	private void SetLastUiResult(HitResult result)
	{
		SetLastUiResult(result, null);
	}

	private void SetLastUiResult(HitResult result, double? signedOffsetSeconds)
	{
		_lastUiHitResult = result;
		_lastUiTimingOffsetSeconds = signedOffsetSeconds;
		_lastUiHitResultSeconds = 0.20f;
	}

	private bool EnqueueDefeatedEnemyPattern(Enemy enemy)
	{
		if (enemy == null || !IsInstanceValid(enemy) || _queuedDefeatedEnemies.Contains(enemy))
		{
			return false;
		}

		List<HitType> notes = enemy.GetPatternHits();
		if (notes.Count == 0)
		{
			return false;
		}

		_memoryPatternQueue.Enqueue(notes);
		_queuedDefeatedEnemies.Add(enemy);
		return true;
	}

	private void TryAppendQueuedMemoryPhrase()
	{
		if (!_combatActive || _rhythmLineView == null || _rhythmManager == null || _memoryPatternQueue.Count == 0)
		{
			return;
		}

		int currentStep = _rhythmManager.CurrentStepIndex;
		if (currentStep < 0 || currentStep % GetBarSlotCount() != 0)
		{
			return;
		}

		int currentBarIndex = currentStep / GetBarSlotCount();
		if (currentBarIndex == _lastMemoryAppendBarIndex)
		{
			return;
		}

		_lastMemoryAppendBarIndex = currentBarIndex;
		List<HitType> phrase = _memoryPatternQueue.Dequeue();
		_rhythmLineView.AppendMemoryPhrase(phrase);
		UpdateFeedback($"ФРАЗА ВСТАВЛЕНА В TIMELINE | QUEUE: {_memoryPatternQueue.Count}");
	}
}
