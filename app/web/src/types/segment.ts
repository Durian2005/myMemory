/**
 * 口述片段（Segment）类型定义。
 *
 * 为什么要有「片段」这一层：一段记录可能由多次录音或手工文字拼成，
 * 若只用一个 audio_url 字段无法表达多段录音（技术方案 8.4 的结构决策三）。
 * 片段既是转写的单位，也是「原始转写文本不可被 AI 覆盖」的存放位置。
 */
export type SegmentKind = 'audio' | 'text';

/** 片段状态：待转写 / 转写中 / 已转写 / 转写失败。 */
export type SegmentStatus = '待转写' | '转写中' | '已转写' | '转写失败';

/**
 * 这段文字由谁产生。用于在界面上如实标注来源，避免把不同引擎的结果混为一谈。
 * - browser-webspeech：浏览器语音识别（录音时同步识别，需联网）
 * - server-stub：后端备用引擎（当前为本地模拟，仅保证链路可用）
 * - manual：用户自己打字录入
 */
export type TranscriptionEngine = 'browser-webspeech' | 'server-stub' | 'manual';

export interface Segment {
  /** 片段唯一标识，由前端生成（UUID），同时作为转写接口的幂等键。 */
  segment_id: string;
  /** 所属记录标识。 */
  story_id: string;
  /** 片段序号，从 1 开始，按录入顺序递增。 */
  seq: number;
  /** 素材类型：录音或直接输入的文字。 */
  kind: SegmentKind;
  /** 音频 MIME 类型；文字片段为 null。 */
  audio_mime: string | null;
  /** 音频时长（秒）；文字片段为 0。 */
  audio_duration: number;
  /** 音频二进制；文字片段为 null。音频保存在本地，不上传留存。 */
  audio_blob: Blob | null;
  /** 原始转写文本或用户手写的文字素材。写入后 AI 不得覆盖。 */
  transcribed_text: string;
  /** 转写状态。 */
  status: SegmentStatus;
  /** 转写失败时的可读原因；成功时为 null。 */
  transcription_error: string | null;
  /** 这段文字的来源引擎；旧数据可能没有该字段。 */
  transcription_engine?: TranscriptionEngine | null;
  /** 录入时间（ISO 8601）。 */
  created_at: string;
}
