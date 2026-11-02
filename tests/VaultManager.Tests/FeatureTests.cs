using System.Net;
using Newtonsoft.Json.Linq;
using VaultManager.Core;
using VaultManager.Core.Catalog;
using VaultManager.Core.Changes;
using VaultManager.Core.Demo;
using VaultManager.Core.Foundation;
using VaultManager.Core.Obsidian;
using VaultManager.Core.Profiles;
using VaultManager.Core.Store;
using VaultManager.Core.Sync;

namespace VaultManager.Tests;

public sealed class UndoTests
{
	static CopyPlugin Copy(string target, string source, string id, string? from, string to) =>
		new(target, id, id, from, to, source, false, Fixture.MainHash(target, id));

	[Fact]
	public async Task Undo_puts_back_the_old_version_settings_and_list_after_a_restart()
	{
		using var f = new Fixture();
		string a = f.Vault("a"), b = f.Vault("b");
		Fixture.Plugin(a, "p", "2.0.0");
		Fixture.Plugin(b, "p", "1.0.0", listed: false, data: """{"mine":1}""");
		await f.Host().Executor.ApplyAsync([Copy(b, a, "p", "1.0.0", "2.0.0")]);
		Assert.Equal("2.0.0", Fixture.VersionOf(b, "p"));

		// A fresh host stands in for restarting the app.
		var report = f.Host().Executor.UndoLast();

		Assert.All(report, r => Assert.Equal(ChangeFailure.None, r.Failure));
		Assert.Equal("1.0.0", Fixture.VersionOf(b, "p"));
		Assert.Contains("mine", File.ReadAllText(Path.Combine(VaultLayout.PluginDir(b, "p"), VaultLayout.DataFile)));
		Assert.DoesNotContain("p", CommunityPluginList.Read(b).Value!);
		Assert.Null(f.Host().Database.LastUndoable());
	}

	[Fact]
	public async Task Undo_of_an_added_plugin_removes_it()
	{
		using var f = new Fixture();
		string a = f.Vault("a"), b = f.Vault("b");
		Fixture.Plugin(a, "p", "1.0.0");
		await f.Host().Executor.ApplyAsync([Copy(b, a, "p", null, "1.0.0")]);

		f.Host().Executor.UndoLast();

		Assert.False(Directory.Exists(VaultLayout.PluginDir(b, "p")));
		Assert.Empty(CommunityPluginList.Read(b).Value!);
	}

	[Fact]
	public async Task A_new_vault_alone_offers_no_undo()
	{
		using var f = new Fixture();
		await f.Host().Executor.ApplyAsync([new CreateVault(f.Root, "fresh", null, new Dictionary<string, string>())]);
		Assert.Null(f.Host().Database.LastUndoable());
		Assert.Empty(f.Host().Executor.UndoLast());
	}

	[Fact]
	public async Task Undo_waits_for_an_open_vault_and_can_be_retried()
	{
		using var f = new Fixture();
		string a = f.Vault("a"), b = f.Vault("b");
		Fixture.Plugin(a, "p", "2.0.0");
		Fixture.Plugin(b, "p", "1.0.0");
		f.Register(("1", a, false), ("2", b, true));
		await f.Host().Executor.ApplyAsync([Copy(b, a, "p", "1.0.0", "2.0.0")]);

		f.Process.IsRunning = true;
		Assert.Equal(ChangeFailure.VaultOpen, f.Host().Executor.UndoLast().Single().Failure);
		Assert.Equal("2.0.0", Fixture.VersionOf(b, "p"));
		Assert.NotNull(f.Host().Database.LastUndoable());

		f.Process.IsRunning = false;
		f.Host().Executor.UndoLast();
		Assert.Equal("1.0.0", Fixture.VersionOf(b, "p"));
	}

	[Fact]
	public async Task Old_snapshots_are_pruned_with_their_operations()
	{
		using var f = new Fixture();
		string a = f.Vault("a"), b = f.Vault("b");
		Fixture.Plugin(a, "p", "1.0.0");
		var executor = new ChangeExecutor(f.Host().Database, f.Host().Registry, f.Process, new DemoCatalog(), f.Paths.SnapshotsDir, keepOperations: 2);
		for (int i = 0; i < 4; i++)
		{
			await executor.ApplyAsync([new SetPluginEnabled(b, "p", "p", i % 2 == 0)]);
			await Task.Delay(5);
		}

		Fixture.Plugin(b, "p", "1.0.0");
		await executor.ApplyAsync([Copy(b, a, "p", "1.0.0", "1.0.0")]);
		await executor.ApplyAsync([Copy(b, a, "p", "1.0.0", "1.0.0")]);
		await executor.ApplyAsync([Copy(b, a, "p", "1.0.0", "1.0.0")]);
		Assert.Equal(2, Directory.GetDirectories(f.Paths.SnapshotsDir).Length);
	}

