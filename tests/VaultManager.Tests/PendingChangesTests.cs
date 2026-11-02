using System.Reflection;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json.Linq;
using VaultManager.Core;
using VaultManager.Core.Changes;
using VaultManager.Core.Profiles;
using VaultManager.Core.Store;

namespace VaultManager.Tests;

/// <summary>Lines that wait outlive the app, so their form on disk and the step to schema 2 are pinned here.</summary>
public sealed class PendingChangesTests
{
	static Profile RichProfile() => new()
	{
		Id = "rich",
		Name = "Rich",
		Description = "With everything",
		Plugins = ["dataview", "obsidian-git"],
		Folders = ["00 System", "01 Architecture"],
		FolderNotes = true,
		VaultMap = true,
		Settings = new Dictionary<string, JToken> { ["appearance.json"] = JObject.Parse("""{"accentColor":"#67ad38","nested":{"a":[1,2]}}""") },
		Templates = new Dictionary<string, string> { ["Note.md"] = "# {{title}}\n" },
	};

	public static IEnumerable<object[]> EveryRecord()
	{
		yield return [new CopyPlugin(@"D:\v\a", "p", "P", "1.0.0", "2.0.0", @"D:\v\b", true, "hash")];
		yield return [new CopyPlugin(@"D:\v\a", "p", "P", null, "2.0.0", @"D:\v\b", false, null)];
		yield return [new DownloadPlugin(@"D:\v\a", "p", "P", "1.0.0", "2.0.0", "owner/repo", 742_357, "hash")];
		yield return [new DownloadPlugin(@"D:\v\a", "p", "P", null, "2.0.0", "owner/repo", 0, null)];
		yield return [new SetPluginEnabled(@"D:\v\a", "p", "P", false)];
		yield return [new SetPluginEnabled(@"D:\v\a", "p", "P", true)];
		yield return [new CopySettingsFile(@"D:\v\a", @"D:\v\b", "snippets/x.css", "source", "target")];
		yield return [new CopySettingsFile(@"D:\v\a", @"D:\v\b", "app.json", "source", null)];
		yield return [new AddProfileParts(@"D:\v\a", RichProfile())];
		yield return [new CreateVault(@"D:\v", "fresh", RichProfile(), new Dictionary<string, string> { ["dataview"] = @"D:\v\b" })];
		yield return [new CreateVault(@"D:\v", "empty", null, new Dictionary<string, string>())];
		yield return [new RegisterVault(@"D:\v\fresh")];
	}

	// Records hold lists and profiles, whose default equality is by reference, so the written form is the measure.
	[Theory]
	[MemberData(nameof(EveryRecord))]
	public void Every_record_survives_the_round_trip(ChangeItem item)
	{
		string json = ChangeItemJson.Serialize(item);

		ChangeItem back = ChangeItemJson.TryParse(json)!;

		Assert.Equal(item.GetType(), back.GetType());
		Assert.Equal(item.Id, back.Id);
		Assert.Equal(item.VaultPath, back.VaultPath);
		Assert.Equal(json, ChangeItemJson.Serialize(back));
	}

