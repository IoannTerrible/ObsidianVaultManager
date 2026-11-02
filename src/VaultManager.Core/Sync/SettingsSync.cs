using VaultManager.Core.Changes;
using VaultManager.Core.Foundation;
using VaultManager.Core.Obsidian;
using VaultManager.Core.Store;

namespace VaultManager.Core.Sync;

public enum SettingsCategory { Hotkeys, Appearance, Editor, CorePlugins, PluginSettings }

public enum SyncDecision
{
	/// <summary>Both vaults already hold the same file.</summary>
	Same,

	/// <summary>Only the source moved since the vaults last agreed, or the target has no such file.</summary>
	Copy,

	/// <summary>Only the target moved: this is its own edit, and it stays.</summary>
	KeepTarget,

	/// <summary>Both moved, or they differ with no shared history. A person has to pick.</summary>
	Conflict,

	/// <summary>The source holds something that looks like a key.</summary>
	BlockedSecret,
}

public sealed record SyncEntry(SettingsCategory Category, string RelativePath, SyncDecision Decision, string SourceHash, string? TargetHash, IReadOnlyList<string> SecretKeys);

/// <summary>
/// Compares settings of two vaults against the hash they last agreed on, so the choice between
/// "copy" and "leave it" comes from history instead of a blind overwrite. Deletions do not travel.
/// </summary>
public sealed class SettingsSync(AppDatabase database)
{
	static readonly string[] IgnoredPluginFiles = [VaultLayout.MainFile, VaultLayout.ManifestFile, "styles.css"];

	public IReadOnlyList<SyncEntry> Preview(string sourceVault, string targetVault, IReadOnlyCollection<SettingsCategory> categories)
	{
		string sourceConfig = VaultLayout.ConfigDir(sourceVault);
		string targetConfig = VaultLayout.ConfigDir(targetVault);
		var entries = new List<SyncEntry>();

		foreach ((SettingsCategory category, string relative) in Candidates(sourceVault, targetVault, categories))
		{
			string source = Path.Combine(sourceConfig, relative.Replace('/', Path.DirectorySeparatorChar));
			string target = Path.Combine(targetConfig, relative.Replace('/', Path.DirectorySeparatorChar));
			string sourceHash = SharedFile.Hash(source);
			string? targetHash = File.Exists(target) ? SharedFile.Hash(target) : null;
			IReadOnlyList<string> secrets = relative.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? SecretScanner.FindKeysInFile(source) : [];
			string? baseHash = database.SyncBase(sourceVault, targetVault, relative);
			entries.Add(new SyncEntry(category, relative, Decide(sourceHash, targetHash, baseHash, secrets.Count > 0), sourceHash, targetHash, secrets));
		}

		return entries;
	}

	public static SyncDecision Decide(string source, string? target, string? baseHash, bool hasSecret)
	{
		if (source == target)
		{
			return SyncDecision.Same;
		}

		if (hasSecret)
		{
			return SyncDecision.BlockedSecret;
		}

		if (target is null)
		{
			return SyncDecision.Copy;
		}

		if (baseHash is null)
		{
			return SyncDecision.Conflict;
		}

		if (target == baseHash)
		{
			return SyncDecision.Copy;
		}

		return source == baseHash ? SyncDecision.KeepTarget : SyncDecision.Conflict;
	}

	public static CopySettingsFile ToChange(string sourceVault, string targetVault, SyncEntry entry) =>
		new(targetVault, sourceVault, entry.RelativePath, entry.SourceHash, entry.TargetHash);

	static IEnumerable<(SettingsCategory, string)> Candidates(string sourceVault, string targetVault, IReadOnlyCollection<SettingsCategory> categories)
	{
		string config = VaultLayout.ConfigDir(sourceVault);

		IEnumerable<(SettingsCategory, string)> One(SettingsCategory category, string file) =>
			categories.Contains(category) && File.Exists(Path.Combine(config, file)) ? [(category, file)] : [];

		IEnumerable<(SettingsCategory, string)> Tree(SettingsCategory category, string folder)
		{
			string root = Path.Combine(config, folder);
			if (!categories.Contains(category) || !Directory.Exists(root))
			{
				yield break;
			}

			foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
			{
				yield return (category, Path.GetRelativePath(config, file).Replace(Path.DirectorySeparatorChar, '/'));
			}
		}

		foreach ((SettingsCategory, string) item in One(SettingsCategory.Hotkeys, "hotkeys.json")
			.Concat(One(SettingsCategory.Editor, "app.json"))
			.Concat(One(SettingsCategory.CorePlugins, "core-plugins.json"))
			.Concat(One(SettingsCategory.Appearance, "appearance.json"))
			.Concat(Tree(SettingsCategory.Appearance, "themes"))
			.Concat(Tree(SettingsCategory.Appearance, "snippets")))
		{
			yield return item;
		}

		if (categories.Contains(SettingsCategory.PluginSettings) && Directory.Exists(VaultLayout.PluginsDir(sourceVault)))
		{
			foreach (string plugin in Directory.EnumerateDirectories(VaultLayout.PluginsDir(sourceVault)))
			{
				string id = Path.GetFileName(plugin);
				// A plugin's settings mean nothing where the plugin is absent, and Git settings point at one repository.
				if (id != "obsidian-git" && File.Exists(Path.Combine(plugin, VaultLayout.DataFile)) && Directory.Exists(VaultLayout.PluginDir(targetVault, id)))
				{
					yield return (SettingsCategory.PluginSettings, $"plugins/{id}/{VaultLayout.DataFile}");
				}
			}
		}
	}
}
