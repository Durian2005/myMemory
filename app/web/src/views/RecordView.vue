<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import AppTopBar from '../components/AppTopBar.vue';
import StatusTag from '../components/StatusTag.vue';
import { useStoryStore } from '../stores/storyStore';
import type { Segment } from '../types/segment';
import {
  WebSpeechSession,
  describeWebSpeechError,
  detectBrowserHint,
  isWebSpeechSupported,
  type WebSpeechErrorCode,
} from '../services/asr/webSpeech';
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

/* ---- 浏览器语音识别（Web Speech API）相关状态 ---- */
/** 当前浏览器是否暴露了识别 API。注意 true 不等于能识别，国内 Chrome 会以 network 失败。 */
const speechSupported = ref(false);
/** 用户浏览器的粗略名字，仅用于提示文案。 */
const browserHint = ref('');
/** 识别过程中的实时字幕（含临时结果）。 */
const liveText = ref('');
/** 停止录音后拿到的识别文本，确认入库时随音频一起保存。 */
const takeTranscript = ref('');
/** 识别失败的可读说明；有值时说明这段录音将回落到后端备用引擎。 */
const speechNotice = ref('');

let recorder: MediaRecorder | null = null;
let stream: MediaStream | null = null;
let chunks: BlobPart[] = [];
let timerId: number | undefined;
let speechSession: WebSpeechSession | null = null;

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
  speechSupported.value = isWebSpeechSupported();
  browserHint.value = detectBrowserHint();

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
  speechSession?.abort();
  speechSession = null;
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
  takeTranscript.value = '';
  liveText.value = '';
  speechNotice.value = '';

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

    // 识别一句话都没拿到时，如实告知会回落到备用引擎，而不是让用户以为识别成功了
    if (!takeTranscript.value && !speechNotice.value) {
      speechNotice.value = speechSupported.value
        ? '这次没有识别出文字，可能是说得太快或环境太吵。确认后会改用备用引擎转写'
        : '当前浏览器不支持语音识别，确认后会用备用引擎转写';
    }

    state.value = 'stopped';
  };

  recorder.start();
  state.value = 'recording';
  elapsedSeconds.value = 0;
  startSpeechRecognition();
  timerId = window.setInterval(() => {
    if (state.value === 'recording') {
      elapsedSeconds.value += 1;
    }
  }, 1000);
}

/**
 * 启动浏览器语音识别。与录音并行，识别失败不影响录音本身。
 *
 * 之所以要在开始录音时一起启动，是因为 Web Speech 只吃实时麦克风流，
 * 无法把已经录好的 webm 文件交给它重新识别。
 */
function startSpeechRecognition(): void {
  liveText.value = '';
  speechNotice.value = '';

  if (!speechSupported.value) {
    return;
  }

  speechSession = new WebSpeechSession({
    onInterim: (text) => {
      liveText.value = `${takeTranscript.value}${text}`;
    },
    onCommit: (text) => {
      liveText.value = `${takeTranscript.value}${text}`;
    },
    onError: (code, message) => {
      handleSpeechError(code, message);
    },
  });
  speechSession.start();
}

/**
 * 收尾一次识别会话，把拿到的文本并入 takeTranscript。
 * 暂停、结束录音都会调用它；因为 Web Speech 没有 pause，
 * 只能结束当前会话、恢复时重开一个新会话，所以文本需要自己累积。
 */
async function flushSpeech(): Promise<void> {
  if (!speechSession) {
    return;
  }
  const session = speechSession;
  speechSession = null;
  await session.stop();
  const text = session.text;
  if (text) {
    takeTranscript.value = `${takeTranscript.value}${text}`;
  }
  liveText.value = takeTranscript.value;
}

function handleSpeechError(code: WebSpeechErrorCode, message: string): void {
  // no-speech 是「这一段没说话」，属正常情况，不打扰用户
  if (code === 'no-speech') {
    return;
  }
  speechNotice.value = `${message || describeWebSpeechError(code)}。这段录音仍会保存，确认后改用备用引擎转写`;
}

function pauseRecording(): void {
  if (recorder?.state === 'recording') {
    recorder.pause();
    state.value = 'paused';
    void flushSpeech();
  }
}

function resumeRecording(): void {
  if (recorder?.state === 'paused') {
    recorder.resume();
    state.value = 'recording';
    startSpeechRecognition();
  }
}

async function stopRecording(): Promise<void> {
  errorText.value = '';
  if (timerId !== undefined) {
    window.clearInterval(timerId);
    timerId = undefined;
  }
  // 先收识别、再停录音：识别需要一点时间把最后一句交出来
  await flushSpeech();
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
  takeTranscript.value = '';
  liveText.value = '';
  speechNotice.value = '';
  state.value = 'idle';
}

