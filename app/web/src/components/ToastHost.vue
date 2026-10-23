<script setup lang="ts">
import { useToastList } from '../utils/useToast';

/** Toast 容器（UI 设计规范 3.6）：底色浅驼、2.5 秒自动消失。 */
const items = useToastList();
</script>

<template>
  <div class="toast-host" aria-live="polite">
    <p v-for="item in items" :key="item.id" class="toast">{{ item.text }}</p>
  </div>
</template>

<style scoped>
/*
 * 位置：底部动作区上方、靠右侧（UI 规范 3.6 要求底部）。
 * 演进过程：最初顶部居中 → 遮挡顶部栏标题；改到底部居中 → 遮挡录音区的计时数字与录音圆钮；
 * 试过右上角 → 压住概览卡片。最终取「底部 + 靠右」：中央的录音钮与计时不受影响，
 * 正文宽 960px 居中，右下的留白是最不打扰的位置。
 */
.toast-host {
  position: fixed;
  bottom: calc(var(--actionbar-height) + var(--space-2));
  right: var(--space-3);
  z-index: 200;
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: var(--space-1);
  max-width: min(360px, calc(100vw - var(--space-3) * 2));
  pointer-events: none;
}

/* 窄屏没有右侧留白，改为通栏居中；仍位于动作区上方，不压主按钮 */
@media (max-width: 720px) {
  .toast-host {
    right: var(--space-3);
    left: var(--space-3);
    align-items: center;
  }

  .toast {
    text-align: center;
  }
}

.toast {
  margin: 0;
  padding: 12px var(--space-3);
  border-radius: var(--radius-md);
  background: var(--color-bg-hover);
  color: var(--color-text);
  font-size: var(--text-label);
  box-shadow: var(--shadow-card);
}
</style>
