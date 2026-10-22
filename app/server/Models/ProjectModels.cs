namespace MyMemory.Server.Models;

/// <summary>
/// 新建传记项目的请求体（POST /api/projects，Content-Type: application/json）。
/// 字段命名与《标准化 API 接口文档》保持一致（camelCase、storyId 指代记录标识）。
/// </summary>
public sealed class CreateProjectRequest
{
    /// <summary>记录标题，必填，最长 40 字。对应本地存储字段 title。</summary>
    public string? Title { get; set; }

    /// <summary>
    /// 前端生成的本地记录标识（UUID），必填。
    /// 由前端生成并回传，是保证本接口幂等的关键：相同输入必得相同输出，后端无需任何状态。
    /// </summary>
    public string? ClientStoryId { get; set; }

    /// <summary>前端确定的创建时间（ISO 8601），必填。缺省会让接口失去幂等性，因此不做服务端兜底。</summary>
    public string? CreatedAt { get; set; }
}

/// <summary>新建传记项目的返回体。结构与前端本地写入 IndexedDB 的记录对象一致。</summary>
public sealed class CreateProjectResponse
{
    /// <summary>记录标识，原样回传入参 clientStoryId。</summary>
    public string StoryId { get; set; } = string.Empty;

    /// <summary>记录标题。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>记录状态，初始为「未录入」。</summary>
    public string Status { get; set; } = ProjectStatus.NotRecorded;

    /// <summary>片段数量，新建时为 0。</summary>
    public int SegmentCount { get; set; }

    /// <summary>创建时间（ISO 8601），原样回传入参 createdAt。</summary>
    public string CreatedAt { get; set; } = string.Empty;

    /// <summary>是否为本次新建。恒为 true，保留该字段以便后续与幂等查询区分。</summary>
    public bool Created { get; set; }
}

/// <summary>
/// 记录状态枚举取值，与 PRD 2.1「展示当前记录状态」及技术方案 8.4 记录（Story）的 status 字段一致。
/// </summary>
public static class ProjectStatus
{
    public const string NotRecorded = "未录入";
    public const string Transcribing = "转写中";
    public const string PendingEdit = "待编辑";
    public const string Completed = "已完成";
}

/// <summary>统一错误响应体。所有可预期的失败场景都返回该结构，不返回裸状态码。</summary>
public sealed class ApiErrorResponse
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool Retryable { get; set; }
}
