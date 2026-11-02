using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using VaultManager.App.Localization;

namespace VaultManager.App.Controls;

/// <summary>One sentence with parts in their own type role: names strong, versions and paths monospace.</summary>
public sealed class SegmentedText : TextBlock
{
	public static readonly StyledProperty<IReadOnlyList<Seg>?> SegmentsProperty = AvaloniaProperty.Register<SegmentedText, IReadOnlyList<Seg>?>(nameof(Segments));

	static SegmentedText() => SegmentsProperty.Changed.AddClassHandler<SegmentedText>((text, _) => text.Fill());

	protected override Type StyleKeyOverride => typeof(TextBlock);

	public IReadOnlyList<Seg>? Segments
	{
		get => GetValue(SegmentsProperty);
		set => SetValue(SegmentsProperty, value);
	}

	void Fill()
	{
		Inlines ??= [];
		Inlines.Clear();
		foreach (Seg seg in Segments ?? [])
		{
			var run = new Run(seg.Text);
			switch (seg.Role)
			{
				case SegRole.Strong:
					if (this.TryFindResource("Font.ReadSemi", out object? strong) && strong is FontFamily semi)
					{
						run.FontFamily = semi;
					}

					break;
				case SegRole.Value:
					if (this.TryFindResource("Font.Value", out object? value) && value is FontFamily mono)
					{
						run.FontFamily = mono;
						run.FontSize = 12;
					}

					break;
			}

			Inlines.Add(run);
		}
	}
}
