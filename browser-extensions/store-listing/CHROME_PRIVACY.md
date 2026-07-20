# Chrome Web Store Privacy Answers

## Single purpose

Transfer browser-created downloads to the locally installed LightDl desktop download manager while safely falling back to the browser if the handoff is not accepted.

## Permission justifications

### downloads

Required to observe newly created downloads, temporarily pause them during local user confirmation, resume them if the handoff fails, and cancel and remove them only after LightDl accepts the task.

### cookies

Required to obtain cookies for the exact download URL so LightDl can complete downloads that require the user's existing authenticated browser session. Cookies are sent only to the LightDl Native Messaging host on the same computer and are not persisted by the extension.

### nativeMessaging

Required to exchange the download request and the user's acceptance result with the locally installed LightDl Browser Host named `com.lightdl.browser`.

### host permissions: all URLs

Downloads can originate from any website selected by the user. Access is used only to request cookies for the URL of a browser-created download and is not used to inject scripts, modify pages, or build browsing history.

## Remote code

No. The extension does not download or execute remote JavaScript, WebAssembly, or other executable code.

## Data handled in the Privacy practices tab

Select the categories corresponding to:

- Authentication information, because authentication cookies may be handled.
- Web history or browsing activity, because download and referrer URLs are handled.
- Website content, because cookies and request-related information are handled.
- User activity, because browser download actions are handled.

Do not select personally identifiable information, health information, financial information, personal communications, location, or analytics unless future code starts handling those categories.

## Data use certifications

The data is used only for the extension's single purpose, is not sold or transferred to third parties, is not used for advertising or creditworthiness, and is not used by humans except when the user explicitly supplies diagnostic information for support.
