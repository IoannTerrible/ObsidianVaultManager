using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VaultManager.Core.Changes;
using VaultManager.Core.Foundation;
using VaultManager.Core.Obsidian;

namespace VaultManager.Core.Profiles;

/// <summary>
/// A vault recipe kept as a plain file, so it can live in git, be sent to someone, or be edited by hand.
/// It never holds plugin settings or anything from a note.
/// </summary>
public sealed record Profile
{
	public const int CurrentFormat = 1;
	public int Format { get; init; } = CurrentFormat;
	public required string Id { get; init; }
	public required string Name { get; init; }
	public string Description { get; init; } = string.Empty;
	public IReadOnlyList<string> Plugins { get; init; } = [];
	public IReadOnlyList<string> Folders { get; init; } = [];
	public bool FolderNotes { get; init; }
	public bool VaultMap { get; init; }

	/// <summary>Whole settings files under .obsidian, by file name.</summary>
	public IReadOnlyDictionary<string, JToken> Settings { get; init; } = new Dictionary<string, JToken>();

	/// <summary>Note templates under 00 System/Templates, by file name.</summary>
	public IReadOnlyDictionary<string, string> Templates { get; init; } = new Dictionary<string, string>();

	[JsonIgnore]
	public bool IsBuiltIn { get; init; }
}

public enum ProfileProblem
{
	None,
	NotJson,
	TooLarge,
	NewerFormat,
	MissingName,
	BadPluginId,
	BadFolder,
	BadSettingsFile,
	BadTemplate,
	SecretInSettings,
}

public sealed class ProfileRejectedException(ProfileProblem problem) : Exception(problem.ToString())
{
	public ProfileProblem Problem { get; } = problem;
}

/// <summary>Checks a profile as untrusted input: an imported file decides where files get written.</summary>
public static class ProfileValidator
{
	public const int MaxFileBytes = 1024 * 1024;
	static readonly Regex PluginId = new(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant);
	static readonly HashSet<string> AllowedSettings = new(StringComparer.Ordinal) { "app.json", "appearance.json", "core-plugins.json", "hotkeys.json", "graph.json" };
	static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase) { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };

	public static ProfileProblem Check(Profile profile)
	{
		if (profile.Format > Profile.CurrentFormat)
		{
			return ProfileProblem.NewerFormat;
		}

		if (string.IsNullOrWhiteSpace(profile.Name) || string.IsNullOrWhiteSpace(profile.Id))
		{
			return ProfileProblem.MissingName;
		}

		// A file can say null where the type promises a list, and an id becomes a file name.
		if (profile.Plugins is null || profile.Folders is null || profile.Settings is null || profile.Templates is null || !PluginId.IsMatch(profile.Id))
		{
			return ProfileProblem.NotJson;
		}

		if (profile.Settings.Values.Any(v => v is null || v.Type == JTokenType.Null))
		{
			return ProfileProblem.BadSettingsFile;
		}

		if (profile.Templates.Values.Any(v => v is null))
		{
			return ProfileProblem.BadTemplate;
		}

		if (profile.Plugins.Any(p => !PluginId.IsMatch(p ?? string.Empty)))
		{
			return ProfileProblem.BadPluginId;
		}

		if (profile.Folders.Any(f => !IsSafeRelative(f)))
		{
			return ProfileProblem.BadFolder;
		}

		if (profile.Settings.Keys.Any(k => !AllowedSettings.Contains(k)))
		{
			return ProfileProblem.BadSettingsFile;
		}

		if (profile.Templates.Keys.Any(t => !IsSafeName(t) || !t.EndsWith(".md", StringComparison.OrdinalIgnoreCase)))
		{
			return ProfileProblem.BadTemplate;
		}

		return profile.Settings.Values.Any(v => SecretScanner.FindKeys(v.ToString()).Count > 0)
			? ProfileProblem.SecretInSettings
			: ProfileProblem.None;
	}

	public static bool IsSafeRelative(string? path)
	{
		if (string.IsNullOrWhiteSpace(path) || path.Contains(':') || path.StartsWith('/') || path.StartsWith('\\'))
		{
			return false;
		}

		string[] parts = path.Split('/', '\\');
		return parts.All(IsSafeName) && !parts[0].StartsWith(".obsidian", StringComparison.OrdinalIgnoreCase);
	}

	static bool IsSafeName(string name) =>
		name.Length is > 0 and <= 120
		&& name != "." && name != ".."
		&& !name.EndsWith('.') && !name.EndsWith(' ')
		&& name.IndexOfAny(Path.GetInvalidFileNameChars().Concat(['<', '>', ':', '"', '|', '?', '*', '/', '\\']).ToArray()) < 0
		&& !ReservedNames.Contains(Path.GetFileNameWithoutExtension(name));
}

