namespace MyMemory.Server.Models;

/// <summary>
/// I02「上传口述素材并转写」的返回参数，字段与《标准化 API 接口文档》2.2 逐项对应。
/// 注意：本接口只返回原始转写文本，不做任何润色与改写。
/// </summary>
public sealed class TranscriptionResponse
{
    /// <summary>原样返回入参中的片段标识，便于前端定位。</summary>
    public string SegmentId { get; set; } = string.Empty;

    /// <summary>原始转写文本，保留口语语气；可能为空字符串。</summary>
    public string TranscribedText { get; set; } = string.Empty;

    /// <summary>服务端识别出的音频时长（秒），供前端核对。</summary>
    public double DurationSeconds { get; set; }

    /// <summary>识别引擎标识，仅用于排查，不含服务地址与密钥。</summary>
    public string Engine { get; set; } = string.Empty;

    /// <summary>完成时间（ISO 8601）。</summary>
    public string FinishedAt { get; set; } = string.Empty;
}

/// <summary>转写接口的校验与执行结果。</summary>
public sealed record TranscriptionOutcome(bool IsValid, string? ErrorCode, TranscriptionResponse? Response)
{
    public static TranscriptionOutcome Ok(TranscriptionResponse response) => new(true, null, response);

    public static TranscriptionOutcome Fail(string errorCode) => new(false, errorCode, null);
}
