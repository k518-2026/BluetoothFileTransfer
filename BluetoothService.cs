using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using InTheHand.Net.Bluetooth;
using InTheHand.Net.Sockets;

namespace BluetoothFileTransfer
{
    public class BluetoothService
    {
        // アプリケーション固有のUUID（送受信機間で一致させる必要があります）
        private static readonly Guid AppServiceUuid = new Guid("11111111-2222-3333-4444-555555555555");

        // UIへログや進捗を通知するためのイベント
        public event Action<string> OnLogMessage;
        public event Action<int> OnProgressChanged;

        private BluetoothListener _listener;
        private bool _isListening;

        /// <summary>
        /// デバイスの探索を行います。
        /// </summary>
        public async Task<List<BluetoothDeviceInfo>> DiscoverDevicesAsync()
        {
            OnLogMessage?.Invoke("デバイスを検索中...");
            return await Task.Run(() =>
            {
                using var client = new BluetoothClient();
                // 最新の32feet.NETでは引数なしで探索します
                var devices = client.DiscoverDevices().ToList();
                OnLogMessage?.Invoke($"{devices.Count} 件のデバイスが見つかりました（過去の履歴を含む）。");
                return devices;
            });
        }

        /// <summary>
        /// 指定したデバイスとペアリング（接続確認）します。
        /// オフライン履歴の場合はアプリを落とさず false を返します。
        /// </summary>
        public async Task<bool> PairDeviceAsync(BluetoothDeviceInfo device)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (device.Authenticated) return true;
                    OnLogMessage?.Invoke($"{device.DeviceName} と接続確認しています...");
                    return BluetoothSecurity.PairRequest(device.DeviceAddress, null);
                }
                catch (Exception ex)
                {
                    OnLogMessage?.Invoke($"接続確認エラー: {ex.Message}");
                    return false; // クラッシュさせずに失敗として扱う
                }
            });
        }

        /// <summary>
        /// 受信サーバーを起動し、バックグラウンドで待機します。
        /// </summary>
        public void StartListening()
        {
            if (_isListening) return;

            try
            {
                _listener = new BluetoothListener(AppServiceUuid);
                _listener.Start();
                _isListening = true;
                OnLogMessage?.Invoke("ファイルの受信待機を開始しました。");

                Task.Run(() => ListenLoop());
            }
            catch (Exception ex)
            {
                OnLogMessage?.Invoke($"受信待機エラー: Bluetoothが有効か確認してください。({ex.Message})");
            }
        }

        private void ListenLoop()
        {
            string saveDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

            while (_isListening)
            {
                try
                {
                    using BluetoothClient client = _listener.AcceptBluetoothClient();
                    OnLogMessage?.Invoke("送信元からの接続を受け付けました。受信を開始します...");
                    OnProgressChanged?.Invoke(0); // 進捗をリセット

                    using var stream = client.GetStream();
                    using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8);

                    // 独自ヘッダーの読み取り
                    string fileName = reader.ReadString();
                    long fileSize = reader.ReadInt64();
                    string savePath = Path.Combine(saveDirectory, fileName);

                    // ファイルの保存
                    using (var fileStream = File.Create(savePath))
                    {
                        byte[] buffer = new byte[81920]; // 80KBチャンク
                        int bytesRead;
                        long totalRead = 0;

                        while (totalRead < fileSize && (bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            fileStream.Write(buffer, 0, bytesRead);
                            totalRead += bytesRead;

                            // 進捗率(%)を計算して通知
                            if (fileSize > 0)
                            {
                                int progress = (int)((double)totalRead / fileSize * 100);
                                OnProgressChanged?.Invoke(progress);
                            }
                        }
                    }
                    OnLogMessage?.Invoke($"受信完了: {fileName}\n保存先: {saveDirectory}");
                    OnProgressChanged?.Invoke(100); // 完了として100%にする
                }
                catch (Exception ex)
                {
                    if (_isListening) OnLogMessage?.Invoke($"受信エラー: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 選択されたデバイスにファイルを送信します（進捗通知対応）。
        /// </summary>
        public async Task<bool> SendFileAsync(BluetoothDeviceInfo targetDevice, string filePath, IProgress<int> progress)
        {
            return await Task.Run(() =>
            {
                try
                {
                    OnLogMessage?.Invoke($"{targetDevice.DeviceName} へ接続中...");
                    using var client = new BluetoothClient();

                    // ※相手がオフラインの履歴の場合、ここで例外が発生し安全にcatchへ飛びます
                    client.Connect(targetDevice.DeviceAddress, AppServiceUuid);

                    OnLogMessage?.Invoke("送信中...");
                    using var stream = client.GetStream();
                    using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8);
                    using var fileStream = File.OpenRead(filePath);

                    // ヘッダーの書き込み
                    writer.Write(Path.GetFileName(filePath));
                    long fileSize = fileStream.Length;
                    writer.Write(fileSize);

                    // チャンクごとに分割して送信しながら進捗を報告
                    byte[] buffer = new byte[81920];
                    int bytesRead;
                    long totalSent = 0;

                    while ((bytesRead = fileStream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        stream.Write(buffer, 0, bytesRead);
                        totalSent += bytesRead;

                        if (fileSize > 0)
                        {
                            int percent = (int)((double)totalSent / fileSize * 100);
                            progress?.Report(percent);
                        }
                    }

                    OnLogMessage?.Invoke($"送信完了: {Path.GetFileName(filePath)}");
                    return true;
                }
                catch (Exception ex)
                {
                    OnLogMessage?.Invoke($"送信失敗: 相手がオフラインか通信圏外です。({ex.Message})");
                    return false;
                }
            });
        }
    }

    /// <summary>
    /// ドロップされたファイルの拡張子を検証するクラス
    /// </summary>
    public static class FileValidator
    {
        // 許可する拡張子の一覧
        private static readonly string[] AllowedExtensions = {
            ".csv", ".json",
            ".pdf", ".docx", ".xlsx",
            ".md", ".txt", ".gif"
        };

        public static (bool IsValid, string ErrorMessage) Validate(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return (false, "ファイルが存在しません。");

            string extension = Path.GetExtension(filePath).ToLowerInvariant();

            if (!AllowedExtensions.Contains(extension))
            {
                string allowedList = string.Join(", ", AllowedExtensions);
                return (false, $"エラー: {extension} 形式は許可されていません。\n許可されている形式: {allowedList}");
            }

            return (true, string.Empty);
        }
    }
}