	[Fact]
	public void A_database_from_a_newer_build_is_refused_rather_than_guessed_at()
	{
		using var f = new Fixture();
		string file = Path.Combine(f.Root, "x.db");
		_ = new AppDatabase(file);
		using (var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={file};Pooling=False"))
		{
			c.Open();
			using var cmd = c.CreateCommand();
			cmd.CommandText = "PRAGMA user_version = 99;";
			cmd.ExecuteNonQuery();
		}

		Assert.Throws<InvalidOperationException>(() => new AppDatabase(file));
	}
}

public sealed class ProfileTests
{
	[Fact]
	public void The_built_in_profile_matches_the_studio_rules()
	{
		Profile p = ProfileStore.BuiltIn();
		Assert.True(p.IsBuiltIn);
		Assert.Equal(["dataview", "folder-notes", "metadata-menu", "obsidian-git", "obsidian-linter", "omnisearch", "templater-obsidian", "waypoint"], p.Plugins);
		Assert.Contains("07 Meetings", p.Folders);
		Assert.Equal(ProfileProblem.None, ProfileValidator.Check(p));
	}

	[Fact]
	public void Export_then_import_gives_an_editable_copy_and_never_overwrites()
	{
		using var f = new Fixture();
		ProfileStore store = f.Host().Profiles;
		string file = Path.Combine(f.Root, "exported" + ProfileFile.Extension);
		store.Export(ProfileStore.BuiltIn(), file);

		Profile first = store.Import(file);
		Profile second = store.Import(file);

		Assert.False(first.IsBuiltIn);
		Assert.NotEqual(ProfileStore.BuiltInId, first.Id);
		Assert.NotEqual(first.Id, second.Id);
		Assert.Equal("Tech Tea Studio 2", first.Name);
		Assert.Equal("Tech Tea Studio 3", second.Name);
		Assert.Equal(3, store.List().Count);
		Assert.Equal(ProfileStore.BuiltIn().Plugins, first.Plugins);
	}

	[Theory]
	[InlineData("""{"Format":1,"Id":"x","Name":"x","Folders":["../../Windows"]}""", ProfileProblem.BadFolder)]
	[InlineData("""{"Format":1,"Id":"x","Name":"x","Folders":["C:\\Windows"]}""", ProfileProblem.BadFolder)]
	[InlineData("""{"Format":1,"Id":"x","Name":"x","Folders":[".obsidian/plugins"]}""", ProfileProblem.BadFolder)]
	[InlineData("""{"Format":1,"Id":"x","Name":"x","Folders":["CON"]}""", ProfileProblem.BadFolder)]
	[InlineData("""{"Format":1,"Id":"x","Name":"x","Plugins":["../evil"]}""", ProfileProblem.BadPluginId)]
	[InlineData("""{"Format":1,"Id":"x","Name":"x","Settings":{"../../evil.json":{}}}""", ProfileProblem.BadSettingsFile)]
	[InlineData("""{"Format":1,"Id":"x","Name":"x","Settings":{"community-plugins.json":[]}}""", ProfileProblem.BadSettingsFile)]
	[InlineData("""{"Format":1,"Id":"x","Name":"x","Templates":{"..\\evil.md":"x"}}""", ProfileProblem.BadTemplate)]
	[InlineData("""{"Format":1,"Id":"x","Name":"x","Settings":{"app.json":{"apiKey":"sk-1234567890"}}}""", ProfileProblem.SecretInSettings)]
	[InlineData("""{"Format":99,"Id":"x","Name":"x"}""", ProfileProblem.NewerFormat)]
	[InlineData("""{"Format":1,"Id":"x","Name":" "}""", ProfileProblem.MissingName)]
	[InlineData("""{ cut""", ProfileProblem.NotJson)]
	public void A_bad_profile_file_is_rejected_with_its_reason(string json, ProfileProblem expected)
	{
		ProfileRejectedException ex = Assert.Throws<ProfileRejectedException>(() => ProfileFile.Parse(json));
		Assert.Equal(expected, ex.Problem);
	}

