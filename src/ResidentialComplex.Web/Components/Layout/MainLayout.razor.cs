using Microsoft.AspNetCore.Components;

namespace ResidentialComplex.Web.Components.Layout;

public partial class MainLayout : LayoutComponentBase
{
    private bool drawerOpen = false;
    private GlobalErrorBoundary? errorBoundary;

    [Inject] public IConfiguration Configuration { get; set; }

    private void ToggleDrawer() => drawerOpen = !drawerOpen;

    private void RecoverFromError() => errorBoundary?.Recover();
}
