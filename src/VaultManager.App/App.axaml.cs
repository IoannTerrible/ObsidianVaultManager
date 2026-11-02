using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using VaultManager.App.Localization;
using VaultManager.App.Services;
using VaultManager.App.ViewModels;
using VaultManager.App.Views;

namespace VaultManager.App;

public sealed class App : Application
{
	/// <summary>Set by Program before the app starts. A headless test builds its own window and leaves it empty.</summary>
	internal static AppSession? Session { get; set; }

	public override void Initialize() => AvaloniaXamlLoader.Load(this);

	public override void OnFrameworkInitializationCompleted()
	{
		if (Session is { } session && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			MainWindow? window = null;
			IFilePicker files = session.DemoRoot is { } root ? new DemoFilePicker(root) : new NativeFilePicker(() => window);
			IShellLauncher shell = session.DemoRoot is null ? new SystemShell() : new RecordingShell(_ => { });
			var services = new AppServices(session.Host, shell, files, new AvaloniaUiThread(), session.Demo, session.DemoRoot, AppSession.Version);

			ShellViewModel.LoadLanguage(session.Host.Database);
			var shellVm = new ShellViewModel(services);
			window = new MainWindow(shellVm);
			desktop.MainWindow = window;
			desktop.Exit += (_, _) => session.Dispose();

			// Scanning after the window is up: the drives may be slow, and the window must never wait on them.
			window.Opened += (_, _) => _ = shellVm.StartAsync();
			Dispatcher.UIThread.UnhandledException += (_, e) =>
			{
				// A queue of unapplied changes is worth more than a clean crash: say it and keep the window alive.
				shellVm.Log.Add(LogKind.Problem, Texts.Format(K.Log_Unexpected, e.Exception.GetType().Name));
				e.Handled = true;
			};
			var watch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
			watch.Tick += (_, _) => _ = shellVm.WatchObsidianAsync();
			watch.Start();
		}

		base.OnFrameworkInitializationCompleted();
	}
}
