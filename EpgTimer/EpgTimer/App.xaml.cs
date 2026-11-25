using System.Reflection;
using System.Windows;

[assembly: AssemblyInformationalVersion("0.10.70.0" + EpgTimer.App.VERSION_TEXT)]

namespace EpgTimer
{
    /// <summary>
    /// App.xaml の相互作用ロジック
    /// </summary>
    public partial class App : Application
    {
#if NETCOREAPP
        public App()
        {
            //.NET 9以降はFluentテーマにして、Windowsのダーク/ライト設定に追従させる
            //(App.xamlに直接ThemeMode属性を書くと.NET Framework版がビルドできなくなるため、ここで設定する)
#pragma warning disable WPF0001
            ThemeMode = ThemeMode.System;
#pragma warning restore WPF0001
        }
#endif
    }
}
