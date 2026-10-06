using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CS2LocalKit.Core.Runtime;

public sealed class MetaModStartupRepairOptions
{
    public string? Cs2Root { get; init; }
    public string? LockPath { get; init; }
    public string? BackupsRoot { get; init; }
    public Func<bool>? Cs2RunningProbe { get; init; }
}

public sealed class MetaModStartupRepairResult
{
    public required bool Success { get; init; }
    public required bool Modified { get; init; }
    public required string Message { get; init; }
    public string? BackupPath { get; init; }
    public string? OriginalSha256 { get; init; }
    public string? RepairedSha256 { get; init; }
}

public sealed class MetaModStartupRepairException : Exception
{
    public MetaModStartupRepairException(string message) : base(message) { }
    public MetaModStartupRepairException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Dedicated, narrow-scope repair service for the MetaMod SearchPath entry in gameinfo.gi.
/// Requires CS2 closed, valid accepted framework/plugin identity, verifiable pre-write backup,
/// idempotent execution, and post-write verification. Fails closed on malformed files.
/// Never touches MetaMod/CSS/InventorySimulator binaries, presets, or user fixtures.
/// </summary>
public sealed class MetaModStartupRepairService
{
    private readonly MetaModStartupRepairOptions _options;

    public MetaModStartupRepairService(MetaModStartupRepairOptions? options = null)
    {
        _options = options ?? new MetaModStartupRepairOptions();
    }

    public MetaModStartupRepairResult Repair()
    {
        // 1. CS2 root detection
        string? cs2Root = _options.Cs2Root;
        if (cs2Root is null)
        {
            try { cs2Root = Cs2Locator.FindCs2Root(); }
            catch { cs2Root = null; }
        }
        if (cs2Root is null || !Directory.Exists(cs2Root))
            throw new MetaModStartupRepairException("未检测到 CS2 安装路径。");

        // 2. CS2 running probe (must be closed)
        var isRunning = (_options.Cs2RunningProbe ?? Cs2Locator.IsCs2Running)();
        if (isRunning)
            throw new MetaModStartupRepairException("CS2 正在运行，必须先关闭 CS2 才能修复启动项。");

        // 3. Lock loading & accepted framework/plugin verification
        var lockPath = _options.LockPath ?? FindDefaultLockPath();
        if (lockPath is null || !File.Exists(lockPath))
            throw new MetaModStartupRepairException("未找到 runtime/inventory-simulator.lock.json，无法校验 accepted 运行环境。");

        var pin = InventorySimulatorLock.Load(lockPath);
        var csgoDir = Path.Combine(cs2Root, "game", "csgo");
        if (!Directory.Exists(csgoDir))
            throw new MetaModStartupRepairException($"CS2 game/csgo 目录不存在: {csgoDir}");

        VerifyAcceptedFrameworkPrerequisites(csgoDir, pin);

        // 4. gameinfo.gi preflight & SearchPaths inspection
        var gameinfoPath = Path.Combine(csgoDir, "gameinfo.gi");
        if (!File.Exists(gameinfoPath))
            throw new MetaModStartupRepairException($"gameinfo.gi 文件不存在: {gameinfoPath}");

        var originalBytes = File.ReadAllBytes(gameinfoPath);
        var hasBom = originalBytes.Length >= 3 && originalBytes[0] == 0xEF && originalBytes[1] == 0xBB && originalBytes[2] == 0xBF;
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: hasBom, throwOnInvalidBytes: true);
        string text = encoding.GetString(originalBytes);

        var searchPathsMatches = Regex.Matches(text, @"\bSearchPaths\b");
        if (searchPathsMatches.Count == 0)
            throw new MetaModStartupRepairException("gameinfo.gi 结构异常：未找到 SearchPaths 块。");
        if (searchPathsMatches.Count > 1)
            throw new MetaModStartupRepairException("gameinfo.gi 结构异常：找到多个 SearchPaths 块，目标不唯一。");

        int searchPathsIdx = searchPathsMatches[0].Index;
        int openIndex = text.IndexOf('{', searchPathsIdx);
        if (openIndex < 0)
            throw new MetaModStartupRepairException("gameinfo.gi 结构异常：SearchPaths 缺少开括号 '{'。");

        var between = text.Substring(searchPathsIdx + "SearchPaths".Length, openIndex - (searchPathsIdx + "SearchPaths".Length)).Trim();
        if (!string.IsNullOrEmpty(between) && !between.StartsWith("//") && !between.StartsWith("/*"))
            throw new MetaModStartupRepairException("gameinfo.gi 结构异常：SearchPaths 与 '{' 之间存在异常标记。");

