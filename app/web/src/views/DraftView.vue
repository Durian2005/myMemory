<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { onBeforeRouteLeave, useRoute, useRouter } from 'vue-router';
import AppTopBar from '../components/AppTopBar.vue';
import ExportDialog from '../components/ExportDialog.vue';
import { triggerDownload, exportDocx } from '../services/exportService';
import { useStoryStore } from '../stores/storyStore';
import { ApiError } from '../types/api';
import { countCharacters, formatDuration } from '../utils/formatTime';
import { showToast } from '../utils/useToast';

/**
 * P4 生成与初稿页（技术方案 9.6，合并了 P5 的编辑能力）。
 *
 * 页面只做三件事：展示原始转写（只读）、生成并编辑初稿、导出文档。
 * 「原始转写」与「AI 初稿」始终分栏并列，AI 永远不会覆盖原文（技术方案 8.4 数据处理原则）。
 */
const route = useRoute();
const router = useRouter();
const store = useStoryStore();

const storyId = computed(() => String(route.params.storyId ?? ''));
const notFound = ref(false);
const draft = ref('');
const isGenerating = ref(false);
const isSaving = ref(false);
const generateError = ref('');
const saveMessage = ref('');
const dirty = ref(false);

const exportVisible = ref(false);
const exporting = ref(false);
const exportError = ref('');

const sourceText = computed(() => store.buildSourceText());
const transcribedCount = computed(
  () => store.segments.filter((segment) => segment.status === '已转写').length,
);
const progressPercent = computed(() =>
  store.segments.length === 0
    ? 0
    : Math.round((transcribedCount.value / store.segments.length) * 100),
);
const totalSeconds = computed(() =>
  store.segments.reduce((sum, segment) => sum + segment.audio_duration, 0),
);
const charCount = computed(() => countCharacters(draft.value));
const isStubEngine = computed(
  () => (store.currentStory?.draft_model ?? '').includes('stub'),
);
const hasDraft = computed(() => draft.value.trim().length > 0);

onMounted(async () => {
  const ok = await store.loadStoryDetail(storyId.value);
  if (!ok) {
    notFound.value = true;
    return;
  }
  draft.value = store.currentStory?.polished_text ?? '';
});

onBeforeRouteLeave(() => {
  // 未保存拦截：初稿是用户亲手改过的内容，不能让一次误点返回就丢掉（PRD 5.6）
  if (dirty.value) {
    return window.confirm('初稿还有未保存的修改，确定要离开吗？');
  }
  return true;
});

function handleDraftInput(): void {
  dirty.value = true;
  saveMessage.value = '';
}

async function generate(): Promise<void> {
  if (store.segments.length === 0) {
    generateError.value = '还没有可用的口述内容，请先回到上一步录入素材';
    return;
  }

  if (sourceText.value.trim().length === 0) {
    generateError.value = '当前素材还没有转写结果，请先回上一步完成转写或填写文字素材';
    return;
  }

  isGenerating.value = true;
  generateError.value = '';
  try {
    const story = await store.generateDraftFromSegments();
    draft.value = story.polished_text;
    dirty.value = false;
    showToast('初稿已生成');
  } catch (error) {
    generateError.value =
      error instanceof ApiError ? error.message : '生成失败，原始转写文本已保留，请重试';
  } finally {
    isGenerating.value = false;
  }
}

async function save(): Promise<void> {
  if (draft.value.trim().length === 0) {
    saveMessage.value = '正文还是空的，写点内容再保存';
    return;
  }

  isSaving.value = true;
  try {
    await store.saveDraft(draft.value);
    dirty.value = false;
    saveMessage.value = '已保存到本机';
    showToast('初稿已保存');
  } catch {
    saveMessage.value = '保存失败，请稍后重试';
  } finally {
    isSaving.value = false;
  }
}

function restoreSaved(): void {
  draft.value = store.currentStory?.polished_text ?? '';
  dirty.value = false;
  saveMessage.value = '已恢复到上次保存的内容';
}

function openExport(): void {
  if (!hasDraft.value) {
    saveMessage.value = '正文为空，请先生成或填写内容再导出';
    return;
  }
  exportError.value = '';
  exportVisible.value = true;
}

