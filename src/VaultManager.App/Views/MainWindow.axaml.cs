using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using VaultManager.App.ViewModels;

namespace VaultManager.App.Views;

public sealed partial class MainWindow : Window
{
	public MainWindow()
	{
		AvaloniaXamlLoader.Load(this);
#if DEBUG
		this.AttachDevTools();
#endif
	}

	internal MainWindow(ShellViewModel shell)
		: this()
	{
		DataContext = shell;
		shell.LanguageChanged += Rebuild;
		Rebuild();
	}

	// Strings in markup are read once, so a language switch makes a new view. Every piece of state lives in the view models.
	void Rebuild()
	{
		var view = new MainView { DataContext = DataContext };
		Content = view;
	}
}
