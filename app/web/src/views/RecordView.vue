<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import AppTopBar from '../components/AppTopBar.vue';
import StatusTag from '../components/StatusTag.vue';
import { useStoryStore } from '../stores/storyStore';
import type { Segment } from '../types/segment';
import { formatDuration } from '../utils/formatTime';
import { showToast } from '../utils/useToast';

/**
 * P3 录音页（技术方案 9.5）。
 *
 * 交互顺序按 PRD 2.2：开始录音 → 结束本段 → 试听 → 确认入库 → 自动转写。
 * 只有点了「确认这段录音」才写库，避免误触把半句话存成一段素材；
 * 音频先入库再上传转写，转写失败时音频仍在本地，可重转（接口文档 2.2 降级约定）。
 */
type RecorderState = 'idle' | 'recording' | 'paused' | 'stopped';

const route = useRoute();
const router = useRouter();
const store = useStoryStore();

const storyId = computed(() => String(route.params.storyId ?? ''));
const notFound = ref(false);
const state = ref<RecorderState>('idle');
const elapsedSeconds = ref(0);
const takeBlob = ref<Blob | null>(null);
const takeUrl = ref('');
const takeDuration = ref(0);
const isSaving = ref(false);
const errorText = ref('');
const showTextInput = ref(false);
const textDraft = ref('');

let recorder: MediaRecorder | null = null;
let stream: MediaStream | null = null;
let chunks: BlobPart[] = [];
let timerId: number | undefined;

const segmentUrls = new Map<string, string>();

const hasSegments = computed(() => store.segments.length > 0);
const transcribedCount = computed(
  () => store.segments.filter((segment) => segment.status === '已转写').length,
);
const totalSeconds = computed(() =>
  store.segments.reduce((sum, segment) => sum + segment.audio_duration, 0),
);

const recorderHint = computed(() => {
  if (state.value === 'recording') {
    return '正在录音，请用平时说话的速度慢慢讲';
  }
  if (state.value === 'paused') {
    return '已暂停，想好了再继续';
  }
  if (state.value === 'stopped') {
    return '这段录音已经录好，可以先试听，再决定是否确认';
  }
  return '点一下圆形按钮，就可以开始讲述';
});

const primaryLabel = computed(() => {
  if (state.value === 'stopped') {
    return isSaving.value ? '正在保存…' : '确认这段录音';
  }
  if (hasSegments.value) {
    return '下一步：生成初稿';
  }
  return '下一步：生成初稿';
});

onMounted(async () => {
  const ok = await store.loadStoryDetail(storyId.value);
  if (!ok) {
    notFound.value = true;
  }
});

onUnmounted(() => {
  cleanupRecorder();
  if (takeUrl.value) {
    URL.revokeObjectURL(takeUrl.value);
  }
  for (const url of segmentUrls.values()) {
    URL.revokeObjectURL(url);
  }
  segmentUrls.clear();
});

function cleanupRecorder(): void {
  if (timerId !== undefined) {
    window.clearInterval(timerId);
    timerId = undefined;
  }
  if (recorder && recorder.state !== 'inactive') {
    recorder.stop();
  }
  recorder = null;
  stream?.getTracks().forEach((track) => track.stop());
  stream = null;
}

/** 选择浏览器支持的录音容器：优先 webm/opus，Safari 走 mp4。 */
function pickMimeType(): string {
  const candidates = ['audio/webm;codecs=opus', 'audio/webm', 'audio/mp4'];
  for (const type of candidates) {
    if (typeof MediaRecorder !== 'undefined' && MediaRecorder.isTypeSupported(type)) {
      return type;
    }
  }
  return '';
}

async function startRecording(): Promise<void> {
  errorText.value = '';

  if (!navigator.mediaDevices?.getUserMedia) {
    errorText.value = '当前浏览器不支持录音，请改用 Chrome 或 Edge 打开本页面';
    return;
  }

  try {
    stream = await navigator.mediaDevices.getUserMedia({ audio: true });
  } catch {
    errorText.value = '没有获得麦克风权限。请在浏览器地址栏允许使用麦克风后重试';
    return;
  }

  const mimeType = pickMimeType();
  chunks = [];
  recorder = mimeType ? new MediaRecorder(stream, { mimeType }) : new MediaRecorder(stream);

  recorder.ondataavailable = (event) => {
    if (event.data.size > 0) {
      chunks.push(event.data);
    }
  };

  recorder.onstop = () => {
    stream?.getTracks().forEach((track) => track.stop());
    stream = null;

    const blob = new Blob(chunks, { type: recorder?.mimeType || 'audio/webm' });
    if (blob.size === 0) {
      errorText.value = '这段录音没有采集到声音，请检查麦克风后重试';
      state.value = 'idle';
      return;
    }

    if (takeUrl.value) {
      URL.revokeObjectURL(takeUrl.value);
    }
    takeBlob.value = blob;
    takeUrl.value = URL.createObjectURL(blob);
    takeDuration.value = elapsedSeconds.value;
    state.value = 'stopped';
  };

  recorder.start();
  state.value = 'recording';
  elapsedSeconds.value = 0;
  timerId = window.setInterval(() => {
    if (state.value === 'recording') {
      elapsedSeconds.value += 1;
    }
  }, 1000);
}

