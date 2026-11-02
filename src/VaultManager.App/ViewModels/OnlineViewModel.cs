using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VaultManager.App.Localization;
using VaultManager.App.Logic;
using VaultManager.Core.Catalog;
using VaultManager.Core.Store;

namespace VaultManager.App.ViewModels;

internal enum OnlineStage { Idle, AskingSize, Question, Checking, Failed }

internal enum OfferStage { None, Asking, Sized, Failed }

internal sealed record DownloadOffer(OfferStage Stage, long Size = 0, string? Version = null);

/// <summary>
/// Everything that touches the network. The connection is expensive, so every call here follows
/// a click, the size is named before anything big, and an answer of "cannot tell" stays that.
/// </summary>
internal sealed partial class OnlineViewModel(ShellViewModel shell) : ObservableObject
{
	const string CheckedAtKey = "online.checkedAt";

	readonly Dictionary<string, DownloadOffer> _offers = new(StringComparer.Ordinal);
	CancellationTokenSource? _running;
	long? _catalogSize;

	[ObservableProperty] OnlineStage _stage;
	[ObservableProperty] string _message = string.Empty;
	[ObservableProperty] bool _isOpen;
	[ObservableProperty] bool _canConfirm;

	IReadOnlyCollection<string> InstalledIds() =>
		shell.Data.Snapshot?.Plugins.Where(p => p.Installs.Any(i => i.HasFolder)).Select(p => p.Id).ToList() ?? [];

	public void LoadCheckedAt()
	{
		string? stored = shell.Host.Database.Setting(CheckedAtKey);
		shell.Data.CheckedAt = DateTimeOffset.TryParse(stored, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset at) ? at : null;
	}

	public void CloseQuestion()
	{
		if (Stage is OnlineStage.Question or OnlineStage.Failed)
		{
			Stage = OnlineStage.Idle;
			IsOpen = false;
		}
	}

	/// <summary>Only asks how big the catalog is. The catalog itself waits for the answer to the question that follows.</summary>
	[RelayCommand]
	public async Task Start()
	{
		if (Stage is OnlineStage.AskingSize or OnlineStage.Checking)
		{
			return;
		}

		IsOpen = true;
		Stage = OnlineStage.AskingSize;
		CanConfirm = false;
		Message = Texts.Get(K.Online_AskingSize);
		try
		{
			_catalogSize = await shell.Host.Catalog.CatalogSizeAsync();
			Stage = OnlineStage.Question;
			CanConfirm = true;
			int requests = InstalledIds().Count;
			Message = _catalogSize is { } size
				? Texts.Format(K.Online_Question, Formats.Size(size), Texts.Count(K.Plural.SmallRequests, requests))
				: Texts.Format(K.Online_QuestionNoSize, Texts.Count(K.Plural.SmallRequests, requests));
		}
		catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
		{
			Fail();
		}
	}

	[RelayCommand]
	public async Task Confirm()
	{
		if (Stage != OnlineStage.Question)
		{
			return;
		}

		Stage = OnlineStage.Checking;
		CanConfirm = false;
		Message = Texts.Get(K.Online_Checking);
		using var cancel = new CancellationTokenSource();
		_running = cancel;
		try
		{
			IReadOnlyCollection<string> ids = InstalledIds();
			OnlineCheck result = await Task.Run(() => shell.Host.Online.CheckAsync(ids, cancel.Token));
			DateTimeOffset now = DateTimeOffset.Now;
			shell.Host.Database.SetSetting(CheckedAtKey, now.ToString("O", CultureInfo.InvariantCulture));
			shell.Data.CheckedAt = now;
			shell.Data.Online = shell.Host.Database.OnlineVersions();
			shell.Data.Catalog = shell.Host.Database.Catalog();
			Stage = OnlineStage.Idle;
			IsOpen = false;
			string line = Texts.Format(K.Log_Checked, Texts.Count(K.Plural.Plugins, result.Versions.Count), Texts.Count(K.Plural.NotInCatalog, result.NotInCatalog.Count));
			shell.Log.Add(LogKind.Ok, result.Failed > 0 ? line + " " + Texts.Count(K.Plural.NoAnswer, result.Failed) : line);
			shell.Rebuild();
		}
		catch (OperationCanceledException) when (cancel.IsCancellationRequested)
		{
			Stage = OnlineStage.Idle;
			IsOpen = false;
			shell.Log.Add(LogKind.Plain, Texts.Get(K.Log_CheckStopped));
		}
		catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
		{
			Fail();
		}
		finally
		{
			_running = null;
		}
	}

	void Fail()
	{
		Stage = OnlineStage.Failed;
		CanConfirm = false;
		Message = Texts.Get(K.Online_Failed);
		shell.Log.Add(LogKind.Problem, Message);
	}

	[RelayCommand]
	public void Stop() => _running?.Cancel();

	[RelayCommand]
	public void Cancel()
	{
		if (Stage == OnlineStage.Checking)
		{
			Stop();
			return;
		}

		Stage = OnlineStage.Idle;
		IsOpen = false;
	}

	public DownloadOffer OfferFor(CachedCatalogEntry entry, string? version) =>
		_offers.TryGetValue(OfferKey(entry.Id, version), out DownloadOffer? offer) ? offer : new DownloadOffer(OfferStage.None, Version: version);

	static string OfferKey(string id, string? version) => id + "|" + version;

	/// <summary>Sums the release's files with HEAD requests, so a download never starts without its size on screen.</summary>
	public async Task AskSizeAsync(CachedCatalogEntry entry, string? knownVersion)
	{
		string key = OfferKey(entry.Id, knownVersion);
		_offers[key] = new DownloadOffer(OfferStage.Asking, Version: knownVersion);
		shell.Rebuild();
		try
		{
			string? version = knownVersion ?? await shell.Host.Catalog.LatestVersionAsync(entry.Repo);
			if (version is null)
			{
				_offers[key] = new DownloadOffer(OfferStage.Failed);
			}
			else
			{
				IReadOnlyList<ReleaseFile> files = await shell.Host.Catalog.ReleaseFilesAsync(entry.Repo, version);
				_offers[key] = new DownloadOffer(OfferStage.Sized, files.Sum(f => f.SizeBytes), version);
			}
		}
		catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
		{
			_offers[key] = new DownloadOffer(OfferStage.Failed);
		}

		shell.Rebuild();
	}
}
