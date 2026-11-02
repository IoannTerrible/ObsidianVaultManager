using Avalonia.Controls.Primitives;

namespace VaultManager.App.Controls;

/// <summary>Looks like a toggle, but the view model alone decides whether it is pressed: a second click must not un-press the current section.</summary>
public sealed class StateButton : ToggleButton
{
	protected override Type StyleKeyOverride => typeof(ToggleButton);

	protected override void Toggle()
	{
	}
}
