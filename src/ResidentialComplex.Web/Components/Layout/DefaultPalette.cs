using MudBlazor;

namespace ResidentialComplex.Web.Components.Layout;
public static class AppTheme
{
    public static MudTheme Default = new()
    {
        PaletteLight = new PaletteLight
        {
            // Brand
            Primary = "#17324D",
            Secondary = "#168C83",
            Tertiary = "#D6A756",

            // Layout
            Background = "#F4F6F8",
            Surface = "#FFFFFF",

            // Navigation
            AppbarBackground = "#17324D",
            AppbarText = "#FFFFFF",
            DrawerBackground = "#17324D",
            DrawerText = "#E7EEF5",
            DrawerIcon = "#AFC3D5",

            // Text and borders
            TextPrimary = "#243447",
            TextSecondary = "#64748B",
            ActionDefault = "#526579",
            Divider = "#E1E7ED",
            LinesDefault = "#DCE3EA",

            // Financial status
            Success = "#21865B",
            Warning = "#D99521",
            Error = "#C74747",
            Info = "#3478C7",

            // Interaction
            ActionDisabled = "#AAB4BF",
            ActionDisabledBackground = "#E8EDF2"
        }
    };
}
