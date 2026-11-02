using VaultManager.App.Localization;
using VaultManager.App.Logic;
using VaultManager.App.Services;
using VaultManager.App.ViewModels;
using VaultManager.Core.Changes;
using VaultManager.Core.Foundation;
using VaultManager.Core.Obsidian;
using VaultManager.Core.Profiles;
using VaultManager.Core.Sync;

namespace VaultManager.Tests.App;

[Collection(AppCollection.Name)]
public sealed class StartAndScanTests
{
	[Fact]
	public Task The_scan_fills_the_window_with_every_vault() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		Assert.False(world.Shell.IsScanning);
		Assert.Equal(10, world.Shell.Vaults.Count);
		Assert.Contains(world.Shell.Log.Entries, e => e.Text.Contains("10 vaults"));
	});

	[Fact]
	public Task A_rescan_keeps_filter_selection_and_row_instances() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		ShellViewModel shell = world.Shell;
		shell.ShowAll();
		shell.Matrix.Filter = "excal";
		MatrixRowVm row = Assert.Single(shell.Matrix.Rows);
		row.Select();
		VaultRowVm vaultRow = shell.Vaults[0];

		await shell.RescanAsync(log: false);

		Assert.Equal("excal", shell.Matrix.Filter);
		Assert.Same(row, Assert.Single(shell.Matrix.Rows));
		Assert.True(row.IsSelected);
		Assert.Same(vaultRow, shell.Vaults[0]);
	});

	[Fact]
	public Task No_registry_file_is_the_no_vaults_state() => Ui.Run(async () =>
	{
		using TestWorld world = TestWorld.Create();
		File.Delete(Path.Combine(world.Paths.ObsidianConfigDir, "obsidian.json"));
		await world.Shell.StartAsync();

		Assert.True(world.Shell.ShowEmpty);
		Assert.True(world.Shell.EmptyCanAddFolder);
		Assert.False(world.Shell.EmptyCanRetry);
		Assert.Equal(Texts.Get(K.Empty_NoVaults), world.Shell.EmptyTitle);
	});

	[Fact]
	public Task A_damaged_registry_says_so_and_offers_a_retry() => Ui.Run(async () =>
	{
		using TestWorld world = TestWorld.Create();
		string registry = Path.Combine(world.Paths.ObsidianConfigDir, "obsidian.json");
		File.WriteAllText(registry, "{ not json");
		await world.Shell.StartAsync();

		Assert.True(world.Shell.ShowEmpty);
		Assert.True(world.Shell.EmptyCanRetry);
		Assert.Equal(Texts.Get(K.Empty_Unreadable), world.Shell.EmptyTitle);
		Assert.Equal("{ not json", File.ReadAllText(registry));
	});

	[Fact]
	public Task An_offline_vault_stays_listed_and_is_no_source() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		VaultRowVm row = world.Shell.Vaults.First(v => v.Name == DemoName.Offline);
		Assert.Equal(VaultManager.App.Controls.StateKind.Offline, row.Glyph);
		world.Shell.ShowSettings();
		Assert.DoesNotContain(world.Shell.Settings.Sources, s => s.Name == DemoName.Offline);
	});

	[Fact]
	public Task A_damaged_enabled_list_blocks_changes_to_that_vault() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		world.Shell.ShowVault(world.VaultNamed(DemoName.Damaged).Id);
		Assert.False(world.Shell.VaultPage.CanAdd);
		Assert.Contains(world.Shell.VaultPage.Banners, b => b.Key == "list");
		Assert.Contains(world.Shell.Attention.Items, i => i.Kind == AttentionKind.ListUnreadable);
	});
}

[Collection(AppCollection.Name)]
public sealed class ChangesFlowTests
{
	static AttentionRowVm Row(TestWorld world, string pluginName) =>
		world.Shell.Attention.Rows.First(r => r.Line.Any(t => t.Text == pluginName));

	[Fact]
	public Task Update_runs_at_once_and_a_line_for_an_open_vault_waits() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		ChangesViewModel changes = world.Shell.Changes;
		string notes = world.VaultNamed("Notes (Vaults)").Path;
		string open = world.Vault(DemoName.Open);

		Row(world, "Excalidraw").Primary.Invoke();
		Assert.True(Row(world, "Excalidraw").IsWaiting);
		await changes.WhenIdle;

