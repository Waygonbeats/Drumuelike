using Godot;
using System;

public partial class RhythmManager : Node
{
	public enum GrooveMode
	{
		Straight = 0,
		Swing = 1
	}

	// BPM ритма, от которого считается длительность удара.
	[Export(PropertyHint.Range, "30,240,1")]
	public double Bpm { get; set; } = 70.0;

	// Ссылка на AudioStreamPlayer для синхронизации времени с музыкой.
	[Export]
	public NodePath AudioPlayerPath { get; set; } = new NodePath();

	// Режим метронома: воспроизводим короткий клик на каждый beat.
	[Export]
	public bool UseBeatClickMetronome { get; set; } = true;

	// Если включено, метроном кликает по subdivision-шагам (1 тик = 1 нота).
	[Export]
	public bool MetronomeUseSubdivisionSteps { get; set; } = false;

	// Деление доли: 1 = четверти, 2 = восьмые, 4 = шестнадцатые.
	[Export(PropertyHint.Range, "1,4,1")]
	public int Subdivision { get; set; } = 2;

	// Режим грува тайминга: straight или swing.
	[Export]
	public GrooveMode TimingGroove { get; set; } = GrooveMode.Straight;

	// Позиция второй восьмой в доле для swing (0.5 = straight).
	[Export(PropertyHint.Range, "0.50,0.75,0.01")]
	public double SwingRatio { get; set; } = 0.62;

	public double BeatIntervalSeconds => Bpm > 0.0 ? 60.0 / Bpm : 0.0;
	public double StepIntervalSeconds => BeatIntervalSeconds > 0.0
		? BeatIntervalSeconds / Math.Max(1, Subdivision)
		: 0.0;
	public double SongTimeSeconds => _songTimeSeconds;
	public int CurrentBeatIndex => BeatIntervalSeconds > 0.0
		? (int)Math.Floor(_songTimeSeconds / BeatIntervalSeconds)
		: 0;
	public int CurrentStepIndex => GetCurrentStepIndexByTime(_songTimeSeconds);

	private AudioStreamPlayer _audioPlayer;
	private double _songTimeSeconds;
	private bool _isRunning;
	private int _lastPlayedMetronomeTickIndex = -1;

	public override void _Ready()
	{
		if (!AudioPlayerPath.IsEmpty)
		{
			_audioPlayer = GetNodeOrNull<AudioStreamPlayer>(AudioPlayerPath);
		}
	}

	public override void _Process(double delta)
	{
		if (!_isRunning)
		{
			return;
		}

		if (!UseBeatClickMetronome && _audioPlayer != null && _audioPlayer.Playing)
		{
			// Если музыка играет, берём время из аудиосистемы для точной синхронизации.
			_songTimeSeconds = GetAudioSyncedSongTime(_audioPlayer);
		}
		else
		{
			// Фолбэк без аудио или режим метронома: считаем время по кадрам.
			_songTimeSeconds += delta;
		}

		if (UseBeatClickMetronome)
		{
			PlayBeatClickIfNeeded();
		}
	}

	public void StartRhythm()
	{
		_songTimeSeconds = 0.0;
		_isRunning = true;
		_lastPlayedMetronomeTickIndex = -1;

		if (!UseBeatClickMetronome && _audioPlayer != null && !_audioPlayer.Playing)
		{
			_audioPlayer.Play();
		}
		else if (UseBeatClickMetronome)
		{
			// При старте сразу даём первый "тик" нулевого бита.
			PlayBeatClickIfNeeded();
		}
	}

	public void StopRhythm()
	{
		_isRunning = false;

		if (_audioPlayer != null && _audioPlayer.Playing)
		{
			_audioPlayer.Stop();
		}
	}

	public double GetBeatTimeSeconds(int beatIndex)
	{
		if (beatIndex < 0 || BeatIntervalSeconds <= 0.0)
		{
			return 0.0;
		}

		return beatIndex * BeatIntervalSeconds;
	}

	public double GetTimeFromNearestBeatSeconds()
	{
		if (BeatIntervalSeconds <= 0.0)
		{
			return 0.0;
		}

		// Отрицательное значение — ранний ввод, положительное — поздний.
		double beatFloat = _songTimeSeconds / BeatIntervalSeconds;
		int nearestBeat = (int)Math.Round(beatFloat);
		double nearestBeatTime = nearestBeat * BeatIntervalSeconds;
		return _songTimeSeconds - nearestBeatTime;
	}

