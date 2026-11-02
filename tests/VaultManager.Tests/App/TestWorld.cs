using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using VaultManager.App;
using VaultManager.App.Localization;
using VaultManager.App.Services;
using VaultManager.App.ViewModels;
using VaultManager.Core;
using VaultManager.Core.Catalog;
using VaultManager.Core.Demo;
using VaultManager.Core.Foundation;
using VaultManager.Core.Obsidian;

namespace VaultManager.Tests.App;

/// <summary>The language is process-wide and the dispatcher is one thread, so everything that touches them runs in turn.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AppCollection
{
	public const string Name = "App";
}

public static class HeadlessEntry
{
	public static AppBuilder BuildAvaloniaApp() =>
		AppBuilder.Configure<VaultManager.App.App>()
			.UseSkia()
			.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
			.With(new FontManagerOptions { DefaultFamilyName = "avares://VaultManager/Assets/Fonts#IBM Plex Sans" });
}

/// <summary>Runs test bodies on the UI thread of one headless app, the way the real window runs its view models.</summary>
public static class Ui
{
	static readonly Lazy<HeadlessUnitTestSession> Session = new(() => HeadlessUnitTestSession.StartNew(typeof(HeadlessEntry)));

	public static Task Run(Func<Task> body) => Session.Value.Dispatch(async () =>
	{
		await body();
		return true;
	}, CancellationToken.None);

	public static Task Run(Action body) => Session.Value.Dispatch(() => body(), CancellationToken.None);
}

sealed class RecordedLinks : IShellLauncher
{
	public List<string> Opened { get; } = [];

	public void Open(string uri) => Opened.Add(uri);
}

sealed class FakeFiles : IFilePicker
{
	public string? Folder { get; set; }
	public string? Import { get; set; }
	public string? Export { get; set; }

	public Task<string?> PickFolderAsync() => Task.FromResult(Folder);
	public Task<string?> PickProfileToImportAsync() => Task.FromResult(Import);
	public Task<string?> PickProfileToExportAsync(string suggestedName) => Task.FromResult(Export);
}

/// <summary>A catalog that records every call, so a test can prove nothing touched the network on its own.</summary>
sealed class CountingCatalog : ICatalogClient
{
	readonly DemoCatalog _inner = new();

	public List<string> Calls { get; } = [];
	public TaskCompletionSource? HoldDownload { get; set; }
	public TaskCompletionSource? DownloadStarted { get; set; }
	public bool Unreachable { get; set; }

	void Note(string call)
	{
		Calls.Add(call);
		if (Unreachable)
		{
			throw new HttpRequestException("offline");
		}
	}

	public Task<long?> CatalogSizeAsync(CancellationToken cancellationToken = default)
	{
		Note("size");
		return _inner.CatalogSizeAsync(cancellationToken);
	}

	public Task<IReadOnlyList<VaultManager.Core.Store.CachedCatalogEntry>> FetchCatalogAsync(CancellationToken cancellationToken = default)
	{
		Note("catalog");
		return _inner.FetchCatalogAsync(cancellationToken);
	}

	public Task<string?> LatestVersionAsync(string repo, CancellationToken cancellationToken = default)
	{
		Note("latest " + repo);
		return _inner.LatestVersionAsync(repo, cancellationToken);
	}

	public Task<IReadOnlyList<ReleaseFile>> ReleaseFilesAsync(string repo, string version, CancellationToken cancellationToken = default)
	{
		Note("files " + repo);
		return _inner.ReleaseFilesAsync(repo, version, cancellationToken);
	}

	public async Task DownloadReleaseAsync(string repo, string version, string targetDirectory, CancellationToken cancellationToken = default)
	{
		Note("download " + repo);
		DownloadStarted?.TrySetResult();
		if (HoldDownload is { } hold)
		{
			await hold.Task;
		}

		await _inner.DownloadReleaseAsync(repo, version, targetDirectory, cancellationToken);
	}
}

/// <summary>The sealed demo world in a temp folder, with a real host and a real shell view model over it.</summary>
sealed class TestWorld : IDisposable
{
	TestWorld(string root, AppPaths paths, DemoProcess process, CountingCatalog catalog, CoreHost host, ShellViewModel shell, RecordedLinks links, FakeFiles files)
	{
		Root = root;
		Paths = paths;
		Process = process;
		Catalog = catalog;
		Host = host;
		Shell = shell;
		Links = links;
		Files = files;
	}

	public string Root { get; }
	public AppPaths Paths { get; }
	public DemoProcess Process { get; }
	public CountingCatalog Catalog { get; }
	public CoreHost Host { get; }
	public ShellViewModel Shell { get; }
	public RecordedLinks Links { get; }
	public FakeFiles Files { get; }

	public string VaultsDir => Path.Combine(Root, "Vaults");

	public string Vault(string name) => Path.Combine(VaultsDir, name);

	/// <param name="foreignProcess">Stands in for Obsidian; the world then looks like a normal run, not a demo.</param>
	public static TestWorld Create(bool demoFiles = false, IObsidianProcess? foreignProcess = null)
	{
		string root = Path.Combine(Path.GetTempPath(), "vm-app-test-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		AppPaths paths = DemoWorkspace.Create(root);
		var process = new DemoProcess();
		var catalog = new CountingCatalog();
		var host = new CoreHost(paths, foreignProcess ?? process, catalog);
		var links = new RecordedLinks();
		var files = new FakeFiles();
		IFilePicker picker = demoFiles ? new DemoFilePicker(root) : files;
		var services = foreignProcess is null
			? new AppServices(host, links, picker, new AvaloniaUiThread(), process, root, "0.6.0.0")
			: new AppServices(host, links, picker, new AvaloniaUiThread(), null, null, "0.6.0.0");
		Texts.Use(AppLanguage.English);
		return new TestWorld(root, paths, process, catalog, host, new ShellViewModel(services), links, files);
	}

	public static async Task<TestWorld> StartAsync(bool demoFiles = false)
	{
		TestWorld world = Create(demoFiles);
		await world.Shell.StartAsync();
		return world;
	}

	/// <summary>A new host and window state over the same files, as after closing the app and starting it again.</summary>
	public ShellViewModel Restart()
	{
		var host = new CoreHost(Paths, Process, Catalog);
		var services = new AppServices(host, Links, Files, new AvaloniaUiThread(), Process, Root, "0.6.0.0");
		return new ShellViewModel(services);
	}

	public VaultState VaultNamed(string name) => Shell.Data.Snapshot!.Vaults.First(v => v.DisplayName == name);

	public PluginRow Plugin(string id) => Shell.Data.Snapshot!.Plugins.First(p => p.Id == id);

	public void Dispose()
	{
		Texts.Use(AppLanguage.English);

		// A background audit may still hold the database for a moment; a temp folder is not worth failing a test over.
		for (int attempt = 0; attempt < 20 && Directory.Exists(Root); attempt++)
		{
			try
			{
				Folder.Delete(Root);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				Thread.Sleep(50);
			}
		}
	}
}
