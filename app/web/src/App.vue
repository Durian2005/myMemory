<script setup lang="ts">
import { onMounted, onUnmounted } from 'vue';
import ToastHost from './components/ToastHost.vue';
import { useServiceStore } from './stores/serviceStore';

/**
 * 应用根组件。
 * 负责启动服务探活轮询，并挂载全局 Toast 容器；页面本身由路由渲染。
 */
const serviceStore = useServiceStore();

onMounted(() => {
  serviceStore.startPolling();
});

onUnmounted(() => {
  serviceStore.stopPolling();
});
</script>

<template>
  <RouterView />
  <ToastHost />
</template>
