<script setup lang="ts">
/**
 * 空态（UI 设计规范 3.8）：一句话说明 + 一个主操作，不使用插画。
 * 主按钮由使用方通过默认插槽传入，保证「每页只有一个主按钮」。
 */
defineProps<{ text: string }>();
</script>

<template>
  <div class="empty">
    <p class="empty__text">{{ text }}</p>
    <div class="empty__action">
      <slot />
    </div>
  </div>
</template>

<style scoped>
.empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: var(--space-3);
  padding: var(--space-5) var(--space-3);
  text-align: center;
}

.empty__text {
  margin: 0;
  font-size: var(--text-body);
}
</style>
