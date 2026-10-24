/**
 * 接口出入参类型定义。
 * 与《myMemory 标准化 API 接口文档》逐字段对应，命名保持 camelCase（接口文档约定）。
 */

/** POST /api/projects 请求体。 */
export interface CreateProjectRequest {
  /** 记录标题，最长 40 字。 */
  title: string;
  /** 前端生成的本地记录标识（UUID），作为幂等键。 */
  clientStoryId: string;
  /** 前端确定的创建时间（ISO 8601）。 */
  createdAt: string;
}

/** POST /api/projects 返回体。 */
export interface CreateProjectResponse {
  storyId: string;
  title: string;
  status: string;
  segmentCount: number;
  createdAt: string;
  created: boolean;
}

/** 统一错误响应体，字段与后端 ApiErrorResponse 一致。 */
export interface ApiErrorBody {
  code: string;
  message: string;
  retryable: boolean;
}

/** GET /api/health 返回体（接口文档 2.1）。 */
export interface HealthResponse {
  status: 'ok' | 'degraded';
  version: string;
  capabilities: {
    transcription: boolean;
    generation: boolean;
  };
  /** 附加字段：当前注入的引擎标识（local-stub 表示后端注入的是规则引擎实现），仅用于界面提示。 */
  engines?: {
    transcription: string;
    generation: string;
  };
  timestamp: string;
}

/** POST /api/transcriptions 返回体（接口文档 2.2）。 */
export interface TranscriptionResponse {
  segmentId: string;
  transcribedText: string;
  durationSeconds: number;
  engine: string;
  finishedAt: string;
}

/** POST /api/polish 请求体（接口文档 2.3）。 */
export interface GenerationRequest {
  storyId: string;
  storyTitle: string;
  sourceText: string;
  mode: 'polish' | 'merge';
}

/** POST /api/polish 返回体（接口文档 2.3）。 */
export interface GenerationResponse {
  polishedText: string;
  charCount: number;
  model: string;
  generatedAt: string;
  sourceTextHash: string;
}

/** POST /api/exports 请求体（接口文档 2.4）。 */
export interface ExportRequest {
  title: string;
  body: string;
  segmentCount?: number;
  format?: 'docx';
}

/** 服务可用性状态，供顶部栏呈现服务状态标签（UI 规范 6.2）。 */
export type ServiceMode = 'unknown' | 'online' | 'local';

/**
 * 接口层统一抛出的错误类型。
 * 前端只按 business code 分支，不依赖 HTTP 状态码（接口文档 1.2）。
 */
export class ApiError extends Error {
  code: string;
  retryable: boolean;

  constructor(code: string, message: string, retryable: boolean) {
    super(message);
    this.name = 'ApiError';
    this.code = code;
    this.retryable = retryable;
  }
}
