using VaultManager.Core.Foundation;

namespace VaultManager.Core.Obsidian;

public sealed record VaultState(
	string Id,
	string Path,
	string DisplayName,
	bool IsOnline,
	bool IsOpen,
	CommunityPluginsState CommunityPlugins,
	bool ListUnreadable,
	string? ScanError);

public sealed record PluginInstall(
	string VaultId,
	string VaultPath,
	string? Version,
	bool Listed,
	bool HasFolder,
	bool HasData,
	string? MainHash,
	long SizeBytes);

public sealed record PluginRow(
	string Id,
	string Name,
	string? Description,
	string? Author,
	string? NewestVersion,
	string? SourceVaultPath,
	bool Drifted,
	bool SameVersionDifferentFiles,
	IReadOnlyList<PluginInstall> Installs)
{
	public PluginInstall? In(string vaultId) => Installs.FirstOrDefault(i => i.VaultId == vaultId);
	public long SizeBytes => Installs.Where(i => i.HasFolder).Select(i => i.SizeBytes).DefaultIfEmpty(0).Max();
}

public sealed record WorkspaceSnapshot(
	ReadStatus RegistryStatus,
	IReadOnlyList<VaultState> Vaults,
	IReadOnlyList<PluginRow> Plugins,
	DateTimeOffset ScannedAt);

/// <summary>Reads everything from disk, one vault after another: external drives here drop under parallel load.</summary>
public sealed class WorkspaceScanner(VaultRegistry registry, CommunityPluginsReader pluginsReader, IObsidianProcess process)
{
	public WorkspaceSnapshot Scan(CancellationToken cancellationToken = default)
	{
		ReadResult<IReadOnlyList<RegistryEntry>> entries = registry.Read();
		if (entries.Status != ReadStatus.Ok)
		{
			return new WorkspaceSnapshot(entries.Status, [], [], DateTimeOffset.Now);
		}

		bool running = process.IsRunning;
		IReadOnlyDictionary<string, bool>? states = pluginsReader.ReadAll();
		IReadOnlyDictionary<string, string> names = DisplayNames(entries.Value!);

		var vaults = new List<VaultState>();
		var byPlugin = new Dictionary<string, List<PluginInstall>>(StringComparer.Ordinal);
		var manifests = new Dictionary<string, PluginManifest>(StringComparer.Ordinal);

		foreach (RegistryEntry entry in entries.Value!)
		{
			cancellationToken.ThrowIfCancellationRequested();
			bool online = IsOnline(entry.Path);
			bool unreadable = false;
			string? error = null;
			if (online)
			{
				try
				{
					unreadable = ScanVault(entry, byPlugin, manifests);
				}
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
				{
					error = ex.GetType().Name;
				}
			}

			vaults.Add(new VaultState(entry.Id, entry.Path, names[entry.Id], online, running && entry.Open,
				CommunityPluginsReader.StateOf(states, entry.Id), unreadable, error));
		}

		var rows = new List<PluginRow>();
		foreach ((string id, List<PluginInstall> installs) in byPlugin.OrderBy(p => p.Key, StringComparer.Ordinal))
		{
			manifests.TryGetValue(id, out PluginManifest? manifest);
			rows.Add(BuildRow(id, manifest, installs));
		}

		return new WorkspaceSnapshot(ReadStatus.Ok, vaults, rows.OrderByDescending(r => r.Drifted).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList(), DateTimeOffset.Now);
	}

	internal static PluginRow BuildRow(string id, PluginManifest? manifest, IReadOnlyList<PluginInstall> installs)
	{
		List<PluginInstall> present = installs.Where(i => i.HasFolder && i.Version is not null).ToList();
		PluginInstall? newest = null;
		foreach (PluginInstall install in present)
		{
			if (newest is null || PluginVersion.Compare(install.Version, newest.Version) > 0)
			{
				newest = install;
			}
		}

		bool drifted = present.Select(i => i.Version).Distinct(StringComparer.Ordinal).Count() > 1;
		bool sameVersionDiffers = present
			.Where(i => i.MainHash is not null)
			.GroupBy(i => i.Version, StringComparer.Ordinal)
			.Any(g => g.Select(i => i.MainHash).Distinct(StringComparer.Ordinal).Count() > 1);

		return new PluginRow(id, manifest?.Name ?? id, manifest?.Description, manifest?.Author, newest?.Version, newest?.VaultPath, drifted, sameVersionDiffers, installs);
	}

	static bool ScanVault(RegistryEntry entry, Dictionary<string, List<PluginInstall>> byPlugin, Dictionary<string, PluginManifest> manifests)
	{
		ReadResult<IReadOnlyList<string>> listed = CommunityPluginList.Read(entry.Path);
		var listedIds = new HashSet<string>(listed.Value ?? [], StringComparer.Ordinal);
		var seen = new HashSet<string>(StringComparer.Ordinal);

		string root = VaultLayout.PluginsDir(entry.Path);
		if (Directory.Exists(root))
		{
			foreach (string dir in Directory.EnumerateDirectories(root))
			{
				string id = Path.GetFileName(dir);
				PluginManifest? manifest = PluginManifest.TryRead(dir);
				if (manifest is not null && !manifests.ContainsKey(id))
				{
					manifests[id] = manifest;
				}

				string main = Path.Combine(dir, VaultLayout.MainFile);
				Add(byPlugin, id, new PluginInstall(entry.Id, entry.Path, manifest?.Version, listedIds.Contains(id), true,
					File.Exists(Path.Combine(dir, VaultLayout.DataFile)), File.Exists(main) ? SharedFile.Hash(main) : null, Folder.Size(dir)));
				seen.Add(id);
			}
		}

		foreach (string id in listedIds.Where(i => !seen.Contains(i)))
		{
			Add(byPlugin, id, new PluginInstall(entry.Id, entry.Path, null, true, false, false, null, 0));
		}

		return listed.Status == ReadStatus.Unreadable;
	}

	static void Add(Dictionary<string, List<PluginInstall>> byPlugin, string id, PluginInstall install)
	{
		if (!byPlugin.TryGetValue(id, out List<PluginInstall>? list))
		{
			list = [];
			byPlugin[id] = list;
		}

		list.Add(install);
	}

	static bool IsOnline(string path)
	{
		try
		{
			return Directory.Exists(path);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return false;
		}
	}

	/// <summary>Two folders may share a name, as D:\Obsidian\PC and D:\Obsidian\CopyPc\PC: the parent then tells them apart.</summary>
	internal static IReadOnlyDictionary<string, string> DisplayNames(IReadOnlyList<RegistryEntry> entries)
	{
		string Leaf(string path) => Path.GetFileName(path.TrimEnd('\\', '/'));
		string Parent(string path) => Leaf(Path.GetDirectoryName(path.TrimEnd('\\', '/')) ?? string.Empty);

		Dictionary<string, int> counts = entries
			.GroupBy(e => Leaf(e.Path), StringComparer.OrdinalIgnoreCase)
			.ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

		return entries.ToDictionary(
			e => e.Id,
			e => counts[Leaf(e.Path)] > 1 && Parent(e.Path).Length > 0 ? $"{Leaf(e.Path)} ({Parent(e.Path)})" : Leaf(e.Path));
	}
}