		Assert.Equal("2.17.2", Fixture.VersionOf(notes, "obsidian-excalidraw-plugin"));
		Assert.Equal("2.15.2", Fixture.VersionOf(open, "obsidian-excalidraw-plugin"));
		QueueLine waiting = Assert.Single(changes.Queue.Lines);
		Assert.Equal(LineState.Waiting, waiting.State);
		Assert.Equal(ChangeFailure.VaultOpen, waiting.Failure);
		Assert.Contains(EnumTexts.Of(ChangeFailure.VaultOpen), changes.Lines.Single().Reason);
	});

	[Fact]
	public Task A_waiting_line_runs_by_itself_when_the_vault_closes() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		ChangesViewModel changes = world.Shell.Changes;
		string open = world.Vault(DemoName.Open);
		await changes.Run(Plans.UpdateBehind(world.Plugin("obsidian-git"), world.Shell.Data.Snapshot!));
		Assert.Single(changes.Lines);
		Assert.Single(world.Host.Database.PendingChanges());

		// A tick while the gate is still shut changes nothing.
		await world.Shell.WatchObsidianAsync();
		Assert.Equal("2.35.0", Fixture.VersionOf(open, "obsidian-git"));

		world.Process.IsRunning = false;
		await world.Shell.WatchObsidianAsync();

		Assert.Equal("2.38.2", Fixture.VersionOf(open, "obsidian-git"));
		Assert.Empty(changes.Queue.Lines);
		Assert.Empty(world.Host.Database.PendingChanges());
	});

	[Fact]
	public Task A_tick_with_nothing_waiting_touches_no_disk() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		WorkspaceSnapshot before = world.Shell.Data.Snapshot!;

		world.Process.IsRunning = false;
		await world.Shell.WatchObsidianAsync();

		Assert.Same(before, world.Shell.Data.Snapshot);
		Assert.False(world.Shell.Data.ObsidianRunning);
		Assert.DoesNotContain(world.Shell.Vaults, v => v.Note.Contains(Texts.Get(K.Vault_Open)));
	});

	[Fact]
	public Task A_waiting_line_survives_a_restart_of_the_host_and_runs_after_it() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		string open = world.Vault(DemoName.Open);
		await world.Shell.Changes.Run(Plans.UpdateBehind(world.Plugin("obsidian-git"), world.Shell.Data.Snapshot!));

		ShellViewModel still = world.Restart();
		await still.StartAsync();
		QueueLine line = Assert.Single(still.Changes.Queue.Lines);
		Assert.Equal(LineState.Waiting, line.State);
		Assert.Equal(ChangeFailure.VaultOpen, line.Failure);
		Assert.Equal("2.35.0", Fixture.VersionOf(open, "obsidian-git"));

		world.Process.IsRunning = false;
		ShellViewModel after = world.Restart();
		await after.StartAsync();

		Assert.Equal("2.38.2", Fixture.VersionOf(open, "obsidian-git"));
		Assert.Empty(after.Changes.Queue.Lines);
		Assert.Empty(world.Host.Database.PendingChanges());
	});

	[Fact]
	public Task A_line_for_an_offline_drive_waits_and_runs_when_the_drive_is_back() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		string offline = world.VaultNamed(DemoName.Offline).Path;
		await world.Shell.Changes.Run(Plans.AddTo(world.Plugin("calendar"), offline, false)!);
		QueueLine line = Assert.Single(world.Shell.Changes.Queue.Lines);
		Assert.Equal(ChangeFailure.VaultOffline, line.Failure);

		Directory.CreateDirectory(VaultLayout.ConfigDir(offline));
		await world.Shell.WatchObsidianAsync();

		Assert.Equal("1.5.10", Fixture.VersionOf(offline, "calendar"));
		Assert.Empty(world.Shell.Changes.Queue.Lines);
	});

	[Fact]
	public Task A_stale_persisted_line_becomes_needs_review_and_overwrites_nothing() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		string open = world.Vault(DemoName.Open);
		string main = Path.Combine(VaultLayout.PluginDir(open, "obsidian-git"), VaultLayout.MainFile);
		await world.Shell.Changes.Run(Plans.UpdateBehind(world.Plugin("obsidian-git"), world.Shell.Data.Snapshot!));
		File.AppendAllText(main, "// edited while the line waited");
		string edited = File.ReadAllText(main);

		world.Process.IsRunning = false;
		ShellViewModel after = world.Restart();
		await after.StartAsync();

		Assert.Equal(edited, File.ReadAllText(main));
		Assert.Equal(LineState.NeedsReview, Assert.Single(after.Changes.Queue.Lines).State);
		Assert.Empty(world.Host.Database.PendingChanges());
		Assert.True(after.Changes.ShowPanel);

		// Retry makes the line again from what is there now.
		ChangeLineVm vm = after.Changes.Lines.Single();
		Assert.True(vm.CanRetry);
		await after.Changes.Retry(vm.Id);
		Assert.Equal("2.38.2", Fixture.VersionOf(open, "obsidian-git"));
		Assert.Empty(after.Changes.Queue.Lines);
	});

	[Fact]
	public Task Removing_a_waiting_line_removes_it_from_the_table() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		ChangesViewModel changes = world.Shell.Changes;
		await changes.Run(Plans.UpdateBehind(world.Plugin("obsidian-git"), world.Shell.Data.Snapshot!));
		Assert.Single(world.Host.Database.PendingChanges());

		changes.Lines.Single().RemoveCommand.Execute(null);

		Assert.Empty(changes.Queue.Lines);
		Assert.Empty(changes.Lines);
		Assert.Empty(world.Host.Database.PendingChanges());
		ShellViewModel after = world.Restart();
		Assert.Empty(after.Changes.Queue.Lines);

		// And it does not run when the gate opens.
		world.Process.IsRunning = false;
		await after.StartAsync();
		Assert.Equal("2.35.0", Fixture.VersionOf(world.Vault(DemoName.Open), "obsidian-git"));
	});

	[Fact]
	public Task A_damaged_persisted_line_is_said_and_dropped() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		world.Host.Database.SavePending("junk", "{ not a line");

		ShellViewModel after = world.Restart();

		Assert.Empty(after.Changes.Queue.Lines);
		Assert.Empty(world.Host.Database.PendingChanges());
		Assert.Contains(after.Log.Entries, e => e.Text == Texts.Get(K.Log_PendingDamaged) && e.IsProblem);
	});

	[Fact]
	public Task Update_all_skips_ignored_and_download_only_lines_and_never_touches_the_network() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		ShellViewModel shell = world.Shell;
		await shell.ToggleDemoObsidian();
		await shell.Online.Start();
		await shell.Online.Confirm();
		Assert.Contains(shell.Attention.Items, i => i.Kind == AttentionKind.NewerOnline);
		string ignored = shell.Attention.Items.First(i => i is { Kind: AttentionKind.Older, Plugin.Id: "automatic-table-of-contents" }).Key;
		world.Host.Database.SetIgnored(ignored, true);
		shell.Data.Ignored = world.Host.Database.Ignored();
		shell.Rebuild();
		int calls = world.Catalog.Calls.Count;

		// Excalidraw in two vaults and Git in one.
		Assert.True(shell.ShowUpdateAll);
		Assert.Equal(Texts.Format(K.Action_UpdateAll, 3), shell.UpdateAllText);

		await shell.UpdateAll();

		Assert.Equal(calls, world.Catalog.Calls.Count);
		Assert.Equal("2.17.2", Fixture.VersionOf(world.Vault("Notes"), "obsidian-excalidraw-plugin"));
		Assert.Equal("2.38.2", Fixture.VersionOf(world.Vault(DemoName.Open), "obsidian-git"));
		Assert.Equal("1.7.3", Fixture.VersionOf(world.Vault(DemoName.Open), "automatic-table-of-contents"));
		Assert.Equal("2.38.2", Fixture.VersionOf(world.Vault("HyperionDocs"), "obsidian-git"));
		Assert.Empty(shell.Changes.Queue.Lines);
		Assert.False(shell.ShowUpdateAll);
	});

	[Fact]
	public Task A_double_click_queues_once() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		ShellViewModel shell = world.Shell;
		await shell.ToggleDemoObsidian();

		// Twice on the header button, before the first has finished.
		Task first = shell.UpdateAll();
		Task second = shell.UpdateAll();
		Assert.Equal(4, shell.Changes.Queue.Lines.Count);
		await Task.WhenAll(first, second);
		Assert.Equal(4, world.Host.Database.LastUndoable()!.ItemCount);

		// Twice on one request: the second finds the first on its way.
		PluginRow calendar = world.Plugin("calendar");
		CopyPlugin a = Plans.AddTo(calendar, world.Vault("Family"), false)!;
		CopyPlugin b = Plans.AddTo(calendar, world.Vault("Family"), false)!;
		Task one = shell.Changes.Run(a);
		Task two = shell.Changes.Run(b);
		Assert.Equal(a.Id, shell.Changes.Queue.Lines.Single().Item.Id);
		await Task.WhenAll(one, two);
		Assert.Equal(1, world.Host.Database.LastUndoable()!.ItemCount);
	});

	[Fact]
	public Task While_running_the_panel_counts_and_requests_go_after_the_run() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		ChangesViewModel changes = world.Shell.Changes;
		string target = world.Vault("ChronosDocs");
		world.Catalog.HoldDownload = new TaskCompletionSource();
		world.Catalog.DownloadStarted = new TaskCompletionSource();

		Task run = changes.Run([
			new DownloadPlugin(target, "calendar", "Calendar", null, "1.5.10", "demo/calendar", 742_357, null),
			new SetPluginEnabled(target, "dataview", "Dataview", false)]);
		await world.Catalog.DownloadStarted.Task;

		Assert.True(changes.IsRunning);
		Assert.True(changes.ShowPanel);
		Assert.Equal(Texts.Format(K.Changes_Updating, 1, 2), changes.RunText);

		// This one is held while the download runs, and becomes its own operation for Undo.
		Task later = changes.Run(new SetPluginEnabled(target, "waypoint", "Waypoint", false));
		Assert.Equal("1", world.Host.Database.LastUndoable() is null ? "1" : "more");
		Assert.Contains("waypoint", CommunityPluginList.Read(target).Value!);

		world.Catalog.HoldDownload.SetResult();
		await Task.WhenAll(run, later);

		Assert.False(changes.IsRunning);
		Assert.Empty(changes.Queue.Lines);
		Assert.Single(world.Catalog.Calls, c => c.StartsWith("download"));
		Assert.DoesNotContain("waypoint", CommunityPluginList.Read(target).Value!);
		Assert.Equal(1, world.Host.Database.LastUndoable()!.ItemCount);
	});

	[Fact]
	public Task Successful_lines_go_to_the_log_and_failed_ones_stay_with_Retry_and_dismiss() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		ChangesViewModel changes = world.Shell.Changes;
		string target = world.Vault("ChronosDocs");
		string pluginDir = VaultLayout.PluginDir(world.Vault("Family"), "obsidian-excalidraw-plugin");
		CopyPlugin failing = Plans.AddTo(world.Plugin("obsidian-excalidraw-plugin"), target, false)!;
		Directory.Move(pluginDir, pluginDir + ".away");

		await changes.Run([failing, new SetPluginEnabled(target, "dataview", "Dataview", false)]);

		QueueLine line = Assert.Single(changes.Queue.Lines);
		Assert.Equal(LineState.Failed, line.State);
		Assert.Equal(ChangeFailure.SourceMissing, line.Failure);
		Assert.Contains(world.Shell.Log.Entries, e => e.Text.Contains("Dataview"));
		ChangeLineVm vm = changes.Lines.Single();
		Assert.True(vm.CanRetry);
		Assert.Empty(world.Host.Database.PendingChanges());

		Directory.Move(pluginDir + ".away", pluginDir);
		await changes.Retry(vm.Id);

		Assert.Empty(changes.Lines);
		Assert.Equal("2.17.2", Fixture.VersionOf(target, "obsidian-excalidraw-plugin"));

		// Dismiss: the same kind of failure, taken away by hand.
		await changes.Run(new CopyPlugin(target, "gone", "Gone", null, "1.0.0", Path.Combine(world.Root, "nowhere"), false, null));
		changes.Lines.Single().RemoveCommand.Execute(null);
		Assert.Empty(changes.Lines);
	});

	[Fact]
	public Task A_target_that_moved_needs_review_and_Retry_renews_it() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		ChangesViewModel changes = world.Shell.Changes;
		string notes = world.VaultNamed("Notes (Vaults)").Path;
		CopyPlugin line = Plans.UpdateBehind(world.Plugin("obsidian-excalidraw-plugin"), world.Shell.Data.Snapshot!).First(c => c.TargetVault == notes);
		File.AppendAllText(Path.Combine(VaultLayout.PluginDir(notes, "obsidian-excalidraw-plugin"), VaultLayout.MainFile), "// edited");

		await changes.Run(line);

		Assert.Equal(LineState.NeedsReview, Assert.Single(changes.Queue.Lines).State);
		ChangeLineVm vm = changes.Lines.Single();
		Assert.True(vm.CanRetry);

		await vm.RetryCommand.ExecuteAsync(null);

		Assert.Empty(changes.Queue.Lines);
		Assert.Equal("2.17.2", Fixture.VersionOf(notes, "obsidian-excalidraw-plugin"));
	});

	[Fact]
	public Task A_refused_settings_copy_says_so_in_the_log() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		ChangesViewModel changes = world.Shell.Changes;

		await changes.Run(Plans.AddTo(world.Plugin("ai-helper"), world.Vault("ChronosDocs"), copySettings: true)!);

		Assert.Contains(world.Shell.Log.Entries, e => e.Text.Contains(EnumTexts.Of(ChangeNote.SettingsSkippedSecret)));
		Assert.False(File.Exists(Path.Combine(VaultLayout.PluginDir(world.Vault("ChronosDocs"), "ai-helper"), VaultLayout.DataFile)));
		Assert.Empty(changes.Lines);
	});

	[Fact]
	public Task The_panel_is_there_only_when_needed() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		ChangesViewModel changes = world.Shell.Changes;
		Assert.False(changes.ShowPanel);

		// Waiting.
		await changes.Run(Plans.UpdateBehind(world.Plugin("obsidian-git"), world.Shell.Data.Snapshot!));
		Assert.True(changes.ShowPanel);
		Assert.False(changes.ShowUndo);

		changes.Lines.Single().RemoveCommand.Execute(null);
		Assert.False(changes.ShowPanel);

		// Undo only: one compact row, and no lines.
		await world.Shell.ToggleDemoObsidian();
		await changes.Run(Plans.UpdateBehind(world.Plugin("obsidian-git"), world.Shell.Data.Snapshot!));
		Assert.True(changes.ShowPanel);
		Assert.True(changes.ShowUndo);
		Assert.Empty(changes.Lines);

		await changes.Undo();
		Assert.False(changes.ShowUndo);
		Assert.False(changes.ShowPanel);
	});

	[Fact]
	public Task Undo_follows_a_direct_update_survives_a_restart_and_puts_things_back() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		ChangesViewModel changes = world.Shell.Changes;
		string notes = world.VaultNamed("Notes (Vaults)").Path;
		Assert.False(changes.ShowUndo);

		await changes.Run(Plans.UpdateBehind(world.Plugin("obsidian-excalidraw-plugin"), world.Shell.Data.Snapshot!).First(c => c.TargetVault == notes));
		Assert.Equal("2.17.2", Fixture.VersionOf(notes, "obsidian-excalidraw-plugin"));
		Assert.True(changes.ShowUndo);
		Assert.Contains(Texts.Count(K.Plural.Updated, 1), changes.UndoText);

		var again = new ShellViewModel(world.Shell.Services);
		await again.StartAsync();
		Assert.NotNull(again.Changes.OlderUndo);

		await again.Changes.Undo();

		Assert.Equal("2.8.3", Fixture.VersionOf(notes, "obsidian-excalidraw-plugin"));
		Assert.Null(again.Changes.OlderUndo);
	});

	[Fact]
	public Task Undo_reports_lines_that_could_not_run_and_keeps_them() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		ChangesViewModel changes = world.Shell.Changes;
		string open = world.Vault(DemoName.Open);
		await world.Shell.ToggleDemoObsidian();
		await changes.Run(new SetPluginEnabled(open, "dataview", "Dataview", false));

		await world.Shell.ToggleDemoObsidian();
		await changes.Undo();

		Assert.True(changes.HasUndoProblem);
		Assert.True(changes.ShowUndo);
		Assert.DoesNotContain("dataview", CommunityPluginList.Read(open).Value!);

		await world.Shell.ToggleDemoObsidian();
		await changes.Undo();
		Assert.False(changes.HasUndoProblem);
		Assert.False(changes.ShowUndo);
		Assert.Contains("dataview", CommunityPluginList.Read(open).Value!);
	});

	[Fact]
	public Task Turning_a_plugin_off_runs_at_once_and_keeps_its_files() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		await world.Shell.ToggleDemoObsidian();
		world.Shell.ShowVault(world.VaultNamed(DemoName.Open).Id);
		VaultPluginRowVm row = world.Shell.VaultPage.PluginRows.First(r => r.Id == "dataview");

		row.Toggle.Invoke();
		Assert.True(row.IsWaiting);
		await world.Shell.Changes.WhenIdle;

		string open = world.Vault(DemoName.Open);
		Assert.DoesNotContain("dataview", CommunityPluginList.Read(open).Value!);
		Assert.True(File.Exists(Path.Combine(VaultLayout.PluginDir(open, "dataview"), VaultLayout.MainFile)));
		Assert.Equal(Texts.Get(K.Action_TurnOn), world.Shell.VaultPage.PluginRows.First(r => r.Id == "dataview").Toggle.Text);
	});

	[Fact]
	public Task Open_in_Obsidian_only_follows_a_click_and_encodes_the_name() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		Assert.Empty(world.Links.Opened);
		world.Shell.ShowVault(world.VaultNamed("Family").Id);
		world.Shell.MenuItems.First(m => m.Text == Texts.Get(K.Action_OpenInObsidian)).Invoke();
		Assert.Equal(["obsidian://open?vault=Family"], world.Links.Opened);
	});

	[Fact]
	public Task A_vault_page_shows_older_first_and_details_on_a_click() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		world.Shell.ShowVault(world.VaultNamed(DemoName.Open).Id);
		VaultPageViewModel page = world.Shell.VaultPage;

		VaultPluginRowVm git = page.OlderRows.First(r => r.Id == "obsidian-git");
		Assert.Equal("2.35.0 → 2.38.2", git.Version);
		Assert.True(git.Update.IsVisible);
		Assert.False(git.IsExpanded);

		git.ExpandCommand.Execute(null);
		Assert.True(git.IsExpanded);
		page.PluginRows.First().ExpandCommand.Execute(null);
		Assert.False(git.IsExpanded);
		Assert.True(page.PluginRows.First().IsExpanded);
	});
}