	[Fact]
	public void An_oversized_file_is_rejected_before_it_is_read()
	{
		using var f = new Fixture();
		string file = Path.Combine(f.Root, "big.json");
		File.WriteAllText(file, new string(' ', ProfileValidator.MaxFileBytes + 1));
		Assert.Equal(ProfileProblem.TooLarge, Assert.Throws<ProfileRejectedException>(() => f.Host().Profiles.Import(file)).Problem);
	}

	[Fact]
	public void The_built_in_profile_cannot_be_saved_over_or_deleted()
	{
		using var f = new Fixture();
		ProfileStore store = f.Host().Profiles;
		Assert.Throws<InvalidOperationException>(() => store.Save(ProfileStore.BuiltIn()));
		Assert.Throws<InvalidOperationException>(() => store.Delete(ProfileStore.BuiltInId));
	}

	[Fact]
	public void A_saved_profile_can_be_edited_and_deleted()
	{
		using var f = new Fixture();
		ProfileStore store = f.Host().Profiles;
		Profile copy = store.Duplicate(ProfileStore.BuiltIn(), "My layout");
		store.Save(copy with { Plugins = ["dataview"] });
		Assert.Equal(["dataview"], store.Find(copy.Id)!.Plugins);
		store.Delete(copy.Id);
		Assert.Null(store.Find(copy.Id));
	}

	[Fact]
	public async Task A_new_vault_gets_folders_notes_templates_plugins_and_no_plugin_settings()
	{
		using var f = new Fixture();
		string source = f.Vault("source");
		Fixture.Plugin(source, "dataview", "0.5.68", data: """{"from":"source"}""");
		Profile profile = ProfileStore.BuiltIn();

		var item = new CreateVault(f.Root, "NewDocs", profile, new Dictionary<string, string> { ["dataview"] = source });
		ChangeResult result = (await f.Host().Executor.ApplyAsync([item])).Results.Single();

		Assert.Equal(ChangeStatus.Done, result.Status);
		string vault = item.NewVaultPath;
		Assert.True(File.Exists(Path.Combine(vault, "00 System", "Vault Map.md")));
		Assert.True(File.Exists(Path.Combine(vault, "06 Decisions", "06 Decisions.md")));
		Assert.True(File.Exists(Path.Combine(vault, "00 System", "Templates", "Decision.md")));
		Assert.True(File.Exists(Path.Combine(VaultLayout.ConfigDir(vault), "core-plugins.json")));
		Assert.Equal(["dataview"], CommunityPluginList.Read(vault).Value);
		Assert.False(File.Exists(Path.Combine(VaultLayout.PluginDir(vault, "dataview"), VaultLayout.DataFile)));
		Assert.StartsWith("---", File.ReadAllText(Path.Combine(vault, "06 Decisions", "06 Decisions.md")));
	}

	[Fact]
	public async Task A_folder_that_already_holds_files_is_never_used_for_a_new_vault()
	{
		using var f = new Fixture();
		string taken = Path.Combine(f.Root, "taken");
		Directory.CreateDirectory(taken);
		File.WriteAllText(Path.Combine(taken, "note.md"), "mine");

		ChangeResult result = (await f.Host().Executor.ApplyAsync([new CreateVault(f.Root, "taken", null, new Dictionary<string, string>())])).Results.Single();

		Assert.Equal(ChangeFailure.TargetNotEmpty, result.Failure);
		Assert.False(Directory.Exists(VaultLayout.ConfigDir(taken)));
	}

	[Fact]
	public async Task Registration_waits_while_Obsidian_runs_and_lands_after()
	{
		using var f = new Fixture();
		string vault = f.Vault("new");
		f.Register();
		var register = new RegisterVault(vault);
		f.Process.IsRunning = true;
		Assert.Equal(ChangeFailure.ObsidianRunning, (await f.Host().Executor.ApplyAsync([register])).Results.Single().Failure);
		Assert.Empty(f.Host().Registry.Read().Value!);

		f.Process.IsRunning = false;
		Assert.Equal(ChangeStatus.Done, (await f.Host().Executor.ApplyAsync([register])).Results.Single().Status);
		Assert.Single(f.Host().Registry.Read().Value!);
	}

