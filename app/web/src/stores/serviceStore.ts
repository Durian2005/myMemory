import { defineStore } from 'pinia';
import { ref } from 'vue';
import { fetchHealth, HEALTH_POLL_INTERVAL_MS } from '../services/healthService';
import type { ServiceMode } from '../types/api';

/**
 * 服务状态模块：轮询 /api/health，决定页面处于「在线」还是「本地模式」。
 * 本地模式下顶部栏显示「本地模式」标签（UI 规范 6.2）。
 */
export const useServiceStore = defineStore('service', () => {
  const mode = ref<ServiceMode>('unknown');
  let timerId: number | undefined;

  async function refresh(): Promise<void> {
    const health = await fetchHealth();
    const available = health !== null && health.status === 'ok';
    mode.value = available ? 'online' : 'local';
  }

  function startPolling(): void {
    void refresh();
    if (timerId === undefined) {
      timerId = window.setInterval(() => {
        void refresh();
      }, HEALTH_POLL_INTERVAL_MS);
    }
  }

  function stopPolling(): void {
    if (timerId !== undefined) {
      window.clearInterval(timerId);
      timerId = undefined;
    }
  }

  return { mode, refresh, startPolling, stopPolling };
});
