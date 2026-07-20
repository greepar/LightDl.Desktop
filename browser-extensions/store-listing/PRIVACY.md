# LightDl Automatic Integration Privacy Policy

Effective date: July 20, 2026

LightDl Automatic Integration connects browser downloads to the LightDl desktop application installed on the same computer. This policy describes the information handled by the extension and how it is used.

## Information handled

When the browser creates a download, the extension may process:

- The download URL and final redirected URL.
- The referring page URL, when provided by the browser.
- The suggested file name, MIME type, and expected file size.
- Cookies associated with the download URL. Cookies may contain authentication information needed to download files that require an authenticated browser session.
- The browser-generated download identifier.

## How the information is used

This information is used only to reproduce the selected browser download in the LightDl desktop application. The extension sends it through the browser's Native Messaging interface to the local native host named `com.lightdl.browser` on the same computer.

## Data sharing and retention

- The extension does not send this information to LightDl-operated servers or third-party services.
- The extension does not sell, rent, share, or use user data for advertising, analytics, profiling, or creditworthiness decisions.
- The extension does not persist cookies, browsing history, or download request headers.
- Temporary in-memory download state is discarded automatically after processing.
- The local LightDl desktop application uses the request information only to perform the download requested by the user.

## Permissions

- `downloads`: observes, pauses, resumes, cancels, and removes the browser download while it is being handed to LightDl.
- `cookies`: obtains cookies for the download URL so authenticated downloads can continue in LightDl.
- `nativeMessaging`: communicates with the locally installed LightDl Browser Host.
- `<all_urls>`: allows cookies to be obtained for download URLs from websites chosen by the user.

## Security

Browser-to-host communication uses the browser's local Native Messaging channel. Sensitive headers are not written to extension logs or extension storage. The extension contains no remote executable code.

## User control

Users can disable or remove the extension at any time. Users can also disable browser integration or unregister the browser host from the LightDl desktop application's browser integration page.

## Limited Use

The use of information received from browser APIs adheres to the Chrome Web Store User Data Policy, including the Limited Use requirements. Data is used only to provide the extension's single purpose and is not transferred for advertising, analytics, profiling, or unrelated purposes.

## Contact

Questions and privacy requests can be submitted through the public issue tracker:

https://github.com/greepar/LightDl.Desktop/issues