[Collection(AppCollection.Name)]
public sealed class HomeListTests
{
	[Fact]
	public Task The_list_is_one_sorted_list_of_short_lines() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		AttentionViewModel attention = world.Shell.Attention;

		List<AttentionKind> kinds = attention.Rows.Select(r => r.Kind).ToList();
		Assert.Equal(kinds.OrderBy(k => (int)k), kinds);
		Assert.Equal(AttentionKind.ListUnreadable, kinds[0]);

		AttentionRowVm git = attention.Rows.First(r => r.Line.Any(t => t.Text == "Git"));
		Assert.Contains(git.Line, s => s.Role == SegRole.Value && s.Text == "2.35.0 → 2.38.2");
		Assert.Contains(git.Line, s => s.Text == Texts.Format(K.Row_In, DemoName.Open));
		Assert.Equal(Texts.Get(K.Action_Update), git.Primary.Text);

		AttentionRowVm excalidraw = attention.Rows.First(r => r.Line.Any(t => t.Text == "Excalidraw"));
		Assert.Contains(excalidraw.Line, s => s.Role == SegRole.Value && s.Text == "→ 2.17.2");
		Assert.Equal(Texts.Format(K.Action_UpdateN, 2), excalidraw.Primary.Text);

		Assert.Contains(attention.Rows, r => r.Line.Any(s => s.Text == Texts.Get(K.Row_Differs)));
		Assert.Contains(attention.Rows, r => r.Line.Any(s => s.Text == Texts.Get(K.Row_ListUnreadable)));
		Assert.Equal(2, attention.Rows.Count(r => r.Line.Any(s => s.Text == Texts.Get(K.Row_PluginsOff))));
	});

	[Fact]
	public Task Only_one_row_is_open_at_a_time_and_it_stays_open_through_a_rebuild() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		AttentionViewModel attention = world.Shell.Attention;
		AttentionRowVm first = attention.Rows[0], second = attention.Rows[1];

		first.Toggle();
		Assert.True(first.IsExpanded);
		second.Toggle();
		Assert.False(first.IsExpanded);
		Assert.True(second.IsExpanded);

		await world.Shell.RescanAsync(log: false);
		Assert.Same(second, attention.Rows[1]);
		Assert.True(second.IsExpanded);

		second.Toggle();
		Assert.False(second.IsExpanded);
	});

	[Fact]
	public Task Keep_as_is_persists_and_Restore_brings_it_back() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		AttentionViewModel attention = world.Shell.Attention;
		int before = attention.Items.Count(i => !i.Ignored);
		AttentionRowVm row = attention.Rows[0];
		string key = row.Key;

		row.Keep.Invoke();

		Assert.Contains(key, world.Host.Database.Ignored());
		Assert.Equal(before - 1, attention.Items.Count(i => !i.Ignored));
		Assert.True(attention.HasIgnored);
		Assert.Equal(Texts.Count(K.Plural.Ignored, 1), attention.IgnoredLink);

		// Survives a restart.
		var again = new ShellViewModel(world.Shell.Services);
		await again.StartAsync();
		Assert.True(again.Attention.HasIgnored);

		again.Attention.ToggleIgnoredCommand.Execute(null);
		Assert.True(again.Attention.IgnoredVisible);
		again.Attention.IgnoredRows.Single().Primary.Invoke();
		Assert.DoesNotContain(key, world.Host.Database.Ignored());
		Assert.False(again.Attention.HasIgnored);
	});

	[Fact]
	public Task The_home_page_reports_each_kind_of_item_the_demo_holds() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		IReadOnlyList<AttentionItem> items = world.Shell.Attention.Items;
		Assert.Contains(items, i => i.Kind == AttentionKind.Older);
		Assert.Contains(items, i => i.Kind == AttentionKind.Differs && i.Plugin!.Id == "beautitab");
		Assert.Contains(items, i => i.Kind == AttentionKind.PluginsOff);
		Assert.Contains(items, i => i.Kind == AttentionKind.PluginsNeverEnabled);
		Assert.Contains(items, i => i.Kind == AttentionKind.ListUnreadable);
		Assert.DoesNotContain(items, i => i.Kind == AttentionKind.NewerOnline);
	});

	[Fact]
	public Task An_empty_list_says_one_calm_sentence() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		foreach (AttentionItem item in world.Shell.Attention.Items)
		{
			world.Host.Database.SetIgnored(item.Key, true);
		}

		world.Shell.Data.Ignored = world.Host.Database.Ignored();
		world.Shell.Rebuild();

		Assert.Empty(world.Shell.Attention.Rows);
		Assert.False(world.Shell.ShowUpdateAll);
		Assert.Equal(Texts.Get(K.Head_NothingNeeded), world.Shell.HeadTitle);
	});
}