        int depth = 1;
        int closeIndex = -1;
        for (int i = openIndex + 1; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    closeIndex = i;
                    break;
                }
            }
        }
        if (closeIndex < 0)
            throw new MetaModStartupRepairException("gameinfo.gi 结构异常：SearchPaths 块未闭合。");

        string body = text.Substring(openIndex + 1, closeIndex - openIndex - 1);
        var existingMatches = Regex.Matches(body, @"^[ \t]*Game[ \t]+csgo/addons/metamod[ \t]*\r?$", RegexOptions.Multiline);
        if (existingMatches.Count > 1)
            throw new MetaModStartupRepairException("gameinfo.gi 结构异常：SearchPaths 中存在多个 MetaMod 启动项。");

        var originalSha256 = HashBytes(originalBytes);

        // 5. Idempotent check: already configured
        if (existingMatches.Count == 1)
        {
            return new MetaModStartupRepairResult
            {
                Success = true,
                Modified = false,
                Message = "MetaMod 启动项已正确配置且唯一，无需修改 (幂等)。",
                OriginalSha256 = originalSha256,
                RepairedSha256 = originalSha256,
            };
        }

        // 6. Plan insertion
        string newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var indentMatch = Regex.Match(body, @"(?m)^([ \t]+)Game(_\w+)?[ \t]+");
        string indent = indentMatch.Success ? indentMatch.Groups[1].Value : "\t\t\t";
        string insertLine = $"{indent}Game\tcsgo/addons/metamod{newline}";

        int insertIndex = openIndex + 1;
        if (insertIndex < text.Length && text[insertIndex] == '\r') insertIndex++;
        if (insertIndex < text.Length && text[insertIndex] == '\n') insertIndex++;

        string repairedText = text.Insert(insertIndex, insertLine);

        // Static verification of modification scope:
        if (repairedText.Length != text.Length + insertLine.Length
            || !repairedText.StartsWith(text.Substring(0, insertIndex))
            || !repairedText.EndsWith(text.Substring(insertIndex)))
        {
            throw new MetaModStartupRepairException("无法安全证明 gameinfo.gi 修改范围，中止操作。");
        }

        // 7. Verifiable pre-write backup
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmssfff");
        var backupsRoot = _options.BackupsRoot ?? Path.Combine(CorePaths.Cs2ModRoot, "backups", "metamod-startup-repair");
        var backupDir = Path.Combine(backupsRoot, $"{stamp}-{Guid.NewGuid().ToString("N")[..8]}");
        Directory.CreateDirectory(backupDir);
        var backupFile = Path.Combine(backupDir, "gameinfo.gi");
        File.WriteAllBytes(backupFile, originalBytes);

        var backupSha256 = HashBytes(File.ReadAllBytes(backupFile));
        if (!string.Equals(backupSha256, originalSha256, StringComparison.OrdinalIgnoreCase))
            throw new MetaModStartupRepairException("备份验证失败：备份文件哈希与原始文件不一致，中止操作。");

        var recordPath = Path.Combine(backupDir, "repair-record.json");
        var record = new
        {
            schemaVersion = 1,
            action = "repair-metamod-startup",
            createdAtUtc = DateTime.UtcNow.ToString("o"),
            targetPath = Path.GetFullPath(gameinfoPath),
            backupPath = Path.GetFullPath(backupFile),
            originalSha256 = originalSha256,
            insertedEntry = "Game\tcsgo/addons/metamod",
        };
        File.WriteAllText(recordPath, JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }));

        // 8. Atomic write to target
        var tempTarget = gameinfoPath + $".new-{Guid.NewGuid().ToString("N")[..8]}";
        byte[] repairedBytes = encoding.GetBytes(repairedText);
        var expectedRepairedSha = HashBytes(repairedBytes);

        try
        {
            File.WriteAllBytes(tempTarget, repairedBytes);
            var stagedSha = HashBytes(File.ReadAllBytes(tempTarget));
            if (!string.Equals(stagedSha, expectedRepairedSha, StringComparison.OrdinalIgnoreCase))
                throw new MetaModStartupRepairException("暂存写入哈希校验失败，中止替换。");

            File.Move(tempTarget, gameinfoPath, overwrite: true);

            // 9. Post-write verification
            byte[] writtenBytes = File.ReadAllBytes(gameinfoPath);
            var writtenSha = HashBytes(writtenBytes);
            if (!string.Equals(writtenSha, expectedRepairedSha, StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllBytes(gameinfoPath, originalBytes);
                throw new MetaModStartupRepairException("写入后验证失败：文件哈希不匹配，已自动回滚。");
            }

            string writtenText = encoding.GetString(writtenBytes);
            if (writtenText != repairedText)
            {
                File.WriteAllBytes(gameinfoPath, originalBytes);
                throw new MetaModStartupRepairException("写入后验证失败：文件内容与预期不一致，已自动回滚。");
            }

            var verifySearchPaths = Regex.Match(writtenText, @"\bSearchPaths\s*\{([^}]*)\}", RegexOptions.Singleline).Groups[1].Value;
            var hasMetamod = Regex.IsMatch(verifySearchPaths, @"^[ \t]*Game[ \t]+csgo/addons/metamod[ \t]*\r?$", RegexOptions.Multiline);
            if (!hasMetamod)
            {
                File.WriteAllBytes(gameinfoPath, originalBytes);
                throw new MetaModStartupRepairException("写入后验证失败：MetaMod 启动项未正确生效，已自动回滚。");
            }

            return new MetaModStartupRepairResult
            {
                Success = true,
                Modified = true,
                Message = $"已成功修复 MetaMod 启动项 (已备份至 {backupFile})",
                BackupPath = backupFile,
                OriginalSha256 = originalSha256,
                RepairedSha256 = writtenSha,
            };
        }
        catch (Exception ex) when (ex is not MetaModStartupRepairException)
        {
            if (File.Exists(backupFile))
            {
                try { File.WriteAllBytes(gameinfoPath, originalBytes); } catch { }
            }
            throw new MetaModStartupRepairException($"修复 gameinfo.gi 时发生异常: {ex.Message}", ex);
        }
        finally
        {
            if (File.Exists(tempTarget))
            {
                try { File.Delete(tempTarget); } catch { }
            }
        }
    }

    private static void VerifyAcceptedFrameworkPrerequisites(string csgoDir, InventorySimulatorLock pin)
    {
        var root = Path.GetFullPath(csgoDir) + Path.DirectorySeparatorChar;

        if (pin.MetaModFiles.Count == 0)
            throw new MetaModStartupRepairException("Lock 文件未配置 accepted MetaMod 校验集合。");
        foreach (var (relPath, expHash) in pin.MetaModFiles)
        {
            var path = Path.GetFullPath(Path.Combine(csgoDir, relPath));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new MetaModStartupRepairException($"组件路径越界: {relPath}");
            if (!File.Exists(path))
                throw new MetaModStartupRepairException($"MetaMod 原生组件缺失: {relPath}");
            using var stream = File.OpenRead(path);
            var sha = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!string.Equals(sha, expHash, StringComparison.OrdinalIgnoreCase))
                throw new MetaModStartupRepairException($"MetaMod 文件哈希不匹配: {relPath}");
        }

        if (pin.CounterStrikeSharpFiles.Count == 0)
            throw new MetaModStartupRepairException("Lock 文件未配置 accepted CounterStrikeSharp 校验集合。");
        foreach (var (relPath, expHash) in pin.CounterStrikeSharpFiles)
        {
            var path = Path.GetFullPath(Path.Combine(csgoDir, relPath));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new MetaModStartupRepairException($"组件路径越界: {relPath}");
            if (!File.Exists(path))
                throw new MetaModStartupRepairException($"CounterStrikeSharp 原生组件缺失: {relPath}");
            using var stream = File.OpenRead(path);
            var sha = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!string.Equals(sha, expHash, StringComparison.OrdinalIgnoreCase))
                throw new MetaModStartupRepairException($"CounterStrikeSharp 文件哈希不匹配: {relPath}");
        }

        var isPluginPath = Path.GetFullPath(Path.Combine(csgoDir, "addons", "counterstrikesharp", "plugins", "InventorySimulator", "InventorySimulator.dll"));
        if (!isPluginPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new MetaModStartupRepairException("插件路径越界。");
        if (!File.Exists(isPluginPath))
            throw new MetaModStartupRepairException("InventorySimulator 插件文件缺失。");

        if (string.IsNullOrEmpty(pin.PatchedDllSha256))
            throw new MetaModStartupRepairException("Lock 文件未配置 accepted patched DLL 哈希。");

        using var isStream = File.OpenRead(isPluginPath);
        var isSha = Convert.ToHexString(SHA256.HashData(isStream)).ToLowerInvariant();
        if (!string.Equals(isSha, pin.PatchedDllSha256, StringComparison.OrdinalIgnoreCase))
            throw new MetaModStartupRepairException("InventorySimulator patched DLL 校验不匹配。");
    }

    private static string HashBytes(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string? FindDefaultLockPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var d = dir; d is not null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "runtime", "inventory-simulator.lock.json");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
