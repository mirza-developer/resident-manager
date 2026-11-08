using Microsoft.AspNetCore.Components;
using System.Globalization;

namespace ResidentialComplex.Web.Components.Layout;

public partial class MainLayout : LayoutComponentBase
{
    private bool drawerOpen = false;
    private GlobalErrorBoundary? errorBoundary;
    private PersianCalendar persianCalendar = new();

    [Inject] public IConfiguration Configuration { get; set; }

    private void ToggleDrawer() => drawerOpen = !drawerOpen;

    private void RecoverFromError() => errorBoundary?.Recover();
}
