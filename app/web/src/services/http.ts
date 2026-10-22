import { ApiError } from '../types/api';
import type { ApiErrorBody } from '../types/api';

/**
 * 后端接口调用的统一封装：负责请求、超时与业务错误码的转换（技术方案 5.2）。
 * 前端不接触任何密钥，后端地址通过构建期公开变量 VITE_API_BASE 注入（技术方案 5.6）。
 */
export const API_BASE: string =
  (import.meta.env.VITE_API_BASE as string | undefined) ?? 'http://127.0.0.1:5210';

/** 默认超时。各接口按《接口文档》2.5 的超时约定单独传入：转写 90 秒、生成 60 秒、导出 20 秒。 */
const DEFAULT_TIMEOUT_MS = 20_000;

function buildHeaders(init: RequestInit): Headers {
  const headers = new Headers(init.headers ?? {});
  // 只有 JSON 请求才声明 Content-Type；multipart 必须让浏览器自动带 boundary
  if (!(init.body instanceof FormData) && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }
  return headers;
}

async function send(
  path: string,
  init: RequestInit,
  timeoutMs: number,
): Promise<Response> {
  const controller = new AbortController();
  const timer = window.setTimeout(() => controller.abort(), timeoutMs);

  try {
    return await fetch(`${API_BASE}${path}`, {
      ...init,
      signal: controller.signal,
      headers: buildHeaders(init),
    });
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') {
      throw new ApiError('MYM_5002', '请求超时，请确认后端服务已启动后重试', true);
    }
    throw new ApiError('MYM_5002', '无法连接后端服务，请确认后端已启动后重试', true);
  } finally {
    window.clearTimeout(timer);
  }
}

async function safeReadError(response: Response): Promise<ApiErrorBody | undefined> {
  try {
    return (await response.json()) as ApiErrorBody;
  } catch {
    return undefined;
  }
}

/** JSON 请求。错误统一转换为 ApiError，页面只按 code 分支。 */
export async function requestJson<T>(
  path: string,
  init: RequestInit = {},
  timeoutMs: number = DEFAULT_TIMEOUT_MS,
): Promise<T> {
  const response = await send(path, init, timeoutMs);

  if (!response.ok) {
    const body = (await safeReadError(response)) ?? null;
    if (body) {
      throw new ApiError(body.code, body.message, body.retryable);
    }
    throw new ApiError('MYM_5001', '服务出现异常，请稍后重试', true);
  }

  return (await response.json()) as T;
}

/** multipart 上传（用于 I02 上传音频并转写）。 */
export async function requestForm<T>(
  path: string,
  form: FormData,
  timeoutMs: number = DEFAULT_TIMEOUT_MS,
): Promise<T> {
  return requestJson<T>(path, { method: 'POST', body: form }, timeoutMs);
}

/** 下载文件（用于 I04 导出 DOCX）。成功时返回二进制与后端建议的文件名。 */
export async function requestFile(
  path: string,
  body: unknown,
  timeoutMs: number = DEFAULT_TIMEOUT_MS,
): Promise<{ blob: Blob; fileName: string }> {
  const response = await send(
    path,
    { method: 'POST', body: JSON.stringify(body) },
    timeoutMs,
  );

  if (!response.ok) {
    const errorBody = (await safeReadError(response)) ?? null;
    if (errorBody) {
      throw new ApiError(errorBody.code, errorBody.message, errorBody.retryable);
    }
    throw new ApiError('MYM_4001', '文档生成失败，你的内容未受影响，请稍后重试', true);
  }

  const disposition = response.headers.get('Content-Disposition') ?? '';
  return {
    blob: await response.blob(),
    fileName: parseFileName(disposition) ?? '我的传记.docx',
  };
}

/** 从 Content-Disposition 中解析文件名，优先取 RFC 5987 的 filename*。 */
function parseFileName(disposition: string): string | undefined {
  const utf8Match = /filename\*=UTF-8''([^;]+)/i.exec(disposition);
  if (utf8Match?.[1]) {
    try {
      return decodeURIComponent(utf8Match[1]);
    } catch {
      return utf8Match[1];
    }
  }

  const plainMatch = /filename="?([^";]+)"?/i.exec(disposition);
  return plainMatch?.[1];
}
