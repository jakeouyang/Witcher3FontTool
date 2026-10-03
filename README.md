# Witcher3FontTool

[中文](#中文) | [English](#english)

<a name="中文"></a>

## 中文

非官方的《巫师 3：狂猎》次世代版（4.0+）中文字体 MOD 生成器。选择任意 TrueType / TrueType Collection 字体，一键生成并安装到游戏 `Mods` 目录，替换游戏 UI 的简体中文 / 繁体中文 / 英文字体。

**[⬇ 下载最新版](https://github.com/jakeouyang/Witcher3FontTool/releases/latest)**（Windows x64 单文件，自带 .NET 运行时）

![CI](https://github.com/jakeouyang/Witcher3FontTool/actions/workflows/ci.yml/badge.svg)

### 特性

- **GUI / CLI 双模式**：无参数启动图形界面；完整功能亦可通过命令行使用
- **多语言字体槽位**：简体（fonts_cn，2 槽位）、繁体（fonts_zh，2 槽位）、英文（fonts_en，3 槽位），可任意组合勾选
- **字形覆盖智能裁剪**：按 GB2312 / Big5 / ASCII / Latin-1 + 游戏原字符集取并集，缺字自动报告且不会生成空白字形
- **一键安装 / 还原**：自动创建 `Mods` 目录；仅管理本工具生成的 MOD（SHA256 清单校验），还原时绝不触碰其他 MOD
- **安全防护**：游戏运行中拒绝安装/还原；目标已存在时拒绝覆盖；无写入权限时提示
- **单文件发布**：自带 .NET 运行时，解压即用

### 使用

#### GUI

运行 `Witcher3FontTool.exe` → 选择游戏根目录 → 选择字体文件 → 勾选语言 → 「生成并安装」。

#### CLI

```text
Witcher3FontTool.exe install  <游戏目录> <字体文件> [--zh-hans] [--zh-hant] [--english]
Witcher3FontTool.exe restore  <游戏目录>
Witcher3FontTool.exe generate <字体文件> <输出目录> [--zh-hans] [--zh-hant] [--english]
Witcher3FontTool.exe validate-game <游戏目录>
Witcher3FontTool.exe verify   <bundle 或 swf>
Witcher3FontTool.exe render   <bundle 或 swf> <文本>
```

不指定语言时默认简体 + 英文。安装后需在游戏启动器 / Mods 菜单中启用 MOD，且游戏语言需与所选字体语言一致。

### 构建

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)（Windows）：

```powershell
dotnet build -c Release
dotnet publish src/Witcher3FontTool -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

推送标签 `v*` 时 CI 会自动构建并发布单文件 exe 到 GitHub Releases。

### 已知限制

- 仅支持 TrueType 轮廓（TTF/TTC）；OTF/CFF 会明确报错拒绝
- 仅支持 BMP（U+0000–U+FFFF）字符；不处理可变字体轴
- TTC 使用第一个字体面
- 游戏 UI 文字需语言设置为对应语言才会使用相应字体资源

### 工作原理（简述）

工具读取内嵌的字体库骨架（源自本机游戏资源、已完全移除原字体轮廓数据），将其中所有
DefineFont3 槽位替换为从用户 TTF 转换出的矢量字形（TrueType 二次贝塞尔 → SWF Shape 记录），
再按巫师3 次世代版 bundle 格式打包并写入 metadata 索引。详见 [docs/format-notes.md](docs/format-notes.md)。

### 致谢

- [WolvenKit](https://github.com/WolvenKit/WolvenKit) —— metadata.store 字段布局参考
- 巫师3 中文本地化社区对字体 MOD 格式的探索

### 免责声明

本项目为非官方粉丝工具，与 CD PROJEKT RED 无关。《巫师》是 CD PROJEKT S.A. 的商标。
请使用你拥有合法授权的字体文件；因使用本工具产生的任何问题由使用者自行承担。

<a name="english"></a>

## English

Unofficial font replacement MOD generator for The Witcher 3: Wild Hunt next-gen (4.0+).
Pick any TrueType / TrueType Collection font, generate a MOD, and install it straight into
the game's `Mods` directory to replace the Simplified Chinese / Traditional Chinese /
English UI fonts.

**[⬇ Download latest](https://github.com/jakeouyang/Witcher3FontTool/releases/latest)** (Windows x64 single-file, self-contained)

### Features

- **GUI and CLI** in one binary
- Per-language font slots: Simplified Chinese (fonts_cn, 2 slots), Traditional Chinese
  (fonts_zh, 2 slots), English (fonts_en, 3 slots)
- Glyph coverage management against GB2312 / Big5 / ASCII / Latin-1 plus the game's original
  character sets, with a missing-glyph report
- Safe install / restore: SHA256 manifest, never touches other mods, refuses to run while the
  game is running
- Self-contained single-file publish

### Build

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows:

```powershell
dotnet build -c Release
dotnet publish src/Witcher3FontTool -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Pushing a `v*` tag makes CI build and attach the exe to a GitHub Release.

### Limitations

- TrueType outlines only (TTF/TTC); OTF/CFF is rejected with a clear error
- BMP (U+0000–U+FFFF) characters only; variable font axes are not handled
- The first face of a TTC is used
- The game only uses a font resource when its UI language matches

### Disclaimer

Unofficial fan tool, not affiliated with or endorsed by CD PROJEKT RED.
The Witcher is a trademark of CD PROJEKT S.A. Use fonts you are licensed to use.

## License

[MIT](LICENSE)
