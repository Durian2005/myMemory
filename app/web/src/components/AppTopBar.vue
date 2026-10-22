<script setup lang="ts">
import { computed } from 'vue';
import { useServiceStore } from '../stores/serviceStore';

/**
 * 顶部栏（UI 设计规范 4.5）：高 64px、底色奶油白、底部 1px 分割线。
 * 左侧返回入口，中间当前页面标题，右侧服务状态标签。
 */
const props = withDefaults(defineProps<{ title: string; showBack?: boolean }>(), {
  showBack: false,
});

defineEmits<{ back: [] }>();

const serviceStore = useServiceStore();

const serviceLabel = computed(() => {
  if (serviceStore.mode === 'online') {
    return '服务正常';
  }
  if (serviceStore.mode === 'local') {
    return '本地模式';
  }
  return '正在检查服务';
});

const serviceHint = computed(() =>
  serviceStore.mode === 'local'
    ? '当前为本地模式，录音、编辑与导出可正常使用'
    : '',
);

void props;
</script>

<template>
  <header class="topbar">
    <div class="topbar__inner">
      <div class="topbar__side">
        <button v-if="showBack" type="button" class="btn btn--text" @click="$emit('back')">
          返回
        </button>
      </div>

      <h1 class="topbar__title">{{ title }}</h1>

      <div class="topbar__side topbar__side--end">
        <span class="topbar__service" :title="serviceHint">{{ serviceLabel }}</span>
      </div>
    </div>
  </header>
</template>

<style scoped>
.topbar {
  position: sticky;
  top: 0;
  z-index: 10;
  height: var(--topbar-height);
  background: var(--color-bg);
  border-bottom: 1px solid var(--color-border);
}

.topbar__inner {
  display: flex;
  align-items: center;
  height: 100%;
  max-width: var(--content-max-width);
  margin: 0 auto;
  padding: 0 var(--content-gutter);
}

.topbar__side {
  flex: 1;
  display: flex;
  align-items: center;
}

.topbar__side--end {
  justify-content: flex-end;
}

.topbar__title {
  font-size: 18px;
  font-weight: 600;
  text-align: center;
}

.topbar__service {
  padding: 4px 12px;
  border-radius: 999px;
  background: var(--color-neutral-bg);
  color: var(--color-neutral-text);
  font-size: var(--text-caption);
}
</style>
