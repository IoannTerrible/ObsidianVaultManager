namespace VaultManager.Core.Foundation;

/// <summary>Why a change could not go through. The window turns these into words; nothing decides by a string.</summary>
public enum ChangeFailure
{
	None,
	VaultOffline,
	VaultOpen,
	ObsidianRunning,
	SourceMissing,
	SourceChanged,
	TargetChanged,
	TargetNotEmpty,
	ListUnreadable,
	RegistryUnreadable,
	SecretInSettings,
	DownloadFailed,
	VerifyFailed,
	NothingToUndo,
	WriteFailed,

	/// <summary>A swap failed and so did putting the old folder back. The old copy is on disk in the staging folder.</summary>
	RecoveryNeeded,
	Unexpected,
}

public sealed class ChangeRefusedException(ChangeFailure failure) : Exception(failure.ToString())
{
	public ChangeFailure Failure { get; } = failure;
}
