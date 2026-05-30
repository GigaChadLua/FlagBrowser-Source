namespace FlagInjector;
static class Program
{
    [STAThread]
    static void Main()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
            MessageBox.Show(e.Exception.ToString(), "Thread Exception", MessageBoxButtons.OK, MessageBoxIcon.Error);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            MessageBox.Show(e.ExceptionObject.ToString(), "Unhandled Exception", MessageBoxButtons.OK, MessageBoxIcon.Error);
        try
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Startup Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