public static class ProfileFile
{
	public const string Extension = ".vaultprofile.json";

	public static string Serialize(Profile profile) => JsonConvert.SerializeObject(profile, Formatting.Indented);

	public static Profile Parse(string json)
	{
		if (System.Text.Encoding.UTF8.GetByteCount(json) > ProfileValidator.MaxFileBytes)
		{
			throw new ProfileRejectedException(ProfileProblem.TooLarge);
		}

		Profile? profile;
		try
		{
			// TypeNameHandling stays off: a file from elsewhere must not pick types.
			profile = JsonConvert.DeserializeObject<Profile>(json, new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None });
		}
		catch (JsonException)
		{
			throw new ProfileRejectedException(ProfileProblem.NotJson);
		}

		if (profile is null)
		{
			throw new ProfileRejectedException(ProfileProblem.NotJson);
		}

		ProfileProblem problem = ProfileValidator.Check(profile);
		if (problem != ProfileProblem.None)
		{
			throw new ProfileRejectedException(problem);
		}

		return profile;
	}
}

/// <summary>Profiles are files in one folder, plus one read-only built in.</summary>
public sealed class ProfileStore(string directory)
{
	public const string BuiltInId = "tech-tea-studio";

	public IReadOnlyList<Profile> List()
	{
		var profiles = new List<Profile> { BuiltIn() };
		if (Directory.Exists(directory))
		{
			foreach (string file in Directory.EnumerateFiles(directory, "*" + ProfileFile.Extension))
			{
				try
				{
					Profile profile = ProfileFile.Parse(SharedFile.ReadAllText(file));
					if (profile.Id != BuiltInId)
					{
						profiles.Add(profile);
					}
				}
				catch (Exception ex) when (ex is ProfileRejectedException or IOException or UnauthorizedAccessException)
				{
					// One broken file must not hide the others. Import reports problems; a hand-edited file shows up by its absence.
				}
			}
		}

		return profiles.OrderBy(p => p.IsBuiltIn ? 0 : 1).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
	}

	public Profile? Find(string id) => List().FirstOrDefault(p => p.Id == id);

	public void Save(Profile profile)
	{
		if (profile.IsBuiltIn || profile.Id == BuiltInId)
		{
			throw new InvalidOperationException("The built-in profile is read-only; save a copy.");
		}

		ProfileProblem problem = ProfileValidator.Check(profile);
		if (problem != ProfileProblem.None)
		{
			throw new ProfileRejectedException(problem);
		}

		AtomicFile.WriteAllText(PathOf(profile.Id), ProfileFile.Serialize(profile));
	}

	public void Delete(string id)
	{
		if (id == BuiltInId)
		{
			throw new InvalidOperationException("The built-in profile cannot be deleted.");
		}

		string path = PathOf(id);
		if (File.Exists(path))
		{
			File.Delete(path);
		}
	}

	/// <summary>A new, editable copy under a free name.</summary>
	public Profile Duplicate(Profile source, string name) => source with { Id = NewId(name), Name = name, IsBuiltIn = false };

