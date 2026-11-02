using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using VaultManager.App.ViewModels;

namespace VaultManager.App.Views;

public sealed partial class OnlineQuestionView : UserControl
{
	OnlineViewModel? _watched;

	public OnlineQuestionView()
	{
		AvaloniaXamlLoader.Load(this);
		DataContextChanged += (_, _) =>
		{
			if (_watched is not null)
			{
				_watched.PropertyChanged -= OnChanged;
			}

			_watched = DataContext as OnlineViewModel;
			if (_watched is not null)
			{
				_watched.PropertyChanged += OnChanged;
			}
		};
	}

	// Focus lands on Cancel: a stray Enter must not start a paid download.
	void OnChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(OnlineViewModel.IsOpen) && _watched is { IsOpen: true })
		{
			Dispatcher.UIThread.Post(() => this.FindControl<Button>("CancelButton")?.Focus(), DispatcherPriority.Input);
		}
	}
}
