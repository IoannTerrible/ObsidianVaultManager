using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VaultManager.App.Controls;
using VaultManager.App.Localization;
using VaultManager.App.Logic;
using VaultManager.Core.Changes;
using VaultManager.Core.Foundation;
using VaultManager.Core.Obsidian;
using VaultManager.Core.Sync;

namespace VaultManager.App.ViewModels;

internal sealed partial class SyncCategoryVm(SettingsCategory category, Action changed) : ObservableObject
{
	public SettingsCategory Category { get; } = category;

	// Plugin settings hold keys and vault-specific paths, so they travel only when asked for.
	[ObservableProperty] bool _isOn = category != SettingsCategory.PluginSettings;
	[ObservableProperty] string _label = string.Empty;

	partial void OnIsOnChanged(bool value) => changed();
}

internal sealed partial class SyncVaultChoice(string id) : ObservableObject
{
	public string Id { get; } = id;

	[ObservableProperty] string _name = string.Empty;
}

internal sealed partial class SyncLineVm(SyncEntry entry, SettingsSyncViewModel owner) : ObservableObject
{
	public SyncEntry Entry { get; set; } = entry;

	public string Key => Entry.RelativePath;

	[ObservableProperty] StateKind _glyph;
	[ObservableProperty] string _path = entry.RelativePath;
	[ObservableProperty] string _category = string.Empty;
	[ObservableProperty] string _sentence = string.Empty;
	[ObservableProperty] bool _isSelected;
	[ObservableProperty] bool _canSelect;
	[ObservableProperty] bool _isConflict;
	[ObservableProperty] bool _takesSource;
	[ObservableProperty] bool _keepsTarget;
	[ObservableProperty] bool _isWaiting;

	public ConflictChoice Choice => TakesSource ? ConflictChoice.TakeSource : KeepsTarget ? ConflictChoice.KeepTarget : ConflictChoice.None;

	partial void OnIsSelectedChanged(bool value) => owner.SelectionChanged();

	partial void OnTakesSourceChanged(bool value)
	{
		if (value)
		{
			KeepsTarget = false;
		}

		owner.SelectionChanged();
	}

	partial void OnKeepsTargetChanged(bool value)
	{
		if (value)
		{
			TakesSource = false;
		}

		owner.SelectionChanged();
	}
}

/// <summary>Settings between two vaults. Every decision comes from Core; this only shows it and asks where a person must choose.</summary>
internal sealed partial class SettingsSyncViewModel : ObservableObject
{
	readonly ShellViewModel _shell;

	public SettingsSyncViewModel(ShellViewModel shell)
	{
		_shell = shell;
		foreach (SettingsCategory category in Enum.GetValues<SettingsCategory>())
		{
			Categories.Add(new SyncCategoryVm(category, Invalidate));
		}
	}

	public ObservableCollection<SyncVaultChoice> Sources { get; } = [];
	public ObservableCollection<SyncVaultChoice> Targets { get; } = [];
	public ObservableCollection<SyncCategoryVm> Categories { get; } = [];
	public ObservableCollection<SyncLineVm> Lines { get; } = [];

	[ObservableProperty] SyncVaultChoice? _source;
	[ObservableProperty] SyncVaultChoice? _target;
	[ObservableProperty] bool _canCompare;
	[ObservableProperty] bool _isComparing;
	[ObservableProperty] bool _compared;
	[ObservableProperty] string _summary = string.Empty;
	[ObservableProperty] string _error = string.Empty;
	[ObservableProperty] bool _hasError;
	[ObservableProperty] bool _canAdd;
	[ObservableProperty] string _addText = string.Empty;
	[ObservableProperty] bool _hasLines;
	[ObservableProperty] bool _hasFixedTarget;
	[ObservableProperty] bool _isCopying;

	string _comparedSource = string.Empty;
	string _comparedTarget = string.Empty;
	bool _settingChoices;
	int _question;

	WorkspaceData Data => _shell.Data;

