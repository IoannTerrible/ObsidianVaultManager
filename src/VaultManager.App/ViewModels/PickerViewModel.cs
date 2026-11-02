using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VaultManager.App.Localization;
using VaultManager.App.Logic;
using VaultManager.Core.Changes;
using VaultManager.Core.Obsidian;
using VaultManager.Core.Store;

namespace VaultManager.App.ViewModels;

internal sealed partial class PickerRowVm(string id) : ObservableObject
{
	public string Id { get; } = id;

	[ObservableProperty] string _name = string.Empty;
	[ObservableProperty] string _sub = string.Empty;
	[ObservableProperty] string _version = string.Empty;
	[ObservableProperty] bool _isWaiting;

	public ActionSlot Add { get; } = new();
}

/// <summary>Add plugin. Local copies come first and cost nothing; catalog rows name their size before they can be queued.</summary>
internal sealed partial class PickerViewModel(ShellViewModel shell) : ObservableObject
{
	const int CatalogRowLimit = 40;

	public ObservableCollection<PickerRowVm> Rows { get; } = [];
	public ObservableCollection<PickerRowVm> CatalogRows { get; } = [];

	/// <summary>A click on Add moves focus to the button; the view puts it back in the filter.</summary>
	public event Action? RefocusFilter;

	[ObservableProperty] bool _isOpen;
	[ObservableProperty] string _filter = string.Empty;
	[ObservableProperty] bool _copySettings;
	[ObservableProperty] string _title = string.Empty;
	[ObservableProperty] string _empty = string.Empty;
	[ObservableProperty] bool _isEmpty;
	[ObservableProperty] bool _hasCatalog;
	[ObservableProperty] string _note = string.Empty;
	[ObservableProperty] string _catalogMore = string.Empty;
	[ObservableProperty] bool _hasCatalogMore;

	WorkspaceData Data => shell.Data;

	partial void OnFilterChanged(string value) => Rebuild();

	partial void OnCopySettingsChanged(bool value) => Rebuild();

	[RelayCommand]
	public void Toggle()
	{
		IsOpen = !IsOpen;
		if (IsOpen)
		{
			Filter = string.Empty;
			Rebuild();
		}
	}

	[RelayCommand]
	public void Close()
	{
		if (IsOpen)
		{
			IsOpen = false;
			Filter = string.Empty;
		}
	}

	/// <summary>Enter in the filter. Only ever a local copy: a key press must not start a download.</summary>
	public void AddFirst()
	{
		PickerRowVm? first = Rows.FirstOrDefault(r => r.Add.IsVisible && r.Add.IsEnabled);
		first?.Add.Invoke();
	}

	void AddLocal(string pluginId)
	{
		VaultState? vault = shell.CurrentVault;
		PluginRow? plugin = Data.Snapshot?.Plugins.FirstOrDefault(p => p.Id == pluginId);
		if (vault is not null && plugin is not null && Plans.AddTo(plugin, vault.Path, CopySettings) is { } item)
		{
			_ = shell.Changes.Run(item);
		}

		RefocusFilter?.Invoke();
	}

	public void Rebuild()
	{
		VaultState? vault = shell.CurrentVault;
		WorkspaceSnapshot? snapshot = Data.Snapshot;
		if (vault is null || snapshot is null)
		{
			Rows.Clear();
			CatalogRows.Clear();
			return;
		}

		Title = Texts.Format(K.Picker_Title, vault.DisplayName).ToUpper(Texts.Culture);

		List<PluginRow> missing = snapshot.Plugins
			.Where(p => p.In(vault.Id) is null or { HasFolder: false } && p.NewestVersion is not null && p.SourceVaultPath is not null)
			.ToList();
		List<PluginRow> rows = PluginFilter.Apply(missing, Filter, p => p.Name).ToList();
		CollectionSync.Sync(Rows, rows, r => r.Id, p => p.Id, p => new PickerRowVm(p.Id), (_, _) => { });
		foreach (PluginRow plugin in rows)
		{
			UpdateLocal(Rows.First(r => r.Id == plugin.Id), plugin, vault, snapshot);
		}

		UpdateCatalog(vault, snapshot);

		IsEmpty = rows.Count == 0 && CatalogRows.Count == 0;
		Empty = missing.Count == 0 && Filter.Trim().Length == 0
			? Texts.Get(K.Picker_AllHere)
			: Texts.Get(Data.Catalog.Count > 0 ? K.Picker_NoMatchCatalog : K.Picker_NoMatch);
		Note = Texts.Get(Data.Catalog.Count > 0 ? K.Picker_NoteCatalog : K.Picker_Note);
	}

