using System.Text.Json;
using MyMemory.Server.Models;
using MyMemory.Server.Services;

namespace MyMemory.Server.Endpoints;

/// <summary>
/// 传记项目相关端点。
/// 对应业务动作「新建传记项目」。相对实验3《标准化 API 接口文档》的 I01–I04 属新增接口，
/// 原因见项目根 README 与《实验4 交付说明》：本次实验要求该动作具备后端接口。
/// 边界仍然遵守技术方案 8.3 决策一：后端只做准入校验与结构规范化，不持久化用户数据。
/// </summary>
public static class ProjectEndpoints
{
    public static IEndpointRouteBuilder MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/projects", HandleCreateProjectAsync);
        return app;
    }

    private static async Task<IResult> HandleCreateProjectAsync(
        HttpRequest request,
        ProjectService projectService,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("ProjectEndpoints");

        CreateProjectRequest? payload;
        try
        {
            payload = await request.ReadFromJsonAsync<CreateProjectRequest>();
        }
        catch (JsonException)
        {
            // 请求体不是合法 JSON：归入 400 输入类，避免抛出未捕获异常
            logger.LogWarning("新建项目请求体解析失败");
            return ApiErrors.From(ErrorCodes.InvalidRequest);
        }

        var result = projectService.Create(payload);
        if (!result.IsValid)
        {
            // 日志只记录错误码，不记录标题原文（技术方案 6.6：日志不得记录正文原文）
            logger.LogInformation("新建项目被拒绝，错误码 {ErrorCode}", result.ErrorCode);
            return ApiErrors.From(result.ErrorCode!);
        }

        return Results.Ok(result.Project);
    }
}
