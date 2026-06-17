# KubeCheck — KUBE 365 审批配置校验系统

基于 .NET 8 + ASP.NET Core Razor Pages 的企业审批配置数据管理平台，集冲突检测、一致性比对、信息查询、A2 审批矩阵可视化于一体。

---

## 技术栈

| 层级 | 技术 | 说明 |
|------|------|------|
| 后端 | .NET 8 + ASP.NET Core Razor Pages | 页面模型驱动，无 MVC 控制器 |
| 前端 | Bootstrap 3（百度 CDN） + jQuery | 响应式布局，CDN 远端引用 |
| Excel | ClosedXML 0.102.3 | MIT 协议，无需安装 Office |
| 部署 | IIS (web.config) + Kestrel | Windows Server 生产 / 本地调试 |
| 认证 | Cookie 令牌（7天有效） | 一次性授权码 + 邮箱发送 |
| 会话 | Session（服务端内存） | TempData 隔离，30分钟过期 |

---

## 功能模块

### 1. 冲突值检测 (`/ConflictCheck`)

**用途**：上传 CSV 审批配置表，自动检测同一条件组内的数据冲突。

**编辑逻辑**：
- 自动识别列：匹配 `^role\d*$` 正则定位审批人列，左侧为条件列
- `id` 列及全唯一列自动排除
- 用户选择第一个审批人起始列 → 该列及右侧全部为审批人列
- 按条件列值分组（`|||` 分隔拼接），组内执行三条规则：

| 规则 | 检测逻辑 | 异常文案 |
|------|---------|---------|
| 审批人数不一致 | 统计组内各行非空审批单元格数，不一致则全组标记 | 同一条件组内审批人数不一致 |
| 人员排列不一致 | 比较组内审批人姓名、顺序、空值分布，有差异则标记 | 同一条件组内审批人员/排列顺序不一致 |
| 配置完全重复 | 组内多行审批人签名完全一致，视为冗余 | 同一条件组内存在重复审批配置 |

- 结果按条件组合并展示，支持展开/收起 + 确认移除
- 下载带「存在异常值」首列的 CSV

**核心文件**：`Models/CsvValidator.cs`

---

### 2. 一致性比对 (`/Compare`)

**用途**：自选条件列，只检测审批人数是否一致（不管具体是谁）。

**编辑逻辑**：
- 用户勾选作为审批条件的列（不自动勾选）
- 选择审批人起始列
- 校验时仅运行规则1（人数统计），跳过规则2/3
- 同样按条件组展示，支持确认移除异常组

**核心参数**：`CsvValidator.Validate(allRows, condCols, roleCols, countsOnly: true)`

---

### 3. 信息查询 (`/Search`)

**用途**：跨表全文搜索，支持员工、部门、成本中心、A2 审批矩阵。

**编辑逻辑**：
- **启动时加载**：扫描 `search\` 目录下所有 CSV（员工、部门、项目代码）和 `A2\` 目录下 Markdown 文件
- **CSV 索引**：UserList → 员工姓名/工号/邮箱/部门/成本中心 + 上下级汇报链
- **Markdown 索引**：解析 `##` 节标题 + 表格，提取 Code/分类/项目/申请人/审批链
- **中英分离**：按 `\n` 截取中文部分，丢弃英文翻译
- **搜索算法**：按空格、`-`、`/` 拆分关键词 → 任一碎片在 DisplayName/Code/Detail/Relation 中命中即返回
- **交叉引用**：审批角色名（如 CFO）可反向搜索到所有需要该角色的 A2 条目
- **汇报关系**：向上追溯 5 层 → 向下展示直接下级（≤10人）

**索引重载**：右侧「重新加载索引」按钮，无需重启

**核心文件**：`Models/SearchIndex.cs`、`Pages/Search.cshtml`

---

### 4. A2 可视化 (`/A2Viewer`)

**用途**：将 A2 审批矩阵 Markdown 文件渲染为可浏览的网页表格。

**编辑逻辑**：
- 读取 `A2\` 目录下 `.md` 文件
- 按需加载：点击目录项才 AJAX 获取对应章节
- 表格转换为 Bootstrap 样式，√ 标记绿色徽章
- 支持收起/展开目录 → 全屏查看

**核心文件**：`Pages/A2Viewer.cshtml`、`Pages/A2Viewer.cshtml.cs`

---

### 5. 授权登录 (`/Auth`)

**用途**：一次性授权码 + 邮箱申请，控制访问权限。

**编辑逻辑**：
- 内置万能码 `112233`，7天有效 Cookie
- 点击「申请」→ 生成 6 位随机码 → SMTP 发邮件给管理员（fire-and-forget）
- 码 30 分钟有效，一次性使用
- 所有页面 OnGet 检查 `KubeCheckAuth` Cookie，无则 302 跳转登录页

**核心文件**：`Models/AuthCodeStore.cs`、`Pages/Auth.cshtml`

---

## 项目结构

```
KubeCheck/
├── Models/
│   ├── CsvValidator.cs          # CSV 校验引擎
│   ├── SearchIndex.cs           # 搜索索引（CSV + Markdown）
│   └── AuthCodeStore.cs         # 授权码存储
├── Pages/
│   ├── ConflictCheck.cshtml     # 冲突值检测
│   ├── Compare.cshtml           # 一致性比对
│   ├── Search.cshtml            # 信息查询
│   ├── A2Viewer.cshtml          # A2 可视化
│   ├── Auth.cshtml              # 授权登录
│   ├── Error.cshtml             # 错误页
│   └── Shared/_Layout.cshtml    # Bootstrap 3 布局
├── Program.cs                   # 应用入口
├── KubeCheck.csproj             # 项目文件
├── search/                      # 索引数据源（CSV）
├── A2/                          # A2 审批矩阵（.md）
└── wwwroot/                     # 静态资源
```

---

## 本地运行

```bash
git clone https://github.com/th2006464/KubeCheck.git
cd KubeCheck
dotnet run --urls "http://localhost:5000"
```

## 部署

```bash
dotnet publish -c Release -o release
# 复制 release\ 到 IIS 站点目录
# 默认主页：http://<host>/ → 信息查询页面
```
