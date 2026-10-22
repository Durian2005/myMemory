using System.Text.Json;
using MyMemory.Server.Models;
using MyMemory.Server.Services;

namespace MyMemory.Server.Endpoints;

/// <summary>
/// I04「导出 DOCX 文档」端点（承载模块 B04 文档导出）。
/// 成功时直接返回文件字节流（不包 JSON），失败时返回统一 JSON 错误体。
/// </summary>
public static class ExportEndpoints
{
    private const string DocxContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    public static IEndpointRouteBuilder MapExportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/exports", HandleExportAsync);
        return app;
    }

    private static async Task<IResult> HandleExportAsync(
        HttpRequest request,
        DocxExportService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("ExportEndpoints");

        ExportRequest? payload;
        try
        {
            payload = await request.ReadFromJsonAsync<ExportRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            logger.LogWarning("导出请求体解析失败");
            return ApiErrors.From(ErrorCodes.InvalidRequest);
        }

        var outcome = service.Export(payload);
        if (!outcome.IsValid)
        {
            logger.LogInformation("导出被拒绝，错误码 {ErrorCode}", outcome.ErrorCode);
            return ApiErrors.From(outcome.ErrorCode!);
        }

        return Results.File(outcome.Content!, DocxContentType, outcome.FileName);
    }
}
