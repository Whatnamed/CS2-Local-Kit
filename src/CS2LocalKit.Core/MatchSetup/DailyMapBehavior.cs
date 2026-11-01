using System.Text.Json;

namespace CS2LocalKit.Core.MatchSetup;

/// <summary>One map's independent capability writes and exact mode/epoch readbacks.</summary>
public sealed class DailyMapBehavior(AimSetting aim, NadeSetting nades)
{
    public sealed class Setting(string desired)
    {
        private long? _epoch;
        public bool Finished { get; private set; }
        public string State { get; private set; } = "等待模块就绪";
        public string? Readback { get; private set; }
        public void Apply(Tuple<Action<string>, Func<string>>? capability, bool lastAttempt)
        {
            if (Finished) return;
            if (capability is null) { if (lastAttempt) { State = "模块缺失，未应用"; Finished = true; } return; }
            try
            {
                capability.Item1(desired);
                using var result = JsonDocument.Parse(capability.Item2()); var value = result.RootElement;
                var epoch = value.GetProperty("Epoch").GetInt64();
                if (epoch <= 0 || value.GetProperty("Mode").GetString() != desired) throw new InvalidDataException("mode/epoch 读回不符");
                _epoch = epoch;
                Readback = value.GetRawText(); State = "运行时已接收，mode/epoch 读回通过；实际行为待验收";
                if (desired != "off" && value.TryGetProperty("MapCount", out var count) && count.GetInt32() == 0) State = "模式已读回；本地图无投掷线路，行为证据不足";
                Finished = true;
            }
            catch (Exception ex)
            {
                _epoch = null; Readback = null;
                State = (lastAttempt ? "未应用（等待窗口已用尽）：" : "等待重试：") + ex.Message;
                Finished = lastAttempt;
            }
        }
        public void Observe(Tuple<Action<string>, Func<string>>? capability)
        {
            if (_epoch is null) return;
            try
            {
                if (capability is null) throw new InvalidDataException("模块已卸载");
                using var result = JsonDocument.Parse(capability.Item2()); var value = result.RootElement;
                if (value.GetProperty("Epoch").GetInt64() != _epoch || value.GetProperty("Mode").GetString() != desired) throw new InvalidDataException("本图 mode/epoch 已变化；未重新写入");
                Readback = value.GetRawText();
            }
            catch (Exception ex) { State = "无法确认：" + ex.Message; Readback = null; _epoch = null; }
        }
    }
    public Setting Aim { get; } = new(aim.ToString().ToLowerInvariant());
    public Setting Nades { get; } = new(nades.ToString().ToLowerInvariant());
    public bool Finished => Aim.Finished && Nades.Finished;
}