function pauseRecording(): void {
  if (recorder?.state === 'recording') {
    recorder.pause();
    state.value = 'paused';
  }
}

function resumeRecording(): void {
  if (recorder?.state === 'paused') {
    recorder.resume();
    state.value = 'recording';
  }
}

function stopRecording(): void {
  errorText.value = '';
  if (timerId !== undefined) {
    window.clearInterval(timerId);
    timerId = undefined;
  }
  if (recorder && recorder.state !== 'inactive') {
    recorder.stop();
  }
}

/** 重新录制：丢弃这一段试听内容，回到待录状态。 */
function discardTake(): void {
  if (takeUrl.value) {
    URL.revokeObjectURL(takeUrl.value);
  }
  takeBlob.value = null;
  takeUrl.value = '';
  takeDuration.value = 0;
  elapsedSeconds.value = 0;
  state.value = 'idle';
}

async function confirmRecording(): Promise<void> {
  if (!takeBlob.value || isSaving.value) {
    return;
  }

  isSaving.value = true;
  errorText.value = '';
  try {
    const segment = await store.addAudioSegment(takeBlob.value, takeDuration.value);
    if (segment.status === '转写失败') {
      errorText.value = segment.transcription_error ?? '转写失败，可以稍后重试';
    } else {
      showToast('这段录音已保存');
    }
    discardTake();
  } catch (error) {
    errorText.value = error instanceof Error ? error.message : '保存录音失败，请重试';
  } finally {
    isSaving.value = false;
  }
}

async function retryTranscribe(segmentId: string): Promise<void> {
  await store.transcribe(segmentId);
}

async function removeSegment(segment: Segment): Promise<void> {
  await store.removeSegment(segment.segment_id);
  const url = segmentUrls.get(segment.segment_id);
  if (url) {
    URL.revokeObjectURL(url);
    segmentUrls.delete(segment.segment_id);
  }
  showToast('片段已删除');
}

function segmentAudioUrl(segment: Segment): string {
  if (!segment.audio_blob) {
    return '';
  }
  const cached = segmentUrls.get(segment.segment_id);
  if (cached) {
    return cached;
  }
  const url = URL.createObjectURL(segment.audio_blob);
  segmentUrls.set(segment.segment_id, url);
  return url;
}

async function saveTextSegment(): Promise<void> {
  if (textDraft.value.trim().length === 0) {
    errorText.value = '请先写下要记录的内容';
    return;
  }

  errorText.value = '';
  await store.addTextSegment(textDraft.value);
  textDraft.value = '';
  showTextInput.value = false;
  showToast('文字素材已保存');
}

function goNext(): void {
  // 空素材拦截：不把按钮做成禁用态，而是给出可读的原因，避免老人不知道哪里出错
  if (!hasSegments.value) {
    errorText.value = '还没有可用的口述内容，请先录一段音或填写文字素材';
    return;
  }

  if (transcribedCount.value === 0) {
    errorText.value = '当前素材还没有完成转写，请先点片段上的「重新转写」，或直接填写文字素材';
    return;
  }

  void router.push({ name: 'draft', params: { storyId: storyId.value } });
}

function goBack(): void {
  void router.push({ name: 'home' });
}
</script>

