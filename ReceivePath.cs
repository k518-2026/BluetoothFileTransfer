using System;
using System.IO;
using System.Linq;

namespace BluetoothFileTransfer
{
    /// <summary>
    /// 受け取ったファイルの保存先を決める（2026-11-02 追加）。
    /// 送り手が付けたファイル名は信用しない。「..\」や絶対パス、「名前:ストリーム」（NTFS の代替データストリーム）で
    /// 受信フォルダーの外や別の場所に書かせないよう、ただのファイル名に直してから、受信フォルダーの中に収まることを確かめる。
    /// </summary>
    public static class ReceivePath
    {
        private const string Fallback = "received_file";
        private const int MaxNameLength = 200;

        private static readonly string[] ReservedNames =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        /// <summary>送り手のファイル名を、フォルダーを含まない安全なファイル名にする。</summary>
        public static string SanitizeFileName(string remoteName)
        {
            string name = remoteName ?? "";

            // フォルダー部分を落とす（\ と / の両方。「C:evil」のようなドライブ相対も最後の部分だけにする）
            int cut = Math.Max(name.LastIndexOf('\\'), name.LastIndexOf('/'));
            if (cut >= 0) name = name.Substring(cut + 1);
            int colon = name.IndexOf(':');
            if (colon == 1 && char.IsLetter(name[0])) name = name.Substring(2);

            // 使えない文字（: * ? " < > | と制御文字）は _ に置き換える。「:」を残すと代替データストリームになる
            char[] invalid = Path.GetInvalidFileNameChars();
            name = new string(name.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray());

            // Windows は末尾の空白と「.」を黙って捨てるので、先に取る（「evil.exe.」対策）
            name = name.Trim().TrimEnd('.', ' ');

            if (name.Length == 0 || name.All(c => c == '.')) name = Fallback;

            // 予約された名前（CON・NUL・COM1 など。拡張子が付いていても同じ扱い）
            string stem = name.Split('.')[0].Trim();
            if (ReservedNames.Contains(stem, StringComparer.OrdinalIgnoreCase)) name = "_" + name;

            if (name.Length > MaxNameLength)
            {
                string ext = Path.GetExtension(name);
                if (ext.Length > 20) ext = "";
                name = name.Substring(0, MaxNameLength - ext.Length) + ext;
            }
            return name;
        }

        /// <summary>
        /// 受信フォルダーの中の保存先を返す。同じ名前があれば「名前 (2).拡張子」のように番号を付け、上書きしない。
        /// 万一フォルダーの外を指したら例外にする。
        /// </summary>
        public static string Resolve(string receiveFolder, string remoteName)
        {
            string root = Path.GetFullPath(receiveFolder);
            string rootWithSep = root.EndsWith(Path.DirectorySeparatorChar.ToString()) ? root : root + Path.DirectorySeparatorChar;

            string name = SanitizeFileName(remoteName);
            string stem = Path.GetFileNameWithoutExtension(name);
            string ext = Path.GetExtension(name);

            for (int i = 1; i < 10000; i++)
            {
                string candidate = i == 1 ? name : $"{stem} ({i}){ext}";
                string full = Path.GetFullPath(Path.Combine(root, candidate));

                if (!full.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(Path.GetDirectoryName(full), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("受信フォルダーの外に保存しようとしたため、受け取りを中止しました: " + remoteName);

                if (!File.Exists(full) && !Directory.Exists(full)) return full;
            }
            throw new IOException("同じ名前のファイルが多すぎるため、保存先を決められませんでした: " + name);
        }
    }
}
