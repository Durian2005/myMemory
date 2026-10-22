using MyMemory.Server.Models;

namespace MyMemory.Server.Services;

/// <summary>校验结果：通过时返回规范化后的记录；不通过时返回业务错误码。</summary>
public sealed record CreateProjectResult(bool IsValid, string? ErrorCode, CreateProjectResponse? Project)
{
    public static CreateProjectResult Ok(CreateProjectResponse project) => new(true, null, project);

    public static CreateProjectResult Fail(string errorCode) => new(false, errorCode, null);
}

/// <summary>
/// 新建传记项目的准入校验与结构规范化。
/// 本模块不持久化任何用户数据：只负责把前端提交的标题规范化，并组装出与本地数据结构一致的记录对象。
/// 真正的写入仍由前端完成（写入浏览器 IndexedDB），符合技术方案 8.3 决策一「数据本地优先」。
/// 纯计算逻辑，无外部依赖，可直接单元测试（技术方案 6.6）。
/// </summary>
public sealed class ProjectService
{
    /// <summary>记录标题长度上限，与 UI 设计规范 3.3 的字数计数示例「2 / 40」保持一致。</summary>
    public const int MaxTitleLength = 40;

    public CreateProjectResult Create(CreateProjectRequest? request)
    {
        // 1. 必填字段校验：任一必填项缺失即无法组装出幂等结果
        if (request is null
            || string.IsNullOrWhiteSpace(request.ClientStoryId)
            || string.IsNullOrWhiteSpace(request.CreatedAt))
        {
            return CreateProjectResult.Fail(ErrorCodes.InvalidRequest);
        }

        // 2. 标题判空：只有空格等空白字符同样视为未填写
        var title = NormalizeTitle(request.Title);
        if (title.Length == 0)
        {
            return CreateProjectResult.Fail(ErrorCodes.TitleEmpty);
        }

        // 3. 标题长度上限
        if (title.Length > MaxTitleLength)
        {
            return CreateProjectResult.Fail(ErrorCodes.TitleTooLong);
        }

        var project = new CreateProjectResponse
        {
            StoryId = request.ClientStoryId.Trim(),
            Title = title,
            Status = ProjectStatus.NotRecorded,
            SegmentCount = 0,
            CreatedAt = request.CreatedAt,
            Created = true,
        };

        return CreateProjectResult.Ok(project);
    }

    /// <summary>
    /// 标题规范化：去掉首尾空白，并剔除控制字符。
    /// 技术方案 6.2 要求对标题等外部输入做内容处理，避免把不可见字符带入后续导出环节。
    /// </summary>
    private static string NormalizeTitle(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        var trimmed = raw.Trim();
        var buffer = new System.Text.StringBuilder(trimmed.Length);
        foreach (var ch in trimmed)
        {
            if (!char.IsControl(ch))
            {
                buffer.Append(ch);
            }
        }

        return buffer.ToString();
    }
}
