using System.Security.Cryptography;
using System.Text;
using MyMemory.Server.Models;
using MyMemory.Server.Services.Providers;

namespace MyMemory.Server.Services;

/// <summary>
/// 传记初稿生成的业务编排（承载模块 B03 AI 生成）。
///
/// 三条硬约束来自技术方案 8.4 与接口文档 2.3：
/// 1. 原文是唯一内容来源，接口不接收也不返回任何"补充设定"；
/// 2. 只返回初稿，原样保留入参 sourceText 的哈希，保证初稿与原文可追溯；
/// 3. 生成失败时绝不返回半成品文本，避免用户误把残缺内容存成正文。
/// </summary>
public sealed class GenerationService
{
    /// <summary>单次生成的文本上限，超出后让用户分段生成再合并。</summary>
    public const int MaxSourceLength = 20_000;

    private static readonly string[] AllowedModes = ["polish", "merge"];

    private readonly IGenerationProvider _provider;

    public GenerationService(IGenerationProvider provider)
    {
        _provider = provider;
    }

    public bool IsAvailable => _provider.IsAvailable;

    public string ModelName => _provider.Name;

    public async Task<GenerationOutcome> GenerateAsync(
        GenerationRequest? request,
        CancellationToken cancellationToken)
    {
        // 1. 必填字段
        if (request is null
            || string.IsNullOrWhiteSpace(request.StoryId)
            || string.IsNullOrWhiteSpace(request.StoryTitle)
            || string.IsNullOrWhiteSpace(request.Mode))
        {
            return GenerationOutcome.Fail(ErrorCodes.InvalidRequest);
        }

        var mode = request.Mode.Trim().ToLowerInvariant();
        if (!AllowedModes.Contains(mode))
        {
            return GenerationOutcome.Fail(ErrorCodes.InvalidRequest);
        }

        // 2. 原文判空：只填空格同样视为没有内容
        var sourceText = (request.SourceText ?? string.Empty).Trim();
        if (sourceText.Length == 0)
        {
            return GenerationOutcome.Fail(ErrorCodes.TextEmpty);
        }

        // 3. 长度上限
        if (sourceText.Length > MaxSourceLength)
        {
            return GenerationOutcome.Fail(ErrorCodes.TextTooLong);
        }

        // 4. 引擎可用性：不可用时给 503，前端提示"原文已完整保留"
        if (!_provider.IsAvailable)
        {
            return GenerationOutcome.Fail(ErrorCodes.GenerationUnavailable);
        }

        GenerationResult result;
        try
        {
            result = await _provider.GenerateAsync(
                request.StoryTitle.Trim(), sourceText, mode, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return GenerationOutcome.Fail(ErrorCodes.GenerationTimeout);
        }

        if (!result.Success)
        {
            return GenerationOutcome.Fail(result.FailureCode);
        }

        if (string.IsNullOrWhiteSpace(result.Text))
        {
            return GenerationOutcome.Fail(ErrorCodes.GenerationEmpty);
        }

        var polished = result.Text.Trim();
        return GenerationOutcome.Ok(new GenerationResponse
        {
            PolishedText = polished,
            CharCount = CountCharacters(polished),
            Model = result.Model,
            GeneratedAt = DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:sszzz"),
            SourceTextHash = ComputeHash(sourceText),
        });
    }

    /// <summary>字数统计只计有效字符（不含空白与换行），与界面上的「正文字数」口径一致。</summary>
    private static int CountCharacters(string text) =>
        text.Count(ch => !char.IsWhiteSpace(ch));

    /// <summary>对原文取 SHA-256（UTF-8），用于前端核对初稿对应哪一版原文。</summary>
    private static string ComputeHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