[Collection(AppCollection.Name)]
public sealed class HeaderAndLogTests
{
	[Fact]
	public Task The_log_shows_its_latest_line_and_opens_and_closes() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		LogViewModel log = world.Shell.Log;
		Assert.False(log.IsExpanded);
		Assert.True(log.ShowLatest);

		log.Add(LogKind.Plain, "first");
		log.Add(LogKind.Problem, "second");
		Assert.Equal("second", log.LatestText);
		Assert.True(log.LatestIsProblem);

		log.Add(LogKind.Ok, "third");
		Assert.False(log.LatestIsProblem);
		Assert.True(log.LatestIsOk);

		log.ToggleCommand.Execute(null);
		Assert.True(log.IsExpanded);
		Assert.False(log.ShowLatest);
		Assert.Equal(Texts.Get(K.Log_Collapse), log.ToggleTip);

		log.ToggleCommand.Execute(null);
		Assert.False(log.IsExpanded);
		Assert.Equal(Texts.Get(K.Log_Expand), log.ToggleTip);
	});

	[Fact]
	public Task The_menu_holds_what_left_the_header() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		ShellViewModel shell = world.Shell;

		List<string> home = shell.MenuItems.Select(m => m.Text).ToList();
		Assert.Contains(Texts.Get(K.Head_CheckOnline), home);
		Assert.Contains(Texts.Get(K.Head_AllPlugins), home);
		Assert.Contains(Texts.Get(K.Language_English), home);
		Assert.Contains(Texts.Get(K.Language_Russian), home);
		Assert.Single(shell.MenuItems, m => m.IsCurrent);

		shell.ToggleMenuCommand.Execute(null);
		Assert.True(shell.IsMenuOpen);
		shell.MenuItems.First(m => m.Text == Texts.Get(K.Head_AllPlugins)).Invoke();
		Assert.False(shell.IsMenuOpen);
		Assert.True(shell.ShowMatrix);
		Assert.True(shell.CanGoBack);
		Assert.False(shell.ShowUpdateAll);

		shell.Back();
		Assert.True(shell.ShowAttentionList);
		Assert.False(shell.CanGoBack);

		shell.ShowProfiles();
		Assert.Contains(shell.MenuItems, m => m.Text == Texts.Get(K.Profile_Duplicate));
		Assert.False(shell.MenuItems.First(m => m.Text == Texts.Get(K.Profile_Delete)).IsEnabled);
	});

	[Fact]
	public Task Scan_time_lives_in_the_refresh_tooltip() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		Assert.Contains(Texts.Format(K.Head_Scanned, Formats.Time(world.Shell.Data.Snapshot!.ScannedAt)), world.Shell.ScanTip);
		Assert.Contains(Texts.Get(K.Head_NotCheckedOnline), world.Shell.ScanTip);
	});
}

[Collection(AppCollection.Name)]
public sealed class AddPluginTests
{
	[Fact]
	public Task The_filter_narrows_Enter_adds_the_first_and_the_picker_stays_open() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		ShellViewModel shell = world.Shell;
		shell.ShowVault(world.VaultNamed("Family").Id);
		shell.Picker.Toggle();
		Assert.True(shell.Picker.IsOpen);
		int focusRequests = 0;
		shell.Picker.RefocusFilter += () => focusRequests++;

		shell.Picker.Filter = "git";
		Assert.Equal(["obsidian-git"], shell.Picker.Rows.Select(r => r.Id));
		shell.Picker.AddFirst();

		Assert.True(shell.Picker.IsOpen);
		Assert.True(shell.Picker.Rows.Single().IsWaiting);
		Assert.Equal(1, focusRequests);
		CopyPlugin line = Assert.IsType<CopyPlugin>(Assert.Single(shell.Changes.Queue.Lines).Item);
		Assert.Equal(world.Vault("HyperionDocs"), line.SourceVault);
		Assert.False(line.CopySettings);

		// No Apply: it is already on its way and lands by itself.
		await shell.Changes.WhenIdle;
		Assert.Equal("2.38.2", Fixture.VersionOf(world.Vault("Family"), "obsidian-git"));
		Assert.Empty(shell.Picker.Rows);

