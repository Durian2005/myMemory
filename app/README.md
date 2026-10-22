# myMemory 口述记录生成传记系统（网页微缩版）

可运行代码工程。已完成三个细粒度垂直切片，并额外补上导出，形成
「新建记录 → 录入素材 → 生成初稿 → 编辑保存 → 导出文档」的完整可用闭环。

> 只想发给别人用、不想让对方装环境？直接看第七节「打包成免安装分发版」。

## 一、跑起来

```bash
# 终端 1：后端（仅监听回环 127.0.0.1:5210）
cd server
dotnet run

# 终端 2：前端（http://localhost:5173）
cd web
npm install
npm run dev
```

浏览器打开 <http://localhost:5173>。前端通过构建期变量 `VITE_API_BASE` 指定后端地址，缺省
`http://127.0.0.1:5210`；后端 CORS 只放行 `localhost:5173` 与 `127.0.0.1:5173`，改端口会连不上。

## 二、页面与路由

| 页面 | 路由 | 对应任务 |
|---|---|---|
| P1 记录列表 | `/` | A |
| P2 新建记录 | `/create` | A |
| P3 录音与文字素材 | `/story/:storyId/record` | B |
| P4 生成初稿与编辑（含导出弹窗） | `/story/:storyId/draft` | C / D |

> P5「在线编辑」页的编辑与未保存拦截能力已并入 P4：P4 的初稿区可直接编辑，离开前会二次确认，
> 单独成页留待后续迭代（见任务看板待开发列）。

## 三、接口

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/api/health` | 服务状态（I01）。capabilities 由实际注入的引擎决定，另返回 `engines` 供界面提示 |
| POST | `/api/projects` | 新建传记项目。**早期契约中不存在，为实现阶段新增** |
| POST | `/api/transcriptions` | 上传音频并转写（I02），multipart/form-data |
| POST | `/api/polish` | 生成传记初稿（I03） |
| POST | `/api/exports` | 导出 DOCX（I04），成功返回文件流 |

`POST /api/projects` 请求体三项均必填，用前端生成的 `clientStoryId` + `createdAt` 作幂等键，
后端保持零状态：

```jsonc
{ "title": "我的童年", "clientStoryId": "<UUID>", "createdAt": "2026-10-17T09:40:00+08:00" }
```

## 四、外部智能服务：可替换的 Provider

转写与生成都通过接口注入，业务层只依赖接口，不依赖具体供应商：

```
Services/Providers/Providers.cs
  ISpeechToTextProvider  ← StubSpeechToTextProvider（本地模拟）
  IGenerationProvider    ← StubGenerationProvider（本地模拟）
```

**当前注入的是本地模拟实现**，原因很直接：当前运行环境没有语音识别与大模型的密钥。
链路是真实的（真上传、真校验、真错误码、真落库），只有「音频变文字」和「文本润色」两步
用占位内容代替，界面上会明确标注「本地模拟引擎」，不伪装成已接入真实 AI。

`appsettings.json` 可切换失败分支，用于验证错误码：

```jsonc
"Providers": {
  "Transcription": { "Mode": "stub", "Stub": { "FailMode": "none", "DelayMs": 600 } },
  "Generation":    { "Mode": "stub", "Stub": { "FailMode": "none", "DelayMs": 900 } }
}
// FailMode: none | unavailable | timeout | empty | drift(仅生成)
```

接入真实服务时，实现一个 `ISpeechToTextProvider` 或 `IGenerationProvider`，在 `Program.cs`
替换注册即可，接口契约、前端与页面逻辑都不用改。

## 五、数据与降级

- 记录、片段、音频 Blob、原始转写、初稿全部存在浏览器 IndexedDB（库名 `mymemory`，v2 起含 `segments`）。
- 后端不持久化任何用户数据，日志只记错误码，不记正文。
- 后端不可用时前端进入「本地模式」：仍可新建（本地兜底）、录音、录入文字、编辑与保存；
  转写与生成会给出可重试的业务错误，音频已在本地，不会丢。
- 已知偏差：技术方案写「本地模式下导出仍可用」，但本轮导出由后端拼 DOCX，后端不可用时导出会失败并提示重试。

## 六、目录

```
myMemory-app/
├── web/                      前端（Vue 3 + Vite + Pinia + TypeScript）
│   └── src/
│       ├── views/            HomeView(P1) / CreateView(P2) / RecordView(P3) / DraftView(P4)
│       ├── components/       顶部栏、记录卡片、状态标签、确认弹窗、空态、Toast、导出弹窗
│       ├── stores/           storyStore（记录 + 片段 + 生成 + 导出编排）、serviceStore
│       ├── services/         http（统一请求/超时/错误码/下载）、project / transcription / generation / export
│       ├── db/               localDb（IndexedDB：stories + segments）
│       ├── types/            story / segment / api
│       ├── utils/            标识、时间与字数格式化、Toast
│       ├── router/           路由
│       └── styles/           tokens.css（设计 Token）、base.css（骨架与通用组件类）
└── server/                   后端（ASP.NET Core Minimal API / .NET 9）
    ├── Endpoints/            Health / Project / Transcription / Generation / Export
    ├── Services/             ProjectService、TranscriptionService、GenerationService、DocxExportService
    ├── Services/Providers/   转写与生成的接口与本地模拟实现
    ├── Models/               DTO、错误码表与文案
    └── Program.cs            监听、CORS、JSON、上传上限、服务注册
```

目录与命名遵循《技术方案与页面原型规划（人工修改稿）》第 5 章；导出用 OOXML 直接拼包，
不依赖 Office 组件（技术方案 8.3 决策四）。

## 七、打包成免安装分发版（Windows）

分发版把前端产物塞进后端 `wwwroot`，再把后端编译成**自带 .NET 运行时的单文件 exe**。
对方拿到 zip 解压、双击一个 bat 就能用，不需要装 .NET、Node 或数据库。

```bash
# 0. 在 myMemory-app 目录下执行

# 1. 构建前端，并把产物交给后端托管
cd web
npm run build
rm -rf ../server/wwwroot && mkdir -p ../server/wwwroot
cp -r dist/* ../server/wwwroot/

# 2. 发布自带运行时的单文件（win-x64）
cd ../server
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -o ../release/myMemory

# 3. 把 release/myMemory 整个目录打包发出去
```

产物结构：

```
myMemory/
├── MyMemory.Server.exe   程序本体（约 47MB，自带运行时）
├── appsettings.json      配置（可切换外部服务）
├── wwwroot/              前端页面资源
├── 启动 myMemory.bat     双击启动，3 秒后自动开浏览器
└── 使用说明.txt          给使用者的说明与常见问题
```

要点：

- 页面与接口**同源**（都在 `127.0.0.1:5210`），所以分发版不需要 5173、也不需要 CORS。
- 前端用 history 路由，`Program.cs` 里加了 `MapFallbackToFile("index.html")`，刷新 `/story/xxx/draft` 不会 404。
- `server/wwwroot/` 与 `release/` 都是生成物，已在 `.gitignore` 中排除。
- 更换监听端口后要同步改前端 `VITE_API_BASE` 并重新构建，否则页面连不上接口。
