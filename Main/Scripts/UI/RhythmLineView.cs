using Godot;
using System;
using System.Collections.Generic;

public partial class RhythmLineView : Control
{
	[Signal]
	public delegate void TempoChangedRequestedEventHandler(double bpm);

	// Включает режим "статичная линия + движущиеся ноты" как в rhythm-играх.
	[Export]
	public bool UseMovingLaneView { get; set; } = true;

	// Панель, внутри которой рисуем ритм-ленту.
	[Export]
	public NodePath PanelPath { get; set; } = new NodePath("Panel");

	// Положение hit-линии в lane (0..1 от ширины панели), как в Taiko.
	[Export(PropertyHint.Range, "0.05,0.45,0.01")]
	public float HitLineNormalizedX { get; set; } = 0.22f;

	// Расстояние между соседними нотами в lane.
	[Export(PropertyHint.Range, "20,96,1")]
	public float LaneNoteSpacing { get; set; } = 48.0f;

	// Небольшой "разбег" перед первой нотой, чтобы она приходила справа.
	[Export(PropertyHint.Range, "0,6,1")]
	public int LaneLeadInSteps { get; set; } = 2;

	// Размер бара в slot-единицах. MVP использует 8 eighth-note слотов.
	[Export(PropertyHint.Range, "4,16,1")]
	public int BarSlotCount { get; set; } = 8;

	[Export]
	public NodePath CurrentHitLabelPath { get; set; } = new NodePath("Panel/VBoxContainer/CurrentHitLabel");

	[Export]
	public NodePath ExpectedHitLabelPath { get; set; } = new NodePath("Panel/VBoxContainer/ExpectedHitLabel");

	[Export]
	public NodePath NextHitLabelPath { get; set; } = new NodePath("Panel/VBoxContainer/NextHitLabel");

	[Export]
	public NodePath TimelineLabelPath { get; set; } = new NodePath("Panel/VBoxContainer/TimelineLabel");

	[Export]
	public NodePath PlayheadLabelPath { get; set; } = new NodePath("Panel/VBoxContainer/PlayheadLabel");

	[Export]
	public NodePath MemoryTimelineLabelPath { get; set; } = new NodePath("Panel/VBoxContainer/MemoryTimelineLabel");

	[Export]
	public NodePath MemoryPlayheadLabelPath { get; set; } = new NodePath("Panel/VBoxContainer/MemoryPlayheadLabel");

	[Export]
	public NodePath TempoValueLabelPath { get; set; } = new NodePath("Panel/VBoxContainer/TempoRow/TempoValueLabel");

	[Export]
	public NodePath TempoSliderPath { get; set; } = new NodePath("Panel/VBoxContainer/TempoRow/TempoSlider");

	private Label _currentHitLabel;
	private Label _expectedHitLabel;
	private Label _nextHitLabel;
	private Label _timelineLabel;
	private Label _playheadLabel;
	private Label _memoryTimelineLabel;
	private Label _memoryPlayheadLabel;
	private Label _tempoValueLabel;
	private HSlider _tempoSlider;
	private Panel _panel;
	private readonly List<HitType> _timelineNotes = new List<HitType>();
	private readonly List<HitType> _upcomingPhrase = new List<HitType>();
	private readonly List<HitType> _memoryTimeline = new List<HitType>();
	private int _timelineCurrentIndex;
	private int _timelinePlayheadIndex;
	private int _memoryPlayheadIndex;
	private float _timelineMotionBeat;
	private float _hitPulseSeconds;
	private int _laneSubdivision = 2;
	private bool _laneUseSwing;
	private float _laneSwingRatio = 0.62f;
	private float _laneStepIntervalSeconds = 0.25f;
	private bool _hasTimeline;
	private HitResult? _lastResult;
	private double? _lastTimingOffsetSeconds;
	private bool _suppressTempoSliderCallback;

