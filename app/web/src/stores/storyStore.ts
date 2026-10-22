import { defineStore } from 'pinia';
import { ref } from 'vue';
import * as localDb from '../db/localDb';
import { generateDraft } from '../services/generationService';
import { createProject } from '../services/projectService';
import { transcribeSegment } from '../services/transcriptionService';
import { ApiError } from '../types/api';
import type { Segment } from '../types/segment';
import type { Story, StoryStatus } from '../types/story';
import { createStoryId } from '../utils/id';

/**
 * 记录状态模块（storyStore）。
 * 负责记录列表、当前记录的片段集合，以及「素材入库 → 转写 → 生成初稿 → 保存」的流程编排
 * （技术方案 5.2：stores 负责编排，不写数据存取细节；落盘细节在 db/localDb）。
 */
export const useStoryStore = defineStore('story', () => {
  const stories = ref<Story[]>([]);
  const isLoading = ref(false);
  /** 列表加载失败时的错误信息；为空表示无错误。 */
  const loadError = ref('');

  /** 当前正在操作的记录（P3 录音页 / P4 生成页使用）。 */
  const currentStory = ref<Story | null>(null);
  /** 当前记录的全部片段，按序号升序。 */
  const segments = ref<Segment[]>([]);
  /** 片段加载失败信息。 */
  const segmentsError = ref('');
  /** 上一次新建是否走了「后端不可用」的本地兜底，供页面提示文案使用。 */
  const lastCreateOffline = ref(false);

  async function loadStories(): Promise<void> {
    isLoading.value = true;
    loadError.value = '';
    try {
      stories.value = await localDb.listStories();
    } catch {
      loadError.value = '读取本地数据失败，请刷新页面重试';
      stories.value = [];
    } finally {
      isLoading.value = false;
    }
  }

  /**
   * 新建记录：先经后端规范化并取得权威结构，再写入本地数据库。
   * 后端失败时整个操作失败，本地不留半成品数据（技术方案 7.8 异常与恢复原则）。
   *
   * 例外：后端根本没启动（MYM_5002）时降级为本地创建。
   * 依据是技术方案 8.3「无后端时降级为本地工具」——老人正在等着记录，不该因为服务没开就卡住。
   * 其余业务错误（如标题为空、标题超长）仍然抛给页面处理，不能悄悄吞掉。
   */
  async function addStory(title: string): Promise<Story> {
    lastCreateOffline.value = false;

    try {
      const story = await createProject(title);
      await localDb.putStory(story);
      stories.value = [story, ...stories.value];
      return story;
    } catch (error) {
      const isOffline = error instanceof ApiError && error.code === 'MYM_5002';
      if (!isOffline) {
        throw error;
      }

      const now = new Date().toISOString();
      const localStory: Story = {
        story_id: createStoryId(),
        title: title.trim(),
        polished_text: '',
        status: '未录入',
        segment_count: 0,
        created_at: now,
        updated_at: now,
        last_saved_at: null,
      };

      await localDb.putStory(localStory);
      stories.value = [localStory, ...stories.value];
      lastCreateOffline.value = true;
      return localStory;
    }
  }

  async function removeStory(storyId: string): Promise<void> {
    await localDb.deleteStory(storyId);
    stories.value = stories.value.filter((story) => story.story_id !== storyId);
  }

  function findStory(storyId: string): Story | undefined {
    return stories.value.find((story) => story.story_id === storyId);
  }

  /** 载入单条记录及其片段。记录不存在时返回 false，由页面给「记录不存在」兜底。 */
  async function loadStoryDetail(storyId: string): Promise<boolean> {
    segmentsError.value = '';
    try {
      const story = await localDb.getStory(storyId);
      if (!story) {
        currentStory.value = null;
        segments.value = [];
        return false;
      }

      currentStory.value = story;
      segments.value = await localDb.listSegments(storyId);
      return true;
    } catch {
      segmentsError.value = '读取本记录的内容失败，请返回列表后重试';
      currentStory.value = null;
      segments.value = [];
      return false;
    }
  }

  /** 把片段的增删结果同步回记录：片段数量、状态与更新时间。 */
  async function syncStoryMeta(): Promise<void> {
    const story = currentStory.value;
    if (!story) {
      return;
    }

    story.segment_count = segments.value.length;
    story.status = resolveStatus();
    story.updated_at = new Date().toISOString();
    await localDb.putStory(story);
    replaceInList(story);
  }

  /** 状态推导：无片段=未录入；有片段但无初稿=待编辑；已导出=已完成。 */
  function resolveStatus(): StoryStatus {
    const story = currentStory.value;
    if (!story) {
      return '未录入';
    }
    if (segments.value.length === 0) {
      return '未录入';
    }
    if (segments.value.some((segment) => segment.status === '转写中')) {
      return '转写中';
    }
    return story.status === '已完成' ? '已完成' : '待编辑';
  }

  function replaceInList(story: Story): void {
    const index = stories.value.findIndex((item) => item.story_id === story.story_id);
    if (index >= 0) {
      stories.value[index] = { ...story };
    }
  }

  /** 下一个片段序号：始终取当前最大值 + 1，删除后续号不回收，避免转写结果错位。 */
  function nextSeq(): number {
    return segments.value.reduce((max, segment) => Math.max(max, segment.seq), 0) + 1;
  }

  /**
   * 保存一段录音并立即转写。
   * 顺序很重要：先入库再上传，保证转写失败时音频仍在本地，用户可重试（接口文档 2.2 的降级约定）。
   */
  async function addAudioSegment(
    audio: Blob,
    durationSeconds: number,
  ): Promise<Segment> {
    const story = currentStory.value;
    if (!story) {
      throw new ApiError('MYM_5001', '记录未加载，请返回列表后重试', false);
    }

    const segment: Segment = {
      segment_id: createStoryId(),
      story_id: story.story_id,
      seq: nextSeq(),
      kind: 'audio',
      audio_mime: audio.type || 'audio/webm',
      audio_duration: durationSeconds,
      audio_blob: audio,
      transcribed_text: '',
      status: '待转写',
      transcription_error: null,
      created_at: new Date().toISOString(),
    };

    await localDb.putSegment(segment);
    segments.value = [...segments.value, segment];
    await syncStoryMeta();

    await transcribe(segment.segment_id);
    return segments.value.find((item) => item.segment_id === segment.segment_id) ?? segment;
  }

  /** 对指定片段调用 I02 转写。失败时把原因写进片段，不影响其他片段。 */
  async function transcribe(segmentId: string): Promise<void> {
    const story = currentStory.value;
    const segment = segments.value.find((item) => item.segment_id === segmentId);
    if (!story || !segment || segment.kind !== 'audio' || !segment.audio_blob) {
      return;
    }

    segment.status = '转写中';
    segment.transcription_error = null;
    await localDb.putSegment(segment);

    try {
      const response = await transcribeSegment({
        storyId: story.story_id,
        segmentId: segment.segment_id,
        audio: segment.audio_blob,
        durationSeconds: segment.audio_duration,
      });

      segment.transcribed_text = response.transcribedText;
      segment.audio_duration = response.durationSeconds;
      segment.status = '已转写';
      segment.transcription_error = null;
    } catch (error) {
      segment.status = '转写失败';
      segment.transcription_error =
        error instanceof ApiError ? error.message : '转写失败，请稍后重试';
    } finally {
      await localDb.putSegment(segment);
      segments.value = [...segments.value];
      await syncStoryMeta();
    }
  }

  /** 保存一段手写文字素材。文字不经过转写接口，直接成为原始文本。 */
  async function addTextSegment(text: string): Promise<void> {
    const story = currentStory.value;
    if (!story) {
      throw new ApiError('MYM_5001', '记录未加载，请返回列表后重试', false);
    }

    const segment: Segment = {
      segment_id: createStoryId(),
      story_id: story.story_id,
      seq: nextSeq(),
      kind: 'text',
      audio_mime: null,
      audio_duration: 0,
      audio_blob: null,
      transcribed_text: text.trim(),
      status: '已转写',
      transcription_error: null,
      created_at: new Date().toISOString(),
    };

    await localDb.putSegment(segment);
    segments.value = [...segments.value, segment];
    await syncStoryMeta();
  }

  /** 删除单个片段，并同步记录状态。 */
  async function removeSegment(segmentId: string): Promise<void> {
    await localDb.deleteSegment(segmentId);
    segments.value = segments.value.filter((segment) => segment.segment_id !== segmentId);
    await syncStoryMeta();
  }

  /** 原始转写全文：按片段序号拼接，作为生成初稿的唯一内容来源。 */
  function buildSourceText(): string {
    return segments.value
      .map((segment) => segment.transcribed_text.trim())
      .filter((text) => text.length > 0)
      .join('\n\n');
  }

  /**
   * 生成初稿并立即落盘。
   * 先落盘而不是留在内存里，是为了避免用户误关页面丢掉刚生成的内容；
   * 之后的编辑由用户点「保存初稿」显式提交（PRD 2.4 的显式保存原则）。
   */
  async function generateDraftFromSegments(): Promise<Story> {
    const story = currentStory.value;
    if (!story) {
      throw new ApiError('MYM_5001', '记录未加载，请返回列表后重试', false);
    }

    const sourceText = buildSourceText();
    if (sourceText.trim().length === 0) {
      throw new ApiError('MYM_1006', '没有可生成的内容，请先完成转写或手动填写正文', false);
    }

    const response = await generateDraft({
      storyId: story.story_id,
      storyTitle: story.title,
      sourceText,
      mode: segments.value.length > 1 ? 'merge' : 'polish',
    });

    story.polished_text = response.polishedText;
    story.draft_source_hash = response.sourceTextHash;
    story.draft_model = response.model;
    story.status = '待编辑';
    story.updated_at = new Date().toISOString();
    await localDb.putStory(story);
    replaceInList(story);

    return story;
  }

  /** 保存用户编辑后的初稿（显式保存）。 */
  async function saveDraft(text: string): Promise<void> {
    const story = currentStory.value;
    if (!story) {
      throw new ApiError('MYM_5001', '记录未加载，请返回列表后重试', false);
    }

    const now = new Date().toISOString();
    story.polished_text = text;
    story.last_saved_at = now;
    story.updated_at = now;
    story.status = story.status === '已完成' ? '已完成' : '待编辑';
    await localDb.putStory(story);
    replaceInList(story);
  }

  /** 导出成功后把记录标记为已完成。 */
  async function markExported(): Promise<void> {
    const story = currentStory.value;
    if (!story) {
      return;
    }
    story.status = '已完成';
    story.updated_at = new Date().toISOString();
    await localDb.putStory(story);
    replaceInList(story);
  }

  return {
    stories,
    isLoading,
    loadError,
    currentStory,
    segments,
    segmentsError,
    lastCreateOffline,
    loadStories,
    addStory,
    removeStory,
    findStory,
    loadStoryDetail,
    addAudioSegment,
    addTextSegment,
    transcribe,
    removeSegment,
    buildSourceText,
    generateDraftFromSegments,
    saveDraft,
    markExported,
  };
});
