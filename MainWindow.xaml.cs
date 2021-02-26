﻿using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Security.Cryptography;
using NLog;
using Skyzi000.Cryptography;
using System.Diagnostics;
using Path = System.IO.Path;

namespace SkyziBackup
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public static BackupSettings GlobalBackupSettings = BackupSettings.GetGlobalSettings();
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly SynchronizationContext _mainContext;

        private BackupSettings LoadCurrentSettings => BackupSettings.LoadLocalSettingsOrNull(Path.TrimEndingDirectorySeparator(originPath.Text.Trim()), Path.TrimEndingDirectorySeparator(destPath.Text.Trim())) ?? BackupSettings.LoadGlobalSettingsOrNull() ?? GlobalBackupSettings;
        private bool ButtonsIsEnabled
        {
            get => StartBackupButton.IsEnabled;
            set
            {
                StartBackupButton.IsEnabled = value;
                GlobalSettingsMenu.IsEnabled = value;
                LocalSettingsMenu.IsEnabled = value;
                DeleteLocalSettings.IsEnabled = value;
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            _mainContext = SynchronizationContext.Current;
            ContentRendered += (s, e) =>
            {
                password.Password = PasswordManager.LoadPasswordOrNull(LoadCurrentSettings) ?? string.Empty;
            };
        }

       

        private async void StartBackupButton_ClickAsync(object sender, RoutedEventArgs e)
        {
            if (!Directory.Exists(originPath.Text.Trim()))
            {
                MessageBox.Show($"{originPath.Text.Trim()}は存在しません。\n正しいディレクトリパスを入力してください。", $"{App.AssemblyName.Name} - 警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            ButtonsIsEnabled = false;
            var settings = LoadCurrentSettings;
            if (settings.isRecordPassword && settings.IsDifferentPassword(password.Password))
            {
                var changePassword = MessageBox.Show("前回のパスワードと異なります。\nパスワードを変更しますか？\n\n※パスワードを変更する場合、既存のバックアップやデータベースを削除し、\n　再度初めからバックアップし直すことをおすすめします。\n　異なるパスワードでバックアップされたファイルが共存する場合、\n　復元が難しくなります。", $"{App.AssemblyName.Name} - パスワード変更の確認", MessageBoxButton.YesNoCancel);
                switch (changePassword)
                {

                    case MessageBoxResult.Yes:
                        Logger.Info("パスワードを保存");
                        PasswordManager.SavePassword(settings, password.Password);
                        DeleteDatabase();
                        break;
                    case MessageBoxResult.No:
                        if (MessageBox.Show("前回のパスワードを使用します。", App.AssemblyName.Name, MessageBoxButton.OKCancel, MessageBoxImage.Information) == MessageBoxResult.OK && PasswordManager.TryLoadPassword(settings, out string pass))
                            password.Password = pass;
                        else
                            goto case MessageBoxResult.Cancel;
                        break;
                    case MessageBoxResult.Cancel:
                        ButtonsIsEnabled = true;
                        return;
                }
            }
            message.Text = $"\n'{originPath.Text.Trim()}' => '{destPath.Text.Trim()}'";
            message.Text += $"\nバックアップ開始: {DateTime.Now}\n";
            progressBar.Visibility = Visibility.Visible;
            var db = new BackupDirectory(originPath.Text.Trim(), destPath.Text.Trim(), password.Password, settings);
            string m = message.Text;
            db.Results.MessageChanged += (_s, _e) => { _mainContext.Post((d) => { message.Text = m + db.Results.Message + "\n"; }, null); };
            await Task.Run(() => db.StartBackup());
            progressBar.Visibility = Visibility.Hidden;
            ButtonsIsEnabled = true;
        }



        /// <summary>
        /// データベースを削除するかどうかの確認ウィンドウを出してから削除する。
        /// </summary>
        /// <returns>削除したなら true</returns>
        private bool DeleteDatabase()
        {
            string databasePath;
            if (GlobalBackupSettings.isUseDatabase && File.Exists(databasePath = DataContractWriter.GetDatabasePath(Path.TrimEndingDirectorySeparator(originPath.Text.Trim()), Path.TrimEndingDirectorySeparator(destPath.Text.Trim()))))
            {
                var deleteDatabase = MessageBox.Show($"{databasePath}\n上記データベースを削除しますか？\nなお、データベースを削除すると全てのファイルを初めからバックアップすることになります。", $"{App.AssemblyName.Name} - 確認", MessageBoxButton.YesNo);
                switch (deleteDatabase)
                {
                    case MessageBoxResult.Yes:
                        DataContractWriter.DeleteDatabase(Path.TrimEndingDirectorySeparator(originPath.Text.Trim()), Path.TrimEndingDirectorySeparator(destPath.Text.Trim()));
                        MessageBox.Show("データベースを削除しました。");
                        return true;
                    case MessageBoxResult.No:
                    default:
                        return false;
                }
            }
            else
                return false;
        }

        private void RestoreWindowMenu_Click(object sender, RoutedEventArgs e)
        {
            var restoreWindow = new RestoreWindow(destPath.Text.Trim(), originPath.Text.Trim());
            restoreWindow.Show();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void GlobalSettingsMenu_Click(object sender, RoutedEventArgs e)
        {
            new SettingsWindow(ref GlobalBackupSettings).ShowDialog();
            GlobalBackupSettings = BackupSettings.LoadGlobalSettingsOrNull() ?? GlobalBackupSettings;
            password.Password = PasswordManager.LoadPasswordOrNull(LoadCurrentSettings) ?? string.Empty;
        }

        private void LocalSettingsMenu_Click(object sender, RoutedEventArgs e)
        {
            if (LoadCurrentSettings.IsGlobal)
                if (MessageBox.Show("現在のバックアップペアに対応するローカル設定を新規作成します。", $"{App.AssemblyName.Name} - 確認", MessageBoxButton.OKCancel) == MessageBoxResult.Cancel) return;
            new SettingsWindow(Path.TrimEndingDirectorySeparator(originPath.Text.Trim()), Path.TrimEndingDirectorySeparator(destPath.Text.Trim())).ShowDialog();
            password.Password = PasswordManager.LoadPasswordOrNull(LoadCurrentSettings) ?? string.Empty;
        }

        private void ShowCurrentSettings_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(LoadCurrentSettings.ToString(), $"{App.AssemblyName.Name} - 現在の設定");
        }

        private void DeleteLocalSettings_Click(object sender, RoutedEventArgs e)
        {
            var currentSettings = LoadCurrentSettings;
            if (currentSettings.IsGlobal)
            {
                MessageBox.Show("現在のバックアップペアに対応するローカル設定は存在しません。", App.AssemblyName.Name, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                if (MessageBox.Show("現在のバックアップペアに対応するローカル設定を削除します。", $"{App.AssemblyName.Name} - 確認", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.Cancel) return;
                DataContractWriter.Delete(currentSettings);
                password.Password = PasswordManager.LoadPasswordOrNull(LoadCurrentSettings) ?? string.Empty;
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!ButtonsIsEnabled)
            {
                if (MessageBoxResult.Yes != MessageBox.Show("バックアップ実行中です。ウィンドウを閉じますか？\n※バックアップ中に中断するとバックアップ先ファイルが壊れる可能性があります。",
                                                            "確認",
                                                            MessageBoxButton.YesNo,
                                                            MessageBoxImage.Information))
                {
                    e.Cancel = true;
                    return;
                }
            }
        }
    }
}

