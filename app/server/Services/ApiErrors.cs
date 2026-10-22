using MyMemory.Server.Models;

namespace MyMemory.Server.Services;

/// <summary>
/// 统一错误响应工厂。把业务错误码转换为「HTTP 状态码 + 统一 JSON 错误体」，
/// 保证前端只需按 code 分支，不必解析 HTTP 状态码（见《标准化 API 接口文档》1.2 接口设计约定）。
/// </summary>
public static class ApiErrors
{
    public static IResult From(string errorCode)
    {
        var definition = ErrorCatalog.Get(errorCode);
        return Results.Json(
            new ApiErrorResponse
            {
                Code = definition.Code,
                Message = definition.Message,
                Retryable = definition.Retryable,
            },
            statusCode: definition.HttpStatus);
    }
}