	[Fact]
	public async Task Adding_profile_parts_keeps_existing_notes_and_undo_removes_only_what_it_added()
	{
		using var f = new Fixture();
		string v = f.Vault("v");
		Directory.CreateDirectory(Path.Combine(v, "00 System"));
		string map = Path.Combine(v, "00 System", "Vault Map.md");
		File.WriteAllText(map, "mine");
		string note = Path.Combine(v, "06 Decisions", "keep.md");
		Directory.CreateDirectory(Path.GetDirectoryName(note)!);
		File.WriteAllText(note, "keep");

		await f.Host().Executor.ApplyAsync([new AddProfileParts(v, ProfileStore.BuiltIn())]);
		Assert.Equal("mine", File.ReadAllText(map));
		Assert.True(File.Exists(Path.Combine(v, "03 Plugins", "03 Plugins.md")));

		f.Host().Executor.UndoLast();
		Assert.False(Directory.Exists(Path.Combine(v, "03 Plugins")));
		Assert.Equal("keep", File.ReadAllText(note));
		Assert.Equal("mine", File.ReadAllText(map));
	}

	[Fact]
	public void Audit_says_exactly_what_is_missing()
	{
		using var f = new Fixture();
		string v = f.Vault("v");
		Directory.CreateDirectory(Path.Combine(v, "00 System"));
		ProfileAudit audit = ProfileAudit.For(new Profile { Id = "x", Name = "x", Plugins = ["a", "b"], Folders = ["00 System", "01 Architecture"], VaultMap = true }, v, ["a", "other"]);
		Assert.Equal(["b"], audit.MissingPlugins);
		Assert.Equal(["other"], audit.OtherPlugins);
		Assert.Equal(["01 Architecture"], audit.MissingFolders);
		Assert.True(audit.VaultMapMissing);
		Assert.False(audit.IsComplete);
	}
}

public sealed class SyncTests
{
	[Theory]
	[InlineData("s", "s", null, false, SyncDecision.Same)]
	[InlineData("s", null, null, false, SyncDecision.Copy)]
	[InlineData("s", "t", null, false, SyncDecision.Conflict)]
	[InlineData("s2", "b", "b", false, SyncDecision.Copy)]
	[InlineData("b", "t2", "b", false, SyncDecision.KeepTarget)]
	[InlineData("s2", "t2", "b", false, SyncDecision.Conflict)]
	[InlineData("s", "t", null, true, SyncDecision.BlockedSecret)]
	[InlineData("s", "s", null, true, SyncDecision.Same)]
	public void The_three_way_table(string source, string? target, string? baseHash, bool secret, SyncDecision expected) =>
		Assert.Equal(expected, SettingsSync.Decide(source, target, baseHash, secret));

	[Fact]
	public async Task After_a_sync_the_next_preview_knows_who_moved()
	{
		using var f = new Fixture();
		string a = f.Vault("a"), b = f.Vault("b");
		string fileA = Path.Combine(VaultLayout.ConfigDir(a), "hotkeys.json"), fileB = Path.Combine(VaultLayout.ConfigDir(b), "hotkeys.json");
		File.WriteAllText(fileA, "{\"k\":1}");
		File.WriteAllText(fileB, "{\"k\":0}");
		CoreHost host = f.Host();
		SettingsCategory[] hotkeys = [SettingsCategory.Hotkeys];

		SyncEntry first = host.Sync.Preview(a, b, hotkeys).Single();
		Assert.Equal(SyncDecision.Conflict, first.Decision);
		await host.Executor.ApplyAsync([SettingsSync.ToChange(a, b, first)]);
		Assert.Equal(SyncDecision.Same, host.Sync.Preview(a, b, hotkeys).Single().Decision);

		File.WriteAllText(fileB, "{\"k\":2}");
		Assert.Equal(SyncDecision.KeepTarget, host.Sync.Preview(a, b, hotkeys).Single().Decision);

		File.WriteAllText(fileB, "{\"k\":1}");
		File.WriteAllText(fileA, "{\"k\":3}");
		Assert.Equal(SyncDecision.Copy, host.Sync.Preview(a, b, hotkeys).Single().Decision);

		File.WriteAllText(fileB, "{\"k\":4}");
		Assert.Equal(SyncDecision.Conflict, host.Sync.Preview(a, b, hotkeys).Single().Decision);
	}

