using System.Text.Json;
using MyMemory.Server.Models;
using MyMemory.Server.Services;

namespace MyMemory.Server.Endpoints;

/// <summary>
/// I03「生成传记初稿」端点（承载模块 B03 AI 生成）。
/// 请求编码为 application/json；原文是本次生成的唯一内容来源。
/// </summary>
public static class GenerationEndpoints
{
    public static IEndpointRouteBuilder MapGenerationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/polish", HandleGenerateAsync);
        return app;
    }

    private static async Task<IResult> HandleGenerateAsync(
        HttpRequest request,
        GenerationService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("GenerationEndpoints");

        GenerationRequest? payload;
        try
        {
            payload = await request.ReadFromJsonAsync<GenerationRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            logger.LogWarning("生成初稿请求体解析失败");
            return ApiErrors.From(ErrorCodes.InvalidRequest);
        }

        var outcome = await service.GenerateAsync(payload, cancellationToken);

        if (!outcome.IsValid)
        {
            // 只记录错误码与记录标识，不记录正文（技术方案 6.6）
            logger.LogInformation("生成初稿被拒绝，错误码 {ErrorCode}，记录 {StoryId}",
                outcome.ErrorCode, payload?.StoryId);
            return ApiErrors.From(outcome.ErrorCode!);
        }

        return Results.Ok(outcome.Response);
    }
}
