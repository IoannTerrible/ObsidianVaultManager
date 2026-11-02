using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VaultManager.App.Controls;
using VaultManager.App.Localization;
using VaultManager.App.Logic;
using VaultManager.Core.Obsidian;

namespace VaultManager.App.ViewModels;

internal sealed partial class MatrixColumnVm(string id) : ObservableObject
{
	public string Id { get; } = id;

	[ObservableProperty] string _name = string.Empty;
	[ObservableProperty] string _tip = string.Empty;
}

internal sealed partial class MatrixCellVm(string vaultId) : ObservableObject
{
	public string VaultId { get; } = vaultId;

	[ObservableProperty] StateKind _glyph;
	[ObservableProperty] bool _hasGlyph;
	[ObservableProperty] string _version = string.Empty;
	[ObservableProperty] string _tip = string.Empty;
	[ObservableProperty] bool _canAdd;
	[ObservableProperty] bool _isOlder;

	public ActionSlot Add { get; } = new();
}

internal sealed partial class MatrixRowVm(string id, MatrixViewModel owner) : ObservableObject
{
	public string Id { get; } = id;

	[ObservableProperty] string _name = string.Empty;
	[ObservableProperty] string _newest = string.Empty;
	[ObservableProperty] bool _isSelected;

	public ObservableCollection<MatrixCellVm> Cells { get; } = [];

	[RelayCommand]
	public void Select() => owner.Select(Id);
}

/// <summary>Every plugin against every vault. Vault columns come from the scan, so the count is never fixed.</summary>
internal sealed partial class MatrixViewModel(ShellViewModel shell) : ObservableObject
{
	public ObservableCollection<MatrixColumnVm> Columns { get; } = [];
	public ObservableCollection<MatrixRowVm> Rows { get; } = [];

	public ActionSlot DetailAction { get; } = new();
	public ActionSlot CatalogLink { get; } = new();

	[ObservableProperty] string _filter = string.Empty;
	[ObservableProperty] string _detailTitle = string.Empty;
	[ObservableProperty] string _detailSize = string.Empty;
	[ObservableProperty] string _detailText = string.Empty;
	[ObservableProperty] bool _hasDetail;
	[ObservableProperty] bool _detailWaiting;
	[ObservableProperty] bool _notInCatalog;
	[ObservableProperty] string _onlineNote = string.Empty;
	[ObservableProperty] bool _hasOnlineNote;

	string? _selected;

	WorkspaceData Data => shell.Data;

	partial void OnFilterChanged(string value) => Rebuild();

	public void Select(string id)
	{
		_selected = id;
		Rebuild();
	}

	public void Rebuild()
	{
		WorkspaceSnapshot? snapshot = Data.Snapshot;
		IReadOnlyList<VaultState> vaults = snapshot?.Vaults ?? [];
		CollectionSync.Sync(Columns, vaults, c => c.Id, v => v.Id, v => new MatrixColumnVm(v.Id), (_, _) => { });
		foreach (VaultState vault in vaults)
		{
			MatrixColumnVm column = Columns.First(c => c.Id == vault.Id);
			column.Name = vault.DisplayName;
			column.Tip = vault.IsOnline ? vault.Path : vault.Path + " · " + Texts.Get(K.Vault_Offline);
		}

		List<PluginRow> rows = snapshot is null ? [] : PluginFilter.Apply(snapshot.Plugins, Filter, p => p.Name).ToList();
		CollectionSync.Sync(Rows, rows, r => r.Id, p => p.Id, p => new MatrixRowVm(p.Id, this), (_, _) => { });
		foreach (PluginRow plugin in rows)
		{
			UpdateRow(Rows.First(r => r.Id == plugin.Id), plugin, vaults);
		}

		if (_selected is null || rows.All(p => p.Id != _selected))
		{
			_selected = rows.FirstOrDefault()?.Id;
			foreach (MatrixRowVm row in Rows)
			{
				row.IsSelected = row.Id == _selected;
			}
		}

		UpdateDetail(rows.FirstOrDefault(p => p.Id == _selected));
	}

	void UpdateRow(MatrixRowVm row, PluginRow plugin, IReadOnlyList<VaultState> vaults)
	{
		row.Name = plugin.Name;
		row.Newest = plugin.NewestVersion ?? string.Empty;
		row.IsSelected = plugin.Id == _selected;
		CollectionSync.Sync(row.Cells, vaults, c => c.VaultId, v => v.Id, v => new MatrixCellVm(v.Id), (_, _) => { });
		foreach (VaultState vault in vaults)
		{
			UpdateCell(row.Cells.First(c => c.VaultId == vault.Id), plugin, vault);
		}
	}