	public void Export(Profile profile, string path) => AtomicFile.WriteAllText(path, ProfileFile.Serialize(profile with { IsBuiltIn = false }));

	/// <summary>Reads a profile from anywhere. An import never overwrites: a taken id or name gets a new one.</summary>
	public Profile Import(string path)
	{
		if (new FileInfo(path).Length > ProfileValidator.MaxFileBytes)
		{
			throw new ProfileRejectedException(ProfileProblem.TooLarge);
		}

		Profile profile = ProfileFile.Parse(SharedFile.ReadAllText(path));
		IReadOnlyList<Profile> existing = List();
		string name = profile.Name;
		for (int n = 2; existing.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)); n++)
		{
			name = $"{profile.Name} {n}";
		}

		Profile imported = profile with { Id = NewId(name), Name = name, IsBuiltIn = false };
		Save(imported);
		return imported;
	}

	string PathOf(string id) => Path.Combine(directory, id + ProfileFile.Extension);

	string NewId(string name)
	{
		string slug = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
		if (slug.Length == 0)
		{
			slug = "profile";
		}

		string id = slug;
		for (int n = 2; id == BuiltInId || File.Exists(PathOf(id)); n++)
		{
			id = $"{slug}-{n}";
		}

		return id;
	}

	public static Profile BuiltIn()
	{
		Assembly assembly = typeof(ProfileStore).Assembly;
		using Stream stream = assembly.GetManifestResourceStream("VaultManager.Core.Profiles.Builtin.tech-tea-studio.json")!;
		using var reader = new StreamReader(stream);
		return ProfileFile.Parse(reader.ReadToEnd()) with { IsBuiltIn = true };
	}
}

public sealed record ProfileAudit(
	IReadOnlyList<string> MissingPlugins,
	IReadOnlyList<string> PresentPlugins,
	IReadOnlyList<string> OtherPlugins,
	IReadOnlyList<string> MissingFolders,
	IReadOnlyList<string> PresentFolders,
	bool VaultMapMissing)
{
	public bool IsComplete => MissingPlugins.Count == 0 && MissingFolders.Count == 0 && !VaultMapMissing;

	public static ProfileAudit For(Profile profile, string vault, IReadOnlyCollection<string> enabledPlugins)
	{
		bool Has(string folder) => Directory.Exists(Path.Combine(vault, folder));
		return new ProfileAudit(
			profile.Plugins.Where(p => !enabledPlugins.Contains(p)).ToList(),
			profile.Plugins.Where(enabledPlugins.Contains).ToList(),
			enabledPlugins.Where(p => !profile.Plugins.Contains(p)).ToList(),
			profile.Folders.Where(f => !Has(f)).ToList(),
			profile.Folders.Where(Has).ToList(),
			profile.VaultMap && !File.Exists(Path.Combine(vault, VaultBuilder.VaultMapPath)));
	}
}

/// <summary>Writes a profile's parts into a folder. It only ever adds: an existing note stays untouched.</summary>
public static class VaultBuilder
{
	public const string SystemFolder = "00 System";
	public static readonly string VaultMapPath = Path.Combine(SystemFolder, "Vault Map.md");

	public static void Create(CreateVault item)
	{
		string vault = item.NewVaultPath;
		if (Directory.Exists(vault) && Directory.EnumerateFileSystemEntries(vault).Any())
		{
			throw new ChangeRefusedException(ChangeFailure.TargetNotEmpty);
		}

		if (File.Exists(vault))
		{
			throw new ChangeRefusedException(ChangeFailure.TargetNotEmpty);
		}

		// Checked before anything is created: the line promised these plugins, and a vanished drive must not make it a silent half.
		foreach (string plugin in item.Profile?.Plugins ?? [])
		{
			if (item.PluginSources.TryGetValue(plugin, out string? sourceVault) && !Directory.Exists(VaultLayout.PluginDir(sourceVault, plugin)))
			{
				throw new ChangeRefusedException(ChangeFailure.SourceMissing);
			}
		}

		bool existed = Directory.Exists(vault);
		try
		{
			Fill(item, vault);
		}
		catch
		{
			// A half-built folder would make every retry fail as "already holds files".
			Folder.Delete(vault);
			if (existed)
			{
				Directory.CreateDirectory(vault);
			}

			throw;
		}
	}

