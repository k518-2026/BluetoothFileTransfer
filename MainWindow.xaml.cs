using System;
using System.Collections.Generic;
using System.Windows;
using InTheHand.Net.Sockets;

namespace BluetoothFileTransfer
{
    public partial class MainWindow : Window
    {
        private BluetoothService _bluetoothService;

        public MainWindow()
        {
            InitializeComponent();
            _bluetoothService = new BluetoothService();

            // サービスからのログメッセージをUI（テキストボックス）に反映
            _bluetoothService.OnLogMessage += message =>
            {
                // UIスレッドで実行する必要があるためDispatcherを使用
                Dispatcher.Invoke(() =>
                {
                    LogTextBlock.Text += $"[{DateTime.Now:HH:mm:ss}] {message}\n";
                    LogScrollViewer.ScrollToEnd(); // 常に最新のログが見えるように自動スクロール
                });
            };

            // 受信時のプログレスバー（進捗状況）をUIに反映
            _bluetoothService.OnProgressChanged += percent =>
            {
                Dispatcher.Invoke(() =>
                {
                    TransferProgressBar.Value = percent;
                });
            };
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // アプリ起動と同時にバックグラウンドで受信待機を開始
            _bluetoothService.StartListening();
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            // 検索中はボタンを無効化して多重実行を防止（堅牢性）
            SearchButton.IsEnabled = false;
            DeviceListBox.ItemsSource = null;

            var devices = await _bluetoothService.DiscoverDevicesAsync();
            DeviceListBox.ItemsSource = devices;

            // デバイスが見つからなかった場合のOS設定変更の案内表示
            if (devices == null || devices.Count == 0)
            {
                string helpMessage =
                    "デバイスが見つかりませんでした。\n\n" +
                    "【解決方法：受信側PCの設定を変更してください】\n" +
                    "Windowsの初期設定では、他のPCからのBluetooth検索をブロックしています。\n\n" +
                    "1. Windowsの「設定」>「Bluetooth とデバイス」>「デバイス」を開く\n" +
                    "2. 下へスクロールし「Bluetooth デバイスの検出」を探す\n" +
                    "3. 設定を「既定」から「詳細」に変更する\n\n" +
                    "※設定変更後も表示されない場合は、Windows標準の設定画面から手動でペアリングを完了させてから再度検索してください。";

                MessageBox.Show(helpMessage, "デバイス未検出", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            SearchButton.IsEnabled = true;
        }

        private void FileDropArea_DragEnter(object sender, DragEventArgs e)
        {
            // ドラッグ中のマウスカーソルのアイコンを変更
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effects = DragDropEffects.Copy;
            else
                e.Effects = DragDropEffects.None;
        }

        private async void FileDropArea_Drop(object sender, DragEventArgs e)
        {
            // 全体を保護し、予期せぬエラーでのクラッシュを完全に防ぐ
            try
            {
                if (DeviceListBox.SelectedItem is not BluetoothDeviceInfo targetDevice)
                {
                    MessageBox.Show("左側のリストから送信先のデバイスを選択してください。", "未選択", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    string filePath = files[0]; // 複数ドロップされても最初の1ファイルを対象とする

                    // 拡張子のバリデーション（ここで不正なファイルは弾かれる）
                    var validationResult = FileValidator.Validate(filePath);
                    if (!validationResult.IsValid)
                    {
                        MessageBox.Show(validationResult.ErrorMessage, "フォーマットエラー", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // --- 事前ペアリングチェックを撤去し、直接送信処理へ ---

                    // 転送開始前にプログレスバーを0にリセット
                    TransferProgressBar.Value = 0;
                    var progress = new Progress<int>(percent =>
                    {
                        TransferProgressBar.Value = percent;
                    });

                    // 送信処理（ここで接続に失敗した場合は内部で安全に例外となり、isSuccessがfalseになる）
                    bool isSuccess = await _bluetoothService.SendFileAsync(targetDevice, filePath, progress);

                    // 送信タイムアウトなどのエラー時のクリーンアップ
                    if (!isSuccess)
                    {
                        RemoveOfflineDevice(targetDevice);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"予期せぬエラーが発生しました: {ex.Message}", "システムエラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // オフライン（履歴）のデバイスをリストから安全に削除し、警告を出す共通メソッド
        private void RemoveOfflineDevice(BluetoothDeviceInfo targetDevice)
        {
            var currentList = DeviceListBox.ItemsSource as List<BluetoothDeviceInfo>;
            if (currentList != null)
            {
                currentList.Remove(targetDevice);
                DeviceListBox.ItemsSource = null; // 一旦nullにしてUIのバインディングをリセット
                DeviceListBox.ItemsSource = currentList;
            }

            MessageBox.Show(
                $"{targetDevice.DeviceName} への接続を確立できませんでした。\n電源が切れているか、過去のペアリング履歴である可能性が高いためリストから自動削除しました。",
                "通信エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}