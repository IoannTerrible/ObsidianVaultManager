using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VaultManager.App.Controls;
using VaultManager.App.Localization;
using VaultManager.App.Logic;
using VaultManager.Core.Changes;
using VaultManager.Core.Foundation;
using VaultManager.Core.Obsidian;
using VaultManager.Core.Profiles;

namespace VaultManager.App.ViewModels;

internal sealed partial class ProfileItemVm(string id) : ObservableObject
{
	public string Id { get; } = id;

	[ObservableProperty] string _name = string.Empty;
}

internal sealed partial class ProfilePartVm(string value, Action<string> remove) : ObservableObject
{
	public string Value { get; } = value;

	[ObservableProperty] string _label = value;
	[ObservableProperty] bool _canRemove;

	[RelayCommand]
	void Remove() => remove(Value);
}

internal sealed partial class AuditRowVm(string vaultId, ProfilesViewModel owner) : ObservableObject
{
	public string VaultId { get; } = vaultId;

	[ObservableProperty] string _name = string.Empty;
	[ObservableProperty] string _tip = string.Empty;
	[ObservableProperty] string _result = string.Empty;
	[ObservableProperty] AuditResult _kind;
	[ObservableProperty] bool _isMatch;
	[ObservableProperty] bool _isMissing;
	[ObservableProperty] StateKind _glyph;
	[ObservableProperty] bool _isBound;
	[ObservableProperty] bool _isExpanded;
	[ObservableProperty] bool _isWaiting;
	[ObservableProperty] bool _isOffline;

	public ObservableCollection<string> Lines { get; } = [];

	public ActionSlot Fix { get; } = new();
	public ActionSlot Unbind { get; } = new();

	[RelayCommand]
	void ToggleExpand() => owner.Toggle(this);
}

/// <summary>Profiles: one read-only recipe, any number of your own, and how each vault compares.</summary>
internal sealed partial class ProfilesViewModel(ShellViewModel shell) : ObservableObject
{
	bool _loading;
	Profile? _current;
	string? _expanded;
	int _auditRun;
	IReadOnlyDictionary<string, ProfileAudit> _audits = new Dictionary<string, ProfileAudit>();

	public ObservableCollection<ProfileItemVm> Items { get; } = [];
	public ObservableCollection<ProfilePartVm> PluginParts { get; } = [];
	public ObservableCollection<ProfilePartVm> FolderParts { get; } = [];
	public ObservableCollection<AuditRowVm> AuditRows { get; } = [];
	public ObservableCollection<string> LocalPlugins { get; } = [];

	[ObservableProperty] ProfileItemVm? _selected;
	[ObservableProperty] string _name = string.Empty;
	[ObservableProperty] string _description = string.Empty;
	[ObservableProperty] bool _folderNotes;
	[ObservableProperty] bool _vaultMap;
	[ObservableProperty] bool _isBuiltIn;
	[ObservableProperty] bool _isEditable;
	[ObservableProperty] string _problem = string.Empty;
	[ObservableProperty] bool _hasProblem;
	[ObservableProperty] string _newPluginId = string.Empty;
	[ObservableProperty] string _newFolder = string.Empty;
	[ObservableProperty] string? _localPluginChoice;
	[ObservableProperty] bool _confirmingDelete;
	[ObservableProperty] bool _isEditing;

	WorkspaceData Data => shell.Data;

	public Profile? Current => _current;

	/// <summary>The header sentence: how many vaults this recipe fits.</summary>
	public string HeadLine() => Texts.Count(K.Plural.VaultsMatch, _audits.Values.Count(a => a.IsComplete));

	/// <summary>What is short, in plain words: "1 folder missing". Used on the home list and here.</summary>
	public static string MissingText(ProfileAudit audit)
	{
		var parts = new List<string>();
		if (audit.MissingFolders.Count > 0)
		{
			parts.Add(Texts.Count(K.Plural.Folders, audit.MissingFolders.Count));
		}

		if (audit.MissingPlugins.Count > 0)
		{
			parts.Add(Texts.Count(K.Plural.Plugins, audit.MissingPlugins.Count));
		}

		if (audit.VaultMapMissing)
		{
			parts.Add(Texts.Get(K.Attn_GapMap));
		}

		return Texts.Format(K.Audit_SummaryMissing, Texts.Join(parts));
	}