	void UpdateLocal(PickerRowVm row, PluginRow plugin, VaultState vault, WorkspaceSnapshot snapshot)
	{
		string source = PluginFacts.VaultName(snapshot, plugin.SourceVaultPath!);
		SettingsFate fate = PluginFacts.FateOf(new CopyPlugin(vault.Path, plugin.Id, plugin.Name, null, plugin.NewestVersion!, plugin.SourceVaultPath!, CopySettings, null), null);
		row.Name = plugin.Name;
		row.Sub = Texts.Format(K.Picker_RowSub, source, EnumTexts.Of(fate));
		row.Version = plugin.NewestVersion!;
		row.IsWaiting = shell.Changes.Queue.IsPluginPending(vault.Path, plugin.Id);
		if (row.IsWaiting)
		{
			row.Add.Hide();
		}
		else
		{
			string id = plugin.Id;
			row.Add.Set(Texts.Get(K.Action_Add), () => AddLocal(id));
		}
	}

	void UpdateCatalog(VaultState vault, WorkspaceSnapshot snapshot)
	{
		HashSet<string> local = snapshot.Plugins.Where(p => p.NewestVersion is not null).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
		List<CachedCatalogEntry> entries = Data.Catalog.Where(e => !local.Contains(e.Id)).ToList();
		List<CachedCatalogEntry> shown = PluginFilter.Apply(entries, Filter, e => e.Name).ToList();
		HasCatalogMore = shown.Count > CatalogRowLimit;
		CatalogMore = Texts.Format(K.Picker_CatalogMore, shown.Count - CatalogRowLimit);
		shown = shown.Take(CatalogRowLimit).ToList();
		HasCatalog = shown.Count > 0;

		CollectionSync.Sync(CatalogRows, shown, r => r.Id, e => e.Id, e => new PickerRowVm(e.Id), (_, _) => { });
		foreach (CachedCatalogEntry entry in shown)
		{
			UpdateCatalogRow(CatalogRows.First(r => r.Id == entry.Id), entry, vault);
		}
	}

	void UpdateCatalogRow(PickerRowVm row, CachedCatalogEntry entry, VaultState vault)
	{
		DownloadOffer offer = shell.Online.OfferFor(entry, null);
		row.Name = entry.Name;
		row.Sub = Texts.Format(K.Picker_CatalogSub, entry.Author);
		row.Version = offer.Version ?? string.Empty;
		row.IsWaiting = shell.Changes.Queue.IsPluginPending(vault.Path, entry.Id);
		if (row.IsWaiting)
		{
			row.Add.Hide();
			return;
		}

		switch (offer.Stage)
		{
			case OfferStage.Asking:
				row.Add.Set(Texts.Get(K.Offer_Asking), () => { }, enabled: false);
				break;
			case OfferStage.Sized:
				row.Sub = Texts.Format(K.Picker_CatalogSized, entry.Author, Formats.Size(offer.Size));
				row.Add.Set(Texts.Format(K.Offer_AddSized, Formats.Size(offer.Size)), () =>
				{
					_ = shell.Changes.Run(Plans.Download(entry, offer.Version!, offer.Size, vault.Path, null));
					RefocusFilter?.Invoke();
				});
				break;
			case OfferStage.Failed:
				row.Sub = Texts.Get(K.Offer_Failed);
				row.Add.Set(Texts.Get(K.Offer_TryAgain), () => _ = shell.Online.AskSizeAsync(entry, null));
				break;
			default:
				row.Add.Set(Texts.Get(K.Offer_CheckSize), () => _ = shell.Online.AskSizeAsync(entry, null));
				break;
		}
	}
}
