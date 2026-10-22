import type { CreateProjectRequest, CreateProjectResponse } from '../types/api';
import type { Story, StoryStatus } from '../types/story';
import { createStoryId } from '../utils/id';
import { requestJson } from './http';

/**
 * 新建传记项目（POST /api/projects）。
 * 说明：该接口相对实验3《标准化 API 接口文档》的 I01–I04 属新增接口。
 * 后端只做准入校验与结构规范化，记录的落盘仍由前端写入 IndexedDB（技术方案 8.3 决策一）。
 */
export async function createProject(title: string): Promise<Story> {
  const createdAt = new Date().toISOString();
  const payload: CreateProjectRequest = {
    title,
    clientStoryId: createStoryId(),
    createdAt,
  };

  const response = await requestJson<CreateProjectResponse>('/api/projects', {
    method: 'POST',
    body: JSON.stringify(payload),
  });

  return {
    story_id: response.storyId,
    title: response.title,
    polished_text: '',
    status: response.status as StoryStatus,
    segment_count: response.segmentCount,
    created_at: response.createdAt,
    updated_at: response.createdAt,
    last_saved_at: null,
  };
}
