using System.Globalization;
using MyMemory.Server.Models;
using MyMemory.Server.Services;

namespace MyMemory.Server.Endpoints;

/// <summary>
/// I02「上传口述素材并转写」端点（承载模块 B01 素材接入 + B02 语音转写）。
/// 请求编码为 multipart/form-data：音频以二进制上传，其余为普通表单字段。
/// </summary>
public static class TranscriptionEndpoints
{
    public static IEndpointRouteBuilder MapTranscriptionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/transcriptions", HandleTranscribeAsync)
            .DisableAntiforgery();
        return app;
    }

    private static async Task<IResult> HandleTranscribeAsync(
        HttpRequest request,
        TranscriptionService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("TranscriptionEndpoints");

        if (!request.HasFormContentType)
        {
            logger.LogWarning("转写请求不是 multipart/form-data");
            return ApiErrors.From(ErrorCodes.InvalidRequest);
        }

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or BadHttpRequestException)
        {
            // 请求体损坏或超出服务器上限：归入「音频文件过大 / 无法解码」类，避免 500
            logger.LogWarning("转写请求表单读取失败：{ExceptionType}", exception.GetType().Name);
            return ApiErrors.From(ErrorCodes.AudioTooLarge);
        }

        var audio = form.Files["audio"];
        var segmentId = form["segmentId"].ToString();
        var storyId = form["storyId"].ToString();

        double? durationSeconds = null;
        var rawDuration = form["durationSeconds"].ToString();
        if (double.TryParse(rawDuration, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            durationSeconds = parsed;
        }

        await using var stream = audio?.OpenReadStream();

        var outcome = await service.TranscribeAsync(
            stream,
            audio?.Length ?? 0,
            audio?.FileName,
            audio?.ContentType,
            segmentId,
            storyId,
            durationSeconds,
            cancellationToken);

        if (!outcome.IsValid)
        {
            // 日志只记录错误码与片段标识，不记录音频内容与转写正文（技术方案 6.6）
            logger.LogInformation("转写被拒绝，错误码 {ErrorCode}，片段 {SegmentId}", outcome.ErrorCode, segmentId);
            return ApiErrors.From(outcome.ErrorCode!);
        }

        return Results.Ok(outcome.Response);
    }
}