	public int SourceSpan => HasFixedTarget ? 3 : 1;

	partial void OnHasFixedTargetChanged(bool value) => OnPropertyChanged(nameof(SourceSpan));

	partial void OnSourceChanged(SyncVaultChoice? value)
	{
		if (!_settingChoices)
		{
			Invalidate();
			Rebuild();
		}
	}

	partial void OnTargetChanged(SyncVaultChoice? value)
	{
		if (!_settingChoices)
		{
			Invalidate();
			Rebuild();
		}
	}

	/// <summary>Opened from a vault page: this vault takes the settings, the person picks only where they come from.</summary>
	public void SetFixedTarget(string vaultId)
	{
		HasFixedTarget = true;
		_settingChoices = true;
		Source = null;
		_settingChoices = false;
		Rebuild();
		Target = Targets.FirstOrDefault(t => t.Id == vaultId);
		Invalidate();
	}

	/// <summary>A table of an older question must not be queued as if it answered the new one.</summary>
	void Invalidate()
	{
		_question++;
		Lines.Clear();
		Compared = false;
		HasLines = false;
		HasError = false;
		Summary = string.Empty;
		UpdateButtons();
	}

	public void Rebuild()
	{
		IReadOnlyList<VaultState> online = Data.Snapshot?.Vaults.Where(v => v.IsOnline).ToList() ?? [];
		_settingChoices = true;
		string? source = Source?.Id;
		string? target = Target?.Id;
		SyncChoices(Sources, online.Where(v => v.Id != target).ToList());
		SyncChoices(Targets, online.Where(v => v.Id != source).ToList());
		Source = Sources.FirstOrDefault(s => s.Id == source);
		Target = Targets.FirstOrDefault(t => t.Id == target);
		_settingChoices = false;

		foreach (SyncCategoryVm category in Categories)
		{
			category.Label = EnumTexts.Of(category.Category);
		}

		foreach (SyncLineVm line in Lines)
		{
			UpdateLine(line);
		}

		UpdateSummary();
		UpdateButtons();
	}

	static void SyncChoices(ObservableCollection<SyncVaultChoice> choices, IReadOnlyList<VaultState> vaults)
	{
		CollectionSync.Sync(choices, vaults, c => c.Id, v => v.Id, v => new SyncVaultChoice(v.Id), (_, _) => { });
		foreach (VaultState vault in vaults)
		{
			choices.First(c => c.Id == vault.Id).Name = vault.DisplayName;
		}
	}

	VaultState? VaultOf(SyncVaultChoice? choice) => Data.Snapshot?.Vaults.FirstOrDefault(v => v.Id == choice?.Id);

	[RelayCommand]
	public async Task Compare()
	{
		VaultState? source = VaultOf(Source);
		VaultState? target = VaultOf(Target);
		if (source is null || target is null || IsComparing)
		{
			return;
		}

		List<SettingsCategory> categories = Categories.Where(c => c.IsOn).Select(c => c.Category).ToList();
		int question = ++_question;
		IsComparing = true;
		HasError = false;
		UpdateButtons();
		try
		{
			IReadOnlyList<SyncEntry> entries = await Task.Run(() => _shell.Host.Sync.Preview(source.Path, target.Path, categories));

			// On a slow drive the person may have picked other vaults meanwhile; this answer would queue into the wrong one.
			if (question != _question)
			{
				return;
			}

			_comparedSource = source.Path;
			_comparedTarget = target.Path;
			Lines.Clear();
			foreach (SyncEntry entry in entries)
			{
				var line = new SyncLineVm(entry, this);
				line.CanSelect = SyncRules.StartsSelected(entry.Decision);
				line.IsConflict = entry.Decision == SyncDecision.Conflict;
				line.IsSelected = SyncRules.StartsSelected(entry.Decision);
				UpdateLine(line);
				Lines.Add(line);
			}

			Compared = true;
			HasLines = Lines.Count > 0;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			Error = Texts.Format(K.Sync_CompareFailed, ex.GetType().Name);
			HasError = true;
			_shell.Log.Add(LogKind.Problem, Error);
		}
		finally
		{
			IsComparing = false;
			UpdateSummary();
			UpdateButtons();
		}
	}

