using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace GitUpstreamLocal;

public sealed class MainForm : Form
{
    private const string RemoteName = "B";
    private const string IdleText = "UPSTREAM A → B";
    private const string BusyText = "ĐANG ĐỒNG BỘ...";

    private readonly TextBox _sourcePath = new();
    private readonly TextBox _targetPath = new();
    private readonly TextBox _targetUrl = new()
    {
        PlaceholderText = "https://gitlab.com/nhom/repo-b.git  (chỉ cần khi repo B chưa có remote 'B')"
    };
    private readonly Button _sourceBrowse = new() { Text = "Chọn..." };
    private readonly Button _targetBrowse = new() { Text = "Chọn..." };
    private readonly Button _syncButton = new() { Text = IdleText, Height = 42 };
    private readonly TextBox _log = new();

    private const int MaxCommits = 300;
    private readonly Button _refreshButton = new() { Text = "Tải lịch sử" };
    private readonly ListView _historyA = MakeHistoryList();
    private readonly ListView _historyB = MakeHistoryList();
    private readonly Label _infoA = new();
    private readonly Label _infoB = new();
    private readonly SplitContainer _split = new() { Orientation = Orientation.Vertical };
    private readonly System.Windows.Forms.Timer _historyTimer = new() { Interval = 500 };
    private int _historyVersion;
    private bool _busy;

    private static readonly Color OnlyInAColor = Color.FromArgb(255, 232, 200);
    private static readonly Color OnlyInBColor = Color.FromArgb(205, 228, 255);

    private sealed class HistoryData
    {
        public string Branch = "";
        public List<(string Hash, string Author, string Date, string Subject)> Commits = new();
        public HashSet<string> AllHashes = new(StringComparer.OrdinalIgnoreCase);
    }