		shell.Picker.CloseCommand.Execute(null);
		Assert.False(shell.Picker.IsOpen);
		Assert.Equal(string.Empty, shell.Picker.Filter);
	});

	[Fact]
	public Task Copy_settings_goes_on_the_line() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		ShellViewModel shell = world.Shell;
		shell.ShowVault(world.VaultNamed("ChronosDocs").Id);
		shell.Picker.Toggle();
		shell.Picker.CopySettings = true;
		shell.Picker.Rows.First(r => r.Id == "calendar").Add.Invoke();
		Assert.True(Assert.IsType<CopyPlugin>(Assert.Single(shell.Changes.Queue.Lines).Item).CopySettings);
		await shell.Changes.WhenIdle;
		Assert.True(File.Exists(Path.Combine(VaultLayout.PluginDir(world.Vault("ChronosDocs"), "calendar"), VaultLayout.DataFile)));
	});

	[Fact]
	public Task The_catalog_stays_out_until_a_check_filled_it_and_a_size_comes_before_the_line() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		ShellViewModel shell = world.Shell;
		shell.ShowVault(world.VaultNamed("Future").Id);
		shell.Picker.Toggle();
		Assert.False(shell.Picker.HasCatalog);
		Assert.Empty(world.Catalog.Calls);

		// Only "calendar" of the demo catalog has no copy anywhere once this copy is gone.
		Folder.Delete(VaultLayout.PluginDir(world.Vault(DemoName.Open), "calendar"));
		await shell.RescanAsync(log: false);
		await shell.Online.Start();
		await shell.Online.Confirm();
		Assert.True(shell.Picker.HasCatalog);

		PickerRowVm row = Assert.Single(shell.Picker.CatalogRows);
		Assert.Empty(shell.Changes.Queue.Lines);
		await shell.Online.AskSizeAsync(shell.Data.CatalogEntry("calendar")!, null);

		Assert.Contains(world.Catalog.Calls, c => c.StartsWith("files"));
		Assert.Contains(Formats.Size(742_357), row.Add.Text);
		Assert.Empty(shell.Changes.Queue.Lines);

		// Enter must not start anything from the catalog.
		shell.Picker.Filter = "calendar";
		shell.Picker.AddFirst();
		Assert.Empty(shell.Changes.Queue.Lines);
		Assert.DoesNotContain(world.Catalog.Calls, c => c.StartsWith("download"));

		// The button already shows the size, so the click runs the download.
		row.Add.Invoke();
		DownloadPlugin line = Assert.IsType<DownloadPlugin>(Assert.Single(shell.Changes.Queue.Lines).Item);
		Assert.Equal(742_357, line.SizeBytes);
		await shell.Changes.WhenIdle;
		Assert.Contains(world.Catalog.Calls, c => c.StartsWith("download"));
		Assert.Equal("1.5.10", Fixture.VersionOf(world.Vault("Future"), "calendar"));
	});

	[Fact]
	public Task All_plugins_cell_adds_with_keys_of_the_row() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		ShellViewModel shell = world.Shell;
		shell.ShowAll();
		Assert.Equal(shell.Vaults.Count, shell.Matrix.Columns.Count);

		MatrixRowVm row = shell.Matrix.Rows.First(r => r.Id == "calendar");
		MatrixCellVm cell = row.Cells.First(c => c.VaultId == world.VaultNamed("Family").Id);
		Assert.True(cell.CanAdd);

		cell.Add.Invoke();

		Assert.False(cell.CanAdd);
		Assert.Equal(VaultManager.App.Controls.StateKind.Waiting, cell.Glyph);
		await shell.Changes.WhenIdle;
		Assert.Equal("1.5.10", Fixture.VersionOf(world.Vault("Family"), "calendar"));
		Assert.Equal(VaultManager.App.Controls.StateKind.Match, shell.Matrix.Rows.First(r => r.Id == "calendar").Cells.First(c => c.VaultId == world.VaultNamed("Family").Id).Glyph);
		Assert.False(row.Cells.First(c => c.VaultId == world.VaultNamed(DemoName.Offline).Id).CanAdd);
		Assert.False(row.Cells.First(c => c.VaultId == world.VaultNamed(DemoName.Damaged).Id).CanAdd);
	});
}

[Collection(AppCollection.Name)]
public sealed class OnlineFlowTests
{
	[Fact]
	public Task Nothing_reaches_the_network_without_a_click() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		world.Shell.ShowAll();
		world.Shell.ShowProfiles();
		world.Shell.ShowVault(world.VaultNamed("Family").Id);
		world.Shell.Picker.Toggle();
		await world.Shell.RescanAsync(log: true);
		Assert.Empty(world.Catalog.Calls);
	});

	[Fact]
	public Task The_first_click_only_asks_the_size_and_Cancel_stops_there() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		OnlineViewModel online = world.Shell.Online;

		await online.Start();

		Assert.Equal(["size"], world.Catalog.Calls);
		Assert.Equal(OnlineStage.Question, online.Stage);
		Assert.Contains(Formats.Size(3_019_120), online.Message);
		Assert.Contains(Texts.Count(K.Plural.SmallRequests, 13), online.Message);

		online.CancelCommand.Execute(null);
		Assert.False(online.IsOpen);
		Assert.Equal(["size"], world.Catalog.Calls);
	});

	[Fact]
	public Task A_confirmed_check_shows_newer_versions_links_and_badges() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		ShellViewModel shell = world.Shell;

		await shell.Online.Start();
		await shell.Online.Confirm();

		Assert.Contains("catalog", world.Catalog.Calls);
		Assert.NotNull(shell.Data.CheckedAt);
		Assert.Contains(Texts.Format(K.Head_CheckedOnline, Formats.Time(shell.Data.CheckedAt!.Value)), shell.ScanTip);
		AttentionItem newer = Assert.Single(shell.Attention.Items, i => i.Kind == AttentionKind.NewerOnline);
		Assert.Equal("2.39.0", newer.OnlineVersion);

		shell.ShowAll();
		shell.Matrix.Rows.First(r => r.Id == "obsidian-git").Select();
		Assert.True(shell.Matrix.CatalogLink.IsVisible);
		shell.Matrix.CatalogLink.Invoke();
		Assert.Equal(["https://community.obsidian.md/plugins/obsidian-git"], world.Links.Opened);

		shell.Matrix.Rows.First(r => r.Id == "waypoint").Select();
		Assert.True(shell.Matrix.NotInCatalog);
		Assert.False(shell.Matrix.CatalogLink.IsVisible);

		// Remembered across a restart, with no network.
		int calls = world.Catalog.Calls.Count;
		var again = new ShellViewModel(shell.Services);
		await again.StartAsync();
		Assert.NotNull(again.Data.CheckedAt);
		Assert.Contains(again.Attention.Items, i => i.Kind == AttentionKind.NewerOnline);
		Assert.Equal(calls, world.Catalog.Calls.Count);
	});

	[Fact]
	public Task A_newer_version_names_its_size_before_it_can_be_queued() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		ShellViewModel shell = world.Shell;
		await shell.Online.Start();
		await shell.Online.Confirm();
		AttentionRowVm Row() => shell.Attention.Rows.First(r => r.Kind == AttentionKind.NewerOnline);

		Assert.Equal(Texts.Get(K.Offer_CheckSize), Row().Primary.Text);
		await shell.Online.AskSizeAsync(shell.Data.CatalogEntry("obsidian-git")!, "2.39.0");
		Assert.Contains(Formats.Size(742_357), Row().Primary.Text);
		Assert.Empty(shell.Changes.Queue.Lines);

		Row().Primary.Invoke();

		List<DownloadPlugin> lines = shell.Changes.Queue.Lines.Select(l => l.Item).OfType<DownloadPlugin>().ToList();
		Assert.Equal(3, lines.Count);
		Assert.All(lines, l => Assert.Equal("2.39.0", l.ToVersion));
		Assert.All(lines, l => Assert.Equal(742_357, l.SizeBytes));
		Assert.True(Row().IsWaiting);

		await shell.Changes.WhenIdle;
		Assert.Equal("2.39.0", Fixture.VersionOf(world.Vault("HyperionDocs"), "obsidian-git"));
		Assert.Equal(LineState.Waiting, Assert.Single(shell.Changes.Queue.Lines).State);
	});

	[Fact]
	public Task An_unreachable_catalog_says_so_and_changes_nothing() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		world.Catalog.Unreachable = true;

		await world.Shell.Online.Start();

		Assert.Equal(OnlineStage.Failed, world.Shell.Online.Stage);
		Assert.True(world.Shell.Online.IsOpen);
		Assert.Null(world.Shell.Data.CheckedAt);
		Assert.Empty(world.Host.Database.Catalog());
	});
}

