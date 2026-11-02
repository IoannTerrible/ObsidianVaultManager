using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using VaultManager.App.ViewModels;

namespace VaultManager.App.Views;

public sealed partial class MatrixView : UserControl
{
	public MatrixView()
	{
		AvaloniaXamlLoader.Load(this);
		ScrollViewer body = this.FindControl<ScrollViewer>("BodyScroller")!;
		ScrollViewer head = this.FindControl<ScrollViewer>("HeaderScroller")!;
		ScrollViewer side = this.FindControl<ScrollViewer>("FixedScroller")!;

		// Three scrollers, one table: the body drives; the side list also answers the wheel.
		body.ScrollChanged += (_, _) =>
		{
			head.Offset = new Vector(body.Offset.X, 0);
			side.Offset = new Vector(0, body.Offset.Y);
		};
		side.ScrollChanged += (_, _) =>
		{
			if (Math.Abs(side.Offset.Y - body.Offset.Y) > 0.5)
			{
				body.Offset = new Vector(body.Offset.X, side.Offset.Y);
			}
		};

		// A press on a cell picks the row, as the plugin name does.
		body.AddHandler(PointerPressedEvent, (_, e) =>
		{
			if (e.Source is StyledElement source && FindRow(source) is { } row)
			{
				row.Select();
			}
		}, RoutingStrategies.Tunnel);
	}

	static MatrixRowVm? FindRow(StyledElement element)
	{
		for (StyledElement? current = element; current is not null; current = current.Parent)
		{
			if (current.DataContext is MatrixRowVm row)
			{
				return row;
			}
		}

		return null;
	}
}
