using System;
using System.Windows;
namespace ClearView
{
    internal static class Program
    {
        [STAThread] static int Main(string[] args)
        {
            if (args.Length == 3 && args[0] == "--test") return Checks.Run(args[1], args[2]);
            if (args.Length == 2 && args[0] == "--make-fixtures") return Checks.Generate(args[1]);
            var app = new Application(); app.DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e) { MessageBox.Show("작업을 완료하지 못했습니다.\n" + e.Exception.Message, "ClearView"); e.Handled = true; };
            bool uiTest = args.Length == 3 && args[0] == "--ui-test";
            var window = new MainWindow(uiTest ? new string[0] : args);
            if (uiTest) window.ContentRendered += async delegate { await window.RunUiChecks(args[1], args[2]); };
            return app.Run(window);
        }
    }
}
