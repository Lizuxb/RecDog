using System.Windows;

namespace RecDog.Desktop;

public sealed class App : Application
{
    [STAThread]
    public static int Main(string[] args)
    {
        var app = new App { ShutdownMode = ShutdownMode.OnMainWindowClose };
        return app.Run(new WorkbenchWindow(args));
    }
}
