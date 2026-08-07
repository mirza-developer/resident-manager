using System.Diagnostics;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace ResidentialComplex.Web.Components;

public class GlobalErrorBoundary : ErrorBoundary
{
    [Inject]
    private ILogger<GlobalErrorBoundary> Logger { get; set; } = default!;

    protected override Task OnErrorAsync(Exception exception)
    {
        Logger.LogError(exception, "Unhandled exception caught by error boundary");

        if (Debugger.IsAttached)
        {
            Debugger.Break();
        }

        return Task.CompletedTask;
    }
}
