# 技术格式笔记 / Format Notes

巫师3 次世代版（4.0+）字体 MOD 相关格式的逆向笔记，供贡献者参考。
本页不含任何游戏原版资源，所有结构性数据均为格式骨架。

## Bundle（POTATO70）

```
0x00  8B   magic "POTATO70"
0x08  u32  fileSize（流通字体 MOD 中为固定值，与实际长度可以不一致）
0x0C  u32  indexOffset
0x10  u32  indexSize（= 文件槽数 × 320）
0x14  u32  version/depot = 0x00010003
0x18  8B   固定填充 00 13 13 13 13 13 13 13
0x20  文件槽（每槽 320 字节）:
        +0x000 资源路径（如 gameplay\gui_new\swf\witcher3\fonts_cn.redswf）+ NUL
        +0x114 u32 size（解压后）
        +0x118 u32 archiveSize（存储大小；== size 表示未压缩）
        +0x11C u32 offset（数据绝对偏移）
        其余为零
其后为文件数据区
```

流通字体 MOD 的惯例：数据从 0x1000 开始，文件尾部补零到固定大小；游戏对 zlib/CWS
流读取到结束标记即止，尾部填充被忽略。

## metadata.store

356 字节结构：头部 `03 "VTM" 06 00 00 00` + 两个 u32（数据区跨度）+ 字符串表
（bundle 文件名、资源路径、路径组件）+ 若干与文件大小/数量相关的字段。
字段语义部分参考 WolvenKit 7 的 Metadata_Store 读取实现，本工具按观察到的布局
序列化并生成与流通 MOD 一致的输出。

## 字体库（.redswf）

.redswf = 裸 SWF（CWS zlib 压缩或 FWS 未压缩）。字体库 SWF 的 tag 序列：

```
Tag1000（RED 引擎自定义, 38B）: 字体族名 "fonts-cn{hash}" + 资源名 "fonts_cn"
FileAttributes / SetBackgroundColor / DefineSceneAndFrameLabelData
DefineFont3 × N   ← 各语言字体槽位（简中 2 个 / 英文 3 个）
DefineEditText + CSMTextSettings + PlaceObject2 × N
  （Scaleform fontlib 注册机制: EditText 的 HTML face= 引用字体名，
    $NormalFont/$ItalicFont/$BoldFont/$CreditsFont 是 GFx 特殊实例名）
ShowFrame / End
```

生成时除 DefineFont3 外所有 tag 原样保留（内嵌骨架已剥离全部原始字形数据）。

## DefineFont3

标准 SWF 字段布局（fontId / flags / langCode / name / numGlyphs / offsets /
codeTableOffset / glyph shapes / codes / layout / kerning）：

- offsets 与 codeTableOffset 均相对 OffsetTable 起始；offs[0] = numGlyphs*4+4
- 字形坐标单位为 twips；本工具使用 20480 twips/em 的统一缩放
- ⚠️ 字形位流中的样式记录：本游戏（Scaleform GFx）的字体字形在 F0/F1 选择标志后
  【不写】UB[NumFillBits] 填充值位，MoveBits 紧跟其后 —— 与公开 SWF 规范不同，
  按规范写会导致游戏内全部字形渲染为缺字框。实现见 `SwfShapeWriter.EncodeGlyphShape`。

## layout 段（ascent / descent / leading）

layout 段位于 codeTable 之后：

```
s16 FontAscent / s16 FontDescent / s16 FontLeading   （twips，20480 twips/em）
s16 FontAdvanceTable[numGlyphs]
RECT FontBoundsTable[numGlyphs]
u16 KerningCount
```

GFx 用这三个数排版：**首行基线 = 文本框 top + FontAscent**，行盒高度 =
ascent + descent + leading。游戏 UI 的文本框坐标是照着原版字库的这组数值摆放的，
所以**本工具必须沿用原版数值，不能用替换字体的字体度量**：例如替换字体是微软雅黑
（usWin 行盒 1.32 em）或思源黑体（1.45 em）时，ascent 比原版大 0.16–0.28 em，
所有 UI 文字（菜单、字幕、HUD、任务提示）会整体下移 4–8 px，也就是"字下沉"。

原版各槽位的数值（取自 `content\content0\bundles\r4gui.bundle` 中的
`gameplay\gui_new\swf\witcher3\fonts_{cn,zh,en}.redswf`，本仓库只记录数字，不含任何
游戏数据），见 `src/Witcher3FontTool/FontLayout.cs`：

| 字库 | FontID | 原版字体 | ascent | descent | leading |
|------|--------|----------|--------|---------|---------|
| fonts_cn | 1 | 文鼎UD晶熙黑体G30_D | 20245 | 4501 | 853 |
| fonts_cn | 5 | PFDinTextCondPro-Regular | 18091 | 4245 | 853 |
| fonts_zh | 1 | Noto Sans TC Regular | 17408 | 3072 | 853 |
| fonts_zh | 5 | PFDINTextCondPro-Regular | 19157 | 5205 | 853 |
| fonts_en | 1 / 3 / 5 | PF Din Text Cond Pro | 18100 / 18440 / 18900 | 4240 / 4240 / 4400 | 1860 / 2200 / 2820 |

注意原版 `.redswf` 是 **CR2W（`CSwfResource`）**，里面再套一层 zlib 压缩的 SWF
数据（CR2W 偏移处的 tag + u32 长度 + zlib 流）；MOD 里放裸 SWF 即可，游戏同样接受。
原版 bundle 的文件槽是 304 字节（MOD 用 320 字节），且数据经 zlib 压缩，
因此本工具的内嵌骨架是从原版资源中剥离字形后的结构，而非直接读取游戏文件。
