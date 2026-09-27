namespace DiCAN.Core.Settings;

// Stores window placement data.
public readonly record struct WindowPlacement(
    double X,
    double Y,
    double Width,
    double Height,
    bool Maximized,
    double BottomPanelHeight,

    double SidebarWidth = double.NaN,
    bool SidebarOpen = true)
{

    public const double NotRemembered = double.NaN;

    public static readonly WindowPlacement Default =
        new(double.NaN, double.NaN, 1200, 660, false, DefaultBottomPanelHeight);

    public const double DefaultBottomPanelHeight = 300;

    public const double MinimumBottomPanelHeight = 130;

    public bool HasPosition => !double.IsNaN(X) && !double.IsNaN(Y);

    // Limits the requested value.
    public WindowPlacement ClampTo(double workX, double workY, double workWidth, double workHeight)
    {

        double width = Sane(Width, Default.Width);
        double height = Sane(Height, Default.Height);

        width = Math.Min(width, workWidth);
        height = Math.Min(height, workHeight);

        double panel = Math.Clamp(
            Sane(BottomPanelHeight, DefaultBottomPanelHeight),
            MinimumBottomPanelHeight,
            Math.Max(MinimumBottomPanelHeight, height * 0.6));

        if (!HasPosition)
        {
            return this with
            {
                X = double.NaN,
                Y = double.NaN,
                Width = width,
                Height = height,
                BottomPanelHeight = panel,
            };
        }

        double x = Math.Clamp(X, workX, Math.Max(workX, workX + workWidth - width));
        double y = Math.Clamp(Y, workY, Math.Max(workY, workY + workHeight - height));

        return this with
        {
            X = x,
            Y = y,
            Width = width,
            Height = height,
            BottomPanelHeight = panel,
        };
    }

    // Checks numeric limits.
    private static double Sane(double value, double fallback) =>
        double.IsFinite(value) && value > 0 ? value : fallback;
}
