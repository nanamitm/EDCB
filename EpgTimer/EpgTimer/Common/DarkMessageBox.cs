#if NETCOREAPP
using System;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EpgTimer
{
    /// <summary>
    /// System.Windows.MessageBoxの置き換え
    /// EpgTimer名前空間内ではSystem.Windows.MessageBoxよりこちらが優先して解決される
    /// Win32のメッセージボックスはダークモードにならないので、Windowsがダークモードのときだけ
    /// Fluentテーマを使用している場合はWPFのウィンドウで表示する。それ以外はSystem.Windows.MessageBoxを使う
    /// </summary>
    static class MessageBox
    {
        public static MessageBoxResult Show(string messageBoxText)
        {
            return Show(messageBoxText, "", MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None);
        }

        public static MessageBoxResult Show(string messageBoxText, string caption)
        {
            return Show(messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None);
        }

        public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button)
        {
            return Show(messageBoxText, caption, button, MessageBoxImage.None, MessageBoxResult.None);
        }

        public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
        {
            return Show(messageBoxText, caption, button, icon, MessageBoxResult.None);
        }

        public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult)
        {
            Application app = Application.Current;
            if (app != null && IsDarkMode())
            {
                if (app.Dispatcher.CheckAccess() == false)
                {
                    return app.Dispatcher.Invoke(() => Show(messageBoxText, caption, button, icon, defaultResult));
                }
#pragma warning disable WPF0001
                if (app.ThemeMode != ThemeMode.None)
#pragma warning restore WPF0001
                {
                    try
                    {
                        return ShowDark(app, messageBoxText, caption, button, icon, defaultResult);
                    }
                    catch (InvalidOperationException)
                    {
                        //ディスパッチャ停止中などでウィンドウを表示できないときは従来のメッセージボックスにする
                    }
                }
            }
            return System.Windows.MessageBox.Show(messageBoxText, caption, button, icon, defaultResult);
        }

        static bool IsDarkMode()
        {
            if (SystemParameters.HighContrast)
            {
                return false;
            }
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    return key != null && key.GetValue("AppsUseLightTheme") is int v && v == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        static MessageBoxResult ShowDark(Application app, string text, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult)
        {
            var win = new Window
            {
                Title = caption ?? "",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                MinWidth = 280,
            };
            //他のウィンドウと同じフォント設定にする
            if (app.TryFindResource("AppWindowStyle") is Style style && style.TargetType.IsAssignableFrom(typeof(Window)))
            {
                win.Style = style;
            }
            Window owner = app.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible && w != win)
                           ?? (app.MainWindow != null && app.MainWindow.IsVisible ? app.MainWindow : null);
            if (owner != null)
            {
                win.Owner = owner;
                win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }

            //本文(アイコン+テキスト)
            var body = new DockPanel { Margin = new Thickness(24, 20, 24, 20) };
            ImageSource iconSource = GetIconSource(icon);
            if (iconSource != null)
            {
                var image = new Image { Source = iconSource, Width = 32, Height = 32, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 16, 0) };
                DockPanel.SetDock(image, Dock.Left);
                body.Children.Add(image);
            }
            body.Children.Add(new TextBlock
            {
                Text = (text ?? "").Replace("\r\n", "\n"),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = Math.Max(400, SystemParameters.WorkArea.Width / 2),
                VerticalAlignment = VerticalAlignment.Center,
            });

            //ボタン
            MessageBoxResult result = MessageBoxResult.None;
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 12, 16, 12) };
            MessageBoxResult[] results =
                button == MessageBoxButton.OKCancel ? new[] { MessageBoxResult.OK, MessageBoxResult.Cancel } :
                button == MessageBoxButton.YesNo ? new[] { MessageBoxResult.Yes, MessageBoxResult.No } :
                button == MessageBoxButton.YesNoCancel ? new[] { MessageBoxResult.Yes, MessageBoxResult.No, MessageBoxResult.Cancel } :
                new[] { MessageBoxResult.OK };
            if (results.Contains(defaultResult) == false)
            {
                defaultResult = results[0];
            }
            //閉じるボタンやEscキーのときの結果
            MessageBoxResult cancelResult = results.Contains(MessageBoxResult.Cancel) ? MessageBoxResult.Cancel :
                                            results.Length == 1 ? MessageBoxResult.OK : MessageBoxResult.No;
            Button defaultButton = null;
            foreach (MessageBoxResult r in results)
            {
                var b = new Button
                {
                    Content = r == MessageBoxResult.OK ? "OK" : r == MessageBoxResult.Cancel ? "キャンセル" : r == MessageBoxResult.Yes ? "はい(_Y)" : "いいえ(_N)",
                    MinWidth = 88,
                    Margin = new Thickness(8, 0, 0, 0),
                    IsDefault = r == defaultResult,
                    IsCancel = r == cancelResult,
                };
                MessageBoxResult rr = r;
                b.Click += (s, e) => { result = rr; win.Close(); };
                if (b.IsDefault)
                {
                    defaultButton = b;
                }
                buttons.Children.Add(b);
            }
            //ボタン領域はWin32のメッセージボックスと同様に少し色を変える
            var footer = new Border { Child = buttons, Background = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)) };
            DockPanel.SetDock(footer, Dock.Bottom);

            var root = new DockPanel();
            root.Children.Add(footer);
            root.Children.Add(body);
            win.Content = root;
            win.Loaded += (s, e) => defaultButton?.Focus();
            //Ctrl+Cで内容をコピー(Win32のメッセージボックスと同様)
            win.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.C && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0)
                {
                    try { Clipboard.SetText(caption + "\r\n" + text); } catch { }
                }
            };

            PlaySound(icon);
            win.ShowDialog();
            return result == MessageBoxResult.None ? cancelResult : result;
        }

        [DllImport("user32.dll")]
        static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

        static ImageSource GetIconSource(MessageBoxImage icon)
        {
            int id = icon == MessageBoxImage.Error ? 32513 :        //IDI_HAND
                     icon == MessageBoxImage.Question ? 32514 :     //IDI_QUESTION
                     icon == MessageBoxImage.Warning ? 32515 :      //IDI_EXCLAMATION
                     icon == MessageBoxImage.Information ? 32516 :  //IDI_ASTERISK
                     0;
            if (id == 0)
            {
                return null;
            }
            //共有アイコンなので破棄しない
            IntPtr hIcon = LoadIcon(IntPtr.Zero, (IntPtr)id);
            if (hIcon == IntPtr.Zero)
            {
                return null;
            }
            BitmapSource bmp = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bmp.Freeze();
            return bmp;
        }

        static void PlaySound(MessageBoxImage icon)
        {
            (icon == MessageBoxImage.Error ? SystemSounds.Hand :
             icon == MessageBoxImage.Question ? SystemSounds.Question :
             icon == MessageBoxImage.Warning ? SystemSounds.Exclamation :
             icon == MessageBoxImage.Information ? SystemSounds.Asterisk :
             SystemSounds.Beep).Play();
        }
    }
}
#endif
