# Burst Strike 代码约定：命名空间与双语注释

> Purpose / 目的
>
> - Standardize namespaces + bilingual comments to reduce coupling and improve readability. 统一命名空间分层与注释写法，降低耦合、提升可读性。
> - Prevent logic code from calling Unity APIs. 避免逻辑层误用 Unity API。

---

## 1. Root namespace / 根命名空间

- Use `Game` as the root namespace. 使用 `Game` 作为根命名空间（保持现状，降低迁移成本）。

---

## 2. Folder -> namespace mapping / 文件夹 -> 命名空间映射

- The file’s directory determines its namespace; whenever you add/move a script, update its namespace accordingly. 文件所在目录决定命名空间；新增/移动脚本时必须同步调整命名空间。

| Folder (under `Assets/Game`) | Namespace | Notes |
|---|---|---|
| `Command/` | `Game.Command` | Command stream 输入命令流 |
| `FixedMath/` | `Game.FixedMath` *(recommended mid-term)* | Fixed-point math 定点数学 |
| `Grid/` | `Game.Grid` | Grid coordinates 网格坐标 |
| `Map/` | `Game.Map` | Map & obstacles 地图与阻挡 |
| `Pathing/` | `Game.Pathing` | A*, flow field, smoothing A*、流场、平滑 |
| `Pathing/Debug/` | `Game.Pathing.Debug` | Path debug 路径调试 |
| `Serialization/` | `Game.Serialization` | YAML/codecs YAML/编码 |
| `Unit/` | `Game.Unit` | Actor/Ability/Activity |
| `World/` | `Game.World` | World/LogicWorld bridge World/LogicWorld 桥接 |
| `Scripts/Fixed/` | `Game.Scripts.Fixed` *(legacy)* | Legacy namespace 历史命名空间 |

---

## 3. Comment style / 注释风格

### 3.1 Public API: XML docs / 公共 API：XML 文档注释

- Use bilingual XML docs for public/protected types and members. 对 public/protected 的类型与成员使用双语 XML doc。
- Format: English first, Chinese immediately after (no `CN/EN` tags). 格式：英文在前，中文紧跟其后（不使用 `CN/EN` 标签）。

Template / 模板：
```csharp
/// <summary>
/// A one-line purpose in English. 英文一句话说明用途。
/// </summary>
/// <remarks>
/// Extra constraints (threading/coordinates/edge cases). 补充约束（线程/坐标系/边界条件）。
/// </remarks>
```

### 3.2 Implementation notes / 实现细节注释

- Prefer short, high-value comments. 注释短而有信息量。
- English first, Chinese immediately after. 英文在前，中文紧跟其后。

Examples / 示例：
- `// Runs on the logic thread; never call UnityEngine APIs. 运行在逻辑线程；禁止调用 UnityEngine API。`
- `// Reserve the next cell to avoid multi-agent contention. 预占下一格，避免多个单位抢同一个格子。`

### 3.3 Avoid / 避免

- Avoid obvious comments that restate the code. 不要写重复代码含义的废话注释。

---

## 4. Dependency direction / 依赖方向（最重要）

- Logic-side code must not call Unity runtime APIs (GameObject/Transform, etc.). 逻辑侧代码不得调用 Unity 运行时 API（GameObject/Transform 等）。

---

## 5. Incremental migration / 增量改造

- No big-bang rewrite. Apply the Boy Scout Rule. 不追求一次性全量翻新；按“顺手改到哪里补到哪里”的童子军原则推进。
