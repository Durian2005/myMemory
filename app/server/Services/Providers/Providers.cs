using System.Text;
using System.Text.RegularExpressions;
using MyMemory.Server.Models;

namespace MyMemory.Server.Services.Providers;

/// <summary>语音转写结果。失败时 FailureCode 为业务错误码，成功时 Text 为原始转写文本。</summary>
public sealed record SpeechToTextResult(bool Success, string FailureCode, string Text, string Engine);

/// <summary>
/// 语音转写服务抽象（技术方案 8.3 决策二：语音识别统一经自有后端代理，前端不接触密钥）。
/// 换供应商时只替换实现，业务代码与接口契约不动。
/// </summary>
public interface ISpeechToTextProvider
{
    /// <summary>引擎标识，写入响应的 engine 字段，仅用于排查。</summary>
    string Name { get; }

    /// <summary>当前引擎是否可用；不可用时前端进入「本地模式」。</summary>
    bool IsAvailable { get; }

    Task<SpeechToTextResult> TranscribeAsync(
        Stream audio,
        string mimeType,
        double durationSeconds,
        CancellationToken cancellationToken);
}

/// <summary>AI 生成结果。失败时 FailureCode 为业务错误码，成功时 Text 为初稿正文。</summary>
public sealed record GenerationResult(bool Success, string FailureCode, string Text, string Model);

/// <summary>
/// AI 初稿生成服务抽象。提示词中的事实约束（不得虚构人物、时间、地点、事件）由具体实现负责遵守。
/// </summary>
public interface IGenerationProvider
{
    string Name { get; }

    bool IsAvailable { get; }

    Task<GenerationResult> GenerateAsync(
        string storyTitle,
        string sourceText,
        string mode,
        CancellationToken cancellationToken);
}

/// <summary>
/// 本地模拟转写引擎（开发用，不是业务功能）。
///
/// 为什么需要它：实验环境没有语音识别服务的密钥，但接口契约、参数校验、错误码与页面交互都必须真实验证。
/// 因此这里提供一个可注入的本地实现：链路完全真实（真实上传、真实校验、真实响应），
/// 只有「把音频变成文字」这一步用占位内容代替。
///
/// 接入真实服务时：把 appsettings 的 Providers:Transcription:Mode 从 stub 改为 cloud，
/// 并实现一个调用云端服务的 ISpeechToTextProvider 即可，业务层与前端一行都不用改。
///
/// FailMode 用于验证错误分支（初审时逐个触发）：
/// none 正常 / unavailable 转写服务不可用 / timeout 转写超时 / empty 转写结果为空。
/// </summary>
public sealed class StubSpeechToTextProvider : ISpeechToTextProvider
{
    private readonly string _failMode;
    private readonly int _delayMs;

    public StubSpeechToTextProvider(IConfiguration configuration)
    {
        _failMode = configuration["Providers:Transcription:Stub:FailMode"] ?? "none";
        _delayMs = configuration.GetValue("Providers:Transcription:Stub:DelayMs", 600);
    }

    public string Name => "local-stub";

    public bool IsAvailable => !string.Equals(_failMode, "unavailable", StringComparison.OrdinalIgnoreCase);

    public async Task<SpeechToTextResult> TranscribeAsync(
        Stream audio,
        string mimeType,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        if (_delayMs > 0)
        {
            await Task.Delay(_delayMs, cancellationToken);
        }

        if (string.Equals(_failMode, "timeout", StringComparison.OrdinalIgnoreCase))
        {
            return new SpeechToTextResult(false, ErrorCodes.TranscriptionTimeout, string.Empty, Name);
        }

        if (string.Equals(_failMode, "empty", StringComparison.OrdinalIgnoreCase))
        {
            return new SpeechToTextResult(false, ErrorCodes.TranscriptionEmpty, string.Empty, Name);
        }

        // 占位文本只说明「收到了多长的音频」，不编造任何人物、时间、地点与事件，
        // 避免把不存在的事实写进用户的传记。
        var seconds = (int)Math.Round(durationSeconds);
        var text = $"这是本地模拟引擎返回的占位转写内容，对应一段约 {seconds} 秒的口述。"
                   + "真实语音识别尚未接入，请在这里手动填写你刚才讲述的内容。";

        return new SpeechToTextResult(true, string.Empty, text, Name);
    }
}