	static void Fill(CreateVault item, string vault)
	{
		Directory.CreateDirectory(VaultLayout.ConfigDir(vault));
		if (item.Profile is null)
		{
			return;
		}

		foreach ((string file, JToken content) in item.Profile.Settings)
		{
			AtomicFile.WriteAllText(Path.Combine(VaultLayout.ConfigDir(vault), file), content.ToString(Formatting.Indented));
		}

		var enabled = new List<string>();
		foreach (string plugin in item.Profile.Plugins)
		{
			if (!item.PluginSources.TryGetValue(plugin, out string? sourceVault))
			{
				continue;
			}

			string source = VaultLayout.PluginDir(sourceVault, plugin);
			if (!Directory.Exists(source))
			{
				continue;
			}

			// Plugin settings stay behind on purpose: they can hold keys and paths of the source vault.
			Folder.Copy(source, VaultLayout.PluginDir(vault, plugin), relative => relative == VaultLayout.DataFile || relative.StartsWith(".git", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("node_modules", StringComparison.OrdinalIgnoreCase));
			enabled.Add(plugin);
		}

		AtomicFile.WriteAllText(VaultLayout.CommunityPluginsFile(vault), new JArray(enabled).ToString(Formatting.Indented));
		AddParts(vault, item.Profile);
	}

	/// <returns>What was actually created, relative to the vault, so an undo removes only that.</returns>
	public static (IReadOnlyList<string> Files, IReadOnlyList<string> Folders) AddParts(string vault, Profile profile)
	{
		var files = new List<string>();
		var folders = new List<string>();
		string today = DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

		void EnsureFolder(string relative)
		{
			string[] parts = relative.Split('/', '\\');
			for (int depth = 1; depth <= parts.Length; depth++)
			{
				string partial = Path.Combine(parts.Take(depth).ToArray());
				if (!Directory.Exists(Path.Combine(vault, partial)))
				{
					Directory.CreateDirectory(Path.Combine(vault, partial));
					folders.Add(partial);
				}
			}
		}

		void WriteIfAbsent(string relative, string content)
		{
			string path = Path.Combine(vault, relative);
			if (!File.Exists(path))
			{
				EnsureFolder(Path.GetDirectoryName(relative)!);
				AtomicFile.WriteAllText(path, content);
				files.Add(relative);
			}
		}

		try
		{
			foreach (string folder in profile.Folders)
			{
				EnsureFolder(folder);
				if (profile.FolderNotes)
				{
					string leaf = folder.Split('/', '\\').Last();
					WriteIfAbsent(Path.Combine(folder, leaf + ".md"), Note("index", $"Folder note for {leaf}.", leaf, today));
				}
			}

			if (profile.VaultMap)
			{
				WriteIfAbsent(VaultMapPath, Note("index", "Top-level map of this vault.", "Vault Map", today));
			}

			foreach ((string name, string content) in profile.Templates)
			{
				WriteIfAbsent(Path.Combine(SystemFolder, "Templates", name), content);
			}
		}
		catch
		{
			// A failed line offers no undo, so it must not leave its first half behind.
			foreach (string relative in files)
			{
				File.Delete(Path.Combine(vault, relative));
			}

			foreach (string relative in Enumerable.Reverse(folders))
			{
				Directory.Delete(Path.Combine(vault, relative));
			}

			throw;
		}

		return (files, folders);
	}

	static string Note(string type, string summary, string title, string today) => $"""
		---
		type: {type}
		status: draft
		priority: medium
		tags: []
		aliases: []
		summary: "{summary}"
		related: []
		created: {today}
		updated: {today}
		---

		# {title}

		""";
}
