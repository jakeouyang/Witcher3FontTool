## v1.4.0-beta.1 测试版 / Test release

这是预发布测试版，新增功能仍需游戏内验证。

- 支持俄语与乌克兰语游戏字库的西里尔文字替换，包括 Ё、Є、І、Ї、Ґ。主字体必须覆盖所选语言的必需字母。
- 增加可选“英文与数字”字体：中文与西里尔字母优先使用主字体，拉丁字母、数字、半角标点及常见货币符号优先使用第二字体；缺字时尝试另一字体。
- 两种字体直接组合进同一个 MOD 的字体槽位，中文界面中的英文和数字也会使用第二字体。留空第二字体则保持单字体行为。
- 保留游戏原有行距及字体注册。路径、五种语言勾选和中英文界面均可记忆；测试版使用 `settings-beta.json`，首次启动读取现有正式版设置。

下载下方 `Witcher3FontTool.exe`。选择主字体，可选第二字体，勾选游戏语言，再生成并安装。该版本更新同一个 `modW3FontTool`；旧工具 MOD 会备份到游戏根目录 `.W3FontTool/backups`。工具内“还原字体”会停用工具 MOD。

请重点测试菜单、字幕、HUD 数字、金额和中英混排的大小与对齐；俄语和乌克兰语请测试 Ё/ё、Є/є、І/і、Ї/ї、Ґ/ґ。字体没有的字符仍会报告并省略，俄语原字库还包含普通字体未必提供的私用区符号，因此需检查相应符号的显示。任意两种字体的视觉大小与粗细不保证一致，不自动合成粗体/斜体。

---

This is a prerelease for in-game testing.

- Adds Russian and Ukrainian font libraries with Cyrillic support, including Ё, Є, І, Ї and Ґ. The fonts must cover the required alphabet of the selected language.
- Adds an optional Latin/digits font. CJK and Cyrillic prefer the main font; Latin letters, digits, halfwidth punctuation and common currency symbols prefer the second font. Missing characters try the other font.
- Both sources are composed into the same MOD/font slots, including Latin text and numbers inside Chinese UI. Leave the optional font empty for the existing single-font behavior.
- Preserves game line metrics and registrations. Remembers both font paths, five language selections and interface language. Beta settings use `settings-beta.json` and initially import stable preferences.

Download `Witcher3FontTool.exe` below. Select a main font, optionally select a Latin/digits font, choose your game languages, then build and install. This version updates the same `modW3FontTool`; the previous tool MOD is backed up under `.W3FontTool/backups` in the game root. Restore disables the tool MOD.

Please check menus, subtitles, HUD numbers, prices, baseline alignment and mixed text. For Russian/Ukrainian, check Ё/ё, Є/є, І/і, Ї/ї and Ґ/ґ. Characters absent from both fonts are reported and omitted. Russian game libraries also contain private-use symbols often absent from ordinary fonts; verify those symbols in-game. Different fonts may have different visual sizes/weights; bold and italic styles are not synthesized.
