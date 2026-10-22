import { createRouter, createWebHistory } from 'vue-router';
import CreateView from '../views/CreateView.vue';
import DraftView from '../views/DraftView.vue';
import HomeView from '../views/HomeView.vue';
import RecordView from '../views/RecordView.vue';

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', name: 'home', component: HomeView },
    { path: '/create', name: 'create', component: CreateView },
    // P3 素材录入（录音 / 文字）
    { path: '/story/:storyId/record', name: 'record', component: RecordView },
    // P4 转写与初稿（含编辑与导出）
    { path: '/story/:storyId/draft', name: 'draft', component: DraftView },
    // 未知路径回到列表，避免老人看到空白页
    { path: '/:pathMatch(.*)*', redirect: '/' },
  ],
});

export default router;
