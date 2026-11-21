# MarkTop

Windows 10/11 上的本地化 Markdown 编辑器：单文件绿色版，纯离线渲染，配置只落在 exe 同目录的 `setting-marktop.json`。

## 特性

- **多标签 + 文件树**：打开文件夹作为工作区，左侧树懒加载，标签页保留各自的撤销栈与脏标记
- **两种编辑形态**：双栏（源码 + 实时预览）、源码（纯编辑，专注写作）
- **IDE 风格深浅主题**：WPF 外壳、CodeMirror 源码区、Vditor 预览与代码高亮同步换肤
- **语法高亮**：Markdown、JSON、XML、YAML 使用 CodeMirror 语法树着色；其它文本按纯文本编辑
- **语法检查**：markdownlint 在 Web Worker 中运行，问题以波浪线与行号槽标记，支持文档目录下的 `.markdownlint.json`
- **GitHub 扩展语法**：Alerts（ / TIP / IMPORTANT / WARNING / CAUTION）、任务列表、脚注、删除线、裸链接自动识别
- **公式与图表**：KaTeX 与 Mermaid 随包离线分发，无需联网
- **导出**：Markdown、自包含 HTML（内联样式、内嵌 KaTeX 字体、Mermaid 转内联 SVG）、PDF（A4、18mm 页边距、无页眉页脚）
- **导入**：HTML → Markdown，剥离脚本/样式/内联事件后保留标题、列表、表格、代码与链接
- **图片**：粘贴或拖入后落到文档同级 `assets/`，正文写入相对路径
- **GitHub README 快速编辑**：克隆 `https://github.com/<用户名>/<用户名>.git`，打开 README，保存即 `add → commit → push`

## 构建

前置：

| 依赖 | 版本 | 说明 |
| --- | --- | --- |
| .NET SDK | 10.0.403+ | `global.json` 已锁定 |
| Node.js | 20+ | 仅构建前端时需要 |
| WebView2 Runtime | 任意近期版本 | Win10/11 通常随 Edge 自带 |

```powershell
dotnet build MarkTop.slnx -c Debug      # 会自动执行 npm ci / npm run build
dotnet run --project src/MarkTop.App    # 调试运行
```

单独重建前端：

```powershell
powershell -ExecutionPolicy Bypass -File build\web.ps1
```

## 发布单文件

```powershell
powershell -ExecutionPolicy Bypass -File build\publish.ps1
```

产物 `artifacts\publish\MarkTop.exe`（约 63 MB，self-contained + 单文件 + 压缩）。首次启动会把内嵌的 web 资源解压到 `%LOCALAPPDATA%\MarkTop\web\<hash>`，冷启动约 3–5 秒，之后 1 秒级。

## 自检

```powershell
artifacts\publish\MarkTop.exe --self-test .\selftest
```

会依次验证：打开文档 → 从 WebView2 回读文本 → 导出 Markdown / HTML / PDF，结果写入 `selftest\selftest.txt`，全通过时退出码为 0。

## 文件与数据位置

| 内容 | 位置 |
| --- | --- |
| 配置 | `<exe 目录>\setting-marktop.json`（原子写；损坏时备份为 `.bad-<时间戳>` 并回退默认值） |
| web 资源缓存 | `%LOCALAPPDATA%\MarkTop\web\<hash>` |
| WebView2 用户数据 | `%LOCALAPPDATA%\MarkTop\WebView2` |
| GitHub 仓库克隆 | `%LOCALAPPDATA%\MarkTop\repos\<用户>\<用户>`（可在配置 `git.cloneRoot` 改） |
| 日志 | `%LOCALAPPDATA%\MarkTop\logs\app.log` |

`setting-marktop.json` 是唯一会写在 exe 旁边的文件。

## 配置示例

```json
{
  "schemaVersion": 1,
  "theme": "dark",
  "editor": { "mode": "split", "fontSize": 14, "wordWrap": true, "lintEnabled": true },
  "export": { "pdfMarginMm": 18 },
  "git": { "userName": "", "commitMessage": "docs: update profile README", "autoPush": true }
}
```

## GitHub README 流程

1. 本机安装 Git for Windows，并配置 `user.name` / `user.email`
2. 准备 GitHub 凭证（Git Credential Manager 或 SSH key）
3. 点工具栏「GitHub README」，填入用户名与提交信息
4. MarkTop 克隆或快进更新仓库、打开 README；保存时自动提交并推送
5. 远端有新提交时自动 `fetch → rebase → push` 重试一次；冲突会回滚变基并给出提示

## 快捷键

| 快捷键 | 功能 |
| --- | --- |
| `Ctrl+O` / `Ctrl+S` | 打开 / 保存 |
| `Ctrl+E` | 在双栏 / 源码之间切换 |
| `Ctrl+F` / `Ctrl+H` | 查找 / 替换（源码区） |

## 已知限制

- 依赖系统 WebView2 运行时；未安装时会提示，无法在单文件内打包固定版本运行时
- PDF 使用 Chromium 打印，页眉页脚关闭；需要页边距请在配置里调整 `export.pdfMarginMm`
- 导出的 HTML 中，正文图片仍使用相对路径（与文档同级 `assets/`）
- `.lang` 按无语法定义的语言文件处理（纯文本 + 括号匹配）
- 单文件首次启动需解压原生库与 web 资源，杀软扫描可能延长到数秒

## 项目结构

```
src/MarkTop.App      WPF 外壳、WebView2 宿主、桥接、Git/导出编排
src/MarkTop.Core     配置、编码/换行、路径守卫、Git 客户端、HTML 导入（可单测）
src/MarkTop.Web      TypeScript 前端（CodeMirror 6 + Vditor 4.0.1 离线裁剪）
tests/                xunit 单测
build/               发布与前端构建脚本
```