	[Fact]
	public void Every_record_type_has_a_case_above_and_its_own_kind()
	{
		List<Type> all = typeof(ChangeItem).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(ChangeItem)) && !t.IsAbstract).ToList();
		List<ChangeItem> samples = EveryRecord().Select(r => (ChangeItem)r[0]).ToList();

		Assert.All(all, t => Assert.Contains(samples, s => s.GetType() == t));
		Assert.Equal(all.Count, samples.Select(s => JObject.Parse(ChangeItemJson.Serialize(s))["kind"]!.Value<string>()).Distinct().Count());
	}

	[Theory]
	[MemberData(nameof(EveryRecord))]
	public void The_kind_is_an_explicit_field_and_no_type_name_is_written(ChangeItem item)
	{
		string json = ChangeItemJson.Serialize(item);
		Assert.False(string.IsNullOrEmpty(JObject.Parse(json)["kind"]!.Value<string>()));
		Assert.DoesNotContain("$type", json);
		Assert.DoesNotContain("VaultManager.Core", json);
	}

	[Fact]
	public void A_profile_inside_a_line_keeps_its_settings_and_templates()
	{
		var item = new AddProfileParts(@"D:\v\a", RichProfile());

		var back = Assert.IsType<AddProfileParts>(ChangeItemJson.TryParse(ChangeItemJson.Serialize(item)));

		Assert.Equal(["dataview", "obsidian-git"], back.Profile.Plugins);
		Assert.Equal("#67ad38", back.Profile.Settings["appearance.json"]["accentColor"]!.Value<string>());
		Assert.Equal("# {{title}}\n", back.Profile.Templates["Note.md"]);
		Assert.True(back.Profile.VaultMap);
	}

	[Fact]
	public void The_built_in_profile_inside_a_new_vault_round_trips()
	{
		var item = new CreateVault(@"D:\v", "fresh", ProfileStore.BuiltIn(), new Dictionary<string, string> { ["dataview"] = @"D:\v\b" });

		var back = Assert.IsType<CreateVault>(ChangeItemJson.TryParse(ChangeItemJson.Serialize(item)));

		Assert.Equal(ProfileStore.BuiltIn().Plugins, back.Profile!.Plugins);
		Assert.Equal(@"D:\v\b", back.PluginSources["dataview"]);
		Assert.Equal(item.NewVaultPath, back.NewVaultPath);
	}

	[Theory]
	[InlineData("not json")]
	[InlineData("[]")]
	[InlineData("""{"kind":"format-the-disk","id":"00000000000000000000000000000001"}""")]
	[InlineData("""{"id":"00000000000000000000000000000001","targetVault":"D:\\v","pluginId":"p","pluginName":"P","enabled":true}""")]
	[InlineData("""{"kind":"set-plugin-enabled","id":"not-a-guid","targetVault":"D:\\v","pluginId":"p","pluginName":"P","enabled":true}""")]
	[InlineData("""{"kind":"set-plugin-enabled","id":"00000000000000000000000000000001","pluginId":"p","pluginName":"P","enabled":true}""")]
	[InlineData("""{"kind":"add-profile-parts","id":"00000000000000000000000000000001","targetVault":"D:\\v","profile":{"Id":"../../evil","Name":"x"}}""")]
	public void A_damaged_line_is_no_line(string json) => Assert.Null(ChangeItemJson.TryParse(json));

	[Fact]
	public void A_new_database_starts_at_the_current_schema_with_nothing_waiting()
	{
		using var f = new Fixture();
		var db = new AppDatabase(f.Paths.DatabaseFile);
		Assert.Empty(db.PendingChanges());
		Assert.Equal(2, UserVersion(f.Paths.DatabaseFile));
	}

	[Fact]
	public void Pending_lines_keep_their_order_replace_by_id_and_can_be_removed()
	{
		using var f = new Fixture();
		var db = new AppDatabase(f.Paths.DatabaseFile);
		db.SavePending("a", "first");
		db.SavePending("b", "second");
		db.SavePending("a", "first again");

		Assert.Equal([new PendingRow("a", "first again"), new PendingRow("b", "second")], new AppDatabase(f.Paths.DatabaseFile).PendingChanges());

		db.RemovePending("a");
		db.RemovePending("not there");
		Assert.Equal(["b"], db.PendingChanges().Select(p => p.Id));
	}

	/// <summary>The exact tables a version 1 build wrote, with data in them, as a file on disk.</summary>
	static void WriteVersion1File(string path)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
		connection.Open();
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = """
			PRAGMA journal_mode=WAL;
			CREATE TABLE operations(id TEXT PRIMARY KEY, at TEXT NOT NULL, undone INTEGER NOT NULL DEFAULT 0);
			CREATE TABLE operation_items(
				id INTEGER PRIMARY KEY AUTOINCREMENT,
				operation_id TEXT NOT NULL REFERENCES operations(id) ON DELETE CASCADE,
				undo_json TEXT NOT NULL);
			CREATE TABLE ignored(key TEXT PRIMARY KEY);
			CREATE TABLE settings(key TEXT PRIMARY KEY, value TEXT NOT NULL);
			CREATE TABLE sync_bases(source TEXT NOT NULL, target TEXT NOT NULL, path TEXT NOT NULL, hash TEXT NOT NULL, PRIMARY KEY(source, target, path));
			CREATE TABLE vault_profiles(vault_id TEXT PRIMARY KEY, profile_id TEXT NOT NULL);
			CREATE TABLE catalog(id TEXT PRIMARY KEY, name TEXT NOT NULL, author TEXT NOT NULL, description TEXT NOT NULL, repo TEXT NOT NULL);
			CREATE TABLE online_versions(plugin_id TEXT PRIMARY KEY, version TEXT NOT NULL, checked_at TEXT NOT NULL);
			PRAGMA user_version = 1;
			INSERT INTO operations(id, at) VALUES ('op1', '2026-11-01T10:00:00.0000000+00:00');
			INSERT INTO operation_items(operation_id, undo_json) VALUES ('op1', '{"Kind":"toggle","Vault":"D:\\v"}');
			INSERT INTO ignored(key) VALUES ('older:p');
			INSERT INTO settings(key, value) VALUES ('language', 'ru');
			INSERT INTO sync_bases(source, target, path, hash) VALUES ('a', 'b', 'app.json', 'h');
			INSERT INTO vault_profiles(vault_id, profile_id) VALUES ('v', 'tech-tea-studio');
			INSERT INTO catalog(id, name, author, description, repo) VALUES ('p', 'P', 'A', 'D', 'o/r');
			INSERT INTO online_versions(plugin_id, version, checked_at) VALUES ('p', '1.0.0', '2026-11-01T10:00:00.0000000+00:00');
			""";
		command.ExecuteNonQuery();
	}

	static long UserVersion(string path)
	{
		using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
		connection.Open();
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = "PRAGMA user_version;";
		return (long)command.ExecuteScalar()!;
	}

	[Fact]
	public void A_version_1_file_upgrades_and_keeps_all_it_held()
	{
		using var f = new Fixture();
		WriteVersion1File(f.Paths.DatabaseFile);
		Assert.Equal(1, UserVersion(f.Paths.DatabaseFile));

		var db = new AppDatabase(f.Paths.DatabaseFile);

		Assert.Equal(2, UserVersion(f.Paths.DatabaseFile));
		Assert.Empty(db.PendingChanges());
		db.SavePending("a", "line");
		Assert.Single(db.PendingChanges());

		Assert.Equal("op1", db.LastUndoable()!.Id);
		Assert.Single(db.UndoRecords("op1"));
		Assert.Contains("older:p", db.Ignored());
		Assert.Equal("ru", db.Setting("language"));
		Assert.Equal("h", db.SyncBase("a", "b", "app.json"));
		Assert.Equal("tech-tea-studio", db.VaultProfile("v"));
		Assert.Single(db.Catalog());
		Assert.Single(db.OnlineVersions());

		// A second start finds nothing left to do and loses nothing.
		var again = new AppDatabase(f.Paths.DatabaseFile);
		Assert.Single(again.PendingChanges());
		Assert.Equal(2, UserVersion(f.Paths.DatabaseFile));
	}

	[Fact]
	public void A_persisted_line_runs_through_the_executor_gates_after_a_restart()
	{
		using var f = new Fixture();
		string a = f.Vault("a"), b = f.Vault("b");
		Fixture.Plugin(a, "p", "2.0.0");
		Fixture.Plugin(b, "p", "1.0.0");
		f.Register(("1", a, false), ("2", b, true));
		f.Process.IsRunning = true;
		CoreHost first = f.Host();
		var line = new CopyPlugin(b, "p", "p", "1.0.0", "2.0.0", a, false, Fixture.MainHash(b, "p"));
		first.Database.SavePending(line.Id.ToString("N"), ChangeItemJson.Serialize(line));

		CoreHost second = f.Host();
		ChangeItem back = ChangeItemJson.TryParse(second.Database.PendingChanges().Single().ItemJson)!;

		Assert.Equal(VaultManager.Core.Foundation.ChangeFailure.VaultOpen, second.Executor.Gate(back));
		f.Process.IsRunning = false;
		Assert.Equal(VaultManager.Core.Foundation.ChangeFailure.None, second.Executor.Gate(back));
	}

	[Fact]
	public async Task A_stale_persisted_line_needs_review_and_overwrites_nothing()
	{
		using var f = new Fixture();
		string a = f.Vault("a"), b = f.Vault("b");
		Fixture.Plugin(a, "p", "2.0.0");
		Fixture.Plugin(b, "p", "1.0.0");
		var line = new CopyPlugin(b, "p", "p", "1.0.0", "2.0.0", a, false, Fixture.MainHash(b, "p"));
		string json = ChangeItemJson.Serialize(line);
		Fixture.Plugin(b, "p", "1.5.0", main: "// the vault moved on while the line waited");

		ChangeResult result = (await f.Host().Executor.ApplyAsync([ChangeItemJson.TryParse(json)!])).Results.Single();

		Assert.Equal(ChangeStatus.NeedsReview, result.Status);
		Assert.Equal("1.5.0", Fixture.VersionOf(b, "p"));
	}
}