    public MainForm()
    {
        Text = "Git Upstream A → B (One-way)";
        Width = 1100;
        Height = 800;
        MinimumSize = new Size(800, 600);
        StartPosition = FormStartPosition.CenterScreen;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 3,
            RowCount = 8
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); // A
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); // B
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); // URL remote B
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58)); // button
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); // history legend
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));  // history A | B
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); // log label
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));  // log

        layout.Controls.Add(MakeLabel("Repo A (nguồn):"), 0, 0);
        _sourcePath.Dock = DockStyle.Fill;
        _sourceBrowse.Dock = DockStyle.Fill;
        layout.Controls.Add(_sourcePath, 1, 0);
        layout.Controls.Add(_sourceBrowse, 2, 0);

        layout.Controls.Add(MakeLabel("Repo B (đích):"), 0, 1);
        _targetPath.Dock = DockStyle.Fill;
        _targetBrowse.Dock = DockStyle.Fill;
        layout.Controls.Add(_targetPath, 1, 1);
        layout.Controls.Add(_targetBrowse, 2, 1);

        layout.Controls.Add(MakeLabel("URL remote B:"), 0, 2);
        _targetUrl.Dock = DockStyle.Fill;
        layout.Controls.Add(_targetUrl, 1, 2);
        layout.SetColumnSpan(_targetUrl, 2);

        _syncButton.Dock = DockStyle.Fill;
        _syncButton.Enabled = false;
        layout.Controls.Add(_syncButton, 1, 3);
        _refreshButton.Dock = DockStyle.Fill;
        layout.Controls.Add(_refreshButton, 2, 3);

        var legend = MakeLabel("Lịch sử commit  (cam = commit của A chưa có ở B · xanh = commit chỉ có ở B · nhấp đôi để xem chi tiết)");
        layout.Controls.Add(legend, 0, 4);
        layout.SetColumnSpan(legend, 3);

        _split.Dock = DockStyle.Fill;
        _split.Panel1.Controls.Add(WrapHistory(_infoA, _historyA));
        _split.Panel2.Controls.Add(WrapHistory(_infoB, _historyB));
        layout.Controls.Add(_split, 0, 5);
        layout.SetColumnSpan(_split, 3);

        layout.Controls.Add(MakeLabel("Nhật ký:"), 0, 6);
        _log.Dock = DockStyle.Fill;
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.Font = new Font("Consolas", 9);
        layout.Controls.Add(_log, 0, 7);
        layout.SetColumnSpan(_log, 3);

        Controls.Add(layout);

        _sourceBrowse.Click += (_, _) => ChooseFolder(_sourcePath);
        _targetBrowse.Click += (_, _) => ChooseFolder(_targetPath);
        _sourcePath.TextChanged += (_, _) => { UpdateButton(); ScheduleHistory(); };
        _targetPath.TextChanged += (_, _) => { UpdateButton(); ScheduleHistory(); };
        _syncButton.Click += async (_, _) => await SyncAsync();
        _refreshButton.Click += async (_, _) => await LoadHistoryAsync();
        _historyTimer.Tick += async (_, _) => { _historyTimer.Stop(); await LoadHistoryAsync(); };
        _historyA.DoubleClick += async (_, _) => await ShowCommitAsync(_historyA);
        _historyB.DoubleClick += async (_, _) => await ShowCommitAsync(_historyB);
        Shown += (_, _) => _split.SplitterDistance = Math.Max(200, _split.Width / 2);

        Log("Chọn thư mục A (nguồn) và B (đích).");
        Log("Nếu B chưa phải repo Git hoặc chưa có remote 'B', nhập URL remote B để app tự tạo.");
        Log("A chỉ được đọc. App chỉ push tới remote 'B' trong repo B.");
    }

    private static Label MakeLabel(string text) =>
        new() { Text = text, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill };

    private static ListView MakeHistoryList()
    {
        var list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            HideSelection = false,
            MultiSelect = false,
            Font = new Font("Segoe UI", 9)
        };
        list.Columns.Add("Mã", 70);
        list.Columns.Add("Ngày", 115);
        list.Columns.Add("Tác giả", 110);
        list.Columns.Add("Nội dung commit", 420);
        return list;
    }

    private static Control WrapHistory(Label info, ListView list)
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        info.Dock = DockStyle.Top;
        info.Height = 24;
        info.TextAlign = ContentAlignment.MiddleLeft;
        info.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        panel.Controls.Add(list);
        panel.Controls.Add(info);
        return panel;
    }

    private void ScheduleHistory()
    {
        if (_busy) return; // sẽ tải lại ngay khi đồng bộ xong
        _historyTimer.Stop();
        _historyTimer.Start();
    }

    private async Task LoadHistoryAsync()
    {
        int version = ++_historyVersion;
        string pathA = _sourcePath.Text.Trim();
        string pathB = _targetPath.Text.Trim();

        try
        {
            var taskA = ReadHistory(pathA);
            var taskB = ReadHistory(pathB);
            await Task.WhenAll(taskA, taskB);
            if (version != _historyVersion) return; // có yêu cầu mới hơn

            var a = taskA.Result;
            var b = taskB.Result;

            int onlyA = a == null ? 0 : a.AllHashes.Count(h => b != null && !b.AllHashes.Contains(h));
            int onlyB = b == null ? 0 : b.AllHashes.Count(h => a != null && !a.AllHashes.Contains(h));

            FillHistory(_historyA, pathA, a, b?.AllHashes, OnlyInAColor);
            FillHistory(_historyB, pathB, b, a?.AllHashes, OnlyInBColor);

            _infoA.Text = a == null
                ? "A: (chưa chọn repo hợp lệ)"
                : $"A [{a.Branch}] — {a.AllHashes.Count} commit" + (b != null ? $" · chưa có ở B: {onlyA}" : "");
            _infoB.Text = b == null
                ? "B: (chưa có repo — sẽ được tạo khi đồng bộ)"
                : $"B [{b.Branch}] — {b.AllHashes.Count} commit" + (a != null ? $" · chỉ có ở B: {onlyB}" : "");
        }
        catch (Exception ex)
        {
            _infoA.Text = "Không đọc được lịch sử: " + Redact(ex.Message);
        }
    }

    private async Task<HistoryData?> ReadHistory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path) || !IsGitRepository(path))
            return null;

        var data = new HistoryData();
        try { data.Branch = await Query(path, "branch", "--show-current"); } catch { }
        if (string.IsNullOrWhiteSpace(data.Branch)) data.Branch = "detached";

        try
        {
            string log = await Query(path, "log", "-n", MaxCommits.ToString(),
                "--date=format:%Y-%m-%d %H:%M", "--pretty=format:%H%x1f%an%x1f%ad%x1f%s");
            foreach (var line in SplitLines(log))
            {
                var parts = line.Split('\u001f');
                if (parts.Length >= 4)
                    data.Commits.Add((parts[0], parts[1], parts[2], parts[3]));
            }

            foreach (var hash in SplitLines(await Query(path, "rev-list", "HEAD")))
                data.AllHashes.Add(hash);
        }
        catch
        {
            // Repo chưa có commit nào: để danh sách rỗng.
        }

        return data;
    }

    private static void FillHistory(ListView list, string path, HistoryData? data, HashSet<string>? other, Color markColor)
    {
        list.BeginUpdate();
        try
        {
            list.Items.Clear();
            list.Tag = path;
            if (data == null) return;

            foreach (var c in data.Commits)
            {
                var item = new ListViewItem(c.Hash.Length >= 7 ? c.Hash[..7] : c.Hash) { Tag = c.Hash };
                item.SubItems.Add(c.Date);
                item.SubItems.Add(c.Author);
                item.SubItems.Add(c.Subject);
                if (other != null && !other.Contains(c.Hash))
                    item.BackColor = markColor;
                list.Items.Add(item);
            }
        }
        finally
        {
            list.EndUpdate();
        }
    }

    private async Task ShowCommitAsync(ListView list)
    {
        if (list.SelectedItems.Count == 0 || list.Tag is not string path || list.SelectedItems[0].Tag is not string hash)
            return;

        try
        {
            string text = await Query(path, "show", "--stat", "--format=fuller", hash);
            text = text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);

            using var dialog = new Form
            {
                Text = $"Commit {hash[..7]}",
                Width = 820,
                Height = 520,
                StartPosition = FormStartPosition.CenterParent
            };
            dialog.Controls.Add(new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                WordWrap = false,
                ScrollBars = ScrollBars.Both,
                Font = new Font("Consolas", 9),
                Text = text
            });
            dialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Redact(ex.Message), "Không đọc được commit", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ChooseFolder(TextBox target)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Chọn thư mục repository Git",
            UseDescriptionForTitle = true
        };

        if (Directory.Exists(target.Text))
            dialog.SelectedPath = target.Text;

        if (dialog.ShowDialog(this) == DialogResult.OK)
            target.Text = dialog.SelectedPath;
    }

    private void UpdateButton()
    {
        // Thư mục B có thể chưa tồn tại: app sẽ tạo khi cần.
        _syncButton.Enabled =
            Directory.Exists(_sourcePath.Text.Trim()) &&
            !string.IsNullOrWhiteSpace(_targetPath.Text) &&
            _syncButton.Text != BusyText;
    }

    private async Task SyncAsync()
    {
        string source = Path.GetFullPath(_sourcePath.Text.Trim());
        string target = Path.GetFullPath(_targetPath.Text.Trim());
        string urlInput = _targetUrl.Text.Trim();

        if (SamePath(source, target))
        {
            MessageBox.Show(this, "A và B không được là cùng một thư mục.", "Không hợp lệ",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _syncButton.Enabled = false;
        _syncButton.Text = BusyText;
        _busy = true;
        _log.Clear();

        try
        {
            Log("=== BẮT ĐẦU: CHỈ ĐỒNG BỘ A → B ===");

            // ---------- A: chỉ đọc ----------
            EnsureGitRepository(source, "A");

            string sourceBranch = await Query(source, "branch", "--show-current");
            if (string.IsNullOrWhiteSpace(sourceBranch))
                throw new Exception("Repo A đang ở detached HEAD. Hãy checkout branch nguồn, ví dụ main.");

            string sourceUrl = await GetSourceUrl(source);
            Log($"Nguồn A: {sourceUrl}");
            Log($"Branch nguồn A: {sourceBranch}");

            // ---------- B: tạo thư mục / git init nếu chưa có ----------
            if (!Directory.Exists(target))
            {
                if (!Confirm($"Thư mục B chưa tồn tại:\n{target}\n\nTạo thư mục và khởi tạo repo Git mới?"))
                    throw new Exception("Đã hủy: thư mục B chưa tồn tại.");
                Directory.CreateDirectory(target);
                Log("Đã tạo thư mục B.");
            }

            if (!IsGitRepository(target))
            {
                if (Directory.EnumerateFileSystemEntries(target).Any())
                    throw new Exception("Thư mục B không phải Git repository và không rỗng. " +
                                        "App chỉ tự git init trong thư mục rỗng để tránh ảnh hưởng file của bạn.");

                if (!Confirm($"Thư mục B chưa phải Git repository:\n{target}\n\nChạy 'git init' (branch '{sourceBranch}')?"))
                    throw new Exception("Đã hủy: B chưa phải Git repository.");

                await Git(target, "init", "-b", sourceBranch);
                Log("Đã khởi tạo repo B.");
            }

            string targetBranch = await Query(target, "branch", "--show-current");
            if (string.IsNullOrWhiteSpace(targetBranch))
                throw new Exception("Repo B đang ở detached HEAD. Hãy checkout branch đích, ví dụ main.");

            // ---------- B: tạo remote 'B' nếu chưa có ----------
            await EnsureTargetRemote(target, sourceUrl, urlInput);

            string targetPushUrl = await Query(target, "remote", "get-url", "--push", RemoteName);
            if (SameRemote(sourceUrl, targetPushUrl))
                throw new Exception("Remote B đang trỏ cùng URL với repo A. Đã chặn để tránh ghi ngược hoặc push nhầm vào A.");

            Log($"Đích B: {targetPushUrl}");
            Log($"Branch đích B: {targetBranch}");

            string targetStatus = await Query(target, "status", "--porcelain");
            if (!string.IsNullOrWhiteSpace(targetStatus))
                throw new Exception("Repo B có thay đổi chưa commit. Hãy commit hoặc stash trước khi upstream. App không tự xóa/stash thay đổi.");

            // ---------- Đồng bộ ----------
            Log("1/3 Fetch branch từ A vào repo B...");
            await Git(target, "fetch", "--no-tags", sourceUrl, sourceBranch);

            Log("2/3 Merge commit A vào branch hiện tại của B...");
            try
            {
                await Git(target, "merge", "--no-edit", "FETCH_HEAD");
            }
            catch (Exception mergeEx)
            {
                try { await Query(target, "merge", "--abort"); } catch { }
                throw new Exception("Merge thất bại hoặc có conflict (đã thử hủy merge, A không bị thay đổi).\n" + mergeEx.Message);
            }

            Log("3/3 Push branch B lên remote B...");
            await Git(target, "push", "-u", RemoteName, $"{targetBranch}:{targetBranch}");

            Log("HOÀN TẤT: A → B thành công.");
            MessageBox.Show(this, "Đã upstream A sang B thành công.\nRepo A không nhận bất kỳ thay đổi nào từ B.",
                "Hoàn tất", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log("DỪNG: " + Redact(ex.Message));
            MessageBox.Show(this, Redact(ex.Message), "Không thể upstream",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _syncButton.Text = IdleText;
            _busy = false;
            UpdateButton();
            await LoadHistoryAsync();
        }
    }

    /// <summary>Đảm bảo repo B có remote tên "B"; tạo mới nếu chưa có.</summary>
    private async Task EnsureTargetRemote(string target, string sourceUrl, string urlInput)
    {
        string remotes = await Query(target, "remote");
        bool hasB = SplitLines(remotes).Contains(RemoteName, StringComparer.Ordinal);

        if (!hasB)
        {
            if (string.IsNullOrWhiteSpace(urlInput))
                throw new Exception("Repo B chưa có remote 'B'. Hãy nhập URL remote B (ví dụ URL GitLab) để app tự tạo.");

            if (SameRemote(sourceUrl, urlInput))
                throw new Exception("URL remote B trùng với URL repo A. Đã chặn để tránh push nhầm vào A.");

            await Git(target, "remote", "add", RemoteName, urlInput);
            Log($"Đã tạo remote '{RemoteName}' → {Redact(urlInput)}");
            return;
        }

        string current = await Query(target, "remote", "get-url", "--push", RemoteName);
        if (!string.IsNullOrWhiteSpace(urlInput) && !SameRemote(current, urlInput))
        {
            if (SameRemote(sourceUrl, urlInput))
                throw new Exception("URL remote B trùng với URL repo A. Đã chặn để tránh push nhầm vào A.");

            if (Confirm($"Remote 'B' hiện trỏ tới:\n{Redact(current)}\n\nBạn nhập URL khác:\n{Redact(urlInput)}\n\nCập nhật remote 'B' sang URL mới?"))
            {
                await Git(target, "remote", "set-url", RemoteName, urlInput);
                await Git(target, "remote", "set-url", "--push", RemoteName, urlInput);
                Log($"Đã cập nhật remote '{RemoteName}'.");
            }
            else
            {
                Log($"Giữ nguyên remote '{RemoteName}' hiện tại.");
            }
        }
    }

    private bool Confirm(string message) =>
        MessageBox.Show(this, message, "Xác nhận", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    private async Task<string> GetSourceUrl(string source)
    {
        string remotes = await Query(source, "remote");
        var names = SplitLines(remotes);

        string remoteName =
            names.Contains("origin", StringComparer.Ordinal) ? "origin" :
            names.Contains("upstream", StringComparer.Ordinal) ? "upstream" :
            throw new Exception("Repo A cần có remote 'origin' hoặc 'upstream' trỏ đến repo nguồn.");

        return await Query(source, "remote", "get-url", remoteName);
    }

    private static string[] SplitLines(string text) =>
        text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool SameRemote(string a, string b)
    {
        static string Normalize(string value)
        {
            value = value.Trim().TrimEnd('/', '\\');
            if (value.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                value = value[..^4];
            return value.TrimEnd('/', '\\');
        }

        return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(a.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                      b.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                      StringComparison.OrdinalIgnoreCase);

    private static bool IsGitRepository(string path) =>
        Directory.Exists(Path.Combine(path, ".git")) || File.Exists(Path.Combine(path, ".git"));

    private static void EnsureGitRepository(string path, string label)
    {
        if (!IsGitRepository(path))
            throw new Exception($"Thư mục {label} không phải Git repository: {path}");
    }

    /// <summary>Chạy git và ghi output ra nhật ký.</summary>
    private Task<string> Git(string workingDirectory, params string[] args) =>
        RunGit(workingDirectory, true, args);

    /// <summary>Chạy git, không ghi stdout ra nhật ký (dùng cho lệnh chỉ đọc).</summary>
    private Task<string> Query(string workingDirectory, params string[] args) =>
        RunGit(workingDirectory, false, args);

    private async Task<string> RunGit(string workingDirectory, bool echo, string[] args)
    {
        var start = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        start.Environment["GIT_TERMINAL_PROMPT"] = "0"; // tránh treo khi chờ nhập mật khẩu trong console ẩn

        using var process = new Process { StartInfo = start };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new Exception("Không chạy được lệnh 'git'. Hãy cài Git for Windows và đảm bảo 'git' nằm trong PATH.");
        }

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        string stdout = await stdoutTask;
        string stderr = await stderrTask;

        if (echo && !string.IsNullOrWhiteSpace(stdout))
            Log(stdout.Trim());

        if (process.ExitCode != 0)
            throw new Exception(string.IsNullOrWhiteSpace(stderr)
                ? $"Git exit code: {process.ExitCode}"
                : stderr.Trim());

        if (echo && !string.IsNullOrWhiteSpace(stderr))
            Log(stderr.Trim());

        return stdout.Trim();
    }

    /// <summary>Ẩn user:password / token trong URL trước khi hiển thị.</summary>
    private static string Redact(string text) =>
        Regex.Replace(text, @"(?<=://)[^/@\s]+@", "***@");

    private void Log(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(Log), message);
            return;
        }

        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {Redact(message)}{Environment.NewLine}");
    }
}
