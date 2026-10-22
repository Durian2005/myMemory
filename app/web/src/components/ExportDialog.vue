<script setup lang="ts">
import { computed } from 'vue';
import { countCharacters } from '../utils/formatTime';

/**
 * 导出确认弹窗（P6，UI 设计规范 3.7 / 5.5）。
 *
 * 导出前把「导什么、多少字、几个片段、什么格式、叫什么名」一次性摊开给用户确认，
 * 避免老人在不知道结果的情况下点按钮。弹窗内不提供任何修改入口，改内容要回到编辑页。
 */
const props = withDefaults(
  defineProps<{
    visible: boolean;
    title: string;
    body: string;
    segmentCount: number;
    durationText: string;
    busy?: boolean;
    error?: string;
  }>(),
  { busy: false, error: '' },
);

defineEmits<{ cancel: []; confirm: [] }>();

const charCount = computed(() => countCharacters(props.body));

/** 摘要取正文前 40 个字，够用户辨认是不是这一篇即可。 */
const summary = computed(() => {
  const plain = props.body.replace(/\s+/g, ' ').trim();
  return plain.length > 40 ? `${plain.slice(0, 40)}……` : plain;
});

const fileName = computed(() => `${props.title.trim() || '我的传记'}.docx`);
</script>

<template>
  <div v-if="visible" class="mask">
    <div class="dialog" role="dialog" aria-modal="true" aria-labelledby="export-dialog-title">
      <h2 id="export-dialog-title" class="dialog__title">导出文档</h2>

      <dl class="facts">
        <div class="facts__row">
          <dt>标题</dt>
          <dd>{{ title }}</dd>
        </div>
        <div class="facts__row">
          <dt>正文字数</dt>
          <dd>{{ charCount }} 字</dd>
        </div>
        <div class="facts__row">
          <dt>录音片段</dt>
          <dd>{{ segmentCount }} 段 · 累计 {{ durationText }}</dd>
        </div>
        <div class="facts__row">
          <dt>导出格式</dt>
          <dd>.docx（Word 文档）</dd>
        </div>
        <div class="facts__row">
          <dt>预计文件名</dt>
          <dd>{{ fileName }}</dd>
        </div>
      </dl>

      <p class="summary">正文摘要：{{ summary }}</p>

      <div v-if="error" class="error-block error-block--inline">
        <span class="error-block__bar"></span>
        <div>
          <p class="error-block__title">导出没有完成</p>
          <p class="error-block__reason">{{ error }}</p>
        </div>
      </div>

      <div class="dialog__actions">
        <button type="button" class="btn btn--secondary" :disabled="busy" @click="$emit('cancel')">
          取消
        </button>
        <button type="button" class="btn btn--primary" :disabled="busy" @click="$emit('confirm')">
          {{ busy ? '正在导出…' : '导出文档' }}
        </button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.mask {
  position: fixed;
  inset: 0;
  z-index: 100;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: var(--space-2);
  background: rgba(61, 51, 44, 0.34);
}

.dialog {
  width: 100%;
  max-width: var(--form-span-width);
  padding: var(--space-3);
  border-radius: var(--radius-lg);
  background: var(--color-bg);
  box-shadow: var(--shadow-card);
}

.dialog__title {
  font-size: 22px;
  font-weight: 700;
}

.facts {
  margin: var(--space-2) 0;
  padding: var(--space-2);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-sm);
  background: var(--color-bg-subtle);
}

.facts__row {
  display: flex;
  gap: var(--space-2);
  padding: 4px 0;
}

.facts__row + .facts__row {
  border-top: 1px dashed var(--color-border);
}

.facts__row dt {
  flex: 0 0 96px;
  color: var(--color-text-muted);
  font-size: var(--text-label);
}

.facts__row dd {
  margin: 0;
  font-size: var(--text-label);
}

.summary {
  margin: 0 0 var(--space-2);
  color: var(--color-text-muted);
  font-size: var(--text-label);
}

.error-block--inline {
  margin-top: 0;
  margin-bottom: var(--space-2);
}

.dialog__actions {
  display: flex;
  justify-content: flex-end;
  gap: var(--space-2);
}
</style>
