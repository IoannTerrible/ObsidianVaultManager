using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VaultManager.Core.Profiles;

namespace VaultManager.Core.Changes;

/// <summary>
/// The form a waiting line takes on disk. Each record type is written by hand under its own kind name,
/// so the file can never choose a type, and renaming a class cannot strand a line.
/// </summary>
public static class ChangeItemJson
{
	const string Copy = "copy-plugin";
	const string Download = "download-plugin";
	const string Toggle = "set-plugin-enabled";
	const string Settings = "copy-settings-file";
	const string Parts = "add-profile-parts";
	const string Create = "create-vault";
	const string Register = "register-vault";

	public static string Serialize(ChangeItem item)
	{
		JObject o = item switch
		{
			CopyPlugin c => new JObject
			{
				["kind"] = Copy,
				["targetVault"] = c.TargetVault,
				["pluginId"] = c.PluginId,
				["pluginName"] = c.PluginName,
				["fromVersion"] = c.FromVersion,
				["toVersion"] = c.ToVersion,
				["sourceVault"] = c.SourceVault,
				["copySettings"] = c.CopySettings,
				["expectedTargetHash"] = c.ExpectedTargetHash,
			},
			DownloadPlugin d => new JObject
			{
				["kind"] = Download,
				["targetVault"] = d.TargetVault,
				["pluginId"] = d.PluginId,
				["pluginName"] = d.PluginName,
				["fromVersion"] = d.FromVersion,
				["toVersion"] = d.ToVersion,
				["repo"] = d.Repo,
				["sizeBytes"] = d.SizeBytes,
				["expectedTargetHash"] = d.ExpectedTargetHash,
			},
			SetPluginEnabled s => new JObject
			{
				["kind"] = Toggle,
				["targetVault"] = s.TargetVault,
				["pluginId"] = s.PluginId,
				["pluginName"] = s.PluginName,
				["enabled"] = s.Enabled,
			},
			CopySettingsFile f => new JObject
			{
				["kind"] = Settings,
				["targetVault"] = f.TargetVault,
				["sourceVault"] = f.SourceVault,
				["relativePath"] = f.RelativePath,
				["sourceHash"] = f.SourceHash,
				["expectedTargetHash"] = f.ExpectedTargetHash,
			},
			AddProfileParts p => new JObject
			{
				["kind"] = Parts,
				["targetVault"] = p.TargetVault,
				["profile"] = JToken.Parse(ProfileFile.Serialize(p.Profile)),
			},
			CreateVault v => new JObject
			{
				["kind"] = Create,
				["parent"] = v.Parent,
				["name"] = v.Name,
				["profile"] = v.Profile is null ? JValue.CreateNull() : JToken.Parse(ProfileFile.Serialize(v.Profile)),
				["pluginSources"] = JObject.FromObject(v.PluginSources),
			},
			RegisterVault r => new JObject
			{
				["kind"] = Register,
				["newVault"] = r.NewVault,
			},
			_ => throw new NotSupportedException(item.GetType().Name),
		};
		o["id"] = item.Id.ToString("N");
		return o.ToString(Formatting.None);
	}

	/// <returns>Null for damaged text or a kind this build does not know: a guess here could write to the wrong place.</returns>
	public static ChangeItem? TryParse(string json)
	{
		try
		{
			if (JToken.Parse(json) is not JObject o || !Guid.TryParseExact(o.Value<string>("id"), "N", out Guid id))
			{
				return null;
			}

			ChangeItem? item = o.Value<string>("kind") switch
			{
				Copy => new CopyPlugin(Text(o, "targetVault"), Text(o, "pluginId"), Text(o, "pluginName"), o.Value<string>("fromVersion"), Text(o, "toVersion"), Text(o, "sourceVault"), Flag(o, "copySettings"), o.Value<string>("expectedTargetHash")),
				Download => new DownloadPlugin(Text(o, "targetVault"), Text(o, "pluginId"), Text(o, "pluginName"), o.Value<string>("fromVersion"), Text(o, "toVersion"), Text(o, "repo"), o.Value<long>("sizeBytes"), o.Value<string>("expectedTargetHash")),
				Toggle => new SetPluginEnabled(Text(o, "targetVault"), Text(o, "pluginId"), Text(o, "pluginName"), Flag(o, "enabled")),
				Settings => new CopySettingsFile(Text(o, "targetVault"), Text(o, "sourceVault"), Text(o, "relativePath"), Text(o, "sourceHash"), o.Value<string>("expectedTargetHash")),
				Parts => new AddProfileParts(Text(o, "targetVault"), ProfileOf(o) ?? throw new FormatException("profile")),
				Create => new CreateVault(Text(o, "parent"), Text(o, "name"), ProfileOf(o), SourcesOf(o)),
				Register => new RegisterVault(Text(o, "newVault")),
				_ => null,
			};
			return item is null ? null : item with { Id = id };
		}
		catch (Exception ex) when (ex is JsonException or FormatException or InvalidCastException or ProfileRejectedException)
		{
			return null;
		}
	}

	static string Text(JObject o, string name) => o.Value<string>(name) is { Length: > 0 } text ? text : throw new FormatException(name);

	static bool Flag(JObject o, string name) => o[name] is JValue { Type: JTokenType.Boolean } value ? value.Value<bool>() : throw new FormatException(name);

	// Goes through the same checks as an imported file: this text decides where files get written.
	static Profile? ProfileOf(JObject o) => o["profile"] is JObject profile ? ProfileFile.Parse(profile.ToString(Formatting.None)) : null;

	static Dictionary<string, string> SourcesOf(JObject o)
	{
		var sources = new Dictionary<string, string>(StringComparer.Ordinal);
		if (o["pluginSources"] is JObject map)
		{
			foreach (JProperty source in map.Properties())
			{
				sources[source.Name] = source.Value.Value<string>() ?? throw new FormatException(source.Name);
			}
		}

		return sources;
	}
}