async function confirmRecording(): Promise<void> {
  if (!takeBlob.value || isSaving.value) {
    return;
  }

  isSaving.value = true;
  errorText.value = '';
  const transcript = takeTranscript.value;
  const usedFallback = transcript.trim().length === 0;
  try {
    const segment = await store.addAudioSegment(takeBlob.value, takeDuration.value, transcript);
    if (segment.status === '转写失败') {
      errorText.value = segment.transcription_error ?? '转写失败，可以稍后重试';
    } else if (usedFallback) {
      showToast('这段录音已保存，文字由备用引擎生成');
    } else {
      showToast('这段录音和识别文字都已保存');
    }
    discardTake();
  } catch (error) {
    errorText.value = error instanceof Error ? error.message : '保存录音失败，请重试';
  } finally {
    isSaving.value = false;
  }
}

async function retryTranscribe(segment: Segment): Promise<void> {
  // 浏览器识别的结果无法对已录好的音频重跑，也不该用备用引擎覆盖真实识别文本
  if (segment.transcription_engine === 'browser-webspeech') {
    errorText.value =
      '这段文字是录音时由浏览器实时识别的，无法对已经录好的音频重新识别。如需纠正，请重新录制这一段。';
    return;
  }
  await store.transcribe(segment.segment_id);
}

/** 片段上那个按钮该叫什么：浏览器识别的只能重录，备用引擎的可以重试。 */
function segmentTranscribeLabel(segment: Segment): string {
  if (segment.transcription_engine === 'browser-webspeech') {
    return '重新录制';
  }
  return segment.status === '转写失败' ? '重试转写' : '重新转写';
}

/** 如实标注这段文字的来源，避免用户把备用引擎的占位内容当成真实识别。 */
function segmentEngineLabel(segment: Segment): string {
  if (segment.transcription_engine === 'browser-webspeech') {
    return '文字由浏览器在录音时实时识别';
  }
  if (segment.transcription_engine === 'server-stub') {
    return '文字由备用引擎生成，仅供占位';
  }
  return '';
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
    errorText.value = '当前素材还没有可用的文字，请重新录制这一段，或直接填写文字素材';
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

              <p v-if="segmentEngineLabel(segment)" class="segment__engine">
                {{ segmentEngineLabel(segment) }}
              </p>

              <p v-if="segment.transcription_error" class="segment__error">
                {{ segment.transcription_error }}
              </p>

              <div class="segment__actions">
                <button
                  v-if="segment.kind === 'audio' && segment.status !== '转写中'"
                  type="button"
                  class="btn btn--text"
                  @click="retryTranscribe(segment)"
                >
                  {{ segmentTranscribeLabel(segment) }}
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

          <!-- 实时字幕：让用户看得见「确实在识别」 -->
          <p v-if="state === 'recording' || state === 'paused'" class="recorder__live" aria-live="polite">
            {{ liveText || '正在听…请开始说话' }}
          </p>

          <!-- 停止后回显识别结果，确认前可以先核对 -->
          <div v-if="state === 'stopped'" class="recorder__result">
            <p v-if="takeTranscript" class="recorder__result-text">{{ takeTranscript }}</p>
            <p v-if="speechNotice" class="recorder__warn">{{ speechNotice }}</p>
          </div>

          <p class="recorder__engine">
            <template v-if="speechSupported">
              语音识别：本浏览器内置能力<template v-if="browserHint">（{{ browserHint }}）</template>，需要联网
            </template>
            <template v-else>
              语音识别：当前浏览器不支持，录音会改用备用引擎
            </template>
          </p>
          <p class="recorder__privacy">
            录音和文字只存在这台电脑上。语音识别由浏览器提供的在线服务完成，说话内容会发送给浏览器的服务商。
          </p>

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

/* 片段文字来源标注：字号不低于 14px，避免信息被当装饰忽略 */
.segment__engine {
  margin: var(--space-1) 0 0;
  color: var(--color-text-muted);
  font-size: var(--text-label);
}

/* 录音中的实时字幕 */
.recorder__live {
  min-height: 2.4em;
  margin: 0;
  padding: var(--space-2) var(--space-3);
  width: 100%;
  border-radius: var(--radius-sm);
  background: var(--color-bg-subtle);
  color: var(--color-text);
  line-height: 1.7;
  text-align: left;
  word-break: break-word;
}

.recorder__result {
  width: 100%;
}

.recorder__result-text {
  margin: 0;
  padding: var(--space-2) var(--space-3);
  border-radius: var(--radius-sm);
  background: var(--color-bg-subtle);
  line-height: 1.7;
  word-break: break-word;
}

.recorder__warn {
  margin: var(--space-1) 0 0;
  color: var(--color-warning-text, #8A6420);
  font-size: var(--text-label);
  text-align: left;
}

/* 引擎与隐私说明：常驻在录音区，避免用户误以为音频只留在本机 */
.recorder__engine,
.recorder__privacy {
  margin: 0;
  max-width: 34em;
  color: var(--color-text-muted);
  font-size: var(--text-label);
  text-align: center;
  line-height: 1.6;
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