<template>
  <div class="app-shell">
    <AppTopBar
      :title="store.currentStory?.title ?? '录入口述素材'"
      show-back
      @back="goBack"
    />

    <main class="app-main">
      <div v-if="notFound" class="error-block">
        <span class="error-block__bar"></span>
        <div>
          <p class="error-block__title">找不到这条记录</p>
          <p class="error-block__reason">它可能已被删除，请返回列表重新选择。</p>
        </div>
      </div>

      <template v-else>
        <!-- 概览：当前记录 / 已录片段 / 累计时长 -->
        <section class="overview">
          <div class="overview__item">
            <span class="overview__label">当前记录</span>
            <span class="overview__value">{{ store.currentStory?.title }}</span>
          </div>
          <div class="overview__item">
            <span class="overview__label">已录片段</span>
            <span class="overview__value">{{ store.segments.length }} 段</span>
          </div>
          <div class="overview__item">
            <span class="overview__label">累计时长</span>
            <span class="overview__value">{{ formatDuration(totalSeconds) }}</span>
          </div>
        </section>

        <!-- 已录入的片段 -->
        <section v-if="hasSegments" class="section">
          <h2 class="section__title">已录入的素材</h2>
          <ul class="segment-list">
            <li v-for="segment in store.segments" :key="segment.segment_id" class="segment">
              <div class="segment__head">
                <span class="segment__name">
                  {{ segment.kind === 'text' ? '文字素材' : `片段 ${segment.seq}` }}
                </span>
                <span v-if="segment.kind === 'audio'" class="segment__duration">
                  {{ formatDuration(segment.audio_duration) }}
                </span>
                <StatusTag :status="segment.status" />
              </div>

              <audio
                v-if="segment.kind === 'audio'"
                class="segment__audio"
                :src="segmentAudioUrl(segment)"
                controls
                preload="none"
              ></audio>

              <p v-if="segment.transcribed_text" class="segment__text">
                {{ segment.transcribed_text }}
              </p>
              <p v-else-if="segment.status === '转写中'" class="segment__text segment__text--muted">
                正在转写，请稍候…
              </p>

              <p v-if="segment.transcription_error" class="segment__error">
                {{ segment.transcription_error }}
              </p>

              <div class="segment__actions">
                <button
                  v-if="segment.kind === 'audio' && segment.status !== '转写中'"
                  type="button"
                  class="btn btn--text"
                  @click="retryTranscribe(segment.segment_id)"
                >
                  {{ segment.status === '转写失败' ? '重试转写' : '重新转写' }}
                </button>
                <button type="button" class="btn btn--text btn--text-danger" @click="removeSegment(segment)">
                  删除
                </button>
              </div>
            </li>
          </ul>
        </section>

        <!-- 录音区 -->
        <section class="recorder">
          <button
            v-if="state === 'idle'"
            type="button"
            class="recorder__button"
            aria-label="开始录音"
            @click="startRecording"
          >
            <span class="recorder__dot"></span>
          </button>
          <button
            v-else-if="state === 'recording' || state === 'paused'"
            type="button"
            class="recorder__button recorder__button--stop"
            aria-label="结束本段录音"
            @click="stopRecording"
          >
            <span class="recorder__square"></span>
          </button>
          <div v-else class="recorder__done">录音已完成</div>

          <p class="recorder__timer" aria-live="polite">
            {{ formatDuration(state === 'stopped' ? takeDuration : elapsedSeconds) }}
          </p>
          <p class="recorder__hint">{{ recorderHint }}</p>

          <div class="recorder__actions">
            <button
              v-if="state === 'recording'"
              type="button"
              class="btn btn--secondary"
              @click="pauseRecording"
            >
              暂停
            </button>
            <button
              v-if="state === 'paused'"
              type="button"
              class="btn btn--secondary"
              @click="resumeRecording"
            >
              继续
            </button>
            <button
              v-if="state === 'recording' || state === 'paused'"
              type="button"
              class="btn btn--secondary"
              @click="stopRecording"
            >
              结束本段
            </button>
            <button
              v-if="state === 'stopped'"
              type="button"
              class="btn btn--secondary"
              @click="discardTake"
            >
              重新录制
            </button>
          </div>

          <audio v-if="state === 'stopped'" class="recorder__playback" :src="takeUrl" controls></audio>
        </section>

        <!-- 文字素材：给不方便录音的用户一个等价入口 -->
        <section class="section">
          <h2 class="section__title">不方便录音？可以直接打字</h2>
          <button
            v-if="!showTextInput"
            type="button"
            class="btn btn--secondary"
            @click="showTextInput = true"
          >
            填写文字素材
          </button>
          <div v-else class="text-entry">
            <label class="field__label" for="text-material">要记下的内容</label>
            <textarea
              id="text-material"
              v-model="textDraft"
              class="field__control text-entry__area"
              rows="5"
              placeholder="例如：我小时候住在南昌老城区，家里有天井……"
            ></textarea>
            <div class="text-entry__actions">
              <button type="button" class="btn btn--secondary" @click="showTextInput = false">
                取消
              </button>
              <button type="button" class="btn btn--primary" @click="saveTextSegment">
                保存文字素材
              </button>
            </div>
          </div>
        </section>

        <div v-if="errorText" class="error-block">
          <span class="error-block__bar"></span>
          <div>
            <p class="error-block__title">这次没有完成</p>
            <p class="error-block__reason">{{ errorText }}</p>
          </div>
        </div>

        <div v-if="store.segmentsError" class="error-block">
          <span class="error-block__bar"></span>
          <div>
            <p class="error-block__title">读取失败</p>
            <p class="error-block__reason">{{ store.segmentsError }}</p>
          </div>
        </div>
      </template>
    </main>

    <footer v-if="!notFound" class="app-action-bar">
      <button
        v-if="state === 'stopped'"
        type="button"
        class="btn btn--primary"
        :disabled="isSaving"
        @click="confirmRecording"
      >
        {{ primaryLabel }}
      </button>
      <button v-else type="button" class="btn btn--primary" @click="goNext">
        下一步：生成初稿
      </button>
    </footer>
  </div>