	public override void _Ready()
	{
		_currentHitLabel = GetNodeOrNull<Label>(CurrentHitLabelPath);
		_expectedHitLabel = GetNodeOrNull<Label>(ExpectedHitLabelPath);
		_nextHitLabel = GetNodeOrNull<Label>(NextHitLabelPath);
		_timelineLabel = GetNodeOrNull<Label>(TimelineLabelPath);
		_playheadLabel = GetNodeOrNull<Label>(PlayheadLabelPath);
		_memoryTimelineLabel = GetNodeOrNull<Label>(MemoryTimelineLabelPath);
		_memoryPlayheadLabel = GetNodeOrNull<Label>(MemoryPlayheadLabelPath);
		_tempoValueLabel = GetNodeOrNull<Label>(TempoValueLabelPath);
		_tempoSlider = GetNodeOrNull<HSlider>(TempoSliderPath);
		_panel = GetNodeOrNull<Panel>(PanelPath);
		SetProcess(true);
		if (_tempoSlider != null)
		{
			_tempoSlider.ValueChanged += OnTempoSliderValueChanged;
		}
		UpdateMemoryTimelineLabel();
	}

	public void SetTempo(double bpm)
	{
		UpdateTempoLabel(bpm);
		if (_tempoSlider == null)
		{
			return;
		}

		double clamped = Mathf.Clamp((float)bpm, (float)_tempoSlider.MinValue, (float)_tempoSlider.MaxValue);
		_suppressTempoSliderCallback = true;
		_tempoSlider.Value = clamped;
		_suppressTempoSliderCallback = false;
	}

	public void SetTempoRange(double minBpm, double maxBpm)
	{
		if (_tempoSlider == null)
		{
			return;
		}

		_tempoSlider.MinValue = minBpm;
		_tempoSlider.MaxValue = maxBpm;
	}

	public void SetFeedbackText(string feedback)
	{
		if (_currentHitLabel != null)
		{
			_currentHitLabel.Text = feedback;
		}
	}

	public void SetPatternPreview(HitType currentHit, bool hasNextHit, HitType nextHit)
	{
		if (_expectedHitLabel != null)
		{
			_expectedHitLabel.Text = $"Текущий input: {FormatHitType(currentHit)}";
		}

		if (_nextHitLabel != null)
		{
			_nextHitLabel.Text = hasNextHit
				? $"Следующий input: {FormatHitType(nextHit)}"
				: "Следующий input: -";
		}
	}

	public void ClearPatternPreview()
	{
		if (_expectedHitLabel != null)
		{
			_expectedHitLabel.Text = "Текущий input: -";
		}

		if (_nextHitLabel != null)
		{
			_nextHitLabel.Text = "Следующий input: -";
		}
	}

	public void SetTimeline(IReadOnlyList<HitType> notes, int currentIndex, HitResult? lastResult)
	{
		SetTimeline(notes, currentIndex, lastResult, null);
	}

	public void SetTimeline(IReadOnlyList<HitType> notes, int currentIndex, HitResult? lastResult, double? timingOffsetSeconds)
	{
		_timelineNotes.Clear();
		if (notes == null || notes.Count == 0)
		{
			_hasTimeline = false;
			_lastResult = null;
			UpdateTimelineLabels();
			return;
		}

		_timelineNotes.AddRange(notes);
		_timelineCurrentIndex = Mathf.Clamp(currentIndex, 0, notes.Count - 1);
		_timelinePlayheadIndex = Mathf.Clamp(_timelinePlayheadIndex, 0, notes.Count - 1);
		_lastResult = lastResult;
		_lastTimingOffsetSeconds = timingOffsetSeconds;
		if (lastResult != null)
		{
			_hitPulseSeconds = 0.12f;
		}
		_hasTimeline = true;
		UpdateTimelineLabels();
		QueueRedraw();
	}

	public void ClearTimeline()
	{
		_timelineNotes.Clear();
		_upcomingPhrase.Clear();
		_timelineCurrentIndex = 0;
		_timelinePlayheadIndex = 0;
		_timelineMotionBeat = 0.0f;
		_lastResult = null;
		_lastTimingOffsetSeconds = null;
		_hasTimeline = false;
		UpdateTimelineLabels();
		QueueRedraw();
	}