	[RelayCommand]
	public void Edit()
	{
		if (IsEditable)
		{
			IsEditing = true;
			shell.Rebuild();
		}
	}

	[RelayCommand]
	public void DoneEditing()
	{
		IsEditing = false;
		ClearProblem();
		shell.Rebuild();
	}

	partial void OnSelectedChanged(ProfileItemVm? value)
	{
		if (_loading || value is null)
		{
			return;
		}

		ConfirmingDelete = false;
		IsEditing = false;
		Show(Data.Profiles.FirstOrDefault(p => p.Id == value.Id));
		shell.Rebuild();
		_ = RefreshAuditAsync();
	}

	partial void OnNameChanged(string value) => EditScalar(p => p with { Name = value });

	partial void OnDescriptionChanged(string value) => EditScalar(p => p with { Description = value });

	partial void OnFolderNotesChanged(bool value) => EditStructure(p => p with { FolderNotes = value });

	partial void OnVaultMapChanged(bool value) => EditStructure(p => p with { VaultMap = value });

	partial void OnLocalPluginChoiceChanged(string? value)
	{
		if (!_loading && value is not null)
		{
			string id = value;
			_loading = true;
			LocalPluginChoice = null;
			_loading = false;
			AddPlugin(id);
		}
	}

	void EditScalar(Func<Profile, Profile> change)
	{
		if (!_loading && _current is { IsBuiltIn: false } && Save(change(_current)))
		{
			shell.Rebuild();
		}
	}

	void EditStructure(Func<Profile, Profile> change)
	{
		if (!_loading && _current is { IsBuiltIn: false } && Save(change(_current)))
		{
			_ = shell.RefreshAuditsAsync();
		}
	}

	bool Save(Profile candidate)
	{
		ProfileProblem problem = ProfileValidator.Check(candidate);
		if (problem != ProfileProblem.None)
		{
			ShowProblem(EnumTexts.Of(problem));
			return false;
		}

		try
		{
			shell.Host.Profiles.Save(candidate);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			ShowProblem(Texts.Get(K.Profile_SaveFailed));
			return false;
		}

		_current = candidate;
		ClearProblem();
		Data.Profiles = shell.Host.Profiles.List();
		return true;
	}

	void ShowProblem(string text)
	{
		Problem = text;
		HasProblem = true;
	}

	void ClearProblem()
	{
		Problem = string.Empty;
		HasProblem = false;
	}

	[RelayCommand]
	void AddPluginFromBox()
	{
		string id = NewPluginId.Trim();
		if (id.Length > 0 && AddPlugin(id))
		{
			NewPluginId = string.Empty;
		}
	}

	bool AddPlugin(string id)
	{
		if (_current is not { IsBuiltIn: false } profile || profile.Plugins.Contains(id, StringComparer.Ordinal))
		{
			return true;
		}

		if (!Save(profile with { Plugins = profile.Plugins.Append(id).ToList() }))
		{
			return false;
		}

		_ = shell.RefreshAuditsAsync();
		return true;
	}

	void RemovePlugin(string id)
	{
		if (_current is { IsBuiltIn: false } profile && Save(profile with { Plugins = profile.Plugins.Where(p => p != id).ToList() }))
		{
			_ = shell.RefreshAuditsAsync();
		}
	}

	[RelayCommand]
	void AddFolder()
	{
		string folder = NewFolder.Trim();
		if (folder.Length == 0 || _current is not { IsBuiltIn: false } profile)
		{
			return;
		}

		if (!profile.Folders.Contains(folder, StringComparer.OrdinalIgnoreCase) && !Save(profile with { Folders = profile.Folders.Append(folder).ToList() }))
		{
			return;
		}

		NewFolder = string.Empty;
		_ = shell.RefreshAuditsAsync();
	}

	void RemoveFolder(string folder)
	{
		if (_current is { IsBuiltIn: false } profile && Save(profile with { Folders = profile.Folders.Where(f => f != folder).ToList() }))
		{
			_ = shell.RefreshAuditsAsync();
		}
	}

	[RelayCommand]
	public void NewProfile()
	{
		string name = ProfileNames.Free(Texts.Get(K.Profile_NewName), Data.Profiles.Select(p => p.Name));
		Profile fresh = shell.Host.Profiles.Duplicate(new Profile { Id = "new", Name = name }, name);
		AddAndSelect(fresh);
		IsEditing = true;
		shell.Rebuild();
	}

