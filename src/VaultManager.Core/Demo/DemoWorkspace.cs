using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VaultManager.Core.Catalog;
using VaultManager.Core.Foundation;
using VaultManager.Core.Obsidian;
using VaultManager.Core.Store;

namespace VaultManager.Core.Demo;

public sealed class DemoProcess : IObsidianProcess
{
	public bool IsRunning { get; set; } = true;
}

/// <summary>Answers from memory: a demo never touches the network.</summary>
public sealed class DemoCatalog : ICatalogClient
{
	public Task<long?> CatalogSizeAsync(CancellationToken cancellationToken = default) => Task.FromResult<long?>(3_019_120);

	public Task<IReadOnlyList<CachedCatalogEntry>> FetchCatalogAsync(CancellationToken cancellationToken = default)
	{
		IReadOnlyList<CachedCatalogEntry> entries =
		[
			new("dataview", "Dataview", "Michael Brenan", "Query your notes.", "demo/dataview"),
			new("obsidian-git", "Git", "Vinzent", "Back up the vault with Git.", "demo/git"),
			new("templater-obsidian", "Templater", "SilentVoid", "Template language.", "demo/templater"),
			new("calendar", "Calendar", "Liam Cain", "Calendar view of daily notes.", "demo/calendar"),
		];
		return Task.FromResult(entries);
	}

	public Task<string?> LatestVersionAsync(string repo, CancellationToken cancellationToken = default) =>
		Task.FromResult<string?>(repo == "demo/git" ? "2.39.0" : repo == "demo/dataview" ? "0.5.68" : "1.5.10");

	public Task<IReadOnlyList<ReleaseFile>> ReleaseFilesAsync(string repo, string version, CancellationToken cancellationToken = default)
	{
		IReadOnlyList<ReleaseFile> files = [new(VaultLayout.MainFile, 742_000), new(VaultLayout.ManifestFile, 357)];
		return Task.FromResult(files);
	}

	public Task DownloadReleaseAsync(string repo, string version, string targetDirectory, CancellationToken cancellationToken = default)
	{
		string id = repo == "demo/git" ? "obsidian-git" : repo == "demo/dataview" ? "dataview" : repo == "demo/templater" ? "templater-obsidian" : "calendar";
		DemoWorkspace.WritePlugin(targetDirectory, id, id, version, withData: false);
		return Task.CompletedTask;
	}
}

/// <summary>
/// A sealed world for --demo and for screenshots: vaults, a registry and a browser store inside one throwaway
/// folder, with every state the window has to show. Nothing outside the folder is read or written.
/// </summary>
public static class DemoWorkspace
{
	public const string OpenVault = "KnowledgeSnippets";
	public const string OfflineVault = "Archive";
	public const string DamagedVault = "Damaged";

