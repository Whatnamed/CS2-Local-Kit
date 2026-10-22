using System.Security.Cryptography;
using System.Text;

namespace CS2LocalKit.Core.Runtime;

/// <summary>Serializes Controller-owned Bot and Human game-file writes for one CS2 root.</summary>
public static class BotHumanOperationGate
{
    public static IDisposable Acquire(string cs2Root)
    {
        var full = Path.GetFullPath(cs2Root).ToUpperInvariant();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..24];
        var path = Path.Combine(Path.GetTempPath(), "cs2localkit-bothuman-" + key + ".lock");
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                1, FileOptions.DeleteOnClose);
        }
        catch (IOException ex) { throw new IOException("另一项 Bot/Human 文件操作正在执行；请核对当前操作结果", ex); }
    }
}
