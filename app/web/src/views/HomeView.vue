<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { useRouter } from 'vue-router';
import AppTopBar from '../components/AppTopBar.vue';
import ConfirmDialog from '../components/ConfirmDialog.vue';
import EmptyState from '../components/EmptyState.vue';
import StoryCard from '../components/StoryCard.vue';
import { useStoryStore } from '../stores/storyStore';
import { showToast } from '../utils/useToast';

/**
 * P1 首页 / 记录列表（技术方案 9.2）。
 * 顶部为页面标题；中部为记录列表；底部为唯一主要按钮「新建口述记录」。
 * 每条记录提供继续编辑、查看与删除三个次级入口，删除需二次确认（UI 规范 5.4）。
 */
const router = useRouter();
const storyStore = useStoryStore();

const pendingDeleteId = ref('');
const pendingDeleteTitle = ref('');
const dialogVisible = ref(false);

const isEmpty = computed(() => !storyStore.isLoading && storyStore.stories.length === 0);

onMounted(() => {
  void storyStore.loadStories();
});

function goCreate(): void {
  void router.push({ name: 'create' });
}

/** 进入记录：还没有素材时去录音页，已有素材则直接看初稿（省掉一次点击）。 */
function openStory(storyId: string): void {
  const story = storyStore.findStory(storyId);
  if (!story) {
    return;
  }

  const target = story.segment_count > 0 ? 'draft' : 'record';
  void router.push({ name: target, params: { storyId } });
}

/** 本地读取失败后的重试入口（UI 规范 5.6：错误块必须给出下一步动作）。 */
function retryLoad(): void {
  void storyStore.loadStories();
}

function askRemove(storyId: string): void {
  const story = storyStore.findStory(storyId);
  pendingDeleteId.value = storyId;
  pendingDeleteTitle.value = story?.title ?? '';
  dialogVisible.value = true;
}

async function confirmRemove(): Promise<void> {
  dialogVisible.value = false;
  try {
    await storyStore.removeStory(pendingDeleteId.value);
    showToast('记录已删除');
  } catch {
    showToast('删除失败，请稍后重试');
  }
}

function cancelRemove(): void {
  dialogVisible.value = false;
}
</script>

<template>
  <div class="app-shell">
    <AppTopBar title="我的口述记录" />

    <main class="app-main">
      <!-- 加载态：骨架块 + 文字（UI 规范 3.9） -->
      <div v-if="storyStore.isLoading" class="skeleton-wrap">
        <p class="skeleton-text">正在加载…</p>
        <div v-for="index in 2" :key="index" class="skeleton-block"></div>
      </div>

      <!-- 错误态：本地存储读取失败 -->
      <div v-else-if="storyStore.loadError" class="error-block">
        <span class="error-block__bar"></span>
        <div>
          <p class="error-block__title">读取失败</p>
          <p class="error-block__reason">{{ storyStore.loadError }}</p>
          <button type="button" class="btn btn--secondary error-block__action" @click="retryLoad">
            重试
          </button>
        </div>
      </div>

      <!-- 空态 -->
      <EmptyState v-else-if="isEmpty" text="还没有口述记录，可以先创建一条记录" />

      <!-- 正常态 -->
      <div v-else class="story-list">
        <StoryCard
          v-for="story in storyStore.stories"
          :key="story.story_id"
          :story="story"
          @open="openStory"
          @remove="askRemove"
        />
      </div>
    </main>

    <!-- 底部动作区：每页唯一主按钮（UI 规范 4.5 / A5） -->
    <footer class="app-action-bar">
      <button type="button" class="btn btn--primary" @click="goCreate">新建口述记录</button>
    </footer>

    <ConfirmDialog
      :visible="dialogVisible"
      title="删除这条记录？"
      :description="`删除「${pendingDeleteTitle}」后，该记录的口述音频、原始转写文本与传记正文都会被一并清除，且无法找回。`"
      confirm-text="确认删除"
      danger
      @confirm="confirmRemove"
      @cancel="cancelRemove"
    />
  </div>
</template>

<style scoped>
.skeleton-wrap {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}

.skeleton-text {
  margin: 0;
  color: var(--color-text-muted);
  font-size: var(--text-caption);
}

.skeleton-block {
  height: 148px;
  border-radius: 6px;
  background: var(--color-bg-hover);
}

.story-list {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}
</style>
