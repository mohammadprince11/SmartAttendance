using Microsoft.Extensions.DependencyInjection;

namespace ZynoraHR.Mobile;

public partial class App : Application
{
	public App()
	{
		UiLocalization.ConfigureCulture();
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}
}