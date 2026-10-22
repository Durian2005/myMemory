/** 时间与文案格式化工具（无业务依赖的纯函数，技术方案 5.2）。 */

/** 把 ISO 8601 时间格式化为「YYYY-MM-DD HH:mm」。 */
export function formatDateTime(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return '时间未知';
  }

  const pad = (value: number) => value.toString().padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(
    date.getHours(),
  )}:${pad(date.getMinutes())}`;
}

/** 列表卡片上的录音段数文案。 */
export function formatSegmentCount(count: number): string {
  return count > 0 ? `共 ${count} 段录音` : '尚未录音';
}

/** 录音时长：小于 1 小时用 mm:ss，超过 1 小时用 h:mm:ss（老年用户更易读）。 */
export function formatDuration(totalSeconds: number): string {
  const safe = Number.isFinite(totalSeconds) && totalSeconds > 0 ? Math.floor(totalSeconds) : 0;
  const hours = Math.floor(safe / 3600);
  const minutes = Math.floor((safe % 3600) / 60);
  const seconds = safe % 60;
  const pad = (value: number) => value.toString().padStart(2, '0');

  return hours > 0 ? `${hours}:${pad(minutes)}:${pad(seconds)}` : `${pad(minutes)}:${pad(seconds)}`;
}

/** 字数统计：与后端 charCount 口径一致，只计非空白字符。 */
export function countCharacters(text: string): number {
  return Array.from(text).filter((char) => !/\s/.test(char)).length;
}
