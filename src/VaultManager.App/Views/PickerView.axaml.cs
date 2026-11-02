using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using VaultManager.App.ViewModels;

namespace VaultManager.App.Views;

public sealed partial class PickerView : UserControl
{
	PickerViewModel? _watched;

	public PickerView()
	{
		AvaloniaXamlLoader.Load(this);
		TextBox filter = this.FindControl<TextBox>("FilterBox")!;
		filter.AddHandler(KeyDownEvent, (_, e) =>
		{
			if (e.Key == Key.Enter && DataContext is PickerViewModel picker)
			{
				picker.AddFirst();
				e.Handled = true;
			}
		}, RoutingStrategies.Tunnel);

		DataContextChanged += (_, _) =>
		{
			if (_watched is not null)
			{
				_watched.RefocusFilter -= FocusFilter;
				_watched.PropertyChanged -= OnPicker;
			}

			_watched = DataContext as PickerViewModel;
			if (_watched is not null)
			{
				_watched.RefocusFilter += FocusFilter;
				_watched.PropertyChanged += OnPicker;
			}
		};
	}

	void OnPicker(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(PickerViewModel.IsOpen) && _watched is { IsOpen: true })
		{
			FocusFilter();
		}
	}

	void FocusFilter() => Dispatcher.UIThread.Post(() => this.FindControl<TextBox>("FilterBox")?.Focus(), DispatcherPriority.Input);
}
