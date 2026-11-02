using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VaultManager.App.Controls;
using VaultManager.App.Localization;
using VaultManager.App.Logic;
using VaultManager.Core.Changes;
using VaultManager.Core.Obsidian;
using VaultManager.Core.Store;

namespace VaultManager.App.ViewModels;

internal sealed partial class AttentionRowVm(string key, AttentionKind kind, AttentionViewModel owner) : ObservableObject
{
	public string Key { get; } = key;
	public AttentionKind Kind { get; } = kind;

	[ObservableProperty] StateKind _glyph;
	[ObservableProperty] IReadOnlyList<Seg> _line = [];
	[ObservableProperty] IReadOnlyList<Seg> _how = [];
	[ObservableProperty] bool _isWaiting;
	[ObservableProperty] bool _isExpanded;

	public ActionSlot Primary { get; } = new();
	public ActionSlot Secondary { get; } = new();
	public ActionSlot Keep { get; } = new();

	[RelayCommand]
	public void Toggle() => owner.Toggle(this);
}

/// <summary>The Plugins home: one sorted list of one-line rows, and the details of at most one of them.</summary>
internal sealed partial class AttentionViewModel(ShellViewModel shell) : ObservableObject
{
	const string Gap = "   ";

	public ObservableCollection<AttentionRowVm> Rows { get; } = [];
	public ObservableCollection<AttentionRowVm> IgnoredRows { get; } = [];

	[ObservableProperty] bool _hasIgnored;
	[ObservableProperty] string _ignoredLink = string.Empty;
	[ObservableProperty] bool _showIgnored;
	[ObservableProperty] bool _hasFooter;

	IReadOnlyList<AttentionItem> _items = [];
	string? _expanded;

	WorkspaceData Data => shell.Data;

	public IReadOnlyList<AttentionItem> Items => _items;

	/// <summary>What Update all would run right now.</summary>
	public IReadOnlyList<CopyPlugin> UpdateAllPlan { get; private set; } = [];

	partial void OnShowIgnoredChanged(bool value) => OnPropertyChanged(nameof(IgnoredVisible));

	public bool IgnoredVisible => ShowIgnored && HasIgnored;

	[RelayCommand]
	void ToggleIgnored() => ShowIgnored = !ShowIgnored;

	[RelayCommand]
	void ShowAllPlugins() => shell.ShowAll();

	public void Toggle(AttentionRowVm row)
	{
		_expanded = _expanded == row.Key ? null : row.Key;
		foreach (AttentionRowVm other in Rows)
		{
			other.IsExpanded = other.Key == _expanded;
		}
	}

	public void Rebuild()
	{
		WorkspaceSnapshot? snapshot = Data.Snapshot;
		_items = snapshot is { RegistryStatus: VaultManager.Core.Foundation.ReadStatus.Ok }
			? AttentionBuilder.Build(new AttentionInput(snapshot, Data.Ignored, Data.Audits, Data.Online))
			: [];

		List<AttentionItem> active = _items.Where(i => !i.Ignored).ToList();
		List<AttentionItem> ignored = _items.Where(i => i.Ignored).ToList();
		UpdateAllPlan = snapshot is null ? [] : AttentionBuilder.UpdateAll(_items, snapshot, c => shell.Changes.Queue.IsPluginPending(c.TargetVault, c.PluginId));

		SyncRows(Rows, active);
		SyncRows(IgnoredRows, ignored);

		HasIgnored = ignored.Count > 0;
		OnPropertyChanged(nameof(IgnoredVisible));
		IgnoredLink = Texts.Count(K.Plural.Ignored, ignored.Count);
		HasFooter = shell.HasWorkspace;
	}

	void SyncRows(ObservableCollection<AttentionRowVm> rows, IReadOnlyList<AttentionItem> items)
	{
		CollectionSync.Sync(rows, items, r => r.Key, i => i.Key, i => new AttentionRowVm(i.Key, i.Kind, this), (_, _) => { });
		foreach (AttentionItem item in items)
		{
			Update(rows.First(r => r.Key == item.Key), item);
		}
	}

	public string HeadLine()
	{
		if (!shell.HasWorkspace)
		{
			return Texts.Get(K.Head_Plugins);
		}

		List<AttentionItem> active = _items.Where(i => !i.Ignored).ToList();
		int older = active.Count(i => i.Kind == AttentionKind.Older);
		int rest = active.Count - older;
		string first = older > 0 ? Texts.Count(K.Plural.PluginsBehind, older) : rest > 0 ? Texts.Get(K.Head_NoneBehind) : Texts.Get(K.Head_NothingNeeded);
		return rest > 0 ? first + ", " + Texts.Count(K.Plural.OtherItems, rest) : first;
	}

