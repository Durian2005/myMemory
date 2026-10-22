import type { TranscriptionResponse } from '../types/api';
import { requestForm } from './http';

/**
 * I02 上传口述素材并转写（接口文档 2.2）。
 *
 * 只上传当前这一段音频，服务端只返回原始转写文本，不做润色；
 * 返回结果由前端写入本地片段，原始转写一经写入即不可被 AI 覆盖（技术方案 8.4）。
 */
const TRANSCRIBE_TIMEOUT_MS = 90_000;

export interface TranscribeParams {
  storyId: string;
  segmentId: string;
  audio: Blob;
  durationSeconds: number;
}

export async function transcribeSegment(params: TranscribeParams): Promise<TranscriptionResponse> {
  const form = new FormData();
  const mime = params.audio.type || 'audio/webm';

  form.append('audio', params.audio, `segment-${params.segmentId}${extensionOf(mime)}`);
  form.append('segmentId', params.segmentId);
  form.append('storyId', params.storyId);
  form.append('durationSeconds', params.durationSeconds.toFixed(2));
  form.append('audioFormat', mime);

  return requestForm<TranscriptionResponse>('/api/transcriptions', form, TRANSCRIBE_TIMEOUT_MS);
}

/** 按 MIME 给出文件扩展名，便于服务端按扩展名兜底嗅探。 */
function extensionOf(mime: string): string {
  if (mime.includes('wav')) {
    return '.wav';
  }
  if (mime.includes('mpeg') || mime.includes('mp3')) {
    return '.mp3';
  }
  if (mime.includes('mp4') || mime.includes('m4a')) {
    return '.m4a';
  }
  return '.webm';
}