[Collection(AppCollection.Name)]
public sealed class ProfileFlowTests
{
	[Fact]
	public Task The_built_in_profile_is_read_only_and_a_duplicate_is_editable_and_saved() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		ProfilesViewModel profiles = world.Shell.Profiles;
		world.Shell.ShowProfiles();
		Assert.True(profiles.IsBuiltIn);
		Assert.False(profiles.IsEditable);

		profiles.Duplicate();
		Assert.False(profiles.IsBuiltIn);
		Assert.True(profiles.IsEditable);
		string id = profiles.Selected!.Id;

		profiles.Name = "Mine";
		profiles.Description = "Mine, edited";
		profiles.NewFolder = "99 Extra";
		profiles.AddFolderCommand.Execute(null);
		profiles.NewPluginId = "calendar";
		profiles.AddPluginFromBoxCommand.Execute(null);
		profiles.VaultMap = false;
		profiles.PluginParts.First(p => p.Value == "waypoint").RemoveCommand.Execute(null);
		profiles.FolderParts.First(p => p.Value == "Inbox").RemoveCommand.Execute(null);
		await world.Shell.RefreshAuditsAsync();

		Profile saved = world.Host.Profiles.Find(id)!;
		Assert.Equal("Mine", saved.Name);
		Assert.Equal("Mine, edited", saved.Description);
		Assert.Contains("99 Extra", saved.Folders);
		Assert.DoesNotContain("Inbox", saved.Folders);
		Assert.Contains("calendar", saved.Plugins);
		Assert.DoesNotContain("waypoint", saved.Plugins);
		Assert.False(saved.VaultMap);
	});

	[Fact]
	public Task A_bad_folder_is_refused_with_a_sentence_and_nothing_is_saved() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		ProfilesViewModel profiles = world.Shell.Profiles;
		world.Shell.ShowProfiles();
		profiles.NewProfile();
		string id = profiles.Selected!.Id;

		profiles.NewFolder = "..\\outside";
		profiles.AddFolderCommand.Execute(null);

		Assert.True(profiles.HasProblem);
		Assert.Equal(EnumTexts.Of(ProfileProblem.BadFolder), profiles.Problem);
		Assert.Empty(world.Host.Profiles.Find(id)!.Folders);

		profiles.DoneEditing();
		Assert.False(profiles.HasProblem);
	});

	[Fact]
	public Task Delete_asks_first_and_unbinds_the_vaults() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		ProfilesViewModel profiles = world.Shell.Profiles;
		world.Shell.ShowProfiles();
		profiles.NewProfile();
		string id = profiles.Selected!.Id;
		string vault = world.VaultNamed("Family").Id;
		world.Host.Database.SetVaultProfile(vault, id);

		profiles.AskDelete();
		Assert.True(profiles.ConfirmingDelete);
		Assert.NotNull(world.Host.Profiles.Find(id));

		profiles.ConfirmDelete();

		Assert.Null(world.Host.Profiles.Find(id));
		Assert.Null(world.Host.Database.VaultProfile(vault));
		Assert.True(profiles.IsBuiltIn);
	});

	[Fact]
	public Task Export_then_import_makes_a_second_editable_profile() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		ProfilesViewModel profiles = world.Shell.Profiles;
		world.Shell.ShowProfiles();
		string file = Path.Combine(world.Root, "exported" + ProfileFile.Extension);
		world.Files.Export = file;

		await profiles.Export();
		Assert.True(File.Exists(file));

		world.Files.Import = file;
		await profiles.Import();

		Assert.False(profiles.IsBuiltIn);
		Assert.Equal("Tech Tea Studio 2", profiles.Selected!.Name);
		Assert.Equal(2, profiles.Items.Count);
	});

	[Fact]
	public Task Export_adds_the_extension_a_picker_left_out() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		world.Shell.ShowProfiles();
		world.Files.Export = Path.Combine(world.Root, "plain");

		await world.Shell.Profiles.Export();

		Assert.True(File.Exists(Path.Combine(world.Root, "plain" + ProfileFile.Extension)));
	});

	[Fact]
	public Task A_rejected_import_shows_its_reason_and_changes_nothing() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		ProfilesViewModel profiles = world.Shell.Profiles;
		world.Shell.ShowProfiles();
		string file = Path.Combine(world.Root, "bad" + ProfileFile.Extension);
		File.WriteAllText(file, """{"Format":99,"Id":"x","Name":"x"}""");
		world.Files.Import = file;

		await profiles.Import();

		Assert.True(profiles.HasProblem);
		Assert.Contains(EnumTexts.Of(ProfileProblem.NewerFormat), profiles.Problem);
		Assert.Single(profiles.Items);
		Assert.Single(world.Host.Profiles.List());
	});

	[Fact]
	public Task A_cancelled_picker_changes_nothing() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		world.Shell.ShowProfiles();
		await world.Shell.Profiles.Import();
		await world.Shell.Profiles.Export();
		Assert.False(world.Shell.Profiles.HasProblem);
		Assert.Single(world.Host.Profiles.List());
	});

	[Fact]
	public Task Each_vault_gets_one_plain_answer_and_the_fix_runs_on_a_click() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		ShellViewModel shell = world.Shell;
		shell.ShowProfiles();
		await shell.Profiles.RefreshAuditAsync();

		AuditRowVm Row(string name) => shell.Profiles.AuditRows.First(r => r.Name == name);
		Assert.Equal(Texts.Get(K.Audit_Matches), Row("HyperionDocs").Result);
		Assert.Equal(Texts.Get(K.Audit_NotUsed), Row("KnowledgeSnippets").Result);
		Assert.Equal(Texts.Get(K.Vault_Offline), Row(DemoName.Offline).Result);
		Assert.Equal(Texts.Count(K.Plural.VaultsMatch, 1), shell.HeadTitle);

		AuditRowVm chronos = Row("ChronosDocs");
		Assert.Equal(Texts.Get(K.Audit_NotUsed), chronos.Result);
		Assert.Contains(chronos.Lines, l => l.Contains("03 Plugins"));
		Assert.False(chronos.IsBound);
		Assert.False(chronos.IsExpanded);

		chronos.ToggleExpandCommand.Execute(null);
		Assert.True(chronos.IsExpanded);
		Row("Family").ToggleExpandCommand.Execute(null);
		Assert.False(chronos.IsExpanded);
		chronos.ToggleExpandCommand.Execute(null);

		// A vault bound to the profile says what is short, plainly, and shows on the home list too.
		world.Host.Database.SetVaultProfile(world.VaultNamed("ChronosDocs").Id, ProfileStore.BuiltInId);
		await shell.RefreshAuditsAsync();
		Assert.Equal(Texts.Format(K.Audit_SummaryMissing, Texts.Count(K.Plural.Folders, 1)), Row("ChronosDocs").Result);
		Assert.Contains(shell.Attention.Items, i => i.Kind == AttentionKind.ProfileGap && i.Vault!.DisplayName == "ChronosDocs");

		Row("ChronosDocs").Fix.Invoke();
		await shell.Changes.WhenIdle;
		await shell.RefreshAuditsAsync();

		Assert.True(Directory.Exists(Path.Combine(world.Vault("ChronosDocs"), "03 Plugins")));
		Assert.Equal(Texts.Get(K.Audit_Matches), Row("ChronosDocs").Result);
		Assert.DoesNotContain(shell.Attention.Items, i => i.Kind == AttentionKind.ProfileGap && i.Vault!.DisplayName == "ChronosDocs");
	});

	[Fact]
	public Task Use_this_profile_binds_and_runs_at_once() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		ShellViewModel shell = world.Shell;
		shell.ShowProfiles();
		await shell.Profiles.RefreshAuditAsync();

		shell.Profiles.AuditRows.First(r => r.Name == "ChronosDocs").Fix.Invoke();
		await shell.Changes.WhenIdle;
		await shell.RefreshAuditsAsync();

		Assert.Equal(ProfileStore.BuiltInId, world.Host.Database.VaultProfile(world.VaultNamed("ChronosDocs").Id));
		Assert.True(Directory.Exists(Path.Combine(world.Vault("ChronosDocs"), "03 Plugins")));
		Assert.True(shell.Profiles.AuditRows.First(r => r.Name == "ChronosDocs").IsBound);
	});

	[Fact]
	public Task The_editor_stays_behind_Edit() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		ShellViewModel shell = world.Shell;
		shell.ShowProfiles();
		Assert.False(shell.Profiles.IsEditing);
		Assert.False(shell.ShowProfileEdit);

		shell.Profiles.Duplicate();
		Assert.True(shell.Profiles.IsEditing);
		Assert.True(shell.ShowProfileDone);

		shell.Profiles.DoneEditing();
		Assert.False(shell.Profiles.IsEditing);
		Assert.True(shell.ShowProfileEdit);

		shell.Profiles.Edit();
		Assert.True(shell.Profiles.IsEditing);

		shell.Profiles.Selected = shell.Profiles.Items.First(i => i.Id == ProfileStore.BuiltInId);
		Assert.False(shell.Profiles.IsEditing);
		shell.Profiles.Edit();
		Assert.False(shell.Profiles.IsEditing);
	});

	[Fact]
	public Task Plugins_with_no_local_copy_are_named_as_unavailable() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		ShellViewModel shell = world.Shell;
		shell.ShowProfiles();
		shell.Profiles.Duplicate();
		shell.Profiles.NewPluginId = "not-on-this-pc";
		shell.Profiles.AddPluginFromBoxCommand.Execute(null);
		await shell.RefreshAuditsAsync();

		AuditRowVm row = shell.Profiles.AuditRows.First(r => r.Name == "Family");
		Assert.Contains(row.Lines, l => l == Texts.Format(K.Audit_Unavailable, "not-on-this-pc"));

		row.Fix.Invoke();
		Assert.DoesNotContain(shell.Changes.Queue.Lines, l => l.Item is CopyPlugin { PluginId: "not-on-this-pc" });
		await shell.Changes.WhenIdle;
		await shell.RefreshAuditsAsync();
		Assert.False(Directory.Exists(VaultLayout.PluginDir(world.Vault("Family"), "not-on-this-pc")));
	});
}

