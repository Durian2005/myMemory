using MyMemory.Server.Models;
using MyMemory.Server.Services.Providers;

namespace MyMemory.Server.Services;

/// <summary>
/// 口述素材接入与转写的业务编排（承载模块 B01 素材接入 + B02 语音转写）。
///
/// 职责边界（《后端模块划分清单》5.1）：只做「是否接触音频」这一层的事 ——
/// 参数校验、格式嗅探、大小与时长上限、调用转写引擎、组装原始转写结果。
/// 不持久化用户数据，不写业务日志正文（技术方案 6.6）。
/// </summary>
public sealed class TranscriptionService
{
    /// <summary>单段音频字节上限：约 30 分钟 webm/opus 的保守值。</summary>
    public const long MaxAudioBytes = 25L * 1024 * 1024;

    /// <summary>单段音频时长上限（秒），与接口文档「单段不超过 30 分钟」一致。</summary>
    public const double MaxDurationSeconds = 30 * 60;

    private readonly ISpeechToTextProvider _provider;

    public TranscriptionService(ISpeechToTextProvider provider)
    {
        _provider = provider;
    }

    /// <summary>当前转写引擎是否可用（供 /api/health 的 capabilities 使用）。</summary>
    public bool IsAvailable => _provider.IsAvailable;

    public string EngineName => _provider.Name;

    public async Task<TranscriptionOutcome> TranscribeAsync(
        Stream? audio,
        long audioLength,
        string? declaredFileName,
        string? declaredContentType,
        string? segmentId,
        string? storyId,
        double? durationSeconds,
        CancellationToken cancellationToken)
    {
        // 1. 必填项：segmentId / storyId 是定位与幂等的前提，缺失即视为输入不完整
        if (string.IsNullOrWhiteSpace(segmentId) || string.IsNullOrWhiteSpace(storyId))
        {
            return TranscriptionOutcome.Fail(ErrorCodes.InvalidRequest);
        }

        // 2. 未收到音频
        if (audio is null || audioLength <= 0)
        {
            return TranscriptionOutcome.Fail(ErrorCodes.AudioMissing);
        }

        // 3. 大小上限：先于读取内容判断，避免把超大文件整个读进内存
        if (audioLength > MaxAudioBytes)
        {
            return TranscriptionOutcome.Fail(ErrorCodes.AudioTooLarge);
        }

        // 4. 格式嗅探：以文件头为准，声明的内容类型只作为兜底参考
        var header = new byte[16];
        var read = await audio.ReadAsync(header.AsMemory(0, header.Length), cancellationToken);
        var detected = DetectAudioFormat(header, read, declaredFileName, declaredContentType);
        if (detected is null)
        {
            return TranscriptionOutcome.Fail(ErrorCodes.AudioFormatUnsupported);
        }

        audio.Position = 0;

        // 5. 时长校验：前端测量值缺失或非正数视为无法解码；超出上限单独给码
        if (durationSeconds is null || double.IsNaN(durationSeconds.Value) || durationSeconds.Value <= 0)
        {
            return TranscriptionOutcome.Fail(ErrorCodes.AudioEmpty);
        }

        if (durationSeconds.Value > MaxDurationSeconds)
        {
            return TranscriptionOutcome.Fail(ErrorCodes.AudioTooLong);
        }

        // 6. 调用引擎（引擎不可用/超时/结果为空都由引擎返回业务错误码）
        if (!_provider.IsAvailable)
        {
            return TranscriptionOutcome.Fail(ErrorCodes.TranscriptionUnavailable);
        }

        SpeechToTextResult result;
        try
        {
            result = await _provider.TranscribeAsync(
                audio, detected, durationSeconds.Value, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // 客户端断开或内部超时：按可重试的转写超时处理，不当作 500
            return TranscriptionOutcome.Fail(ErrorCodes.TranscriptionTimeout);
        }

        if (!result.Success)
        {
            return TranscriptionOutcome.Fail(result.FailureCode);
        }

        if (string.IsNullOrWhiteSpace(result.Text))
        {
            return TranscriptionOutcome.Fail(ErrorCodes.TranscriptionEmpty);
        }

        return TranscriptionOutcome.Ok(new TranscriptionResponse
        {
            SegmentId = segmentId.Trim(),
            TranscribedText = result.Text,
            DurationSeconds = Math.Round(durationSeconds.Value, 2),
            Engine = result.Engine,
            FinishedAt = DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:sszzz"),
        });
    }

    /// <summary>
    /// 按文件头判断音频容器格式。识别不了时返回 null，由调用方给「格式不支持」。
    /// webm / matroska: 1A 45 DF A3；wav: RIFF....WAVE；mp3: ID3 或帧同步 0xFFEx；m4a/mp4: 偏移 4 处 ftyp。
    /// </summary>
    private static string? DetectAudioFormat(
        byte[] header, int length, string? fileName, string? contentType)
    {
        if (length >= 4 && header[0] == 0x1A && header[1] == 0x45 && header[2] == 0xDF && header[3] == 0xA3)
        {
            return "audio/webm";
        }

        if (length >= 12
            && header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F'
            && header[8] == (byte)'W' && header[9] == (byte)'A' && header[10] == (byte)'V' && header[11] == (byte)'E')
        {
            return "audio/wav";
        }

        if (length >= 3 && header[0] == (byte)'I' && header[1] == (byte)'D' && header[2] == (byte)'3')
        {
            return "audio/mpeg";
        }

        if (length >= 2 && header[0] == 0xFF && (header[1] & 0xE0) == 0xE0)
        {
            return "audio/mpeg";
        }

        if (length >= 8
            && header[4] == (byte)'f' && header[5] == (byte)'t' && header[6] == (byte)'y' && header[7] == (byte)'p')
        {
            return "audio/mp4";
        }

        // 文件头不可识别时，仅当扩展名明确属于受支持格式才放行（兼容部分浏览器产出的边角格式）
        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        return extension switch
        {
            ".webm" => "audio/webm",
            ".wav" => "audio/wav",
            ".mp3" => "audio/mpeg",
            ".m4a" => "audio/mp4",
            _ => null,
        };
    }
}
