import type { Segment } from '../types/segment';
import type { Story } from '../types/story';

/**
 * 本地数据层：封装 IndexedDB 的读写，屏蔽底层细节（技术方案 5.2）。
 * 记录、音频与文本全部保存在当前浏览器，后端不持久化任何用户数据。
 *
 * v2 相对 v1 新增 segments 对象仓库：一段记录可以包含多次录音或手工文字，
 * 音频以 Blob 形式随片段一起入库，避免把大文件塞进记录对象里。
 */

const DB_NAME = 'mymemory';
const DB_VERSION = 2;
const STORE_STORIES = 'stories';
const STORE_SEGMENTS = 'segments';
const INDEX_STORY = 'by_story';

let dbPromise: Promise<IDBDatabase> | null = null;

function openDb(): Promise<IDBDatabase> {
  if (dbPromise) {
    return dbPromise;
  }

  dbPromise = new Promise<IDBDatabase>((resolve, reject) => {
    const request = indexedDB.open(DB_NAME, DB_VERSION);

    request.onupgradeneeded = () => {
      const db = request.result;
      if (!db.objectStoreNames.contains(STORE_STORIES)) {
        db.createObjectStore(STORE_STORIES, { keyPath: 'story_id' });
      }
      if (!db.objectStoreNames.contains(STORE_SEGMENTS)) {
        const store = db.createObjectStore(STORE_SEGMENTS, { keyPath: 'segment_id' });
        // 按记录维度建索引，列表与生成时只取当前记录的片段
        store.createIndex(INDEX_STORY, 'story_id', { unique: false });
      }
    };

    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error ?? new Error('本地数据库打开失败'));
  });

  return dbPromise;
}

function toPromise<T>(request: IDBRequest<T>): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error ?? new Error('本地数据库操作失败'));
  });
}

/**
 * 展开成普通对象再写库。
 *
 * 为什么必须这一步：Store 里的记录与片段都是 Vue 的响应式代理（reactive proxy），
 * 而 IndexedDB 的结构化克隆算法不接受 Proxy，会抛
 * 「Failed to execute 'put' on 'IDBObjectStore': #<Object> could not be cloned」。
 * 展开运算符只做一层浅拷贝，但这两类对象都是扁平结构（Blob 不会被 Vue 代理），足够安全。
 */
function toPlain<T extends object>(value: T): T {
  return { ...value };
}

/** 读取全部记录，按最近修改时间倒序，保证列表中新记录排在前面。 */
export async function listStories(): Promise<Story[]> {
  const db = await openDb();
  const store = db.transaction(STORE_STORIES, 'readonly').objectStore(STORE_STORIES);
  const all = await toPromise(store.getAll() as IDBRequest<Story[]>);
  return all.sort((a, b) => b.updated_at.localeCompare(a.updated_at));
}

/** 读取单条记录。 */
export async function getStory(storyId: string): Promise<Story | undefined> {
  const db = await openDb();
  const store = db.transaction(STORE_STORIES, 'readonly').objectStore(STORE_STORIES);
  return (await toPromise(store.get(storyId) as IDBRequest<Story | undefined>)) ?? undefined;
}

/** 写入或更新一条记录。 */
export async function putStory(story: Story): Promise<void> {
  const db = await openDb();
  const store = db.transaction(STORE_STORIES, 'readwrite').objectStore(STORE_STORIES);
  await toPromise(store.put(toPlain(story)));
}

/** 删除一条记录，并级联清除它的全部片段与音频（技术方案 8.4：删除即彻底）。 */
export async function deleteStory(storyId: string): Promise<void> {
  const db = await openDb();
  const segments = await listSegments(storyId);
  const transaction = db.transaction([STORE_STORIES, STORE_SEGMENTS], 'readwrite');
  const storyStore = transaction.objectStore(STORE_STORIES);
  const segmentStore = transaction.objectStore(STORE_SEGMENTS);

  storyStore.delete(storyId);
  for (const segment of segments) {
    segmentStore.delete(segment.segment_id);
  }

  await new Promise<void>((resolve, reject) => {
    transaction.oncomplete = () => resolve();
    transaction.onerror = () => reject(transaction.error ?? new Error('删除记录失败'));
  });
}

/** 读取某条记录的全部片段，按序号升序。 */
export async function listSegments(storyId: string): Promise<Segment[]> {
  const db = await openDb();
  const store = db.transaction(STORE_SEGMENTS, 'readonly').objectStore(STORE_SEGMENTS);
  const index = store.index(INDEX_STORY);
  const all = await toPromise(index.getAll(storyId) as IDBRequest<Segment[]>);
  return all.sort((a, b) => a.seq - b.seq);
}

/** 写入或更新一个片段。 */
export async function putSegment(segment: Segment): Promise<void> {
  const db = await openDb();
  const store = db.transaction(STORE_SEGMENTS, 'readwrite').objectStore(STORE_SEGMENTS);
  await toPromise(store.put(toPlain(segment)));
}

/** 删除单个片段（重新录制、删除文字素材时使用）。 */
export async function deleteSegment(segmentId: string): Promise<void> {
  const db = await openDb();
  const store = db.transaction(STORE_SEGMENTS, 'readwrite').objectStore(STORE_SEGMENTS);
  await toPromise(store.delete(segmentId));
}
