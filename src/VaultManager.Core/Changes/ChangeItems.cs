using VaultManager.Core.Profiles;

namespace VaultManager.Core.Changes;

public enum ChangeStatus { Done, Failed, Waiting, NeedsReview }

/// <summary>Done can still carry a note, as when settings were left behind because they held a key.</summary>
public enum ChangeNote { None, SettingsSkippedSecret }

public sealed record ChangeResult(Guid ItemId, ChangeStatus Status, Foundation.ChangeFailure Failure = Foundation.ChangeFailure.None, ChangeNote Note = ChangeNote.None);

/// <summary>One line in the Changes panel. Nothing touches a disk until the executor runs it.</summary>
public abstract record ChangeItem
{
	public Guid Id { get; init; } = Guid.NewGuid();

	/// <summary>The vault this writes into; open and offline checks apply to it.</summary>
	public abstract string VaultPath { get; }
}

/// <summary>Installs or updates a plugin by copying it from another vault on this PC.</summary>
/// <param name="ExpectedTargetHash">main.js of the target when queued, to notice it changed before Apply.</param>
public sealed record CopyPlugin(
	string TargetVault,
	string PluginId,
	string PluginName,
	string? FromVersion,
	string ToVersion,
	string SourceVault,
	bool CopySettings,
	string? ExpectedTargetHash) : ChangeItem
{
	public override string VaultPath => TargetVault;
}

/// <summary>Installs or updates a plugin from its GitHub release. Only ever queued from the official catalog.</summary>
public sealed record DownloadPlugin(
	string TargetVault,
	string PluginId,
	string PluginName,
	string? FromVersion,
	string ToVersion,
	string Repo,
	long SizeBytes,
	string? ExpectedTargetHash) : ChangeItem
{
	public override string VaultPath => TargetVault;
}

public sealed record SetPluginEnabled(string TargetVault, string PluginId, string PluginName, bool Enabled) : ChangeItem
{
	public override string VaultPath => TargetVault;
}

/// <param name="RelativePath">Path inside .obsidian, with forward slashes.</param>
public sealed record CopySettingsFile(
	string TargetVault,
	string SourceVault,
	string RelativePath,
	string SourceHash,
	string? ExpectedTargetHash) : ChangeItem
{
	public override string VaultPath => TargetVault;
}

/// <summary>Adds what a profile asks for: folders, folder notes, the vault map, templates. Existing notes stay.</summary>
public sealed record AddProfileParts(string TargetVault, Profile Profile) : ChangeItem
{
	public override string VaultPath => TargetVault;
}

/// <summary>Creates a new vault folder from a profile. Refused when the folder already holds anything.</summary>
public sealed record CreateVault(string Parent, string Name, Profile? Profile, IReadOnlyDictionary<string, string> PluginSources) : ChangeItem
{
	public string NewVaultPath => Path.Combine(Parent, Name);
	public override string VaultPath => NewVaultPath;
}

/// <summary>Adds a vault to Obsidian's list. Waits while Obsidian runs.</summary>
public sealed record RegisterVault(string NewVault) : ChangeItem
{
	public override string VaultPath => NewVault;
}
