using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace VaultManager.App.Views;

public sealed partial class LogView : UserControl
{
	bool _follow = true;

	public LogView()
	{
		AvaloniaXamlLoader.Load(this);
		ScrollViewer scroller = this.FindControl<ScrollViewer>("Scroller")!;
		scroller.ScrollChanged += (_, e) =>
		{
			if (e.ExtentDelta.Y != 0)
			{
				if (_follow)
				{
					scroller.ScrollToEnd();
				}

				return;
			}

			_follow = scroller.Offset.Y + scroller.Viewport.Height >= scroller.Extent.Height - 2;
		};
	}
}
