# LightDl browser integration

The first integration layer uses the `lightdl://add?url=...` protocol. It is intentionally limited to explicit toolbar and context-menu actions and never places cookies or authorization headers in the URI.

## Development loading

- Chromium-derived browsers: load `chromium` as an unpacked extension.
- Firefox-derived browsers: load `firefox/manifest.json` from `about:debugging`.
- Safari: on macOS run `xcrun safari-web-extension-converter safari` and build the generated containing app in Xcode.

Install the desktop protocol handler before testing. Production authenticated downloads should use a native-messaging bridge so cookies and headers travel over local IPC instead of a URI.
