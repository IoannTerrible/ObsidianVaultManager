using VaultManager.Core.Changes;
using VaultManager.Core.Obsidian;
using VaultManager.Core.Profiles;
using VaultManager.Core.Store;

namespace VaultManager.App.Logic;

/// <summary>The order here is the order on the home page, most serious first: what stops work, what may be unsafe, then what is merely behind.</summary>
public enum AttentionKind { ScanError, ListUnreadable, Differs, Older, NewerOnline, ProfileGap, PluginsOff, PluginsNeverEnabled }

internal sealed record BoundAudit(Profile Profile, ProfileAudit Audit);

internal sealed record AttentionInput(
	WorkspaceSnapshot Snapshot,
	IReadOnlySet<string> Ignored,
	IReadOnlyDictionary<string, BoundAudit> Audits,
	IReadOnlyList<OnlineVersion> Online);

internal sealed record AttentionItem(string Key, AttentionKind Kind, bool Ignored, PluginRow? Plugin = null, VaultState? Vault = null, BoundAudit? Gap = null, string? OnlineVersion = null);

/// <summary>Turns a scan into the list of things a person may want to act on. Pure, so the rules can be tested.</summary>
internal static class AttentionBuilder
{
	public static IReadOnlyList<AttentionItem> Build(AttentionInput input)
	{
		var items = new List<AttentionItem>();

		void Add(string key, AttentionKind kind, PluginRow? plugin = null, VaultState? vault = null, BoundAudit? gap = null, string? online = null) =>
			items.Add(new AttentionItem(key, kind, input.Ignored.Contains(key), plugin, vault, gap, online));

		foreach (PluginRow row in input.Snapshot.Plugins)
		{
			if (row.Drifted)
			{
				Add("older:" + row.Id, AttentionKind.Older, row);
			}

			if (row.SameVersionDifferentFiles)
			{
				Add("differs:" + row.Id, AttentionKind.Differs, row);
			}
		}

		Dictionary<string, PluginRow> byId = input.Snapshot.Plugins.ToDictionary(p => p.Id, StringComparer.Ordinal);
		foreach (OnlineVersion online in input.Online)
		{
			if (byId.TryGetValue(online.PluginId, out PluginRow? row) && row.NewestVersion is not null && PluginVersion.Compare(online.Version, row.NewestVersion) > 0)
			{
				// The version is part of the key: ignoring 1.1.0 must not hide 1.2.0 later.
				Add($"online:{row.Id}:{online.Version}", AttentionKind.NewerOnline, row, online: online.Version);
			}
		}

		foreach (VaultState vault in input.Snapshot.Vaults.Where(v => v.IsOnline))
		{
			if (vault.CommunityPlugins == CommunityPluginsState.Off)
			{
				Add("off:" + vault.Id, AttentionKind.PluginsOff, vault: vault);
			}
			else if (vault.CommunityPlugins == CommunityPluginsState.NeverEnabled)
			{
				Add("never:" + vault.Id, AttentionKind.PluginsNeverEnabled, vault: vault);
			}

			if (input.Audits.TryGetValue(vault.Id, out BoundAudit? bound) && !bound.Audit.IsComplete)
			{
				Add("gap:" + vault.Id, AttentionKind.ProfileGap, vault: vault, gap: bound);
			}

			if (vault.ListUnreadable)
			{
				Add("list:" + vault.Id, AttentionKind.ListUnreadable, vault: vault);
			}

			if (vault.ScanError is not null)
			{
				Add("scan:" + vault.Id, AttentionKind.ScanError, vault: vault);
			}
		}

		return items.OrderBy(i => (int)i.Kind).ToList();
	}

	/// <summary>Update all: older copies that a local newer copy can replace. It only ever copies, so it never reaches the network.</summary>
	public static IReadOnlyList<CopyPlugin> UpdateAll(IReadOnlyList<AttentionItem> items, WorkspaceSnapshot snapshot, Func<CopyPlugin, bool> isPending) =>
		items.Where(i => i is { Kind: AttentionKind.Older, Ignored: false })
			.SelectMany(i => Plans.UpdateBehind(i.Plugin!, snapshot))
			.Where(c => !isPending(c))
			.ToList();
}