	[Fact]
	public async Task A_settings_copy_can_be_undone_and_a_changed_target_is_not_overwritten()
	{
		using var f = new Fixture();
		string a = f.Vault("a"), b = f.Vault("b");
		string fileA = Path.Combine(VaultLayout.ConfigDir(a), "app.json"), fileB = Path.Combine(VaultLayout.ConfigDir(b), "app.json");
		File.WriteAllText(fileA, "{\"a\":1}");
		File.WriteAllText(fileB, "{\"b\":1}");
		CoreHost host = f.Host();
		SyncEntry entry = host.Sync.Preview(a, b, [SettingsCategory.Editor]).Single();
		CopySettingsFile change = SettingsSync.ToChange(a, b, entry);

		File.WriteAllText(fileB, "{\"b\":2}");
		Assert.Equal(ChangeFailure.TargetChanged, (await host.Executor.ApplyAsync([change])).Results.Single().Failure);
		Assert.Equal("{\"b\":2}", File.ReadAllText(fileB));

		await host.Executor.ApplyAsync([SettingsSync.ToChange(a, b, host.Sync.Preview(a, b, [SettingsCategory.Editor]).Single())]);
		Assert.Equal("{\"a\":1}", File.ReadAllText(fileB));
		host.Executor.UndoLast();
		Assert.Equal("{\"b\":2}", File.ReadAllText(fileB));
	}

	[Fact]
	public void Plugin_settings_travel_only_where_the_plugin_exists_and_Git_settings_and_secrets_stay()
	{
		using var f = new Fixture();
		string a = f.Vault("a"), b = f.Vault("b");
		Fixture.Plugin(a, "both", "1.0.0", data: "{\"x\":1}");
		Fixture.Plugin(b, "both", "1.0.0");
		Fixture.Plugin(a, "only-a", "1.0.0", data: "{\"x\":1}");
		Fixture.Plugin(a, "obsidian-git", "1.0.0", data: "{\"x\":1}");
		Fixture.Plugin(b, "obsidian-git", "1.0.0");
		Fixture.Plugin(a, "keyed", "1.0.0", data: """{"token":"abcdefgh12345678"}""");
		Fixture.Plugin(b, "keyed", "1.0.0");

		IReadOnlyList<SyncEntry> entries = f.Host().Sync.Preview(a, b, [SettingsCategory.PluginSettings]);

		Assert.Equal(["plugins/both/data.json", "plugins/keyed/data.json"], entries.Select(e => e.RelativePath).OrderBy(p => p, StringComparer.Ordinal));
		SyncEntry keyed = entries.Single(e => e.RelativePath.Contains("keyed"));
		Assert.Equal(SyncDecision.BlockedSecret, keyed.Decision);
		Assert.Equal(["token"], keyed.SecretKeys);
		Assert.Equal(SyncDecision.Copy, entries.Single(e => e.RelativePath.Contains("both")).Decision);
	}

	[Fact]
	public void Themes_and_snippets_come_with_appearance()
	{
		using var f = new Fixture();
		string a = f.Vault("a"), b = f.Vault("b");
		string snippets = Path.Combine(VaultLayout.ConfigDir(a), "snippets");
		Directory.CreateDirectory(snippets);
		File.WriteAllText(Path.Combine(snippets, "mine.css"), "a{}");
		File.WriteAllText(Path.Combine(VaultLayout.ConfigDir(a), "appearance.json"), "{}");

		string[] paths = f.Host().Sync.Preview(a, b, [SettingsCategory.Appearance]).Select(e => e.RelativePath).ToArray();

		Assert.Contains("appearance.json", paths);
		Assert.Contains("snippets/mine.css", paths);
	}
}

public sealed class CatalogTests
{
	sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
	{
		public List<string> Calls { get; } = [];

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Calls.Add($"{request.Method} {request.RequestUri}");
			return Task.FromResult(respond(request));
		}
	}

	static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

	[Fact]
	public async Task Catalog_entries_with_a_hostile_repo_name_are_dropped()
	{
		var handler = new Handler(_ => Ok("""[{"id":"a","name":"A","repo":"x/a"},{"id":"b","name":"B","repo":"../../evil"},{"id":"c","name":"C","repo":"x/y/z"}]"""));
		IReadOnlyList<CachedCatalogEntry> entries = await new GitHubCatalogClient(new HttpClient(handler)).FetchCatalogAsync();
		Assert.Equal(["a"], entries.Select(e => e.Id));
	}