/// <summary>
/// 本地模拟生成引擎（开发用，不是业务功能）。
///
/// 它只做两件确定的事：去掉口语填充词、按句读分段。**不新增任何事实**，
/// 因此不会违反「AI 不得虚构人物、时间、地点、事件」的约束。
///
/// 真实 AI 服务接入方式与转写一致：替换 IGenerationProvider 的实现即可。
/// FailMode：none 正常 / unavailable 服务不可用 / timeout 超时 / empty 返回空 / drift 疑似偏离事实。
/// </summary>
public sealed class StubGenerationProvider : IGenerationProvider
{
    private static readonly Regex FillerPattern = new(
        "(呃|嗯|那个|就是说|然后呢|啊，)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string _failMode;
    private readonly int _delayMs;

    public StubGenerationProvider(IConfiguration configuration)
    {
        _failMode = configuration["Providers:Generation:Stub:FailMode"] ?? "none";
        _delayMs = configuration.GetValue("Providers:Generation:Stub:DelayMs", 900);
    }

    public string Name => "local-stub";

    public bool IsAvailable => !string.Equals(_failMode, "unavailable", StringComparison.OrdinalIgnoreCase);

    public async Task<GenerationResult> GenerateAsync(
        string storyTitle,
        string sourceText,
        string mode,
        CancellationToken cancellationToken)
    {
        if (_delayMs > 0)
        {
            await Task.Delay(_delayMs, cancellationToken);
        }

        if (string.Equals(_failMode, "timeout", StringComparison.OrdinalIgnoreCase))
        {
            return new GenerationResult(false, ErrorCodes.GenerationTimeout, string.Empty, Name);
        }

        if (string.Equals(_failMode, "empty", StringComparison.OrdinalIgnoreCase))
        {
            return new GenerationResult(false, ErrorCodes.GenerationEmpty, string.Empty, Name);
        }

        if (string.Equals(_failMode, "drift", StringComparison.OrdinalIgnoreCase))
        {
            return new GenerationResult(false, ErrorCodes.GenerationDrift, string.Empty, Name);
        }

        var cleaned = NormalizePunctuation(FillerPattern.Replace(sourceText, string.Empty).Trim());
        if (cleaned.Length == 0)
        {
            return new GenerationResult(false, ErrorCodes.GenerationEmpty, string.Empty, Name);
        }

        // merge 模式：把多个片段的正文合成一篇；polish 模式：单段整理。
        // 两种模式都不改写事实，只在段与段之间补空行。
        var paragraphs = cleaned
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Trim());

        var builder = new StringBuilder();
        foreach (var paragraph in paragraphs)
        {
            builder.AppendLine(paragraph);
            builder.AppendLine();
        }

        var result = builder.ToString().TrimEnd();
        return new GenerationResult(true, string.Empty, result, Name);
    }

    /// <summary>
    /// 去掉填充词后可能留下「。，」「，、」这类标点残渣，这里做最小收敛：
    /// 1. 终止符之前的逗号顿号直接删掉；
    /// 2. 终止符之后紧跟的逗号顿号删掉；
    /// 3. 省略号后紧跟的逗号删掉；
    /// 4. 首尾多余的逗号顿号分号删掉。
    /// 只处理标点，不增删任何文字内容。
    /// </summary>
    private static string NormalizePunctuation(string text)
    {
        var normalized = Regex.Replace(text, "[，、；]{1,}(?=[。！？])", string.Empty);
        normalized = Regex.Replace(normalized, "([。！？])[，、；]+", "$1");
        normalized = Regex.Replace(normalized, "…[，、；]", "…");
        normalized = Regex.Replace(normalized, "^[，、；]+|[，、；]+$", string.Empty);
        return normalized.Trim();
    }
}