async function confirmExport(): Promise<void> {
  const story = store.currentStory;
  if (!story) {
    return;
  }

  exporting.value = true;
  exportError.value = '';
  try {
    const { blob, fileName } = await exportDocx({
      title: story.title,
      body: draft.value,
      segmentCount: store.segments.length,
      format: 'docx',
    });
    triggerDownload(blob, fileName);
    await store.markExported();
    exportVisible.value = false;
    showToast('文档已导出');
  } catch (error) {
    exportError.value =
      error instanceof ApiError ? error.message : '导出失败，你的内容未受影响，请稍后重试';
  } finally {
    exporting.value = false;
  }
}

function goBack(): void {
  void router.push({ name: 'record', params: { storyId: storyId.value } });
}

function goHome(): void {
  void router.push({ name: 'home' });
}
</script>

<template>
  <div class="app-shell">
    <AppTopBar :title="store.currentStory?.title ?? '生成初稿'" show-back @back="goBack" />

    <main class="app-main">
      <div v-if="notFound" class="error-block">
        <span class="error-block__bar"></span>
        <div>
          <p class="error-block__title">找不到这条记录</p>
          <p class="error-block__reason">它可能已被删除，请返回列表重新选择。</p>
        </div>
      </div>

      <template v-else>
        <!-- 事实提示条：AI 只做整理，不负责事实 -->
        <div class="notice notice--warning">
          <span class="notice__bar"></span>
          <div>
            <p class="notice__title">AI 生成内容可能存在错误，请核对人物、时间、地点和事件。</p>
            <p class="notice__text">原始转写文本不会被 AI 修改，可以随时对照。</p>
          </div>
        </div>

        <!-- 转写进度 -->
        <section class="card progress-card">
          <div class="progress-card__head">
            <h2 class="card__title">转写进度</h2>
            <span class="tag tag--neutral">{{ progressPercent }}%</span>
          </div>
          <div class="progress" role="progressbar" :aria-valuenow="progressPercent" aria-valuemin="0" aria-valuemax="100">
            <div class="progress__bar" :style="{ width: `${progressPercent}%` }"></div>
          </div>
          <p class="progress-card__meta">
            已完成 {{ transcribedCount }} / {{ store.segments.length }} 段 · 累计录音
            {{ formatDuration(totalSeconds) }}
          </p>
        </section>

        <!-- 原始转写：只读 -->
        <section class="card">
          <div class="card__head">
            <h2 class="card__title">原始转写文本</h2>
            <span class="tag tag--neutral">只读 · AI 不可覆盖</span>
          </div>
          <div class="readonly-box">
            <p v-if="sourceText" class="readonly-box__text">{{ sourceText }}</p>
            <p v-else class="readonly-box__empty">
              还没有转写内容，请回到上一步录音或填写文字素材。
            </p>
          </div>
        </section>

        <!-- 初稿：可编辑 -->
        <section class="card">
          <div class="card__head">
            <h2 class="card__title">AI 初稿（可编辑）</h2>
            <span class="tag tag--neutral">
              {{ isStubEngine ? '本地模拟引擎' : 'AI 生成' }} · 可编辑
            </span>
          </div>

          <p v-if="isStubEngine" class="engine-hint">
            当前初稿由本机模拟引擎生成，用于打通流程；接入真实 AI 服务后内容会由模型重写。
          </p>

          <label class="visually-hidden" for="draft-text">初稿正文</label>
          <textarea
            id="draft-text"
            v-model="draft"
            class="field__control draft__area"
            rows="12"
            placeholder="点下方「生成初稿」，或直接在这里写下正文"
            @input="handleDraftInput"
          ></textarea>

          <div class="draft__meta">
            <span>{{ charCount }} 字</span>
            <span v-if="saveMessage">{{ saveMessage }}</span>
          </div>

          <div class="draft__actions">
            <button type="button" class="btn btn--secondary" :disabled="isGenerating" @click="generate">
              {{ isGenerating ? '正在生成…' : hasDraft ? '重新生成' : '生成初稿' }}
            </button>
            <button
              v-if="dirty"
              type="button"
              class="btn btn--text"
              @click="restoreSaved"
            >
              放弃修改
            </button>
          </div>

          <p v-if="isGenerating" class="loading-text" aria-live="polite">
            正在生成初稿，通常需要几秒钟，请稍候…
          </p>

          <div v-if="generateError" class="error-block">
            <span class="error-block__bar"></span>
            <div>
              <p class="error-block__title">生成没有完成</p>
              <p class="error-block__reason">{{ generateError }}</p>
              <button type="button" class="btn btn--secondary error-block__action" @click="generate">
                重试生成
              </button>
            </div>
          </div>
        </section>

        <div class="bottom-links">
          <button type="button" class="btn btn--text" @click="goHome">回到记录列表</button>
        </div>
      </template>
    </main>

    <footer v-if="!notFound" class="app-action-bar app-action-bar--split">
      <button type="button" class="btn btn--secondary" @click="openExport">导出文档</button>
      <button type="button" class="btn btn--primary" :disabled="isSaving" @click="save">
        {{ isSaving ? '正在保存…' : '保存初稿' }}
      </button>
    </footer>

    <ExportDialog
      :visible="exportVisible"
      :title="store.currentStory?.title ?? ''"
      :body="draft"
      :segment-count="store.segments.length"
      :duration-text="formatDuration(totalSeconds)"
      :busy="exporting"
      :error="exportError"
      @cancel="exportVisible = false"
      @confirm="confirmExport"
    />
  </div>
