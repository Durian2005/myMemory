using MyMemory.Server.Services;

namespace MyMemory.Server.Endpoints;

/// <summary>
/// 服务健康检查端点（I01，承载模块 B06 服务状态模块）。
/// 前端启动时调用一次、之后每 60 秒轮询，用于判断是否进入「本地模式」，
/// 并在顶部栏呈现服务状态标签（UI 设计规范 4.5 / 6.2 的要求）。
///
/// capabilities 由实际注入的引擎决定：转写与生成都可用时 status 为 ok；
/// 任一不可用则降级为 degraded，前端据此进入本地模式（录音、编辑、导出照常可用）。
/// engines 为附加字段，仅供界面提示「当前使用本地模拟引擎」，不参与前端分支逻辑。
/// </summary>
public static class HealthEndpoints
{
    private const string Version = "0.3.0";

    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", (TranscriptionService transcription, GenerationService generation) =>
        {
            var transcriptionReady = transcription.IsAvailable;
            var generationReady = generation.IsAvailable;

            return Results.Ok(new
            {
                status = transcriptionReady && generationReady ? "ok" : "degraded",
                version = Version,
                capabilities = new
                {
                    transcription = transcriptionReady,
                    generation = generationReady,
                },
                engines = new
                {
                    transcription = transcription.EngineName,
                    generation = generation.ModelName,
                },
                timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:sszzz"),
            });
        });

        return app;
    }
}
