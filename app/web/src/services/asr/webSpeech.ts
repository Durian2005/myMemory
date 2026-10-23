/**
 * 浏览器语音识别（Web Speech API）封装。
 *
 * ⚠️ 必须先说清一件事：这个能力**不是本地识别**。
 * Edge 会把音频发送到微软的在线服务、Chrome 发送到 Google 的在线服务，
 * 识别完再把文字返回给页面。所以它带来两个硬约束：
 *   1. 必须联网；国内 Chrome 大概率失败（无法访问 Google 服务），Edge 通常可用。
 *   2. 它只接受**实时麦克风输入**，不能把已录好的 webm 文件交给它转写。
 *      → 因此「转写失败后重试」在本引擎下退化为「重新录制」，这一点在录音页有明确提示。
 *
 * 设计要点：识别与录音**并行**。录音仍由 MediaRecorder 负责落盘，
 * 识别只提供文字；两者互不依赖，识别挂了录音仍然有效，此时回落到后端备用引擎。
 *
 * 参考：https://developer.mozilla.org/docs/Web/API/Web_Speech_API
 */

/* ---------------- 类型声明（TS 的 lib.dom 未内置 SpeechRecognition） ---------------- */

interface SpeechAlternativeLike {
  readonly transcript: string;
  readonly confidence: number;
}

interface SpeechResultLike {
  readonly isFinal: boolean;
  readonly length: number;
  readonly [index: number]: SpeechAlternativeLike;
}

interface SpeechResultListLike {
  readonly length: number;
  readonly [index: number]: SpeechResultLike;
}

interface SpeechResultEventLike extends Event {
  readonly resultIndex: number;
  readonly results: SpeechResultListLike;
}

interface SpeechErrorEventLike extends Event {
  readonly error: string;
  readonly message: string;
}

interface SpeechRecognitionLike extends EventTarget {
  lang: string;
  continuous: boolean;
  interimResults: boolean;
  maxAlternatives: number;
  start(): void;
  stop(): void;
  abort(): void;
  onresult: ((event: SpeechResultEventLike) => void) | null;
  onerror: ((event: SpeechErrorEventLike) => void) | null;
  onend: (() => void) | null;
  onstart: (() => void) | null;
}

type SpeechRecognitionCtor = new () => SpeechRecognitionLike;

interface SpeechWindow {
  SpeechRecognition?: SpeechRecognitionCtor;
  webkitSpeechRecognition?: SpeechRecognitionCtor;
}

/* ---------------- 对外类型 ---------------- */

/** 识别失败的可读归类。`network` 是国内 Chrome 最常见的失败原因。 */
export type WebSpeechErrorCode =
  | 'not-allowed'
  | 'service-not-allowed'
  | 'network'
  | 'audio-capture'
  | 'no-speech'
  | 'aborted'
  | 'unknown';

export interface WebSpeechHandlers {
  /** 识别过程中的临时结果（会随说话不断变化），用于实时字幕。 */
  onInterim?: (text: string) => void;
  /** 每次拿到一段确定结果时触发，text 为累积后的完整文本。 */
  onCommit?: (text: string) => void;
  /** 识别出错。出错后录音仍可继续，只是没有文字。 */
  onError?: (code: WebSpeechErrorCode, message: string) => void;
}

/** 错误码到老年人能看懂的中文说明。 */
const ERROR_MESSAGES: Record<WebSpeechErrorCode, string> = {
  'not-allowed': '浏览器没有获得麦克风权限，语音识别无法启动',
  'service-not-allowed': '当前浏览器不允许使用在线语音识别（可能不是安全地址）',
  network: '浏览器语音识别服务连不上，可能是网络问题',
  'audio-capture': '没有找到可用的麦克风',
  'no-speech': '这段时间没有检测到说话声',
  aborted: '语音识别被中断',
  unknown: '语音识别出现未知问题',
};

export function describeWebSpeechError(code: WebSpeechErrorCode): string {
  return ERROR_MESSAGES[code] ?? ERROR_MESSAGES.unknown;
}

/**
 * 当前浏览器是否具备语音识别能力。
 *
 * 注意：返回 true 只表示「API 存在」，不代表能真正识别 ——
 * 国内 Chrome 的 API 存在但会以 `network` 错误失败，因此调用方必须准备回落方案。
 */
export function isWebSpeechSupported(): boolean {
  if (typeof window === 'undefined') {
    return false;
  }
  const scope = window as unknown as SpeechWindow;
  return Boolean(scope.SpeechRecognition ?? scope.webkitSpeechRecognition);
}

/** 粗略判断浏览器名字，仅用于界面提示，不影响功能。 */
export function detectBrowserHint(): string {
  if (typeof navigator === 'undefined') {
    return '';
  }
  const ua = navigator.userAgent;
  if (/Edg\//.test(ua)) {
    return 'Edge';
  }
  if (/Chrome\//.test(ua)) {
    return 'Chrome';
  }
  if (/Firefox\//.test(ua)) {
    return 'Firefox';
  }
  return '';
}