	void UpdateCell(MatrixCellVm cell, PluginRow plugin, VaultState vault)
	{
		bool queued = shell.Changes.Queue.IsPluginPending(vault.Path, plugin.Id);
		CellKind kind = PluginFacts.Cell(plugin, vault, queued);
		PluginInstall? install = plugin.In(vault.Id);
		cell.IsOlder = kind == CellKind.Older;
		cell.Version = kind is CellKind.Older or CellKind.Off ? install?.Version ?? string.Empty : string.Empty;
		cell.CanAdd = kind == CellKind.Empty;
		cell.HasGlyph = kind != CellKind.Empty;
		cell.Glyph = kind switch
		{
			CellKind.Current => StateKind.Match,
			CellKind.Older => StateKind.Older,
			CellKind.Off => StateKind.Off,
			CellKind.Broken or CellKind.Blocked => StateKind.Blocked,
			CellKind.Waiting => StateKind.Waiting,
			CellKind.Offline => StateKind.Offline,
			_ => StateKind.None,
		};
		cell.Tip = kind == CellKind.Current ? install?.Version ?? string.Empty : EnumTexts.Of(kind);
		if (kind == CellKind.Empty)
		{
			cell.Add.Set(Texts.Get(K.Action_PlusAdd), () => AddTo(plugin.Id, vault.Path));
			cell.Tip = Texts.Format(K.Matrix_AddTip, plugin.Name, vault.DisplayName);
		}
		else
		{
			cell.Add.Hide();
		}
	}

	void AddTo(string pluginId, string vaultPath)
	{
		PluginRow? plugin = Data.Snapshot?.Plugins.FirstOrDefault(p => p.Id == pluginId);
		if (plugin is not null && Plans.AddTo(plugin, vaultPath, copySettings: false) is { } item)
		{
			_ = shell.Changes.Run(item);
		}
	}

	void UpdateDetail(PluginRow? plugin)
	{
		HasDetail = plugin is not null;
		DetailAction.Hide();
		CatalogLink.Hide();
		DetailWaiting = false;
		NotInCatalog = false;
		HasOnlineNote = false;
		if (plugin is null)
		{
			return;
		}

		WorkspaceSnapshot snapshot = Data.Snapshot!;
		DetailTitle = plugin.Name;
		DetailSize = Formats.Size(plugin.SizeBytes);
		IReadOnlyList<PluginInstall> behind = PluginFacts.Behind(plugin);
		IReadOnlyList<PluginInstall> present = PluginFacts.Present(plugin);
		string source = PluginFacts.VaultName(snapshot, plugin.SourceVaultPath ?? string.Empty);
		DetailText = plugin.SameVersionDifferentFiles
			? Texts.Get(K.Matrix_DetailDiffers)
			: behind.Count > 0
				? Texts.Format(K.Matrix_DetailOlder, Texts.Join(behind.Select(b => $"{PluginFacts.VaultName(snapshot, b.VaultPath)} ({b.Version})").ToList()), source)
				: present.Count == 1
					? Texts.Format(K.Matrix_DetailOnly, PluginFacts.VaultName(snapshot, present[0].VaultPath))
					: present.Count == 0
						? Texts.Get(K.Matrix_DetailNoFiles)
						: Texts.Format(K.Matrix_DetailSame, Texts.Count(K.Plural.Vaults, present.Count));

		List<VaultManager.Core.Changes.CopyPlugin> todo = Plans.UpdateBehind(plugin, snapshot).Where(c => !shell.Changes.Queue.IsPluginPending(c.TargetVault, c.PluginId)).ToList();
		if (todo.Count > 0)
		{
			DetailAction.Set(Texts.Format(K.Action_UpdateVaults, Texts.Count(K.Plural.Vaults, todo.Count)), () => _ = shell.Changes.Run(todo));
		}
		else
		{
			DetailWaiting = behind.Any(b => shell.Changes.Queue.IsPluginPending(b.VaultPath, plugin.Id));
		}

		if (Data.Catalog.Count > 0)
		{
			if (Data.CatalogEntry(plugin.Id) is not null)
			{
				CatalogLink.Set(Texts.Get(K.Action_CatalogPage), () => shell.OpenLink(Links.CatalogPage(plugin.Id)));
			}
			else
			{
				NotInCatalog = true;
			}
		}

		if (Data.OnlineVersionOf(plugin.Id) is { } online)
		{
			HasOnlineNote = true;
			OnlineNote = Texts.Format(K.Matrix_OnlineVersion, online);
		}
	}
}
