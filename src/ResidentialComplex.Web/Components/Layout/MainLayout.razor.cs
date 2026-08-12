using Microsoft.AspNetCore.Components;

namespace ResidentialComplex.Web.Components.Layout;

public partial class MainLayout : LayoutComponentBase
{
    private bool drawerOpen = true;
    private GlobalErrorBoundary? errorBoundary;

    private void ToggleDrawer() => drawerOpen = !drawerOpen;

    private void RecoverFromError() => errorBoundary?.Recover();
}
