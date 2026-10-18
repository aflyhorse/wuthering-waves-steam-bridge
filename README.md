# Wuthering Waves Steam Bridge

让《鸣潮》客户端通过 Steam 游戏库启动，无需下载国际服。

下载程序：进入本仓库的 **Releases**。Windows 下载使用 `WutheringWavesSteamBridge-<version>-win-x64.zip`，解压后以管理员身份运行 `WutheringWavesSteamBridge.exe`。

## 主要功能

检测当前 Steam 账号是否拥有鸣潮（AppID 3513350）。

选择清晰度分包（SD / HD / UHD，默认 HD）并指定鸣潮游戏可执行文件路径后，一键载入。之后从 Steam 游戏库开始游戏，桥接会以 `Client -krqlv=<清晰度> -SkipSplash` 参数直接启动你的游戏本体。

支持 Steam 游玩时长、Shift+Tab 叠加层和 F12 截图。

一键撤回载入；原本没下载国际服，恢复 Steam 的“安装”状态。

## 简单使用

先登录 Steam 并将鸣潮加入库，安装鸣潮国服客户端。退出游戏后，以管理员身份运行本工具，选择清晰度分包，选择游戏目录中的可执行文件（如 `Wuthering Waves Game\Client.exe`），点击“一键载入”，之后从 Steam 开始游戏即可。

需要恢复时，退出游戏，点击“撤回载入”。

适用于 Windows 10/11 x64，需要 .NET Framework 4.8。Steam 也需以管理员身份运行。

本项目作者：LunarShaddow（[Wuthering Waves Steam Bridge](https://github.com/aflyhorse/wuthering-waves-steam-bridge)），派生自 SoledadAki 的 [Endfield Steam Bridge](https://github.com/SoledadAki/endfield-steam-bridge)，代码采用 MIT 开源协议。
