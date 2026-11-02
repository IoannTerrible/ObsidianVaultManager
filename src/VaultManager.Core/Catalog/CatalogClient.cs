using System.Net.Http.Headers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VaultManager.Core.Foundation;
using VaultManager.Core.Obsidian;
using VaultManager.Core.Store;

namespace VaultManager.Core.Catalog;

public sealed record ReleaseFile(string Name, long SizeBytes);

/// <summary>
/// The only door to the network. Nothing here runs by itself: the connection is expensive,
/// so every call follows a click, and sizes are known before a download starts.
/// </summary>
public interface ICatalogClient
{
	/// <summary>Size of the catalog file, for the question asked before fetching it. Null if the server does not say.</summary>
	Task<long?> CatalogSizeAsync(CancellationToken cancellationToken = default);

	Task<IReadOnlyList<CachedCatalogEntry>> FetchCatalogAsync(CancellationToken cancellationToken = default);

	/// <summary>The latest version a plugin's repository publishes. Null when it cannot be told.</summary>
	Task<string?> LatestVersionAsync(string repo, CancellationToken cancellationToken = default);

	Task<IReadOnlyList<ReleaseFile>> ReleaseFilesAsync(string repo, string version, CancellationToken cancellationToken = default);

	Task DownloadReleaseAsync(string repo, string version, string targetDirectory, CancellationToken cancellationToken = default);
}

public sealed class GitHubCatalogClient(HttpClient http) : ICatalogClient
{
	public const string CatalogUrl = "https://raw.githubusercontent.com/obsidianmd/obsidian-releases/master/community-plugins.json";
	static readonly string[] Required = [VaultLayout.MainFile, VaultLayout.ManifestFile];
	static readonly string[] Optional = ["styles.css"];

	static string Asset(string repo, string version, string file) => $"https://github.com/{repo}/releases/download/{Uri.EscapeDataString(version)}/{file}";

	public async Task<long?> CatalogSizeAsync(CancellationToken cancellationToken = default) => await SizeOfAsync(CatalogUrl, cancellationToken).ConfigureAwait(false);

	public async Task<IReadOnlyList<CachedCatalogEntry>> FetchCatalogAsync(CancellationToken cancellationToken = default)
	{
		string json = await http.GetStringAsync(CatalogUrl, cancellationToken).ConfigureAwait(false);
		var entries = new List<CachedCatalogEntry>();
		try
		{
			foreach (JToken token in JArray.Parse(json))
			{
				if (token is JObject o && o.Value<string>("id") is { } id && o.Value<string>("repo") is { } repo && IsRepoName(repo))
				{
					entries.Add(new CachedCatalogEntry(id, o.Value<string>("name") ?? id, o.Value<string>("author") ?? string.Empty, o.Value<string>("description") ?? string.Empty, repo));
				}
			}
		}
		catch (JsonException ex)
		{
			throw new HttpRequestException("Catalog is not readable.", ex);
		}

		return entries;
	}

	public async Task<string?> LatestVersionAsync(string repo, CancellationToken cancellationToken = default)
	{
		if (!IsRepoName(repo))
		{
			return null;
		}

		using HttpResponseMessage response = await http.GetAsync($"https://raw.githubusercontent.com/{repo}/HEAD/{VaultLayout.ManifestFile}", cancellationToken).ConfigureAwait(false);
		if (!response.IsSuccessStatusCode)
		{
			return null;
		}

		try
		{
			return PluginManifest.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false))?.Version;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	public async Task<IReadOnlyList<ReleaseFile>> ReleaseFilesAsync(string repo, string version, CancellationToken cancellationToken = default)
	{
		var files = new List<ReleaseFile>();
		foreach (string name in Required.Concat(Optional))
		{
			long? size = await SizeOfAsync(Asset(repo, version, name), cancellationToken).ConfigureAwait(false);
			if (size is not null)
			{
				files.Add(new ReleaseFile(name, size.Value));
			}
			else if (Required.Contains(name))
			{
				throw new HttpRequestException($"Release has no {name}.");
			}
		}

		return files;
	}

	public async Task DownloadReleaseAsync(string repo, string version, string targetDirectory, CancellationToken cancellationToken = default)
	{
		Directory.CreateDirectory(targetDirectory);
		foreach (string name in Required.Concat(Optional))
		{
			using HttpResponseMessage response = await http.GetAsync(Asset(repo, version, name), cancellationToken).ConfigureAwait(false);
			if (!response.IsSuccessStatusCode)
			{
				if (Required.Contains(name))
				{
					throw new HttpRequestException($"Could not download {name}.");
				}

				continue;
			}

			byte[] content = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
			AtomicFile.WriteAllBytes(Path.Combine(targetDirectory, name), content);
		}
	}

	async Task<long?> SizeOfAsync(string url, CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(HttpMethod.Head, url);
		using HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
		return response.IsSuccessStatusCode ? response.Content.Headers.ContentLength : null;
	}

	/// <summary>Repository names come from a downloaded file and end up in URLs.</summary>
	internal static bool IsRepoName(string repo)
	{
		string[] parts = repo.Split('/');
		return parts.Length == 2 && parts.All(p => p.Length > 0 && p != "." && p != ".." && p.All(c => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '_' or '.'));
	}

	public static void Configure(HttpClient client, string version)
	{
		client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("VaultManager", version));
		client.Timeout = TimeSpan.FromMinutes(2);
	}
}

public sealed record OnlineCheck(IReadOnlyList<OnlineVersion> Versions, IReadOnlyList<string> NotInCatalog, int Failed);

/// <summary>Refreshes the cached catalog and asks about installed plugins only: one small file each.</summary>
public sealed class OnlineChecker(ICatalogClient client, AppDatabase database)
{
	public async Task<OnlineCheck> CheckAsync(IReadOnlyCollection<string> installedPluginIds, CancellationToken cancellationToken = default)
	{
		IReadOnlyList<CachedCatalogEntry> catalog = await client.FetchCatalogAsync(cancellationToken).ConfigureAwait(false);
		database.ReplaceCatalog(catalog);
		Dictionary<string, CachedCatalogEntry> byId = catalog.GroupBy(e => e.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

		var versions = new List<OnlineVersion>();
		var notInCatalog = new List<string>();
		int failed = 0;
		foreach (string id in installedPluginIds)
		{
			if (!byId.TryGetValue(id, out CachedCatalogEntry? entry))
			{
				notInCatalog.Add(id);
				continue;
			}

			string? version = await client.LatestVersionAsync(entry.Repo, cancellationToken).ConfigureAwait(false);
			if (version is null)
			{
				// Unknown stays unknown: no answer is not "up to date".
				failed++;
				continue;
			}

			versions.Add(new OnlineVersion(id, version, DateTimeOffset.UtcNow));
		}

		database.SaveOnlineVersions(versions);
		return new OnlineCheck(versions, notInCatalog, failed);
	}
}
