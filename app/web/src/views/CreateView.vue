<script setup lang="ts">
import { computed, ref } from 'vue';
import { useRouter } from 'vue-router';
import AppTopBar from '../components/AppTopBar.vue';
import { useStoryStore } from '../stores/storyStore';
import { ApiError } from '../types/api';
import { showToast } from '../utils/useToast';

/**
 * P2 新建记录（技术方案 9.3）。
 * 仅包含一个标题输入框与一个主要按钮「开始录音」；标题为空时提示「请先填写记录标题」。
 * 创建成功后提示「记录已创建，接下来可以开始录音」。
 * 标题长度上限 40 字，与 UI 设计规范 3.3 的计数示例「2 / 40」一致。
 */
const MAX_TITLE_LENGTH = 40;

const router = useRouter();
const storyStore = useStoryStore();

const title = ref('');
const titleError = ref('');
const submitting = ref(false);
const createError = ref('');

const counterText = computed(() => `${title.value.length} / ${MAX_TITLE_LENGTH}`);

/**
 * 只在提交过程中禁用按钮。
 * 标题为空时**不禁用**，而是让用户点下去后看到「请先填写记录标题」——
 * 按钮变灰会让老人不知道哪里没填，也无法触发界面规范要求的提示文案。
 */
const isSubmitDisabled = computed(() => submitting.value);

const titleInput = ref<HTMLInputElement | null>(null);

function handleBack(): void {
  void router.push({ name: 'home' });
}

/** 输入过程中实时重算校验结果，校验通过即清除错误（UI 规范 5.3）。 */
function handleInput(): void {
  if (titleError.value && title.value.trim().length > 0) {
    titleError.value = '';
  }
}

async function handleSubmit(): Promise<void> {
  const trimmed = title.value.trim();
  if (trimmed.length === 0) {
    titleError.value = '请先填写记录标题';
    // 把焦点送回输入框，键盘与读屏用户能立刻定位到问题（UI 规范 5.3）
    titleInput.value?.focus();
    return;
  }

  submitting.value = true;
  createError.value = '';

  try {
    const story = await storyStore.addStory(trimmed);
    showToast(
      storyStore.lastCreateOffline
        ? '后端未连接，记录已保存在本机（本地模式）'
        : '记录已创建，接下来可以开始录音',
    );
    void router.push({ name: 'record', params: { storyId: story.story_id } });
  } catch (error) {
    createError.value =
      error instanceof ApiError ? error.message : '记录创建失败，请稍后重试';
  } finally {
    submitting.value = false;
  }
}
</script>

<template>
  <div class="app-shell">
    <AppTopBar title="新建口述记录" show-back @back="handleBack" />

    <main class="app-main">
      <p class="intro">给这段回忆起一个名字，稍后就可以开始口述。</p>

      <div class="field">
        <label class="field__label" for="story-title">记录标题</label>
        <input
          id="story-title"
          ref="titleInput"
          v-model="title"
          class="field__control"
          :class="{ 'field__control--invalid': titleError !== '' }"
          type="text"
          :maxlength="MAX_TITLE_LENGTH"
          placeholder="例如：我的童年"
          @input="handleInput"
        />
        <div class="field__footer">
          <span class="field__error">{{ titleError }}</span>
          <span class="field__counter">{{ counterText }}</span>
        </div>
      </div>

      <!-- 创建失败：错误块，保留用户已填写的内容（UI 规范 5.3） -->
      <div v-if="createError" class="error-block">
        <span class="error-block__bar"></span>
        <div>
          <p class="error-block__title">创建失败</p>
          <p class="error-block__reason">{{ createError }}</p>
        </div>
      </div>

      <section class="tips">
        <h2 class="tips__title">使用说明</h2>
        <ul class="tips__list">
          <li>标题可以随时修改，填写后按最下方的按钮继续。</li>
          <li>创建后会进入录音页，可以录一段，也可以直接打字。</li>
          <li>记录和录音都保存在本设备当前浏览器中，不会上传到别处。</li>
        </ul>
      </section>
    </main>

    <footer class="app-action-bar">
      <button type="button" class="btn btn--primary" :disabled="isSubmitDisabled" @click="handleSubmit">
        {{ submitting ? '处理中…' : '开始录音' }}
      </button>
    </footer>
  </div>
</template>

<style scoped>
.intro {
  margin: 0 0 var(--space-4);
  max-width: var(--form-span-width);
  color: var(--color-text-muted);
}

.tips {
  margin-top: var(--space-4);
  max-width: var(--form-span-width);
}

.tips__title {
  font-size: var(--text-subtitle);
  font-weight: 600;
}

.tips__list {
  margin: var(--space-2) 0 0;
  padding-left: var(--space-3);
  color: var(--color-text-muted);
}

.tips__list li + li {
  margin-top: var(--space-1);
}
</style>
