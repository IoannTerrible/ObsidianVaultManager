using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace VaultManager.App.Views;

public sealed partial class NewVaultView : UserControl
{
	public NewVaultView() => AvaloniaXamlLoader.Load(this);
}
