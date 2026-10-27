namespace WMC.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Visual styles are intentionally NOT enabled (no Application.EnableVisualStyles /
        // ApplicationConfiguration.Initialize): controls render with the classic Win32 look.
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}
