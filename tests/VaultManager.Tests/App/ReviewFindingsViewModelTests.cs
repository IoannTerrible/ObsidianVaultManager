using VaultManager.App.Logic;
using VaultManager.App.ViewModels;
using VaultManager.Core.Changes;
using VaultManager.Core.Obsidian;
using VaultManager.Core.Sync;

namespace VaultManager.Tests.App;

/// <summary>What an independent review of v0.6.0.0 proved could go wrong in the window's logic.</summary>
[Collection(AppCollection.Name)]
public sealed class ReviewFindingsViewModelTests
{
	[Fact]
	public Task Plugin_settings_are_not_part_of_a_sync_until_asked_for() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		Assert.False(world.Shell.Settings.Categories.Single(c => c.Category == SettingsCategory.PluginSettings).IsOn);
		Assert.All(world.Shell.Settings.Categories.Where(c => c.Category != SettingsCategory.PluginSettings), c => Assert.True(c.IsOn));
	});

	[Fact]
	public Task A_compare_for_vaults_no_longer_selected_is_thrown_away() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		SettingsSyncViewModel sync = world.Shell.Settings;
		world.Shell.ShowSettings();
		sync.Source = sync.Sources.First(s => s.Name == "ChronosDocs");
		sync.Target = sync.Targets.First(t => t.Name == "HyperionDocs");

		Task compare = sync.Compare();
		sync.Target = sync.Targets.First(t => t.Name == "Family");
		await compare;

		Assert.False(sync.Compared);
		Assert.Empty(sync.Lines);
	});

	[Fact]
	public Task Keeping_the_target_alone_can_be_answered_and_is_remembered() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		SettingsSyncViewModel sync = world.Shell.Settings;
		world.Shell.ShowSettings();
		sync.Source = sync.Sources.First(s => s.Name == "ChronosDocs");
		sync.Target = sync.Targets.First(t => t.Name == "HyperionDocs");
		await sync.Compare();
		SyncLineVm conflict = sync.Lines.First(l => l.Entry.Decision == SyncDecision.Conflict);
		string path = conflict.Entry.RelativePath;
		foreach (SyncLineVm line in sync.Lines)
		{
			line.IsSelected = false;
		}

		conflict.KeepsTarget = true;
		Assert.True(sync.CanAdd);
		sync.Add();
		await sync.Compare();

		Assert.Equal(SyncDecision.KeepTarget, sync.Lines.First(l => l.Entry.RelativePath == path).Entry.Decision);
		Assert.Empty(world.Shell.Changes.Queue.Lines);
	});

	[Fact]
	public Task Renewing_a_line_takes_the_newest_source_now() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		WorkspaceSnapshot snapshot = world.Shell.Data.Snapshot!;
		var line = new CopyPlugin(world.Vault("Notes"), "obsidian-excalidraw-plugin", "Excalidraw", "2.0.0", "2.15.2", world.Vault(DemoWorkspaceNames.Open), false, "stale");

		var renewed = Assert.IsType<CopyPlugin>(QueueRules.Renew(line, snapshot));

		Assert.Equal("2.17.2", renewed.ToVersion);
		Assert.Equal(world.Vault("Family"), renewed.SourceVault);
		Assert.Equal("2.8.3", renewed.FromVersion);
	});

	[Fact]
	public Task Renewing_never_offers_a_downgrade() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		WorkspaceSnapshot snapshot = world.Shell.Data.Snapshot!;

		// Family moved on to the newest copy while this line waited: there is nothing left to do.
		var copy = new CopyPlugin(world.Vault("Family"), "obsidian-excalidraw-plugin", "Excalidraw", "2.0.0", "2.15.2", world.Vault(DemoWorkspaceNames.Open), false, "stale");
		Assert.Null(QueueRules.Renew(copy, snapshot));

		var download = new DownloadPlugin(world.Vault("HyperionDocs"), "obsidian-git", "Git", "2.0.0", "2.30.0", "demo/git", 100, "stale");
		Assert.Null(QueueRules.Renew(download, snapshot));
	});
}

static class DemoWorkspaceNames
{
	public const string Open = VaultManager.Core.Demo.DemoWorkspace.OpenVault;
}
