# Reviewer Test Instructions

The extension requires the open-source LightDl desktop application and its Native Messaging host. The extension does not contact an external server and no test account is required.

## Setup

1. Install or build LightDl Desktop and LightDl Browser Host from https://github.com/greepar/LightDl.Desktop.
2. Start LightDl Desktop.
3. Open Browser Integration in the left navigation.
4. Click Register / Repair Host.
5. Confirm that browser integration is enabled in Settings.
6. Restart the browser after registering the native host.

## Test the accepted path

1. Start a normal HTTP or HTTPS file download in the browser.
2. The browser task is temporarily paused and the LightDl confirmation window appears.
3. Accept the request.
4. Confirm that the browser task is cancelled and removed only after acceptance.
5. Confirm that the task appears in LightDl.

## Test the fallback path

1. Start another browser download.
2. Reject the request in LightDl, or exit LightDl before starting the download.
3. Confirm that the original browser download resumes and remains usable.

## Sensitive data behavior

Cookies are requested only for the active download URL and are sent only through Native Messaging to `com.lightdl.browser` on the same computer. They are marked sensitive in the request, are not logged by the extension, and are not stored by the extension.

## Source and build

The submitted package contains plain, unminified JavaScript with no build step, bundled dependencies, obfuscation, or remote code.