	[RelayCommand]
	public void Duplicate()
	{
		if (_current is null)
		{
			return;
		}

		string name = ProfileNames.Free(Texts.Format(K.Profile_CopyName, _current.Name), Data.Profiles.Select(p => p.Name));
		AddAndSelect(shell.Host.Profiles.Duplicate(_current, name));
		IsEditing = true;
		shell.Rebuild();
	}

	void AddAndSelect(Profile profile)
	{
		try
		{
			shell.Host.Profiles.Save(profile);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			ShowProblem(Texts.Get(K.Profile_SaveFailed));
			return;
		}

		SelectAfterChange(profile.Id);
		shell.Log.Add(LogKind.Plain, Texts.Format(K.Log_ProfileCreated, profile.Name));
	}

	void SelectAfterChange(string id)
	{
		Data.Profiles = shell.Host.Profiles.List();
		SyncItems();
		ProfileItemVm? item = Items.FirstOrDefault(i => i.Id == id);
		if (item is not null)
		{
			_loading = true;
			Selected = item;
			_loading = false;
			Show(Data.Profiles.FirstOrDefault(p => p.Id == id));
		}

		_ = shell.RefreshAuditsAsync();
	}

	[RelayCommand]
	public void AskDelete()
	{
		if (_current is { IsBuiltIn: false })
		{
			ConfirmingDelete = true;
		}
	}

	[RelayCommand]
	void CancelDelete() => ConfirmingDelete = false;

	[RelayCommand]
	public void ConfirmDelete()
	{
		ConfirmingDelete = false;
		if (_current is not { IsBuiltIn: false } profile)
		{
			return;
		}

		try
		{
			shell.Host.Profiles.Delete(profile.Id);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			ShowProblem(Texts.Get(K.Profile_SaveFailed));
			return;
		}

		// A binding to a deleted profile would point at nothing; the vaults simply stop using it.
		foreach (VaultState vault in Data.Snapshot?.Vaults ?? [])
		{
			if (shell.Host.Database.VaultProfile(vault.Id) == profile.Id)
			{
				shell.Host.Database.SetVaultProfile(vault.Id, null);
			}
		}

		shell.Log.Add(LogKind.Plain, Texts.Format(K.Log_ProfileDeleted, profile.Name));
		IsEditing = false;
		SelectAfterChange(ProfileStore.BuiltInId);
	}

	[RelayCommand]
	public async Task Import()
	{
		string? path = await shell.Services.Files.PickProfileToImportAsync();
		if (path is null)
		{
			return;
		}

		try
		{
			Profile imported = shell.Host.Profiles.Import(path);
			ClearProblem();
			shell.Log.Add(LogKind.Ok, Texts.Format(K.Log_ProfileImported, imported.Name));
			SelectAfterChange(imported.Id);
		}
		catch (ProfileRejectedException ex)
		{
			ShowProblem(Texts.Format(K.Profile_Rejected, EnumTexts.Of(ex.Problem)));
			shell.Log.Add(LogKind.Problem, Problem);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			ShowProblem(Texts.Get(K.Profile_ImportFailed));
			shell.Log.Add(LogKind.Problem, Problem);
		}
	}

	[RelayCommand]
	public async Task Export()
	{
		if (_current is null)
		{
			return;
		}

		string? path = await shell.Services.Files.PickProfileToExportAsync(_current.Id + ProfileFile.Extension);
		if (path is null)
		{
			return;
		}

		// A picker may hand back the name as typed; without the extension, Import would not list the file.
		if (!path.EndsWith(ProfileFile.Extension, StringComparison.OrdinalIgnoreCase))
		{
			path += ProfileFile.Extension;
		}

		try
		{
			shell.Host.Profiles.Export(_current, path);
			ClearProblem();
			shell.Log.Add(LogKind.Ok, Texts.Format(K.Log_ProfileExported, _current.Name, path));
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			ShowProblem(Texts.Get(K.Profile_ExportFailed));
			shell.Log.Add(LogKind.Problem, Problem);
		}
	}

	void Show(Profile? profile)
	{
		_current = profile;
		_loading = true;
		Name = profile?.Name ?? string.Empty;
		Description = profile?.Description ?? string.Empty;
		FolderNotes = profile?.FolderNotes ?? false;
		VaultMap = profile?.VaultMap ?? false;
		IsBuiltIn = profile?.IsBuiltIn ?? false;
		IsEditable = profile is { IsBuiltIn: false };
		NewPluginId = string.Empty;
		NewFolder = string.Empty;
		_loading = false;
		ClearProblem();
	}

