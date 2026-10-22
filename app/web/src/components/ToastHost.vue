<script setup lang="ts">
import { useToastList } from '../utils/useToast';

/** Toast 容器（UI 设计规范 3.6）：底部居中、底色浅驼、2.5 秒自动消失。 */
const items = useToastList();
</script>

<template>
  <div class="toast-host" aria-live="polite">
    <p v-for="item in items" :key="item.id" class="toast">{{ item.text }}</p>
  </div>
</template>

<style scoped>
/* 位置：底部动作区上方。旧版放在顶部会遮挡顶部栏标题（UI 规范 3.6 要求底部） */
.toast-host {
  position: fixed;
  bottom: calc(var(--actionbar-height) + var(--space-2));
  left: 50%;
  z-index: 200;
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
  transform: translateX(-50%);
  pointer-events: none;
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