	public double GetTimeFromNearestStepSeconds()
	{
		if (StepIntervalSeconds <= 0.0)
		{
			return 0.0;
		}

		int nearestStep = GetNearestStepIndexByTime(_songTimeSeconds);
		double nearestStepTime = GetStepTimeSeconds(nearestStep);
		return _songTimeSeconds - nearestStepTime;
	}

	public int GetNearestStepIndex()
	{
		return GetNearestStepIndexByTime(_songTimeSeconds);
	}

	public int GetNearestStepIndexByTime(double timeSeconds)
	{
		if (StepIntervalSeconds <= 0.0)
		{
			return 0;
		}

		int current = GetCurrentStepIndexByTime(timeSeconds);
		int nearestStep = current;
		double nearestDistance = double.MaxValue;

		// Проверяем соседние шаги, чтобы корректно найти ближайший с учётом swing.
		for (int i = -1; i <= 1; i++)
		{
			int candidate = Math.Max(0, current + i);
			double candidateTime = GetStepTimeSeconds(candidate);
			double distance = Math.Abs(timeSeconds - candidateTime);
			if (distance < nearestDistance)
			{
				nearestDistance = distance;
				nearestStep = candidate;
			}
		}

		return nearestStep;
	}

	private static double GetAudioSyncedSongTime(AudioStreamPlayer audioPlayer)
	{
		// Классическая формула Godot: позиция проигрывания + время микса - задержка вывода.
		double time = audioPlayer.GetPlaybackPosition();
		time += AudioServer.GetTimeSinceLastMix();
		time -= AudioServer.GetOutputLatency();
		if (time < 0.0)
		{
			time = 0.0;
		}

		return time;
	}

	private void PlayBeatClickIfNeeded()
	{
		if (_audioPlayer == null || _audioPlayer.Stream == null || BeatIntervalSeconds <= 0.0)
		{
			return;
		}

		int tickIndex = MetronomeUseSubdivisionSteps ? CurrentStepIndex : CurrentBeatIndex;
		if (tickIndex == _lastPlayedMetronomeTickIndex)
		{
			return;
		}

		_lastPlayedMetronomeTickIndex = tickIndex;
		_audioPlayer.Play();
	}

	private int GetCurrentStepIndexByTime(double timeSeconds)
	{
		if (StepIntervalSeconds <= 0.0 || timeSeconds <= 0.0)
		{
			return 0;
		}

		// Swing-профиль поддерживаем для восьмых (Subdivision=2), иначе straight.
		if (TimingGroove != GrooveMode.Swing || Math.Max(1, Subdivision) != 2 || BeatIntervalSeconds <= 0.0)
		{
			return (int)Math.Floor(timeSeconds / StepIntervalSeconds);
		}

		double clampedSwing = Math.Clamp(SwingRatio, 0.5, 0.75);
		int beatIndex = (int)Math.Floor(timeSeconds / BeatIntervalSeconds);
		double beatStart = beatIndex * BeatIntervalSeconds;
		double withinBeat = timeSeconds - beatStart;
		double splitTime = BeatIntervalSeconds * clampedSwing;
		return withinBeat < splitTime ? beatIndex * 2 : beatIndex * 2 + 1;
	}

	public double GetStepTimeSeconds(int stepIndex)
	{
		if (stepIndex <= 0 || StepIntervalSeconds <= 0.0)
		{
			return 0.0;
		}

		if (TimingGroove != GrooveMode.Swing || Math.Max(1, Subdivision) != 2 || BeatIntervalSeconds <= 0.0)
		{
			return stepIndex * StepIntervalSeconds;
		}

		double clampedSwing = Math.Clamp(SwingRatio, 0.5, 0.75);
		int beatIndex = stepIndex / 2;
		bool offBeat = (stepIndex % 2) == 1;
		double beatStart = beatIndex * BeatIntervalSeconds;
		if (!offBeat)
		{
			return beatStart;
		}

		return beatStart + BeatIntervalSeconds * clampedSwing;
	}
}
