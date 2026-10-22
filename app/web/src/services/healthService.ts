import type { HealthResponse } from '../types/api';
import { requestJson } from './http';

/**
 * 服务健康检查（I01）。
 * 前端启动时调用一次，之后每 60 秒轮询一次（接口文档 2.1 调用时机）。
 * 探活失败不抛异常：失败即视为进入「本地模式」，录音、编辑与导出照常可用。
 */
export const HEALTH_POLL_INTERVAL_MS = 60_000;

export async function fetchHealth(): Promise<HealthResponse | null> {
  try {
    return await requestJson<HealthResponse>('/api/health', { method: 'GET' }, 3_000);
  } catch {
    return null;
  }
}
