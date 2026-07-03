using Godot;
using System;

public partial class InputJudge : Node
{
	// Ссылка на RhythmManager как на единственный источник времени.
	[Export]
	public NodePath RhythmManagerPath { get; set; } = new NodePath();

	// Окно идеального попадания (в секундах, по модулю).
	[Export(PropertyHint.Range, "0.01,0.20,0.005")]
	public double PerfectWindowSeconds { get; set; } = 0.06;

	// Окно хорошего попадания (в секундах, по модулю).
	[Export(PropertyHint.Range, "0.02,0.35,0.005")]
	public double GoodWindowSeconds { get; set; } = 0.12;

	// Калибровка задержки ввода в миллисекундах (плюс = считаем ввод чуть позже).
	[Export(PropertyHint.Range, "-120,120,1")]
	public double InputLatencyOffsetMs { get; set; } = 0.0;

	private RhythmManager _rhythmManager;

	public override void _Ready()
	{
		if (!RhythmManagerPath.IsEmpty)
		{
			_rhythmManager = GetNodeOrNull<RhythmManager>(RhythmManagerPath);
		}
	}

	public HitResult JudgeCurrentInput()
	{
		return JudgeCurrentInputDetailed(out _, out _);
	}

	public HitResult JudgeCurrentInputDetailed(out int judgedStepIndex, out double signedOffsetSeconds)
	{
		judgedStepIndex = -1;
		signedOffsetSeconds = 0.0;

		if (_rhythmManager == null)
		{
			return HitResult.Miss;
		}

		double adjustedSongTime = _rhythmManager.SongTimeSeconds + InputLatencyOffsetMs * 0.001;
		judgedStepIndex = _rhythmManager.GetNearestStepIndexByTime(adjustedSongTime);
		double targetStepTime = _rhythmManager.GetStepTimeSeconds(judgedStepIndex);
		signedOffsetSeconds = adjustedSongTime - targetStepTime;
		return JudgeByOffset(signedOffsetSeconds);
	}

	public HitResult JudgeInputForStepDetailed(int expectedStepIndex, out int judgedStepIndex, out double signedOffsetSeconds)
	{
		judgedStepIndex = expectedStepIndex;
		signedOffsetSeconds = 0.0;

		if (_rhythmManager == null || expectedStepIndex < 0)
		{
			return HitResult.Miss;
		}

		double adjustedSongTime = _rhythmManager.SongTimeSeconds + InputLatencyOffsetMs * 0.001;
		double targetStepTime = _rhythmManager.GetStepTimeSeconds(expectedStepIndex);
		signedOffsetSeconds = adjustedSongTime - targetStepTime;
		return JudgeByOffset(signedOffsetSeconds);
	}

	public HitResult JudgeByOffset(double offsetSeconds)
	{
		// Сравниваем только расстояние до бита, без учёта знака (раньше/позже).
		double absoluteOffset = Math.Abs(offsetSeconds);
		const double epsilon = 1e-6;
		double perfectWindow = PerfectWindowSeconds;
		double goodWindow = GoodWindowSeconds;

		// Ограничиваем окна мягко, чтобы сохранить чёткость, но не ломать проходимость паттернов.
		if (_rhythmManager != null && _rhythmManager.StepIntervalSeconds > 0.0)
		{
			double halfStep = _rhythmManager.StepIntervalSeconds * 0.5;
			double maxGood = halfStep * 0.98;
			double maxPerfect = halfStep * 0.70;

			goodWindow = Math.Min(goodWindow, maxGood);
			perfectWindow = Math.Min(perfectWindow, Math.Min(goodWindow, maxPerfect));
		}

		if (absoluteOffset <= perfectWindow + epsilon)
		{
			return HitResult.Perfect;
		}

		if (absoluteOffset <= goodWindow + epsilon)
		{
			return HitResult.Good;
		}

		return HitResult.Miss;
	}
}