	public void SetUpcomingPhrasePreview(IReadOnlyList<HitType> phrase)
	{
		_upcomingPhrase.Clear();
		if (phrase != null)
		{
			_upcomingPhrase.AddRange(phrase);
		}

		QueueRedraw();
	}

	public void ClearUpcomingPhrasePreview()
	{
		if (_upcomingPhrase.Count == 0)
		{
			return;
		}

		_upcomingPhrase.Clear();
		QueueRedraw();
	}

	public void UpdateTimelinePlayhead(int stepIndex)
	{
		if (!_hasTimeline || _timelineNotes.Count == 0)
		{
			return;
		}

		_timelinePlayheadIndex = Mathf.PosMod(stepIndex, _timelineNotes.Count);
		UpdateTimelineLabels();
		QueueRedraw();
	}

	public void UpdateTimelineMotion(float elapsedSeconds)
	{
		if (!_hasTimeline || _timelineNotes.Count == 0)
		{
			return;
		}

		_timelineMotionBeat = elapsedSeconds;
		if (UseMovingLaneView)
		{
			_timelinePlayheadIndex = GetHitLineNoteIndex(_timelineMotionBeat);
			UpdateTimelineLabels();
		}
		QueueRedraw();
	}

	public void UpdateLaneTimingProfile(int subdivision, bool useSwing, float swingRatio, float stepIntervalSeconds)
	{
		_laneSubdivision = Math.Max(1, subdivision);
		_laneUseSwing = useSwing;
		_laneSwingRatio = Mathf.Clamp(swingRatio, 0.5f, 0.75f);
		_laneStepIntervalSeconds = Math.Max(0.0001f, stepIntervalSeconds);
	}

	public void AppendMemoryPhrase(IReadOnlyList<HitType> phrase)
	{
		if (phrase == null || phrase.Count == 0)
		{
			return;
		}

		_memoryTimeline.AddRange(phrase);

		// Ограничиваем длину окна памяти, чтобы UI оставался читаемым.
		const int maxSlots = 64;
		if (_memoryTimeline.Count > maxSlots)
		{
			int removeCount = _memoryTimeline.Count - maxSlots;
			_memoryTimeline.RemoveRange(0, removeCount);
		}

		UpdateMemoryTimelineLabel();
	}

