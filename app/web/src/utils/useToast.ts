import { ref } from 'vue';

/** 全局 Toast 列表。Toast 由页面触发，由 App.vue 中的 ToastHost 统一呈现。 */
export interface ToastItem {
  id: number;
  text: string;
}

const items = ref<ToastItem[]>([]);
let seed = 0;

/** 显示一条 Toast（UI 规范 3.6：底部 #EFE4D6、2.5 秒自动消失、含文字）。 */
export function showToast(text: string, durationMs = 2500): void {
  seed += 1;
  const item: ToastItem = { id: seed, text };
  items.value = [...items.value, item];

  window.setTimeout(() => {
    items.value = items.value.filter((current) => current.id !== item.id);
  }, durationMs);
}

export function useToastList() {
  return items;
}
