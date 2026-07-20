# Store Listing Copy

## Product name

LightDl Automatic Integration

Chinese: LightDl 浏览器自动接管

## Short description

Send browser downloads to the local LightDl desktop app for faster, resumable downloading.

Chinese: 将浏览器下载安全地交给本机 LightDl，支持多线程下载、断点续传和任务管理。

## Detailed description

LightDl Automatic Integration connects Chrome, Edge, or Firefox downloads to the LightDl desktop download manager installed on the same computer.

When a download starts, the extension temporarily pauses the browser task and asks the local LightDl application whether to accept it. If the user accepts, LightDl performs the download and the original browser task is removed. If LightDl is unavailable or the user rejects the request, the browser download resumes normally.

Features:

- Automatic handoff to the local LightDl desktop application.
- Support for authenticated downloads by forwarding the required cookies locally.
- Safe fallback to the browser when LightDl is unavailable or the request is rejected.
- No cloud service, advertising, analytics, or remote executable code.
- Compatible with Windows, macOS, and Linux desktop installations of LightDl.

Requirements:

- Install the LightDl desktop application and LightDl Browser Host.
- Open LightDl, select Browser Integration, and choose Register / Repair Host.
- Keep browser integration enabled in LightDl settings.

All download information is transferred only through the browser's Native Messaging channel to the LightDl application on the same computer.

## Detailed description, Chinese

LightDl 浏览器自动接管扩展用于将 Chrome、Edge 或 Firefox 创建的下载任务交给同一台电脑上安装的 LightDl 下载管理器。

浏览器开始下载后，扩展会暂时暂停原任务，并询问本机 LightDl 是否接管。用户接受后，由 LightDl 执行下载并移除浏览器中的原任务；如果 LightDl 未运行、通信失败或用户拒绝，浏览器会自动恢复原下载，不会造成任务丢失。

主要功能：

- 自动将浏览器下载交给本机 LightDl。
- 在本机传递必要 Cookie，支持需要登录状态的下载。
- LightDl 不可用或用户拒绝时自动恢复浏览器下载。
- 不使用云端服务，不包含广告、统计分析或远程代码。
- 支持安装了 LightDl Desktop 的 Windows、macOS 和 Linux。

使用要求：

- 安装 LightDl Desktop 和 LightDl Browser Host。
- 在 LightDl 的“浏览器集成”页面点击“注册 / 修复宿主”。
- 在 LightDl 设置中保持浏览器自动接管开启。

下载信息仅通过浏览器 Native Messaging 通道发送到同一台电脑上的 LightDl，不会发送到开发者服务器。

## Category

Productivity

## Homepage

https://github.com/greepar/LightDl.Desktop

## Support URL

https://github.com/greepar/LightDl.Desktop/issues

## Privacy policy URL

https://github.com/greepar/LightDl.Desktop/blob/main/browser-extensions/store-listing/PRIVACY.md

## Suggested keywords

download manager, downloader, resume download, native messaging, LightDl, 下载管理器, 断点续传
