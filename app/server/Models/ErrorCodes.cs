namespace MyMemory.Server.Models;

/// <summary>
/// 业务错误码常量。
/// 编码规则见《标准化 API 接口文档》3.1：统一前缀 MYM_ + 4 位数字，第 1 位表示类别
/// （1xxx 输入与素材校验类 / 2xxx 语音转写类 / 3xxx AI 生成类 / 4xxx 文档导出类 / 5xxx 服务与通用类）。
/// </summary>
public static class ErrorCodes
{
    // ---- 1xxx 输入与素材校验类 ----

    /// <summary>400 未收到音频数据（I02）。</summary>
    public const string AudioMissing = "MYM_1001";

    /// <summary>415 音频格式不支持（I02）。</summary>
    public const string AudioFormatUnsupported = "MYM_1002";

    /// <summary>413 音频文件过大（I02）。</summary>
    public const string AudioTooLarge = "MYM_1003";

    /// <summary>422 音频为空或无法解码（I02）。</summary>
    public const string AudioEmpty = "MYM_1004";

    /// <summary>422 音频时长超出上限（I02）。</summary>
    public const string AudioTooLong = "MYM_1005";

    /// <summary>422 提交的文本为空（I03）。</summary>
    public const string TextEmpty = "MYM_1006";

    /// <summary>413 文本超出长度上限（I03）。</summary>
    public const string TextTooLong = "MYM_1007";

    /// <summary>422 导出标题为空（I04）。</summary>
    public const string ExportTitleEmpty = "MYM_1008";

    /// <summary>422 导出正文为空（I04）。</summary>
    public const string ExportBodyEmpty = "MYM_1009";

    /// <summary>422 记录标题为空（POST /api/projects，实验4 新增）。</summary>
    public const string TitleEmpty = "MYM_1010";

    /// <summary>413 记录标题超出长度上限（POST /api/projects，实验4 新增）。</summary>
    public const string TitleTooLong = "MYM_1011";

    /// <summary>400 请求缺少必填字段或格式不正确（通用输入类）。</summary>
    public const string InvalidRequest = "MYM_1012";

    // ---- 2xxx 语音转写类 ----

    /// <summary>503 转写服务不可用（I02）。</summary>
    public const string TranscriptionUnavailable = "MYM_2001";

    /// <summary>504 转写超时（I02）。</summary>
    public const string TranscriptionTimeout = "MYM_2002";

    /// <summary>422 转写结果为空（I02）。</summary>
    public const string TranscriptionEmpty = "MYM_2003";

    // ---- 3xxx AI 生成类 ----

    /// <summary>503 AI 服务不可用（I03）。</summary>
    public const string GenerationUnavailable = "MYM_3001";

    /// <summary>504 AI 生成超时（I03）。</summary>
    public const string GenerationTimeout = "MYM_3002";

    /// <summary>422 AI 返回内容为空（I03）。</summary>
    public const string GenerationEmpty = "MYM_3003";

    /// <summary>409 生成内容疑似偏离原始事实（I03）。</summary>
    public const string GenerationDrift = "MYM_3004";

    // ---- 4xxx 文档导出类 ----

    /// <summary>500 文档生成失败（I04）。</summary>
    public const string ExportFailed = "MYM_4001";

    /// <summary>413 导出内容超出上限（I04）。</summary>
    public const string ExportTooLarge = "MYM_4002";

    // ---- 5xxx 服务与通用类 ----

    /// <summary>500 服务内部异常。</summary>
    public const string InternalError = "MYM_5001";

    /// <summary>503 服务未就绪。</summary>
    public const string ServiceNotReady = "MYM_5002";

    /// <summary>429 请求过于频繁。</summary>
    public const string TooManyRequests = "MYM_5003";
}

/// <summary>
/// 单个业务错误码的完整定义：错误码、对应 HTTP 状态码、面向用户的中文文案、是否建议重试。
/// 与《标准化 API 接口文档》3.2 错误码总表的结构保持一致。
/// 文案统一遵循「原因 + 下一步动作」句式（技术方案 9.8）。
/// </summary>
public sealed record ErrorDefinition(string Code, int HttpStatus, string Message, bool Retryable);