[Collection(AppCollection.Name)]
public sealed class NewVaultFlowTests
{
	[Fact]
	public Task Create_vault_runs_at_once_and_registration_waits_for_Obsidian_by_itself() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		ShellViewModel shell = world.Shell;
		shell.ShowNewVault();
		NewVaultViewModel vault = shell.NewVault;
		Assert.False(vault.CanAdd);
		Assert.Equal(EnumTexts.Of(NewVaultProblem.EmptyName), vault.Hint);
		Assert.Equal(Texts.Get(K.NewVault_Add), vault.AddText);

		vault.Parent = world.VaultsDir;
		vault.Name = "Fresh";
		await vault.ProbeAsync();
		Assert.True(vault.CanAdd);
		Assert.Contains("00 System", vault.Tree);
		Assert.Contains(Texts.Get(K.NewVault_TreeList), vault.Tree);

		await vault.AddAsync();

		string path = world.Vault("Fresh");
		Assert.True(Directory.Exists(Path.Combine(path, "00 System")));
		Assert.Contains(shell.Log.Entries, e => e.Text == Texts.Get(K.Log_NewVaultNoUndo));
		QueueLine register = Assert.Single(shell.Changes.Queue.Lines);
		Assert.IsType<RegisterVault>(register.Item);
		Assert.Equal(LineState.Waiting, register.State);
		Assert.Equal(ChangeFailure.ObsidianRunning, register.Failure);
		Assert.DoesNotContain(world.Host.Registry.Read().Value!, e => e.Path == path);
		Assert.Equal(string.Empty, vault.Name);

		// New vaults are not undone, and only the registration may still be pending.
		Assert.False(shell.Changes.ShowUndo);

		world.Process.IsRunning = false;
		await shell.WatchObsidianAsync();

		Assert.Contains(world.Host.Registry.Read().Value!, e => e.Path == path);
		Assert.Contains(shell.Vaults, v => v.Name == "Fresh");
		Assert.Empty(shell.Changes.Queue.Lines);
		Assert.Empty(world.Host.Database.PendingChanges());
	});

	[Fact]
	public Task The_preview_follows_the_profile_and_the_register_box() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		world.Shell.ShowNewVault();
		NewVaultViewModel vault = world.Shell.NewVault;
		vault.Parent = world.VaultsDir;
		vault.Name = "Fresh";
		await vault.ProbeAsync();

		vault.Choice = vault.Choices.Single(c => c.Id is null);
		Assert.Equal(Texts.Get(K.NewVault_NoPlugins), vault.PluginsText);
		Assert.DoesNotContain("00 System", vault.Tree);

		vault.Register = false;
		Assert.DoesNotContain(Texts.Get(K.NewVault_TreeList), vault.Tree);

		Task run = vault.AddAsync();
		CreateVault create = Assert.IsType<CreateVault>(Assert.Single(world.Shell.Changes.Queue.Lines).Item);
		Assert.Null(create.Profile);
		await run;
		Assert.True(Directory.Exists(VaultLayout.ConfigDir(world.Vault("Fresh"))));
		Assert.Contains(world.Shell.Log.Entries, e => e.Text == Texts.Get(K.Log_NewVaultNoUndo));
	});

	[Fact]
	public Task An_occupied_folder_and_a_taken_name_are_refused() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		world.Shell.ShowNewVault();
		NewVaultViewModel vault = world.Shell.NewVault;
		vault.Parent = world.VaultsDir;

		vault.Name = "Family";
		await vault.ProbeAsync();
		Assert.Equal(EnumTexts.Of(NewVaultProblem.NameTaken), vault.Hint);

		Directory.CreateDirectory(Path.Combine(world.VaultsDir, "Occupied"));
		File.WriteAllText(Path.Combine(world.VaultsDir, "Occupied", "note.md"), "x");
		vault.Name = "Occupied";
		await vault.ProbeAsync();
		Assert.Equal(EnumTexts.Of(NewVaultProblem.FolderNotEmpty), vault.Hint);
		Assert.False(vault.CanAdd);
	});

	[Fact]
	public Task In_a_demo_the_parent_folder_cannot_leave_the_demo() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		world.Shell.ShowNewVault();
		world.Shell.NewVault.Name = "Fresh";
		world.Shell.NewVault.Parent = Path.GetPathRoot(world.Root) + "outside-the-demo";
		await world.Shell.NewVault.ProbeAsync();
		Assert.Equal(EnumTexts.Of(NewVaultProblem.OutsideDemo), world.Shell.NewVault.Hint);
	});
}

[Collection(AppCollection.Name)]
public sealed class SettingsFlowTests
{
	static async Task<SettingsSyncViewModel> ComparedAsync(TestWorld world)
	{
		GalleryTests.SyncScene(world);
		world.Shell.ShowSettings();
		SettingsSyncViewModel sync = world.Shell.Settings;
		sync.Source = sync.Sources.First(s => s.Name == "ChronosDocs");
		sync.Target = sync.Targets.First(s => s.Name == "HyperionDocs");
		await sync.Compare();
		return sync;
	}

	static SyncLineVm Line(SettingsSyncViewModel sync, SyncDecision decision) => sync.Lines.First(l => l.Entry.Decision == decision);

	[Fact]
	public Task Decisions_come_from_Core_with_a_sentence_for_each() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		SettingsSyncViewModel sync = await ComparedAsync(world);

		foreach (SyncDecision decision in Enum.GetValues<SyncDecision>())
		{
			Assert.False(string.IsNullOrWhiteSpace(Line(sync, decision).Sentence));
		}

