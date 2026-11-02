using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using VaultManager.App.Localization;
using VaultManager.App.Logic;
using VaultManager.App.Services;
using VaultManager.Core.Store;

namespace VaultManager.App;

public static class Program
{
	/// <summary>Family every text falls back to when a role has not set one.</summary>
	const string DefaultFont = "avares://VaultManager/Assets/Fonts#IBM Plex Sans";

	[STAThread]
	public static int Main(string[] args)
	{
		StartOptions options = StartOptions.Parse(args);
		if (options.Smoke)
		{
			return Smoke();
		}

		string? demoRoot = options.Demo ? AppSession.NewDemoRoot() : null;
		if (demoRoot is not null)
		{
			Directory.CreateDirectory(demoRoot);
			AppSession.ConfineTempTo(demoRoot);
		}

		App.Session = demoRoot is null ? AppSession.ForThisUser() : AppSession.ForDemo(demoRoot);
		BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
		return 0;
	}

	public static AppBuilder BuildAvaloniaApp() =>
		AppBuilder.Configure<App>()
			.UsePlatformDetect()
			.With(new FontManagerOptions { DefaultFamilyName = DefaultFont })
			.LogToTrace();

	/// <summary>
	/// A published build has no window to look at in a check, so this proves the parts a single file can
	/// lose: native SQLite and Skia load, the Russian satellite and the fonts are inside.
	/// Opens no window, touches nothing of the user's.
	/// </summary>
	static int Smoke()
	{
		string root = Path.Combine(Path.GetTempPath(), "vault-manager-smoke-" + Guid.NewGuid().ToString("N"));
		try
		{
			Directory.CreateDirectory(root);
			var database = new AppDatabase(Path.Combine(root, "smoke.db"));
			database.SetSetting("smoke", "ok");
			if (database.Setting("smoke") != "ok")
			{
				return 2;
			}

			string? english = Texts.Find(K.Nav_Plugins, AppLanguage.English);
			string? russian = Texts.Find(K.Nav_Plugins, AppLanguage.Russian);
			if (string.IsNullOrEmpty(english) || string.IsNullOrEmpty(russian) || english == russian)
			{
				return 3;
			}

			BuildAvaloniaApp().SetupWithoutStarting();
			bool font = Application.Current!.TryGetResource("Font.Label", ThemeVariant.Dark, out object? found)
				&& found is FontFamily family
				&& FontManager.Current.TryGetGlyphTypeface(new Typeface(family), out IGlyphTypeface? face)
				&& face.GetGlyph('\u0416') != 0;
			return font ? 0 : 4;
		}
		finally
		{
			try
			{
				Directory.Delete(root, recursive: true);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				// Temp is the right place for a leftover; the check already answered.
			}
		}
	}
}