	void SetIgnored(string key, bool ignored)
	{
		shell.Host.Database.SetIgnored(key, ignored);
		Data.Ignored = shell.Host.Database.Ignored();
		shell.Rebuild();
	}

	void Update(AttentionRowVm row, AttentionItem item)
	{
		row.Primary.Hide();
		row.Secondary.Hide();
		row.IsWaiting = false;
		row.IsExpanded = !item.Ignored && _expanded == item.Key;

		switch (item.Kind)
		{
			case AttentionKind.Older:
				DescribeOlder(row, item);
				break;
			case AttentionKind.Differs:
				DescribeDiffers(row, item);
				break;
			case AttentionKind.NewerOnline:
				DescribeNewer(row, item);
				break;
			case AttentionKind.PluginsOff:
			case AttentionKind.PluginsNeverEnabled:
				DescribeOff(row, item);
				break;
			case AttentionKind.ProfileGap:
				DescribeGap(row, item);
				break;
			case AttentionKind.ListUnreadable:
				row.Glyph = StateKind.Blocked;
				row.Line = [Seg.Strong(item.Vault!.DisplayName), new Seg(Gap), new Seg(Texts.Get(K.Row_ListUnreadable))];
				row.How = Segments.Of(K.Attn_ListUnreadableHow);
				break;
			case AttentionKind.ScanError:
				row.Glyph = StateKind.Blocked;
				row.Line = [Seg.Strong(item.Vault!.DisplayName), new Seg(Gap), new Seg(Texts.Get(K.Row_ScanError))];
				row.How = Segments.Of(K.Attn_ScanErrorHow, Seg.Value(item.Vault.ScanError));
				break;
		}

		bool softWord = item.Kind is AttentionKind.PluginsOff or AttentionKind.PluginsNeverEnabled or AttentionKind.ListUnreadable or AttentionKind.ScanError;
		if (item.Ignored)
		{
			row.Primary.Set(Texts.Get(K.Action_Restore), () => SetIgnored(item.Key, false));
			row.IsWaiting = false;
			row.Keep.Hide();
		}
		else
		{
			row.Keep.Set(Texts.Get(softWord ? K.Action_Ignore : K.Action_KeepAsIs), () => SetIgnored(item.Key, true));
		}
	}

	void DescribeOlder(AttentionRowVm row, AttentionItem item)
	{
		PluginRow plugin = item.Plugin!;
		WorkspaceSnapshot snapshot = Data.Snapshot!;
		IReadOnlyList<PluginInstall> behind = PluginFacts.Behind(plugin);
		row.Glyph = StateKind.Older;
		string versions = behind.Count == 1 ? $"{behind[0].Version} → {plugin.NewestVersion}" : $"→ {plugin.NewestVersion}";
		string where = Texts.Format(K.Row_In, string.Join(", ", behind.Select(b => PluginFacts.VaultName(snapshot, b.VaultPath))));
		row.Line = [Seg.Strong(plugin.Name), new Seg(Gap), Seg.Value(versions), new Seg(Gap), new Seg(where)];

		string source = PluginFacts.VaultName(snapshot, plugin.SourceVaultPath ?? string.Empty);
		row.How = Segments.Of(behind.Any(b => b.HasData) ? K.Attn_OlderHowKept : K.Attn_OlderHow, Seg.Value(plugin.NewestVersion), source);

		List<CopyPlugin> todo = Plans.UpdateBehind(plugin, snapshot).Where(c => !shell.Changes.Queue.IsPluginPending(c.TargetVault, c.PluginId)).ToList();
		if (todo.Count > 0)
		{
			row.Primary.Set(todo.Count == 1 ? Texts.Get(K.Action_Update) : Texts.Format(K.Action_UpdateN, todo.Count), () => _ = shell.Changes.Run(Plans.UpdateBehind(plugin, Data.Snapshot!)));
		}
		else
		{
			row.IsWaiting = behind.Any(b => shell.Changes.Queue.IsPluginPending(b.VaultPath, plugin.Id));
		}
	}

	void DescribeDiffers(AttentionRowVm row, AttentionItem item)
	{
		PluginRow plugin = item.Plugin!;
		WorkspaceSnapshot snapshot = Data.Snapshot!;
		List<IGrouping<string?, PluginInstall>> groups = PluginFacts.Present(plugin).GroupBy(i => i.Version).Where(g => g.Select(i => i.MainHash).Distinct().Count() > 1).ToList();
		List<PluginInstall> same = groups.SelectMany(g => g).ToList();
		row.Glyph = StateKind.Warning;
		row.Line = [Seg.Strong(plugin.Name), new Seg(Gap), new Seg(Texts.Get(K.Row_Differs))];
		row.How = Segments.Of(K.Attn_Differs, plugin.Name, Seg.Value(groups.FirstOrDefault()?.Key), Texts.Join(same.Select(i => PluginFacts.VaultName(snapshot, i.VaultPath)).ToList()));
		var how = new List<Seg>(row.How) { new(". "), new(Texts.Get(K.Attn_DiffersHow)) };
		row.How = how;
	}

