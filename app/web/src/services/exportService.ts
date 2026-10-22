import type { ExportRequest } from '../types/api';
import { requestFile } from './http';

/**
 * I04 导出 DOCX 文档（接口文档 2.4）。
 * 成功后拿到二进制与后端建议的文件名，由页面触发浏览器下载；
 * 前端不参与文档拼装，正文原样交给后端（技术方案 8.3 决策四）。
 */
const EXPORT_TIMEOUT_MS = 20_000;

export async function exportDocx(payload: ExportRequest): Promise<{ blob: Blob; fileName: string }> {
  return requestFile(
    '/api/exports',
    { ...payload, format: 'docx' },
    EXPORT_TIMEOUT_MS,
  );
}

/** 触发浏览器下载。用对象 URL 避免把大文件塞进 data URI。 */
export function triggerDownload(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = fileName;
  document.body.append(anchor);
  anchor.click();
  anchor.remove();
  // 下载触发后释放对象 URL，避免内存泄漏
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
}
