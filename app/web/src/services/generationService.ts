import type { GenerationRequest, GenerationResponse } from '../types/api';
import { requestJson } from './http';

/**
 * I03 生成传记初稿（接口文档 2.3）。
 * mode=merge 用于把多段口述合并成一篇初稿；单段时用 polish。
 * 超时按接口文档 2.5 取 60 秒，超时后由页面给「重试 / 改用手动编辑」两个出口。
 */
const GENERATE_TIMEOUT_MS = 60_000;

export async function generateDraft(
  payload: GenerationRequest,
): Promise<GenerationResponse> {
  return requestJson<GenerationResponse>(
    '/api/polish',
    { method: 'POST', body: JSON.stringify(payload) },
    GENERATE_TIMEOUT_MS,
  );
}
