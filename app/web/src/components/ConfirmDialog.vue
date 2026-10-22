<script setup lang="ts">
/**
 * 确认弹窗（UI 设计规范 3.7 / 5.5）。
 * 结构固定为：标题 22px/700 → 说明文字 16px → 按钮区（右下对齐，主操作在最右）。
 */
const props = withDefaults(
  defineProps<{
    visible: boolean;
    title: string;
    description: string;
    confirmText?: string;
    cancelText?: string;
    danger?: boolean;
  }>(),
  {
    confirmText: '确认',
    cancelText: '取消',
    danger: false,
  },
);

const emit = defineEmits<{ confirm: []; cancel: [] }>();

function handleMaskClick(): void {
  emit('cancel');
}

void props;
</script>

<template>
  <div v-if="props.visible" class="mask" @click="handleMaskClick">
    <div class="dialog" role="dialog" aria-modal="true" @click.stop>
      <h2 class="dialog__title">{{ props.title }}</h2>
      <p class="dialog__description">{{ props.description }}</p>

      <div class="dialog__actions">
        <button type="button" class="btn btn--secondary" @click="emit('cancel')">
          {{ props.cancelText }}
        </button>
        <button
          type="button"
          class="btn"
          :class="props.danger ? 'btn--danger' : 'btn--primary'"
          @click="emit('confirm')"
        >
          {{ props.confirmText }}
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
  background: rgba(61, 51, 44, 0.34);
}

.dialog {
  width: 100%;
  max-width: 632px;
  padding: var(--space-3);
  border-radius: var(--radius-lg);
  background: var(--color-bg);
  box-shadow: var(--shadow-card);
}

.dialog__title {
  font-size: 22px;
  font-weight: 700;
}

.dialog__description {
  margin: var(--space-2) 0 var(--space-3);
  color: var(--color-text-muted);
  font-size: var(--text-body);
}

.dialog__actions {
  display: flex;
  justify-content: flex-end;
  gap: var(--space-2);
}
</style>
