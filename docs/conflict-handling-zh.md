# Patchwork 冲突处理机制

## 优先级顺序

资源按固定优先级顺序加载。最先声明某个资源键的包获胜；后续提供相同键的包将被静默跳过并记录为失败方。

```
基础 Patchwork 文件夹  （最高优先级，始终优先）
包 #0  （Pack Manager 列表顶部）
包 #1
...
包 #N  （最低优先级）
```

## 覆盖的资源类型

| 类型 | 键格式 | 冲突判断条件 |
|------|--------|------------|
| T2D 单独精灵 | `图集清理名/精灵名` 或裸 `精灵名` | `_preloadedBytes.ContainsKey` |
| T2D 精灵表 | PNG 文件名（不含扩展名） | `SpritesheetOverrides.ContainsKey` |
| tk2d 精灵 / 精灵表 | `集合名/精灵名` | 扫描循环中第一个匹配项 |
| 音频 | 文件名（不含扩展名） | `_soundIndex.ContainsKey` |

## ConflictTracker

每个被跳过的资源都会记录为一条 `Entry`：

```
Type       — "sprite" | "sheet" | "t2d-sprite" | "t2d-sheet"
Key        — 资源标识符
WinnerPack — 实际被采用的包路径（null 表示基础文件夹）
LoserPack  — 被跳过的包路径（null 表示基础文件夹）
```

追踪器在每次 `Apply()` 或 `Rescan()` 开始时清空，并在加载过程中重新填充。目前提供两个 GUI 查询接口：

- `ShadowedCount(packPath)` — 该包中有多少资源被更高优先级的包覆盖
- `OverridingCount(packPath)` — 该包覆盖了多少低优先级包的资源

## 注意事项

- 音频冲突**不会**记录到 `ConflictTracker`——`RebuildSoundIndex` 仅静默取第一个找到的文件。
- 基础文件夹对所有包的优先权是硬编码的，无法通过调整包顺序改变。
- 冲突数据仅在下一次 `Apply()` / `Rescan()` / 切换包之前有效。
