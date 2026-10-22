using System.Text.Json.Serialization;

namespace MyMemory.Server.Models;

/// <summary>
/// I03「生成传记初稿」请求体，字段与《标准化 API 接口文档》2.3 逐项对应。
/// styleHint 在接口文档中标注为「第一版不接受该参数」，因此这里显式忽略：
/// 若前端误传，反序列化时不报错但也不会影响生成行为（避免把未启用能力做成半成品）。
/// </summary>
public sealed class GenerationRequest
{
    /// <summary>记录标识，仅用于日志关联。</summary>
    public string? StoryId { get; set; }

    /// <summary>记录标题，作为生成时的上下文。</summary>
    public string? StoryTitle { get; set; }

    /// <summary>原始转写文本，本次生成唯一的内容来源。</summary>
    public string? SourceText { get; set; }

    /// <summary>取值为 polish（单段润色）或 merge（多段合并为完整初稿）。</summary>
    public string? Mode { get; set; }

    /// <summary>接口文档标注「第一版不接受」，此处仅为显式忽略声明。</summary>
    [JsonIgnore]
    public string? StyleHint { get; set; }
}

/// <summary>I03 返回参数，字段与接口文档 2.3 逐项对应。</summary>
public sealed class GenerationResponse
{
    /// <summary>传记初稿正文，可编辑。</summary>
    public string PolishedText { get; set; } = string.Empty;

    /// <summary>初稿字数，供前端展示与导出前校验。</summary>
    public int CharCount { get; set; }

    /// <summary>模型标识，仅用于排查。</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>生成时间（ISO 8601）。</summary>
    public string GeneratedAt { get; set; } = string.Empty;

    /// <summary>入参原始文本的哈希值，供前端核对初稿对应的是哪一版原文。</summary>
    public string SourceTextHash { get; set; } = string.Empty;
}

/// <summary>生成接口的校验与执行结果。</summary>
public sealed record GenerationOutcome(bool IsValid, string? ErrorCode, GenerationResponse? Response)
{
    public static GenerationOutcome Ok(GenerationResponse response) => new(true, null, response);

    public static GenerationOutcome Fail(string errorCode) => new(false, errorCode, null);
}