	[Fact]
	public async Task A_download_writes_the_three_release_files_and_tolerates_missing_styles()
	{
		using var f = new Fixture();
		var handler = new Handler(r => r.RequestUri!.AbsolutePath.EndsWith("styles.css", StringComparison.Ordinal) ? new HttpResponseMessage(HttpStatusCode.NotFound) : Ok("x"));
		await new GitHubCatalogClient(new HttpClient(handler)).DownloadReleaseAsync("x/y", "1.0.0", f.Root + "/dl");
		Assert.True(File.Exists(f.Root + "/dl/main.js"));
		Assert.True(File.Exists(f.Root + "/dl/manifest.json"));
		Assert.False(File.Exists(f.Root + "/dl/styles.css"));
		Assert.Contains(handler.Calls, c => c.Contains("github.com/x/y/releases/download/1.0.0/main.js"));
	}

	[Fact]
	public async Task A_missing_required_file_fails_the_download()
	{
		using var f = new Fixture();
		var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
		await Assert.ThrowsAsync<HttpRequestException>(() => new GitHubCatalogClient(new HttpClient(handler)).DownloadReleaseAsync("x/y", "1.0.0", f.Root + "/dl"));
	}

	[Fact]
	public async Task An_online_check_asks_only_about_installed_plugins_and_keeps_unknown_as_unknown()
	{
		using var f = new Fixture();
		var handler = new Handler(r =>
		{
			string url = r.RequestUri!.ToString();
			if (url == GitHubCatalogClient.CatalogUrl)
			{
				return Ok("""[{"id":"a","name":"A","repo":"x/a"},{"id":"b","name":"B","repo":"x/b"},{"id":"unused","name":"U","repo":"x/u"}]""");
			}

			return url.Contains("/x/a/") ? Ok("""{"version":"2.0.0"}""") : new HttpResponseMessage(HttpStatusCode.NotFound);
		});
		CoreHost host = f.Host(new GitHubCatalogClient(new HttpClient(handler)));

		OnlineCheck check = await host.Online.CheckAsync(["a", "b", "sideloaded"]);

		Assert.Equal("2.0.0", check.Versions.Single().Version);
		Assert.Equal(["sideloaded"], check.NotInCatalog);
		Assert.Equal(1, check.Failed);
		Assert.DoesNotContain(handler.Calls, c => c.Contains("/x/u/"));
		Assert.Equal(3, host.Database.Catalog().Count);
		Assert.Single(host.Database.OnlineVersions());
	}

	[Fact]
	public async Task A_downloaded_release_for_another_plugin_is_rejected()
	{
		using var f = new Fixture();
		string v = f.Vault("v");
		ChangeResult result = (await f.Host().Executor.ApplyAsync([new DownloadPlugin(v, "wanted", "Wanted", null, "2.39.0", "demo/git", 100, null)])).Results.Single();
		Assert.Equal(ChangeFailure.VerifyFailed, result.Failure);
		Assert.False(Directory.Exists(VaultLayout.PluginDir(v, "wanted")));
	}

	[Fact]
	public async Task A_downloaded_release_of_another_version_is_rejected()
	{
		using var f = new Fixture();
		string v = f.Vault("v");
		Fixture.Plugin(v, "obsidian-git", "2.35.0");
		var catalog = new OtherVersionCatalog();

		ChangeResult result = (await f.Host(catalog).Executor.ApplyAsync([new DownloadPlugin(v, "obsidian-git", "Git", "2.35.0", "2.39.0", "demo/git", 100, Fixture.MainHash(v, "obsidian-git"))])).Results.Single();

		Assert.Equal(ChangeFailure.VerifyFailed, result.Failure);
		Assert.Equal("2.35.0", Fixture.VersionOf(v, "obsidian-git"));
	}

