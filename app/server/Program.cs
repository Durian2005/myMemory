using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using MyMemory.Server.Endpoints;
using MyMemory.Server.Services;
using MyMemory.Server.Services.Providers;

var builder = WebApplication.CreateBuilder(args);

// 只监听本机回环地址：后端不对局域网或公网开放（技术方案 6.2 安全）。
builder.WebHost.UseUrls("http://127.0.0.1:5210");

// JSON 统一使用 camelCase，与《标准化 API 接口文档》的字段命名保持一致；
// 中文不转义为 \uXXXX，便于前端调试与日志排查。
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
});

// 音频上传上限：业务上限 25MB 在 TranscriptionService 内判断，
// 这里把服务器侧上限放宽到 30MB，让「文件过大」走业务错误码而不是框架 413。
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 30L * 1024 * 1024;
});

// 跨域只允许本地前端源（技术方案 6.2）。
const string LocalWebCorsPolicy = "local-web";
builder.Services.AddCors(options =>
{
    options.AddPolicy(LocalWebCorsPolicy, policy => policy
        .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
        .AllowAnyHeader()
        .AllowAnyMethod()
        .WithExposedHeaders("Content-Disposition"));
});

// 外部服务通过接口注入：当前为本地模拟实现，接入真实服务时只替换实现类，
// 业务层（TranscriptionService / GenerationService）与前端均无需改动。
builder.Services.AddSingleton<ISpeechToTextProvider, StubSpeechToTextProvider>();
builder.Services.AddSingleton<IGenerationProvider, StubGenerationProvider>();

// 业务服务：无状态、可单测（技术方案 6.6）。
builder.Services.AddSingleton<ProjectService>();
builder.Services.AddSingleton<TranscriptionService>();
builder.Services.AddSingleton<GenerationService>();
builder.Services.AddSingleton<DocxExportService>();

var app = builder.Build();

app.UseCors(LocalWebCorsPolicy);

// 分发版把前端打包产物放在 wwwroot，由后端同源托管：
// 用户只需要运行一个可执行文件，不必再装 Node、也不必开第二个终端。
// 开发态没有 wwwroot 时这段不会生效，前端仍走 vite dev server（5173）。
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHealthEndpoints();
app.MapProjectEndpoints();
app.MapTranscriptionEndpoints();
app.MapGenerationEndpoints();
app.MapExportEndpoints();

// 前端用 history 路由（/story/xxx/draft），刷新时需要回落到 index.html。
// API 路由在前面已经注册，只有未命中的请求才会走到这里。
app.MapFallbackToFile("index.html");

app.Run();
