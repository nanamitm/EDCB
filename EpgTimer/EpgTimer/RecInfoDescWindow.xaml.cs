using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EpgTimer
{
    /// <summary>
    /// RecInfoDescWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class RecInfoDescWindow : Window
    {
        private RecFileInfo recInfo = null;
        private string pgInfoForProgramText = "";

        public RecInfoDescWindow()
        {
            InitializeComponent();
        }

        public void SetRecInfo(RecFileInfo info)
        {
            recInfo = info;
            EpgEventInfo eventInfo = null;
            if (info.ProgramInfo.Length == 0 && info.EventID != 0xFFFF)
            {
                // 過去番組情報を探してみる
                var arcList = new List<EpgServiceEventInfo>();
                if (CommonManager.CreateSrvCtrl().SendEnumPgArc(new List<long> {
                        0, (long)CommonManager.Create64Key(info.OriginalNetworkID, info.TransportStreamID, info.ServiceID),
                        info.StartTime.ToFileTimeUtc(), info.StartTime.ToFileTimeUtc() + 1 }, ref arcList) == ErrCode.CMD_SUCCESS &&
                    arcList.Count > 0 && arcList[0].eventList.Count > 0)
                {
                    eventInfo = arcList[0].eventList[0];
                }
                else
                {
                    // 番組情報を探してみる
                    eventInfo = CommonManager.Instance.DB.GetPgInfo(info.OriginalNetworkID, info.TransportStreamID,
                                                                    info.ServiceID, info.EventID, false);
                    if (eventInfo == null || eventInfo.StartTimeFlag == 0 || eventInfo.start_time != info.StartTime)
                    {
                        eventInfo = null;
                    }
                }
            }
            string basicInfo = "";
            string extText = "";
            string propertyInfo = "";
            if (eventInfo != null)
            {
                basicInfo = CommonManager.ConvertProgramText(eventInfo, EventInfoTextMode.BasicInfoForProgramText) +
                            CommonManager.ConvertProgramText(eventInfo, EventInfoTextMode.BasicTextForProgramText);
                extText = CommonManager.ConvertProgramText(eventInfo, EventInfoTextMode.ExtendedTextForProgramText);
                propertyInfo = CommonManager.ConvertProgramText(eventInfo, EventInfoTextMode.PropertyInfo);
            }
            else
            {
                // 詳細情報を分離してみる
                basicInfo = info.ProgramInfo;
                // 2個目の空行までマッチ
                Match m = Regex.Match(basicInfo, @"^[\s\S]*?\r?\n\r?\n[\s\S]*?\r?\n\r?\n");
                if (m.Success)
                {
                    propertyInfo = basicInfo.Substring(m.Length);
                    basicInfo = basicInfo.Substring(0, m.Length);
                    // "詳細情報"のとき空行2行までマッチ
                    m = Regex.Match(propertyInfo, @"^詳細情報\r?\n[\s\S]*?\r?\n\r?\n\r?\n");
                    if (m.Success)
                    {
                        extText = propertyInfo.Substring(0, m.Length);
                        propertyInfo = propertyInfo.Substring(m.Length);
                    }
                }
            }
            richTextBox_pgInfo.Document = new FlowDocument(CommonManager.ConvertDisplayText(basicInfo, extText, propertyInfo));
#if NETCOREAPP
#pragma warning disable WPF0001
            // (おそらくバグにより)フォントが継承されないため
            if (Application.Current.ThemeMode != ThemeMode.None)
            {
                richTextBox_pgInfo.Document.FontFamily = richTextBox_pgInfo.FontFamily;
                richTextBox_pgInfo.Document.FontSize = richTextBox_pgInfo.FontSize;
            }
#pragma warning restore WPF0001
#endif
            pgInfoForProgramText = basicInfo + extText + propertyInfo;
            button_save_program.IsEnabled = pgInfoForProgramText.Length > 0;
            textBox_errLog.Text = info.ErrInfo;
            textBox_recFilePath.Text = info.RecFilePath;
            button_rename.IsEnabled = false;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (tabControl.SelectedItem != null)
            {
                ((TabItem)tabControl.SelectedItem).Focus();
            }
        }

        private void textBox_recFilePath_TextChanged(object sender, TextChangedEventArgs e)
        {
            button_rename.IsEnabled = CommonManager.Instance.NWMode == false &&
                                      recInfo != null &&
                                      recInfo.RecFilePath.Length > 0 &&
                                      textBox_recFilePath.Text.Length > 0 &&
                                      recInfo.RecFilePath != textBox_recFilePath.Text;
        }

        private void button_rename_Click(object sender, RoutedEventArgs e)
        {
            if (CommonManager.Instance.NWMode == false && recInfo != null && recInfo.RecFilePath.Length > 0)
            {
                string destPath = null;
                try
                {
                    // 絶対パスであること
                    string path = textBox_recFilePath.Text;
                    if (Path.GetFullPath(path).Equals(path, StringComparison.OrdinalIgnoreCase))
                    {
                        // 拡張子は変更できない
                        if (Path.GetExtension(path).Equals(Path.GetExtension(recInfo.RecFilePath), StringComparison.OrdinalIgnoreCase))
                        {
                            // 移動先のディレクトリは存在しなければならない
                            if (Directory.Exists(Path.GetDirectoryName(path)))
                            {
                                destPath = path;
                            }
                        }
                    }
                }
                catch { }

                if (destPath == null)
                {
                    popup_rename.DataContext = "拡張子または移動先が不正です";
                    popup_rename.IsOpen = true;
                    textBox_recFilePath.Text = recInfo.RecFilePath;
                }
                else if (RenameRecording(recInfo.RecFilePath, destPath))
                {
                    recInfo.RecFilePath = destPath;
                }
                textBox_recFilePath_TextChanged(null, null);
            }
        }

        private bool RenameRecording(string originalPath, string destPath)
        {
            var cmd = CommonManager.CreateSrvCtrl();
            var current = new RecFileInfo();
            bool moved = false;
            try
            {
                if (cmd.SendGetRecInfo(recInfo.ID, ref current) != ErrCode.CMD_SUCCESS ||
                    !string.Equals(current.RecFilePath, originalPath, StringComparison.Ordinal))
                {
                    MessageBox.Show("録画済み情報を確認できませんでした。一覧を更新してからやり直してください。");
                    return false;
                }

                try
                {
                    File.Move(originalPath, destPath);
                    moved = true;
                }
                catch (FileNotFoundException)
                {
                    // 手動で移動済みの場合は、既存ファイルへの関連付けを確認する。
                    bool destinationExists;
                    try
                    {
                        destinationExists = true;
                        if ((File.GetAttributes(destPath) & FileAttributes.Directory) != 0)
                        {
                            throw new IOException("移動先と同名のフォルダーがあります: " + destPath);
                        }
                    }
                    catch (FileNotFoundException)
                    {
                        destinationExists = false;
                    }
                    if (destinationExists &&
                        MessageBox.Show("元の録画ファイルが見つかりません。次のファイルをこの録画済み情報に関連付けますか？\r\n" + destPath,
                            "録画ファイルの関連付け", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
                    {
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("録画ファイルを移動できませんでした。\r\n" + ex.Message);
                return false;
            }

            var change = new RecFileInfo
            {
                ID = current.ID,
                RecFilePath = destPath,
                StartTime = current.StartTime,
                StartTimeEpg = current.StartTimeEpg,
            };
            string error;
            try
            {
                ErrCode err = cmd.SendChgPathRecInfo(new List<RecFileInfo>() { change });
                error = err == ErrCode.CMD_SUCCESS ? null : CommonManager.GetErrCodeText(err) ?? "録画済み情報の更新に失敗しました。";
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            if (error != null)
            {
                // 応答が途切れた場合も、サーバーに反映済みかどうかを確認してから戻す。
                var updated = new RecFileInfo();
                try
                {
                    if (cmd.SendGetRecInfo(recInfo.ID, ref updated) == ErrCode.CMD_SUCCESS)
                    {
                        if (string.Equals(updated.RecFilePath, destPath, StringComparison.Ordinal))
                        {
                            error = null;
                        }
                        else if (moved && string.Equals(updated.RecFilePath, originalPath, StringComparison.Ordinal))
                        {
                            File.Move(destPath, originalPath);
                            moved = false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    error += "\r\n" + ex.Message;
                }

                if (error != null)
                {
                    MessageBox.Show(error + (moved ? "\r\n録画ファイルの移動先: " + destPath : ""));
                    return false;
                }
            }

            MoveRecordingSidecars(originalPath, destPath);
            return true;
        }

        private static void MoveRecordingSidecars(string originalPath, string destPath)
        {
            try
            {
                // 拡張子が付加されたファイルも移動する。
                foreach (string path in Directory.GetFiles(Path.GetDirectoryName(originalPath), Path.GetFileName(originalPath) + ".*"))
                {
                    if (path.Length > originalPath.Length && path.StartsWith(originalPath, StringComparison.OrdinalIgnoreCase))
                    {
                        MoveRecordingSidecar(path, destPath + path.Substring(originalPath.Length));
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("関連ファイルを確認できませんでした。\r\n" + ex.Message);
            }

            try
            {
                string recInfoFolder = IniFileHandler.GetPrivateProfileString("SET", "RecInfoFolder", "", SettingPath.CommonIniPath);
                if (recInfoFolder.Length > 0)
                {
                    foreach (string suffix in new[] { ".err", ".program.txt" })
                    {
                        MoveRecordingSidecar(Path.Combine(recInfoFolder, Path.GetFileName(originalPath) + suffix),
                                             Path.Combine(recInfoFolder, Path.GetFileName(destPath) + suffix));
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("録画情報保存フォルダーを処理できませんでした。\r\n" + ex.Message);
            }
        }

        private static void MoveRecordingSidecar(string source, string destination)
        {
            if (string.Equals(source, destination, StringComparison.Ordinal))
            {
                return;
            }
            try
            {
                File.Move(source, destination);
            }
            catch (FileNotFoundException) { }
            catch (Exception ex)
            {
                MessageBox.Show("移動に失敗しました: " + source + "\r\n" + ex.Message);
            }
        }

        private void button_play_Click(object sender, RoutedEventArgs e)
        {
            if (recInfo != null)
            {
                string errorMessage = CommonManager.Instance.FilePlay(recInfo.RecFilePath, recInfo.ID);
                if (errorMessage != null)
                {
                    popup_play.DataContext = errorMessage;
                    popup_play.IsOpen = true;
                }
            }
        }

        private void button_del_Click(object sender, RoutedEventArgs e)
        {
            if (recInfo != null)
            {
                if (Settings.Instance.ConfirmDelRecInfo)
                {
                    if ((recInfo.RecFilePath.Length > 0 || Settings.Instance.ConfirmDelRecInfoAlways) &&
                        MessageBox.Show("削除してよろしいですか?" +
                                        (recInfo.RecFilePath.Length > 0 ? "\r\n\r\n「録画ファイルも削除する」設定が有効な場合、ファイルも削除されます。" : ""), "確認",
                                        MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.OK) != MessageBoxResult.OK)
                    {
                        return;
                    }
                }
                try
                {
                    CommonManager.CreateSrvCtrl().SendDelRecInfo(new List<uint>() { recInfo.ID });
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.ToString());
                }
            }
            Close();
        }

        private void button_save_program_Click(object sender, RoutedEventArgs e)
        {
            CommonManager.Instance.ShowSaveProgramTextDialog(pgInfoForProgramText);
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.None)
            {
                switch (e.Key)
                {
                    case Key.Escape:
                        Close();
                        e.Handled = true;
                        break;
                }
            }
            popup_rename.IsOpen = false;
            popup_play.IsOpen = false;
        }

        private void button_cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