	public static AppPaths Create(string root)
	{
		string config = Path.Combine(root, "obsidian");
		string vaults = Path.Combine(root, "Vaults");
		Directory.CreateDirectory(config);

		var registry = new JObject();
		var store = new System.Text.StringBuilder();

		void Vault(string id, string name, bool open = false, bool? plugins = true, string? parent = null)
		{
			string path = Path.Combine(parent ?? vaults, name);
			registry[id] = new JObject { ["path"] = path, ["ts"] = 1, ["open"] = open };
			Directory.CreateDirectory(VaultLayout.ConfigDir(path));
			if (plugins is not null)
			{
				store.Append($"enable-plugin-{id}\u0001{(plugins == true ? "true" : "false")}");
			}
		}

		string Dir(string name, string? parent = null) => Path.Combine(parent ?? vaults, name);

		void Plugin(string vault, string id, string name, string version, bool on = true, bool data = false, string? mainBody = null, string? parent = null)
		{
			string dir = VaultLayout.PluginDir(Dir(vault, parent), id);
			WritePlugin(dir, id, name, version, data, mainBody);
			if (on)
			{
				CommunityPluginList.Set(Dir(vault, parent), id, true);
			}
		}

		Vault("0000000000000001", "Future", plugins: null);
		Vault("0000000000000002", "Family");
		Vault("0000000000000003", OpenVault, open: true);
		Vault("0000000000000004", "Notes");
		Vault("0000000000000005", "HyperionDocs");
		Vault("0000000000000006", "ChronosDocs");
		Vault("0000000000000007", "Notes", parent: Path.Combine(vaults, "CopyPc"));
		Vault("0000000000000009", DamagedVault);
		registry["0000000000000008"] = new JObject { ["path"] = Path.Combine(root, "UnpluggedDrive", OfflineVault), ["ts"] = 1 };
		registry["000000000000000a"] = new JObject { ["path"] = Path.Combine(vaults, "Family") + "-restricted", ["ts"] = 1 };
		Directory.CreateDirectory(VaultLayout.ConfigDir(Path.Combine(vaults, "Family") + "-restricted"));
		store.Append("enable-plugin-000000000000000a\u0001false");

		string[] studio = ["dataview", "folder-notes", "metadata-menu", "obsidian-git", "obsidian-linter", "omnisearch", "templater-obsidian", "waypoint"];
		string[] studioNames = ["Dataview", "Folder notes", "Metadata Menu", "Git", "Linter", "Omnisearch", "Templater", "Waypoint"];
		string[] studioVersions = ["0.5.68", "1.8.19", "0.8.12", "2.38.2", "1.31.2", "1.28.2", "2.20.0", "3.0.1"];
		for (int i = 0; i < studio.Length; i++)
		{
			Plugin("HyperionDocs", studio[i], studioNames[i], studioVersions[i], data: i is 1 or 2);
			Plugin("ChronosDocs", studio[i], studioNames[i], studioVersions[i], data: i is 1 or 2);
		}

		Plugin(OpenVault, "obsidian-git", "Git", "2.35.0", data: true);
		Plugin(OpenVault, "dataview", "Dataview", "0.5.68");
		Plugin(OpenVault, "calendar", "Calendar", "1.5.10", on: false, data: true);
		Plugin(OpenVault, "automatic-table-of-contents", "Automatic Table Of Contents", "1.7.3");
		Plugin("ChronosDocs", "automatic-table-of-contents", "Automatic Table Of Contents", "1.8.0");
		Plugin("Family", "obsidian-excalidraw-plugin", "Excalidraw", "2.17.2", data: true);
		Plugin(OpenVault, "obsidian-excalidraw-plugin", "Excalidraw", "2.15.2", data: true);
		Plugin("Notes", "obsidian-excalidraw-plugin", "Excalidraw", "2.8.3", data: true);
		Plugin("Notes", "beautitab", "Beautitab", "1.6.1", mainBody: "// one build");
		Plugin("Family", "beautitab", "Beautitab", "1.6.1", mainBody: "// another build");
		Plugin("Family", "ai-helper", "AI Helper", "1.0.0");
		File.WriteAllText(Path.Combine(VaultLayout.PluginDir(Dir("Family"), "ai-helper"), VaultLayout.DataFile), """{"apiKey":"demo-not-a-real-key-000000"}""");

		// A list cut short by a dropped drive: the app must refuse to touch it.
		File.WriteAllText(VaultLayout.CommunityPluginsFile(Dir(DamagedVault)), "[\"dataview\",");

		File.WriteAllText(Path.Combine(VaultLayout.ConfigDir(Dir("ChronosDocs")), "hotkeys.json"), """{"editor:toggle-bold":[{"modifiers":["Mod"],"key":"B"}]}""");
		File.WriteAllText(Path.Combine(VaultLayout.ConfigDir(Dir("ChronosDocs")), "appearance.json"), """{"accentColor":"#67ad38"}""");
		File.WriteAllText(Path.Combine(VaultLayout.ConfigDir(Dir("HyperionDocs")), "appearance.json"), """{"accentColor":"#9d2499"}""");

		string[] folders = ["00 System", "01 Architecture", "02 Services", "04 Operations", "05 Summaries", "06 Decisions", "07 Meetings", "Inbox"];
		foreach (string folder in folders)
		{
			Directory.CreateDirectory(Path.Combine(Dir("ChronosDocs"), folder));
			Directory.CreateDirectory(Path.Combine(Dir("HyperionDocs"), folder));
		}

		Directory.CreateDirectory(Path.Combine(Dir("HyperionDocs"), "03 Plugins"));
		File.WriteAllText(Path.Combine(Dir("ChronosDocs"), "00 System", "Vault Map.md"), "# Vault Map\n");
		File.WriteAllText(Path.Combine(Dir("HyperionDocs"), "00 System", "Vault Map.md"), "# Vault Map\n");

		File.WriteAllText(Path.Combine(config, "obsidian.json"), new JObject { ["vaults"] = registry }.ToString(Formatting.None));
		string leveldb = Path.Combine(config, "Local Storage", "leveldb");
		Directory.CreateDirectory(leveldb);
		File.WriteAllText(Path.Combine(leveldb, "000001.log"), store.ToString(), System.Text.Encoding.Latin1);

		return new AppPaths(config, Path.Combine(root, "data"));
	}

	internal static void WritePlugin(string dir, string id, string name, string version, bool withData, string? mainBody = null)
	{
		Directory.CreateDirectory(dir);
		File.WriteAllText(Path.Combine(dir, VaultLayout.ManifestFile), new JObject { ["id"] = id, ["name"] = name, ["version"] = version, ["author"] = "Demo" }.ToString());
		File.WriteAllText(Path.Combine(dir, VaultLayout.MainFile), mainBody ?? $"// {id} {version}");
		if (withData)
		{
			File.WriteAllText(Path.Combine(dir, VaultLayout.DataFile), """{"enabled":true,"rememberPassword":false}""");
		}
	}
}