		Assert.Contains("apiKey", Line(sync, SyncDecision.BlockedSecret).Sentence);
		Assert.DoesNotContain("demo-not-a-real-key", Line(sync, SyncDecision.BlockedSecret).Sentence);
		Assert.False(Line(sync, SyncDecision.BlockedSecret).CanSelect);
		Assert.True(Line(sync, SyncDecision.Copy).IsSelected);
		Assert.False(Line(sync, SyncDecision.KeepTarget).IsSelected);
		Assert.False(Line(sync, SyncDecision.Same).CanSelect);
	});

	[Fact]
	public Task Only_copy_lines_and_conflicts_taken_from_the_source_run_and_the_button_says_how_many() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		world.Process.IsRunning = false;
		SettingsSyncViewModel sync = await ComparedAsync(world);
		string target = VaultLayout.ConfigDir(world.Vault("HyperionDocs"));
		string appBefore = File.ReadAllText(Path.Combine(target, "app.json"));
		int copies = sync.Lines.Count(l => l.Entry.Decision == SyncDecision.Copy);
		Assert.Equal(Texts.Count(K.Plural.CopyFiles, copies), sync.AddText);

		Line(sync, SyncDecision.Conflict).TakesSource = true;
		Assert.Equal(Texts.Count(K.Plural.CopyFiles, copies + 1), sync.AddText);

		await sync.AddAsync();

		Assert.True(File.Exists(Path.Combine(target, "hotkeys.json")));
		Assert.Equal(File.ReadAllText(Path.Combine(VaultLayout.ConfigDir(world.Vault("ChronosDocs")), "appearance.json")), File.ReadAllText(Path.Combine(target, "appearance.json")));
		Assert.False(File.Exists(Path.Combine(target, "snippets", "keys.json")));
		Assert.Equal(appBefore, File.ReadAllText(Path.Combine(target, "app.json")));

		// The table was asked again and shows what is true now.
		Assert.Equal(SyncDecision.Same, sync.Lines.First(l => l.Entry.RelativePath == "hotkeys.json").Entry.Decision);
		Assert.Equal(Texts.Get(K.Sync_Add), sync.AddText);
		Assert.False(sync.CanAdd);
	});

	[Fact]
	public Task Copy_settings_opens_with_this_vault_as_target_and_Back_returns_to_it() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		ShellViewModel shell = world.Shell;
		VaultState vault = world.VaultNamed("HyperionDocs");
		shell.ShowVault(vault.Id);

		shell.MenuItems.First(m => m.Text == Texts.Get(K.Menu_CopySettings)).Invoke();

		SettingsSyncViewModel sync = shell.Settings;
		Assert.True(shell.IsSettingsView);
		Assert.True(sync.HasFixedTarget);
		Assert.Equal(vault.Id, sync.Target!.Id);
		Assert.DoesNotContain(sync.Sources, s => s.Id == vault.Id);
		Assert.False(sync.Categories.Single(c => c.Category == SettingsCategory.PluginSettings).IsOn);
		Assert.Equal(Texts.Format(K.Sync_TitleTo, vault.DisplayName), shell.HeadTitle);
		Assert.True(shell.CanGoBack);

		shell.Back();

		Assert.True(shell.IsVaultView);
		Assert.Equal(vault.Id, shell.CurrentVaultId);
	});

	[Fact]
	public Task An_offline_vault_offers_no_settings_copy() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		world.Shell.ShowVault(world.VaultNamed(DemoName.Offline).Id);
		Assert.False(world.Shell.MenuItems.First(m => m.Text == Texts.Get(K.Menu_CopySettings)).IsEnabled);
	});

	[Fact]
	public Task Keeping_the_target_is_remembered_by_the_next_Compare() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		SettingsSyncViewModel sync = await ComparedAsync(world);
		SyncLineVm conflict = Line(sync, SyncDecision.Conflict);
		string file = conflict.Entry.RelativePath;
		foreach (SyncLineVm line in sync.Lines.Where(l => l.CanSelect))
		{
			line.IsSelected = false;
		}

		conflict.KeepsTarget = true;
		Assert.True(sync.CanAdd);
		sync.Add();

		await sync.Compare();
		Assert.Equal(SyncDecision.KeepTarget, sync.Lines.First(l => l.Entry.RelativePath == file).Entry.Decision);
	});

	[Fact]
	public Task A_settings_copy_for_an_open_vault_waits_and_changing_a_vault_drops_the_table() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		GalleryTests.SyncScene(world);
		world.Shell.ShowSettings();
		SettingsSyncViewModel sync = world.Shell.Settings;
		sync.Source = sync.Sources.First(s => s.Name == "ChronosDocs");
		sync.Target = sync.Targets.First(s => s.Name == DemoName.Open);
		await sync.Compare();

		await sync.AddAsync();

		Assert.True(Line(sync, SyncDecision.Copy).IsWaiting);
		Assert.All(world.Shell.Changes.Queue.Lines, l => Assert.Equal(LineState.Waiting, l.State));
		Assert.False(File.Exists(Path.Combine(VaultLayout.ConfigDir(world.Vault(DemoName.Open)), "hotkeys.json")));

		world.Process.IsRunning = false;
		await world.Shell.WatchObsidianAsync();
		Assert.True(File.Exists(Path.Combine(VaultLayout.ConfigDir(world.Vault(DemoName.Open)), "hotkeys.json")));

		sync.Target = sync.Targets.First(t => t.Name == "Family");
		Assert.Empty(sync.Lines);
		Assert.False(sync.Compared);
		Assert.False(sync.CanAdd);
	});
}

[Collection(AppCollection.Name)]
public sealed class LanguageFlowTests
{
	[Fact]
	public Task A_switch_translates_keeps_the_queue_and_is_remembered() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		ShellViewModel shell = world.Shell;
		shell.Matrix.Filter = "git";
		await shell.Changes.Run(Plans.UpdateBehind(world.Plugin("obsidian-git"), shell.Data.Snapshot!));
		ChangeLineVm line = shell.Changes.Lines.Single();
		string english = line.Reason;
		int rebuilds = 0;
		shell.LanguageChanged += () => rebuilds++;

		shell.UseLanguage(AppLanguage.Russian);

		Assert.Equal(1, rebuilds);
		Assert.Equal(AppLanguage.Russian, Texts.Current);
		Assert.Single(shell.Changes.Queue.Lines);
		Assert.Same(line, shell.Changes.Lines.Single());
		Assert.NotEqual(english, line.Reason);
		Assert.Equal("git", shell.Matrix.Filter);
		Assert.Equal("ru", world.Host.Database.Setting(Texts.SettingKey));

		Texts.Use(AppLanguage.English);
		ShellViewModel.LoadLanguage(world.Host.Database);
		Assert.Equal(AppLanguage.Russian, Texts.Current);
	});

	[Fact]
	public void A_count_picks_the_form_for_its_number()
	{
		Texts.Use(AppLanguage.Russian);
		try
		{
			Assert.Equal("1 плагин", Texts.Count(K.Plural.Plugins, 1));
			Assert.Equal("3 плагина", Texts.Count(K.Plural.Plugins, 3));
			Assert.Equal("11 плагинов", Texts.Count(K.Plural.Plugins, 11));
			Assert.Equal("21 плагин", Texts.Count(K.Plural.Plugins, 21));
		}
		finally
		{
			Texts.Use(AppLanguage.English);
		}

		Assert.Equal("1 plugin", Texts.Count(K.Plural.Plugins, 1));
		Assert.Equal("2 plugins", Texts.Count(K.Plural.Plugins, 2));
	}
}

[Collection(AppCollection.Name)]
public sealed class DemoSessionTests
{
	[Fact]
	public void A_demo_session_lives_in_its_folder_and_takes_it_along_on_exit()
	{
		string root = AppSession.NewDemoRoot();
		var session = AppSession.ForDemo(root);
		Assert.StartsWith(root, session.Host.Paths.DatabaseFile);
		Assert.StartsWith(root, session.Host.Paths.ObsidianConfigDir);
		Assert.NotNull(session.Demo);
		Assert.IsType<VaultManager.Core.Demo.DemoCatalog>(session.Host.Catalog);
		Assert.True(Directory.Exists(root));

		session.Dispose();

		Assert.False(Directory.Exists(root));
	}

	[Fact]
	public async Task The_demo_file_picker_never_answers_outside_its_folder()
	{
		string root = Path.Combine(Path.GetTempPath(), "vm-demo-picker-" + Guid.NewGuid().ToString("N"));
		var picker = new DemoFilePicker(root);
		try
		{
			Assert.StartsWith(root, await picker.PickFolderAsync());
			Assert.Null(await picker.PickProfileToImportAsync());
			string export = (await picker.PickProfileToExportAsync("a" + ProfileFile.Extension))!;
			Assert.StartsWith(root, export);
			File.WriteAllText(export, "{}");
			Assert.Equal(export, await picker.PickProfileToImportAsync());
		}
		finally
		{
			Folder.Delete(root);
		}
	}
}
