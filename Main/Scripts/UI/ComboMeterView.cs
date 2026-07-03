using Godot;

public partial class ComboMeterView : Control
{
	[Export]
	public NodePath StreakLabelPath { get; set; } = new NodePath("Panel/VBoxContainer/StreakLabel");

	[Export]
	public NodePath MultiplierLabelPath { get; set; } = new NodePath("Panel/VBoxContainer/MultiplierLabel");

	[Export]
	public NodePath PlayerHpLabelPath { get; set; } = new NodePath("Panel/VBoxContainer/PlayerHpLabel");

	private Label _streakLabel;
	private Label _multiplierLabel;
	private Label _playerHpLabel;

	public override void _Ready()
	{
		_streakLabel = GetNodeOrNull<Label>(StreakLabelPath);
		_multiplierLabel = GetNodeOrNull<Label>(MultiplierLabelPath);
		_playerHpLabel = GetNodeOrNull<Label>(PlayerHpLabelPath);
	}

	public void SetValues(int streak, int multiplier)
	{
		if (_streakLabel != null)
		{
			_streakLabel.Text = $"Streak: {streak}";
		}

		if (_multiplierLabel != null)
		{
			_multiplierLabel.Text = $"Multiplier: x{multiplier}";
		}
	}

	public void SetPlayerHp(int currentHp, int maxHp)
	{
		if (_playerHpLabel != null)
		{
			_playerHpLabel.Text = $"Player HP: {currentHp}/{maxHp}";
		}
	}
}
