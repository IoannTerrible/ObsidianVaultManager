using System.Globalization;
using System.Reflection;
using System.Resources;

[assembly: NeutralResourcesLanguage("en")]

namespace VaultManager.App.Localization;

public enum AppLanguage { English, Russian }

public enum PluralForm { One, Few, Many }

internal static class Plural
{
	public static PluralForm Choose(int n, AppLanguage language)
	{
		if (language == AppLanguage.English)
		{
			return n == 1 ? PluralForm.One : PluralForm.Many;
		}

		int tens = Math.Abs(n) % 100;
		int ones = tens % 10;
		if (tens is >= 11 and <= 14)
		{
			return PluralForm.Many;
		}

		return ones == 1 ? PluralForm.One : ones is >= 2 and <= 4 ? PluralForm.Few : PluralForm.Many;
	}
}

/// <summary>The language the app runs in. Everything a person reads comes through here.</summary>
internal static class Texts
{
	static readonly ResourceManager Resources = new("VaultManager.App.Localization.Strings", typeof(Texts).Assembly);
	static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");
	static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru");

	public const string SettingKey = "language";

	public static AppLanguage Current { get; private set; } = AppLanguage.English;

	public static CultureInfo Culture => Current == AppLanguage.Russian ? Russian : English;

	public static void Use(AppLanguage language) => Current = language;

	public static string CodeOf(AppLanguage language) => language == AppLanguage.Russian ? "ru" : "en";

	public static AppLanguage FromCode(string? code) => code == "ru" ? AppLanguage.Russian : AppLanguage.English;

	public static string Get(string key) => Resources.GetString(key, Culture) ?? key;

	public static string? Find(string key, AppLanguage language) =>
		Resources.GetString(key, language == AppLanguage.Russian ? Russian : English);

	public static string Format(string key, params object?[] args) => string.Format(Culture, Get(key), args);

	/// <summary>The number and its noun together, in the form the language wants for that number.</summary>
	public static string Count(string pluralKey, int n) => string.Format(Culture, Get(pluralKey + "_" + Plural.Choose(n, Current)), n);

	public static string Caps(string key) => Get(key).ToUpper(Culture);

	/// <summary>"a", "a and b", "a, b and c".</summary>
	public static string Join(IReadOnlyList<string> items) => items.Count switch
	{
		0 => string.Empty,
		1 => items[0],
		_ => string.Join(", ", items.Take(items.Count - 1)) + " " + Get(K.List_And) + " " + items[^1],
	};
}