	void SyncItems()
	{
		CollectionSync.Sync(Items, Data.Profiles, i => i.Id, p => p.Id, p => new ProfileItemVm(p.Id), (_, _) => { });
		foreach (Profile profile in Data.Profiles)
		{
			Items.First(i => i.Id == profile.Id).Name = profile.Name;
		}
	}

	public void Rebuild()
	{
		_loading = true;
		SyncItems();
		if (Selected is null || Items.All(i => i.Id != Selected.Id))
		{
			Selected = Items.FirstOrDefault();
		}

		_loading = false;

		if (_current is null || _current.Id != Selected?.Id)
		{
			Show(Data.Profiles.FirstOrDefault(p => p.Id == Selected?.Id));
		}

		Profile? profile = _current;
		WorkspaceSnapshot? snapshot = Data.Snapshot;
		if (profile is null)
		{
			return;
		}

		SyncParts(PluginParts, profile.Plugins, RemovePlugin, id => snapshot?.Plugins.FirstOrDefault(p => p.Id == id)?.Name ?? id);
		SyncParts(FolderParts, profile.Folders, RemoveFolder, f => f);

		_loading = true;
		List<string> local = snapshot?.Plugins.Where(p => !profile.Plugins.Contains(p.Id)).Select(p => p.Id).ToList() ?? [];
		CollectionSync.Sync(LocalPlugins, local, s => s, s => s, s => s, (_, _) => { });
		_loading = false;

		// Reads the database per vault, so it waits until the view is on screen; moving there rebuilds.
		if (shell.View == MainView.Profiles)
		{
			UpdateAuditRows(profile, snapshot);
		}
	}

	void SyncParts(ObservableCollection<ProfilePartVm> parts, IReadOnlyList<string> values, Action<string> remove, Func<string, string> label)
	{
		CollectionSync.Sync(parts, values, p => p.Value, v => v, v => new ProfilePartVm(v, remove), (_, _) => { });
		foreach (ProfilePartVm part in parts)
		{
			part.Label = label(part.Value);
			part.CanRemove = IsEditable;
		}
	}

	/// <summary>Reads folders of every online vault, one after another, off the UI thread.</summary>
	public async Task RefreshAuditAsync()
	{
		Profile? profile = _current;
		WorkspaceSnapshot? snapshot = Data.Snapshot;
		if (profile is null || snapshot is null)
		{
			return;
		}

		int run = ++_auditRun;
		Dictionary<string, ProfileAudit> audits = await Task.Run(() =>
		{
			var found = new Dictionary<string, ProfileAudit>();
			foreach (VaultState vault in snapshot.Vaults.Where(v => v.IsOnline))
			{
				found[vault.Id] = ProfileAudit.For(profile, vault.Path, PluginFacts.EnabledIn(snapshot, vault.Id).ToList());
			}

			return found;
		});

		if (run == _auditRun)
		{
			_audits = audits;
			UpdateAuditRows(_current ?? profile, Data.Snapshot);
			shell.RefreshHead();
		}
	}

	void UpdateAuditRows(Profile profile, WorkspaceSnapshot? snapshot)
	{
		IReadOnlyList<VaultState> vaults = snapshot?.Vaults ?? [];
		CollectionSync.Sync(AuditRows, vaults, r => r.VaultId, v => v.Id, v => new AuditRowVm(v.Id, this), (_, _) => { });
		foreach (VaultState vault in vaults)
		{
			UpdateAuditRow(AuditRows.First(r => r.VaultId == vault.Id), profile, vault, snapshot!);
		}
	}

	public void Toggle(AuditRowVm row)
	{
		_expanded = _expanded == row.VaultId ? null : row.VaultId;
		foreach (AuditRowVm other in AuditRows)
		{
			other.IsExpanded = other.VaultId == _expanded;
		}
	}

