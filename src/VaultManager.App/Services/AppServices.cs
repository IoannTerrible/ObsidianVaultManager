using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using VaultManager.App.Localization;
using VaultManager.Core;
using VaultManager.Core.Catalog;
using VaultManager.Core.Demo;
using VaultManager.Core.Foundation;
using VaultManager.Core.Obsidian;
using VaultManager.Core.Profiles;

namespace VaultManager.App.Services;

/// <summary>Hands a link or a vault to Windows. Only ever called from a click.</summary>
internal interface IShellLauncher
{
	void Open(string uri);
}

internal sealed class SystemShell : IShellLauncher
{
	public void Open(string uri)
	{
		using Process? process = Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
	}
}

/// <summary>A demo stays in its folder, so links are written down instead of followed.</summary>
internal sealed class RecordingShell(Action<string> onOpen) : IShellLauncher
{
	public void Open(string uri) => onOpen(uri);
}

internal interface IFilePicker
{
	Task<string?> PickFolderAsync();
	Task<string?> PickProfileToImportAsync();
	Task<string?> PickProfileToExportAsync(string suggestedName);
}

internal sealed class NativeFilePicker(Func<TopLevel?> topLevel) : IFilePicker
{
	static FilePickerFileType ProfileType => new(Texts.Get(K.Profile_FileType)) { Patterns = ["*" + ProfileFile.Extension] };

	public async Task<string?> PickFolderAsync()
	{
		if (topLevel() is not { } top)
		{
			return null;
		}

		IReadOnlyList<IStorageFolder> folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
		return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
	}

	public async Task<string?> PickProfileToImportAsync()
	{
		if (topLevel() is not { } top)
		{
			return null;
		}

		IReadOnlyList<IStorageFile> files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false, FileTypeFilter = [ProfileType] });
		return files.Count > 0 ? files[0].TryGetLocalPath() : null;
	}

	public async Task<string?> PickProfileToExportAsync(string suggestedName)
	{
		if (topLevel() is not { } top)
		{
			return null;
		}

		IStorageFile? file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
		{
			SuggestedFileName = suggestedName,
			DefaultExtension = ProfileFile.Extension.TrimStart('.'),
			FileTypeChoices = [ProfileType],
		});
		return file?.TryGetLocalPath();
	}
}

/// <summary>A native picker can walk out of the demo folder, so a demo answers from inside it instead.</summary>
internal sealed class DemoFilePicker(string root) : IFilePicker
{
	string ExportsDir => Path.Combine(root, "exports");

	public Task<string?> PickFolderAsync() => Task.FromResult<string?>(Path.Combine(root, "Vaults"));

	public Task<string?> PickProfileToImportAsync()
	{
		string? newest = Directory.Exists(ExportsDir)
			? new DirectoryInfo(ExportsDir).EnumerateFiles("*" + ProfileFile.Extension).OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.FullName
			: null;
		return Task.FromResult(newest);
	}

	public Task<string?> PickProfileToExportAsync(string suggestedName)
	{
		Directory.CreateDirectory(ExportsDir);
		return Task.FromResult<string?>(Path.Combine(ExportsDir, suggestedName));
	}
}

internal interface IUiThread
{
	void Post(Action action);
}

internal sealed class AvaloniaUiThread : IUiThread
{
	public void Post(Action action) => Dispatcher.UIThread.Post(action);
}

internal sealed record AppServices(CoreHost Host, IShellLauncher Shell, IFilePicker Files, IUiThread Ui, DemoProcess? Demo, string? DemoRoot, string Version)
{
	public bool IsDemo => Demo is not null;
}

/// <summary>Everything one run of the app owns. The single HttpClient is created here.</summary>
internal sealed class AppSession : IDisposable
{
	readonly HttpClient? _http;
	readonly string? _demoRoot;

	AppSession(CoreHost host, DemoProcess? demo, string? demoRoot, HttpClient? http)
	{
		Host = host;
		Demo = demo;
		_demoRoot = demoRoot;
		_http = http;
	}

	public CoreHost Host { get; }
	public DemoProcess? Demo { get; }
	public string? DemoRoot => _demoRoot;

	public static string Version =>
		typeof(AppSession).Assembly.GetName().Version?.ToString(4) ?? "0.0.0.0";

	public static AppSession ForThisUser()
	{
		var http = new HttpClient();
		GitHubCatalogClient.Configure(http, Version);
		return new AppSession(new CoreHost(AppPaths.ForThisUser(), new SystemObsidianProcess(), new GitHubCatalogClient(http)), null, null, http);
	}

	public static AppSession ForDemo(string root)
	{
		Directory.CreateDirectory(root);
		AppPaths paths = DemoWorkspace.Create(root);
		var process = new DemoProcess();
		return new AppSession(new CoreHost(paths, process, new DemoCatalog()), process, root, null);
	}

	public static string NewDemoRoot() => Path.Combine(Path.GetTempPath(), "vault-manager-demo-" + Guid.NewGuid().ToString("N"));

	/// <summary>Core stages downloads under the system temp folder; a demo must not write there.</summary>
	public static void ConfineTempTo(string root)
	{
		string temp = Path.Combine(root, "temp");
		Directory.CreateDirectory(temp);
		Environment.SetEnvironmentVariable("TMP", temp);
		Environment.SetEnvironmentVariable("TEMP", temp);
	}

	public void Dispose()
	{
		_http?.Dispose();
		if (_demoRoot is not null)
		{
			try
			{
				Folder.Delete(_demoRoot);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				// A file still in use leaves a folder under temp, which Windows cleans in time. Exit must not crash for it.
			}
		}
	}
}