	public void UpdateMemoryPlayhead(int stepIndex)
	{
		if (_memoryPlayheadLabel == null)
		{
			return;
		}

		if (_memoryTimeline.Count == 0)
		{
			_memoryPlayheadLabel.Text = "Позиция памяти: -/-";
			return;
		}

		_memoryPlayheadIndex = Mathf.PosMod(stepIndex, _memoryTimeline.Count);
		_memoryPlayheadLabel.Text = $"Позиция памяти: {_memoryPlayheadIndex + 1}/{_memoryTimeline.Count}";
		UpdateMemoryTimelineLabel();
		QueueRedraw();
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

	private void UpdateTimelineLabels()
	{
		if (_timelineLabel == null || _playheadLabel == null)
		{
			return;
		}

		if (!_hasTimeline || _timelineNotes.Count == 0)
		{
			_timelineLabel.Text = "| - |";
			_playheadLabel.Text = "Позиция: -/-";
			return;
		}

		if (UseMovingLaneView)
		{
			_timelineLabel.Text = "TAIKO lane: ноты идут справа налево";
			_playheadLabel.Text = $"Позиция: {_timelinePlayheadIndex + 1}/{_timelineNotes.Count}";

			// В lane-режиме текущий слот не пропускает Rest: пауза тоже является правилом ввода.
			int currentSlotIndex = Mathf.Clamp(_timelinePlayheadIndex, 0, _timelineNotes.Count - 1);
			int nextPlayableIndex = GetNextPlayableIndex(currentSlotIndex + 1);
			if (_expectedHitLabel != null)
			{
				_expectedHitLabel.Text = $"Текущий input: {FormatHitType(_timelineNotes[currentSlotIndex])}";
			}
			if (_nextHitLabel != null)
			{
				_nextHitLabel.Text = $"Следующий input: {FormatHitType(_timelineNotes[nextPlayableIndex])}";
			}
			return;
		}

		System.Text.StringBuilder timeline = new System.Text.StringBuilder();
		timeline.Append("| ");
		for (int i = 0; i < _timelineNotes.Count; i++)
		{
			string symbol = FormatTimelineSymbol(_timelineNotes[i], i == _timelineCurrentIndex, _lastResult);
			if (i == _timelinePlayheadIndex)
			{
				// Выделяем текущий слот прямо в строке таймлайна, чтобы не зависеть от ширины шрифта.
				timeline.Append('[');
				timeline.Append(symbol);
				timeline.Append(']');
			}
			else
			{
				timeline.Append(symbol);
			}

			if (i < _timelineNotes.Count - 1)
			{
				timeline.Append(' ');
			}
		}
		timeline.Append(" |");
		_timelineLabel.Text = timeline.ToString();

		_playheadLabel.Text = $"Позиция: {_timelinePlayheadIndex + 1}/{_timelineNotes.Count}";
	}

	public override void _Process(double delta)
	{
		if (_hitPulseSeconds > 0.0f)
		{
			_hitPulseSeconds -= (float)delta;
		}

		if (UseMovingLaneView && _hasTimeline && _timelineNotes.Count > 0)
		{
			QueueRedraw();
		}
	}

	public override void _Draw()
	{
		if (!UseMovingLaneView || !_hasTimeline || _timelineNotes.Count == 0 || _panel == null)
		{
			return;
		}

		Rect2 panelRect = new Rect2(_panel.Position, _panel.Size);
		float hitX = panelRect.Position.X + panelRect.Size.X * Mathf.Clamp(HitLineNormalizedX, 0.05f, 0.45f);
		float laneY = panelRect.Position.Y + panelRect.Size.Y - 22.0f;
		float laneLeft = panelRect.Position.X + 24.0f;
		float laneRight = panelRect.Position.X + panelRect.Size.X - 24.0f;
		float pixelsPerSecond = LaneNoteSpacing / _laneStepIntervalSeconds;
		Font font = ThemeDB.FallbackFont;

		// Базовая линия движения и статичная "зона попадания" (Taiko-style).
		DrawLine(new Vector2(laneLeft, laneY), new Vector2(laneRight, laneY), new Color(0.55f, 0.55f, 0.6f), 1.0f);
		DrawCircle(new Vector2(hitX, laneY), 13.0f, new Color(0.15f, 0.15f, 0.18f));
		DrawArc(new Vector2(hitX, laneY), 13.0f, 0.0f, Mathf.Tau, 40, new Color(1.0f, 1.0f, 1.0f), 2.0f);
		DrawArc(new Vector2(hitX, laneY), 9.0f, 0.0f, Mathf.Tau, 40, new Color(0.85f, 0.85f, 0.9f), 1.0f);

		if (_hitPulseSeconds > 0.0f && _lastResult != null)
		{
			Color pulseColor = _lastResult == HitResult.Perfect
				? new Color(0.3f, 1.0f, 0.6f, 0.8f)
				: (_lastResult == HitResult.Good
					? new Color(1.0f, 0.9f, 0.3f, 0.75f)
					: new Color(1.0f, 0.35f, 0.35f, 0.8f));
			float pulseRadius = 15.0f + (0.12f - _hitPulseSeconds) * 82.0f;
			float pulseWidth = _lastResult == HitResult.Perfect ? 3.0f : 2.0f;
			DrawArc(new Vector2(hitX, laneY), pulseRadius, 0.0f, Mathf.Tau, 56, pulseColor, pulseWidth);

			if (font != null)
			{
				string resultText = FormatHitResultLabel(_lastResult.Value, _lastTimingOffsetSeconds);
				Vector2 size = font.GetStringSize(resultText, HorizontalAlignment.Left, -1, 14);
				Color textColor = pulseColor.Lightened(0.2f);
				DrawString(font, new Vector2(hitX - size.X * 0.5f, laneY - 30.0f), resultText, HorizontalAlignment.Left, -1, 14, textColor);
			}
		}

		// Время визуального цикла учитывает lead-in, иначе последние ноты не успевают дойти до hit-line.
		int cycleLength = Math.Max(1, _timelineNotes.Count + Math.Max(0, LaneLeadInSteps));
		float cycleDurationSeconds = Math.Max(0.0001f, GetStepTimeSeconds(cycleLength));
		float motionSeconds = Mathf.PosMod(Math.Max(0.0f, _timelineMotionBeat), cycleDurationSeconds);
		float leadInSeconds = GetStepTimeSeconds(LaneLeadInSteps);
		float startX = hitX + (leadInSeconds - motionSeconds) * pixelsPerSecond;

		// Тонкие направляющие шагов для более чёткого чтения расположения нот.
		int previewSlotCount = _upcomingPhrase.Count;
		int visualSlotCount = _timelineNotes.Count + previewSlotCount;
		int barSlots = Math.Max(1, BarSlotCount);
		for (int i = 0; i < visualSlotCount; i++)
		{
			float gx = startX + GetStepTimeSeconds(i) * pixelsPerSecond;
			if (gx < laneLeft || gx > laneRight)
			{
				continue;
			}

			Color guideColor = i < _timelineNotes.Count
				? new Color(0.35f, 0.35f, 0.4f, 0.45f)
				: new Color(0.6f, 0.6f, 0.72f, 0.28f);
			DrawLine(new Vector2(gx, laneY - 6.0f), new Vector2(gx, laneY + 6.0f), guideColor, 1.0f);

			if (i % barSlots == 0)
			{
				bool previewBoundary = i >= _timelineNotes.Count;
				Color barColor = previewBoundary
					? new Color(0.75f, 0.78f, 1.0f, 0.45f)
					: new Color(1.0f, 1.0f, 1.0f, 0.68f);
				DrawLine(new Vector2(gx, laneY - 20.0f), new Vector2(gx, laneY + 20.0f), barColor, 2.0f);
			}
		}

		if (_upcomingPhrase.Count > 0)
		{
			float previewStartX = startX + GetStepTimeSeconds(_timelineNotes.Count) * pixelsPerSecond;
			if (previewStartX >= laneLeft && previewStartX <= laneRight)
			{
				DrawLine(
					new Vector2(previewStartX, laneY - 24.0f),
					new Vector2(previewStartX, laneY + 24.0f),
					new Color(0.45f, 0.9f, 1.0f, 0.65f),
					2.0f);
			}
		}

		// Рисуем текущий паттерн.
		for (int i = 0; i < _timelineNotes.Count; i++)
		{
			float x = startX + GetStepTimeSeconds(i) * pixelsPerSecond;
			if (x < laneLeft - 24.0f || x > laneRight + 24.0f)
			{
				continue;
			}

			HitType note = _timelineNotes[i];
			if (note == HitType.Rest)
			{
				DrawCircle(new Vector2(x, laneY), 3.5f, new Color(0.7f, 0.7f, 0.76f, 0.65f));
				DrawLine(new Vector2(x - 5.0f, laneY), new Vector2(x + 5.0f, laneY), new Color(0.7f, 0.7f, 0.76f, 0.45f), 1.0f);
				continue;
			}

			Color color = GetLaneColor(note);
			bool accent = IsAccent(note);
			float radius = accent ? 11.0f : 9.0f;
			float distanceToHitPx = Mathf.Abs(x - hitX);
			if (distanceToHitPx < LaneNoteSpacing * 0.18f)
			{
				// Лёгкая подсветка ноты, которая проходит через hit-line.
				color = color.Lightened(0.2f);
				radius += 1.5f;
			}

			DrawCircle(new Vector2(x, laneY), radius, color);
			if (font != null)
			{
				string symbol = FormatHitType(note);
				Vector2 size = font.GetStringSize(symbol, HorizontalAlignment.Left, -1, 14);
				DrawString(font, new Vector2(x - size.X * 0.5f, laneY + 5.0f), symbol, HorizontalAlignment.Left, -1, 14, Colors.Black);
			}
		}

		DrawUpcomingPhrasePreview(startX, pixelsPerSecond, laneY, laneLeft, laneRight, font);
	}

	private void DrawUpcomingPhrasePreview(float startX, float pixelsPerSecond, float laneY, float laneLeft, float laneRight, Font font)
	{
		if (_upcomingPhrase.Count == 0 || _timelineNotes.Count == 0)
		{
			return;
		}

		int previewStartIndex = _timelineNotes.Count;
		for (int i = 0; i < _upcomingPhrase.Count; i++)
		{
			float x = startX + GetStepTimeSeconds(previewStartIndex + i) * pixelsPerSecond;
			if (x < laneLeft - 24.0f || x > laneRight + 24.0f)
			{
				continue;
			}

			HitType note = _upcomingPhrase[i];
			if (note == HitType.Rest)
			{
				DrawCircle(new Vector2(x, laneY), 3.0f, new Color(0.8f, 0.8f, 0.88f, 0.35f));
				continue;
			}

			Color color = GetLaneColor(note);
			color.A = 0.42f;
			DrawCircle(new Vector2(x, laneY), IsAccent(note) ? 9.5f : 8.0f, color);
			if (font != null)
			{
				string symbol = FormatHitType(note);
				Vector2 size = font.GetStringSize(symbol, HorizontalAlignment.Left, -1, 14);
				DrawString(font, new Vector2(x - size.X * 0.5f, laneY + 5.0f), symbol, HorizontalAlignment.Left, -1, 14, new Color(0.05f, 0.05f, 0.06f, 0.65f));
			}
		}
	}

	private float GetStepTimeSeconds(float stepPosition)
	{
		// Swing-раскладка: неравные расстояния между соседними шагами (long-short) в step-единицах.
		if (_laneUseSwing && _laneSubdivision == 2)
		{
			float clampedStep = Math.Max(0.0f, stepPosition);
			int floorStep = Mathf.FloorToInt(clampedStep);
			float frac = clampedStep - floorStep;
			float a = GetStepTimeSecondsInt(floorStep);
			float b = GetStepTimeSecondsInt(floorStep + 1);
			return Mathf.Lerp(a, b, frac);
		}

		return Math.Max(0.0f, stepPosition) * _laneStepIntervalSeconds;
	}

	private int GetHitLineNoteIndex(float motionStep)
	{
		if (_timelineNotes.Count == 0)
		{
			return 0;
		}

		int cycleLength = Math.Max(1, _timelineNotes.Count + Math.Max(0, LaneLeadInSteps));
		float cycleDurationSeconds = Math.Max(0.0001f, GetStepTimeSeconds(cycleLength));
		float motionSeconds = Mathf.PosMod(Math.Max(0.0f, motionStep), cycleDurationSeconds);
		float leadInSeconds = GetStepTimeSeconds(LaneLeadInSteps);

		int nearestIndex = 0;
		float nearestDistance = float.MaxValue;
		for (int i = 0; i < _timelineNotes.Count; i++)
		{
			float noteSeconds = GetStepTimeSeconds(i);
			float dist = Mathf.Abs(noteSeconds - (motionSeconds - leadInSeconds));
			if (dist < nearestDistance)
			{
				nearestDistance = dist;
				nearestIndex = i;
			}
		}

		return nearestIndex;
	}

	private int GetNextPlayableIndex(int startIndex)
	{
		if (_timelineNotes.Count == 0)
		{
			return 0;
		}

		int idx = Mathf.PosMod(startIndex, _timelineNotes.Count);
		for (int i = 0; i < _timelineNotes.Count; i++)
		{
			int probe = (idx + i) % _timelineNotes.Count;
			if (_timelineNotes[probe] != HitType.Rest)
			{
				return probe;
			}
		}

		return idx;
	}

	private float GetStepTimeSecondsInt(int stepIndex)
	{
		if (_laneUseSwing && _laneSubdivision == 2)
		{
			int beatIndex = stepIndex / 2;
			bool offbeat = (stepIndex % 2) == 1;
			float beatInterval = _laneStepIntervalSeconds * Math.Max(1, _laneSubdivision);
			float beatStart = beatIndex * beatInterval;
			return offbeat ? beatStart + beatInterval * _laneSwingRatio : beatStart;
		}

		return Math.Max(0, stepIndex) * _laneStepIntervalSeconds;
	}

	private static string FormatTimelineSymbol(HitType hitType, bool isCurrent, HitResult? lastResult)
	{
		string symbol = FormatHitType(hitType);
		if (!isCurrent || lastResult == null)
		{
			return symbol;
		}

		if (lastResult == HitResult.Miss)
		{
			return "x";
		}

		if (lastResult == HitResult.Perfect)
		{
			return symbol.ToUpperInvariant();
		}

		return symbol;
	}

	private static Color GetLaneColor(HitType hitType)
	{
		if (hitType == HitType.Left)
		{
			return new Color(0.25f, 0.8f, 1.0f);
		}

		if (hitType == HitType.Right)
		{
			return new Color(0.95f, 0.6f, 0.25f);
		}

		if (hitType == HitType.AccentLeft)
		{
			return new Color(0.55f, 1.0f, 1.0f);
		}

		if (hitType == HitType.AccentRight)
		{
			return new Color(1.0f, 0.82f, 0.35f);
		}

		return new Color(0.45f, 0.45f, 0.5f);
	}

	private static bool IsAccent(HitType hitType)
	{
		return hitType == HitType.AccentLeft || hitType == HitType.AccentRight;
	}

	private static string FormatHitResultLabel(HitResult result, double? timingOffsetSeconds)
	{
		string resultText = result == HitResult.Perfect
			? "PERFECT"
			: (result == HitResult.Good ? "GOOD" : "MISS");

		if (timingOffsetSeconds == null)
		{
			return resultText;
		}

		int ms = (int)Math.Round(timingOffsetSeconds.Value * 1000.0);
		string sign = ms > 0 ? "+" : string.Empty;
		return $"{resultText} {sign}{ms}ms";
	}

	private void OnTempoSliderValueChanged(double value)
	{
		UpdateTempoLabel(value);
		if (_suppressTempoSliderCallback)
		{
			return;
		}

		EmitSignal(SignalName.TempoChangedRequested, value);
	}

	private void UpdateTempoLabel(double bpm)
	{
		if (_tempoValueLabel != null)
		{
			_tempoValueLabel.Text = $"{Math.Round(bpm):0} BPM";
		}
	}

	private void UpdateMemoryTimelineLabel()
	{
		if (_memoryTimelineLabel == null)
		{
			return;
		}

		if (_memoryTimeline.Count == 0)
		{
			_memoryTimelineLabel.Text = "| - |";
			if (_memoryPlayheadLabel != null)
			{
				_memoryPlayheadLabel.Text = "Позиция памяти: -/-";
			}
			return;
		}

		_memoryPlayheadIndex = Mathf.Clamp(_memoryPlayheadIndex, 0, _memoryTimeline.Count - 1);
		System.Text.StringBuilder sb = new System.Text.StringBuilder();
		sb.Append("| ");
		for (int i = 0; i < _memoryTimeline.Count; i++)
		{
			string symbol = FormatHitType(_memoryTimeline[i]);
			if (i == _memoryPlayheadIndex)
			{
				sb.Append('[');
				sb.Append(symbol);
				sb.Append(']');
			}
			else
			{
				sb.Append(symbol);
			}

			if (i < _memoryTimeline.Count - 1)
			{
				sb.Append(' ');
			}
		}
		sb.Append(" |");
		_memoryTimelineLabel.Text = sb.ToString();
	}
}
