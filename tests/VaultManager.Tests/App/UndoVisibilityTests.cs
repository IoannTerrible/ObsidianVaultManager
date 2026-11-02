using VaultManager.App.Localization;
using VaultManager.App.Logic;
using VaultManager.App.ViewModels;
using VaultManager.Core.Obsidian;

namespace VaultManager.Tests.App;

/// <summary>Undo records live for weeks, and a panel kept up by them would never leave the screen.</summary>
[Collection(AppCollection.Name)]
public sealed class UndoVisibilityTests
{
	[Fact]
	public Task Undo_sits_in_the_panel_for_this_session_and_in_the_menu_after_a_restart() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		world.Process.IsRunning = false;
		string notes = world.Vault("Notes");
		string before = Fixture.VersionOf(notes, "obsidian-excalidraw-plugin")!;
		await world.Shell.Changes.Run(Plans.UpdateBehind(world.Plugin("obsidian-excalidraw-plugin"), world.Shell.Data.Snapshot!));
		Assert.True(world.Shell.Changes.ShowUndo);
		Assert.True(world.Shell.Changes.ShowPanel);
		Assert.NotEqual(before, Fixture.VersionOf(notes, "obsidian-excalidraw-plugin"));

		ShellViewModel after = world.Restart();
		await after.StartAsync();

		Assert.False(after.Changes.ShowUndo);
		Assert.False(after.Changes.ShowPanel);
		after.ToggleMenuCommand.Execute(null);
		MenuItemVm item = Assert.Single(after.MenuItems, m => m.Text.StartsWith(Texts.Format(K.Menu_UndoLast, string.Empty).TrimEnd(')', '('), StringComparison.Ordinal));

		item.Invoke();
		for (int i = 0; i < 300 && Fixture.VersionOf(notes, "obsidian-excalidraw-plugin") != before; i++)
		{
			await Task.Delay(10);
		}

		Assert.Equal(before, Fixture.VersionOf(notes, "obsidian-excalidraw-plugin"));
	});

	[Fact]
	public Task With_nothing_to_undo_the_menu_does_not_offer_it() => Ui.Run(async () =>
	{
		using TestWorld world = await TestWorld.StartAsync();
		world.Shell.ToggleMenuCommand.Execute(null);
		Assert.DoesNotContain(world.Shell.MenuItems, m => m.Text.StartsWith(Texts.Format(K.Menu_UndoLast, string.Empty).TrimEnd(')', '('), StringComparison.Ordinal));
	});
}
