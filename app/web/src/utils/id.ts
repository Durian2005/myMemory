/**
 * 标识生成工具（无业务依赖的纯函数，技术方案 5.2）。
 * 记录标识由前端生成并回传后端，这是保证「新建项目」接口幂等的前提。
 */
export function createStoryId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }
  // 兜底：老旧浏览器不支持 crypto.randomUUID 时，用时间戳 + 随机数保证本机唯一
  const random = Math.random().toString(16).slice(2, 10);
  return `story-${Date.now().toString(16)}-${random}`;
}
