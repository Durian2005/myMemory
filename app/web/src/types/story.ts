/**
 * 记录（Story）类型定义。
 * 字段命名遵循技术方案 5.5：本地数据字段统一使用小写下划线，与 8.4 数据结构表一致。
 */

/** 记录状态，取值与 PRD 2.1 及技术方案 8.4 一致。 */
export type StoryStatus = '未录入' | '转写中' | '待编辑' | '已完成';

export interface Story {
  /** 记录唯一标识，创建时由前端生成（UUID）。 */
  story_id: string;
  /** 记录或传记标题。 */
  title: string;
  /** 整理后的传记正文，可被用户编辑覆盖。新建时为空白。 */
  polished_text: string;
  /** 记录当前状态。 */
  status: StoryStatus;
  /** 片段数量，用于列表展示。 */
  segment_count: number;
  /** 首次创建时间（ISO 8601）。 */
  created_at: string;
  /** 最近修改时间（ISO 8601）。 */
  updated_at: string;
  /** 最近一次保存正文或标题的时间；新建时尚未保存过正文。 */
  last_saved_at: string | null;
  /**
   * 生成初稿时对应原文的哈希（来自 I03 的 sourceTextHash）。
   * 用于核对「当前初稿基于哪一版原文」，原文改动后哈希不一致即提示重新生成。
   */
  draft_source_hash?: string | null;
  /** 生成初稿所用的引擎标识（如 local-stub），仅用于界面提示与排查。 */
  draft_model?: string | null;
}