/// <summary>错误码查表。新增错误码时必须在此登记，避免出现「有码无文案」的情况。</summary>
public static class ErrorCatalog
{
    private static readonly Dictionary<string, ErrorDefinition> Definitions = new(StringComparer.Ordinal)
    {
        // ---------- 1xxx 输入与素材校验 ----------
        [ErrorCodes.AudioMissing] = new(
            ErrorCodes.AudioMissing, StatusCodes.Status400BadRequest,
            "没有收到录音内容，请重新录制后再试", Retryable: false),

        [ErrorCodes.AudioFormatUnsupported] = new(
            ErrorCodes.AudioFormatUnsupported, StatusCodes.Status415UnsupportedMediaType,
            "当前录音格式不受支持，请改用网页录音或上传 mp3 / wav 文件", Retryable: false),

        [ErrorCodes.AudioTooLarge] = new(
            ErrorCodes.AudioTooLarge, StatusCodes.Status413PayloadTooLarge,
            "这段录音太大了，请分成几段较短的内容分别录制", Retryable: false),

        [ErrorCodes.AudioEmpty] = new(
            ErrorCodes.AudioEmpty, StatusCodes.Status422UnprocessableEntity,
            "录音内容为空或已损坏，请重新录制这一段", Retryable: false),

        [ErrorCodes.AudioTooLong] = new(
            ErrorCodes.AudioTooLong, StatusCodes.Status422UnprocessableEntity,
            "单段录音请控制在 30 分钟以内", Retryable: false),

        [ErrorCodes.TextEmpty] = new(
            ErrorCodes.TextEmpty, StatusCodes.Status422UnprocessableEntity,
            "没有可生成的内容，请先完成转写或手动填写正文", Retryable: false),

        [ErrorCodes.TextTooLong] = new(
            ErrorCodes.TextTooLong, StatusCodes.Status413PayloadTooLarge,
            "内容过长，请分段生成后再合并", Retryable: false),

        [ErrorCodes.ExportTitleEmpty] = new(
            ErrorCodes.ExportTitleEmpty, StatusCodes.Status422UnprocessableEntity,
            "请先填写记录标题，再导出文档", Retryable: false),

        [ErrorCodes.ExportBodyEmpty] = new(
            ErrorCodes.ExportBodyEmpty, StatusCodes.Status422UnprocessableEntity,
            "正文为空无法导出，请先完成初稿或手动填写内容", Retryable: false),

        [ErrorCodes.TitleEmpty] = new(
            ErrorCodes.TitleEmpty, StatusCodes.Status422UnprocessableEntity,
            "请先填写记录标题，再开始记录", Retryable: false),

        [ErrorCodes.TitleTooLong] = new(
            ErrorCodes.TitleTooLong, StatusCodes.Status413PayloadTooLarge,
            "记录标题过长，请控制在 40 字以内", Retryable: false),

        [ErrorCodes.InvalidRequest] = new(
            ErrorCodes.InvalidRequest, StatusCodes.Status400BadRequest,
            "请求内容不完整，请返回后重新提交", Retryable: false),

        // ---------- 2xxx 语音转写 ----------
        [ErrorCodes.TranscriptionUnavailable] = new(
            ErrorCodes.TranscriptionUnavailable, StatusCodes.Status503ServiceUnavailable,
            "语音转写服务暂时不可用，音频已保存在本地，可稍后重试", Retryable: true),

        [ErrorCodes.TranscriptionTimeout] = new(
            ErrorCodes.TranscriptionTimeout, StatusCodes.Status504GatewayTimeout,
            "转写用时过长已中断，音频已保留，可重试或改用手动输入", Retryable: true),

        [ErrorCodes.TranscriptionEmpty] = new(
            ErrorCodes.TranscriptionEmpty, StatusCodes.Status422UnprocessableEntity,
            "没有识别出有效内容，请检查录音音量或周围环境后重试", Retryable: true),

        // ---------- 3xxx AI 生成 ----------
        [ErrorCodes.GenerationUnavailable] = new(
            ErrorCodes.GenerationUnavailable, StatusCodes.Status503ServiceUnavailable,
            "初稿生成服务暂时不可用，原始转写文本已完整保留，可稍后重试", Retryable: true),

        [ErrorCodes.GenerationTimeout] = new(
            ErrorCodes.GenerationTimeout, StatusCodes.Status504GatewayTimeout,
            "生成用时过长已中断，原始转写文本未受影响，可重试", Retryable: true),

        [ErrorCodes.GenerationEmpty] = new(
            ErrorCodes.GenerationEmpty, StatusCodes.Status422UnprocessableEntity,
            "未能生成初稿，原始转写文本已保留，可稍后重试", Retryable: true),

        [ErrorCodes.GenerationDrift] = new(
            ErrorCodes.GenerationDrift, StatusCodes.Status409Conflict,
            "初稿可能与原始讲述不一致，请对照原文核对人物、时间、地点与事件", Retryable: false),

        // ---------- 4xxx 文档导出 ----------
        [ErrorCodes.ExportFailed] = new(
            ErrorCodes.ExportFailed, StatusCodes.Status500InternalServerError,
            "文档生成失败，你的内容未受影响，请稍后重试", Retryable: true),

        [ErrorCodes.ExportTooLarge] = new(
            ErrorCodes.ExportTooLarge, StatusCodes.Status413PayloadTooLarge,
            "内容过长无法一次导出，请精简后再试", Retryable: false),

        // ---------- 5xxx 通用 ----------
        [ErrorCodes.InternalError] = new(
            ErrorCodes.InternalError, StatusCodes.Status500InternalServerError,
            "服务出现异常，请稍后重试；若持续出现请重启后端服务", Retryable: true),

        [ErrorCodes.ServiceNotReady] = new(
            ErrorCodes.ServiceNotReady, StatusCodes.Status503ServiceUnavailable,
            "后端服务尚未就绪，请确认后端已启动", Retryable: true),

        [ErrorCodes.TooManyRequests] = new(
            ErrorCodes.TooManyRequests, StatusCodes.Status429TooManyRequests,
            "操作过于频繁，请稍候几秒再试", Retryable: true),
    };

    /// <summary>按错误码取定义；未登记的码回退为 MYM_5001，保证任何异常路径都有可读文案。</summary>
    public static ErrorDefinition Get(string code) =>
        Definitions.TryGetValue(code, out var definition) ? definition : Definitions[ErrorCodes.InternalError];
}
