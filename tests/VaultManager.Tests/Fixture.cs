using VaultManager.Core;
using VaultManager.Core.Catalog;
using VaultManager.Core.Demo;
using VaultManager.Core.Foundation;
using VaultManager.Core.Obsidian;
using Newtonsoft.Json.Linq;

namespace VaultManager.Tests;

/// <summary>A real temp folder per test: this code is about files, so tests use real files.</summary>
public sealed class Fixture : IDisposable
{
	public string Root { get; } = Path.Combine(Path.GetTempPath(), "vm-test-" + Guid.NewGuid().ToString("N"));
	public DemoProcess Process { get; } = new() { IsRunning = false };
	public AppPaths Paths { get; }

	public Fixture()
	{
		Directory.CreateDirectory(Root);
		Paths = new AppPaths(Path.Combine(Root, "obsidian"), Path.Combine(Root, "data"));
		Directory.CreateDirectory(Paths.ObsidianConfigDir);
	}

	public CoreHost Host(ICatalogClient? catalog = null) => new(Paths, Process, catalog ?? new DemoCatalog());

	public string Vault(string name)
	{
		string path = Path.Combine(Root, "vaults", name);
		Directory.CreateDirectory(VaultLayout.ConfigDir(path));
		return path;
	}

	public void Register(params (string Id, string Path, bool Open)[] vaults)
	{
		var body = new JObject();
		foreach ((string id, string path, bool open) in vaults)
		{
			body[id] = new JObject { ["path"] = path, ["ts"] = 1, ["open"] = open };
		}

		File.WriteAllText(Path.Combine(Paths.ObsidianConfigDir, "obsidian.json"), new JObject { ["vaults"] = body }.ToString());
	}

	public static string Plugin(string vault, string id, string version, bool listed = true, string? data = null, string? main = null)
	{
		string dir = VaultLayout.PluginDir(vault, id);
		Directory.CreateDirectory(dir);
		File.WriteAllText(Path.Combine(dir, VaultLayout.ManifestFile), new JObject { ["id"] = id, ["name"] = id, ["version"] = version }.ToString());
		File.WriteAllText(Path.Combine(dir, VaultLayout.MainFile), main ?? $"// {id} {version}");
		if (data is not null)
		{
			File.WriteAllText(Path.Combine(dir, VaultLayout.DataFile), data);
		}

		if (listed)
		{
			CommunityPluginList.Set(vault, id, true);
		}

		return dir;
	}

	public static string? MainHash(string vault, string id)
	{
		string main = Path.Combine(VaultLayout.PluginDir(vault, id), VaultLayout.MainFile);
		return File.Exists(main) ? SharedFile.Hash(main) : null;
	}

	public static string? VersionOf(string vault, string id) => PluginManifest.TryRead(VaultLayout.PluginDir(vault, id))?.Version;

	public void Dispose()
	{
		try
		{
			Folder.Delete(Root);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			// SQLite may still hold the file for a moment; the temp folder is not worth failing a test over.
		}
	}
}