	sealed class OtherVersionCatalog : ICatalogClient
	{
		readonly DemoCatalog _inner = new();
		public Task<long?> CatalogSizeAsync(CancellationToken cancellationToken = default) => _inner.CatalogSizeAsync(cancellationToken);
		public Task<IReadOnlyList<CachedCatalogEntry>> FetchCatalogAsync(CancellationToken cancellationToken = default) => _inner.FetchCatalogAsync(cancellationToken);
		public Task<string?> LatestVersionAsync(string repo, CancellationToken cancellationToken = default) => _inner.LatestVersionAsync(repo, cancellationToken);
		public Task<IReadOnlyList<ReleaseFile>> ReleaseFilesAsync(string repo, string version, CancellationToken cancellationToken = default) => _inner.ReleaseFilesAsync(repo, version, cancellationToken);
		public Task DownloadReleaseAsync(string repo, string version, string targetDirectory, CancellationToken cancellationToken = default) => _inner.DownloadReleaseAsync(repo, "9.9.9", targetDirectory, cancellationToken);
	}

	[Fact]
	public async Task A_downloaded_update_keeps_settings_and_can_be_undone()
	{
		using var f = new Fixture();
		string v = f.Vault("v");
		Fixture.Plugin(v, "obsidian-git", "2.35.0", data: """{"mine":1}""");
		CoreHost host = f.Host();
		await host.Executor.ApplyAsync([new DownloadPlugin(v, "obsidian-git", "Git", "2.35.0", "2.39.0", "demo/git", 100, Fixture.MainHash(v, "obsidian-git"))]);
		Assert.Equal("2.39.0", Fixture.VersionOf(v, "obsidian-git"));
		Assert.Contains("mine", File.ReadAllText(Path.Combine(VaultLayout.PluginDir(v, "obsidian-git"), VaultLayout.DataFile)));
		host.Executor.UndoLast();
		Assert.Equal("2.35.0", Fixture.VersionOf(v, "obsidian-git"));
	}
}

public sealed class DemoAndVersionTests
{
	[Fact]
	public void The_demo_world_shows_every_state_the_window_has_to_draw()
	{
		using var f = new Fixture();
		AppPaths paths = DemoWorkspace.Create(f.Root);
		var host = new CoreHost(paths, new DemoProcess(), new DemoCatalog());

		WorkspaceSnapshot s = host.Scanner.Scan();

		Assert.Equal(ReadStatus.Ok, s.RegistryStatus);
		Assert.Contains(s.Vaults, v => !v.IsOnline);
		Assert.Contains(s.Vaults, v => v.IsOpen);
		Assert.Contains(s.Vaults, v => v.ListUnreadable);
		Assert.Contains(s.Vaults, v => v.CommunityPlugins == CommunityPluginsState.NeverEnabled);
		Assert.Contains(s.Vaults, v => v.CommunityPlugins == CommunityPluginsState.Off);
		Assert.Contains(s.Plugins, p => p.Drifted);
		Assert.Contains(s.Plugins, p => p.SameVersionDifferentFiles);
		Assert.Contains(s.Plugins, p => p.Installs.Any(i => i.HasFolder && !i.Listed));
		Assert.Equal(2, s.Vaults.Count(v => v.DisplayName.StartsWith("Notes (", StringComparison.Ordinal)));
	}

	[Fact]
	public void The_demo_stays_inside_its_folder()
	{
		using var f = new Fixture();
		AppPaths paths = DemoWorkspace.Create(f.Root);
		Assert.StartsWith(f.Root, paths.ObsidianConfigDir, StringComparison.Ordinal);
		Assert.StartsWith(f.Root, paths.DataDir, StringComparison.Ordinal);
		foreach (var entry in new VaultRegistry(paths.ObsidianConfigDir).Read().Value!)
		{
			Assert.StartsWith(f.Root, entry.Path, StringComparison.Ordinal);
		}
	}

	static string RepoRoot()
	{
		string? dir = AppContext.BaseDirectory;
		while (dir is not null && !File.Exists(Path.Combine(dir, "VERSION")))
		{
			dir = Path.GetDirectoryName(dir);
		}

		return dir ?? throw new InvalidOperationException("VERSION not found");
	}

	[Fact]
	public void The_version_file_has_no_bom_and_one_digit_per_part()
	{
		byte[] bytes = File.ReadAllBytes(Path.Combine(RepoRoot(), "VERSION"));
		Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
		Assert.Matches(@"^\d\.\d\.\d\.\d$", System.Text.Encoding.ASCII.GetString(bytes).Trim());
	}

	[Fact]
	public void The_assembly_carries_the_version_file()
	{
		string expected = File.ReadAllText(Path.Combine(RepoRoot(), "VERSION")).Trim();
		Assert.Equal(expected, typeof(CoreHost).Assembly.GetName().Version!.ToString());
	}
}
