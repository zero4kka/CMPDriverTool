using System.Text;
using System.Windows;

namespace CMPDriverTool.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }
        catch { }

        base.OnStartup(e);
    }
}
