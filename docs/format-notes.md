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