/* ---------------- 会话 ---------------- */

/** 浏览器自动断开后的重启上限，防止异常情况下无限重连。 */
const MAX_RESTART = 20;

/**
 * 一次录音期间的识别会话。生命周期与录音一致：
 * `start()` 开始，`stop()` 结束并给出最终文本。
 */
export class WebSpeechSession {
  private recognition: SpeechRecognitionLike | null = null;
  private finalText = '';
  private interimText = '';
  private stopping = false;
  private restarts = 0;
  private failed = false;
  private readonly handlers: WebSpeechHandlers;
  private readonly lang: string;

  constructor(handlers: WebSpeechHandlers = {}, lang = 'zh-CN') {
    this.handlers = handlers;
    this.lang = lang;
  }

  /** 累计的确定文本（不含临时结果）。 */
  get committedText(): string {
    return this.finalText.trim();
  }

  /** 最终文本：优先确定结果；若一段都没确定，退而使用最后一次临时结果。 */
  get text(): string {
    const committed = this.finalText.trim();
    return committed.length > 0 ? committed : this.interimText.trim();
  }

  /** 是否发生过不可恢复的错误（调用方据此提示「已回落到备用引擎」）。 */
  get hasFailed(): boolean {
    return this.failed;
  }

  start(): void {
    const scope = window as unknown as SpeechWindow;
    const Ctor = scope.SpeechRecognition ?? scope.webkitSpeechRecognition;
    if (!Ctor) {
      this.failed = true;
      this.handlers.onError?.('service-not-allowed', ERROR_MESSAGES['service-not-allowed']);
      return;
    }

    this.stopping = false;
    this.failed = false;
    this.finalText = '';
    this.interimText = '';
    this.createAndStart(Ctor);
  }

  private createAndStart(Ctor: SpeechRecognitionCtor): void {
    const recognition = new Ctor();
    recognition.lang = this.lang;
    // continuous 让识别在停顿后继续；部分浏览器静音一段时间仍会自行结束，由 onend 重启兜住。
    recognition.continuous = true;
    recognition.interimResults = true;
    recognition.maxAlternatives = 1;

    recognition.onresult = (event: SpeechResultEventLike) => {
      let interim = '';
      for (let i = event.resultIndex; i < event.results.length; i += 1) {
        const result = event.results[i];
        const transcript = result[0]?.transcript ?? '';
        if (result.isFinal) {
          this.finalText += transcript;
        } else {
          interim += transcript;
        }
      }
      if (interim.length > 0) {
        this.interimText = interim;
        this.handlers.onInterim?.(`${this.finalText}${interim}`.trim());
      }
      if (this.finalText.length > 0) {
        this.handlers.onCommit?.(this.finalText.trim());
      }
    };

    recognition.onerror = (event: SpeechErrorEventLike) => {
      // no-speech / aborted 属于可继续的情况，不当作致命错误。
      if (event.error === 'no-speech') {
        return;
      }
      if (event.error === 'aborted' && this.stopping) {
        return;
      }
      const code = (event.error as WebSpeechErrorCode) ?? 'unknown';
      // network / service-not-allowed 视为不可恢复：不再自动重启，交给调用方回落。
      if (code === 'network' || code === 'service-not-allowed' || code === 'not-allowed') {
        this.failed = true;
      }
      this.handlers.onError?.(code, event.message || describeWebSpeechError(code));
    };

    recognition.onend = () => {
      // 主动停止：正常收尾。
      if (this.stopping) {
        return;
      }
      // 被动结束（静音超时或服务侧断开）：仍在录音就重启，保证长句连续。
      if (!this.failed && this.restarts < MAX_RESTART) {
        this.restarts += 1;
        try {
          recognition.start();
        } catch {
          this.failed = true;
        }
        return;
      }
      if (this.failed) {
        return;
      }
    };

    this.recognition = recognition;
    try {
      recognition.start();
    } catch {
      // 连续调用 start() 会抛 InvalidStateError，此时沿用已有实例即可。
    }
  }

  /** 停止识别。返回 Promise 是为了等浏览器把最后一段结果交出来。 */
  stop(): Promise<void> {
    return new Promise<void>((resolve) => {
      const recognition = this.recognition;
      if (!recognition) {
        resolve();
        return;
      }
      this.stopping = true;

      const finish = (): void => {
        this.recognition = null;
        resolve();
      };

      // onend 正常会在 stop() 后触发；这里加一个兜底，避免个别浏览器不回调。
      const timer = window.setTimeout(finish, 1500);
      const originalOnEnd = recognition.onend;
      recognition.onend = () => {
        originalOnEnd?.();
        window.clearTimeout(timer);
        finish();
      };

      try {
        recognition.stop();
      } catch {
        window.clearTimeout(timer);
        finish();
      }
    });
  }

  /** 立即中断，不等待结果（用于离页清理）。 */
  abort(): void {
    this.stopping = true;
    try {
      this.recognition?.abort();
    } catch {
      /* 已结束则忽略 */
    }
    this.recognition = null;
  }
}