</template>

<style scoped>
.overview {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-3);
  padding: var(--space-2) var(--space-3);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  background: var(--color-bg);
}

.overview__item {
  display: flex;
  align-items: baseline;
  gap: var(--space-1);
}

.overview__label {
  color: var(--color-text-muted);
  font-size: var(--text-label);
}

.overview__value {
  font-size: var(--text-body);
  font-weight: 500;
}

.section {
  margin-top: var(--space-4);
}

.section__title {
  margin-bottom: var(--space-2);
  font-size: var(--text-subtitle);
  font-weight: 600;
}

.segment-list {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
  margin: 0;
  padding: 0;
  list-style: none;
}

.segment {
  padding: var(--space-2) var(--space-3);
  border-radius: var(--radius-md);
  background: var(--color-bg);
  box-shadow: var(--shadow-card);
}

.segment__head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--space-2);
}

.segment__name {
  font-size: var(--text-body);
  font-weight: 500;
}

.segment__duration {
  color: var(--color-text-muted);
  font-size: var(--text-label);
  font-variant-numeric: tabular-nums;
}

.segment__audio,
.recorder__playback {
  width: 100%;
  margin-top: var(--space-1);
}

.segment__text {
  margin: var(--space-2) 0 0;
  padding: var(--space-2);
  border-radius: var(--radius-sm);
  background: var(--color-bg-subtle);
}

.segment__text--muted {
  color: var(--color-text-muted);
}

.segment__error {
  margin: var(--space-1) 0 0;
  color: var(--color-danger-text);
  font-size: var(--text-label);
}

.segment__actions {
  display: flex;
  justify-content: flex-end;
  gap: var(--space-2);
  margin-top: var(--space-1);
}

.recorder {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: var(--space-2);
  margin-top: var(--space-4);
  padding: var(--space-4) var(--space-3);
  border-radius: var(--radius-lg);
  background: var(--color-bg);
  box-shadow: var(--shadow-card);
}

/* 录音圆钮 96px（UI 规范 3.2 / A6） */
.recorder__button {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 96px;
  height: 96px;
  border: none;
  border-radius: 50%;
  background: var(--color-primary);
  cursor: pointer;
}

.recorder__button:hover {
  background: var(--color-primary-hover);
}

.recorder__dot {
  width: 40px;
  height: 40px;
  border-radius: 50%;
  background: var(--color-bg);
}

.recorder__square {
  width: 36px;
  height: 36px;
  border-radius: 6px;
  background: var(--color-bg);
}

.recorder__button--stop {
  background: var(--color-danger);
}

.recorder__done {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 96px;
  color: var(--color-text-muted);
}

.recorder__timer {
  margin: 0;
  font-size: var(--text-display);
  font-weight: 500;
  font-variant-numeric: tabular-nums;
}

.recorder__hint {
  margin: 0;
  color: var(--color-text-muted);
  text-align: center;
}

.recorder__actions {
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  gap: var(--space-2);
}

.text-entry {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
  max-width: var(--form-span-width);
}

.text-entry__area {
  height: auto;
  padding: var(--space-2);
  line-height: 1.8;
  resize: vertical;
}

.text-entry__actions {
  display: flex;
  justify-content: flex-end;
  gap: var(--space-2);
}
</style>
