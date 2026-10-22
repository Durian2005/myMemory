<script setup lang="ts">
import type { Story } from '../types/story';
import { formatDateTime, formatSegmentCount } from '../utils/formatTime';
import StatusTag from './StatusTag.vue';

/**
 * 记录卡片（UI 设计规范 3.4）：自上而下固定为「标题 → 更新时间与录音段数 → 状态标签 → 次级操作行」。
 * 点击卡片主体与点击「继续编辑」等价（UI 规范 5.4）。
 */
const props = defineProps<{ story: Story }>();

defineEmits<{ open: [storyId: string]; remove: [storyId: string] }>();
</script>

<template>
  <article class="story-card" @click="$emit('open', props.story.story_id)">
    <h2 class="story-card__title">{{ props.story.title }}</h2>

    <p class="story-card__meta">
      {{ formatDateTime(props.story.updated_at) }} · {{ formatSegmentCount(props.story.segment_count) }}
    </p>

    <div class="story-card__status">
      <StatusTag :status="props.story.status" />
    </div>

    <div class="story-card__actions" @click.stop>
      <button type="button" class="btn btn--text" @click="$emit('open', props.story.story_id)">
        查看
      </button>
      <button type="button" class="btn btn--text btn--text-danger" @click="$emit('remove', props.story.story_id)">
        删除
      </button>
      <button type="button" class="btn btn--secondary" @click="$emit('open', props.story.story_id)">
        继续编辑
      </button>
    </div>
  </article>
</template>

<style scoped>
.story-card {
  background: var(--color-bg);
  border-radius: var(--radius-md);
  box-shadow: var(--shadow-card);
  padding: var(--space-3);
  cursor: pointer;
  transition: background-color var(--motion-base) ease-out;
}

.story-card:hover {
  background: var(--color-bg-hover);
}

.story-card__title {
  font-size: var(--text-title);
  font-weight: 700;
}

.story-card__meta {
  margin: var(--space-1) 0;
  color: var(--color-text-muted);
  /* 字号下限 16px（UI 规范 4.2）：旧版误用 14px，已按规范改为 --text-caption */
  font-size: var(--text-caption);
}

.story-card__status {
  margin-bottom: var(--space-2);
}

.story-card__actions {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: var(--space-2);
}
</style>