</template>

<style scoped>
.notice--warning {
  background: var(--color-warning-bg);
  color: var(--color-warning-text);
}

.notice__bar {
  background: var(--color-warning-graphic);
}

.notice__title {
  margin: 0;
  font-weight: 500;
}

.notice__text {
  margin: 4px 0 0;
  font-size: var(--text-label);
}

.card {
  margin-top: var(--space-3);
}

.card__head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--space-2);
  margin-bottom: var(--space-2);
}

.card__title {
  font-size: var(--text-subtitle);
  font-weight: 600;
}

.tag--neutral {
  background: var(--color-neutral-bg);
  color: var(--color-neutral-text);
}

.progress-card__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.progress {
  height: 10px;
  margin: var(--space-1) 0;
  border-radius: 999px;
  background: var(--color-bg-hover);
  overflow: hidden;
}

.progress__bar {
  height: 100%;
  border-radius: 999px;
  background: var(--color-primary);
  transition: width var(--motion-base) ease-out;
}

.progress-card__meta {
  margin: 0;
  color: var(--color-text-muted);
  font-size: var(--text-label);
}

.readonly-box {
  padding: var(--space-2);
  border-radius: var(--radius-sm);
  background: var(--color-bg-subtle);
  border: 1px solid var(--color-border);
}

.readonly-box__text {
  margin: 0;
  white-space: pre-wrap;
}

.readonly-box__empty {
  margin: 0;
  color: var(--color-text-muted);
}

.engine-hint {
  margin: 0 0 var(--space-2);
  color: var(--color-text-muted);
  font-size: var(--text-label);
}

.draft__area {
  height: auto;
  padding: var(--space-2);
  line-height: 1.8;
  resize: vertical;
}

.draft__meta {
  display: flex;
  justify-content: space-between;
  margin-top: var(--space-1);
  color: var(--color-text-muted);
  font-size: var(--text-label);
}

.draft__actions {
  display: flex;
  justify-content: flex-end;
  gap: var(--space-2);
  margin-top: var(--space-2);
}

.loading-text {
  margin: var(--space-2) 0 0;
  color: var(--color-text-muted);
}

.error-block__action {
  margin-top: var(--space-1);
}

.bottom-links {
  margin-top: var(--space-3);
  text-align: center;
}

.app-action-bar--split {
  gap: var(--space-2);
  padding: 0 var(--content-gutter);
}

.visually-hidden {
  position: absolute;
  width: 1px;
  height: 1px;
  overflow: hidden;
  clip: rect(0 0 0 0);
  white-space: nowrap;
}
</style>