	void UpdateAuditRow(AuditRowVm row, Profile profile, VaultState vault, WorkspaceSnapshot snapshot)
	{
		string? boundId = shell.Host.Database.VaultProfile(vault.Id);
		bool bound = boundId == profile.Id;
		Profile? other = bound ? null : Data.Profiles.FirstOrDefault(p => p.Id == boundId);
		_audits.TryGetValue(vault.Id, out ProfileAudit? audit);

		row.Name = vault.DisplayName;
		row.Tip = vault.Path;
		row.IsBound = bound;
		row.IsExpanded = _expanded == vault.Id;
		row.IsOffline = !vault.IsOnline;
		row.Kind = AuditRules.Of(audit, vault.IsOnline, bound, other is not null);
		(row.Result, row.Glyph) = row.Kind switch
		{
			AuditResult.Offline => (Texts.Get(K.Vault_Offline), StateKind.Offline),
			AuditResult.Matches => (Texts.Get(K.Audit_Matches), StateKind.Match),
			AuditResult.Missing => (MissingText(audit!), StateKind.Older),
			AuditResult.UsesOther => (Texts.Format(K.Audit_UsesOther, other!.Name), StateKind.Off),
			AuditResult.NotUsing => (Texts.Get(K.Audit_NotUsed), StateKind.None),
			_ => (string.Empty, StateKind.None),
		};
		row.IsMatch = row.Kind == AuditResult.Matches;
		row.IsMissing = row.Kind == AuditResult.Missing;
		row.Fix.Hide();
		row.Unbind.Hide();
		row.IsWaiting = false;
		row.Lines.Clear();

		if (!vault.IsOnline)
		{
			row.Lines.Add(Texts.Get(K.Audit_Offline));
			return;
		}

		if (audit is null)
		{
			return;
		}

		ProfileFill fill = Plans.Fill(profile, audit, vault, snapshot);
		if (audit.MissingPlugins.Count > 0)
		{
			IEnumerable<string> names = audit.MissingPlugins.Select(id => snapshot.Plugins.FirstOrDefault(p => p.Id == id)?.Name ?? id);
			row.Lines.Add(Texts.Format(K.Audit_MissingPlugins, string.Join(", ", names)));
		}

		if (fill.Unavailable.Count > 0)
		{
			row.Lines.Add(Texts.Format(K.Audit_Unavailable, string.Join(", ", fill.Unavailable)));
		}

		if (audit.MissingFolders.Count > 0)
		{
			row.Lines.Add(Texts.Format(K.Audit_MissingFolders, string.Join(", ", audit.MissingFolders)));
		}

		if (audit.VaultMapMissing)
		{
			row.Lines.Add(Texts.Get(K.Audit_MissingMap));
		}

		if (audit.OtherPlugins.Count > 0)
		{
			IEnumerable<string> names = audit.OtherPlugins.Select(id => snapshot.Plugins.FirstOrDefault(p => p.Id == id)?.Name ?? id);
			row.Lines.Add(Texts.Format(K.Audit_OtherPlugins, string.Join(", ", names)));
		}

		if (audit.IsComplete)
		{
			row.Lines.Add(Texts.Get(K.Audit_Complete));
		}

		List<ChangeItem> todo = fill.Items.Where(i => !IsPending(i)).ToList();
		if (!bound)
		{
			row.Fix.Set(Texts.Get(K.Action_UseProfile), () =>
			{
				shell.Host.Database.SetVaultProfile(vault.Id, profile.Id);
				shell.Log.Add(LogKind.Plain, Texts.Format(K.Log_ProfileBound, vault.DisplayName, profile.Name));
				_ = RunAndRefresh(todo);
			}, enabled: !vault.ListUnreadable);
		}
		else
		{
			row.Unbind.Set(Texts.Get(K.Action_StopUsing), () =>
			{
				shell.Host.Database.SetVaultProfile(vault.Id, null);
				_ = shell.RefreshAuditsAsync();
			});
			if (todo.Count > 0)
			{
				row.Fix.Set(Texts.Get(K.Action_AddMissing), () => _ = RunAndRefresh(todo), enabled: !vault.ListUnreadable);
			}
		}

		row.IsWaiting = todo.Count == 0 && fill.Items.Count > 0;
	}

	async Task RunAndRefresh(IReadOnlyList<ChangeItem> items)
	{
		await shell.Changes.Run(items);
		await shell.RefreshAuditsAsync();
	}

	bool IsPending(ChangeItem item) => item switch
	{
		CopyPlugin c => shell.Changes.Queue.IsPluginPending(c.TargetVault, c.PluginId),
		SetPluginEnabled s => shell.Changes.Queue.IsTogglePending(s.TargetVault, s.PluginId),
		AddProfileParts p => shell.Changes.Queue.ArePartsPending(p.TargetVault),
		_ => false,
	};
}
