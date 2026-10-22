namespace MyMemory.Server.Models;

/// <summary>
/// I04「导出 DOCX 文档」请求体，字段与《标准化 API 接口文档》2.4 的入参表对应。
/// 成功时直接返回 DOCX 文件字节流；失败时返回统一 JSON 错误体。
/// </summary>
public sealed class ExportRequest
{
    /// <summary>导出文档标题，即记录标题。</summary>
    public string? Title { get; set; }

    /// <summary>导出正文，即用户确认过的传记初稿。</summary>
    public string? Body { get; set; }

    /// <summary>录音片段数，仅写入文档的附加信息区，便于用户核对来源。</summary>
    public int? SegmentCount { get; set; }

    /// <summary>导出格式，当前仅支持 docx；缺省按 docx 处理。</summary>
    public string? Format { get; set; }
}

/// <summary>导出服务的校验结果。通过时携带已生成的文件字节。</summary>
public sealed record ExportOutcome(bool IsValid, string? ErrorCode, byte[]? Content, string? FileName)
{
    public static ExportOutcome Ok(byte[] content, string fileName) => new(true, null, content, fileName);

    public static ExportOutcome Fail(string errorCode) => new(false, errorCode, null, null);
}
