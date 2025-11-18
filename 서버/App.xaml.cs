using System.Configuration;
using System.Data;
using System.Windows;

namespace DanawaR_Host
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 프로그램 실행될 때 서버 자동 실행
            Server.Start();
        }
    }
}
