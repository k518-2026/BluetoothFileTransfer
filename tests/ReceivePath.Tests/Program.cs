using BluetoothFileTransfer;

// 受信フォルダーは検査プログラムの出力フォルダーの中に作る（Downloads や C:\ には書かない）
string root = Path.Combine(AppContext.BaseDirectory, "recv-test");
if (Directory.Exists(root)) Directory.Delete(root, true);
string recv = Path.Combine(root, "受信");
Directory.CreateDirectory(recv);
string recvFull = Path.GetFullPath(recv);

int pass = 0, fail = 0;
void Check(string name, bool ok, string detail = "")
{
    if (ok) pass++; else fail++;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}  {detail}");
}

// 受信フォルダーの中（直下）に収まっているか
bool Inside(string p) => string.Equals(Path.GetDirectoryName(p), recvFull, StringComparison.OrdinalIgnoreCase);

string R(string remote) => ReceivePath.Resolve(recv, remote);

// 1) ..\ で外に出ようとする名前
foreach (var evil in new[] { @"..\evil.exe", @"..\..\..\Windows\evil.dll", "../evil.txt", @"sub\..\..\evil.txt", "..", "...", @"..\" })
{
    string p = R(evil);
    Check($"..\\ で外に出られない: {evil}", Inside(p) && !Path.GetFileName(p).StartsWith(".."), Path.GetFileName(p));
}

// 2) 絶対パス・UNC・ドライブ相対
foreach (var evil in new[] { @"C:\Windows\System32\evil.dll", @"C:evil.txt", @"\\server\share\evil.txt", @"\evil.txt", "/etc/passwd" })
{
    string p = R(evil);
    Check($"絶対パスでも受信フォルダーの中: {evil}", Inside(p), Path.GetFileName(p));
}
Check("絶対パスはファイル名だけ残す", Path.GetFileName(R(@"C:\Windows\System32\evil.dll")) == "evil.dll");

// 3) 代替データストリーム（「:」）
foreach (var ads in new[] { "report.txt:hidden", "report.txt::$DATA", "a:b:c.txt" })
{
    string name = Path.GetFileName(R(ads));
    Check($"「:」を残さない（ADS 不可）: {ads}", Inside(R(ads)) && !name.Contains(':'), name);
}

// 4) 予約名・空・末尾のドットや空白
Check("CON は使わない", !string.Equals(Path.GetFileNameWithoutExtension(R("CON")), "CON", StringComparison.OrdinalIgnoreCase), Path.GetFileName(R("CON")));
Check("nul.txt は使わない", !Path.GetFileName(R("nul.txt")).Equals("nul.txt", StringComparison.OrdinalIgnoreCase), Path.GetFileName(R("nul.txt")));
Check("COM1.csv は使わない", Path.GetFileName(R("COM1.csv")).StartsWith("_"), Path.GetFileName(R("COM1.csv")));
Check("空の名前は received_file", Path.GetFileName(R("")) == "received_file");
Check("空白だけでも received_file", Path.GetFileName(R("   ")) == "received_file");
Check("末尾のドットと空白は取る", Path.GetFileName(R("evil.exe. . ")) == "evil.exe", Path.GetFileName(R("evil.exe. . ")));
Check("使えない文字は _ に", Path.GetFileName(R("a*b?c<d>e|f\"g.txt")) == "a_b_c_d_e_f_g.txt", Path.GetFileName(R("a*b?c<d>e|f\"g.txt")));
Check("長すぎる名前は 200 文字まで（拡張子は残す）", Path.GetFileName(R(new string('a', 500) + ".pdf")).Length <= 200 && R(new string('a', 500) + ".pdf").EndsWith(".pdf"));

// 5) 普通の名前はそのまま
Check("普通の日本語の名前はそのまま", Path.GetFileName(R("会議の資料 第2版.pdf")) == "会議の資料 第2版.pdf");

// 6) 上書きしない（番号を付ける）
File.WriteAllText(Path.Combine(recv, "report.csv"), "existing");
string p2 = R("report.csv");
Check("同じ名前があれば (2) を付ける", Path.GetFileName(p2) == "report (2).csv", Path.GetFileName(p2));
File.WriteAllText(p2, "second");
Check("さらにあれば (3)", Path.GetFileName(R("report.csv")) == "report (3).csv", Path.GetFileName(R("report.csv")));
Check("既存のファイルは変わらない", File.ReadAllText(Path.Combine(recv, "report.csv")) == "existing");

// 7) 外にファイルが作られていない
Check("受信フォルダーの外には何も無い", Directory.GetFileSystemEntries(root).Length == 1);

Console.WriteLine($"\n合格 {pass} / 不合格 {fail}");
Directory.Delete(root, true);
return fail == 0 ? 0 : 1;
