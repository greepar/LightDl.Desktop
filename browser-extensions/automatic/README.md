# LightDl automatic browser integration

These extensions observe browser downloads and send one `capture-download` request to the Native Messaging host `com.lightdl.browser`. They do not use HTTP or the `lightdl://` protocol.

## Development loading

- Chrome or Edge: load `automatic/chromium` as an unpacked extension.
- Firefox 128 or newer: load `automatic/firefox/manifest.json` from `about:debugging`.

The Chromium manifest contains a fixed development key, so Chrome and Edge use extension ID `nafkhlacfpmamhmhdfbnfnkainjdaohi`. Firefox uses `automatic-integration@lightdl.io`. LightDl Desktop's browser-integration page registers native-host manifests for these IDs.

The extension temporarily pauses a newly created browser download while LightDl is waiting for user confirmation. It cancels and removes the browser task only when the response contains both `accepted: true` and `cancelBrowserDownload: true`. If the host is absent, communication fails, or the request is rejected, the extension resumes the browser download.

Cookie values are sent only inside the Native Messaging request, marked as sensitive, and are not logged or persisted by the extension.
