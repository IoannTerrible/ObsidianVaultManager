using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace VaultManager.App.Views;

public sealed partial class VaultPageView : UserControl
{
	public VaultPageView() => AvaloniaXamlLoader.Load(this);
}
