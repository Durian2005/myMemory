<script setup lang="ts">
import { computed } from 'vue';
import type { CSSProperties } from 'vue';

/**
 * 状态标签（UI 设计规范 3.5）：胶囊形，内边距 4px 12px，字号 16px。
 * 统一「浅底 + 深字」（UI 规范 2.4），不使用深底白字，避免小字在深底上对比度不足。
 * 同时服务于记录状态与片段转写状态，两者语义一致：完成=暖橄榄、进行中=中性、失败=陶土红。
 */
const props = defineProps<{ status: string }>();

type TagStyle = CSSProperties;

const SUCCESS: TagStyle = {
  background: 'var(--color-success-bg)',
  color: 'var(--color-success-text)',
};

const WARNING: TagStyle = {
  background: 'var(--color-warning-bg)',
  color: 'var(--color-warning-text)',
};

const NEUTRAL: TagStyle = {
  background: 'var(--color-neutral-bg)',
  color: 'var(--color-neutral-text)',
};

const DANGER: TagStyle = {
  background: 'var(--color-danger-bg)',
  color: 'var(--color-danger-text)',
};

const STYLE_MAP: Record<string, TagStyle> = {
  已完成: SUCCESS,
  已转写: SUCCESS,
  待编辑: WARNING,
  待转写: WARNING,
  转写中: NEUTRAL,
  未录入: NEUTRAL,
  转写失败: DANGER,
};

const style = computed<CSSProperties>(() => STYLE_MAP[props.status] ?? NEUTRAL);
</script>

<template>
  <span class="tag" :style="style">{{ status }}</span>
</template>
