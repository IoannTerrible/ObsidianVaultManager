using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VaultManager.App.Controls;
using VaultManager.App.Localization;
using VaultManager.App.Logic;
using VaultManager.Core.Changes;
using VaultManager.Core.Obsidian;
using VaultManager.Core.Profiles;

namespace VaultManager.App.ViewModels;

internal sealed partial class VaultBannerVm(string key) : ObservableObject
{
	public string Key { get; } = key;

	[ObservableProperty] StateKind _glyph;
	[ObservableProperty] string _title = string.Empty;
	[ObservableProperty] string _how = string.Empty;
}

internal sealed partial class VaultPluginRowVm(string id, VaultPageViewModel owner) : ObservableObject
{
	public string Id { get; } = id;

	[ObservableProperty] StateKind _glyph;
	[ObservableProperty] string _name = string.Empty;
	[ObservableProperty] string _version = string.Empty;
	[ObservableProperty] string _state = string.Empty;
	[ObservableProperty] string _how = string.Empty;
	[ObservableProperty] bool _hasHow;
	[ObservableProperty] bool _isWaiting;
	[ObservableProperty] bool _isExpanded;
	[ObservableProperty] bool _notInCatalog;
	[ObservableProperty] string _online = string.Empty;
	[ObservableProperty] bool _hasOnline;

	public ActionSlot Update { get; } = new();
	public ActionSlot Toggle { get; } = new();
	public ActionSlot Catalog { get; } = new();

	[RelayCommand]
	public void Expand() => owner.Toggle(this);
}

/// <summary>One vault: what is behind first, then every plugin on one line. Details and Turn on/off open on a click.</summary>
internal sealed partial class VaultPageViewModel(ShellViewModel shell) : ObservableObject
{
	string? _expanded;

	public ObservableCollection<VaultBannerVm> Banners { get; } = [];
	public ObservableCollection<VaultPluginRowVm> OlderRows { get; } = [];
	public ObservableCollection<VaultPluginRowVm> PluginRows { get; } = [];

	[ObservableProperty] string _profileLine = string.Empty;
	[ObservableProperty] bool _hasProfileLine;
	[ObservableProperty] bool _profileComplete;
	[ObservableProperty] bool _hasPlugins;
	[ObservableProperty] string _empty = string.Empty;
	[ObservableProperty] bool _isEmpty;
	[ObservableProperty] bool _canAdd;
	[ObservableProperty] bool _canOpen;

	WorkspaceData Data => shell.Data;

	public void Toggle(VaultPluginRowVm row)
	{
		_expanded = _expanded == row.Id ? null : row.Id;
		foreach (VaultPluginRowVm other in OlderRows.Concat(PluginRows))
		{
			other.IsExpanded = other.Id == _expanded;
		}
	}

	public void Rebuild()
	{
		VaultState? vault = shell.CurrentVault;
		WorkspaceSnapshot? snapshot = Data.Snapshot;
		if (vault is null || snapshot is null)
		{
			Banners.Clear();
			OlderRows.Clear();
			PluginRows.Clear();
			HasPlugins = IsEmpty = HasProfileLine = false;
			return;
		}

		CanOpen = vault.IsOnline;
		CanAdd = !vault.ListUnreadable && vault.ScanError is null;
		UpdateProfileLine(vault);
		UpdateBanners(vault);

		List<PluginRow> installed = snapshot.Plugins.Where(p => p.In(vault.Id) is not null).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
		List<PluginRow> older = installed.Where(p => PluginFacts.IsBehind(p, p.In(vault.Id)!)).ToList();
		SyncRows(OlderRows, older, vault);
		SyncRows(PluginRows, installed.Except(older).ToList(), vault);
		HasPlugins = installed.Count > 0;

		IsEmpty = installed.Count == 0;
		Empty = !vault.IsOnline
			? Texts.Get(K.VaultPage_EmptyOffline)
			: vault.ListUnreadable || vault.ScanError is not null
				? Texts.Get(K.VaultPage_EmptyBlocked)
				: Texts.Get(K.VaultPage_Empty);
	}

	void UpdateProfileLine(VaultState vault)
	{
		HasProfileLine = Data.Audits.TryGetValue(vault.Id, out BoundAudit? bound);
		if (bound is not null)
		{
			ProfileComplete = bound.Audit.IsComplete;
			ProfileLine = Texts.Format(K.VaultPage_ProfileLine, bound.Profile.Name, bound.Audit.IsComplete ? Texts.Get(K.Audit_Matches) : ProfilesViewModel.MissingText(bound.Audit));
		}
	}