	void DescribeNewer(AttentionRowVm row, AttentionItem item)
	{
		PluginRow plugin = item.Plugin!;
		string version = item.OnlineVersion!;
		row.Glyph = StateKind.Online;
		row.Line = [Seg.Strong(plugin.Name), new Seg(Gap), Seg.Value($"{plugin.NewestVersion} → {version}"), new Seg(Gap), new Seg(Texts.Get(K.Row_Online))];
		row.How = Segments.Of(K.Attn_NewerHow, Seg.Value(plugin.NewestVersion));
		row.Secondary.Set(Texts.Get(K.Action_CatalogPage), () => shell.OpenLink(Links.CatalogPage(plugin.Id)));
		if (Data.CatalogEntry(plugin.Id) is not { } entry)
		{
			return;
		}

		List<PluginInstall> targets = PluginFacts.Present(plugin).Where(i => PluginVersion.Compare(i.Version, version) < 0).ToList();
		if (targets.Count > 0 && targets.All(t => shell.Changes.Queue.IsPluginPending(t.VaultPath, plugin.Id)))
		{
			row.IsWaiting = true;
			return;
		}

		DownloadOffer offer = shell.Online.OfferFor(entry, version);
		switch (offer.Stage)
		{
			case OfferStage.Asking:
				row.Primary.Set(Texts.Get(K.Offer_Asking), () => { }, enabled: false);
				break;
			case OfferStage.Sized:
				row.Primary.Set(Texts.Format(K.Offer_Download, Formats.Size(offer.Size)), () => _ = shell.Changes.Run(
					targets.Where(t => !shell.Changes.Queue.IsPluginPending(t.VaultPath, plugin.Id))
						.Select(t => Plans.Download(entry, version, offer.Size, t.VaultPath, t))
						.ToList()));
				break;
			case OfferStage.Failed:
				row.Primary.Set(Texts.Get(K.Offer_TryAgain), () => _ = shell.Online.AskSizeAsync(entry, version));
				row.How = Segments.Of(K.Offer_Failed);
				break;
			default:
				row.Primary.Set(Texts.Get(K.Offer_CheckSize), () => _ = shell.Online.AskSizeAsync(entry, version));
				break;
		}
	}

	void DescribeOff(AttentionRowVm row, AttentionItem item)
	{
		VaultState vault = item.Vault!;
		row.Glyph = StateKind.Blocked;
		row.Line = [Seg.Strong(vault.DisplayName), new Seg(Gap), new Seg(Texts.Get(K.Row_PluginsOff))];
		row.How = Segments.Of(K.Attn_OffHow);
		row.Secondary.Set(Texts.Get(K.Action_OpenInObsidian), () => shell.OpenVaultInObsidian(vault));
	}

	void DescribeGap(AttentionRowVm row, AttentionItem item)
	{
		VaultState vault = item.Vault!;
		BoundAudit gap = item.Gap!;
		ProfileFill fill = Plans.Fill(gap.Profile, gap.Audit, vault, Data.Snapshot!);

		row.Glyph = StateKind.Older;
		row.Line = [Seg.Strong(vault.DisplayName), new Seg(Gap), new Seg(ProfilesViewModel.MissingText(gap.Audit))];
		row.How = fill.Unavailable.Count > 0
			? Segments.Of(K.Attn_GapHowUnavailable, gap.Profile.Name, string.Join(", ", fill.Unavailable))
			: Segments.Of(K.Attn_GapHow, gap.Profile.Name);

		List<ChangeItem> todo = fill.Items.Where(i => !IsPending(i)).ToList();
		if (todo.Count > 0)
		{
			row.Primary.Set(Texts.Get(K.Action_Add), () => _ = shell.Changes.Run(todo));
		}
		else
		{
			row.IsWaiting = fill.Items.Count > 0;
		}
	}

	bool IsPending(ChangeItem item) => item switch
	{
		CopyPlugin c => shell.Changes.Queue.IsPluginPending(c.TargetVault, c.PluginId),
		SetPluginEnabled s => shell.Changes.Queue.IsTogglePending(s.TargetVault, s.PluginId),
		AddProfileParts p => shell.Changes.Queue.ArePartsPending(p.TargetVault),
		_ => false,
	};
}