	void UpdateLine(SyncLineVm line)
	{
		SyncEntry entry = line.Entry;
		line.Category = EnumTexts.Of(entry.Category);
		line.Sentence = entry.Decision == SyncDecision.BlockedSecret
			? Texts.Format(EnumTexts.KeyOf(SyncDecision.BlockedSecret), string.Join(", ", entry.SecretKeys))
			: EnumTexts.Of(entry.Decision);
		line.Glyph = entry.Decision switch
		{
			SyncDecision.Same => StateKind.Match,
			SyncDecision.Copy => StateKind.Selection,
			SyncDecision.KeepTarget => StateKind.Off,
			SyncDecision.Conflict => StateKind.Older,
			_ => StateKind.Blocked,
		};
		line.IsWaiting = _shell.Changes.Queue.IsSettingsPending(_comparedTarget, entry.RelativePath);
	}

	public void SelectionChanged() => UpdateButtons();

	void UpdateSummary()
	{
		if (!Compared)
		{
			Summary = string.Empty;
			return;
		}

		int conflicts = Lines.Count(l => l.Entry.Decision == SyncDecision.Conflict);
		Summary = Lines.Count == 0
			? Texts.Get(K.Sync_NoFiles)
			: conflicts > 0
				? Texts.Count(K.Plural.Files, Lines.Count) + ", " + Texts.Count(K.Plural.Conflicts, conflicts)
				: Texts.Count(K.Plural.Files, Lines.Count);
	}

	void UpdateButtons()
	{
		CanCompare = Source is not null && Target is not null && Source.Id != Target.Id && !IsComparing && Categories.Any(c => c.IsOn);
		int count = Lines.Count(WillQueue);
		CanAdd = !IsCopying && (count > 0 || Lines.Any(KeepsTargetAnswer));
		AddText = count > 0
			? Texts.Count(K.Plural.CopyFiles, count)
			: Lines.Any(KeepsTargetAnswer) ? Texts.Get(K.Sync_SaveChoices) : Texts.Get(K.Sync_Add);
	}

	bool WillQueue(SyncLineVm line) => !line.IsWaiting && SyncRules.WillQueue(line.Entry.Decision, line.IsSelected, line.Choice);

	static bool KeepsTargetAnswer(SyncLineVm line) => line.Entry.Decision == SyncDecision.Conflict && line.Choice == ConflictChoice.KeepTarget;

	[RelayCommand]
	public void Add() => _ = AddAsync();

	/// <summary>Runs at once. Afterwards the table is asked again, so it shows what is true now.</summary>
	public async Task AddAsync()
	{
		var items = new List<ChangeItem>();
		foreach (SyncLineVm line in Lines)
		{
			if (WillQueue(line))
			{
				items.Add(SettingsSync.ToChange(_comparedSource, _comparedTarget, line.Entry));
			}
			else if (KeepsTargetAnswer(line))
			{
				// Keeping the target is an answer too: the next Compare should not ask again until the source moves.
				_shell.Host.Database.SetSyncBase(_comparedSource, _comparedTarget, line.Entry.RelativePath, line.Entry.SourceHash);
				line.Entry = line.Entry with { Decision = SyncDecision.KeepTarget };
				line.KeepsTarget = false;
				line.IsConflict = false;
				UpdateLine(line);
			}
		}

		UpdateSummary();
		if (items.Count == 0)
		{
			UpdateButtons();
			return;
		}

		IsCopying = true;
		UpdateButtons();
		int question = _question;
		try
		{
			await _shell.Changes.Run(items);
		}
		finally
		{
			IsCopying = false;
		}

		if (question == _question)
		{
			await Compare();
		}

		UpdateButtons();
	}
}