	void UpdateBanners(VaultState vault)
	{
		var wanted = new List<(string Key, StateKind Glyph, string Title, string How)>();
		if (!vault.IsOnline)
		{
			wanted.Add(("offline", StateKind.Offline, Texts.Get(K.Banner_Offline), Texts.Get(K.Banner_OfflineHow)));
		}
		else
		{
			if (vault.IsOpen && Data.ObsidianRunning)
			{
				wanted.Add(("open", StateKind.Waiting, Texts.Get(K.Banner_Open), Texts.Get(K.Banner_OpenHow)));
			}

			if (vault.ListUnreadable)
			{
				wanted.Add(("list", StateKind.Blocked, Texts.Get(K.Banner_ListUnreadable), Texts.Get(K.Banner_ListUnreadableHow)));
			}

			if (vault.ScanError is not null)
			{
				wanted.Add(("scan", StateKind.Blocked, Texts.Get(K.Banner_ScanError), Texts.Format(K.Banner_ScanErrorHow, vault.ScanError)));
			}

			switch (vault.CommunityPlugins)
			{
				case CommunityPluginsState.Off:
					wanted.Add(("plugins", StateKind.Blocked, EnumTexts.Of(vault.CommunityPlugins), Texts.Get(K.Banner_OffHow)));
					break;
				case CommunityPluginsState.NeverEnabled:
					wanted.Add(("plugins", StateKind.Blocked, EnumTexts.Of(vault.CommunityPlugins), Texts.Get(K.Banner_OffHow)));
					break;
				case CommunityPluginsState.Unknown:
					wanted.Add(("plugins", StateKind.None, EnumTexts.Of(vault.CommunityPlugins), Texts.Get(K.Banner_UnknownHow)));
					break;
			}
		}

		CollectionSync.Sync(Banners, wanted, b => b.Key, w => w.Key, w => new VaultBannerVm(w.Key), (_, _) => { });
		foreach ((string key, StateKind glyph, string title, string how) in wanted)
		{
			VaultBannerVm banner = Banners.First(b => b.Key == key);
			banner.Glyph = glyph;
			banner.Title = title;
			banner.How = how;
		}
	}

	void SyncRows(ObservableCollection<VaultPluginRowVm> rows, IReadOnlyList<PluginRow> plugins, VaultState vault)
	{
		CollectionSync.Sync(rows, plugins, r => r.Id, p => p.Id, p => new VaultPluginRowVm(p.Id, this), (_, _) => { });
		foreach (PluginRow plugin in plugins)
		{
			UpdateRow(rows.First(r => r.Id == plugin.Id), plugin, vault);
		}
	}

	void UpdateRow(VaultPluginRowVm row, PluginRow plugin, VaultState vault)
	{
		WorkspaceSnapshot snapshot = Data.Snapshot!;
		PluginInstall install = plugin.In(vault.Id)!;
		bool behind = PluginFacts.IsBehind(plugin, install);
		bool copyPending = shell.Changes.Queue.IsPluginPending(vault.Path, plugin.Id);
		bool togglePending = shell.Changes.Queue.IsTogglePending(vault.Path, plugin.Id);
		bool blocked = vault.ListUnreadable || vault.ScanError is not null;

		row.Name = plugin.Name;
		row.IsWaiting = copyPending || togglePending;
		row.IsExpanded = _expanded == plugin.Id;
		if (!install.HasFolder)
		{
			row.Glyph = StateKind.Blocked;
			row.Version = string.Empty;
			row.State = Texts.Get(K.VaultPage_FilesMissing);
		}
		else
		{
			row.Glyph = behind ? StateKind.Older : install.Listed ? StateKind.Match : StateKind.Off;
			row.Version = behind ? $"{install.Version} → {plugin.NewestVersion}" : install.Version ?? string.Empty;
			row.State = Texts.Get(install.Listed ? K.VaultPage_On : K.VaultPage_Off);
		}

		if (behind)
		{
			string source = PluginFacts.VaultName(snapshot, plugin.SourceVaultPath ?? string.Empty);
			row.How = Texts.Format(install.HasData ? K.VaultPage_FromKept : K.VaultPage_From, source);
			row.HasHow = true;
		}
		else
		{
			row.How = string.Empty;
			row.HasHow = false;
		}

		CopyPlugin? repair = behind || !install.HasFolder ? (behind ? Plans.UpdateOne(plugin, install) : Plans.AddTo(plugin, vault.Path, false)) : null;
		if (repair is not null && !copyPending && !blocked)
		{
			row.Update.Set(Texts.Get(behind ? K.Action_Update : K.Action_Restore), () => _ = shell.Changes.Run(repair));
		}
		else
		{
			row.Update.Hide();
		}

		if (install.HasFolder && !togglePending)
		{
			bool enable = !install.Listed;
			row.Toggle.Set(Texts.Get(enable ? K.Action_TurnOn : K.Action_TurnOff),
				() => _ = shell.Changes.Run(new SetPluginEnabled(vault.Path, plugin.Id, plugin.Name, enable)), enabled: !blocked);
		}
		else
		{
			row.Toggle.Hide();
		}

		row.Catalog.Hide();
		row.NotInCatalog = false;
		row.HasOnline = false;
		if (Data.Catalog.Count > 0)
		{
			if (Data.CatalogEntry(plugin.Id) is not null)
			{
				row.Catalog.Set(Texts.Get(K.Action_CatalogPage), () => shell.OpenLink(Links.CatalogPage(plugin.Id)));
			}
			else
			{
				row.NotInCatalog = true;
			}
		}

		if (Data.OnlineVersionOf(plugin.Id) is { } online && PluginVersion.Compare(online, install.Version) > 0)
		{
			row.HasOnline = true;
			row.Online = Texts.Format(K.Matrix_OnlineVersion, online);
		}
	}
}
